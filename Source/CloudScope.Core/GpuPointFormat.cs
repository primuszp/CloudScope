using System;
using System.Runtime.InteropServices;

namespace CloudScope;

/// <summary>
/// A point as it is stored in GPU memory: position at full precision, color quantized to a
/// byte per channel.
/// </summary>
/// <remarks>
/// Sixteen bytes rather than the twenty-four a float color would take. On a cloud of a
/// hundred million points that is 800 MB saved, which is the difference between fitting in
/// GPU memory and not; the color loses nothing visible, since LAS colors arrive as integers
/// and end up on an 8-bit-per-channel display anyway. Positions stay <see cref="float"/>
/// because quantizing them is visible as banding as soon as the camera moves in.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuPointVertex
{
    public const int Stride = 16;

    public GpuPointVertex(float x, float y, float z, float red, float green, float blue)
    {
        X = x;
        Y = y;
        Z = z;
        R = ToByte(red);
        G = ToByte(green);
        B = ToByte(blue);
        A = byte.MaxValue;
    }

    public readonly float X;
    public readonly float Y;
    public readonly float Z;
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;
    public readonly byte A;

    internal static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}

/// <summary>
/// The per-point attributes the shader colors by, quantized to their natural precision.
/// </summary>
/// <remarks>
/// Twelve bytes instead of the twenty-eight the same values take as floats. Height and
/// intensity keep 16 bits, which is the precision LAS itself stores them at; class and
/// return number are single bytes by definition; the source color is the same byte per
/// channel as <see cref="GpuPointVertex"/>. Field order keeps the color four-byte aligned,
/// which some drivers require for a normalized byte attribute.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuPointAttribute
{
    public const int Stride = 12;

    public GpuPointAttribute(
        float zNormalized,
        float intensityNormalized,
        float classCode,
        float returnNumber,
        float red,
        float green,
        float blue)
    {
        ZNormalized = ToUNorm16(zNormalized);
        IntensityNormalized = ToUNorm16(intensityNormalized);
        R = GpuPointVertex.ToByte(red);
        G = GpuPointVertex.ToByte(green);
        B = GpuPointVertex.ToByte(blue);
        A = byte.MaxValue;
        ClassCode = (byte)Math.Clamp((int)classCode, 0, 255);
        ReturnNumber = (byte)Math.Clamp((int)returnNumber, 0, 255);
        Padding = 0;
    }

    public readonly ushort ZNormalized;
    public readonly ushort IntensityNormalized;
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;
    public readonly byte A;
    public readonly byte ClassCode;
    public readonly byte ReturnNumber;
    private readonly ushort Padding;

    private static ushort ToUNorm16(float value) =>
        (ushort)Math.Clamp((int)MathF.Round(value * 65535f), 0, 65535);
}
