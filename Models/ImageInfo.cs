namespace PhotonViewer.Models;

/// <summary>
/// Lightweight image metadata without full pixel decoding.
/// Used for directory listings and thumbnail virtualization.
/// </summary>
public sealed record ImageInfo
{
    /// <summary>
    /// Full path to the image file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// File name without path.
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// File extension (lowercase, with dot).
    /// </summary>
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Human-readable file size.
    /// </summary>
    public string FileSizeDisplay => FormatFileSize(FileSizeBytes);

    /// <summary>
    /// File creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// File modification timestamp.
    /// </summary>
    public DateTime ModifiedAt { get; init; }

    /// <summary>
    /// Detected image format.
    /// </summary>
    public ImageFormat Format { get; init; }

    /// <summary>
    /// Index in the current folder for navigation.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Total images in the current folder.
    /// </summary>
    public int TotalInFolder { get; init; }

    /// <summary>
    /// Creates ImageInfo from file path with minimal I/O.
    /// </summary>
    public static ImageInfo FromFile(string filePath, int index = 0, int total = 1)
    {
        var fileInfo = new FileInfo(filePath);
        
        return new ImageInfo
        {
            FilePath = filePath,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            CreatedAt = fileInfo.Exists ? fileInfo.CreationTimeUtc : DateTime.MinValue,
            ModifiedAt = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue,
            Format = DetectFormat(filePath),
            Index = index,
            TotalInFolder = total
        };
    }

    /// <summary>
    /// Detects image format from file extension.
    /// </summary>
    public static ImageFormat DetectFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        
        return ext switch
        {
            ".jpg" or ".jpeg" or ".jpe" or ".jfif" => ImageFormat.Jpeg,
            ".png" => ImageFormat.Png,
            ".gif" => ImageFormat.Gif,
            ".bmp" or ".dib" => ImageFormat.Bmp,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            ".webp" => ImageFormat.WebP,
            ".heic" or ".heif" => ImageFormat.Heic,
            ".avif" => ImageFormat.Avif,
            ".svg" or ".svgz" => ImageFormat.Svg,
            ".raw" or ".cr2" or ".cr3" or ".nef" or ".arw" or ".dng" 
                or ".orf" or ".rw2" or ".pef" or ".srw" or ".raf" => ImageFormat.Raw,
            _ => ImageFormat.Unknown
        };
    }

    /// <summary>
    /// Checks if a file extension is a supported image format.
    /// </summary>
    public static bool IsSupportedFormat(string filePath)
    {
        return DetectFormat(filePath) != ImageFormat.Unknown;
    }

    /// <summary>
    /// Gets all supported file extensions.
    /// </summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } = new[]
    {
        ".jpg", ".jpeg", ".jpe", ".jfif",
        ".png", ".gif", ".bmp", ".dib",
        ".tif", ".tiff", ".webp",
        ".heic", ".heif", ".avif",
        ".svg", ".svgz",
        ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng",
        ".orf", ".rw2", ".pef", ".srw", ".raf"
    };

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        int i = 0;
        double size = bytes;
        
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        
        return $"{size:0.##} {suffixes[i]}";
    }
}
