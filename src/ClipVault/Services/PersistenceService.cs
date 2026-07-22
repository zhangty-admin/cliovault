using System.IO;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using ClipVault.Models;

namespace ClipVault.Services;

/// <summary>
/// 持久化服务 — 将剪贴板历史保存到 JSON 文件，图片保存为本地文件
/// 数据目录: %LocalAppData%\ClipVault\
/// </summary>
public class PersistenceService
{
    private static readonly string DefaultDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipVault");
    private readonly string _dataDir;
    private readonly string _imagesDir;
    private readonly string _contentsDir;
    private readonly string _historyFile;
    private readonly ConcurrentDictionary<Guid, string> _knownContentPaths = new();
    private readonly object _saveQueueLock = new();
    private PersistenceSnapshot? _pendingSave;
    private bool _saveWorkerRunning;
    private const int LargeTextThresholdBytes = 256 * 1024;
    private const int BackupCount = 5;

    /// <summary>
    /// 图片存储目录（供 CleanupService 使用）
    /// </summary>
    public static string ImageDirectory => Path.Combine(DefaultDataDir, "images");

    public static string ContentDirectory => Path.Combine(DefaultDataDir, "contents");

    public static string DataDirectory => DefaultDataDir;

    public static string HistoryFilePath => Path.Combine(DefaultDataDir, "history.json");

    public string DataDirectoryPath => _dataDir;
    public string ImageDirectoryPath => _imagesDir;
    public string ContentDirectoryPath => _contentsDir;

    public PersistenceService(string? dataDirectory = null)
    {
        _dataDir = dataDirectory ?? DefaultDataDir;
        _imagesDir = Path.Combine(_dataDir, "images");
        _contentsDir = Path.Combine(_dataDir, "contents");
        _historyFile = Path.Combine(_dataDir, "history.json");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 保存全部历史记录到磁盘
    /// </summary>
    public void Save(
        IEnumerable<ClipboardItem> items,
        IEnumerable<string>? standaloneTags = null,
        IEnumerable<RecycleBinEntry>? recycleBin = null)
    {
        Save(CreateSnapshot(items, standaloneTags, recycleBin));
    }

    public PersistenceSnapshot CreateSnapshot(
        IEnumerable<ClipboardItem> items,
        IEnumerable<string>? standaloneTags = null,
        IEnumerable<RecycleBinEntry>? recycleBin = null)
    {
        return new PersistenceSnapshot(
            items.Select(CloneItem).ToList(),
            standaloneTags?.ToList() ?? new List<string>(),
            recycleBin?.Select(x => new RecycleBinEntry
            {
                Item = CloneItem(x.Item),
                DeletedAt = x.DeletedAt
            }).ToList() ?? new List<RecycleBinEntry>());
    }

    /// <summary>
    /// 后台单写入队列。新请求会替换尚未开始的旧请求，始终保存最新快照。
    /// </summary>
    public void QueueSave(PersistenceSnapshot snapshot)
    {
        lock (_saveQueueLock)
        {
            _pendingSave = snapshot;
            if (_saveWorkerRunning)
                return;

            _saveWorkerRunning = true;
            _ = Task.Run(ProcessSaveQueue);
        }
    }

    public void Flush(PersistenceSnapshot snapshot)
    {
        QueueSave(snapshot);
        lock (_saveQueueLock)
        {
            while (_saveWorkerRunning)
                Monitor.Wait(_saveQueueLock);
        }
    }

    public void InvalidateText(Guid itemId) => _knownContentPaths.TryRemove(itemId, out _);

    private void ProcessSaveQueue()
    {
        while (true)
        {
            PersistenceSnapshot? snapshot;
            lock (_saveQueueLock)
            {
                snapshot = _pendingSave;
                _pendingSave = null;
                if (snapshot == null)
                {
                    _saveWorkerRunning = false;
                    Monitor.PulseAll(_saveQueueLock);
                    return;
                }
            }

            Save(snapshot);
        }
    }

    private void Save(PersistenceSnapshot snapshot)
    {
        try
        {
            EnsureDirectories();

            var data = new ClipboardHistoryData
            {
                Items = snapshot.Items.Select(ToDto).ToList(),
                Tags = snapshot.Tags,
                RecycleBin = snapshot.RecycleBin.Select(x => new RecycleBinEntryDto
                {
                    Item = ToDto(x.Item),
                    DeletedAt = x.DeletedAt
                }).ToList()
            };

            // 写入临时文件再重命名，避免写入失败导致文件损坏
            var tempFile = _historyFile + ".tmp";
            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_historyFile))
            {
                RotateBackups();
                File.Delete(_historyFile);
            }
            File.Move(tempFile, _historyFile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存失败: {ex.Message}");
            Helpers.MessageWindow.Log($"[Persistence] Save failed: {ex}");
        }
    }

