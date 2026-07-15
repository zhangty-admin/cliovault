using System.IO;
using System.IO.Compression;

namespace ClipVault.Services;

public sealed class ImportExportService
{
    private readonly PersistenceService _persistence;

    public ImportExportService(PersistenceService persistence)
    {
        _persistence = persistence;
    }

    public void ExportTo(string zipPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddFileIfExists(archive, Path.Combine(PersistenceService.DataDirectory, "history.json"), "history.json");
        AddFileIfExists(archive, Path.Combine(PersistenceService.DataDirectory, "settings.json"), "settings.json");

        for (var i = 1; i <= 5; i++)
            AddFileIfExists(archive, Path.Combine(PersistenceService.DataDirectory, $"history.json.bak{i}"), $"history.json.bak{i}");

        var imagesDir = PersistenceService.ImageDirectory;
        if (Directory.Exists(imagesDir))
        {
            foreach (var file in Directory.EnumerateFiles(imagesDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(imagesDir, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, $"images/{relative}", CompressionLevel.Optimal);
            }
        }
    }

    public ImportSummary Preview(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return new ImportSummary(
            HasHistory: archive.GetEntry("history.json") != null,
            HasSettings: archive.GetEntry("settings.json") != null,
            ImageCount: archive.Entries.Count(e => e.FullName.StartsWith("images/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(e.Name)));
    }

    public void ImportFrom(string zipPath)
    {
        var summary = Preview(zipPath);
        if (!summary.HasHistory)
            throw new InvalidOperationException("备份包缺少 history.json");

        BackupCurrentData();

        using var archive = ZipFile.OpenRead(zipPath);
        Directory.CreateDirectory(PersistenceService.DataDirectory);
        Directory.CreateDirectory(PersistenceService.ImageDirectory);

        ExtractFileIfExists(archive, "history.json", Path.Combine(PersistenceService.DataDirectory, "history.json"));
        ExtractFileIfExists(archive, "settings.json", Path.Combine(PersistenceService.DataDirectory, "settings.json"));

        for (var i = 1; i <= 5; i++)
            ExtractFileIfExists(archive, $"history.json.bak{i}", Path.Combine(PersistenceService.DataDirectory, $"history.json.bak{i}"));

        foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("images/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(e.Name)))
        {
            var relative = entry.FullName["images/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(PersistenceService.ImageDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private void BackupCurrentData()
    {
        var backupsDir = Path.Combine(PersistenceService.DataDirectory, "import-backups");
        var zipPath = Path.Combine(backupsDir, $"before-import-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        ExportTo(zipPath);
    }

    private static void AddFileIfExists(ZipArchive archive, string path, string entryName)
    {
        if (File.Exists(path))
            archive.CreateEntryFromFile(path, entryName, CompressionLevel.Optimal);
    }

    private static void ExtractFileIfExists(ZipArchive archive, string entryName, string targetPath)
    {
        var entry = archive.GetEntry(entryName);
        if (entry == null) return;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        entry.ExtractToFile(targetPath, overwrite: true);
    }
}

public readonly record struct ImportSummary(bool HasHistory, bool HasSettings, int ImageCount);
