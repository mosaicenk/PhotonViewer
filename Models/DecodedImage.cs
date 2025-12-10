using System.Buffers;
using SkiaSharp;

namespace PhotonViewer.Models;

/// <summary>
/// Represents a decoded image with GPU-ready bitmap and metadata.
/// Implements IDisposable for proper SKBitmap cleanup.
/// Uses memory pooling for pixel buffers to reduce GC pressure.
/// </summary>
public sealed class DecodedImage : IDisposable
{
    private bool _disposed;
    private byte[]? _pooledBuffer;
    private static readonly ArrayPool<byte> PixelPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// The GPU-ready Skia bitmap for rendering.
    /// </summary>
    public SKBitmap? Bitmap { get; private set; }

    /// <summary>
    /// Original image width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Original image height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// File path or URI of the source image.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSizeBytes { get; }

    /// <summary>
    /// Image format (JPEG, PNG, WebP, HEIC, AVIF, RAW).
    /// </summary>
    public ImageFormat Format { get; }

    /// <summary>
    /// Time taken to decode the image in milliseconds.
    /// </summary>
    public double DecodeTimeMs { get; init; }

    /// <summary>
    /// Memory footprint of the decoded bitmap in bytes.
    /// </summary>
    public long MemoryFootprintBytes => (long)Width * Height * 4; // RGBA

    /// <summary>
    /// Indicates if the image has an alpha channel.
    /// </summary>
    public bool HasAlpha { get; init; }

    /// <summary>
    /// Color depth (bits per channel).
    /// </summary>
    public int BitsPerChannel { get; init; } = 8;

    /// <summary>
    /// EXIF orientation (1-8), 1 = normal.
    /// </summary>
    public int ExifOrientation { get; init; } = 1;

    /// <summary>
    /// Timestamp when the image was loaded.
    /// </summary>
    public DateTime LoadedAt { get; } = DateTime.UtcNow;

    private DecodedImage(
        SKBitmap bitmap,
        string sourcePath,
        long fileSizeBytes,
        ImageFormat format,
        byte[]? pooledBuffer = null)
    {
        Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        Width = bitmap.Width;
        Height = bitmap.Height;
        SourcePath = sourcePath;
        FileSizeBytes = fileSizeBytes;
        Format = format;
        _pooledBuffer = pooledBuffer;
    }

    /// <summary>
    /// Creates a DecodedImage from raw pixel data using pooled memory.
    /// </summary>
    public static DecodedImage CreateFromPixels(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        string sourcePath,
        long fileSizeBytes,
        ImageFormat format,
        SKColorType colorType = SKColorType.Rgba8888)
    {
        // Rent buffer from pool to avoid allocation
        var bufferSize = width * height * 4;
        var pooledBuffer = PixelPool.Rent(bufferSize);

        try
        {
            pixels.CopyTo(pooledBuffer.AsSpan());

            var info = new SKImageInfo(width, height, colorType, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);

            unsafe
            {
                fixed (byte* ptr = pooledBuffer)
                {
                    bitmap.InstallPixels(info, (IntPtr)ptr, info.RowBytes);
                }
            }

            // Create a copy that owns its pixels (so we can return pooled buffer)
            var ownedBitmap = bitmap.Copy();
            bitmap.Dispose();

            // Return pooled buffer immediately after copy
            PixelPool.Return(pooledBuffer);

            return new DecodedImage(ownedBitmap, sourcePath, fileSizeBytes, format);
        }
        catch
        {
            PixelPool.Return(pooledBuffer);
            throw;
        }
    }

    /// <summary>
    /// Creates a DecodedImage from an existing SKBitmap.
    /// Takes ownership of the bitmap.
    /// </summary>
    public static DecodedImage CreateFromBitmap(
        SKBitmap bitmap,
        string sourcePath,
        long fileSizeBytes,
        ImageFormat format)
    {
        return new DecodedImage(bitmap, sourcePath, fileSizeBytes, format);
    }

    /// <summary>
    /// Creates a downsampled version for thumbnail display.
    /// Uses high-quality bicubic resampling.
    /// </summary>
    public DecodedImage CreateThumbnail(int maxDimension)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var scale = Math.Min(
            (float)maxDimension / Width,
            (float)maxDimension / Height);

        var newWidth = (int)(Width * scale);
        var newHeight = (int)(Height * scale);

        var resized = Bitmap!.Resize(
            new SKImageInfo(newWidth, newHeight),
            SKFilterQuality.High);

        return new DecodedImage(resized, SourcePath, FileSizeBytes, Format)
        {
            DecodeTimeMs = 0,
            HasAlpha = HasAlpha,
            BitsPerChannel = BitsPerChannel,
            ExifOrientation = ExifOrientation
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Bitmap?.Dispose();
        Bitmap = null;

        if (_pooledBuffer != null)
        {
            PixelPool.Return(_pooledBuffer);
            _pooledBuffer = null;
        }

        GC.SuppressFinalize(this);
    }

    ~DecodedImage()
    {
        Dispose();
    }
}

/// <summary>
/// Supported image formats.
/// </summary>
public enum ImageFormat
{
    Unknown,
    Jpeg,
    Png,
    Gif,
    Bmp,
    Tiff,
    WebP,
    Heic,
    Avif,
    Raw,
    Svg
}
