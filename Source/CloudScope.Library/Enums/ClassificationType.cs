namespace CloudScope.Library.Enums
{
    public enum ClassificationType : byte
    {
        Created = 0,
        Unclassified = 1,
        Ground = 2,
        LowVegetation = 3,
        MediumVegetation = 4,
        HighVegetation = 5,
        Building = 6,
        LowPoint = 7,
        Reserved8 = 8,
        Water = 9,
        Rail = 10,
        RoadSurface = 11,
        Reserved12 = 12,
        WireGuard = 13,
        WireConductor = 14,
        TransmissionTower = 15,
        WireStructureConnector = 16,
        BridgeDeck = 17,
        HighNoise = 18,
        OverheadStructure = 19,
        IgnoredGround = 20,
        Snow = 21,
        TemporalExclusion = 22,
        // 23-63 Reserved for ASPRS
        // 64-255 User definable
    }
}

