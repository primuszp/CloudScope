using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using CloudScope;
using CloudScope.Loading;

internal static class NativeWorkflowFixture
{
    /// <summary>A portable, reproducible survey with known terrain and two vertical objects, in LAS and LAZ.</summary>
    public static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var points=new List<(double X,double Y,double Z,byte Class,ushort R,ushort G,ushort B)>();
        for(int y=-40;y<=40;y++) for(int x=-40;x<=40;x++)
            points.Add((x*.25,y*.25,.02*x+.01*y,2,16000,38000,16000));
        for(int tree=0;tree<2;tree++) for(int z=0;z<=40;z++) for(int a=0;a<24;a++)
        {
            double angle=a*Math.Tau/24;
            points.Add(((tree==0?-3:3)+.6*Math.Cos(angle),.6*Math.Sin(angle),z*.2,5,tree==0?(ushort)60000:(ushort)12000,20000,tree==0?(ushort)12000:(ushort)60000));
        }
        const int header=227,stride=34; var bytes=new byte[header+points.Count*stride];
        LasChecks.Fixture(3,2,stride).AsSpan(0,header).CopyTo(bytes);
        Encoding.ASCII.GetBytes("CloudScope native fixture").CopyTo(bytes,26);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(107),(uint)points.Count);
        bytes.AsSpan(111,20).Clear(); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(111),(uint)points.Count);
        for(int axis=0;axis<3;axis++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(131+axis*8),.001);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(155+axis*8),axis==0?1000:axis==1?2000:100);
            var values=points.Select(p=>axis==0?p.X:axis==1?p.Y:p.Z);
            double origin=axis==0?1000:axis==1?2000:100;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(179+axis*16),values.Max()+origin);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(187+axis*16),values.Min()+origin);
        }
        for(int i=0;i<points.Count;i++)
        {
            var p=points[i]; var record=bytes.AsSpan(header+i*stride,stride);
            BinaryPrimitives.WriteInt32LittleEndian(record,(int)Math.Round(p.X*1000));
            BinaryPrimitives.WriteInt32LittleEndian(record[4..],(int)Math.Round(p.Y*1000));
            BinaryPrimitives.WriteInt32LittleEndian(record[8..],(int)Math.Round(p.Z*1000));
            BinaryPrimitives.WriteUInt16LittleEndian(record[12..],1000); record[14]=9; record[15]=p.Class;
            BinaryPrimitives.WriteDoubleLittleEndian(record[20..],i*.01);
            BinaryPrimitives.WriteUInt16LittleEndian(record[28..],p.R); BinaryPrimitives.WriteUInt16LittleEndian(record[30..],p.G); BinaryPrimitives.WriteUInt16LittleEndian(record[32..],p.B);
        }
        string las=Path.Combine(directory,"native-survey.las"),laz=Path.Combine(directory,"native-survey.laz");
        File.WriteAllBytes(las,bytes); LazNative.Transcode(las,laz,points.Count,true);
        File.WriteAllText(Path.Combine(directory,"README.txt"),$"8529 points: 6561 Ground + two 984-point vertical objects.\nWorld offset 1000,2000,100.\nOPEN \"{laz}\" 0\nTIN Ground 0.25 1\n");
        Console.WriteLine($"Wrote {points.Count} native LAS/LAZ workflow points to {directory}.");
    }
}
