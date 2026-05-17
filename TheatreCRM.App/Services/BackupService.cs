using System.IO.Compression;
using System.IO;
using Microsoft.Data.Sqlite;

namespace TheatreCRM.App.Services;

public sealed class BackupService(AppPaths paths)
{
    public string CreateBackup()
    {
        Directory.CreateDirectory(paths.BackupsPath);
        var filePath = Path.Combine(paths.BackupsPath, $"theatre-crm-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var tempPath = Path.Combine(paths.RootPath, $"theatre-crm-backup-{Guid.NewGuid():N}.zip");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        ZipFile.CreateFromDirectory(paths.DataPath, tempPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        File.Move(tempPath, filePath);
        return filePath;
    }

    public void RestoreBackup(string backupPath)
    {
        SqliteConnection.ClearAllPools();
        var safetyBackup = CreateBackup();
        _ = safetyBackup;
        ZipFile.ExtractToDirectory(backupPath, paths.DataPath, overwriteFiles: true);
    }
}
