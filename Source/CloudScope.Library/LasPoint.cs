using CloudScope.Library.Enums;

namespace CloudScope.Library
{
    public struct LasPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public ushort Intensity { get; set; }
        public ClassificationType Classification { get; set; }

        public ushort R { get; set; }
        public ushort G { get; set; }
        public ushort B { get; set; }
        public ushort NIR { get; set; }

        // Legacy (formats 0-5)
        public byte ReturnNumber { get; set; }
        public byte NumberOfReturns { get; set; }
        public bool EdgeOfFlightLine { get; set; }
        public sbyte ScanDirectionFlag { get; set; }
        public sbyte ScanAngleRank { get; set; }
        public byte UserData { get; set; }
        public ushort PointSourceId { get; set; }
        public double GpsTime { get; set; }

        // LAS 1.4 (formats 6-10)
        public byte ScannerChannel { get; set; }
        public short ScanAngle { get; set; }
        public bool ClassificationSynthetic { get; set; }
        public bool ClassificationKeyPoint { get; set; }
        public bool ClassificationWithheld { get; set; }
        public bool ClassificationOverlap { get; set; }

        // Waveform fields (formats 4, 5, 9, 10)
        public byte WavePacketDescriptorIndex { get; set; }
        public ulong ByteOffsetToWaveformData { get; set; }
        public uint WaveformPacketSize { get; set; }
        public float ReturnPointWaveformLocation { get; set; }
        public float WaveformParamX { get; set; }
        public float WaveformParamY { get; set; }
        public float WaveformParamZ { get; set; }

        public override string ToString()
        {
            return $"X: {X}, Y: {Y}, Z: {Z}, Intensity: {Intensity}, Class: {Classification}";
        }
    }
}


