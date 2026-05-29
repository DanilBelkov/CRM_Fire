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
            CareInstructions TEXT NULL,
            Dimensions TEXT NULL,
            Weight TEXT NULL,
            Fragility TEXT NULL,
            Movement TEXT NULL,
            CharacterName TEXT NULL,
            ActorName TEXT NULL,
            Director TEXT NULL,
            PremiereDate TEXT NULL,
            Season TEXT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            DeletedAt TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS SavedViews (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            SectionType INTEGER NOT NULL,
            SearchText TEXT NULL,
            IsShownInSidebar INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS Users (
            Id TEXT PRIMARY KEY,
            FullName TEXT NOT NULL UNIQUE COLLATE NOCASE,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
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

        CREATE TABLE IF NOT EXISTS SectorCostumes (
            SectorId TEXT NOT NULL,
            CostumeId TEXT NOT NULL,
            PRIMARY KEY (SectorId, CostumeId),
            FOREIGN KEY (SectorId) REFERENCES CatalogItems(Id) ON DELETE CASCADE,
            FOREIGN KEY (CostumeId) REFERENCES CatalogItems(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS SectorProps (
            SectorId TEXT NOT NULL,
            PropId TEXT NOT NULL,
            PRIMARY KEY (SectorId, PropId),
            FOREIGN KEY (SectorId) REFERENCES CatalogItems(Id) ON DELETE CASCADE,
            FOREIGN KEY (PropId) REFERENCES CatalogItems(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS SectorClothingItems (
            SectorId TEXT NOT NULL,
            ClothingItemId TEXT NOT NULL,
            PRIMARY KEY (SectorId, ClothingItemId),
            FOREIGN KEY (SectorId) REFERENCES CatalogItems(Id) ON DELETE CASCADE,
            FOREIGN KEY (ClothingItemId) REFERENCES CatalogItems(Id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS IX_CatalogItems_Type_DeletedAt ON CatalogItems(Type, DeletedAt);
        CREATE INDEX IF NOT EXISTS IX_CatalogItems_Title ON CatalogItems(Title);
        CREATE INDEX IF NOT EXISTS IX_Users_FullName ON Users(FullName);
        """;
        command.ExecuteNonQuery();

        EnsureSavedViewsSchema(connection);
    }

    public List<CatalogItem> Search(CatalogItemType type, string searchText)
    {
        using var connection = OpenConnection();
        var items = LoadCatalogItems(connection, includeDeleted: false, type);
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return items;
        }

        var filter = searchText.Trim();
        return items
            .Where(item => BuildSearchDocument(item).Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
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
        reader.Close();
        FillRelatedItems(connection, item);
        return item;
    }

    public void Save(CatalogItem item)
    {
        var now = DateTime.UtcNow;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var exists = Exists(connection, "CatalogItems", item.Id);
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

        SaveUserIfNeeded(connection, transaction, item.ResponsiblePerson);
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
            WriteAudit(connection, transaction, id, item.Title, "Восстановление из корзины", "");
        }
        transaction.Commit();
    }

    public void PermanentlyDelete(string id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var title = GetTitle(connection, id);
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
        return LoadCatalogItems(connection, includeDeleted: true)
            .Where(item => !string.IsNullOrWhiteSpace(item.DeletedAt))
            .ToList();
    }

    public List<CatalogItem> GetAllItems(bool includeDeleted = false)
    {
        using var connection = OpenConnection();
        return LoadCatalogItems(connection, includeDeleted);
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

    public void DeletePhoto(string catalogItemId, string relativePath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Photos WHERE CatalogItemId = $catalogItemId AND RelativePath = $relativePath";
        command.Parameters.AddWithValue("$catalogItemId", catalogItemId);
        command.Parameters.AddWithValue("$relativePath", relativePath);
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

    public HashSet<string> GetOwnerIds(string table, string ownerColumn, string linkedColumn, string linkedId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {ownerColumn} FROM {table} WHERE {linkedColumn} = $linkedId";
        command.Parameters.AddWithValue("$linkedId", linkedId);
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

    public void ReplaceInverseLinks(string table, string ownerColumn, string linkedColumn, string linkedId, IEnumerable<string> ownerIds)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = $"DELETE FROM {table} WHERE {linkedColumn} = $linkedId";
        delete.Parameters.AddWithValue("$linkedId", linkedId);
        delete.ExecuteNonQuery();

        foreach (var ownerId in ownerIds.Distinct())
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

    public List<SavedView> GetSavedViews(bool sidebarOnly = false)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT Id, Name, SectionType, COALESCE(SearchText, ''), IsShownInSidebar
        FROM SavedViews
        WHERE $sidebarOnly = 0 OR IsShownInSidebar = 1
        ORDER BY Name COLLATE NOCASE
        """;
        command.Parameters.AddWithValue("$sidebarOnly", sidebarOnly ? 1 : 0);
        using var reader = command.ExecuteReader();
        var result = new List<SavedView>();
        while (reader.Read())
        {
            result.Add(new SavedView
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                SectionType = (CatalogItemType)reader.GetInt32(2),
                SearchText = reader.GetString(3),
                IsShownInSidebar = reader.GetInt32(4) == 1
            });
        }

        return result;
    }

    public void SaveView(SavedView view)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO SavedViews (Id, Name, SectionType, SearchText, IsShownInSidebar)
        VALUES ($id, $name, $sectionType, $searchText, $isShownInSidebar)
        ON CONFLICT(Id) DO UPDATE SET
            Name = excluded.Name,
            SectionType = excluded.SectionType,
            SearchText = excluded.SearchText,
            IsShownInSidebar = excluded.IsShownInSidebar
        """;
        command.Parameters.AddWithValue("$id", view.Id);
        command.Parameters.AddWithValue("$name", view.Name);
        command.Parameters.AddWithValue("$sectionType", (int)view.SectionType);
        command.Parameters.AddWithValue("$searchText", view.SearchText);
        command.Parameters.AddWithValue("$isShownInSidebar", view.IsShownInSidebar ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public List<DirectoryUser> GetUsers()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, FullName FROM Users ORDER BY FullName COLLATE NOCASE";
        using var reader = command.ExecuteReader();
        var result = new List<DirectoryUser>();
        while (reader.Read())
        {
            result.Add(new DirectoryUser
            {
                Id = reader.GetString(0),
                FullName = reader.GetString(1)
            });
        }

        return result;
    }

    public CatalogItem FindOrCreateSector(string sectorName)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT * FROM CatalogItems
        WHERE Type = $type AND DeletedAt IS NULL AND Title = $title COLLATE NOCASE
        LIMIT 1
        """;
        command.Parameters.AddWithValue("$type", (int)CatalogItemType.Sector);
        command.Parameters.AddWithValue("$title", sectorName.Trim());
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            var existing = ReadCatalogItem(reader);
            reader.Close();
            FillRelatedItems(connection, existing);
            return existing;
        }

        var sector = new CatalogItem
        {
            Type = CatalogItemType.Sector,
            Title = sectorName.Trim(),
            Condition = "Хорошее"
        };
        Save(sector);
        return GetById(sector.Id)!;
    }

    private List<CatalogItem> LoadCatalogItems(SqliteConnection connection, bool includeDeleted, CatalogItemType? type = null)
    {
        using var command = connection.CreateCommand();
        var whereParts = new List<string>();
        if (!includeDeleted)
        {
            whereParts.Add("DeletedAt IS NULL");
        }
        if (type is not null)
        {
            whereParts.Add("Type = $type");
            command.Parameters.AddWithValue("$type", (int)type.Value);
        }

        command.CommandText = $"""
        SELECT * FROM CatalogItems
        {(whereParts.Count == 0 ? "" : $"WHERE {string.Join(" AND ", whereParts)}")}
        ORDER BY UpdatedAt DESC, Title COLLATE NOCASE
        """;

        using var reader = command.ExecuteReader();
        var items = new List<CatalogItem>();
        while (reader.Read())
        {
            items.Add(ReadCatalogItem(reader));
        }

        reader.Close();
        foreach (var item in items)
        {
            FillRelatedItems(connection, item);
        }

        return items;
    }

    private static string BuildSearchDocument(CatalogItem item)
    {
        var values = new List<string>
        {
            item.Title,
            item.Description,
            item.InventoryNumber,
            item.StorageLocation,
            item.Condition,
            item.ResponsiblePerson,
            item.Notes,
            item.Size,
            item.Color,
            item.Material,
            item.CareInstructions,
            item.Dimensions,
            item.Weight,
            item.Fragility,
            item.CharacterName,
            item.ActorName,
            item.Director,
            item.PremiereDate,
            item.Season,
            item.Movement
        };

        values.AddRange(item.RelatedItems.Select(related => related.Title));
        values.AddRange(item.SectorNames);
        return string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static bool Exists(SqliteConnection connection, string table, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void EnsureSavedViewsSchema(SqliteConnection connection)
    {
        AddColumnIfMissing(connection, "SavedViews", "SearchText", "TEXT NULL");
        AddColumnIfMissing(connection, "SavedViews", "IsShownInSidebar", "INTEGER NOT NULL DEFAULT 1");
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

    private static void SaveUserIfNeeded(SqliteConnection connection, SqliteTransaction transaction, string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return;
        }

        var trimmed = fullName.Trim();
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT Id FROM Users WHERE FullName = $fullName COLLATE NOCASE";
        select.Parameters.AddWithValue("$fullName", trimmed);
        var existing = select.ExecuteScalar() as string;
        var now = DateTime.UtcNow.ToString("O");

        if (!string.IsNullOrWhiteSpace(existing))
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Users SET UpdatedAt = $updatedAt WHERE Id = $id";
            update.Parameters.AddWithValue("$updatedAt", now);
            update.Parameters.AddWithValue("$id", existing);
            update.ExecuteNonQuery();
            return;
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
        INSERT INTO Users (Id, FullName, CreatedAt, UpdatedAt)
        VALUES ($id, $fullName, $createdAt, $updatedAt)
        """;
        insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        insert.Parameters.AddWithValue("$fullName", trimmed);
        insert.Parameters.AddWithValue("$createdAt", now);
        insert.Parameters.AddWithValue("$updatedAt", now);
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

    private static void FillRelatedItems(SqliteConnection connection, CatalogItem item)
    {
        item.RelatedItems.Clear();
        item.SectorNames.Clear();

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
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN SectorClothingItems sci ON sci.SectorId = ci.Id
                WHERE sci.ClothingItemId = $id AND ci.DeletedAt IS NULL
                """,
            CatalogItemType.Prop => """
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN CostumeProps cp ON cp.CostumeId = ci.Id
                WHERE cp.PropId = $id AND ci.DeletedAt IS NULL
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN PerformanceProps pp ON pp.PerformanceId = ci.Id
                WHERE pp.PropId = $id AND ci.DeletedAt IS NULL
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN SectorProps sp ON sp.SectorId = ci.Id
                WHERE sp.PropId = $id AND ci.DeletedAt IS NULL
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
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN SectorCostumes sc ON sc.SectorId = ci.Id
                WHERE sc.CostumeId = $id AND ci.DeletedAt IS NULL
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
            CatalogItemType.Sector => """
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN SectorCostumes sc ON sc.CostumeId = ci.Id
                WHERE sc.SectorId = $id AND ci.DeletedAt IS NULL
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN SectorProps sp ON sp.PropId = ci.Id
                WHERE sp.SectorId = $id AND ci.DeletedAt IS NULL
                UNION
                SELECT ci.Id, ci.Type, ci.Title FROM CatalogItems ci
                JOIN SectorClothingItems sci ON sci.ClothingItemId = ci.Id
                WHERE sci.SectorId = $id AND ci.DeletedAt IS NULL
                """,
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"{sql} ORDER BY Title COLLATE NOCASE";
        command.Parameters.AddWithValue("$id", item.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var related = new CatalogSummary
            {
                Id = reader.GetString(0),
                Type = (CatalogItemType)reader.GetInt32(1),
                Title = reader.GetString(2)
            };

            item.RelatedItems.Add(related);
            if (related.Type == CatalogItemType.Sector)
            {
                item.SectorNames.Add(related.Title);
            }
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
        CareInstructions = ReadString(reader, "CareInstructions"),
        Dimensions = ReadString(reader, "Dimensions"),
        Weight = ReadString(reader, "Weight"),
        Fragility = ReadString(reader, "Fragility"),
        Movement = ReadString(reader, "Movement"),
        CharacterName = ReadString(reader, "CharacterName"),
        ActorName = ReadString(reader, "ActorName"),
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
        command.Parameters.AddWithValue("$careInstructions", item.CareInstructions);
        command.Parameters.AddWithValue("$dimensions", item.Dimensions);
        command.Parameters.AddWithValue("$weight", item.Weight);
        command.Parameters.AddWithValue("$fragility", item.Fragility);
        command.Parameters.AddWithValue("$movement", item.Movement);
        command.Parameters.AddWithValue("$characterName", item.CharacterName);
        command.Parameters.AddWithValue("$actorName", item.ActorName);
        command.Parameters.AddWithValue("$director", item.Director);
        command.Parameters.AddWithValue("$premiereDate", item.PremiereDate);
        command.Parameters.AddWithValue("$season", item.Season);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToString("O"));
    }

    private const string InsertSql = """
        INSERT INTO CatalogItems (
            Id, Type, Title, Description, InventoryNumber, StorageLocation, Condition, ResponsiblePerson, Notes, MainPhotoPath,
            Size, Color, Material, CareInstructions, Dimensions, Weight, Fragility, Movement,
            CharacterName, ActorName, Director, PremiereDate, Season, CreatedAt, UpdatedAt
        ) VALUES (
            $id, $type, $title, $description, $inventoryNumber, $storageLocation, $condition, $responsiblePerson, $notes, $mainPhotoPath,
            $size, $color, $material, $careInstructions, $dimensions, $weight, $fragility, $movement,
            $characterName, $actorName, $director, $premiereDate, $season, $createdAt, $updatedAt
        )
        """;

    private const string UpdateSql = """
        UPDATE CatalogItems SET
            Type = $type,
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
            CareInstructions = $careInstructions,
            Dimensions = $dimensions,
            Weight = $weight,
            Fragility = $fragility,
            Movement = $movement,
            CharacterName = $characterName,
            ActorName = $actorName,
            Director = $director,
            PremiereDate = $premiereDate,
            Season = $season,
            UpdatedAt = $updatedAt
        WHERE Id = $id
        """;
}
