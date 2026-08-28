using CloudScope.Sections;
using OpenTK.Mathematics;

namespace CloudScope.Rendering;

/// <summary>
/// The cross-section guide as polylines rather than a line list, so its corners go through
/// the shared joined-line path and get real round joins instead of two overlapping caps.
/// </summary>
internal static class SectionGuideGeometry
{
    public static Vector4 Color => new(1f, 0.72f, 0.12f, 0.95f);

    public const float WidthPixels = 2f;

    /// <summary>The section rectangle, as a closed loop.</summary>
    public static Vector3[] BuildOutline(SectionDefinition section)
    {
        Vector3 offset = section.Normal * (section.Width * 0.5f);
        return
        [
            section.Start + offset,
            section.End + offset,
            section.End - offset,
            section.Start - offset
        ];
    }

    public static Vector3[] BuildBaseline(SectionDefinition section)
        => [section.Start, section.End];

    /// <summary>The view-direction arrow shaft, from the section centre to the tip.</summary>
    public static Vector3[] BuildArrowShaft(SectionDefinition section)
        => [section.Center, section.Center + section.Normal * ArrowLength(section)];

    /// <summary>Both arrow barbs as one polyline, so they join cleanly at the tip.</summary>
    public static Vector3[] BuildArrowHead(SectionDefinition section)
    {
        float arrowLength = ArrowLength(section);
        Vector3 tip = section.Center + section.Normal * arrowLength;
        Vector3 back = tip - section.Normal * arrowLength * 0.32f;
        Vector3 spread = section.Along * arrowLength * 0.22f;
        return [back + spread, tip, back - spread];
    }

    private static float ArrowLength(SectionDefinition section)
        => MathF.Max(section.Width, section.Length * 0.08f);
}
