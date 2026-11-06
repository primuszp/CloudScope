using OpenTK.Mathematics;

namespace CloudScope.Loading;

/// <summary>A stable double-precision world origin for all local geometry in one document.</summary>
public readonly record struct CoordinateFrame(double X, double Y, double Z)
{
    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Z))
            throw new InvalidDataException("Invalid document coordinate frame.");
    }
    public Vector3 OffsetFrom(CoordinateFrame document)
    {
        Validate(); document.Validate();
        var offset = new Vector3((float)(X - document.X), (float)(Y - document.Y), (float)(Z - document.Z));
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y) || !float.IsFinite(offset.Z))
            throw new InvalidDataException("Layer origin lies outside the document coordinate range.");
        return offset;
    }
}
