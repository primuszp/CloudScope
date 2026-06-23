using System.Runtime.InteropServices;

namespace CloudScope.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PointRenderAttributeData
    {
        public PointRenderAttributeData(
            float zNormalized,
            float intensityNormalized,
            float classCode,
            float returnNumber,
            float red,
            float green,
            float blue)
        {
            ZNormalized = zNormalized;
            IntensityNormalized = intensityNormalized;
            ClassCode = classCode;
            ReturnNumber = returnNumber;
            Red = red;
            Green = green;
            Blue = blue;
        }

        public readonly float ZNormalized;
        public readonly float IntensityNormalized;
        public readonly float ClassCode;
        public readonly float ReturnNumber;
        public readonly float Red;
        public readonly float Green;
        public readonly float Blue;
    }
}
