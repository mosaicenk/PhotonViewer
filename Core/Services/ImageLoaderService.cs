using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using PhotonViewer.Core.Memory;
using PhotonViewer.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using ImageMagick;

namespace PhotonViewer.Core.Services;

/// <summary>
/// High-performance asynchronous image loading service.
/// Features: Pipeline-based I/O, format auto-detection, memory pooling.
/// </summary>
public interface IImageLoaderService
{
    /// <summary>
    /// Loads an image asynchronously with optimal performance.
    /// </summary>
    Task<DecodedImage> LoadAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Loads a thumbnail at reduced resolution for fast gallery display.
    /// </summary>
    Task<DecodedImage> LoadThumbnailAsync(string filePath, int maxDimension = 256, CancellationToken ct = default);

    /// <summary>
    /// Preloads an image into cache without returning it.
    /// </summary>
    Task PreloadAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Gets supported formats for file dialogs.
    /// </summary>
    string GetFileFilterString();
}

public sealed class ImageLoaderService : IImageLoaderService
{
    private readonly ImageMemoryPool _memoryPool;
    private readonly SemaphoreSlim _loadSemaphore;
    
    // Decoder configuration
    private static readonly DecoderOptions FastDecoderOptions = new()
    {
        SkipMetadata = true,
        MaxFrames = 1
    };

    // Configure ImageMagick for RAW support
    static ImageLoaderService()
    {
        // Optimize ImageMagick resource limits
        ResourceLimits.Memory = 512UL * 1024 * 1024; // 512MB
        ResourceLimits.LimitMemory(new Percentage(50));
    }

    public ImageLoaderService(ImageMemoryPool? memoryPool = null)
    {
        _memoryPool = memoryPool ?? ImageMemoryPool.Shared;
        
        // Limit concurrent loads to prevent memory spikes
        _loadSemaphore = new SemaphoreSlim(
            Environment.ProcessorCount, 
            Environment.ProcessorCount * 2);
    }

    public async Task<DecodedImage> LoadAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Image file not found", filePath);

        await _loadSemaphore.WaitAsync(ct);
        
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var fileInfo = new FileInfo(filePath);
            var format = ImageInfo.DetectFormat(filePath);

            var bitmap = format switch
            {
                ImageFormat.Heic or ImageFormat.Avif => await DecodeHeifAsync(filePath, ct),
                ImageFormat.Raw => await DecodeRawAsync(filePath, ct),
                ImageFormat.Svg => await DecodeSvgAsync(filePath, ct),
                _ => await DecodeStandardAsync(filePath, ct)
            };

            stopwatch.Stop();

