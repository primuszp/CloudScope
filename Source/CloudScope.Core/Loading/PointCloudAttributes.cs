using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CloudScope.Library.Enums;

namespace CloudScope.Loading
{
    public sealed class PointCloudAttributes
    {
        public PointCloudAttributes(
            byte[] classes,
            ushort[] intensity,
            byte[] returnNumber,
            double[] z,
            double minZ,
            double maxZ)
        {
            Class = classes;
            Intensity = intensity;
            ReturnNumber = returnNumber;
            Z = z;
            MinZ = minZ;
            MaxZ = maxZ;
        }

        public byte[] Class { get; }
        public ushort[] Intensity { get; }
        public byte[] ReturnNumber { get; }
        public double[] Z { get; }
        public double MinZ { get; }
        public double MaxZ { get; }
        public int Count => Class.Length;

        public string DescribeAll()
        {
            return string.Join(Environment.NewLine, new[]
            {
                DescribeClass(),
                DescribeIntensity(),
                DescribeReturn(),
                DescribeZ()
            });
        }

        public string DescribeClass()
        {
            var counts = CountValues(Class);
            if (counts.Count == 0)
                return "Class: no values.";

            return "Class:" + Environment.NewLine + string.Join(Environment.NewLine,
                counts.Select(kv => $"  {kv.Key} {FormatClassName(kv.Key)}: {kv.Value:N0}"));
        }

        public string DescribeIntensity()
        {
            if (Intensity.Length == 0)
                return "Intensity: no values.";

            ushort min = ushort.MaxValue;
            ushort max = ushort.MinValue;
            foreach (ushort value in Intensity)
            {
                if (value < min) min = value;
                if (value > max) max = value;
            }

            return $"Intensity: min {min}, max {max}";
        }

        public string DescribeReturn()
        {
            var counts = CountValues(ReturnNumber);
            if (counts.Count == 0)
                return "Return: no values.";

            return "Return:" + Environment.NewLine + string.Join(Environment.NewLine,
                counts.Select(kv => $"  {kv.Key}: {kv.Value:N0}"));
        }

        public string DescribeZ()
        {
            return string.Create(CultureInfo.InvariantCulture, $"Z: min {MinZ:0.###}, max {MaxZ:0.###}");
        }

        public static string FormatClassName(byte value)
        {
            return Enum.IsDefined(typeof(ClassificationType), value)
                ? $"({(ClassificationType)value})"
                : "(User)";
        }

        private static SortedDictionary<byte, int> CountValues(byte[] values)
        {
            var counts = new SortedDictionary<byte, int>();
            foreach (byte value in values)
            {
                counts.TryGetValue(value, out int count);
                counts[value] = count + 1;
            }

            return counts;
        }
    }
}
