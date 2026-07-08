using System.IO;
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
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipVault");

    private static readonly string ImagesDir = Path.Combine(DataDir, "images");
    private static readonly string HistoryFile = Path.Combine(DataDir, "history.json");
    private const int BackupCount = 5;

    /// <summary>
    /// 图片存储目录（供 CleanupService 使用）
    /// </summary>
    public static string ImageDirectory => ImagesDir;

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
        try
        {
            EnsureDirectories();

            var data = new ClipboardHistoryData
            {
                Items = items.Select(ToDto).ToList(),
                Tags = standaloneTags?.ToList() ?? new List<string>(),
                RecycleBin = recycleBin?.Select(x => new RecycleBinEntryDto
                {
                    Item = ToDto(x.Item),
                    DeletedAt = x.DeletedAt
                }).ToList() ?? new List<RecycleBinEntryDto>()
            };

            // 写入临时文件再重命名，避免写入失败导致文件损坏
            var tempFile = HistoryFile + ".tmp";
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(tempFile, json);

            if (File.Exists(HistoryFile))
            {
                // 只轮转可正常解析的历史文件，避免用损坏文件覆盖有效快照。
                if (TryDeserialize(HistoryFile, out _))
                    RotateBackups();
                File.Delete(HistoryFile);
            }
            File.Move(tempFile, HistoryFile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存失败: {ex.Message}");
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
        var candidates = new[] { HistoryFile }
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

            if (!string.Equals(candidate, HistoryFile, StringComparison.OrdinalIgnoreCase))
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
            data = JsonSerializer.Deserialize<ClipboardHistoryData>(File.ReadAllText(path), JsonOptions);
            return data != null;
        }
        catch (Exception ex)
        {
            Helpers.MessageWindow.Log($"[Persistence] Invalid {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    private static string BackupPath(int index) => $"{HistoryFile}.bak{index}";

    private static void RotateBackups()
    {
        var oldest = BackupPath(BackupCount);
        if (File.Exists(oldest)) File.Delete(oldest);

        for (var i = BackupCount - 1; i >= 1; i--)
        {
            var source = BackupPath(i);
            if (File.Exists(source)) File.Move(source, BackupPath(i + 1));
        }

        File.Copy(HistoryFile, BackupPath(1), overwrite: true);
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
            Tag = item.Tag
        };

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
            var fullPath = Path.Combine(ImagesDir, dto.ImagePath);
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

        var item = new ClipboardItem
        {
            Id = dto.Id,
            Type = actualType,
            Text = dto.Text,
            FilePaths = dto.FilePaths?.ToList().AsReadOnly(),
            CapturedAt = dto.CapturedAt,
            IsPinned = dto.IsPinned,
            Tag = dto.Tag,
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
        var filePath = Path.Combine(ImagesDir, fileName);

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
        if (!Directory.Exists(DataDir))
            Directory.CreateDirectory(DataDir);
        if (!Directory.Exists(ImagesDir))
            Directory.CreateDirectory(ImagesDir);
    }

    /// <summary>
    /// 清理所有持久化数据
    /// </summary>
    public void ClearAll()
    {
        try
        {
            if (File.Exists(HistoryFile))
                File.Delete(HistoryFile);
            if (Directory.Exists(ImagesDir))
                Directory.Delete(ImagesDir, true);
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
            if (!Directory.Exists(ImagesDir))
                return 0;

            foreach (var file in Directory.EnumerateFiles(ImagesDir, "*.png"))
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
