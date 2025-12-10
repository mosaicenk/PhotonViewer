using System.Numerics;
using SkiaSharp;

namespace PhotonViewer.Core.Rendering;

/// <summary>
/// High-performance GPU-accelerated image renderer using SkiaSharp.
/// Handles zoom, pan, rotation with hardware-accelerated transforms.
/// </summary>
public sealed class ImageRenderer : IDisposable
{
    // Current transform state
    private Matrix3x2 _transformMatrix = Matrix3x2.Identity;
    private float _zoom = 1.0f;
    private float _rotation = 0f; // degrees
    private Vector2 _pan = Vector2.Zero;
    private Vector2 _viewportSize;
    
    // Zoom constraints
    public float MinZoom { get; set; } = 0.1f;
    public float MaxZoom { get; set; } = 50.0f;
    public float ZoomStep { get; set; } = 1.2f;
    
    // Current image
    private SKBitmap? _currentBitmap;
    private SKImage? _gpuImage;
    
    // Render quality settings
    public SKFilterQuality FilterQuality { get; set; } = SKFilterQuality.High;
    public bool UseAntialiasing { get; set; } = true;
    public SKColor BackgroundColor { get; set; } = new SKColor(30, 30, 30); // Dark theme

    // Performance metrics
    public double LastFrameTimeMs { get; private set; }
    private readonly System.Diagnostics.Stopwatch _frameTimer = new();