    /// <summary>
    /// 从磁盘加载历史记录
    /// </summary>
    public (List<ClipboardItem> items, List<string> tags) LoadWithTags()
    {
        var (items, tags, _) = LoadAll();
        return (items, tags);
    }

    public (List<ClipboardItem> items, List<string> tags, List<RecycleBinEntry> recycleBin) LoadAll()
    {
        var candidates = new[] { _historyFile }
            .Concat(Enumerable.Range(1, BackupCount).Select(BackupPath));

        foreach (var candidate in candidates)
        {
            if (!TryDeserialize(candidate, out var data) || data == null)
                continue;

            var tags = data.Tags ?? new List<string>();
            var result = new List<ClipboardItem>();
            foreach (var dto in data.Items ?? new List<ClipboardItemDto>())
            {
                var item = FromDto(dto);
                if (item != null)
                    result.Add(item);
            }

            var recycleBin = new List<RecycleBinEntry>();
            foreach (var dto in data.RecycleBin ?? new List<RecycleBinEntryDto>())
            {
                var item = FromDto(dto.Item);
                if (item != null)
                    recycleBin.Add(new RecycleBinEntry { Item = item, DeletedAt = dto.DeletedAt });
            }

            if (!string.Equals(candidate, _historyFile, StringComparison.OrdinalIgnoreCase))
            {
                Helpers.MessageWindow.Log($"[Persistence] Recovered history from {Path.GetFileName(candidate)}");
            }

            return (result, tags, recycleBin);
        }

        return (new List<ClipboardItem>(), new List<string>(), new List<RecycleBinEntry>());
    }

    private static bool TryDeserialize(string path, out ClipboardHistoryData? data)
    {
        data = null;
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            data = JsonSerializer.Deserialize<ClipboardHistoryData>(stream, JsonOptions);
            return data != null;
        }
        catch (Exception ex)
        {
            Helpers.MessageWindow.Log($"[Persistence] Invalid {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    private string BackupPath(int index) => $"{_historyFile}.bak{index}";

    private void RotateBackups()
    {
        var oldest = BackupPath(BackupCount);
        if (File.Exists(oldest)) File.Delete(oldest);

        for (var i = BackupCount - 1; i >= 1; i--)
        {
            var source = BackupPath(i);
            if (File.Exists(source)) File.Move(source, BackupPath(i + 1));
        }

        File.Copy(_historyFile, BackupPath(1), overwrite: true);
    }

    /// <summary>
    /// 将 ClipboardItem 转为可序列化 DTO
    /// </summary>
    private ClipboardItemDto ToDto(ClipboardItem item)
    {
        var dto = new ClipboardItemDto
        {
            Id = item.Id,
            Type = item.Type,
            Text = item.Text,
            FilePaths = item.FilePaths?.ToList(),
            CapturedAt = item.CapturedAt,
            IsPinned = item.IsPinned,
            Tag = item.Tag,
            Tags = item.Tags.ToList(),
            SmartType = item.SmartType
        };

        if (ShouldStoreExternally(item.Text))
        {
            dto.ContentPath = SaveTextContent(item.Id, item.Text!);
            dto.TextLengthBytes = Encoding.UTF8.GetByteCount(item.Text!);
            dto.Text = null;
        }

        // 任何类型只要有 Image 就保存为 PNG（支持 Image/Rtf/Html/Files 带图片预览）
        if (item.Image != null)
        {
            dto.ImagePath = SaveImage(item.Image, item.Id);
        }

        return dto;
    }

    /// <summary>
    /// 从 DTO 恢复 ClipboardItem
    /// </summary>
    private ClipboardItem? FromDto(ClipboardItemDto dto)
    {
        var actualType = dto.Type;
        BitmapSource? image = null;

        // 统一加载图片：从 ImagePath 加载（适用于所有类型）
        if (!string.IsNullOrEmpty(dto.ImagePath))
        {
            var fullPath = Path.Combine(_imagesDir, dto.ImagePath);
            if (File.Exists(fullPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image = bitmap;
                }
                catch
                {
                    // 图片损坏，Image 类型降级为 Text
                    if (actualType == ClipboardItemType.Image)
                        actualType = ClipboardItemType.Text;
                }
            }
            else
            {
                // ImagePath 文件不存在，Image 类型降级为 Text
                if (actualType == ClipboardItemType.Image)
                    actualType = ClipboardItemType.Text;
            }
        }
        else if (actualType == ClipboardItemType.Image)
        {
            // 纯 Image 类型但无 ImagePath（旧数据），降级为 Text
            actualType = ClipboardItemType.Text;
        }

        // 文件类型：如果是单个图片文件且没有从 ImagePath 加载，尝试从源文件加载缩略图
        if (image == null
            && dto.Type == ClipboardItemType.Files
            && dto.FilePaths != null
            && dto.FilePaths.Count == 1
            && Helpers.ClipboardFormatDetector.IsImageFile(dto.FilePaths[0]))
        {
            try
            {
                var imgPath = dto.FilePaths[0];
                if (File.Exists(imgPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 200;
                    bitmap.StreamSource = new FileStream(imgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image = bitmap;
                }
            }
            catch { }
        }

        var text = LoadTextContent(dto);
        var item = new ClipboardItem
        {
            Id = dto.Id,
            Type = actualType,
            Text = text,
            FilePaths = dto.FilePaths?.ToList().AsReadOnly(),
            CapturedAt = dto.CapturedAt,
            IsPinned = dto.IsPinned,
            Tags = MergeTags(dto.Tags, dto.Tag),
            SmartType = dto.SmartType,
            Image = image
        };

        return item;
    }

    /// <summary>
    /// 保存图片为 PNG 文件
    /// </summary>
    private string SaveImage(BitmapSource image, Guid id)
    {
        EnsureDirectories();

        var fileName = $"{id:N}.png";
        var filePath = Path.Combine(_imagesDir, fileName);

        if (!File.Exists(filePath))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var fs = File.Create(filePath);
            encoder.Save(fs);
        }

        return fileName;
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_imagesDir);
        Directory.CreateDirectory(_contentsDir);
    }

    private static ClipboardItem CloneItem(ClipboardItem item) => new()
    {
        Id = item.Id,
        Type = item.Type,
        Text = item.Text,
        Image = item.Image,
        FilePaths = item.FilePaths?.ToList().AsReadOnly(),
        CapturedAt = item.CapturedAt,
        IsPinned = item.IsPinned,
        Tags = item.Tags.ToList(),
        SmartType = item.SmartType
    };

    private static bool ShouldStoreExternally(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < LargeTextThresholdBytes / 3)
            return false;
        return Encoding.UTF8.GetByteCount(text) >= LargeTextThresholdBytes;
    }

    private string SaveTextContent(Guid id, string text)
    {
        if (_knownContentPaths.TryGetValue(id, out var knownPath)
            && File.Exists(Path.Combine(_contentsDir, knownPath)))
            return knownPath;

        EnsureDirectories();
        // 编辑后的内容使用新版本文件名，避免覆盖仍被 history.json.bak* 引用的旧内容。
        var fileName = $"{id:N}-{DateTime.UtcNow.Ticks}.txt";
        var target = Path.Combine(_contentsDir, fileName);
        var temp = target + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, target, overwrite: true);
        _knownContentPaths[id] = fileName;
        return fileName;
    }

