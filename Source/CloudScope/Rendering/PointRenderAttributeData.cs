using System.Runtime.InteropServices;

namespace CloudScope.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PointRenderAttributeData
    {
        public PointRenderAttributeData(float zNormalized, float intensityNormalized, float classCode, float returnNumber)
        {
            ZNormalized = zNormalized;
            IntensityNormalized = intensityNormalized;
            ClassCode = classCode;
            ReturnNumber = returnNumber;
        }

        public readonly float ZNormalized;
        public readonly float IntensityNormalized;
        public readonly float ClassCode;
        public readonly float ReturnNumber;
    }
}
