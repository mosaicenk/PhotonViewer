using System.Collections.Concurrent;
using PhotonViewer.Core.Memory;
using PhotonViewer.Models;

namespace PhotonViewer.Core.Services;

/// <summary>
/// LRU cache service with intelligent prefetching.
/// Maintains decoded images in memory for instant navigation.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Gets an image from cache, or null if not cached.
    /// </summary>
    DecodedImage? Get(string key);

    /// <summary>
    /// Adds an image to cache.
    /// </summary>
    void Put(string key, DecodedImage image);

    /// <summary>
    /// Removes an image from cache.
    /// </summary>
    bool Remove(string key);

    /// <summary>
    /// Clears the entire cache.
    /// </summary>
    void Clear();

    /// <summary>
    /// Initiates prefetch for adjacent images.
    /// </summary>
    Task PrefetchAsync(IReadOnlyList<string> paths, int currentIndex, CancellationToken ct = default);

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    CacheStatistics GetStatistics();
}

public sealed class ImageCacheService : ICacheService, IDisposable
{
    private readonly IImageLoaderService _loader;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache;
    private readonly ReaderWriterLockSlim _evictionLock;
    private readonly LinkedList<string> _lruList;
    
    private readonly long _maxCacheBytes;
    private long _currentCacheBytes;
    
    // Prefetch configuration
    private readonly int _prefetchAhead;
    private readonly int _prefetchBehind;
    private CancellationTokenSource? _prefetchCts;

    // Statistics
    private long _hits;
    private long _misses;

    public ImageCacheService(
        IImageLoaderService loader,
        long maxCacheMegabytes = 512,
        int prefetchAhead = 2,
        int prefetchBehind = 1)
    {
        _loader = loader;
        _maxCacheBytes = maxCacheMegabytes * 1024 * 1024;
        _prefetchAhead = prefetchAhead;
        _prefetchBehind = prefetchBehind;
        
        _cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        _lruList = new LinkedList<string>();
        _evictionLock = new ReaderWriterLockSlim();
    }

