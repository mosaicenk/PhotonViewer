using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PhotonViewer.Core.Rendering;
using PhotonViewer.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace PhotonViewer.Views.Controls;

/// <summary>
/// High-performance GPU-accelerated image viewer control.
/// Features: Hardware rendering, smooth zoom/pan, touch support.
/// </summary>
public class SkiaImageViewer : SKElement
{
    private readonly ImageRenderer _renderer;
    private bool _isPanning;
    private Point _lastMousePos;
    private bool _isDisposed;

    #region Dependency Properties

    public static readonly DependencyProperty DecodedImageProperty =
        DependencyProperty.Register(
            nameof(DecodedImage),
            typeof(DecodedImage),
            typeof(SkiaImageViewer),
            new PropertyMetadata(null, OnDecodedImageChanged));

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(
            nameof(ZoomLevel),
            typeof(double),
            typeof(SkiaImageViewer),
            new FrameworkPropertyMetadata(
                1.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnZoomLevelChanged));

    public static readonly DependencyProperty BackgroundColorProperty =
        DependencyProperty.Register(
            nameof(BackgroundColor),
            typeof(Color),
            typeof(SkiaImageViewer),
            new PropertyMetadata(Color.FromRgb(30, 30, 30), OnBackgroundColorChanged));

    public static readonly DependencyProperty RenderQualityProperty =
        DependencyProperty.Register(
            nameof(RenderQuality),
            typeof(RenderQuality),
            typeof(SkiaImageViewer),
            new PropertyMetadata(RenderQuality.High, OnRenderQualityChanged));

    public static readonly DependencyProperty FrameTimeProperty =
        DependencyProperty.Register(
            nameof(FrameTime),
            typeof(double),
            typeof(SkiaImageViewer),
            new PropertyMetadata(0.0));

    /// <summary>
    /// The decoded image to display.
    /// </summary>
    public DecodedImage? DecodedImage
    {
        get => (DecodedImage?)GetValue(DecodedImageProperty);
        set => SetValue(DecodedImageProperty, value);
    }

    /// <summary>
    /// Current zoom level (1.0 = 100%).
    /// </summary>
    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    /// <summary>
    /// Background color of the viewer.
    /// </summary>
    public Color BackgroundColor
    {
        get => (Color)GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
    }

    /// <summary>
    /// Rendering quality setting.
    /// </summary>
    public RenderQuality RenderQuality
    {
        get => (RenderQuality)GetValue(RenderQualityProperty);
        set => SetValue(RenderQualityProperty, value);
    }

    /// <summary>
    /// Last frame render time in milliseconds.
    /// </summary>
    public double FrameTime
    {
        get => (double)GetValue(FrameTimeProperty);
        private set => SetValue(FrameTimeProperty, value);
    }

    #endregion

    #region Routed Events

