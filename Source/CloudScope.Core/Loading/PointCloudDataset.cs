using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CloudScope.Loading
{
    public sealed class PointCloudDataset
    {
        private readonly PointData[] _sourcePoints;
        private readonly bool _hasColor;
        private readonly float _colorScale;
        private PointFilter? _filter;
        private ColorSource _colorSource;
        private const int RenderOrderSeed = 42;

        public PointCloudDataset(
            PointData[] sourcePoints,
            int loadedCount,
            float radius,
            bool hasColor,
            float colorScale,
            PointCloudAttributes attributes)
        {
            _sourcePoints = sourcePoints;
            LoadedCount = loadedCount;
            Radius = radius;
            _hasColor = hasColor;
            _colorScale = colorScale;
            Attributes = attributes;
            _colorSource = hasColor ? ColorSource.Rgb : ColorSource.Height;
            ViewPoints = sourcePoints;
            VisibleCount = loadedCount;
            ViewToSource = IdentityMap(loadedCount);
            RenderOrder = BuildProgressiveRenderOrder(loadedCount, RenderOrderSeed);
        }

        public int LoadedCount { get; }
        public int VisibleCount { get; private set; }
        public float Radius { get; }
        public PointCloudAttributes Attributes { get; }
        public PointData[] ViewPoints { get; private set; }

        /// <summary>The full point array in source (LAS record) order. Highlights and label
        /// storage are keyed by source index so they stay valid across filter/color changes.</summary>
        public PointData[] SourcePoints => _sourcePoints;

        /// <summary>Maps a visible (ViewPoints) index to its source index. Identity when unfiltered.</summary>
        public int[] ViewToSource { get; private set; }
        /// <summary>Visible-point draw order. The prefix is an unbiased overview sample for budgeted rendering.</summary>
        public int[]? RenderOrder { get; private set; }
        public string FilterDescription => _filter?.Description ?? "None";
        public ColorSource CurrentColorSource => _colorSource;
        public ColorSource DefaultColorSource => _hasColor ? ColorSource.Rgb : ColorSource.Height;
        public string ColorDescription => _colorSource.ToDisplayName();

        public string ApplyFilter(PointFilter? filter)
        {
            _filter = filter;
            RebuildView();
            string filterText = filter?.Description ?? "None";
            return $"Filter: {filterText}. Visible points: {VisibleCount:N0} / {LoadedCount:N0}.";
        }

        public string SetColorSource(ColorSource source)
        {
            _colorSource = NormalizeColorSource(source);
            RecolorView();
            return $"Color: {_colorSource.ToDisplayName()}.";
        }

        public string ClearColorSource()
        {
            _colorSource = DefaultColorSource;
            RecolorView();
            return $"Color: {_colorSource.ToDisplayName()}.";
        }

        public ColorSource NormalizeColorSource(ColorSource source) =>
            source == ColorSource.Rgb && !_hasColor ? ColorSource.Height : source;

        private void RebuildView()
        {
            if (_filter == null)
            {
                ViewPoints = new PointData[LoadedCount];
                Array.Copy(_sourcePoints, ViewPoints, LoadedCount);
                ApplyColors(ViewPoints, null);
                VisibleCount = ViewPoints.Length;
                ViewToSource = IdentityMap(LoadedCount);
                RenderOrder = BuildProgressiveRenderOrder(VisibleCount, RenderOrderSeed);
                return;
            }

            var points = new List<PointData>(Math.Min(LoadedCount, 1_000_000));
            var map = new List<int>(points.Capacity);
            for (int i = 0; i < LoadedCount; i++)
            {
                if (!_filter.Matches(Attributes, i))
                    continue;

                points.Add(_sourcePoints[i]);
                map.Add(i);
            }

            ViewPoints = points.ToArray();
            ViewToSource = map.ToArray();
            ApplyColors(ViewPoints, _filter);
            VisibleCount = ViewPoints.Length;
            RenderOrder = BuildProgressiveRenderOrder(VisibleCount, RenderOrderSeed);
        }

        private static int[] IdentityMap(int count)
        {
            var map = new int[count];
            for (int i = 0; i < count; i++) map[i] = i;
            return map;
        }

        private static int[]? BuildProgressiveRenderOrder(int count, int seed)
        {
            if (count <= 1)
                return null;

            var order = IdentityMap(count);
            int samplePrefix = ComputeProgressiveSamplePrefix(count);
            var rng = new Random(seed);

            for (int i = 0; i < samplePrefix; i++)
            {
                int j = rng.Next(i, count);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return order;
        }

        private static int ComputeProgressiveSamplePrefix(int count)
        {
            const int MinimumPrefix = 1_000_000;
            const int MaximumPrefix = 5_000_000;
            int ratioPrefix = count / 10;
            return Math.Min(count, Math.Clamp(ratioPrefix, MinimumPrefix, MaximumPrefix));
        }

        private void ApplyColors(PointData[] points, PointFilter? filter)
        {
            for (int sourceIndex = 0, viewIndex = 0; sourceIndex < LoadedCount && viewIndex < points.Length; sourceIndex++)
            {
                if (filter != null && !filter.Matches(Attributes, sourceIndex))
                    continue;

                ApplyColor(ref points[viewIndex], sourceIndex);
                viewIndex++;
            }
        }

        private void ApplyColor(ref PointData point, int index)
        {
            switch (_colorSource)
            {
                case ColorSource.Rgb:
                    CopyRgb(ref point, _sourcePoints[index]);
                    return;
                case ColorSource.Height:
                    SetHeightColor(ref point, Attributes.Z[index]);
                    return;
                case ColorSource.Class:
                    SetClassColor(ref point, Attributes.Class[index]);
                    return;
                case ColorSource.Intensity:
                    SetGradientColor(ref point, Attributes.Intensity[index] / 65535f);
                    return;
                case ColorSource.Return:
                    SetClassColor(ref point, Attributes.ReturnNumber[index]);
                    return;
            }
        }

        private void RecolorView()
        {
            if (ReferenceEquals(ViewPoints, _sourcePoints))
            {
                if (_colorSource == ColorSource.Rgb)
                    return;

                ViewPoints = new PointData[LoadedCount];
                Array.Copy(_sourcePoints, ViewPoints, LoadedCount);
            }

            ApplyColors(ViewPoints, _filter);
        }

        private static void CopyRgb(ref PointData point, PointData source)
        {
            point.R = source.R;
            point.G = source.G;
            point.B = source.B;
        }

        private void SetHeightColor(ref PointData point, double z)
        {
            double span = Attributes.MaxZ - Attributes.MinZ;
            float t = span > 0 ? (float)((z - Attributes.MinZ) / span) : 0.5f;
            t = Math.Clamp(t, 0f, 1f);
            point.R = t;
            point.G = 1f - MathF.Abs(2f * t - 1f);
            point.B = 1f - t;
        }

        private static void SetGradientColor(ref PointData point, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            point.R = t;
            point.G = MathF.Min(1f, 2f * MathF.Min(t, 1f - t));
            point.B = 1f - t;
        }

        private static void SetClassColor(ref PointData point, byte value)
        {
            var c = ClassColorPalette.GetColor(value);
            point.R = c.X;
            point.G = c.Y;
            point.B = c.Z;
        }
    }

    public enum ColorSource
    {
        Rgb,
        Height,
        Class,
        Intensity,
        Return
    }

    public static class ColorSourceExtensions
    {
        public static string ToDisplayName(this ColorSource source) => source switch
        {
            ColorSource.Rgb => "Rgb",
            ColorSource.Height => "Height",
            ColorSource.Class => "Class",
            ColorSource.Intensity => "Intensity",
            ColorSource.Return => "Return",
            _ => source.ToString()
        };
    }

    public abstract record PointFilter(string Description)
    {
        public abstract bool Matches(PointCloudAttributes attributes, int index);
    }

    public sealed record ClassFilter(byte[] Values)
        : PointFilter($"Class IN {string.Join(",", Values.OrderBy(v => v))}")
    {
        private readonly HashSet<byte> _values = Values.ToHashSet();
        public override bool Matches(PointCloudAttributes attributes, int index) => _values.Contains(attributes.Class[index]);
    }

    public sealed record ReturnFilter(byte[] Values)
        : PointFilter($"Return IN {string.Join(",", Values.OrderBy(v => v))}")
    {
        private readonly HashSet<byte> _values = Values.ToHashSet();
        public override bool Matches(PointCloudAttributes attributes, int index) => _values.Contains(attributes.ReturnNumber[index]);
    }

    public sealed record IntensityFilter(ushort Min, ushort Max)
        : PointFilter(string.Create(CultureInfo.InvariantCulture, $"Intensity {Min}..{Max}"))
    {
        public override bool Matches(PointCloudAttributes attributes, int index)
        {
            ushort value = attributes.Intensity[index];
            return value >= Min && value <= Max;
        }
    }

    public sealed record ZFilter(double Min, double Max)
        : PointFilter(string.Create(CultureInfo.InvariantCulture, $"Z {Min:0.###}..{Max:0.###}"))
    {
        public override bool Matches(PointCloudAttributes attributes, int index)
        {
            double value = attributes.Z[index];
            return value >= Min && value <= Max;
        }
    }
}
