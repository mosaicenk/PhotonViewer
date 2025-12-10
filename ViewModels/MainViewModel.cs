using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotonViewer.Core.Memory;
using PhotonViewer.Core.Services;
using PhotonViewer.Models;

namespace PhotonViewer.ViewModels;

/// <summary>
/// Main ViewModel for the image viewer application.
/// Handles image loading, navigation, zoom, and UI state.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IImageLoaderService _loader;
    private readonly ICacheService _cache;
    private CancellationTokenSource? _loadingCts;
    private bool _disposed;

    #region Observable Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(ImageResolutionText))]
    [NotifyPropertyChangedFor(nameof(FileSizeText))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private DecodedImage? _currentImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(NavigationText))]
    private ImageInfo? _currentImageInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomPercentText))]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private double _loadTimeMs;

    [ObservableProperty]
    private double _frameTimeMs;

    [ObservableProperty]
    private bool _showInfoPanel = true;

    [ObservableProperty]
    private bool _showThumbnailStrip = true;

    // File list for navigation
    [ObservableProperty]
    private ObservableCollection<ImageInfo> _imageList = [];

    [ObservableProperty]
    private int _currentIndex = -1;

    // Memory stats
    [ObservableProperty]
    private string _memoryUsageText = "0 MB";

    [ObservableProperty]
    private string _cacheUsageText = "0 / 512 MB";

    #endregion

    #region Computed Properties

    public bool HasImage => CurrentImage != null;
    
    public string WindowTitle => CurrentImageInfo != null 
        ? $"PhotonViewer - {CurrentImageInfo.FileName}" 
        : "PhotonViewer";

    public string ImageResolutionText => CurrentImage != null 
        ? $"{CurrentImage.Width} × {CurrentImage.Height}" 
        : "-";

    public string FileSizeText => CurrentImageInfo?.FileSizeDisplay ?? "-";

    public string ZoomPercentText => $"{ZoomLevel:P0}";

    public string NavigationText => CurrentImageInfo != null 
        ? $"{CurrentImageInfo.Index + 1} / {CurrentImageInfo.TotalInFolder}" 
        : "-";

    public bool CanGoNext => CurrentIndex < ImageList.Count - 1;
    public bool CanGoPrevious => CurrentIndex > 0;

    #endregion

    public MainViewModel(
        IImageLoaderService? loader = null,
        ICacheService? cache = null)
    {
        _loader = loader ?? new ImageLoaderService();
        _cache = cache ?? new ImageCacheService(_loader);
        
        // Start memory monitoring timer
        StartMemoryMonitoring();
    }

    #region Commands

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = _loader.GetFileFilterString(),
            Title = "Open Image"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadImageAsync(dialog.FileName);
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = _loader.GetFileFilterString(),
            Title = "Select an Image in Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadFolderAsync(dialog.FileName);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task PreviousImageAsync()
    {
        if (CurrentIndex > 0)
        {
            await NavigateToIndexAsync(CurrentIndex - 1);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextImageAsync()
    {
        if (CurrentIndex < ImageList.Count - 1)
        {
            await NavigateToIndexAsync(CurrentIndex + 1);
        }
    }

    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = Math.Min(ZoomLevel * 1.2, 50.0);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = Math.Max(ZoomLevel / 1.2, 0.1);
    }

    [RelayCommand]
    private void ZoomToFit()
    {
        // This will be handled by the view binding
        ZoomLevel = 1.0; // Will be recalculated by viewer
    }

    [RelayCommand]
    private void ZoomToActualSize()
    {
        ZoomLevel = 1.0;
    }

    [RelayCommand]
    private void ToggleInfoPanel()
    {
        ShowInfoPanel = !ShowInfoPanel;
    }

    [RelayCommand]
    private void ToggleThumbnailStrip()
    {
        ShowThumbnailStrip = !ShowThumbnailStrip;
    }

    [RelayCommand]
    private async Task DeleteCurrentImageAsync()
    {
        if (CurrentImageInfo == null) return;

        var path = CurrentImageInfo.FilePath;
        var nextIndex = CurrentIndex < ImageList.Count - 1 ? CurrentIndex : CurrentIndex - 1;

        try
        {
            // Remove from cache
            _cache.Remove(path);
            
            // Remove from list
            ImageList.RemoveAt(CurrentIndex);

            // Navigate to next/previous or clear if last
            if (ImageList.Count > 0 && nextIndex >= 0)
            {
                await NavigateToIndexAsync(nextIndex);
            }
            else
            {
                ClearCurrentImage();
            }

            // Delete file (optional - uncomment if desired)
            // Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
            //     path,
            //     Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
            //     Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearCache()
    {
        _cache.Clear();
        UpdateCacheStats();
    }

    #endregion

    #region Navigation

    public async Task LoadImageAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            ErrorMessage = "File not found";
            return;
        }

        await LoadFolderAsync(filePath);
    }

    private async Task LoadFolderAsync(string selectedFile)
    {
        var directory = Path.GetDirectoryName(selectedFile);
        if (string.IsNullOrEmpty(directory)) return;

        // Find all images in folder
        var files = Directory.GetFiles(directory)
            .Where(ImageInfo.IsSupportedFormat)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            ErrorMessage = "No images found in folder";
            return;
        }

        // Build image list
        ImageList.Clear();
        for (int i = 0; i < files.Count; i++)
        {
            ImageList.Add(ImageInfo.FromFile(files[i], i, files.Count));
        }

        // Find selected file index
        var selectedIndex = files.FindIndex(f => 
            f.Equals(selectedFile, StringComparison.OrdinalIgnoreCase));

        await NavigateToIndexAsync(selectedIndex >= 0 ? selectedIndex : 0);
    }

    private async Task NavigateToIndexAsync(int index)
    {
        if (index < 0 || index >= ImageList.Count) return;

        // Cancel any pending load
        _loadingCts?.Cancel();
        _loadingCts = new CancellationTokenSource();
        var ct = _loadingCts.Token;

        CurrentIndex = index;
        CurrentImageInfo = ImageList[index];
        
        NotifyCanExecuteChanged();

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var filePath = CurrentImageInfo.FilePath;

            // Check cache first
            var cached = _cache.Get(filePath);
            if (cached != null)
            {
                CurrentImage = cached;
                LoadTimeMs = 0; // Cached - instant
            }
            else
            {
                // Load from disk
                var image = await _loader.LoadAsync(filePath, ct);
                
                ct.ThrowIfCancellationRequested();
                
                // Cache it
                _cache.Put(filePath, image);
                
                CurrentImage = image;
                LoadTimeMs = image.DecodeTimeMs;
            }

            // Trigger prefetch for adjacent images
            var paths = ImageList.Select(i => i.FilePath).ToList();
            _ = _cache.PrefetchAsync(paths, index, ct);
        }
        catch (OperationCanceledException)
        {
            // Navigation changed, ignore
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Load failed: {ex.Message}";
            CurrentImage = null;
        }
        finally
        {
            IsLoading = false;
            UpdateCacheStats();
        }
    }

    private void ClearCurrentImage()
    {
        CurrentImage = null;
        CurrentImageInfo = null;
        CurrentIndex = -1;
        NotifyCanExecuteChanged();
    }

    private void NotifyCanExecuteChanged()
    {
        PreviousImageCommand.NotifyCanExecuteChanged();
        NextImageCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Memory Monitoring

    private System.Timers.Timer? _memoryTimer;

    private void StartMemoryMonitoring()
    {
        _memoryTimer = new System.Timers.Timer(2000); // Every 2 seconds
        _memoryTimer.Elapsed += (_, _) => UpdateMemoryStats();
        _memoryTimer.Start();
    }

    private void UpdateMemoryStats()
    {
        var stats = ImageMemoryPool.Shared.GetStatistics();
        MemoryUsageText = stats.ProcessMemoryDisplay;
        UpdateCacheStats();
    }

    private void UpdateCacheStats()
    {
        var cacheStats = _cache.GetStatistics();
        CacheUsageText = $"{cacheStats.CurrentSizeDisplay} / {cacheStats.MaxSizeDisplay} ({cacheStats.HitRateDisplay})";
    }

    #endregion

    #region Disposal

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _memoryTimer?.Stop();
        _memoryTimer?.Dispose();
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        
        if (_cache is IDisposable disposableCache)
        {
            disposableCache.Dispose();
        }

        CurrentImage?.Dispose();
        CurrentImage = null;

        GC.SuppressFinalize(this);
    }

    #endregion
}
