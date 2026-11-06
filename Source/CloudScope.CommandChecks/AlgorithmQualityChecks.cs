using CloudScope;
using CloudScope.Forestry;
using CloudScope.Loading;
using CloudScope.Terrain;
using CloudScope.Ui;
using OpenTK.Mathematics;

internal static class AlgorithmQualityChecks
{
    public static void Run(Action<string,bool,string> check)
    {
        var metrics = ClassificationMetrics.Evaluate([true,true,false,false],[0,2,2]);
        check("Quality metrics independently count TP/FP/FN/TN and ignore repeated predictions",metrics == new ClassificationMetrics(1,1,1,1)
            && metrics.Precision == .5 && metrics.Recall == .5 && metrics.F1 == .5,"");
        check("Undefined quality rates remain null",ClassificationMetrics.Evaluate([],[]).Accuracy == null
            && ClassificationMetrics.Evaluate([false],[]).Recall == null,"");
        var plane = (from y in Enumerable.Range(0,5) from x in Enumerable.Range(0,5)
            select new PointData { X=x, Y=y, Z=2*x+3*y+7 }).ToArray();
        var mesh = TerrainTriangulation.Build(plane,new(0,2),(1000,2000,3000));
        var edges = new Dictionary<(int,int),int>(); double area = 0; bool up = true, emptyCircles = true;
        for (int i=0; i<mesh.Indices.Length; i+=3)
        {
            int ia=mesh.Indices[i],ib=mesh.Indices[i+1],ic=mesh.Indices[i+2];
            var a=mesh.Vertices[ia]; var b=mesh.Vertices[ib]; var c=mesh.Vertices[ic];
            double cross=((double)b.X-a.X)*(c.Y-a.Y)-((double)b.Y-a.Y)*(c.X-a.X);
            area+=cross/2; up &= cross>0;
            foreach(var (u,v) in new[] { (ia,ib),(ib,ic),(ic,ia) })
            { var key=u<v?(u,v):(v,u); edges[key]=edges.GetValueOrDefault(key)+1; }
            foreach(var p in mesh.Vertices)
            {
                double ax=a.X-p.X, ay=a.Y-p.Y, bx=b.X-p.X, by=b.Y-p.Y, cx=c.X-p.X, cy=c.Y-p.Y;
                double determinant=(ax*ax+ay*ay)*(bx*cy-by*cx)-(bx*bx+by*by)*(ax*cy-ay*cx)+(cx*cx+cy*cy)*(ax*by-ay*bx);
                emptyCircles &= determinant <= 1e-8;
            }
        }
        check("TIN covers an analytic 4x4 plane exactly with 32 upward triangles",mesh.Indices.Length/3==32 && area==16 && up,"");
        check("TIN is a manifold disk with no duplicate faces or empty-circle violations",edges.Values.All(n=>n is 1 or 2)
            && mesh.Vertices.Length-edges.Count+mesh.Indices.Length/3==1 && emptyCircles,"");
        check("TIN preserves analytic elevations, normals and survey origin",mesh.Vertices.All(p=>p.Z==2*p.X+3*p.Y+7)
            && mesh.Normals.All(n=>Vector3.Dot(n,new Vector3(-2,-3,1).Normalized())>.99999f)
            && mesh.OriginX==1000 && mesh.OriginZ==3000,"");
        var duplicated = TerrainTriangulation.Build([..plane,new() { X=0,Y=0,Z=100 }],new(0,2),(0,0,0));
        check("Duplicate XY sites deterministically retain the lower source elevation",duplicated.Vertices.Length==25 && duplicated.Vertices.Single(p=>p.X==0 && p.Y==0).Z==7,"");
        var islands = new PointData[] { new(){X=0,Y=0},new(){X=1,Y=0},new(){X=0,Y=1},new(){X=1,Y=1},
            new(){X=20,Y=0},new(){X=21,Y=0},new(){X=20,Y=1},new(){X=21,Y=1} };
        var gaps = TerrainTriangulation.Build(islands,new(0,2),(0,0,0));
        check("TIN gap limit does not connect separate survey islands",gaps.Indices.Length/3==4,"");
        bool rejected=false;
        try { TerrainTriangulation.Build([new(){X=0},new(){X=1},new(){X=2}],new(0,5),(0,0,0)); }
        catch(InvalidOperationException) { rejected=true; }
        check("Collinear terrain cannot produce fabricated triangles",rejected,"");
        bool cancelled=false;
        try { TerrainTriangulation.Build(plane,new(0,2),(0,0,0),new CancellationToken(true)); }
        catch(OperationCanceledException) { cancelled=true; }
        check("TIN cancellation precedes triangulation and allocation",cancelled,"");
        var grid=TerrainTriangulation.Build(plane,new(2,5),(0,0,0));
        check("TIN cell sampling has a bounded and explicit source-site count",grid.Vertices.Length==9,"");

        foreach(bool rolling in new[]{false,true})
        {
            var points=new List<PointData>(); var truth=new List<bool>();
            for(int y=0;y<=40;y++) for(int x=0;x<=40;x++)
            {
                float xx=x*.25f, yy=y*.25f;
                float z=.12f*xx-.08f*yy+(rolling?.2f*MathF.Sin(xx*.5f)*MathF.Cos(yy*.5f):0);
                points.Add(new(){X=xx,Y=yy,Z=z}); truth.Add(true);
                if((x+y)%3==0) { points.Add(new(){X=xx,Y=yy,Z=z+2+(x%5)*.2f}); truth.Add(false); }
            }
            var result=GroundSegmentation.Segment(points,new(){ClothResolution=.5f,ClassThreshold=.2f});
            var quality=ClassificationMetrics.Evaluate(truth,result.PointIndices);
            check($"CSF {(rolling?"rolling":"sloping")} reference precision and recall exceed 98%",result.Succeeded && quality.Precision>=.98 && quality.Recall>=.98,quality.ToString());
            var reversed=GroundSegmentation.Segment(points.AsEnumerable().Reverse().ToArray(),new(){ClothResolution=.5f,ClassThreshold=.2f});
            check($"CSF {(rolling?"rolling":"sloping")} reference is invariant under point-order reversal",result.PointIndices.ToHashSet().SetEquals(reversed.PointIndices.Select(i=>points.Count-1-i)),"");
        }

        var forest=new List<PointData>(); var target=new List<bool>();
        for(int tree=0;tree<2;tree++) for(int z=0;z<=60;z++) for(int angle=0;angle<12;angle++)
        {
            float a=angle*MathF.Tau/12;
            forest.Add(new(){X=tree*3+.12f*MathF.Cos(a),Y=.12f*MathF.Sin(a),Z=z*.08f}); target.Add(tree==0);
        }
        var treeResult=TreeSegmentation.Segment(forest,new(.12f,0,1));
        var treeQuality=ClassificationMetrics.Evaluate(target,treeResult.PointIndices);
        check("TREESEG two known trunks have >99% precision/recall",treeResult.Succeeded && treeQuality.Precision>=.99 && treeQuality.Recall>=.99,treeQuality.ToString());

        using var viewer=new ViewerController(1280,720,new HeadlessBackend()); viewer.Load(); viewer.LoadPointCloud(plane,20);
        var commands=new ViewerCommandDispatcher(viewer);
        var command=commands.Execute("TIN All 0 2");
        var timer=System.Diagnostics.Stopwatch.StartNew();
        while(viewer.Status.SurfaceProgress>=0 && timer.Elapsed<TimeSpan.FromSeconds(5))
        { viewer.UpdateFrame(1f/60,new ViewerKeyboardState()); Thread.Sleep(1); }
        check("TIN command publishes a terrain mesh through the shared surface lifecycle",command.Status==CloudScope.Commands.CommandStatus.Ended && viewer.Status.SurfaceTriangles==32,command.Message);
    }
}
