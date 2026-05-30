using Microsoft.Data.Sqlite;
using System.IO;
using System.IO.Compression;

namespace TheatreCRM.App.Services;

public sealed class BackupService(AppPaths paths)
{
    // Максимальное количество хранимых бэкапов
    private const int MaxBackupCount = 15;

    public string CreateBackup()
    {
        Directory.CreateDirectory(paths.BackupsPath);
        CleanupOldBackups();

        // Проверяем, есть ли уже бэкап за сегодня
        var todayPrefix = $"theatre-crm-backup-{DateTime.Now:yyyyMMdd}";
        var existingToday = Directory.GetFiles(paths.BackupsPath, $"{todayPrefix}*.zip");
        if (existingToday.Length > 0)
        {
            // Бэкап за сегодня уже есть — не создаём новый
            return existingToday[0];
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var filePath = Path.Combine(paths.BackupsPath, $"theatre-crm-backup-{timestamp}.zip");
        var tempPath = Path.Combine(paths.RootPath, $"theatre-crm-backup-{Guid.NewGuid():N}.zip");

        ZipFile.CreateFromDirectory(paths.DataPath, tempPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        File.Move(tempPath, filePath);
        return filePath;
    }

    public void RestoreBackup(string backupPath)
    {
        SqliteConnection.ClearAllPools();
        var safetyBackup = CreateBackup();
        _ = safetyBackup;
        CleanupOldBackups();
        ZipFile.ExtractToDirectory(backupPath, paths.DataPath, overwriteFiles: true);
    }

    private void CleanupOldBackups()
    {
        if (!Directory.Exists(paths.BackupsPath))
            return;

        var backupFiles = Directory.GetFiles(paths.BackupsPath, "theatre-crm-backup-*.zip")
            .OrderByDescending(f => f)
            .ToList();

        // Удаляем файлы, превышающие лимит
        if (backupFiles.Count <= MaxBackupCount)
            return;

        foreach (var file in backupFiles.Skip(MaxBackupCount))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Если файл занят — пропускаем
            }
        }
    }
}
