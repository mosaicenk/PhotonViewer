using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime;

namespace PhotonViewer.Core.Memory;

/// <summary>
/// High-performance memory pool manager for image processing.
/// Reduces GC pressure through buffer reuse and controlled allocation.
/// </summary>
public sealed class ImageMemoryPool : IDisposable
{
    private static readonly Lazy<ImageMemoryPool> LazyInstance = new(() => new ImageMemoryPool());
    public static ImageMemoryPool Shared => LazyInstance.Value;

    // Tiered pools for different image sizes
    private readonly ArrayPool<byte> _smallPool;   // < 1MB
    private readonly ArrayPool<byte> _mediumPool;  // 1-16MB
    private readonly ArrayPool<byte> _largePool;   // 16-64MB

    // Track allocated buffers for statistics
    private readonly ConcurrentDictionary<int, long> _allocationStats = new();
    
    private long _totalBytesAllocated;
    private long _totalBytesReturned;
    private bool _disposed;

    // Memory pressure thresholds
    private const long HighMemoryThresholdBytes = 512 * 1024 * 1024; // 512MB
    private const long CriticalMemoryThresholdBytes = 1024 * 1024 * 1024; // 1GB

    public ImageMemoryPool()
    {
        // Configure pools with appropriate bucket sizes
        _smallPool = ArrayPool<byte>.Create(1024 * 1024, 50);        // 1MB max, 50 arrays
        _mediumPool = ArrayPool<byte>.Create(16 * 1024 * 1024, 20);  // 16MB max, 20 arrays
        _largePool = ArrayPool<byte>.Create(64 * 1024 * 1024, 5);    // 64MB max, 5 arrays
    }

    /// <summary>
    /// Rents a buffer of at least the specified size.
    /// Returns a buffer from the appropriate tier pool.
    /// </summary>
    public byte[] Rent(int minimumSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // Check memory pressure before allocation
        CheckMemoryPressure();

        var pool = SelectPool(minimumSize);
        var buffer = pool.Rent(minimumSize);

        Interlocked.Add(ref _totalBytesAllocated, buffer.Length);
        _allocationStats.AddOrUpdate(buffer.Length, 1, (_, count) => count + 1);

        return buffer;
    }

    /// <summary>
    /// Returns a rented buffer to the pool.
    /// </summary>
    public void Return(byte[] buffer, bool clearArray = false)
    {
        if (buffer == null || _disposed) return;

        var pool = SelectPool(buffer.Length);
        pool.Return(buffer, clearArray);

        Interlocked.Add(ref _totalBytesReturned, buffer.Length);
    }

    /// <summary>
    /// Creates a pooled memory owner that automatically returns on dispose.
    /// Preferred API for using statements.
    /// </summary>
    public PooledBuffer RentBuffer(int minimumSize)
    {
        return new PooledBuffer(this, Rent(minimumSize));
    }

    /// <summary>
    /// Calculates required buffer size for an image.
    /// </summary>
    public static int CalculateBufferSize(int width, int height, int bytesPerPixel = 4)
    {
        // Add 16-byte alignment padding
        var rowBytes = ((width * bytesPerPixel + 15) / 16) * 16;
        return rowBytes * height;
    }

    /// <summary>
    /// Forces garbage collection if memory pressure is high.
    /// Should be called sparingly, e.g., after closing large images.
    /// </summary>
    public static void TriggerCleanup(bool aggressive = false)
    {
        if (aggressive)
        {
            // Full blocking collection for critical situations
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        }
        else
        {
            // Optimized background collection
            GC.Collect(1, GCCollectionMode.Optimized, false);
        }
    }

    /// <summary>
    /// Gets current memory usage statistics.
    /// </summary>
    public MemoryStatistics GetStatistics()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        
        return new MemoryStatistics
        {
            TotalBytesAllocated = _totalBytesAllocated,
            TotalBytesReturned = _totalBytesReturned,
            NetBytesInUse = _totalBytesAllocated - _totalBytesReturned,
            ProcessMemoryBytes = process.WorkingSet64,
            GcTotalMemory = GC.GetTotalMemory(false),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }

    private ArrayPool<byte> SelectPool(int size)
    {
        return size switch
        {
            <= 1024 * 1024 => _smallPool,
            <= 16 * 1024 * 1024 => _mediumPool,
            _ => _largePool
        };
    }

    private void CheckMemoryPressure()
    {
        var totalMemory = GC.GetTotalMemory(false);

        if (totalMemory > CriticalMemoryThresholdBytes)
        {
            // Critical: aggressive cleanup
            TriggerCleanup(aggressive: true);
        }
        else if (totalMemory > HighMemoryThresholdBytes)
        {
            // High: gentle cleanup
            TriggerCleanup(aggressive: false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        // ArrayPool doesn't need explicit disposal, but we clear our stats
        _allocationStats.Clear();
    }
}

/// <summary>
/// RAII wrapper for pooled buffers. Use with 'using' statements.
/// </summary>
public readonly struct PooledBuffer : IDisposable
{
    private readonly ImageMemoryPool _pool;
    
    public byte[] Array { get; }
    public int Length => Array.Length;
    public Span<byte> Span => Array.AsSpan();
    public Memory<byte> Memory => Array.AsMemory();

    internal PooledBuffer(ImageMemoryPool pool, byte[] array)
    {
        _pool = pool;
        Array = array;
    }

    public void Dispose()
    {
        _pool.Return(Array);
    }
}

/// <summary>
/// Memory usage statistics snapshot.
/// </summary>
public readonly record struct MemoryStatistics
{
    public long TotalBytesAllocated { get; init; }
    public long TotalBytesReturned { get; init; }
    public long NetBytesInUse { get; init; }
    public long ProcessMemoryBytes { get; init; }
    public long GcTotalMemory { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }

    public string ProcessMemoryDisplay => FormatBytes(ProcessMemoryBytes);
    public string GcMemoryDisplay => FormatBytes(GcTotalMemory);
    public string NetInUseDisplay => FormatBytes(NetBytesInUse);

    private static string FormatBytes(long bytes)
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