    public static readonly RoutedEvent ImageLoadedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ImageLoaded),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(SkiaImageViewer));

    public event RoutedEventHandler ImageLoaded
    {
        add => AddHandler(ImageLoadedEvent, value);
        remove => RemoveHandler(ImageLoadedEvent, value);
    }

    #endregion

    public SkiaImageViewer()
    {
        _renderer = new ImageRenderer();
        
        // Enable focus for keyboard input
        Focusable = true;
        
        // Enable clip to bounds
        ClipToBounds = true;
        
        // Hardware acceleration hint
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    #region Lifecycle

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        
        // Subscribe to size changes
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _renderer.SetViewportSize((float)e.NewSize.Width, (float)e.NewSize.Height);
        InvalidateVisual();
    }

    #endregion

    #region Rendering

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        
        var canvas = e.Surface.Canvas;
        
        // Render via our high-performance renderer
        _renderer.Render(canvas);
        
        // Update frame time for performance monitoring
        FrameTime = _renderer.LastFrameTimeMs;
    }

    #endregion

    #region Property Change Handlers

    private static void OnDecodedImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SkiaImageViewer viewer) return;
        
        var newImage = e.NewValue as DecodedImage;
        viewer._renderer.SetImage(newImage?.Bitmap);
        viewer.InvalidateVisual();
        
        if (newImage != null)
        {
            viewer.RaiseEvent(new RoutedEventArgs(ImageLoadedEvent));
        }
    }

    private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SkiaImageViewer viewer) return;
        
        viewer._renderer.Zoom = (float)(double)e.NewValue;
        viewer.InvalidateVisual();
    }

    private static void OnBackgroundColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SkiaImageViewer viewer) return;
        
        var color = (Color)e.NewValue;
        viewer._renderer.BackgroundColor = new SKColor(color.R, color.G, color.B, color.A);
        viewer.InvalidateVisual();
    }

    private static void OnRenderQualityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SkiaImageViewer viewer) return;
        
        viewer._renderer.FilterQuality = (RenderQuality)e.NewValue switch
        {
            RenderQuality.Low => SKFilterQuality.Low,
            RenderQuality.Medium => SKFilterQuality.Medium,
            RenderQuality.High => SKFilterQuality.High,
            _ => SKFilterQuality.High
        };
        viewer.InvalidateVisual();
    }

    #endregion

    #region Mouse Input

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        
        var pos = e.GetPosition(this);
        var point = new Vector2((float)pos.X, (float)pos.Y);
        
        _renderer.ZoomAtPoint(e.Delta, point);
        ZoomLevel = _renderer.Zoom;
        
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        
        _isPanning = true;
        _lastMousePos = e.GetPosition(this);
        CaptureMouse();
        Cursor = Cursors.Hand;
        
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        
        if (!_isPanning) return;
        
        var currentPos = e.GetPosition(this);
        var delta = new Vector2(
            (float)(currentPos.X - _lastMousePos.X),
            (float)(currentPos.Y - _lastMousePos.Y));
        
        _renderer.PanBy(delta);
        _lastMousePos = currentPos;
        
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        
        if (_isPanning)
        {
            _isPanning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        
        if (e.ChangedButton == MouseButton.Left)
        {
            // Toggle between fit and actual size
            if (Math.Abs(_renderer.Zoom - 1.0f) < 0.01f)
            {
                _renderer.ZoomToFit();
            }
            else
            {
                _renderer.ZoomToActualSize();
            }
            
            ZoomLevel = _renderer.Zoom;
            InvalidateVisual();
        }
    }

    #endregion

    #region Keyboard Input

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var handled = true;
        
        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus:
                _renderer.ZoomIn();
                break;
                
            case Key.Subtract:
            case Key.OemMinus:
                _renderer.ZoomOut();
                break;
                
            case Key.D0:
            case Key.NumPad0:
                _renderer.ZoomToFit();
                break;
                
            case Key.D1:
            case Key.NumPad1:
                _renderer.ZoomToActualSize();
                break;
                
            case Key.R when Keyboard.Modifiers == ModifierKeys.Control:
                _renderer.RotateClockwise();
                break;
                
            case Key.R when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                _renderer.RotateCounterClockwise();
                break;
                
            case Key.Home:
                _renderer.ResetView();
                break;
                
            default:
                handled = false;
                break;
        }

        if (handled)
        {
            ZoomLevel = _renderer.Zoom;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Zooms to fit the image in the viewport.
    /// </summary>
    public void ZoomToFit()
    {
        _renderer.ZoomToFit();
        ZoomLevel = _renderer.Zoom;
        InvalidateVisual();
    }

    /// <summary>
    /// Zooms to actual size (100%).
    /// </summary>
    public void ZoomToActualSize()
    {
        _renderer.ZoomToActualSize();
        ZoomLevel = _renderer.Zoom;
        InvalidateVisual();
    }

    /// <summary>
    /// Rotates the image 90 degrees clockwise.
    /// </summary>
    public void RotateClockwise()
    {
        _renderer.RotateClockwise();
        InvalidateVisual();
    }

    /// <summary>
    /// Rotates the image 90 degrees counter-clockwise.
    /// </summary>
    public void RotateCounterClockwise()
    {
        _renderer.RotateCounterClockwise();
        InvalidateVisual();
    }

    /// <summary>
    /// Resets the view to default (fit, no rotation).
    /// </summary>
    public void ResetView()
    {
        _renderer.ResetView();
        ZoomLevel = _renderer.Zoom;
        InvalidateVisual();
    }

    #endregion

    #region Disposal

    ~SkiaImageViewer()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        
        if (disposing)
        {
            _renderer.Dispose();
        }
        
        _isDisposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>
/// Render quality options.
/// </summary>
public enum RenderQuality
{
    Low,
    Medium,
    High
}