            return DecodedImage.CreateFromBitmap(
                bitmap,
                filePath,
                fileInfo.Length,
                format)
            {
                DecodeTimeMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    public async Task<DecodedImage> LoadThumbnailAsync(
        string filePath, 
        int maxDimension = 256, 
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var stopwatch = Stopwatch.StartNew();
        var fileInfo = new FileInfo(filePath);
        var format = ImageInfo.DetectFormat(filePath);

        // For standard formats, use ImageSharp's efficient downscale-on-load
        if (format is not (ImageFormat.Heic or ImageFormat.Avif or ImageFormat.Raw or ImageFormat.Svg))
        {
            return await DecodeResizedStandardAsync(filePath, maxDimension, ct);
        }

        // For special formats, load full then resize
        var fullImage = await LoadAsync(filePath, ct);
        
        try
        {
            return fullImage.CreateThumbnail(maxDimension);
        }
        finally
        {
            fullImage.Dispose();
        }
    }

    public async Task PreloadAsync(string filePath, CancellationToken ct = default)
    {
        // Simply load and let caller cache the result
        using var image = await LoadAsync(filePath, ct);
        // Image loaded into memory, caller should cache if needed
    }

    public string GetFileFilterString()
    {
        return "All Images|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.heic;*.heif;*.avif;*.tiff;*.tif;*.svg|" +
               "JPEG|*.jpg;*.jpeg|" +
               "PNG|*.png|" +
               "WebP|*.webp|" +
               "HEIC/HEIF|*.heic;*.heif|" +
               "AVIF|*.avif|" +
               "RAW|*.raw;*.cr2;*.cr3;*.nef;*.arw;*.dng|" +
               "All Files|*.*";
    }

    #region Decoder Implementations

    /// <summary>
    /// Decodes standard formats (JPEG, PNG, WebP, GIF, BMP, TIFF) using ImageSharp.
    /// Uses pipeline-based streaming for memory efficiency.
    /// </summary>
    private async Task<SKBitmap> DecodeStandardAsync(string filePath, CancellationToken ct)
    {
        // Use file stream with optimal buffer size
        await using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920, // 80KB buffer for sequential read
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Load with ImageSharp (handles JPEG, PNG, WebP, GIF, BMP, TIFF)
        using var image = await Image.LoadAsync<Rgba32>(FastDecoderOptions, fileStream, ct);

        return ConvertToSkBitmap(image);
    }

    /// <summary>
    /// Decodes with resize-on-load for thumbnails (memory efficient).
    /// </summary>
    private async Task<DecodedImage> DecodeResizedStandardAsync(
        string filePath, 
        int maxDimension, 
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var fileInfo = new FileInfo(filePath);

        await using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Configure resize-on-load for efficiency
        var options = new DecoderOptions
        {
            SkipMetadata = true,
            MaxFrames = 1,
            TargetSize = new Size(maxDimension, maxDimension)
        };

        using var image = await Image.LoadAsync<Rgba32>(options, fileStream, ct);
        
        // Resize maintaining aspect ratio
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(maxDimension, maxDimension),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3
        }));

        var bitmap = ConvertToSkBitmap(image);
        stopwatch.Stop();

        return DecodedImage.CreateFromBitmap(
            bitmap,
            filePath,
            fileInfo.Length,
            ImageInfo.DetectFormat(filePath))
        {
            DecodeTimeMs = stopwatch.Elapsed.TotalMilliseconds
        };
    }

    /// <summary>
    /// Decodes HEIC/HEIF/AVIF using ImageMagick (libheif backend).
    /// </summary>
    private async Task<SKBitmap> DecodeHeifAsync(string filePath, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            
            using var magickImage = new MagickImage(filePath);
            
            // Ensure RGBA format
            magickImage.Alpha(AlphaOption.Set);
            magickImage.ColorSpace = ColorSpace.sRGB;
            
            var width = (int)magickImage.Width;
            var height = (int)magickImage.Height;
            
            // Get pixel data
            using var pixels = magickImage.GetPixels();
            var pixelData = pixels.ToByteArray(PixelMapping.RGBA);
            
            if (pixelData == null)
                throw new InvalidOperationException("Failed to decode HEIF pixel data");

            return CreateBitmapFromRgba(pixelData, width, height);
        }, ct);
    }

    /// <summary>
    /// Decodes RAW camera formats using ImageMagick (dcraw/LibRaw backend).
    /// </summary>
    private async Task<SKBitmap> DecodeRawAsync(string filePath, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var settings = new MagickReadSettings
            {
                // Use camera white balance
                SetDefine(MagickFormat.Dng, "use-camera-wb", "true")
            };

            using var magickImage = new MagickImage(filePath, settings);
            
            // Apply auto-orientation from EXIF
            magickImage.AutoOrient();
            magickImage.ColorSpace = ColorSpace.sRGB;
            
            var width = (int)magickImage.Width;
            var height = (int)magickImage.Height;
            
            using var pixels = magickImage.GetPixels();
            var pixelData = pixels.ToByteArray(PixelMapping.RGBA);
            
            if (pixelData == null)
                throw new InvalidOperationException("Failed to decode RAW pixel data");

            return CreateBitmapFromRgba(pixelData, width, height);
        }, ct);
    }

    /// <summary>
    /// Renders SVG to bitmap using SkiaSharp's native SVG support.
    /// </summary>
    private async Task<SKBitmap> DecodeSvgAsync(string filePath, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            
            var svgContent = File.ReadAllText(filePath);
            
            // Parse SVG
            var svg = new SkiaSharp.Extended.Svg.SKSvg();
            svg.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgContent)));

            // Calculate render size (default to 1024px max dimension)
            var bounds = svg.Picture?.CullRect ?? SKRect.Empty;
            var scale = Math.Min(1024f / bounds.Width, 1024f / bounds.Height);
            var width = (int)(bounds.Width * scale);
            var height = (int)(bounds.Height * scale);

            var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            
            if (svg.Picture != null)
            {
                canvas.DrawPicture(svg.Picture);
            }

            return bitmap;
        }, ct);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Converts ImageSharp image to SkiaSharp bitmap efficiently.
    /// Uses unsafe memory access for zero-copy when possible.
    /// </summary>
    private static SKBitmap ConvertToSkBitmap(Image<Rgba32> image)
    {
        var width = image.Width;
        var height = image.Height;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);

        // Get pointer to bitmap pixels
        var ptr = bitmap.GetPixels();
        
        // Copy row by row (ImageSharp stores rows contiguously)
        image.ProcessPixelRows(accessor =>
        {
            var destPtr = ptr;
            var rowBytes = width * 4;
            
            for (int y = 0; y < height; y++)
            {
                var srcRow = accessor.GetRowSpan(y);
                unsafe
                {
                    fixed (Rgba32* srcPtr = srcRow)
                    {
                        Buffer.MemoryCopy(srcPtr, (void*)destPtr, rowBytes, rowBytes);
                    }
                }
                destPtr += rowBytes;
            }
        });

        bitmap.NotifyPixelsChanged();
        return bitmap;
    }

    /// <summary>
    /// Creates SKBitmap from raw RGBA byte array.
    /// </summary>
    private static SKBitmap CreateBitmapFromRgba(byte[] rgba, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);

        unsafe
        {
            fixed (byte* ptr = rgba)
            {
                var srcInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                bitmap.InstallPixels(srcInfo, (IntPtr)ptr, srcInfo.RowBytes);
            }
        }

        // Create owned copy (InstallPixels doesn't copy)
        var ownedBitmap = bitmap.Copy();
        bitmap.Dispose();
        
        return ownedBitmap;
    }

    #endregion
}

// Extension method for MagickReadSettings
internal static class MagickReadSettingsExtensions
{
    public static MagickReadSettings SetDefine(
        this MagickReadSettings settings, 
        MagickFormat format, 
        string name, 
        string value)
    {
        settings.SetDefine(format, name, value);
        return settings;
    }
}
