namespace CloudScope.Commands;

/// <summary>Checked conversion at the boundary between numeric prompts and float geometry.</summary>
public static class CommandNumber
{
    public static float ToSingle(double value)
    {
        float result = (float)value;
        if (!float.IsFinite(result))
            throw new ArgumentOutOfRangeException(nameof(value), "Value exceeds the finite rendering coordinate range.");
        return result;
    }
}
