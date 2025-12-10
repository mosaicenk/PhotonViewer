using System.Numerics;
using System.Runtime.CompilerServices;

namespace PhotonViewer.Helpers;

/// <summary>
/// High-performance math helpers using SIMD operations where available.
/// </summary>
public static class MathHelpers
{
    /// <summary>
    /// Clamps a value between min and max.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float value, float min, float max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    /// <summary>
    /// Clamps a Vector2 componentwise.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Clamp(Vector2 value, Vector2 min, Vector2 max)
    {
        return Vector2.Max(min, Vector2.Min(max, value));
    }

    /// <summary>
    /// Linear interpolation between two values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    /// <summary>
    /// Linear interpolation between two vectors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        return Vector2.Lerp(a, b, t);
    }

    /// <summary>
    /// Smooth step interpolation (Hermite).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Smoother step interpolation (Ken Perlin's improved version).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmootherStep(float edge0, float edge1, float x)
    {
        var t = Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DegreesToRadians(float degrees)
    {
        return degrees * MathF.PI / 180f;
    }

    /// <summary>
    /// Converts radians to degrees.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float RadiansToDegrees(float radians)
    {
        return radians * 180f / MathF.PI;
    }

    /// <summary>
    /// Calculates the scale factor to fit a source rectangle within a destination rectangle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CalculateFitScale(float srcWidth, float srcHeight, float dstWidth, float dstHeight)
    {
        var scaleX = dstWidth / srcWidth;
        var scaleY = dstHeight / srcHeight;
        return Math.Min(scaleX, scaleY);
    }

    /// <summary>
    /// Calculates the scale factor to fill a destination rectangle with a source rectangle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CalculateFillScale(float srcWidth, float srcHeight, float dstWidth, float dstHeight)
    {
        var scaleX = dstWidth / srcWidth;
        var scaleY = dstHeight / srcHeight;
        return Math.Max(scaleX, scaleY);
    }

    /// <summary>
    /// Checks if a point is inside a rectangle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPointInRect(Vector2 point, Vector2 rectMin, Vector2 rectMax)
    {
        return point.X >= rectMin.X && point.X <= rectMax.X &&
               point.Y >= rectMin.Y && point.Y <= rectMax.Y;
    }

    /// <summary>
    /// Calculates the distance between two points.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(Vector2 a, Vector2 b)
    {
        return Vector2.Distance(a, b);
    }

    /// <summary>
    /// Calculates the squared distance between two points (faster than Distance).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DistanceSquared(Vector2 a, Vector2 b)
    {
        return Vector2.DistanceSquared(a, b);
    }

    /// <summary>
    /// Normalizes an angle to the range [-180, 180] degrees.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    /// <summary>
    /// Rounds to the nearest multiple of a value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float RoundToMultiple(float value, float multiple)
    {
        return MathF.Round(value / multiple) * multiple;
    }

    /// <summary>
    /// Gets the next power of two greater than or equal to value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int NextPowerOfTwo(int value)
    {
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }
}

/// <summary>
/// Extension methods for Vector2.
/// </summary>
public static class Vector2Extensions
{
    /// <summary>
    /// Rotates a vector by the specified angle in radians.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Rotate(this Vector2 v, float radians)
    {
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return new Vector2(
            v.X * cos - v.Y * sin,
            v.X * sin + v.Y * cos);
    }

    /// <summary>
    /// Returns the perpendicular vector (90° counter-clockwise).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Perpendicular(this Vector2 v)
    {
        return new Vector2(-v.Y, v.X);
    }

    /// <summary>
    /// Gets the angle of the vector in radians.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Angle(this Vector2 v)
    {
        return MathF.Atan2(v.Y, v.X);
    }

    /// <summary>
    /// Creates a unit vector from an angle in radians.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 FromAngle(float radians)
    {
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
    }
}
