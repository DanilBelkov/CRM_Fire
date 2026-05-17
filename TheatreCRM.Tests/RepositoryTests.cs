using TheatreCRM.App.Data;
using TheatreCRM.App.Models;

namespace TheatreCRM.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly string _rootPath;
    private readonly string _databasePath;
    private readonly TheatreRepository _repository;

    public RepositoryTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "TheatreCRM.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);
        _databasePath = Path.Combine(_rootPath, "theatre-crm.sqlite");
        _repository = new TheatreRepository(_databasePath);
        _repository.Initialize();
    }

    [Fact]
    public void Initialize_CreatesSqliteDatabase()
    {
        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public void SaveAndSearch_Clothing_ReturnsCreatedItemWithTags()
    {
        var item = new CatalogItem
        {
            Type = CatalogItemType.Clothing,
            Title = "Зеленая шляпа",
            Description = "Шляпа для весенней сцены",
            StorageLocation = "Сектор A",
            Condition = "Хорошее",
            Size = "M"
        };
        item.Tags.Add("весна");
        item.Tags.Add("головной убор");

        _repository.Save(item);

        var result = _repository.Search(CatalogItemType.Clothing, "весенней");

        Assert.Single(result);
        Assert.Equal("Зеленая шляпа", result[0].Title);
        Assert.Contains("весна", result[0].Tags);
        Assert.Equal("M", result[0].Size);
    }

    [Fact]
    public void SaveAndSearch_Prop_ReturnsCreatedItem()
    {
        var item = new CatalogItem
        {
            Type = CatalogItemType.Prop,
            Title = "Фонарь",
            Description = "Ручной фонарь для сцены в лесу",
            Dimensions = "30 см",
            Fragility = "Хрупкий"
        };

        _repository.Save(item);

        var result = _repository.Search(CatalogItemType.Prop, "лесу");

        Assert.Single(result);
        Assert.Equal("Фонарь", result[0].Title);
        Assert.Equal("30 см", result[0].Dimensions);
    }

    [Fact]
    public void Costume_CanContainClothingAndProps()
    {
        var hat = SaveItem(CatalogItemType.Clothing, "Шляпа");
        var cane = SaveItem(CatalogItemType.Prop, "Трость");
        var costume = SaveItem(CatalogItemType.Costume, "Костюм Волшебника");

        _repository.ReplaceLinks("CostumeClothingItems", "CostumeId", "ClothingItemId", costume.Id, [hat.Id]);
        _repository.ReplaceLinks("CostumeProps", "CostumeId", "PropId", costume.Id, [cane.Id]);

        var loaded = _repository.GetById(costume.Id);

        Assert.NotNull(loaded);
        Assert.Contains(loaded.RelatedItems, item => item.Id == hat.Id && item.Type == CatalogItemType.Clothing);
        Assert.Contains(loaded.RelatedItems, item => item.Id == cane.Id && item.Type == CatalogItemType.Prop);
    }

    [Fact]
    public void Performance_CanContainCostumeDirectPropAndDirectClothing()
    {
        var dress = SaveItem(CatalogItemType.Clothing, "Платье");
        var mirror = SaveItem(CatalogItemType.Prop, "Зеркало");
        var costume = SaveItem(CatalogItemType.Costume, "Костюм Королевы");
        var performance = SaveItem(CatalogItemType.Performance, "Снежная королева");

        _repository.ReplaceLinks("PerformanceCostumes", "PerformanceId", "CostumeId", performance.Id, [costume.Id]);
        _repository.ReplaceLinks("PerformanceProps", "PerformanceId", "PropId", performance.Id, [mirror.Id]);
        _repository.ReplaceLinks("PerformanceClothingItems", "PerformanceId", "ClothingItemId", performance.Id, [dress.Id]);

        var loaded = _repository.GetById(performance.Id);

        Assert.NotNull(loaded);
        Assert.Contains(loaded.RelatedItems, item => item.Id == costume.Id && item.Type == CatalogItemType.Costume);
        Assert.Contains(loaded.RelatedItems, item => item.Id == mirror.Id && item.Type == CatalogItemType.Prop);
        Assert.Contains(loaded.RelatedItems, item => item.Id == dress.Id && item.Type == CatalogItemType.Clothing);
    }

    [Fact]
    public void SoftDelete_HidesItemFromSearch()
    {
        var item = SaveItem(CatalogItemType.Clothing, "Старый плащ");

        _repository.SoftDelete(item.Id);

        var result = _repository.Search(CatalogItemType.Clothing, "плащ");

        Assert.Empty(result);
    }

    [Fact]
    public void Tags_CanBeGroupedAndAssignedToAllCatalogTypes()
    {
        var group = new TagGroup { Name = "Сезон" };
        _repository.SaveTagGroup(group);
        var tag = new Tag { GroupId = group.Id, Name = "Зима" };
        _repository.SaveTag(tag);

        foreach (var type in new[] { CatalogItemType.Clothing, CatalogItemType.Prop, CatalogItemType.Costume, CatalogItemType.Performance })
        {
            var item = SaveItem(type, $"{type} Зима");
            item.Tags.Add("Зима");
            _repository.Save(item);
        }

        Assert.Single(_repository.Search(CatalogItemType.Clothing, "", group.Id, tag.Id));
        Assert.Single(_repository.Search(CatalogItemType.Prop, "", group.Id, tag.Id));
        Assert.Single(_repository.Search(CatalogItemType.Costume, "", group.Id, tag.Id));
        Assert.Single(_repository.Search(CatalogItemType.Performance, "", group.Id, tag.Id));
    }

    [Fact]
    public void TagReplace_MovesLinksWithoutDeletingCatalogItems()
    {
        var group = new TagGroup { Name = "Эпоха" };
        _repository.SaveTagGroup(group);
        var oldTag = new Tag { GroupId = group.Id, Name = "Барокко" };
        var newTag = new Tag { GroupId = group.Id, Name = "XVIII век" };
        _repository.SaveTag(oldTag);
        _repository.SaveTag(newTag);
        var item = SaveItem(CatalogItemType.Prop, "Канделябр");
        item.Tags.Add("Барокко");
        _repository.Save(item);

        _repository.ReplaceTag(oldTag.Id, newTag.Id);

        Assert.Equal(0, _repository.GetTagUsageCount(oldTag.Id));
        Assert.Equal(1, _repository.GetTagUsageCount(newTag.Id));
        Assert.NotNull(_repository.GetById(item.Id));
    }

    [Fact]
    public void ArchiveTag_KeepsExistingLinks()
    {
        var group = new TagGroup { Name = "Сектор" };
        _repository.SaveTagGroup(group);
        var tag = new Tag { GroupId = group.Id, Name = "Стеллаж 1" };
        _repository.SaveTag(tag);
        var item = SaveItem(CatalogItemType.Clothing, "Пальто");
        item.Tags.Add("Стеллаж 1");
        _repository.Save(item);

        _repository.ArchiveTag(tag.Id);

        Assert.Equal(1, _repository.GetTagUsageCount(tag.Id));
        Assert.Empty(_repository.GetTags(group.Id));
        Assert.Contains(_repository.GetTags(group.Id, includeArchived: true), x => x.Id == tag.Id && x.IsArchived);
    }

    [Fact]
    public void GroupingByTagGroup_ReturnsItemsWithGroupHeaderAndWithoutTagBucket()
    {
        var group = new TagGroup { Name = "Материал" };
        _repository.SaveTagGroup(group);
        var tag = new Tag { GroupId = group.Id, Name = "Шелк" };
        _repository.SaveTag(tag);
        var silk = SaveItem(CatalogItemType.Clothing, "Платок");
        silk.Tags.Add("Шелк");
        _repository.Save(silk);
        SaveItem(CatalogItemType.Clothing, "Жилет");

        var result = _repository.Search(CatalogItemType.Clothing, "", group.Id);

        Assert.Contains(result, item => item.Title == "Платок" && item.GroupHeader == "Шелк");
        Assert.Contains(result, item => item.Title == "Жилет" && item.GroupHeader == "Без тега");
    }

    [Fact]
    public void SavedView_PersistsSectionTagFiltersAndGrouping()
    {
        var group = new TagGroup { Name = "Вид хранения" };
        _repository.SaveTagGroup(group);
        var tag = new Tag { GroupId = group.Id, Name = "Короб" };
        _repository.SaveTag(tag);
        var view = new SavedView
        {
            Name = "Одежда в коробах",
            SectionType = CatalogItemType.Clothing,
            TagGroupId = group.Id,
            TagId = tag.Id,
            GroupByTagGroup = true,
            IsShownInSidebar = true
        };

        _repository.SaveView(view);

        var loaded = Assert.Single(_repository.GetSavedViews(sidebarOnly: true));
        Assert.Equal("Одежда в коробах", loaded.Name);
        Assert.Equal(CatalogItemType.Clothing, loaded.SectionType);
        Assert.Equal(group.Id, loaded.TagGroupId);
        Assert.Equal(tag.Id, loaded.TagId);
        Assert.True(loaded.GroupByTagGroup);
    }

    private CatalogItem SaveItem(CatalogItemType type, string title)
    {
        var item = new CatalogItem
        {
            Type = type,
            Title = title,
            Description = $"{title} описание"
        };
        _repository.Save(item);
        return item;
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
