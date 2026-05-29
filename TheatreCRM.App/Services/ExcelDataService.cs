using ClosedXML.Excel;
using System.IO;
using TheatreCRM.App.Data;
using TheatreCRM.App.Models;

namespace TheatreCRM.App.Services;

public sealed class ExcelDataService(TheatreRepository repository, AppPaths paths)
{
    public string ExportDatabase(bool includePhotos)
    {
        Directory.CreateDirectory(paths.ExportsPath);
        var filePath = Path.Combine(paths.ExportsPath, $"theatre-crm-export-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");
        using var workbook = new XLWorkbook();
        var items = repository.GetAllItems().ToList();
        AddCatalogSheet(workbook, "Одежда", items.Where(x => x.Type == CatalogItemType.Clothing), includePhotos);
        AddCatalogSheet(workbook, "Реквизит", items.Where(x => x.Type == CatalogItemType.Prop), includePhotos);
        AddCatalogSheet(workbook, "Костюмы", items.Where(x => x.Type == CatalogItemType.Costume), includePhotos);
        AddCatalogSheet(workbook, "Спектакли", items.Where(x => x.Type == CatalogItemType.Performance), includePhotos);
        AddCatalogSheet(workbook, "Секторы", items.Where(x => x.Type == CatalogItemType.Sector), includePhotos);
        AddRelationshipsSheet(workbook, items);
        workbook.SaveAs(filePath);
        return filePath;
    }

    public int ImportCatalog(string filePath)
    {
        if (Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return ImportCsv(filePath, CatalogItemType.Clothing);
        }

        using var workbook = new XLWorkbook(filePath);
        var imported = 0;
        imported += ImportSheet(workbook, "Одежда", CatalogItemType.Clothing);
        imported += ImportSheet(workbook, "Реквизит", CatalogItemType.Prop);
        imported += ImportSheet(workbook, "Костюмы", CatalogItemType.Costume);
        imported += ImportSheet(workbook, "Спектакли", CatalogItemType.Performance);
        imported += ImportSheet(workbook, "Секторы", CatalogItemType.Sector);
        return imported;
    }

    private static void AddCatalogSheet(XLWorkbook workbook, string name, IEnumerable<CatalogItem> items, bool includePhotos)
    {
        var sheet = workbook.Worksheets.Add(name);
        var headers = new[] { "Id", "Название", "Описание", "Инвентарный номер", "Место хранения", "Состояние", "Ответственный", "Секторы", "Фото" };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(1, i + 1).Style.Font.Bold = true;
        }

        var row = 2;
        foreach (var item in items)
        {
            sheet.Cell(row, 1).Value = item.Id;
            sheet.Cell(row, 2).Value = item.Title;
            sheet.Cell(row, 3).Value = item.Description;
            sheet.Cell(row, 4).Value = item.InventoryNumber;
            sheet.Cell(row, 5).Value = item.StorageLocation;
            sheet.Cell(row, 6).Value = item.Condition;
            sheet.Cell(row, 7).Value = item.ResponsiblePerson;
            sheet.Cell(row, 8).Value = item.Type == CatalogItemType.Sector ? "" : string.Join(", ", item.SectorNames);
            sheet.Cell(row, 9).Value = includePhotos ? item.MainPhotoPath : "";
            row++;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void AddRelationshipsSheet(XLWorkbook workbook, IEnumerable<CatalogItem> items)
    {
        var sheet = workbook.Worksheets.Add("Связи");
        sheet.Cell(1, 1).Value = "Карточка";
        sheet.Cell(1, 2).Value = "Раздел";
        sheet.Cell(1, 3).Value = "Связанная карточка";
        sheet.Cell(1, 4).Value = "Связанный раздел";
        sheet.Range("A1:D1").Style.Font.Bold = true;
        var row = 2;
        foreach (var item in items)
        {
            foreach (var relation in item.RelatedItems)
            {
                sheet.Cell(row, 1).Value = item.Title;
                sheet.Cell(row, 2).Value = item.Type.ToRussian();
                sheet.Cell(row, 3).Value = relation.Title;
                sheet.Cell(row, 4).Value = relation.Type.ToRussian();
                row++;
            }
        }
        sheet.Columns().AdjustToContents();
    }

    private int ImportSheet(XLWorkbook workbook, string sheetName, CatalogItemType type)
    {
        if (!workbook.Worksheets.TryGetWorksheet(sheetName, out var sheet))
        {
            return 0;
        }

        var imported = 0;
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var title = row.Cell(2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var item = new CatalogItem
            {
                Type = type,
                Title = title,
                Description = row.Cell(3).GetString(),
                InventoryNumber = row.Cell(4).GetString(),
                StorageLocation = row.Cell(5).GetString(),
                Condition = row.Cell(6).GetString(),
                ResponsiblePerson = row.Cell(7).GetString()
            };

            repository.Save(item);
            LinkSectors(item, row.Cell(8).GetString());
            imported++;
        }

        return imported;
    }

    private int ImportCsv(string filePath, CatalogItemType type)
    {
        var lines = File.ReadAllLines(filePath);
        var imported = 0;
        foreach (var line in lines.Skip(1))
        {
            var columns = line.Split(';');
            if (columns.Length == 1)
            {
                columns = line.Split(',');
            }

            var title = columns.ElementAtOrDefault(0)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var item = new CatalogItem
            {
                Type = type,
                Title = title,
                Description = columns.ElementAtOrDefault(1)?.Trim() ?? "",
                InventoryNumber = columns.ElementAtOrDefault(2)?.Trim() ?? "",
                StorageLocation = columns.ElementAtOrDefault(3)?.Trim() ?? "",
                Condition = columns.ElementAtOrDefault(4)?.Trim() ?? "",
                ResponsiblePerson = columns.ElementAtOrDefault(5)?.Trim() ?? ""
            };

            repository.Save(item);
            LinkSectors(item, columns.ElementAtOrDefault(6) ?? "");
            imported++;
        }

        return imported;
    }

    private void LinkSectors(CatalogItem item, string rawSectorNames)
    {
        if (item.Type == CatalogItemType.Sector)
        {
            return;
        }

        var sectorIds = rawSectorNames
            .Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => repository.FindOrCreateSector(name).Id)
            .Distinct()
            .ToList();

        var table = item.Type switch
        {
            CatalogItemType.Clothing => "SectorClothingItems",
            CatalogItemType.Prop => "SectorProps",
            CatalogItemType.Costume => "SectorCostumes",
            _ => ""
        };

        var linkedColumn = item.Type switch
        {
            CatalogItemType.Clothing => "ClothingItemId",
            CatalogItemType.Prop => "PropId",
            CatalogItemType.Costume => "CostumeId",
            _ => ""
        };

        if (!string.IsNullOrWhiteSpace(table))
        {
            repository.ReplaceInverseLinks(table, "SectorId", linkedColumn, item.Id, sectorIds);
        }
    }
}
