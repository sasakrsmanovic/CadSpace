using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json.Nodes;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

internal static class LinetypeRegression
{
    public static void Register(Action<string, Action> test)
    {
        void Check(bool value) { if (!value) throw new Exception("Linetype regression assertion failed."); }
        void Near(double a, double b) { if (Math.Abs(a-b) > 1e-7) throw new Exception($"Expected {b}, got {a}."); }
        void Reject(Action action) { var rejected = false; try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { rejected = true; } Check(rejected); }
        var dash = new Linetype("TEST", "Dash gap dot", [12, -3, 0, -3]);
        Drawing Styled() => Drawing.Empty with { Linetypes = Drawing.Empty.Linetypes.Add(dash.Name, dash) };
        test("simple linetype resolves ByLayer and entity overrides", () => {
            var d = Styled(); d = d with { Layers = d.Layers.SetItem("0", new("0") { Linetype = "TEST" }), Entities = [new LineEntity(default, new(100,0)), new LineEntity(default,new(0,100)) { Linetype = "CONTINUOUS" }] };
            var s=EntityGeometry.BuildScene(d); Check(s.Paths[0].Pattern?.Definition == dash && s.Paths[1].Pattern == null);
        });
        test("case insensitive ByLayer and explicit linetype", () => { var d=Styled() with { Entities=[new LineEntity(default,new(10,0)){Linetype="test"}] }; Check(EntityGeometry.BuildScene(d).Paths[0].Pattern?.Definition == dash); });
        test("ByBlock inherits nested INSERT linetype", () => {
            var d=Styled(); d=d with { Blocks=d.Blocks.Add("B",new("B",default,[new LineEntity(default,new(100,0)){Linetype="BYBLOCK"}])), Entities=[new BlockReferenceEntity("B",default,new(1,1,1)){Linetype="TEST"}] };
            Check(EntityGeometry.BuildScene(d).Paths[0].Pattern?.Definition == dash);
        });
        test("top-level ByBlock is continuous", () => Check(EntityGeometry.BuildScene(Styled() with{Entities=[new LineEntity(default,new(10,0)){Linetype="BYBLOCK"}]}).Paths[0].Pattern==null));
        test("global and per-object scales multiply", () => {
            var s=EntityGeometry.BuildScene(Styled() with{LinetypeScale=2.5,Entities=[new LineEntity(default,new(100,0)){Linetype="TEST",LinetypeScale=2}]}); Near(s.Paths[0].Pattern!.Scale,5); Near(s.Paths[0].Pattern!.Length,90);
        });
        test("pattern ink includes dots and excludes gaps", () => { var p=new StrokePattern(dash,1); Check(p.IsInk(5)&&!p.IsInk(13)&&p.IsInk(15)&&!p.IsInk(16)&&p.IsInk(23)); });
        test("pattern phase supports negative positions", () => { var p=new StrokePattern(dash,1); Check(p.IsInk(-15)&&!p.IsInk(-5)); });
        test("clipped billion-unit path emits only viewport strokes", () => {
            var p=new StrokePattern(dash,1); var path=new ScenePath(Guid.NewGuid(),"0",0xFFFFFFFF,[new(-1e9,0),new(1e9,0)],false){Pattern=p}; var segments=p.VisibleStrokes(path,new(new(-50,-10),new(50,10))).ToArray();
            Check(segments.Length<30&&segments.All(s=>s.Start.X>=-50.000001&&s.End.X<=50.000001));
        });
        test("linetype dots stay point strokes", () => {var p=new StrokePattern(new("DOT","",[0,-5]),1);var path=new ScenePath(Guid.NewGuid(),"0",0xFFFFFFFF,[default,new(20,0)],false);var result=p.VisibleStrokes(path,new(new(-1,-1),new(21,1))).ToArray();Check(result.Length==5&&result.All(s=>s.Dot&&s.Start==s.End));});
        test("pattern phase continues across path segments", () => {
            var p=new StrokePattern(new("D","",[10,-10]),1);var path=new ScenePath(Guid.NewGuid(),"0",0xFFFFFFFF,[default,new(15,0),new(30,0)],false);var result=p.VisibleStrokes(path,new(new(-1,-1),new(31,1))).ToArray();Check(result.Length==2);Near(result[1].Start.X,20);
        });
        test("polyline PLINEGEN flag controls vertex pattern reset", () => {
            var e=PolylineEntity.FromPoints([default,new(15,0),new(15,15)]) with{Linetype="TEST"};var d=Styled() with{Entities=[e]};Check(EntityGeometry.BuildScene(d).Paths.Length==2);Check(EntityGeometry.BuildScene(d with{Entities=[e with{ContinuousLinetype=true}]}).Paths.Length==1);
        });
        test("invalid linetype patterns rejected", () => {Reject(()=>Linetype.Validate(new("bad","",[double.NaN])));Reject(()=>Linetype.Validate(new("bad","",[0,0])));Reject(()=>Linetype.Validate(new("bad/name","",[1,-1])));Reject(()=>Linetype.Validate(new("bad","",Enumerable.Repeat(1.0,65).ToImmutableArray())));});
        test("invalid drawing and entity scale edits are atomic", () => {var s=new CadSession();var before=s.Document.Drawing;Reject(()=>s.Document.Edit("bad",d=>d with{LinetypeScale=0}));Reject(()=>s.Add("bad",new LineEntity(default,new(1,0)){LinetypeScale=double.NaN}));Check(before==s.Document.Drawing);});
        test("DXF LTYPE table and LTSCALE ASCII roundtrip", () => {
            var d=Styled() with{LinetypeScale=2.5,Entities=[new LineEntity(default,new(100,0)){Linetype="TEST",LinetypeScale=.5}]};var r=DxfCodec.Read(DxfCodec.Write(d).Text).Drawing;
            Check(r.Linetypes["TEST"].Elements.SequenceEqual(dash.Elements));Near(r.LinetypeScale,2.5);Near(r.Entities[0].LinetypeScale,.5);Check(r.Entities[0].Linetype=="TEST");
        });
        test("DXF binary linetype and layer mapping roundtrip", () => {
            var d=Styled();d=d with{Layers=d.Layers.SetItem("0",new("0"){Linetype="TEST"}),Entities=[new CircleEntity(default,10)]};var r=DxfBinary.Read(DxfBinary.Write(d,binary:true).Bytes).Drawing;
            Check(r.Layers["0"].Linetype=="TEST"&&r.Linetypes["TEST"].Elements.SequenceEqual(dash.Elements));
        });
        test("DXF PLINEGEN roundtrip", () => {var p=PolylineEntity.FromPoints([default,new(10,0),new(10,10)],true) with{ContinuousLinetype=true};Check(((PolylineEntity)DxfCodec.Read(DxfCodec.Write(Styled() with{Entities=[p]}).Text).Drawing.Entities[0]).ContinuousLinetype);});
        test("native project retains pattern scales and generation flags", () => {
            var d=Styled() with{LinetypeScale=3,Entities=[PolylineEntity.FromPoints([default,new(10,0)]) with{Linetype="TEST",LinetypeScale=4,ContinuousLinetype=true}]};var text=CadProjectCodec.Write(d);Check(CadProjectCodec.Write(CadProjectCodec.Read(text).Drawing)==text);
        });
        test("native provenance rejects altered linetype definitions", () => {
            var r=DxfCodec.Read(DxfCodec.Write(Styled() with{Entities=[new LineEntity(default,new(10,0)){Linetype="TEST"}]}).Text);var json=JsonNode.Parse(CadProjectCodec.Write(r.Drawing,r.Source))!;
            json["sourceGraph"]!["linetypeScale"]=30;Reject(()=>CadProjectCodec.Read(json.ToJsonString()));
        });
        test("untouched linetypes preserve source groups", () => {
            var raw=DxfCodec.Write(Styled()).Text.Replace("3\nDash gap dot\n","3\nDash gap dot\n102\n{CUSTOM\n1\nKeep me\n102\n}\n");var r=DxfCodec.Read(raw);var next=r.Drawing with{Entities=[new LineEntity(default,new(10,0))]};Check(DxfCodec.Write(next,r.Source).Text.Contains("1\nKeep me"));
        });
        test("complex linetype remains source-backed and warns", () => {
            var raw=DxfCodec.Write(Styled()).Text.Replace("49\n12\n74\n0","49\n12\n74\n2");var r=DxfCodec.Read(raw);Check(r.Drawing.Linetypes["TEST"].IsComplex&&r.Warnings.Any(w=>w.Contains("Complex linetype")));Check(DxfCodec.Write(r.Drawing,r.Source).Text==raw);Reject(()=>DxfCodec.Write(r.Drawing));
        });
        test("load built-in then create styled geometry through commands", () => {var s=new CadSession();var c=new CommandEngine(s);foreach(var v in new[]{"CELTYPE","DASHED","CELTSCALE","2","LINE","0,0","100,0","","LTSCALE","3"})c.Submit(v);Check(s.Document.Drawing.Entities.Single().Linetype=="DASHED");Near(s.Scene.Paths.Single().Pattern!.Length,108);});
        test("linetype assignment respects locked selection", () => {var s=new CadSession(new(Styled() with{Entities=[new LineEntity(default,new(10,0))],Layers=Drawing.Empty.Layers.SetItem("0",new("0",Locked:true))}));s.SelectAll();Reject(()=>s.SetSelectedLinetype("TEST",1));});
        test("linetype assignment is one undo transaction", () => {var s=new CadSession(new(Styled() with{Entities=[new LineEntity(default,new(10,0)),new CircleEntity(default,10)]}));var before=s.Document.Drawing;s.SelectAll();s.SetSelectedLinetype("TEST",2);Check(s.Document.Drawing.Entities.All(e=>e.Linetype=="TEST"));s.Document.Undo();Check(s.Document.Drawing==before);});
        test("layer rename changes nested block references atomically", () => {var s=new CadSession();s.AddLayer("OLD");s.CurrentLayer="OLD";s.Add("Line",new LineEntity(default,new(10,0)));s.SelectAll();s.CreateBlock("B",default);var before=s.Document.Drawing;s.UpdateLayer("OLD",new("NEW"));Check(s.Document.Drawing.Blocks["B"].Entities[0].Layer=="NEW"&&s.Document.Drawing.Entities[0].Layer=="NEW");s.Document.Undo();Check(s.Document.Drawing==before);});
        test("layer rename refuses opaque source references", () => {var d=Drawing.Empty;d=d with{Layers=d.Layers.Add("OLD",new("OLD")),Entities=[new OpaqueEntity("UNKNOWN","0\nUNKNOWN\n8\nOLD\n"){Layer="OLD"}]};var s=new CadSession(new(d));Reject(()=>s.UpdateLayer("OLD",new("NEW")));Check(s.Document.Drawing==d);});
        test("used and current layers cannot be deleted", () => {var s=new CadSession();s.AddLayer("USED");s.CurrentLayer="USED";s.Add("Line",new LineEntity(default,new(10,0)));Reject(()=>s.DeleteLayer("USED"));s.CurrentLayer="0";Reject(()=>s.DeleteLayer("USED"));Reject(()=>s.DeleteLayer("0"));});
        test("unused layer deletion and undo", () => {var s=new CadSession();s.AddLayer("EMPTY");s.DeleteLayer("EMPTY");Check(!s.Document.Drawing.Layers.ContainsKey("EMPTY"));s.Document.Undo();Check(s.Document.Drawing.Layers.ContainsKey("EMPTY"));});
        test("layer cache rebuilds only affected roots", () => {
            var d=Styled();d=d with{Layers=d.Layers.Add("A",new("A")).Add("B",new("B")),Entities=[new CircleEntity(default,10){Layer="A"},new CircleEntity(new(20,0),5){Layer="B"}]};var cache=new DrawingSceneCache();var a=cache.Build(d);var b=cache.Build(d with{Layers=d.Layers.SetItem("A",new("A",0xFF00FF00))});Check(cache.RebuiltRoots==1&&cache.ReusedRoots==1&&ReferenceEquals(a.Paths[1],b.Paths[1]));
        });
        test("unrelated block changes retain existing tessellation", () => {
            var d=Styled();d=d with{Blocks=d.Blocks.Add("B",new("B",default,[new CircleEntity(default,10)])),Entities=[new BlockReferenceEntity("B",default,new(1,1,1)),new LineEntity(default,new(10,0))]};var cache=new DrawingSceneCache();cache.Build(d);cache.Build(d with{Blocks=d.Blocks.Add("UNUSED",new("UNUSED",default,[new PointEntity(default)]))});Check(cache.RebuiltRoots==0&&cache.ReusedRoots==2);
        });
        test("nested block dependency changes invalidate the correct root", () => {
            var d=Styled();d=d with{Blocks=d.Blocks.Add("INNER",new("INNER",default,[new CircleEntity(default,10)])).Add("OUTER",new("OUTER",default,[new BlockReferenceEntity("INNER",default,new(1,1,1))])),Entities=[new BlockReferenceEntity("OUTER",default,new(1,1,1)),new LineEntity(default,new(10,0))]};var cache=new DrawingSceneCache();cache.Build(d);cache.Build(d with{Blocks=d.Blocks.SetItem("INNER",new("INNER",default,[new CircleEntity(default,20)]))});Check(cache.RebuiltRoots==1&&cache.ReusedRoots==1);
        });
        test("pattern definition changes invalidate only matching roots", () => {var d=Styled() with{Entities=[new LineEntity(default,new(10,0)){Linetype="TEST"},new LineEntity(default,new(20,0))]};var c=new DrawingSceneCache();c.Build(d);c.Build(d with{Linetypes=d.Linetypes.SetItem("TEST",dash with{Elements=[5,-5]})});Check(c.RebuiltRoots==1&&c.ReusedRoots==1);});
        test("global scale changes reuse continuous geometry", () => {var d=Styled() with{Entities=[new CircleEntity(default,20){Linetype="TEST"},new CircleEntity(default,10)]};var c=new DrawingSceneCache();c.Build(d);c.Build(d with{LinetypeScale=3});Check(c.RebuiltRoots==1&&c.ReusedRoots==1);});
        test("3000-circle layer edit benchmark matches full rebuild", () => {
            var d=Styled();d=d with{Layers=d.Layers.Add("EDIT",new("EDIT")),Entities=Enumerable.Range(0,3000).Select(i=>(Entity)new CircleEntity(new(i*30,0),10){Layer=i==1500?"EDIT":"0"}).ToImmutableArray()};
            var cache=new DrawingSceneCache();cache.Build(d);var changed=d with{Layers=d.Layers.SetItem("EDIT",new("EDIT",0xFF0088FF))};
            var before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();var indexed=cache.Build(changed);watch.Stop();var incremental=watch.Elapsed.TotalMilliseconds;var incrementalBytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(cache.RebuiltRoots==1&&cache.ReusedRoots==2999);
            before=GC.GetAllocatedBytesForCurrentThread();watch.Restart();var full=new DrawingSceneCache().Build(changed);watch.Stop();var fullBytes=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(indexed.Paths.Length==full.Paths.Length&&indexed.Paths.Zip(full.Paths).All(p=>p.First.Color==p.Second.Color&&p.First.Points.SequenceEqual(p.Second.Points)));
            var message=$"BENCHMARK 3000 circles / one layer color edit: dependency={incremental:0.###} ms / {incrementalBytes} bytes; full={watch.Elapsed.TotalMilliseconds:0.###} ms / {fullBytes} bytes; rebuilt=1; reused=2999; matching results=true";
            Console.WriteLine(message);var output=Environment.GetEnvironmentVariable("CADSPACE_BENCHMARK_OUTPUT");if(!string.IsNullOrEmpty(output))File.AppendAllText(output,message+Environment.NewLine);
        });
        test("export linetypes for independent audit", () => {
            var output=Environment.GetEnvironmentVariable("CADSPACE_STYLE_OUTPUT");if(string.IsNullOrEmpty(output))return;Directory.CreateDirectory(output);var d=Styled();d=d with{LinetypeScale=2.5,Layers=d.Layers.SetItem("0",new("0"){Linetype="TEST"}),Entities=[new LineEntity(default,new(100,0)){LinetypeScale=.5},PolylineEntity.FromPoints([default,new(10,0),new(10,10)]) with{ContinuousLinetype=true}]};File.WriteAllText(Path.Combine(output,"styles.dxf"),DxfCodec.Write(d).Text);File.WriteAllBytes(Path.Combine(output,"styles-binary.dxf"),DxfBinary.Write(d,binary:true).Bytes);
        });
    }
}
