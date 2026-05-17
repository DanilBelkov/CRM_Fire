using Microsoft.Data.Sqlite;
using TheatreCRM.App.Models;

namespace TheatreCRM.App.Data;

public sealed class TheatreRepository
{
    private readonly string _connectionString;

    public TheatreRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS CatalogItems (
            Id TEXT PRIMARY KEY,
            Type INTEGER NOT NULL,
            Title TEXT NOT NULL,
            Description TEXT NULL,
            InventoryNumber TEXT NULL,
            StorageLocation TEXT NULL,
            Condition TEXT NULL,
            ResponsiblePerson TEXT NULL,
            Notes TEXT NULL,
            MainPhotoPath TEXT NULL,
            Size TEXT NULL,
            Color TEXT NULL,
            Material TEXT NULL,
            Measurements TEXT NULL,
            CareInstructions TEXT NULL,
            Dimensions TEXT NULL,
            Weight TEXT NULL,
            Fragility TEXT NULL,
            UsageNotes TEXT NULL,
            CharacterName TEXT NULL,
            ActorName TEXT NULL,
            SceneNotes TEXT NULL,
            Director TEXT NULL,
            PremiereDate TEXT NULL,
            Season TEXT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            DeletedAt TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS Tags (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL UNIQUE COLLATE NOCASE
        );

        CREATE TABLE IF NOT EXISTS TagGroups (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL UNIQUE COLLATE NOCASE,
            Description TEXT NULL,
            Color TEXT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS CatalogItemTags (
            CatalogItemId TEXT NOT NULL,
            TagId TEXT NOT NULL,
            PRIMARY KEY (CatalogItemId, TagId),
            FOREIGN KEY (CatalogItemId) REFERENCES CatalogItems(Id) ON DELETE CASCADE,
            FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS SavedViews (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            SectionType INTEGER NOT NULL,
            TagGroupId TEXT NULL,
            TagId TEXT NULL,
            GroupByTagGroup INTEGER NOT NULL DEFAULT 0,
            IsShownInSidebar INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS AuditLog (
            Id TEXT PRIMARY KEY,
            EntityId TEXT NULL,
            EntityTitle TEXT NULL,
            Operation TEXT NOT NULL,
            Details TEXT NULL,
            CreatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Photos (
            Id TEXT PRIMARY KEY,
            CatalogItemId TEXT NOT NULL,
            RelativePath TEXT NOT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL,
            FOREIGN KEY (CatalogItemId) REFERENCES CatalogItems(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS CostumeClothingItems (
            CostumeId TEXT NOT NULL,
            ClothingItemId TEXT NOT NULL,
            PRIMARY KEY (CostumeId, ClothingItemId),
            FOREIGN KEY (CostumeId) REFERENCES CatalogItems(Id) ON DELETE CASCADE,
            FOREIGN KEY (ClothingItemId) REFERENCES CatalogItems(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS CostumeProps (
            CostumeId TEXT NOT NULL,
            PropId TEXT NOT NULL,
            PRIMARY KEY (CostumeId, PropId),
            FOREIGN KEY (CostumeId) REFERENCES CatalogItems(Id) ON DELETE CASCADE,
            FOREIGN KEY (PropId) REFERENCES CatalogItems(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS PerformanceCostumes (
            PerformanceId TEXT NOT NULL,
            CostumeId TEXT NOT NULL,
            PRIMARY KEY (PerformanceId, CostumeId),
            FOREIGN KEY (PerformanceId) REFERENCES CatalogItems(Id) ON DELETE CASCADE,
            FOREIGN KEY (CostumeId) REFERENCES CatalogItems(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS PerformanceProps (
            PerformanceId TEXT NOT NULL,
            PropId TEXT NOT NULL,
            PRIMARY KEY (PerformanceId, PropId),
            FOREIGN KEY (PerformanceId) REFERENCES CatalogItems(Id) ON DELETE CASCADE,
            FOREIGN KEY (PropId) REFERENCES CatalogItems(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS PerformanceClothingItems (
            PerformanceId TEXT NOT NULL,
            ClothingItemId TEXT NOT NULL,
            PRIMARY KEY (PerformanceId, ClothingItemId),
            FOREIGN KEY (PerformanceId) REFERENCES CatalogItems(Id) ON DELETE CASCADE,
            FOREIGN KEY (ClothingItemId) REFERENCES CatalogItems(Id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS IX_CatalogItems_Type_DeletedAt ON CatalogItems(Type, DeletedAt);
        CREATE INDEX IF NOT EXISTS IX_CatalogItems_Title ON CatalogItems(Title);
        """;
        command.ExecuteNonQuery();
        EnsureTagSchema(connection);
        EnsureDefaultTagGroup(connection);
        EnsureSearchSchema(connection);
        RebuildSearchIndex(connection);
    }

    public List<CatalogItem> Search(CatalogItemType type, string searchText, string? tagGroupId = null, string? tagId = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var hasSearch = !string.IsNullOrWhiteSpace(searchText);
        var hasTag = !string.IsNullOrWhiteSpace(tagId);
        command.CommandText = hasSearch ? $"""
        SELECT ci.* FROM CatalogItems ci
        JOIN CatalogItemsFts fts ON fts.CatalogItemId = ci.Id
        WHERE ci.Type = $type AND ci.DeletedAt IS NULL
        AND CatalogItemsFts MATCH $search
        {(hasTag ? "AND EXISTS (SELECT 1 FROM CatalogItemTags cit WHERE cit.CatalogItemId = ci.Id AND cit.TagId = $tagId)" : "")}
        ORDER BY ci.UpdatedAt DESC, ci.Title COLLATE NOCASE
        """ : $"""
        SELECT * FROM CatalogItems
        WHERE Type = $type AND DeletedAt IS NULL
        {(hasTag ? "AND EXISTS (SELECT 1 FROM CatalogItemTags cit WHERE cit.CatalogItemId = CatalogItems.Id AND cit.TagId = $tagId)" : "")}
        ORDER BY UpdatedAt DESC, Title COLLATE NOCASE
        """;
        command.Parameters.AddWithValue("$type", (int)type);
        if (hasSearch)
        {
            command.Parameters.AddWithValue("$search", EscapeFtsQuery(searchText.Trim()));
        }
        if (hasTag)
        {
            command.Parameters.AddWithValue("$tagId", tagId);
        }

        using var reader = command.ExecuteReader();
        var items = new List<CatalogItem>();
        while (reader.Read())
        {
            var item = ReadCatalogItem(reader);
            FillTags(connection, item);
            item.GroupHeader = !string.IsNullOrWhiteSpace(tagGroupId) ? GetGroupHeader(connection, item.Id, tagGroupId!) : "Без группировки";
            FillRelatedItems(connection, item);
            items.Add(item);
        }

        return items;
    }

    public CatalogItem? GetById(string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM CatalogItems WHERE Id = $id AND DeletedAt IS NULL";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var item = ReadCatalogItem(reader);
        FillTags(connection, item);
        FillRelatedItems(connection, item);
        return item;
    }

    public void Save(CatalogItem item)
    {
        var now = DateTime.UtcNow;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var exists = Exists(connection, item.Id);
        item.UpdatedAt = now;
        if (!exists)
        {
            item.CreatedAt = now;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = exists ? UpdateSql : InsertSql;
        AddCatalogParameters(command, item);
        command.ExecuteNonQuery();

        SaveTags(connection, transaction, item);
        UpsertSearchIndex(connection, transaction, item);
        WriteAudit(connection, transaction, item.Id, item.Title, exists ? "Обновление карточки" : "Создание карточки", item.Type.ToRussian());
        transaction.Commit();
    }

    public void SoftDelete(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE CatalogItems SET DeletedAt = $deletedAt, UpdatedAt = $updatedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$deletedAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        using var deleteFts = connection.CreateCommand();
        deleteFts.Transaction = transaction;
        deleteFts.CommandText = "DELETE FROM CatalogItemsFts WHERE CatalogItemId = $id";
        deleteFts.Parameters.AddWithValue("$id", id);
        deleteFts.ExecuteNonQuery();
        WriteAudit(connection, transaction, id, GetTitle(connection, id), "Удаление в корзину", "");
        transaction.Commit();
    }

    public void RestoreFromTrash(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE CatalogItems SET DeletedAt = NULL, UpdatedAt = $updatedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        var item = GetByIdIncludingDeleted(connection, id);
        if (item is not null)
        {
            UpsertSearchIndex(connection, transaction, item);
            WriteAudit(connection, transaction, id, item.Title, "Восстановление из корзины", "");
        }
        transaction.Commit();
    }

    public void PermanentlyDelete(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var title = GetTitle(connection, id);
        using var deleteFts = connection.CreateCommand();
        deleteFts.Transaction = transaction;
        deleteFts.CommandText = "DELETE FROM CatalogItemsFts WHERE CatalogItemId = $id";
        deleteFts.Parameters.AddWithValue("$id", id);
        deleteFts.ExecuteNonQuery();

        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM CatalogItems WHERE Id = $id";
        delete.Parameters.AddWithValue("$id", id);
        delete.ExecuteNonQuery();
        WriteAudit(connection, transaction, id, title, "Окончательное удаление", "");
        transaction.Commit();
    }

    public List<CatalogItem> GetTrash()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM CatalogItems WHERE DeletedAt IS NOT NULL ORDER BY UpdatedAt DESC";
        using var reader = command.ExecuteReader();
        var items = new List<CatalogItem>();
        while (reader.Read())
        {
            var item = ReadCatalogItem(reader);
            FillTags(connection, item);
            FillRelatedItems(connection, item);
            items.Add(item);
        }

        return items;
    }

    public List<CatalogItem> GetAllItems(bool includeDeleted = false)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = includeDeleted
            ? "SELECT * FROM CatalogItems ORDER BY Type, Title COLLATE NOCASE"
            : "SELECT * FROM CatalogItems WHERE DeletedAt IS NULL ORDER BY Type, Title COLLATE NOCASE";
        using var reader = command.ExecuteReader();
        var items = new List<CatalogItem>();
        while (reader.Read())
        {
            var item = ReadCatalogItem(reader);
            FillTags(connection, item);
            FillRelatedItems(connection, item);
            items.Add(item);
        }

        return items;
    }

    public List<string> GetAuditLog(string? entityId = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT CreatedAt, Operation, COALESCE(EntityTitle, ''), COALESCE(Details, '')
        FROM AuditLog
        WHERE $entityId IS NULL OR EntityId = $entityId
        ORDER BY CreatedAt DESC
        LIMIT 200
        """;
        command.Parameters.AddWithValue("$entityId", (object?)entityId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            result.Add($"{DateTime.Parse(reader.GetString(0)):dd.MM.yyyy HH:mm} | {reader.GetString(1)} | {reader.GetString(2)} | {reader.GetString(3)}");
        }

        return result;
    }

    public void AddPhoto(string catalogItemId, string relativePath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO Photos (Id, CatalogItemId, RelativePath, SortOrder, CreatedAt)
        VALUES ($id, $catalogItemId, $relativePath, (SELECT COUNT(*) FROM Photos WHERE CatalogItemId = $catalogItemId), $createdAt)
        """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$catalogItemId", catalogItemId);
        command.Parameters.AddWithValue("$relativePath", relativePath);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public List<string> GetPhotos(string catalogItemId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RelativePath FROM Photos WHERE CatalogItemId = $id ORDER BY SortOrder";
        command.Parameters.AddWithValue("$id", catalogItemId);
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public List<CatalogSummary> GetCandidates(CatalogItemType type, string? excludeId = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT Id, Type, Title FROM CatalogItems
        WHERE Type = $type AND DeletedAt IS NULL AND ($excludeId IS NULL OR Id <> $excludeId)
        ORDER BY Title COLLATE NOCASE
        """;
        command.Parameters.AddWithValue("$type", (int)type);
        command.Parameters.AddWithValue("$excludeId", (object?)excludeId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var result = new List<CatalogSummary>();
        while (reader.Read())
        {
            result.Add(new CatalogSummary
            {
                Id = reader.GetString(0),
                Type = (CatalogItemType)reader.GetInt32(1),
                Title = reader.GetString(2)
            });
        }

        return result;
    }

    public HashSet<string> GetLinkedIds(string table, string ownerColumn, string linkedColumn, string ownerId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {linkedColumn} FROM {table} WHERE {ownerColumn} = $ownerId";
        command.Parameters.AddWithValue("$ownerId", ownerId);
        using var reader = command.ExecuteReader();
        var result = new HashSet<string>();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public void ReplaceLinks(string table, string ownerColumn, string linkedColumn, string ownerId, IEnumerable<string> linkedIds)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = $"DELETE FROM {table} WHERE {ownerColumn} = $ownerId";
        delete.Parameters.AddWithValue("$ownerId", ownerId);
        delete.ExecuteNonQuery();

        foreach (var linkedId in linkedIds.Distinct())
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT OR IGNORE INTO {table} ({ownerColumn}, {linkedColumn}) VALUES ($ownerId, $linkedId)";
            insert.Parameters.AddWithValue("$ownerId", ownerId);
            insert.Parameters.AddWithValue("$linkedId", linkedId);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public List<TagGroup> GetTagGroups()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, Color, SortOrder FROM TagGroups ORDER BY SortOrder, Name COLLATE NOCASE";
        using var reader = command.ExecuteReader();
        var result = new List<TagGroup>();
        while (reader.Read())
        {
            result.Add(new TagGroup
            {
                Id = reader.GetString(0),
                Name = ReadNullable(reader, 1),
                Description = ReadNullable(reader, 2),
                Color = ReadNullable(reader, 3),
                SortOrder = reader.GetInt32(4)
            });
        }

        return result;
    }

    public List<Tag> GetTags(string? groupId = null, bool includeArchived = false)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
        SELECT t.Id, t.GroupId, COALESCE(g.Name, ''), t.Name, COALESCE(t.Description, ''), COALESCE(t.Color, ''), t.IsArchived,
               (SELECT COUNT(*) FROM CatalogItemTags cit WHERE cit.TagId = t.Id) AS UsageCount
        FROM Tags t
        LEFT JOIN TagGroups g ON g.Id = t.GroupId
        WHERE ($groupId IS NULL OR t.GroupId = $groupId)
        {(includeArchived ? "" : "AND t.IsArchived = 0")}
        ORDER BY g.SortOrder, g.Name COLLATE NOCASE, t.Name COLLATE NOCASE
        """;
        command.Parameters.AddWithValue("$groupId", (object?)groupId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var result = new List<Tag>();
        while (reader.Read())
        {
            result.Add(new Tag
            {
                Id = reader.GetString(0),
                GroupId = ReadNullable(reader, 1),
                GroupName = ReadNullable(reader, 2),
                Name = ReadNullable(reader, 3),
                Description = ReadNullable(reader, 4),
                Color = ReadNullable(reader, 5),
                IsArchived = reader.GetInt32(6) == 1,
                UsageCount = reader.GetInt32(7)
            });
        }

        return result;
    }

    public void SaveTagGroup(TagGroup group)
    {
        using var connection = OpenConnection();
        var exists = Exists(connection, "TagGroups", group.Id);
        using var command = connection.CreateCommand();
        command.CommandText = exists
            ? "UPDATE TagGroups SET Name = $name, Description = $description, Color = $color, SortOrder = $sortOrder, UpdatedAt = $updatedAt WHERE Id = $id"
            : "INSERT INTO TagGroups (Id, Name, Description, Color, SortOrder, CreatedAt, UpdatedAt) VALUES ($id, $name, $description, $color, $sortOrder, $createdAt, $updatedAt)";
        command.Parameters.AddWithValue("$id", group.Id);
        command.Parameters.AddWithValue("$name", group.Name.Trim());
        command.Parameters.AddWithValue("$description", group.Description);
        command.Parameters.AddWithValue("$color", string.IsNullOrWhiteSpace(group.Color) ? "#21A66B" : group.Color);
        command.Parameters.AddWithValue("$sortOrder", group.SortOrder);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void SaveTag(Tag tag)
    {
        using var connection = OpenConnection();
        var exists = Exists(connection, "Tags", tag.Id);
        using var command = connection.CreateCommand();
        command.CommandText = exists
            ? "UPDATE Tags SET GroupId = $groupId, Name = $name, Description = $description, Color = $color, IsArchived = $isArchived WHERE Id = $id"
            : "INSERT INTO Tags (Id, GroupId, Name, Description, Color, IsArchived) VALUES ($id, $groupId, $name, $description, $color, $isArchived)";
        command.Parameters.AddWithValue("$id", tag.Id);
        command.Parameters.AddWithValue("$groupId", tag.GroupId);
        command.Parameters.AddWithValue("$name", tag.Name.Trim());
        command.Parameters.AddWithValue("$description", tag.Description);
        command.Parameters.AddWithValue("$color", string.IsNullOrWhiteSpace(tag.Color) ? "#21A66B" : tag.Color);
        command.Parameters.AddWithValue("$isArchived", tag.IsArchived ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public int GetTagUsageCount(string tagId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM CatalogItemTags WHERE TagId = $tagId";
        command.Parameters.AddWithValue("$tagId", tagId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void ArchiveTag(string tagId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Tags SET IsArchived = 1 WHERE Id = $tagId";
        command.Parameters.AddWithValue("$tagId", tagId);
        command.ExecuteNonQuery();
    }

    public void RemoveTagFromItems(string tagId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CatalogItemTags WHERE TagId = $tagId";
        command.Parameters.AddWithValue("$tagId", tagId);
        command.ExecuteNonQuery();
    }

    public void ReplaceTag(string sourceTagId, string targetTagId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
        INSERT OR IGNORE INTO CatalogItemTags (CatalogItemId, TagId)
        SELECT CatalogItemId, $targetTagId FROM CatalogItemTags WHERE TagId = $sourceTagId
        """;
        insert.Parameters.AddWithValue("$sourceTagId", sourceTagId);
        insert.Parameters.AddWithValue("$targetTagId", targetTagId);
        insert.ExecuteNonQuery();

        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM CatalogItemTags WHERE TagId = $sourceTagId";
        delete.Parameters.AddWithValue("$sourceTagId", sourceTagId);
        delete.ExecuteNonQuery();
        transaction.Commit();
    }

    public List<SavedView> GetSavedViews(bool sidebarOnly = false)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
        SELECT Id, Name, SectionType, TagGroupId, TagId, GroupByTagGroup, IsShownInSidebar
        FROM SavedViews
        {(sidebarOnly ? "WHERE IsShownInSidebar = 1" : "")}
        ORDER BY Name COLLATE NOCASE
        """;
        using var reader = command.ExecuteReader();
        var result = new List<SavedView>();
        while (reader.Read())
        {
            result.Add(new SavedView
            {
                Id = reader.GetString(0),
                Name = ReadNullable(reader, 1),
                SectionType = (CatalogItemType)reader.GetInt32(2),
                TagGroupId = reader.IsDBNull(3) ? null : reader.GetString(3),
                TagId = reader.IsDBNull(4) ? null : reader.GetString(4),
                GroupByTagGroup = reader.GetInt32(5) == 1,
                IsShownInSidebar = reader.GetInt32(6) == 1
            });
        }

        return result;
    }

    public void SaveView(SavedView view)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO SavedViews (Id, Name, SectionType, TagGroupId, TagId, GroupByTagGroup, IsShownInSidebar)
        VALUES ($id, $name, $sectionType, $tagGroupId, $tagId, $groupByTagGroup, $isShownInSidebar)
        ON CONFLICT(Id) DO UPDATE SET
            Name = excluded.Name,
            SectionType = excluded.SectionType,
            TagGroupId = excluded.TagGroupId,
            TagId = excluded.TagId,
            GroupByTagGroup = excluded.GroupByTagGroup,
            IsShownInSidebar = excluded.IsShownInSidebar
        """;
        command.Parameters.AddWithValue("$id", view.Id);
        command.Parameters.AddWithValue("$name", view.Name.Trim());
        command.Parameters.AddWithValue("$sectionType", (int)view.SectionType);
        command.Parameters.AddWithValue("$tagGroupId", (object?)view.TagGroupId ?? DBNull.Value);
        command.Parameters.AddWithValue("$tagId", (object?)view.TagId ?? DBNull.Value);
        command.Parameters.AddWithValue("$groupByTagGroup", view.GroupByTagGroup ? 1 : 0);
        command.Parameters.AddWithValue("$isShownInSidebar", view.IsShownInSidebar ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static bool Exists(SqliteConnection connection, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM CatalogItems WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool Exists(SqliteConnection connection, string table, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void EnsureTagSchema(SqliteConnection connection)
    {
        AddColumnIfMissing(connection, "Tags", "GroupId", "TEXT NULL");
        AddColumnIfMissing(connection, "Tags", "Description", "TEXT NULL");
        AddColumnIfMissing(connection, "Tags", "Color", "TEXT NULL");
        AddColumnIfMissing(connection, "Tags", "IsArchived", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    private static void EnsureDefaultTagGroup(SqliteConnection connection)
    {
        var now = DateTime.UtcNow.ToString("O");
        const string defaultId = "default";
        using var insert = connection.CreateCommand();
        insert.CommandText = """
        INSERT OR IGNORE INTO TagGroups (Id, Name, Description, Color, SortOrder, CreatedAt, UpdatedAt)
        VALUES ($id, 'Общие теги', 'Группа для тегов, созданных прямо из карточек.', '#21A66B', 0, $now, $now)
        """;
        insert.Parameters.AddWithValue("$id", defaultId);
        insert.Parameters.AddWithValue("$now", now);
        insert.ExecuteNonQuery();

        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE Tags SET GroupId = $id WHERE GroupId IS NULL OR GroupId = ''";
        update.Parameters.AddWithValue("$id", defaultId);
        update.ExecuteNonQuery();
    }

    private static void EnsureSearchSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
        CREATE VIRTUAL TABLE IF NOT EXISTS CatalogItemsFts USING fts5(
            CatalogItemId UNINDEXED,
            Title,
            Description,
            InventoryNumber,
            Notes
        );
        """;
        command.ExecuteNonQuery();
    }

    private static void RebuildSearchIndex(SqliteConnection connection)
    {
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM CatalogItemsFts";
        if (Convert.ToInt32(count.ExecuteScalar()) > 0)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO CatalogItemsFts (CatalogItemId, Title, Description, InventoryNumber, Notes)
        SELECT Id, Title, COALESCE(Description, ''), COALESCE(InventoryNumber, ''), COALESCE(Notes, '')
        FROM CatalogItems
        WHERE DeletedAt IS NULL
        """;
        command.ExecuteNonQuery();
    }

    private static void UpsertSearchIndex(SqliteConnection connection, SqliteTransaction transaction, CatalogItem item)
    {
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM CatalogItemsFts WHERE CatalogItemId = $id";
        delete.Parameters.AddWithValue("$id", item.Id);
        delete.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
        INSERT INTO CatalogItemsFts (CatalogItemId, Title, Description, InventoryNumber, Notes)
        VALUES ($id, $title, $description, $inventoryNumber, $notes)
        """;
        insert.Parameters.AddWithValue("$id", item.Id);
        insert.Parameters.AddWithValue("$title", item.Title);
        insert.Parameters.AddWithValue("$description", item.Description);
        insert.Parameters.AddWithValue("$inventoryNumber", item.InventoryNumber);
        insert.Parameters.AddWithValue("$notes", item.Notes);
        insert.ExecuteNonQuery();
    }

    private static void WriteAudit(SqliteConnection connection, SqliteTransaction transaction, string? entityId, string? entityTitle, string operation, string details)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
        INSERT INTO AuditLog (Id, EntityId, EntityTitle, Operation, Details, CreatedAt)
        VALUES ($id, $entityId, $entityTitle, $operation, $details, $createdAt)
        """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$entityId", (object?)entityId ?? DBNull.Value);
        command.Parameters.AddWithValue("$entityTitle", (object?)entityTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$operation", operation);
        command.Parameters.AddWithValue("$details", details);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static string GetTitle(SqliteConnection connection, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(Title, '') FROM CatalogItems WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() as string ?? "";
    }

    private static CatalogItem? GetByIdIncludingDeleted(SqliteConnection connection, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM CatalogItems WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCatalogItem(reader) : null;
    }

    private static string EscapeFtsQuery(string query)
    {
        var parts = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => "\"" + part.Replace("\"", "\"\"") + "\"");
        return string.Join(" ", parts);
    }

    private static void SaveTags(SqliteConnection connection, SqliteTransaction transaction, CatalogItem item)
    {
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM CatalogItemTags WHERE CatalogItemId = $id";
        delete.Parameters.AddWithValue("$id", item.Id);
        delete.ExecuteNonQuery();

        foreach (var tagName in item.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var tagId = GetOrCreateTag(connection, transaction, tagName);
            using var link = connection.CreateCommand();
            link.Transaction = transaction;
            link.CommandText = "INSERT OR IGNORE INTO CatalogItemTags (CatalogItemId, TagId) VALUES ($itemId, $tagId)";
            link.Parameters.AddWithValue("$itemId", item.Id);
            link.Parameters.AddWithValue("$tagId", tagId);
            link.ExecuteNonQuery();
        }
    }

    private static string GetOrCreateTag(SqliteConnection connection, SqliteTransaction transaction, string name)
    {
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT Id FROM Tags WHERE Name = $name COLLATE NOCASE";
        select.Parameters.AddWithValue("$name", name);
        var existing = select.ExecuteScalar() as string;
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var id = Guid.NewGuid().ToString("N");
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO Tags (Id, GroupId, Name, Description, Color, IsArchived) VALUES ($id, 'default', $name, '', '#21A66B', 0)";
        insert.Parameters.AddWithValue("$id", id);
        insert.Parameters.AddWithValue("$name", name);
        insert.ExecuteNonQuery();
        return id;
    }

    private static void FillTags(SqliteConnection connection, CatalogItem item)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT t.Name
        FROM Tags t
        JOIN CatalogItemTags cit ON cit.TagId = t.Id
        WHERE cit.CatalogItemId = $id
        ORDER BY t.Name COLLATE NOCASE
        """;
        command.Parameters.AddWithValue("$id", item.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            item.Tags.Add(reader.GetString(0));
        }
    }

    private static string GetGroupHeader(SqliteConnection connection, string itemId, string tagGroupId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT t.Name
        FROM Tags t
        JOIN CatalogItemTags cit ON cit.TagId = t.Id
        WHERE cit.CatalogItemId = $itemId AND t.GroupId = $tagGroupId
        ORDER BY t.Name COLLATE NOCASE
        """;
        command.Parameters.AddWithValue("$itemId", itemId);
        command.Parameters.AddWithValue("$tagGroupId", tagGroupId);
        using var reader = command.ExecuteReader();
        var tags = new List<string>();
        while (reader.Read())
        {
            tags.Add(reader.GetString(0));
        }

        return tags.Count == 0 ? "Без тега" : string.Join(", ", tags);
    }

    private static void FillRelatedItems(SqliteConnection connection, CatalogItem item)
    {
        var sql = item.Type switch
        {
            CatalogItemType.Clothing => """
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN CostumeClothingItems cci ON cci.CostumeId = ci.Id
                WHERE cci.ClothingItemId = $id AND ci.DeletedAt IS NULL
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN PerformanceClothingItems pci ON pci.PerformanceId = ci.Id
                WHERE pci.ClothingItemId = $id AND ci.DeletedAt IS NULL
                """,
            CatalogItemType.Prop => """
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN CostumeProps cp ON cp.CostumeId = ci.Id
                WHERE cp.PropId = $id AND ci.DeletedAt IS NULL
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN PerformanceProps pp ON pp.PerformanceId = ci.Id
                WHERE pp.PropId = $id AND ci.DeletedAt IS NULL
                """,
            CatalogItemType.Costume => """
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN CostumeClothingItems cci ON cci.ClothingItemId = ci.Id
                WHERE cci.CostumeId = $id AND ci.DeletedAt IS NULL
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN CostumeProps cp ON cp.PropId = ci.Id
                WHERE cp.CostumeId = $id AND ci.DeletedAt IS NULL
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN PerformanceCostumes pc ON pc.PerformanceId = ci.Id
                WHERE pc.CostumeId = $id AND ci.DeletedAt IS NULL
                """,
            CatalogItemType.Performance => """
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN PerformanceCostumes pc ON pc.CostumeId = ci.Id
                WHERE pc.PerformanceId = $id AND ci.DeletedAt IS NULL
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN PerformanceProps pp ON pp.PropId = ci.Id
                WHERE pp.PerformanceId = $id AND ci.DeletedAt IS NULL
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN PerformanceClothingItems pci ON pci.ClothingItemId = ci.Id
                WHERE pci.PerformanceId = $id AND ci.DeletedAt IS NULL
                """,
            _ => ""
        };

        using var command = connection.CreateCommand();
        command.CommandText = $"{sql} ORDER BY Title COLLATE NOCASE";
        command.Parameters.AddWithValue("$id", item.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            item.RelatedItems.Add(new CatalogSummary
            {
                Id = reader.GetString(0),
                Type = (CatalogItemType)reader.GetInt32(1),
                Title = reader.GetString(2)
            });
        }
    }

    private static CatalogItem ReadCatalogItem(SqliteDataReader reader) => new()
    {
        Id = ReadString(reader, "Id"),
        Type = (CatalogItemType)reader.GetInt32(reader.GetOrdinal("Type")),
        Title = ReadString(reader, "Title"),
        Description = ReadString(reader, "Description"),
        InventoryNumber = ReadString(reader, "InventoryNumber"),
        StorageLocation = ReadString(reader, "StorageLocation"),
        Condition = ReadString(reader, "Condition"),
        ResponsiblePerson = ReadString(reader, "ResponsiblePerson"),
        Notes = ReadString(reader, "Notes"),
        MainPhotoPath = ReadString(reader, "MainPhotoPath"),
        Size = ReadString(reader, "Size"),
        Color = ReadString(reader, "Color"),
        Material = ReadString(reader, "Material"),
        Measurements = ReadString(reader, "Measurements"),
        CareInstructions = ReadString(reader, "CareInstructions"),
        Dimensions = ReadString(reader, "Dimensions"),
        Weight = ReadString(reader, "Weight"),
        Fragility = ReadString(reader, "Fragility"),
        UsageNotes = ReadString(reader, "UsageNotes"),
        CharacterName = ReadString(reader, "CharacterName"),
        ActorName = ReadString(reader, "ActorName"),
        SceneNotes = ReadString(reader, "SceneNotes"),
        Director = ReadString(reader, "Director"),
        PremiereDate = ReadString(reader, "PremiereDate"),
        Season = ReadString(reader, "Season"),
        CreatedAt = DateTime.Parse(ReadString(reader, "CreatedAt")),
        UpdatedAt = DateTime.Parse(ReadString(reader, "UpdatedAt")),
        DeletedAt = ReadString(reader, "DeletedAt")
    };

    private static string ReadString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
    }

    private static string ReadNullable(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);

    private static void AddCatalogParameters(SqliteCommand command, CatalogItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$type", (int)item.Type);
        command.Parameters.AddWithValue("$title", item.Title.Trim());
        command.Parameters.AddWithValue("$description", item.Description);
        command.Parameters.AddWithValue("$inventoryNumber", item.InventoryNumber);
        command.Parameters.AddWithValue("$storageLocation", item.StorageLocation);
        command.Parameters.AddWithValue("$condition", item.Condition);
        command.Parameters.AddWithValue("$responsiblePerson", item.ResponsiblePerson);
        command.Parameters.AddWithValue("$notes", item.Notes);
        command.Parameters.AddWithValue("$mainPhotoPath", item.MainPhotoPath);
        command.Parameters.AddWithValue("$size", item.Size);
        command.Parameters.AddWithValue("$color", item.Color);
        command.Parameters.AddWithValue("$material", item.Material);
        command.Parameters.AddWithValue("$measurements", item.Measurements);
        command.Parameters.AddWithValue("$careInstructions", item.CareInstructions);
        command.Parameters.AddWithValue("$dimensions", item.Dimensions);
        command.Parameters.AddWithValue("$weight", item.Weight);
        command.Parameters.AddWithValue("$fragility", item.Fragility);
        command.Parameters.AddWithValue("$usageNotes", item.UsageNotes);
        command.Parameters.AddWithValue("$characterName", item.CharacterName);
        command.Parameters.AddWithValue("$actorName", item.ActorName);
        command.Parameters.AddWithValue("$sceneNotes", item.SceneNotes);
        command.Parameters.AddWithValue("$director", item.Director);
        command.Parameters.AddWithValue("$premiereDate", item.PremiereDate);
        command.Parameters.AddWithValue("$season", item.Season);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToString("O"));
    }

    private const string InsertSql = """
        INSERT INTO CatalogItems (
            Id, Type, Title, Description, InventoryNumber, StorageLocation, Condition, ResponsiblePerson, Notes, MainPhotoPath,
            Size, Color, Material, Measurements, CareInstructions, Dimensions, Weight, Fragility, UsageNotes,
            CharacterName, ActorName, SceneNotes, Director, PremiereDate, Season, CreatedAt, UpdatedAt
        ) VALUES (
            $id, $type, $title, $description, $inventoryNumber, $storageLocation, $condition, $responsiblePerson, $notes, $mainPhotoPath,
            $size, $color, $material, $measurements, $careInstructions, $dimensions, $weight, $fragility, $usageNotes,
            $characterName, $actorName, $sceneNotes, $director, $premiereDate, $season, $createdAt, $updatedAt
        )
        """;

    private const string UpdateSql = """
        UPDATE CatalogItems SET
            Title = $title,
            Description = $description,
            InventoryNumber = $inventoryNumber,
            StorageLocation = $storageLocation,
            Condition = $condition,
            ResponsiblePerson = $responsiblePerson,
            Notes = $notes,
            MainPhotoPath = $mainPhotoPath,
            Size = $size,
            Color = $color,
            Material = $material,
            Measurements = $measurements,
            CareInstructions = $careInstructions,
            Dimensions = $dimensions,
            Weight = $weight,
            Fragility = $fragility,
            UsageNotes = $usageNotes,
            CharacterName = $characterName,
            ActorName = $actorName,
            SceneNotes = $sceneNotes,
            Director = $director,
            PremiereDate = $premiereDate,
            Season = $season,
            UpdatedAt = $updatedAt
        WHERE Id = $id
        """;
}
