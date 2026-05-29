using System.Collections.ObjectModel;

namespace TheatreCRM.App.Models;

public enum CatalogItemType
{
    Clothing = 1,
    Prop = 2,
    Costume = 3,
    Performance = 4,
    Sector = 5
}

public static class CatalogItemTypeNames
{
    public static string ToRussian(this CatalogItemType type) => type switch
    {
        CatalogItemType.Clothing => "Одежда на подборе",
        CatalogItemType.Prop => "Реквизит",
        CatalogItemType.Costume => "Костюмы",
        CatalogItemType.Performance => "Спектакли",
        CatalogItemType.Sector => "Секторы",
        _ => "Раздел"
    };

    public static string CreateTitle(this CatalogItemType type) => type switch
    {
        CatalogItemType.Clothing => "Новая одежда на подборе",
        CatalogItemType.Prop => "Новый реквизит",
        CatalogItemType.Costume => "Новый костюм",
        CatalogItemType.Performance => "Новый спектакль",
        CatalogItemType.Sector => "Новый сектор",
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
    public string CareInstructions { get; set; } = "";
    public string Dimensions { get; set; } = "";
    public string Weight { get; set; } = "";
    public string Fragility { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string ActorName { get; set; } = "";
    public string Movement { get; set; } = "";
    public string Director { get; set; } = "";
    public string PremiereDate { get; set; } = "";
    public string Season { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string DeletedAt { get; set; } = "";
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<CatalogSummary> RelatedItems { get; } = [];
    public ObservableCollection<string> SectorNames { get; } = [];

    public string Subtitle
    {
        get
        {
            var parts = new[] { InventoryNumber, StorageLocation, Condition }
                .Where(value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" • ", parts);
        }
    }

    public string SectorsText => SectorNames.Count == 0 ? "Без сектора" : $"Секторы: {string.Join(", ", SectorNames)}";
}

public sealed class SavedView
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public CatalogItemType SectionType { get; set; }
    public string SearchText { get; set; } = "";
    public bool IsShownInSidebar { get; set; } = true;
}

public sealed class CatalogSummary
{
    public string Id { get; set; } = "";
    public CatalogItemType Type { get; set; }
    public string Title { get; set; } = "";
    public string Display => $"{Type.ToRussian()}: {Title}";
}

public sealed class DirectoryUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FullName { get; set; } = "";

    public override string ToString() => FullName;
}
