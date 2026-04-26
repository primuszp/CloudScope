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

        // EgyĂ©b opcionĂˇlis attribĂştumok, amik a Point FormatoktĂłl fĂĽggenek
        public byte ReturnNumber { get; set; }
        public byte NumberOfReturns { get; set; }
        public bool EdgeOfFlightLine { get; set; }
        public sbyte ScanDirectionFlag { get; set; }
        public sbyte ScanAngleRank { get; set; }
        public byte UserData { get; set; }
        public ushort PointSourceId { get; set; }
        public double GpsTime { get; set; }

        public override string ToString()
        {
            return $"X: {X}, Y: {Y}, Z: {Z}, Intensity: {Intensity}, Class: {Classification}";
        }
    }
}


