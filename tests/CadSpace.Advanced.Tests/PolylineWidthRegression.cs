using System.Collections.Immutable;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

internal static class PolylineWidthRegression
{
    public static void Register(Action<string, Action> test)
    {
        void Check(bool condition) { if (!condition) throw new Exception("Wide polyline assertion failed."); }
        void Near(double a, double b, double tolerance = 1e-7) { if (Math.Abs(a - b) > tolerance) throw new Exception($"Expected {b}, got {a}."); }
        void Reject(Action work) { try { work(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { return; } throw new Exception("Expected rejection."); }
        PolylineEntity Line(double width = 10) => PolylineEntity.FromPoints([default, new(100, 0)]) with { ConstantWidth = width };
        DrawingScene Scene(Entity entity) => EntityGeometry.BuildScene(Drawing.Empty with { Entities = [entity] });
        double Area(DrawingScene scene) => scene.Triangles.Sum(t => (t.B - t.A).Cross(t.C - t.A).Length / 2);
        CadSession Session(PolylineEntity? entity = null) { var session = new CadSession(new(Drawing.Empty with { Entities = [entity ?? Line()] })); session.SelectAll(); return session; }
        foreach (var binary in new[] { false, true })
        {
            test($"independent width fixture import binary={binary}", () =>
            {
                var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", binary ? "independent-widths-binary.dxf" : "independent-widths.dxf"));
                var r = DxfBinary.Read(bytes); Check(r.Drawing.Entities.Length == 5); Check(r.Drawing.Entities.All(e => e is PolylineEntity or PlacedEntity));
                var constant = (PolylineEntity)r.Drawing.Entities[0]; Near(constant.ConstantWidth, 8); Near(Area(Scene(constant)), 800);
                var taper = (PolylineEntity)r.Drawing.Entities[1]; Near(taper.Vertices[0].StartWidth, 2); Near(taper.Vertices[0].EndWidth, 12);
                var tilted = (PlacedEntity)r.Drawing.Entities[3]; Check(((PolylineEntity)tilted.Geometry).HasWidth); Check(Scene(tilted).Triangles.Length > 0);
                Check(DxfBinary.Write(r.Drawing, r.Source, binary).Bytes.SequenceEqual(bytes));
            });
            test($"new constant width DXF roundtrip binary={binary}", () =>
            {
                var d = Drawing.Empty with { Entities = [Line()] }; var r = DxfBinary.Read(DxfBinary.Write(d, binary: binary).Bytes);
                var polyline = (PolylineEntity)r.Drawing.Entities.Single(); Near(polyline.ConstantWidth, 10); Check(polyline.HasWidth); Near(Area(Scene(polyline)), 1000);
            });
            test($"variable width bulge and closing edge roundtrip binary={binary}", () =>
            {
                var p = new PolylineEntity([new(default, .5) { StartWidth = 2, EndWidth = 6 }, new(new(100, 0)) { StartWidth = 3, EndWidth = 7 }, new(new(100, 40)) { StartWidth = 8, EndWidth = 4 }], true);
                var r = (PolylineEntity)DxfBinary.Read(DxfBinary.Write(Drawing.Empty with { Entities = [p] }, binary: binary).Bytes).Drawing.Entities[0];
                Check(p.Vertices.SequenceEqual(r.Vertices)); Check(r.Closed && r.ConstantWidth == 0);
            });
            test($"transformed OCS widths survive analytic export binary={binary}", () =>
            {
                var p = Line(8); var t = Transform3.Scaling(new(3, 3, 3)).Then(Coordinates3D.ObjectCoordinateSystem(new(1, 2, 3)));
                var placed = new PlacedEntity(p, t); var r = DxfBinary.Read(DxfBinary.Write(Drawing.Empty with { Entities = [placed] }, binary: binary).Bytes);
                Near(Area(Scene(r.Drawing.Entities[0])), Area(Scene(placed)), 1e-5); Check(r.Drawing.Entities[0] is PlacedEntity { Geometry: PolylineEntity { ConstantWidth: 24 } });
            });
        }
        test("legacy POLYLINE default and per-vertex widths are interpreted", () =>
        {
            var r = DxfBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "independent-widths-r12.dxf")));
            var p = (PolylineEntity)r.Drawing.Entities.Single(); Near(p.Vertices[0].StartWidth, 4); Near(p.Vertices[0].EndWidth, 6); Near(p.Vertices[1].StartWidth, 8); Near(p.Vertices[1].EndWidth, 3);
        });
        test("width fills are represented once in 2D and as two GPU triangles", () => { var s = Scene(Line()); Check(s.Paths.Length == 1 && s.Paths[0].Filled && s.Triangles.Length == 2); Near(s.Bounds.Min.Y, -5); Near(s.Bounds.Max.Y, 5); });
        test("tapered straight segment area", () => { var p = new PolylineEntity([new(default) { StartWidth = 2, EndWidth = 12 }, new(new(100, 0))]); Near(Area(Scene(p)), 700); });
        test("taper to zero produces a triangle without invalid faces", () => { var p = new PolylineEntity([new(default) { StartWidth = 0, EndWidth = 10 }, new(new(100, 0))]); var s = Scene(p); Check(s.Triangles.Length == 1); Near(Area(s), 500); });
        test("semicircle width approximates analytic annular area", () => { var p = new PolylineEntity([new(new(-50, 0), 1), new(new(50, 0))]) { ConstantWidth = 10 }; Near(Area(Scene(p)), Math.PI * 50 * 10, .2); });
        test("negative bulge width retains finite consistently oriented faces", () => { var p = new PolylineEntity([new(new(-50, 0), -1), new(new(50, 0))]) { ConstantWidth = 10 }; var s = Scene(p); Check(s.Triangles.All(t => (t.B - t.A).Cross(t.C - t.A).Z > 0)); Near(Area(s), Math.PI * 500, .2); });
        test("bevel joins close outside corner gaps", () => { var p = PolylineEntity.FromPoints([default, new(100, 0), new(100, 100)]) with { ConstantWidth = 10 }; var s = Scene(p); Check(s.Paths.Length == 4); Check(s.Paths.All(x => x.Filled)); });
        test("wide closed rectangle does not fill its interior", () => { var p = PolylineEntity.FromPoints([default, new(100, 0), new(100, 100), new(0, 100)], true) with { ConstantWidth = 10 }; var session = Session(p); Check(session.HitTest(new(50, 50), .1) == null); Check(session.HitTest(new(50, 3), .1) == p.Id); });
        test("zero width is centerline geometry only", () => { var s = Scene(Line(0)); Check(s.Triangles.IsEmpty && s.Paths.All(p => !p.Filled)); });
        test("mixed width path retains its zero-width segment", () => { var p = new PolylineEntity([new(default) { StartWidth = 10, EndWidth = 10 }, new(new(100, 0)), new(new(200, 0))]); var s = Scene(p); Check(s.Paths.Any(x => !x.Filled)); Near(Area(s), 1000); });
        test("wide surface can be picked in 3D away from centerline", () => { var p = Line(); var s = Scene(p); var pick = ScenePicking.Pick(s, new(new(50, 4, 100), -Vec3.UnitZ), v => new(v.X, v.Y, 100 - v.Z), new(50, 4), .1); Check(pick?.EntityId == p.Id); });
        test("wide window containment uses outer fill", () => { var p = Line(); var s = Session(p); s.SelectWindow(new(-1, -1), new(101, 1), false); Check(s.Selection.Count == 0); s.SelectWindow(new(-1, -6), new(101, 6), false); Check(s.Selection.Count == 1); });
        test("negative and nonfinite widths rejected atomically", () => { foreach (var n in new[] { -1d, double.NaN, double.PositiveInfinity }) { var s = new CadSession(); Reject(() => s.Add("bad", Line(n))); Check(!s.Document.CanUndo); } });
        test("nonplanar wide polyline rejected", () => Reject(() => new CadDocument(Drawing.Empty with { Entities = [PolylineEntity.FromPoints([default, new(10, 0, 1)]) with { ConstantWidth = 2 }] })));
        test("native widths survive roundtrip and provenance verification", () => { var raw = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "independent-widths-binary.dxf")); var r = DxfBinary.Read(raw); var text = CadProjectCodec.Write(r.Drawing, r.Source); var next = CadProjectCodec.Read(text); Check(CadProjectCodec.Write(next.Drawing, next.DxfSource) == text); Check(DxfBinary.Write(next.Drawing, next.DxfSource, true).Bytes.SequenceEqual(raw)); });
        test("uniform transform scales both constant and vertex widths", () => { var p = new PolylineEntity([new(default) { StartWidth = 2, EndWidth = 6 }, new(new(100, 0))]); var scaled = (PolylineEntity)EntityGeometry.Transform(p, Transform3.Scaling(new(3, 3, 3))); Near(scaled.Vertices[0].StartWidth, 6); Near(scaled.Vertices[0].EndWidth, 18); Near(((PolylineEntity)EntityGeometry.Transform(Line(), Transform3.Scaling(new(2, 2, 2)))).ConstantWidth, 20); });
        test("mirror preserves widths and flips bulges", () => { var p = new PolylineEntity([new(default, .5) { StartWidth = 2, EndWidth = 6 }, new(new(100, 0))]); var m = (PolylineEntity)EntityGeometry.Transform(p, Transform3.MirrorXY(default, Vec3.UnitX)); Near(m.Vertices[0].Bulge, -.5); Near(m.Vertices[0].StartWidth, 2); });
        test("nonuniform affine width export uses mesh geometry with warning", () => { var e = EntityGeometry.Transform(Line(), Transform3.Scaling(new(3, 2, 1))); Check(e is PlacedEntity); var w = DxfCodec.Write(Drawing.Empty with { Entities = [e] }); Check(w.Warnings.Any(x => x.Contains("wide polyline"))); var r = DxfCodec.Read(w.Text); Check(r.Drawing.Entities.All(x => x is MeshEntity)); Near(Area(Scene(r.Drawing.Entities.Single())), 6000); });
        test("width editing preserves ID and is a single undo", () => { var s = Session(); var old = s.Document.Drawing; s.SetWidth(20); Near(((PolylineEntity)s.Document.Drawing.Entities[0]).ConstantWidth, 20); Check(s.Document.Drawing.Entities[0].Id == old.Entities[0].Id); s.Document.Undo(); Check(s.Document.Drawing == old); });
        test("PEDIT subset and PLINEWID command workflows", () => { var s = new CadSession(); var c = new CommandEngine(s); foreach (var input in new[] { "PLINEWID", "8", "PLINE", "0,0", "100,0", "100,50", "", "SELECTALL", "PEDIT", "WIDTH", "12", "PEDIT", "CLOSE", "PEDIT", "REVERSE", "PEDIT", "OPEN" }) c.Submit(input); var p = (PolylineEntity)s.Document.Drawing.Entities.Single(); Near(p.ConstantWidth, 12); Check(!p.Closed && p.Vertices[0].Position == new Vec3(100, 50)); });
        test("PEDIT current default does not affect ordinary line entities", () => { var s = new CadSession(); var c = new CommandEngine(s); foreach (var input in new[] { "PLINEWID", "8", "LINE", "0,0", "100,0", "" }) c.Submit(input); Check(s.Document.Drawing.Entities.Single() is LineEntity); });
        test("width assignment rejects mixed selections without partial edits", () => { var s = Session(); s.Document.Add("point", new PointEntity(default)); s.SelectAll(); var d = s.Document.Drawing; Reject(() => s.SetWidth(5)); Check(s.Document.Drawing == d); });
        test("width assignment respects locked layers", () => { var s = Session(); s.Document.Edit("lock", d => d with { Layers = d.Layers.SetItem("0", new("0", Locked: true)) }); Reject(() => s.SetWidth(5)); });
        test("wide EXPLODE cannot silently discard width", () => { var s = Session(); var d = s.Document.Drawing; Reject(s.Explode); Check(s.Document.Drawing == d); });
        test("reverse twice restores bulges and widths including dormant final vertex", () => { var p = new PolylineEntity([new(default, .5) { StartWidth = 2, EndWidth = 4 }, new(new(10, 0), -.3) { StartWidth = 5, EndWidth = 7 }, new(new(20, 0), .1) { StartWidth = 8, EndWidth = 9 }]); foreach (var closed in new[] { false, true }) { var source = p with { Closed = closed }; var r = PolylineEditing.Reverse(PolylineEditing.Reverse(source)); Check(source.Vertices.SequenceEqual(r.Vertices)); } });
        test("segment property editing materializes other global widths", () => { var p = PolylineEntity.FromPoints([default, new(10, 0), new(20, 0)]) with { ConstantWidth = 8 }; var s = Session(p); s.EditPolylineVertex(p, 0, p.Vertices[0] with { StartWidth = 2, EndWidth = 4 }); var next = (PolylineEntity)s.Document.Drawing.Entities[0]; Near(next.ConstantWidth, 0); Near(next.Vertices[1].StartWidth, 8); Near(next.Vertices[0].EndWidth, 4); });
        test("stale segment edit is rejected", () => { var p = Line(); var s = Session(p); s.SetWidth(6); Reject(() => s.EditPolylineVertex(p, 0, p.Vertices[0] with { StartWidth = 9 })); });
        test("vertex grip retains segment width properties", () => { var p = new PolylineEntity([new(default) { StartWidth = 3, EndWidth = 7 }, new(new(100, 0))]); var moved = (PolylineEntity)GripEditing.Move(p, 0, new(0, 10)); Near(moved.Vertices[0].StartWidth, 3); Near(moved.Vertices[0].EndWidth, 7); });
        test("width export files for independent audit", () =>
        {
            var folder = Environment.GetEnvironmentVariable("CADSPACE_EXCHANGE_OUTPUT"); if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder); var d = DxfBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "independent-widths.dxf"))).Drawing;
            File.WriteAllText(Path.Combine(folder, "widths.dxf"), DxfCodec.Write(d).Text);
            File.WriteAllBytes(Path.Combine(folder, "widths-binary.dxf"), DxfBinary.Write(d, binary: true).Bytes);
        });
    }
}