    private string? LoadTextContent(ClipboardItemDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ContentPath))
            return dto.Text;

        try
        {
            var fullPath = ResolveContentPath(dto.ContentPath);
            var text = File.ReadAllText(fullPath, Encoding.UTF8);
            _knownContentPaths[dto.Id] = dto.ContentPath;
            return text;
        }
        catch (Exception ex)
        {
            Helpers.MessageWindow.Log($"[Persistence] Cannot load content {dto.ContentPath}: {ex.Message}");
            return dto.Text;
        }
    }

    private string ResolveContentPath(string relativePath)
    {
        var root = Path.GetFullPath(_contentsDir) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(_contentsDir, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("历史记录包含非法的大文本路径");
        return fullPath;
    }

    private static List<string> MergeTags(IEnumerable<string>? tags, string? legacyTag)
    {
        var result = new List<string>();
        if (tags != null)
            result.AddRange(tags);
        if (!string.IsNullOrWhiteSpace(legacyTag))
            result.Add(legacyTag);

        return result
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 清理所有持久化数据
    /// </summary>
    public void ClearAll()
    {
        try
        {
            if (File.Exists(_historyFile))
                File.Delete(_historyFile);
            if (Directory.Exists(_imagesDir))
                Directory.Delete(_imagesDir, true);
            if (Directory.Exists(_contentsDir))
                Directory.Delete(_contentsDir, true);
        }
        catch { }
    }

    /// <summary>
    /// 清理孤儿图片文件（JSON 中不再引用的 PNG）
    /// </summary>
    /// <param name="validImagePaths">当前历史记录中引用的图片文件名集合</param>
    /// <returns>清理掉的文件数</returns>
    public int CleanupOrphanedImages(HashSet<string> validImagePaths)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(_imagesDir))
                return 0;

            foreach (var file in Directory.EnumerateFiles(_imagesDir, "*.png"))
            {
                var fileName = Path.GetFileName(file);
                if (!validImagePaths.Contains(fileName))
                {
                    File.Delete(file);
                    removed++;
                }
            }
        }
        catch { }
        return removed;
    }
}

public sealed record PersistenceSnapshot(
    List<ClipboardItem> Items,
    List<string> Tags,
    List<RecycleBinEntry> RecycleBin);
