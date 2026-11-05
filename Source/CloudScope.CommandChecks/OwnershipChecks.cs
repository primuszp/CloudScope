using CloudScope;
using CloudScope.Loading;
using OpenTK.Mathematics;

internal static class OwnershipChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        var a = new ViewerDocument(); var b = new ViewerDocument();
        var data = new PointCloudDataset([new PointData()], 1, 1, false, 1,
            new PointCloudAttributes(new byte[1], new ushort[1], new byte[1], new double[1], 0, 0));
        a.ReplaceResident(data); a.SetSource("scan.xyz");
        check("Document instances own independent sources and layer lists", b.Dataset == null && b.SourcePath == "" && !ReferenceEquals(a.Layers, b.Layers), "");
        a.ReplaceResident(null);
        check("Source replacement cannot retain an old export pathname", a.Dataset == null && a.SourceName == "" && a.SourcePath == "", "");
        var navigation = new ViewNavigation();
        var first = new OrbitCamera(); var second = new OrbitCamera();
        first.FitToCloud(10); second.FitToCloud(10);
        var original = first.SaveState();
        navigation.Remember(first);
        first.OrbitBy(20, 10);
        check("Camera histories are independent of active viewport order", !navigation.Previous(second) && navigation.Previous(first) && first.SaveState() == original, "");
        navigation.Save(first, "Saved camera");
        first.OrbitBy(30, 10);
        navigation.Restore(first, "saved CAMERA");
        first.TickTransition(1);
        check("Named views belong to one workspace and preserve camera state", navigation.NamedViews.Length == 1 && new ViewNavigation().NamedViews.Length == 0 && first.SaveState().Translation == original.Translation, "");
    }
}
