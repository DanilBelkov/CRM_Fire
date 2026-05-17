using System.Collections.ObjectModel;

namespace TheatreCRM.App.Models;

public enum CatalogItemType
{
    Clothing = 1,
    Prop = 2,
    Costume = 3,
    Performance = 4
}

public static class CatalogItemTypeNames
{
    public static string ToRussian(this CatalogItemType type) => type switch
    {
        CatalogItemType.Clothing => "Одежда",
        CatalogItemType.Prop => "Реквизит",
        CatalogItemType.Costume => "Костюмы",
        CatalogItemType.Performance => "Спектакли",
        _ => "Раздел"
    };

    public static string CreateTitle(this CatalogItemType type) => type switch
    {
        CatalogItemType.Clothing => "Новая одежда",
        CatalogItemType.Prop => "Новый реквизит",
        CatalogItemType.Costume => "Новый костюм",
        CatalogItemType.Performance => "Новый спектакль",
        _ => "Новая карточка"
    };
}

public sealed class CatalogItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public CatalogItemType Type { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string InventoryNumber { get; set; } = "";
    public string StorageLocation { get; set; } = "";
    public string Condition { get; set; } = "Хорошее";
    public string ResponsiblePerson { get; set; } = "";
    public string Notes { get; set; } = "";
    public string MainPhotoPath { get; set; } = "";
    public string Size { get; set; } = "";
    public string Color { get; set; } = "";
    public string Material { get; set; } = "";
    public string Measurements { get; set; } = "";
    public string CareInstructions { get; set; } = "";
    public string Dimensions { get; set; } = "";
    public string Weight { get; set; } = "";
    public string Fragility { get; set; } = "Обычный";
    public string UsageNotes { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string ActorName { get; set; } = "";
    public string SceneNotes { get; set; } = "";
    public string Director { get; set; } = "";
    public string PremiereDate { get; set; } = "";
    public string Season { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<CatalogSummary> RelatedItems { get; } = [];
    public string GroupHeader { get; set; } = "Без группировки";

    public string TagsText => Tags.Count == 0 ? "Без тегов" : string.Join(", ", Tags);
    public string Subtitle
    {
        get
        {
            var parts = new[] { InventoryNumber, StorageLocation, Condition }
                .Where(value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" • ", parts);
        }
    }
}

public sealed class TagGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Color { get; set; } = "#21A66B";
    public int SortOrder { get; set; }

    public override string ToString() => Name;
}

public sealed class Tag
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Color { get; set; } = "#21A66B";
    public bool IsArchived { get; set; }
    public int UsageCount { get; set; }

    public string DisplayName => IsArchived ? $"{Name} (архив)" : Name;
    public override string ToString() => DisplayName;
}

public sealed class SavedView
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public CatalogItemType SectionType { get; set; }
    public string? TagGroupId { get; set; }
    public string? TagId { get; set; }
    public bool GroupByTagGroup { get; set; }
    public bool IsShownInSidebar { get; set; } = true;
}

public sealed class CatalogSummary
{
    public string Id { get; set; } = "";
    public CatalogItemType Type { get; set; }
    public string Title { get; set; } = "";
    public string Display => $"{Type.ToRussian()}: {Title}";
}