    public DecodedImage? Get(string key)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            // Update LRU position
            UpdateLruPosition(key);
            Interlocked.Increment(ref _hits);
            return entry.Image;
        }

        Interlocked.Increment(ref _misses);
        return null;
    }

    public void Put(string key, DecodedImage image)
    {
        var size = image.MemoryFootprintBytes;

        // Evict if necessary to make room
        while (_currentCacheBytes + size > _maxCacheBytes && _lruList.Count > 0)
        {
            EvictOldest();
        }

        var entry = new CacheEntry(image, size);
        
        if (_cache.TryAdd(key, entry))
        {
            AddToLru(key);
            Interlocked.Add(ref _currentCacheBytes, size);
        }
        else
        {
            // Key already exists, update it
            if (_cache.TryGetValue(key, out var existingEntry))
            {
                Interlocked.Add(ref _currentCacheBytes, -existingEntry.SizeBytes);
                existingEntry.Image.Dispose();
            }
            
            _cache[key] = entry;
            UpdateLruPosition(key);
            Interlocked.Add(ref _currentCacheBytes, size);
        }
    }

    public bool Remove(string key)
    {
        if (_cache.TryRemove(key, out var entry))
        {
            RemoveFromLru(key);
            Interlocked.Add(ref _currentCacheBytes, -entry.SizeBytes);
            entry.Image.Dispose();
            return true;
        }
        return false;
    }

    public void Clear()
    {
        _evictionLock.EnterWriteLock();
        try
        {
            foreach (var entry in _cache.Values)
            {
                entry.Image.Dispose();
            }
            
            _cache.Clear();
            _lruList.Clear();
            _currentCacheBytes = 0;
        }
        finally
        {
            _evictionLock.ExitWriteLock();
        }

        // Trigger GC cleanup
        ImageMemoryPool.TriggerCleanup();
    }

    public async Task PrefetchAsync(
        IReadOnlyList<string> paths, 
        int currentIndex, 
        CancellationToken ct = default)
    {
        // Cancel any existing prefetch
        _prefetchCts?.Cancel();
        _prefetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _prefetchCts.Token;

        var prefetchTasks = new List<Task>();

        // Prefetch ahead (higher priority)
        for (int i = 1; i <= _prefetchAhead && currentIndex + i < paths.Count; i++)
        {
            var path = paths[currentIndex + i];
            if (!_cache.ContainsKey(path))
            {
                prefetchTasks.Add(PrefetchSingleAsync(path, token));
            }
        }

        // Prefetch behind (lower priority)
        for (int i = 1; i <= _prefetchBehind && currentIndex - i >= 0; i++)
        {
            var path = paths[currentIndex - i];
            if (!_cache.ContainsKey(path))
            {
                prefetchTasks.Add(PrefetchSingleAsync(path, token));
            }
        }

        // Wait for all prefetch operations
        try
        {
            await Task.WhenAll(prefetchTasks);
        }
        catch (OperationCanceledException)
        {
            // Expected when navigation changes
        }
    }

    private async Task PrefetchSingleAsync(string path, CancellationToken ct)
    {
        try
        {
            // Low-priority load
            await Task.Yield();
            
            var image = await _loader.LoadAsync(path, ct);
            Put(path, image);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception)
        {
            // Prefetch failures are non-critical, ignore
        }
    }

    public CacheStatistics GetStatistics()
    {
        return new CacheStatistics
        {
            ItemCount = _cache.Count,
            CurrentSizeBytes = _currentCacheBytes,
            MaxSizeBytes = _maxCacheBytes,
            Hits = _hits,
            Misses = _misses,
            HitRate = _hits + _misses > 0 
                ? (double)_hits / (_hits + _misses) 
                : 0
        };
    }

    #region LRU Management

    private void AddToLru(string key)
    {
        _evictionLock.EnterWriteLock();
        try
        {
            _lruList.AddLast(key);
        }
        finally
        {
            _evictionLock.ExitWriteLock();
        }
    }

    private void UpdateLruPosition(string key)
    {
        _evictionLock.EnterWriteLock();
        try
        {
            var node = _lruList.Find(key);
            if (node != null)
            {
                _lruList.Remove(node);
                _lruList.AddLast(key);
            }
        }
        finally
        {
            _evictionLock.ExitWriteLock();
        }
    }

    private void RemoveFromLru(string key)
    {
        _evictionLock.EnterWriteLock();
        try
        {
            _lruList.Remove(key);
        }
        finally
        {
            _evictionLock.ExitWriteLock();
        }
    }

    private void EvictOldest()
    {
        _evictionLock.EnterWriteLock();
        try
        {
            if (_lruList.First == null) return;
            
            var keyToEvict = _lruList.First.Value;
            _lruList.RemoveFirst();

            if (_cache.TryRemove(keyToEvict, out var entry))
            {
                Interlocked.Add(ref _currentCacheBytes, -entry.SizeBytes);
                entry.Image.Dispose();
            }
        }
        finally
        {
            _evictionLock.ExitWriteLock();
        }
    }

    #endregion

    public void Dispose()
    {
        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();
        Clear();
        _evictionLock.Dispose();
    }

    private sealed record CacheEntry(DecodedImage Image, long SizeBytes);
}

/// <summary>
/// Cache performance statistics.
/// </summary>
public readonly record struct CacheStatistics
{
    public int ItemCount { get; init; }
    public long CurrentSizeBytes { get; init; }
    public long MaxSizeBytes { get; init; }
    public long Hits { get; init; }
    public long Misses { get; init; }
    public double HitRate { get; init; }
    
    public string CurrentSizeDisplay => FormatBytes(CurrentSizeBytes);
    public string MaxSizeDisplay => FormatBytes(MaxSizeBytes);
    public string HitRateDisplay => $"{HitRate:P1}";
    public double UsagePercent => MaxSizeBytes > 0 
        ? (double)CurrentSizeBytes / MaxSizeBytes * 100 
        : 0;

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
