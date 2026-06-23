namespace CloudScope.Labeling
{
    /// <summary>Semantic label plus optional instance identifier assigned to one point.</summary>
    public readonly record struct PointAnnotation(string LabelName, int? InstanceId)
    {
        public string DisplayName => InstanceId is int id
            ? $"{LabelName} #{id}"
            : LabelName;
    }
}
