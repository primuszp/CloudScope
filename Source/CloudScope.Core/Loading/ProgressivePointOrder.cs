namespace CloudScope.Loading;

public readonly record struct ProgressivePointOrder(int Count, int Offset, int Stride)
{
    public int Resolve(int orderedIndex)
    {
        if ((uint)orderedIndex >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(orderedIndex));
        return (int)((Offset + (long)orderedIndex * Stride) % Count);
    }
}
