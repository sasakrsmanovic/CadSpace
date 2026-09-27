using System.Collections.Immutable;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

var tests = new List<(string, Action)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool c) { if (!c) throw new Exception("Assertion failed"); }
void Near(double a, double b) => Check(Math.Abs(a - b) < 1e-7);
Test("basic DXF entity roundtrip", () =>
{
    var d = Drawing.Empty with { Entities = [new LineEntity(new(1, 2, 3), new(4, 5, 6)), new CircleEntity(new(50, 60), 12), new ArcEntity(default, 5, 40, 240), PolylineEntity.FromPoints(new Vec3[] { new(0, 0), new(20, 0), new(10, 20) }, true), new TextEntity(new(10, 20), "CadSpace Ω", 5)] };
    var r = DxfCodec.Read(DxfCodec.Write(d).Text).Drawing; Check(r.Entities.Length == 5); Near(((LineEntity)r.Entities[0]).End.Z, 6); Near(((CircleEntity)r.Entities[1]).Radius, 12);
});
Test("unchanged DXF text pass through", () => { var text = "0\r\nSECTION\r\n2\r\nENTITIES\r\n0\r\nLINE\r\n5\r\nF\r\n10\r\n1\r\n20\r\n2\r\n11\r\n3\r\n21\r\n4\r\n0\r\nENDSEC\r\n0\r\nEOF\r\n"; var read = DxfCodec.Read(text); Check(DxfCodec.Write(read.Drawing, read.Source).Text == text); });
Test("opaque record survives unrelated edit", () => { var text = "0\nSECTION\n2\nENTITIES\n0\nACAD_PROXY_ENTITY\n5\nEE\n310\nCAFEBABE\n0\nENDSEC\n0\nEOF\n"; var read = DxfCodec.Read(text); var edited = read.Drawing with { Entities = read.Drawing.Entities.Add(new LineEntity(default, new(4, 4))) }; Check(DxfCodec.Write(edited, read.Source).Text.Contains("310\nCAFEBABE")); });
Test("unmodeled sections retained", () => { var text = "0\nSECTION\n2\nOBJECTS\n0\nDICTIONARY\n5\nAB\n3\nPreserveMe\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n"; var r = DxfCodec.Read(text); var output = DxfCodec.Write(r.Drawing with { Entities = [new PointEntity(new(2, 3))] }, r.Source).Text; Check(output.Contains("3\nPreserveMe")); });
Test("nonworld OCS becomes placed analytic circle", () => { var r = DxfCodec.Read("0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n40\n5\n210\n0\n220\n1\n230\n0\n0\nENDSEC\n0\nEOF\n"); Check(r.Drawing.Entities[0] is PlacedEntity { Geometry: CircleEntity }); });
Test("block roundtrip", () => { var s = Drawing.Empty; s = s with { Blocks = s.Blocks.Add("Test", new("Test", new(10, 0), [new CircleEntity(new(10, 0), 8)])), Entities = [new BlockReferenceEntity("Test", new(100, 50), new(2, 2, 2), 30)] }; var r = DxfCodec.Read(DxfCodec.Write(s).Text); Check(r.Drawing.Blocks.ContainsKey("Test")); Check(EntityGeometry.BuildScene(r.Drawing).Paths.Length == 1); });
Test("layer roundtrip", () => { var d = Drawing.Empty; d = d with { Layers = d.Layers.Add("Walls", new("Walls", 0xFF1267AB, false, true, 0.5)) }; var r = DxfCodec.Read(DxfCodec.Write(d).Text).Drawing.Layers["Walls"]; Check(r.Color == 0xFF1267AB && !r.Visible && r.Locked); Near(r.LineWeight, 0.5); });
Test("mesh indexed topology roundtrip", () => { var d = Drawing.Empty with { Entities = [MeshFactory.Box(default, new(10, 20, 30))] }; var w = DxfCodec.Write(d); var r = DxfCodec.Read(w.Text); Check(r.Drawing.Entities.Length == 1 && r.Drawing.Entities[0] is MeshEntity { Triangles.Length: 36 }); });
Test("line command coordinate pipeline", () => { var s = new CadSession(); var c = new CommandEngine(s); c.Submit("L"); c.Submit("10,20"); c.Submit("@30,0"); c.Submit(""); var line = (LineEntity)s.Document.Drawing.Entities[0]; Check(line.Start == new Vec3(10, 20) && line.End == new Vec3(40, 20)); });
Test("circle radius command", () => { var s = new CadSession(); var c = new CommandEngine(s); c.Submit("C"); c.Submit("5,5"); c.Submit("12"); Near(((CircleEntity)s.Document.Drawing.Entities[0]).Radius, 12); });
Test("closed polyline command", () => { var s = new CadSession(); var c = new CommandEngine(s); foreach (var input in new[] { "PL", "0,0", "10,0", "10,10", "C" }) c.Submit(input); Check(s.Document.Drawing.Entities[0] is PolylineEntity { Closed: true }); });
Test("selection move and undo", () => { var s = new CadSession(); s.Add("Test", new LineEntity(default, new(5, 0))); s.SelectAll(); s.TransformSelection("Move", Transform3.Translation(new(5, 5))); Check(((LineEntity)s.Document.Drawing.Entities[0]).Start == new Vec3(5, 5)); s.Document.Undo(); Check(((LineEntity)s.Document.Drawing.Entities[0]).Start == Vec3.Zero); });
Test("locked selection cannot be erased", () => { var d = Drawing.Empty; d = d with { Layers = d.Layers.SetItem("0", new("0", Locked: true)), Entities = [new LineEntity(default, new(5, 0))] }; var s = new CadSession(new(d)); s.SelectAll(); var threw = false; try { s.Erase(); } catch (InvalidOperationException) { threw = true; } Check(threw && s.Document.Drawing.Entities.Length == 1); });
Test("window versus crossing selection", () => { var s = new CadSession(); s.Add("Line", new LineEntity(new(-10, 0), new(10, 0))); s.SelectWindow(new(-1, -1), new(1, 1), false); Check(s.Selection.Count == 0); s.SelectWindow(new(-1, -1), new(1, 1), true); Check(s.Selection.Count == 1); });
Test("endpoint snapping", () => { var s = new CadSession(); s.Add("Line", new LineEntity(new(2, 3), new(10, 3))); var result = s.Snap(new(2.1, 3.1), 0.5); Check(result.Kind == SnapKind.Endpoint && result.Point == new Vec3(2, 3)); });
Test("create insert explode block", () => { var s = new CadSession(); s.Add("Line", new LineEntity(new(10, 10), new(20, 10))); s.SelectAll(); s.CreateBlock("B", new(10, 10)); s.SelectAll(); s.Explode(); Check(s.Document.Drawing.Entities[0] is LineEntity l && l.Start == new Vec3(10, 10)); });
EditingRegression.Register(Test, Check, Near);
var failures = 0;
foreach (var (name, run) in tests) try { run(); Console.WriteLine($"PASS {name}"); } catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
Console.WriteLine($"{tests.Count - failures}/{tests.Count} exchange/engine tests passed.");
return failures == 0 ? 0 : 1;