    /// <summary>
    /// Current zoom level (1.0 = 100%).
    /// </summary>
    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, MinZoom, MaxZoom);
            UpdateTransformMatrix();
        }
    }

    /// <summary>
    /// Current rotation in degrees.
    /// </summary>
    public float Rotation
    {
        get => _rotation;
        set
        {
            _rotation = value % 360;
            UpdateTransformMatrix();
        }
    }

    /// <summary>
    /// Current pan offset in viewport coordinates.
    /// </summary>
    public Vector2 Pan
    {
        get => _pan;
        set
        {
            _pan = value;
            UpdateTransformMatrix();
        }
    }

    /// <summary>
    /// Sets the current image for rendering.
    /// </summary>
    public void SetImage(SKBitmap? bitmap)
    {
        _gpuImage?.Dispose();
        _gpuImage = null;
        _currentBitmap = bitmap;

        if (bitmap != null)
        {
            // Create GPU-backed image for faster rendering
            _gpuImage = SKImage.FromBitmap(bitmap);
        }

        ResetView();
    }

    /// <summary>
    /// Updates the viewport size (call on window resize).
    /// </summary>
    public void SetViewportSize(float width, float height)
    {
        _viewportSize = new Vector2(width, height);
        UpdateTransformMatrix();
    }

    /// <summary>
    /// Renders the current image to the canvas.
    /// Called from SKElement.PaintSurface event.
    /// </summary>
    public void Render(SKCanvas canvas)
    {
        _frameTimer.Restart();

        // Clear background
        canvas.Clear(BackgroundColor);

        if (_gpuImage == null || _currentBitmap == null)
        {
            _frameTimer.Stop();
            LastFrameTimeMs = _frameTimer.Elapsed.TotalMilliseconds;
            return;
        }

        canvas.Save();

        // Apply transform matrix
        var skMatrix = ToSkMatrix(_transformMatrix);
        canvas.SetMatrix(skMatrix);

        // Configure paint for high-quality rendering
        using var paint = new SKPaint
        {
            FilterQuality = FilterQuality,
            IsAntialias = UseAntialiasing,
            IsDither = true
        };

        // Draw image centered at origin (transform handles positioning)
        var destRect = new SKRect(
            -_currentBitmap.Width / 2f,
            -_currentBitmap.Height / 2f,
            _currentBitmap.Width / 2f,
            _currentBitmap.Height / 2f);

        canvas.DrawImage(_gpuImage, destRect, paint);

        canvas.Restore();

        _frameTimer.Stop();
        LastFrameTimeMs = _frameTimer.Elapsed.TotalMilliseconds;
    }

    #region Zoom Operations

    /// <summary>
    /// Zooms in by the configured step factor.
    /// </summary>
    public void ZoomIn() => Zoom *= ZoomStep;

    /// <summary>
    /// Zooms out by the configured step factor.
    /// </summary>
    public void ZoomOut() => Zoom /= ZoomStep;

    /// <summary>
    /// Zooms to fit the image within the viewport.
    /// </summary>
    public void ZoomToFit()
    {
        if (_currentBitmap == null || _viewportSize == Vector2.Zero) return;

        var scaleX = _viewportSize.X / _currentBitmap.Width;
        var scaleY = _viewportSize.Y / _currentBitmap.Height;
        
        Zoom = Math.Min(scaleX, scaleY) * 0.95f; // 5% margin
        _pan = Vector2.Zero;
        UpdateTransformMatrix();
    }

    /// <summary>
    /// Zooms to show the image at actual size (100%).
    /// </summary>
    public void ZoomToActualSize()
    {
        Zoom = 1.0f;
        _pan = Vector2.Zero;
        UpdateTransformMatrix();
    }

    /// <summary>
    /// Zooms centered on a specific point (for mouse wheel zoom).
    /// </summary>
    public void ZoomAtPoint(float delta, Vector2 point)
    {
        // Convert point to image coordinates before zoom
        var imagePointBefore = ViewportToImage(point);
        
        // Apply zoom
        var newZoom = _zoom * (delta > 0 ? ZoomStep : 1f / ZoomStep);
        _zoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        
        // Convert same image point to viewport coordinates after zoom
        UpdateTransformMatrix();
        var imagePointAfter = ImageToViewport(imagePointBefore);
        
        // Adjust pan to keep the point stationary
        _pan += point - imagePointAfter;
        UpdateTransformMatrix();
    }

    #endregion

    #region Pan Operations

    /// <summary>
    /// Pans by a delta in viewport coordinates.
    /// </summary>
    public void PanBy(Vector2 delta)
    {
        _pan += delta;
        UpdateTransformMatrix();
    }

    /// <summary>
    /// Centers the image in the viewport.
    /// </summary>
    public void CenterImage()
    {
        _pan = Vector2.Zero;
        UpdateTransformMatrix();
    }

    #endregion

    #region Rotation Operations

    /// <summary>
    /// Rotates by 90 degrees clockwise.
    /// </summary>
    public void RotateClockwise() => Rotation += 90;

    /// <summary>
    /// Rotates by 90 degrees counter-clockwise.
    /// </summary>
    public void RotateCounterClockwise() => Rotation -= 90;

    /// <summary>
    /// Resets rotation to 0 degrees.
    /// </summary>
    public void ResetRotation() => Rotation = 0;

    #endregion

    #region View State

    /// <summary>
    /// Resets the view to fit the current image.
    /// </summary>
    public void ResetView()
    {
        _rotation = 0;
        ZoomToFit();
    }

    /// <summary>
    /// Gets the current view state for serialization.
    /// </summary>
    public ViewState GetViewState() => new(_zoom, _pan, _rotation);

    /// <summary>
    /// Restores a previously saved view state.
    /// </summary>
    public void SetViewState(ViewState state)
    {
        _zoom = Math.Clamp(state.Zoom, MinZoom, MaxZoom);
        _pan = state.Pan;
        _rotation = state.Rotation;
        UpdateTransformMatrix();
    }

    #endregion

    #region Coordinate Transforms

    /// <summary>
    /// Converts viewport coordinates to image coordinates.
    /// </summary>
    public Vector2 ViewportToImage(Vector2 viewportPoint)
    {
        if (!Matrix3x2.Invert(_transformMatrix, out var inverse))
            return Vector2.Zero;

        return Vector2.Transform(viewportPoint, inverse);
    }

    /// <summary>
    /// Converts image coordinates to viewport coordinates.
    /// </summary>
    public Vector2 ImageToViewport(Vector2 imagePoint)
    {
        return Vector2.Transform(imagePoint, _transformMatrix);
    }

    /// <summary>
    /// Gets the visible image bounds in image coordinates.
    /// </summary>
    public SKRect GetVisibleImageBounds()
    {
        var topLeft = ViewportToImage(Vector2.Zero);
        var bottomRight = ViewportToImage(_viewportSize);
        
        return new SKRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    #endregion

    #region Transform Matrix

    private void UpdateTransformMatrix()
    {
        // Build transform: Translate to center -> Rotate -> Scale -> Translate by pan
        var center = _viewportSize / 2;
        
        _transformMatrix =
            Matrix3x2.CreateTranslation(center) *
            Matrix3x2.CreateRotation(_rotation * MathF.PI / 180f) *
            Matrix3x2.CreateScale(_zoom) *
            Matrix3x2.CreateTranslation(_pan);
    }

    private static SKMatrix ToSkMatrix(Matrix3x2 m)
    {
        return new SKMatrix(
            m.M11, m.M21, m.M31,
            m.M12, m.M22, m.M32,
            0, 0, 1);
    }

    #endregion

    public void Dispose()
    {
        _gpuImage?.Dispose();
        _gpuImage = null;
        _currentBitmap = null;
    }
}

/// <summary>
/// Serializable view state for saving/restoring view configuration.
/// </summary>
public readonly record struct ViewState(float Zoom, Vector2 Pan, float Rotation);
