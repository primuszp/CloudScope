namespace CloudScope.Forestry;

/// <summary>Point-order-independent binary classification counts; undefined rates remain null.</summary>
public readonly record struct ClassificationMetrics(long TruePositive, long FalsePositive, long FalseNegative, long TrueNegative)
{
    public double? Precision => Rate(TruePositive, TruePositive + FalsePositive);
    public double? Recall => Rate(TruePositive, TruePositive + FalseNegative);
    public double? F1 => Rate(2 * TruePositive, 2 * TruePositive + FalsePositive + FalseNegative);
    public double? Accuracy => Rate(TruePositive + TrueNegative, TruePositive + FalsePositive + FalseNegative + TrueNegative);
    private static double? Rate(long numerator, long denominator) => denominator == 0 ? null : (double)numerator / denominator;
    public static ClassificationMetrics Evaluate(IReadOnlyList<bool> reference, IEnumerable<int> predicted)
    {
        var actual = new HashSet<int>();
        foreach (int index in predicted)
        {
            if ((uint)index >= (uint)reference.Count) throw new ArgumentOutOfRangeException(nameof(predicted));
            actual.Add(index);
        }
        long tp = 0, fp = 0, fn = 0, tn = 0;
        for (int i = 0; i < reference.Count; i++)
            if (actual.Contains(i)) { if (reference[i]) tp++; else fp++; }
            else { if (reference[i]) fn++; else tn++; }
        return new(tp, fp, fn, tn);
    }
}
