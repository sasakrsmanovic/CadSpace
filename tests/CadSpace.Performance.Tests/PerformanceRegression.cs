using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json.Nodes;
using CadSpace.Geometry;
using CadSpace.Model;
using CadSpace.Engine;
using CadSpace.Rendering;
using CadSpace.Dxf;

internal static class PerformanceRegression
{
    public static void Register(Action<string,Action> test)
    {
        void Check(bool c){if(!c)throw new Exception("Regression assertion failed");}
        void Near(double a,double b,double e=1e-7){if(Math.Abs(a-b)>e)throw new Exception($"Expected {b}, got {a}");}
        void Reject(Action a){var failed=false;try{a();}catch(Exception e)when(e is ArgumentException or FormatException or InvalidOperationException){failed=true;}Check(failed);}
        ImmutableArray<Vec3> Square(double z)=>[new(-2,-2,z),new(2,-2,z),new(2,2,z),new(-2,2,z)];
        test("BVH point/range queries equal brute force",()=>
        {
            var random=new Random(42);var boxes=Enumerable.Range(0,5000).Select(_=>{var p=new Vec3(random.NextDouble()*100,random.NextDouble()*100,random.NextDouble()*100);return new Bounds3(p,p+new Vec3(2,3,4));}).ToArray();
            var index=new SpatialIndex(boxes);
            for(var i=0;i<100;i++){var p=new Vec3(random.NextDouble()*100,random.NextDouble()*100,random.NextDouble()*100);var q=new Bounds3(p,p+new Vec3(10,10,10));var hits=new List<int>();index.Query(q,hits);var expected=Enumerable.Range(0,boxes.Length).Where(n=>boxes[n].IntersectsXY(q)&&boxes[n].Max.Z>=q.Min.Z&&boxes[n].Min.Z<=q.Max.Z);Check(hits.Order().SequenceEqual(expected));}
        });
        test("BVH ray query agrees with brute slabs",()=>
        {
            var boxes=Enumerable.Range(0,1000).Select(i=>new Bounds3(new(i*3,0,0),new(i*3+1,1,1))).ToArray();var index=new SpatialIndex(boxes);var ray=new Ray3(new(-5,.5,.5),Vec3.UnitX);var hits=new List<int>();index.Query(ray,hits,20);Check(hits.Order().SequenceEqual(Enumerable.Range(0,1000).Where(i=>SpatialIndex.Intersects(ray,boxes[i],20))));
        });
        test("BVH rejects malformed bounds and handles empty input",()=>{Reject(()=>new SpatialIndex([Bounds3.Empty]));var result=new List<int>();new SpatialIndex([]).Query(new Bounds3(default,new(1,1,1)),result);Check(result.Count==0);});
        test("scene acceleration is identity-scoped after record copy",()=>{var s=new DrawingScene([new ScenePath(Guid.NewGuid(),"0",0xFFFFFFFF,[default,new(1,1)],false)],[],[]);var before=s.Bounds;var copy=s with{Paths=[new ScenePath(Guid.NewGuid(),"0",0xFFFFFFFF,[new(100,100),new(200,200)],false)]};Near(copy.Bounds.Min.X,100);Near(before.Min.X,0);Check(ReferenceEquals(SceneAcceleration.For(s),SceneAcceleration.For(s)));});
        test("incremental scene rebuild reuses unchanged root geometry",()=>
        {
            var d=Drawing.Empty with{Entities=Enumerable.Range(0,1000).Select(i=>(Entity)new CircleEntity(new(i*10,0),3)).ToImmutableArray()};var cache=new DrawingSceneCache();var a=cache.Build(d);var e=(CircleEntity)d.Entities[500];var b=cache.Build(d with{Entities=d.Entities.SetItem(500,e with{Radius=4})});Check(cache.RebuiltRoots==1&&cache.ReusedRoots==999);Check(ReferenceEquals(a.Paths[0],b.Paths[0])&&!ReferenceEquals(a.Paths[500],b.Paths[500]));
        });
        test("scene cache invalidates layer/block dependencies",()=>{var cache=new DrawingSceneCache();var d=Drawing.Empty with{Entities=[new LineEntity(default,new(2,3))]};cache.Build(d);var h=d with{Layers=d.Layers.SetItem("0",new("0",Visible:false))};Check(cache.Build(h).Paths.IsEmpty);Check(cache.Build(d).Paths.Length==1);});
        test("indexed nested block endpoint and midpoint snaps",()=>{var d=Drawing.Empty;d=d with{Blocks=d.Blocks.Add("B",new("B",default,[new LineEntity(default,new(10,0))])),Entities=[new BlockReferenceEntity("B",new(100,200),new(2,2,2),90)]};var s=new CadSession(new(d));var hit=s.Snap(new(100.1,220.1),.5);Check(hit.Kind==SnapKind.Endpoint);Near(hit.Point.DistanceTo(new(100,220)),0);Check(s.Snap(new(100.1,210.1),.5).Kind==SnapKind.Midpoint);});
        test("indexed OCS circle center snap",()=>{var t=Coordinates3D.ObjectCoordinateSystem(new(1,2,3));var e=new PlacedEntity(new CircleEntity(new(4,5),3),t);var s=new CadSession(new(Drawing.Empty with{Entities=[e]}));var center=t.Point(new(4,5));Check(s.Snap(center+new Vec3(.01,.01,.01),.1).Kind==SnapKind.Center);});
        test("line intersection snapping respects WCS elevation",()=>{var s=new CadSession();s.Add("Lines",new LineEntity(default,new(10,10)),new LineEntity(new(0,10),new(10,0)));s.SnapModes=ObjectSnapModes.Intersection;Check(s.Snap(new(5.1,5),.5).Kind==SnapKind.Intersection);});
        test("perpendicular snap and no false endpoint clamp",()=>{var s=new CadSession();s.Add("Line",new LineEntity(default,new(10,0)));s.SnapModes=ObjectSnapModes.Perpendicular;Check(s.Snap(new(3.1,0),.5,new(3,7)).Kind==SnapKind.Perpendicular);Check(s.Snap(new(10,0),.5,new(20,7)).Kind==SnapKind.None);});
        test("analytic tangent snaps to transformed circle",()=>{var s=new CadSession();s.Add("Circle",new CircleEntity(default,5));s.SnapModes=ObjectSnapModes.Tangent;var expected=new Vec3(2.5,Math.Sqrt(18.75));var r=s.Snap(expected+new Vec3(.02,0),.2,new(10,0));Check(r.Kind==SnapKind.Tangent);Near(r.Point.Dot(r.Point-new Vec3(10,0)),0);});
        test("nearest snap includes polyline closing segment",()=>{var s=new CadSession();s.Add("Polyline",PolylineEntity.FromPoints([default,new(10,0),new(10,10),new(0,10)],true));s.SnapModes=ObjectSnapModes.Nearest;var r=s.Snap(new(.1,3),.5);Near(r.Point.X,0);Near(r.Point.Y,3);});
        test("selection stream uploads only changed entity ranges",()=>{var a=Guid.NewGuid();var b=Guid.NewGuid();var stream=new SelectionStream();for(var i=0;i<10000;i++)stream.AddVertex(i<9990?a:b);stream.Allocate();var ranges=stream.Update(new HashSet<Guid>{b});Check(ranges.Count==1&&ranges[0].Count==10);Check(stream.Update(new HashSet<Guid>{b}).Count==0);Check(stream.Update(new HashSet<Guid>()).Single().Count==10);Check(stream.Values.All(v=>v==0));});
        test("straight sweep closes and has expected volume",()=>{var m=MeshSurfaces.Sweep(Square(0),[default,new(0,0,10)]);MeshBoolean.ValidateClosed(m);Near(MeshFactory.SignedVolume(m),160);});
        test("sweep transports frame through a 3D bend",()=>{var m=MeshSurfaces.Sweep(Square(0),[default,new(0,0,20),new(10,0,30),new(20,10,40)]);MeshBoolean.ValidateClosed(m);Check(m.Vertices.Length==16&&MeshFactory.SignedVolume(m)>0);});
        test("sweep rejects reversing paths",()=>Reject(()=>MeshSurfaces.Sweep(Square(0),[default,new(0,0,10),default])));
        test("polygonal loft has expected volume and caps",()=>{var m=MeshSurfaces.Loft([Square(0),Square(5),Square(10)]);Near(MeshFactory.SignedVolume(m),160);MeshBoolean.ValidateClosed(m);});
        test("loft mismatched profiles rejected atomically",()=>{var s=new CadSession();s.Add("profiles",PolylineEntity.FromPoints(Square(0),true),PolylineEntity.FromPoints([new(0,0,10),new(5,0,10),new(0,5,10)],true));s.SelectAll();var before=s.Document.Drawing;Reject(s.LoftSelection);Check(before==s.Document.Drawing);});
        test("3D polyline command supports close and undo",()=>{var s=new CadSession();var c=new CommandEngine(s);foreach(var v in new[]{"3DPOLY","0,0,0","10,0,10","10,10,20","C"})c.Submit(v);Check(s.Document.Drawing.Entities.Single() is Polyline3DEntity{Closed:true});s.Document.Undo();Check(s.Document.Drawing.Entities.IsEmpty);});
        test("control-point spline command is analytic and persistent",()=>{var s=new CadSession();var c=new CommandEngine(s);foreach(var v in new[]{"SPLINE","0,0","10,20","20,20","30,0",""})c.Submit(v);var curve=(SplineEntity)s.Document.Drawing.Entities.Single();Near(AdvancedGeometry.Evaluate(curve,0).X,0);Near(AdvancedGeometry.Evaluate(curve,1).X,30);Check(CadProjectCodec.Read(CadProjectCodec.Write(s.Document.Drawing)).Drawing.Entities.Single() is SplineEntity);});
        test("3D rotation command rotates about arbitrary axis",()=>{var s=new CadSession();s.Add("line",new LineEntity(default,new(0,10,0)));s.SelectAll();var c=new CommandEngine(s);foreach(var v in new[]{"ROTATE3D","0,0,0","1,0,0","90"})c.Submit(v);Near(((LineEntity)s.Document.Drawing.Entities[0]).End.Z,10);});
        test("3D mirror and rigid alignment preserve distances",()=>{var t=AdvancedEditing.MirrorPlane(default,Vec3.UnitX,Vec3.UnitY);Near(t.Point(new(1,2,3)).Z,-3);var align=AdvancedEditing.Align(default,Vec3.UnitX,Vec3.UnitY,new(20,30,40),new(20,31,40),new(20,30,41));Near(align.Point(new(2,3,4)).DistanceTo(align.Point(default)),Math.Sqrt(29));Near(align.Point(default).DistanceTo(new(20,30,40)),0);});
        test("bulge explode preserves circular radius",()=>{var s=new CadSession();s.Add("bulge",new PolylineEntity([new(default,1),new(new(10,0))]));s.SelectAll();s.Explode();var arc=(ArcEntity)s.Document.Drawing.Entities.Single();Near(arc.Radius,5);Near(arc.Center.X,5);});
        test("completion ranks exact aliases and names first",()=>{Check(CommandCatalog.Suggest("c")[0].Name=="CIRCLE");Check(CommandCatalog.Suggest("rotate3d")[0].Name=="ROTATE3D");Check(CommandCatalog.Suggest("  ").Count==0);});
        test("MTEXT radians and final direction precedence",()=>{string Wrap(string data)=>"0\nSECTION\n2\nENTITIES\n0\nMTEXT\n10\n0\n20\n0\n40\n10\n1\nLabel\n"+data+"0\nENDSEC\n0\nEOF\n";var a=DxfCodec.Read(Wrap("50\n1.5707963267948966\n"));Near(EntityGeometry.BuildScene(a.Drawing).Texts.Single().AxisX.Y,1);var b=DxfCodec.Read(Wrap("50\n1.5707963267948966\n11\n1\n21\n0\n31\n0\n"));Near(EntityGeometry.BuildScene(b.Drawing).Texts.Single().AxisX.X,1);var c=DxfCodec.Read(Wrap("11\n1\n21\n0\n31\n0\n11\n0\n21\n1\n31\n0\n"));Near(EntityGeometry.BuildScene(c.Drawing).Texts.Single().AxisX.Y,1);});
        test("native provenance cannot substitute edited geometry for raw DXF",()=>{var r=DxfCodec.Read(DxfCodec.Write(Drawing.Empty with{Entities=[new CircleEntity(default,5)]}).Text);var json=JsonNode.Parse(CadProjectCodec.Write(r.Drawing,r.Source))!;json["sourceGraph"]!["entities"]![0]!["radius"]=50;json["drawing"]!["entities"]![0]!["radius"]=50;Reject(()=>CadProjectCodec.Read(json.ToJsonString()));var units=JsonNode.Parse(CadProjectCodec.Write(r.Drawing,r.Source))!;units["sourceGraph"]!["units"]=1;units["drawing"]!["units"]=1;Reject(()=>CadProjectCodec.Read(units.ToJsonString()));});
        test("100k-object indexed picking correctness and work bound",()=>
        {
            var entities=Enumerable.Range(0,100000).Select(i=>(Entity)new LineEntity(new(i%1000*10,i/1000*10),new(i%1000*10+3,i/1000*10+2))).ToImmutableArray();
            var session=new CadSession(new(Drawing.Empty with{Entities=entities}));var scene=session.Scene;var acceleration=SceneAcceleration.For(scene);_=acceleration.Paths;
            var q=new Bounds3(new(4999,499,-1),new(5005,505,1));var hits=new List<int>();var tested=acceleration.Paths.Query(q,hits,true);Check(tested<128&&hits.Count<=4);
            var points=Enumerable.Range(0,150).Select(i=>new Vec3((i*617%1000)*10+1.5,(i*31%100)*10+1)).ToArray();
            _=session.HitTest(points[0],1);
            var watch=Stopwatch.StartNew();var indexed=points.Select(p=>session.HitTest(p,1)).ToArray();watch.Stop();var indexedMs=watch.Elapsed.TotalMilliseconds;
            watch.Restart();var brute=points.Select(p=>entities.Cast<LineEntity>().Where(e=>GeometryMath.NearestOnSegment(p,e.Start,e.End).DistanceTo(p)<=1).Select(e=>(Guid?)e.Id).LastOrDefault()).ToArray();watch.Stop();Check(indexed.SequenceEqual(brute));
            var message=$"BENCHMARK 100000 lines / 150 picks: indexed={indexedMs:0.###} ms; linear={watch.Elapsed.TotalMilliseconds:0.###} ms; nodes={tested}; matching results=true";
            Console.WriteLine(message);var output=Environment.GetEnvironmentVariable("CADSPACE_BENCHMARK_OUTPUT");if(!string.IsNullOrEmpty(output))File.WriteAllText(output,message+Environment.NewLine);
        });
    }
}
