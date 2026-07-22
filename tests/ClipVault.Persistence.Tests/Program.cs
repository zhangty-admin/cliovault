using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClipVault.Models;
using ClipVault.Services;

var testRoot = Path.Combine(Path.GetTempPath(), "ClipVault.Persistence.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);

try
{
    TestLargeTextRoundTrip(testRoot);
    TestLegacyHistoryMigration(Path.Combine(testRoot, "legacy"));
    TestQueuedSaveKeepsLatestSnapshot(Path.Combine(testRoot, "queue"));
    TestExportImportIncludesLargeText(Path.Combine(testRoot, "export"), Path.Combine(testRoot, "import"));
    Console.WriteLine("Persistence tests passed.");
}
finally
{
    if (Directory.Exists(testRoot))
        Directory.Delete(testRoot, recursive: true);
}

static void TestLargeTextRoundTrip(string dataDir)
{
    var service = new PersistenceService(dataDir);
    // 约 12 MB，贴近用户复制大段 SQL/JSON 的真实使用规模。
    var largeText = string.Concat(Enumerable.Repeat("SELECT * FROM BOM表 WHERE ID = 1;\r\n", 300_000));
    var item = NewTextItem(largeText);

    service.Save([item], ["工作"], []);

    var historyPath = Path.Combine(dataDir, "history.json");
    var json = File.ReadAllText(historyPath);
    Assert(json.Length < largeText.Length / 10, "history.json should contain metadata instead of the full large text");
    Assert(!json.Contains(largeText[..100], StringComparison.Ordinal), "large text leaked into history.json");

    using var document = JsonDocument.Parse(json);
    var dto = document.RootElement.GetProperty("Items")[0];
    var contentPath = dto.GetProperty("ContentPath").GetString();
    Assert(!string.IsNullOrWhiteSpace(contentPath), "large text should have ContentPath");
    Assert(File.Exists(Path.Combine(dataDir, "contents", contentPath!)), "large text file should exist");

    var (items, tags, recycleBin) = service.LoadAll();
    Assert(items.Count == 1 && items[0].Text == largeText, "large text must round-trip without truncation");
    Assert(tags.SequenceEqual(["工作"]), "tags must round-trip");
    Assert(recycleBin.Count == 0, "recycle bin should remain empty");

    var updatedText = largeText.Replace("ID = 1", "ID = 2", StringComparison.Ordinal);
    item.Text = updatedText;
    service.InvalidateText(item.Id);
    service.Save([item], ["工作"], []);
    Assert(service.LoadAll().items[0].Text == updatedText, "editing large text must replace external content");
}

static void TestLegacyHistoryMigration(string dataDir)
{
    Directory.CreateDirectory(dataDir);
    var legacyText = string.Concat(Enumerable.Repeat("legacy-json-content\n", 20_000));
    var item = NewTextItem(legacyText);
    var legacy = new ClipboardHistoryData
    {
        Version = 3,
        Items =
        [
            new ClipboardItemDto
            {
                Id = item.Id,
                Type = item.Type,
                Text = item.Text,
                CapturedAt = item.CapturedAt,
                Tags = ["旧数据"]
            }
        ]
    };
    File.WriteAllText(Path.Combine(dataDir, "history.json"), JsonSerializer.Serialize(legacy));

    var service = new PersistenceService(dataDir);
    var loaded = service.LoadAll();
    Assert(loaded.items.Single().Text == legacyText, "version 3 inline text must still load");

    service.Save(loaded.items, loaded.tags, loaded.recycleBin);
    var migratedJson = File.ReadAllText(Path.Combine(dataDir, "history.json"));
    Assert(migratedJson.Contains("ContentPath", StringComparison.Ordinal), "legacy large text should migrate on save");
    Assert(!migratedJson.Contains(legacyText[..100], StringComparison.Ordinal), "migrated history must not retain the large inline text");
    Assert(service.LoadAll().items.Single().Text == legacyText, "migrated text must remain complete");
}

static void TestQueuedSaveKeepsLatestSnapshot(string dataDir)
{
    var service = new PersistenceService(dataDir);
    for (var i = 0; i < 10; i++)
    {
        var snapshot = service.CreateSnapshot([NewTextItem($"queued-{i}")]);
        service.QueueSave(snapshot);
    }

    var latest = service.CreateSnapshot([NewTextItem("queued-final")]);
    service.Flush(latest);
    Assert(service.LoadAll().items.Single().Text == "queued-final", "flush must persist the latest queued snapshot");
}

static void TestExportImportIncludesLargeText(string sourceDir, string targetDir)
{
    var largeText = string.Concat(Enumerable.Repeat("export-import-large-content\n", 20_000));
    var sourcePersistence = new PersistenceService(sourceDir);
    sourcePersistence.Save([NewTextItem(largeText)]);
    var zipPath = Path.Combine(sourceDir, "exports", "clipvault.zip");
    var sourceTransfer = new ImportExportService(sourcePersistence);
    sourceTransfer.ExportTo(zipPath);

    var preview = sourceTransfer.Preview(zipPath);
    Assert(preview.HasHistory && preview.ContentCount == 1, "export must include external large text");

    var targetPersistence = new PersistenceService(targetDir);
    new ImportExportService(targetPersistence).ImportFrom(zipPath);
    Assert(targetPersistence.LoadAll().items.Single().Text == largeText, "imported large text must remain complete");
}

static ClipboardItem NewTextItem(string text) => new()
{
    Type = ClipboardItemType.Text,
    Text = text,
    CapturedAt = DateTime.Now,
    SmartType = "SQL"
};

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
