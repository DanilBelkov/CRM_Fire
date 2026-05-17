using System.IO;

namespace TheatreCRM.App.Services;

public sealed class AppPaths
{
    public AppPaths()
    {
        RootPath = AppContext.BaseDirectory;
        DataPath = Path.Combine(RootPath, "data");
        PhotosPath = Path.Combine(DataPath, "photos");
        BackupsPath = Path.Combine(DataPath, "backups");
        ExportsPath = Path.Combine(DataPath, "exports");
        SettingsPath = Path.Combine(DataPath, "settings");
        DatabasePath = Path.Combine(DataPath, "theatre-crm.sqlite");
    }

    public string RootPath { get; }
    public string DataPath { get; }
    public string PhotosPath { get; }
    public string BackupsPath { get; }
    public string ExportsPath { get; }
    public string SettingsPath { get; }
    public string DatabasePath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(PhotosPath);
        Directory.CreateDirectory(Path.Combine(PhotosPath, "clothing"));
        Directory.CreateDirectory(Path.Combine(PhotosPath, "props"));
        Directory.CreateDirectory(Path.Combine(PhotosPath, "costumes"));
        Directory.CreateDirectory(Path.Combine(PhotosPath, "performances"));
        Directory.CreateDirectory(BackupsPath);
        Directory.CreateDirectory(ExportsPath);
        Directory.CreateDirectory(SettingsPath);
    }

    public string ToAbsolutePhotoPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return "";
        }

        return Path.Combine(DataPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public string CopyPhoto(string sourcePath, Models.CatalogItemType type)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return "";
        }

        var folder = type switch
        {
            Models.CatalogItemType.Clothing => "clothing",
            Models.CatalogItemType.Prop => "props",
            Models.CatalogItemType.Costume => "costumes",
            Models.CatalogItemType.Performance => "performances",
            _ => "misc"
        };

        var extension = Path.GetExtension(sourcePath);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var relativePath = Path.Combine("photos", folder, fileName).Replace('\\', '/');
        var destination = Path.Combine(DataPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(sourcePath, destination, overwrite: false);
        return relativePath;
    }
}
