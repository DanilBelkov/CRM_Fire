using TheatreCRM.App.Data;
using TheatreCRM.App.Models;
using TheatreCRM.App.Services;

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
    public void SaveAndSearch_Clothing_ReturnsCreatedItem()
    {
        var item = new CatalogItem
        {
            Type = CatalogItemType.Clothing,
            Title = "Зеленая шляпа",
            Description = "Шляпа для весенней сцены",
            StorageLocation = "Стеллаж 1",
            Condition = "Хорошее",
            Size = "M"
        };

        _repository.Save(item);

        var result = _repository.Search(CatalogItemType.Clothing, "весенней");

        Assert.Single(result);
        Assert.Equal("Зеленая шляпа", result[0].Title);
        Assert.Equal("M", result[0].Size);
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
    public void Sector_CanContainClothingCostumesAndProps()
    {
        var coat = SaveItem(CatalogItemType.Clothing, "Пальто");
        var sword = SaveItem(CatalogItemType.Prop, "Шпага");
        var costume = SaveItem(CatalogItemType.Costume, "Мундир");
        var sector = SaveItem(CatalogItemType.Sector, "Исторический сектор");

        _repository.ReplaceLinks("SectorClothingItems", "SectorId", "ClothingItemId", sector.Id, [coat.Id]);
        _repository.ReplaceLinks("SectorProps", "SectorId", "PropId", sector.Id, [sword.Id]);
        _repository.ReplaceLinks("SectorCostumes", "SectorId", "CostumeId", sector.Id, [costume.Id]);

        var loadedSector = _repository.GetById(sector.Id);
        var loadedCoat = _repository.GetById(coat.Id);

        Assert.NotNull(loadedSector);
        Assert.Contains(loadedSector.RelatedItems, item => item.Id == coat.Id);
        Assert.Contains(loadedSector.RelatedItems, item => item.Id == sword.Id);
        Assert.Contains(loadedSector.RelatedItems, item => item.Id == costume.Id);

        Assert.NotNull(loadedCoat);
        Assert.Contains("Исторический сектор", loadedCoat.SectorNames);
    }

    [Fact]
    public void ResponsiblePerson_IsStoredInUserDirectory()
    {
        var item = SaveItem(CatalogItemType.Prop, "Канделябр");
        item.ResponsiblePerson = "Иван Петров";

        _repository.Save(item);

        Assert.Contains(_repository.GetUsers(), user => user.FullName == "Иван Петров");
    }

    [Fact]
    public void Search_FiltersByAllFieldsIncludingResponsibleAndStorage()
    {
        var item = SaveItem(CatalogItemType.Clothing, "Плащ");
        item.StorageLocation = "Комната А";
        item.ResponsiblePerson = "Мария Соколова";
        _repository.Save(item);

        Assert.Single(_repository.Search(CatalogItemType.Clothing, "Комната А"));
        Assert.Single(_repository.Search(CatalogItemType.Clothing, "Мария"));
    }

    [Fact]
    public void SavedView_PersistsSectionAndSearchText()
    {
        var view = new SavedView
        {
            Name = "Одежда Марии",
            SectionType = CatalogItemType.Clothing,
            SearchText = "Мария",
            IsShownInSidebar = true
        };

        _repository.SaveView(view);

        var loaded = Assert.Single(_repository.GetSavedViews(sidebarOnly: true));
        Assert.Equal("Одежда Марии", loaded.Name);
        Assert.Equal(CatalogItemType.Clothing, loaded.SectionType);
        Assert.Equal("Мария", loaded.SearchText);
    }

    [Fact]
    public void FtsLikeSearch_UpdatesAfterEdit()
    {
        var item = SaveItem(CatalogItemType.Prop, "Книга");
        item.Description = "Старинный фолиант для библиотеки";
        _repository.Save(item);

        Assert.Single(_repository.Search(CatalogItemType.Prop, "фолиант"));

        item.Description = "Современный журнал";
        _repository.Save(item);

        Assert.Empty(_repository.Search(CatalogItemType.Prop, "фолиант"));
        Assert.Single(_repository.Search(CatalogItemType.Prop, "журнал"));
    }

    [Fact]
    public void Trash_RestoreAndPermanentDelete_WorkWithoutLosingControl()
    {
        var item = SaveItem(CatalogItemType.Clothing, "Плащ для корзины");

        _repository.SoftDelete(item.Id);

        Assert.Single(_repository.GetTrash());
        Assert.Empty(_repository.Search(CatalogItemType.Clothing, "корзины"));

        _repository.RestoreFromTrash(item.Id);

        Assert.Empty(_repository.GetTrash());
        Assert.Single(_repository.Search(CatalogItemType.Clothing, "корзины"));

        _repository.SoftDelete(item.Id);
        _repository.PermanentlyDelete(item.Id);

        Assert.Null(_repository.GetById(item.Id));
        Assert.Empty(_repository.GetTrash());
    }

    [Fact]
    public void AuditLog_RecordsCreateUpdateAndDelete()
    {
        var item = SaveItem(CatalogItemType.Performance, "История");
        item.Description = "Изменение";
        _repository.Save(item);
        _repository.SoftDelete(item.Id);

        var audit = _repository.GetAuditLog(item.Id);

        Assert.Contains(audit, row => row.Contains("Создание карточки"));
        Assert.Contains(audit, row => row.Contains("Обновление карточки"));
        Assert.Contains(audit, row => row.Contains("Удаление в корзину"));
    }

    [Fact]
    public void Photos_CanBeLinkedToCatalogItem()
    {
        var item = SaveItem(CatalogItemType.Clothing, "Фото карточка");

        _repository.AddPhoto(item.Id, "photos/clothing/test.jpg");
        _repository.AddPhoto(item.Id, "photos/clothing/test-2.jpg");

        var photos = _repository.GetPhotos(item.Id);
        Assert.Equal(["photos/clothing/test.jpg", "photos/clothing/test-2.jpg"], photos);
    }

    [Fact]
    public void ExcelExportAndImport_RoundTripsCatalogRows()
    {
        var paths = new AppPaths();
        paths.EnsureCreated();
        var service = new ExcelDataService(_repository, paths);
        var item = SaveItem(CatalogItemType.Clothing, "Экспортируемая шляпа");
        var sector = SaveItem(CatalogItemType.Sector, "Сектор А");
        _repository.ReplaceLinks("SectorClothingItems", "SectorId", "ClothingItemId", sector.Id, [item.Id]);

        var exportPath = service.ExportDatabase(includePhotos: false);

        Assert.True(File.Exists(exportPath));

        var secondDatabase = Path.Combine(_rootPath, "import.sqlite");
        var secondRepository = new TheatreRepository(secondDatabase);
        secondRepository.Initialize();
        var importService = new ExcelDataService(secondRepository, paths);

        var imported = importService.ImportCatalog(exportPath);

        Assert.True(imported >= 1);
        Assert.Contains(secondRepository.Search(CatalogItemType.Clothing, "шляпа"), x => x.Title == "Экспортируемая шляпа");
    }

    [Fact]
    public void CsvImport_CreatesCatalogRowsAndLinksSectors()
    {
        var csvPath = Path.Combine(_rootPath, "import.csv");
        File.WriteAllText(csvPath, "Название;Описание;Инвентарный номер;Место хранения;Состояние;Ответственный;Секторы\nCSV шляпа;Описание;INV-1;Склад;Хорошее;Иван;Лето|Шляпный сектор");
        var service = new ExcelDataService(_repository, new AppPaths());

        var imported = service.ImportCatalog(csvPath);

        Assert.Equal(1, imported);
        var item = Assert.Single(_repository.Search(CatalogItemType.Clothing, "CSV"));
        Assert.Contains("Лето", item.SectorNames);
        Assert.Contains("Шляпный сектор", item.SectorNames);
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
