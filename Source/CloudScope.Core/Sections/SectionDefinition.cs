using System;
using OpenTK.Mathematics;

namespace CloudScope.Sections;

/// <summary>
/// A finite vertical point-cloud cross-section. The picked baseline is the horizontal axis;
/// <see cref="Width"/> is the symmetric capture distance normal to it and Z stays vertical.
/// </summary>
public readonly record struct SectionDefinition(
    int Id,
    string Name,
    Vector3 Start,
    Vector3 End,
    float Width,
    bool Flipped = false)
{
    public Vector3 Along
    {
        get
        {
            Vector3 delta = new(End.X - Start.X, End.Y - Start.Y, 0f);
            return delta.LengthSquared > 1e-10f ? delta.Normalized() : Vector3.UnitX;
        }
    }

    public Vector3 Normal
    {
        get
        {
            Vector3 along = Along;
            Vector3 normal = new(-along.Y, along.X, 0f);
            return Flipped ? -normal : normal;
        }
    }

    public Vector3 Center => new((Start.X + End.X) * 0.5f, (Start.Y + End.Y) * 0.5f,
        (Start.Z + End.Z) * 0.5f);

    public float Length => new Vector2(End.X - Start.X, End.Y - Start.Y).Length;

    public SectionClip ToClip() => new(Center, Along, Normal, Length * 0.5f, Width * 0.5f, true);
}

/// <summary>
/// Renderer-friendly representation of a finite vertical section slab.
/// </summary>
public readonly record struct SectionClip(
    Vector3 Center,
    Vector3 Along,
    Vector3 Normal,
    float HalfLength,
    float HalfWidth,
    bool Enabled)
{
    public static SectionClip None => default;

    public bool Contains(Vector3 point)
    {
        if (!Enabled) return true;
        Vector3 delta = point - Center;
        return MathF.Abs(Vector3.Dot(delta, Along)) <= HalfLength
            && MathF.Abs(Vector3.Dot(delta, Normal)) <= HalfWidth;
    }

    /// <summary>Separating-axis test between this infinite-Z slab and an axis-aligned box.</summary>
    public bool IntersectsAabb(Vector3 min, Vector3 max)
    {
        if (!Enabled) return true;
        Vector3 boxCenter = (min + max) * 0.5f;
        Vector3 half = (max - min) * 0.5f;
        Vector3 delta = boxCenter - Center;
        return IntersectsAxis(delta, half, Along, HalfLength)
            && IntersectsAxis(delta, half, Normal, HalfWidth);
    }

    private static bool IntersectsAxis(Vector3 delta, Vector3 half, Vector3 axis, float sectionHalf)
    {
        float centerDistance = MathF.Abs(Vector3.Dot(delta, axis));
        float boxRadius = MathF.Abs(axis.X) * half.X
            + MathF.Abs(axis.Y) * half.Y
            + MathF.Abs(axis.Z) * half.Z;
        return centerDistance <= sectionHalf + boxRadius;
    }
}

public enum SectionDisplayMode
{
    None,
    PlanGuide,
    Profile
}
