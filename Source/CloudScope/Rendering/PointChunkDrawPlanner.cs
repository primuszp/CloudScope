using OpenTK.Mathematics;

namespace CloudScope.Rendering;

internal readonly record struct PointRenderChunk(int Offset, int Count, Vector3 Min, Vector3 Max);
internal readonly record struct PointDrawRange(int First, int Count);

internal static class PointChunkDrawPlanner
{
    public static int FillDrawRanges(
        ReadOnlySpan<PointRenderChunk> chunks,
        ref Matrix4 view,
        ref Matrix4 projection,
        int drawBudget,
        Span<PointDrawRange> destination,
        out int drawnPointCount)
    {
        drawnPointCount = 0;
        if (drawBudget <= 0 || chunks.IsEmpty)
            return 0;

        long visiblePoints = 0;
        for (int i = 0; i < chunks.Length; i++)
            if (IsVisible(chunks[i], ref view, ref projection))
                visiblePoints += chunks[i].Count;

        if (visiblePoints == 0)
            return 0;

        int rangeCount = 0;
        int remainingBudget = Math.Min(drawBudget, (int)Math.Min(visiblePoints, int.MaxValue));
        long remainingVisible = visiblePoints;
        for (int i = 0; i < chunks.Length && remainingBudget > 0; i++)
        {
            PointRenderChunk chunk = chunks[i];
            if (!IsVisible(chunk, ref view, ref projection))
                continue;

            int count = remainingVisible == chunk.Count
                ? remainingBudget
                : Math.Max(1, (int)((long)remainingBudget * chunk.Count / remainingVisible));
            count = Math.Min(count, chunk.Count);
            destination[rangeCount++] = new PointDrawRange(chunk.Offset, count);
            drawnPointCount += count;
            remainingBudget -= count;
            remainingVisible -= chunk.Count;
        }

        return rangeCount;
    }

    private static bool IsVisible(PointRenderChunk chunk, ref Matrix4 view, ref Matrix4 projection)
    {
        bool outsideLeft = true, outsideRight = true;
        bool outsideBottom = true, outsideTop = true;

        for (int corner = 0; corner < 8; corner++)
        {
            var point = new Vector4(
                (corner & 1) == 0 ? chunk.Min.X : chunk.Max.X,
                (corner & 2) == 0 ? chunk.Min.Y : chunk.Max.Y,
                (corner & 4) == 0 ? chunk.Min.Z : chunk.Max.Z,
                1f);
            Vector4 clip = point * view * projection;
            outsideLeft &= clip.X < -clip.W;
            outsideRight &= clip.X > clip.W;
            outsideBottom &= clip.Y < -clip.W;
            outsideTop &= clip.Y > clip.W;
        }

        return !(outsideLeft || outsideRight || outsideBottom || outsideTop);
    }
}
