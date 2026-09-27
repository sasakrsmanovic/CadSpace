using System.Collections.Immutable;
using System.Diagnostics;
using CadSpace.Geometry;
using CadSpace.Model;
using CadSpace.Engine;
using CadSpace.Dxf;

internal static class InteractionRegression
{
    private static void Check(bool condition) { if (!condition) throw new Exception("Interaction assertion failed."); }
    private static void Near(Vec3 a, Vec3 b) => Check(a.DistanceTo(b) < 1e-7);
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or IOException) { return; }
        throw new Exception("Expected operation rejection.");
    }
    private static CadSession Session(params Entity[] entities) => new(new(Drawing.Empty with { Entities = entities.ToImmutableArray() }));
    public static void Register(Action<string, Action> test)
    {
        test("overlap query includes triangle-only filled roots", () =>
        {
            var id = Guid.NewGuid(); var scene = new DrawingScene([], [], [new(id, 0xFFFFFFFF, default, new(10, 0), new(0, 10))]);
            Check(SelectionQueries.For(scene).Pick(new(2, 2), .1).SequenceEqual([id]));
        });
        test("zero displacement does not bypass stretch corner validation", () =>
        {
            var s = Session(new LineEntity(default, new(10, 0))); Reject(() => s.PreviewStretch(new(double.NaN, 0), default, default)); Check(!s.Document.IsDirty);
        });
        test("median BVH partition handles identical centers and ordered adversarial inputs", () =>
        {
            foreach (var pattern in Enumerable.Range(0, 4))
            {
                var boxes = Enumerable.Range(0, 4097).Select(i => { var x = pattern switch { 0 => 5, 1 => i, 2 => 4097 - i, _ => i % 2 == 0 ? i : -i }; return new Bounds3(new(x, 0), new(x + 1, 1)); }).ToArray();
                var tree = new SpatialIndex(boxes);
                foreach (var x in new[] { -3000, -1, 5, 1024, 4096 })
                {
                    var box = new Bounds3(new(x, -1), new(x + 2, 2)); var results = new List<int>(); tree.Query(box, results, true);
                    Check(results.Order().SequenceEqual(Enumerable.Range(0, boxes.Length).Where(i => boxes[i].IntersectsXY(box))));
                }
            }
        });
        test("indexed window includes only fully enclosed whole roots", () =>
        {
            var s = Session(new LineEntity(default, new(10, 0)), new LineEntity(new(5, 5), new(15, 5)));
            s.SelectWindow(new(-1, -1), new(11, 6), false); Check(s.Selection.SetEquals([s.Document.Drawing.Entities[0].Id]));
            s.SelectWindow(new(-1, -1), new(11, 6), true); Check(s.Selection.Count == 2);
        });
        test("window cannot select a partly contained attributed root", () =>
        {
            var id = Guid.NewGuid(); var scene = new DrawingScene([new(id, "0", 0xFFFFFFFF, [default, new(2, 0)], false)], [new(id, "0", 0xFFFFFFFF, new(100, 0), "OUTSIDE", 2, 0)], []);
            var q = SelectionQueries.For(scene); Check(q.Window(new(-1, -1), new(3, 3), false).EntityIds.IsEmpty);
            Check(q.Window(new(-1, -1), new(3, 3), true).EntityIds.SequenceEqual([id]));
        });
        test("text crossing considers extents not just insertion", () =>
        {
            var s = Session(new TextEntity(default, "TEXT", 10)); s.SelectWindow(new(5, 1), new(15, 8), true); Check(s.Selection.Count == 1);
            s.SelectWindow(new(-1, -1), new(1, 1), false); Check(s.Selection.Count == 0);
        });
        test("window query includes triangle-only roots", () =>
        {
            var id = Guid.NewGuid(); var scene = new DrawingScene([], [], [new(id, 0xFFFFFFFF, default, new(10, 0), new(0, 10))]);
            Check(SelectionQueries.For(scene).Window(new(2, 2), new(3, 3), true).EntityIds.SequenceEqual([id]));
        });
        test("crossing supports collinear boundary and zero-area window", () =>
        {
            var s = Session(new LineEntity(new(-10, 0), new(10, 0))); s.SelectWindow(new(-1, 0), new(1, 0), true); Check(s.Selection.Count == 1);
            s.SelectWindow(new(-1, 0), new(1, 0), false); Check(s.Selection.Count == 0);
        });
        test("filled path enclosing a crossing window is selected", () =>
        {
            var s = Session(new HatchEntity([default, new(20, 0), new(20, 20), new(0, 20)], Solid: true));
            s.SelectWindow(new(4, 4), new(5, 5), true); Check(s.Selection.Count == 1);
        });
        test("selection remove and toggle are atomic set operations", () =>
        {
            var s = Session(new LineEntity(default, new(10, 0)), new CircleEntity(new(30, 30), 5)); s.SelectAll(); var id = s.Document.Drawing.Entities[0].Id;
            s.ApplySelection([id, id], SelectionMode.Toggle); Check(s.Selection.Count == 1 && !s.Selection.Contains(id));
            s.ApplySelection([id], SelectionMode.Add); s.SelectWindow(new(-1, -1), new(11, 1), false, SelectionMode.Remove); Check(s.Selection.Count == 1);
        });
        test("no-op selection does not advance selection revision", () =>
        {
            var s = Session(new PointEntity(default)); s.SelectAll(); var revision = s.SelectionRevision; s.SelectAll(); Check(s.SelectionRevision == revision);
            s.ApplySelection(s.Selection); Check(s.Selection.Count == 1 && s.SelectionRevision == revision);
        });
        test("window validates coordinates before clearing selection", () =>
        {
            var s = Session(new PointEntity(default)); s.SelectAll(); Reject(() => s.SelectWindow(new(double.NaN, 0), default, true)); Check(s.Selection.Count == 1);
        });
        test("selected lookup retains drawing order and follows undo", () =>
        {
            var s = Session(new PointEntity(default), new PointEntity(new(1, 0))); var ids = s.Document.Drawing.Entities.Select(e => e.Id).ToArray();
            s.ApplySelection(ids.Reverse()); Check(s.SelectedEntities().Select(e => e.Id).SequenceEqual(ids)); s.TransformSelection("Move", Transform3.Translation(new(3, 0)));
            Near(((PointEntity)s.FindEntity(ids[0])!).Position, new(3, 0)); s.Document.Undo(); Near(((PointEntity)s.FindEntity(ids[0])!).Position, default);
        });
        test("quick select filters active layout and hidden layers", () =>
        {
            var d = Drawing.Empty; d = d with { Layers = d.Layers.Add("Hidden", new("Hidden", Visible: false)), Entities = [new LineEntity(default, new(1, 0)), new CircleEntity(default, 3), new LineEntity(default, new(2, 0)) { Layer = "Hidden" }, new LineEntity(default, new(3, 0)) { Layout = "Layout1" }] };
            var s = new CadSession(new(d)); s.QuickSelect("line", "0"); Check(s.Selection.Count == 1); s.ActiveLayout = "Layout1"; s.QuickSelect("LINE"); Check(s.Selection.Count == 1);
        });
        test("quick select current-selection scope preserves outside objects", () =>
        {
            var s = Session(new LineEntity(default, new(1, 0)), new CircleEntity(default, 3), new CircleEntity(new(10, 0), 3));
            s.ApplySelection(s.Document.Drawing.Entities.Take(2).Select(e => e.Id)); s.QuickSelect("CIRCLE", "*", SelectionMode.Replace, true); Check(s.Selection.SetEquals([s.Document.Drawing.Entities[1].Id]));
        });
        test("quick select command and SelectSimilar workflows", () =>
        {
            var s = Session(new LineEntity(default, new(1, 0)), new LineEntity(new(2, 0), new(3, 0)), new CircleEntity(default, 3)); var c = new CommandEngine(s);
            c.Submit("QSELECT"); c.Submit("LINE,*,Replace,All"); Check(s.Selection.Count == 2 && !c.IsActive);
            s.Select(s.Document.Drawing.Entities[0].Id); c.Start("SELECTSIMILAR"); Check(s.Selection.Count == 2);
        });
        test("overlap picking deduplicates roots and is deterministic", () =>
        {
            var a = new LineEntity(default, new(10, 0)); var b = new LineEntity(default, new(10, 0)); var s = Session(a, b);
            Check(SelectionQueries.For(s.Scene).Pick(new(5, 0), 1).SequenceEqual([b.Id, a.Id]));
        });
        test("grip endpoint preview is immutable and commits one undo step", () =>
        {
            var line = new LineEntity(default, new(10, 0)); var s = Session(line); var preview = (LineEntity)s.PreviewGrip(line, 1, new(20, 5));
            Check(ReferenceEquals(s.Document.Drawing.Entities[0], line)); Near(preview.End, new(20, 5)); s.MoveGrip(line, 1, new(20, 5));
            Check(s.Document.Revision == 1); s.Document.Undo(); Check(s.Document.Drawing.Entities[0] == line);
        });
        test("midpoint grip translates the whole line", () =>
        {
            var line = new LineEntity(default, new(10, 0)); var next = (LineEntity)GripEditing.Move(line, 2, new(8, 4)); Near(next.Start, new(3, 4)); Near(next.End, new(13, 4)); Check(next.Id == line.Id);
        });
        test("circle radius grips preserve center and reject zero radius", () =>
        {
            var c = new CircleEntity(new(1, 2), 5); var n = (CircleEntity)GripEditing.Move(c, 1, new(11, 2)); Near(n.Center, c.Center); Check(n.Radius == 10);
            Reject(() => GripEditing.Move(c, 1, c.Center)); Reject(() => GripEditing.Move(c, 1, new(11, 2, 4)));
        });
        test("circle center grip translates without resizing", () =>
        {
            var c = new CircleEntity(default, 5); var n = (CircleEntity)GripEditing.Move(c, 0, new(10, 20)); Near(n.Center, new(10, 20)); Check(n.Radius == 5);
        });
        test("polyline grip preserves bulges and identity", () =>
        {
            var p = new PolylineEntity([new(default, .5), new(new(10, 0))]); var n = (PolylineEntity)GripEditing.Move(p, 1, new(15, 2));
            Check(n.Vertices[0].Bulge == .5 && n.Id == p.Id); Near(n.Vertices[1].Position, new(15, 2)); Reject(() => GripEditing.Move(p, 1, new(15, 2, 3)));
        });
        test("placed grip edits invert rotated nonuniform placement", () =>
        {
            var t = Transform3.Scaling(new(2, 3, 1)).Then(Transform3.RotationZ(32)).Then(Transform3.Translation(new(50, 60)));
            var p = new PlacedEntity(new LineEntity(default, new(10, 0)), t); var n = (PlacedEntity)GripEditing.Move(p, 1, t.Point(new(12, 7)));
            Near(((LineEntity)n.Geometry).End, new(12, 7)); Check(n.Id == p.Id && n.Placement == p.Placement);
        });
        test("spline control grip preserves rational knots and weights", () =>
        {
            var p = AdvancedEditing.ControlSpline([default, new(5, 10), new(10, 0)]); var n = (SplineEntity)GripEditing.Move(p, 1, new(6, 20));
            Check(n.Knots == p.Knots && n.Weights == p.Weights); Near(n.ControlPoints[1], new(6, 20));
        });
        test("grips reject stale edits without replacing newer geometry", () =>
        {
            var line = new LineEntity(default, new(10, 0)); var s = Session(line); s.MoveGrip(line, 1, new(20, 0)); var drawing = s.Document.Drawing;
            Reject(() => s.MoveGrip(line, 0, new(3, 0))); Check(ReferenceEquals(s.Document.Drawing, drawing));
        });
        test("grips reject locked layers and nonfinite targets", () =>
        {
            var e = new PointEntity(default); var d = Drawing.Empty with { Entities = [e] }; d = d with { Layers = d.Layers.SetItem("0", new("0", Locked: true)) }; var s = new CadSession(new(d));
            Reject(() => s.MoveGrip(e, 0, new(1, 1))); Reject(() => GripEditing.Move(e, 0, new(double.NaN, 1)));
        });
        test("stretch moves only enclosed endpoints", () =>
        {
            var line = new LineEntity(default, new(10, 0)); var s = Session(line); s.Stretch(new(8, -2), new(12, 2), new(5, 3));
            var n = (LineEntity)s.Document.Drawing.Entities[0]; Near(n.Start, default); Near(n.End, new(15, 3)); s.Document.Undo(); Check(s.Document.Drawing.Entities[0] == line);
        });
        test("stretch translates whole circles but refuses partial radius deformation", () =>
        {
            var s = Session(new CircleEntity(default, 5)); s.Stretch(new(-6, -6), new(6, 6), new(10, 0)); Near(((CircleEntity)s.Document.Drawing.Entities[0]).Center, new(10, 0));
            var d = s.Document.Drawing; Reject(() => s.Stretch(new(9, -1), new(16, 1), new(5, 0))); Check(ReferenceEquals(d, s.Document.Drawing));
        });
        test("stretch crossing no endpoints leaves line unchanged", () =>
        {
            var s = Session(new LineEntity(new(-20, 0), new(20, 0))); var d = s.Document.Drawing; s.Stretch(new(-1, -1), new(1, 1), new(4, 4)); Check(s.Document.Drawing == d);
        });
        test("stretch is atomic when a selected layer is locked", () =>
        {
            var d = Drawing.Empty; d = d with { Layers = d.Layers.Add("Locked", new("Locked", Locked: true)), Entities = [new PointEntity(default), new PointEntity(new(2, 0)) { Layer = "Locked" }] };
            var s = new CadSession(new(d)); Reject(() => s.Stretch(new(-1, -1), new(3, 1), new(10, 0))); Check(s.Document.Drawing == d);
        });
        test("stretch command uses crossing corners then base and second point", () =>
        {
            var s = Session(new LineEntity(default, new(10, 0))); var c = new CommandEngine(s);
            foreach (var input in new[] { "STRETCH", "8,-2", "12,2", "0,0", "@5,3" }) c.Submit(input);
            Check(!c.IsActive); Near(((LineEntity)s.Document.Drawing.Entities[0]).End, new(15, 3));
        });
        test("grip and stretch changes survive native project roundtrip", () =>
        {
            var line = new LineEntity(default, new(10, 0)); var s = Session(line); s.MoveGrip(line, 1, new(30, 10));
            var read = CadProjectCodec.Read(CadProjectCodec.Write(s.Document.Drawing)); Near(((LineEntity)read.Drawing.Entities[0]).End, new(30, 10));
        });
        test("mark recovered drawing unsaved does not manufacture undo history", () =>
        {
            var d = new CadDocument(); d.MarkUnsaved(); Check(d.IsDirty && !d.CanUndo); d.MarkSaved(); Check(!d.IsDirty);
        });
        test("100k window queries match old linear selection with bounded work", () =>
        {
            var entities = Enumerable.Range(0, 100000).Select(i => (Entity)new LineEntity(new(i % 1000 * 10, i / 1000 * 10), new(i % 1000 * 10 + 3, i / 1000 * 10 + 2))).ToImmutableArray();
            var s = new CadSession(new(Drawing.Empty with { Entities = entities })); var scene = s.Scene;
            var cold = Stopwatch.StartNew(); var index = SelectionQueries.For(scene); cold.Stop();
            var first = new Vec3(4999, 499); var second = new Vec3(5005, 505); var bounds = Bounds3.From([first, second]);
            var query = index.Window(first, second, false); Check(query.CandidateCount <= 4 && query.VisitedNodes < 128);
            Guid[] Linear() => scene.Paths.GroupBy(p => p.EntityId).Where(g => g.SelectMany(p => p.Points).ToArray() is var points && points.Length > 0 && points.All(bounds.ContainsXY)).Select(g => g.Key).ToArray();
            Check(query.EntityIds.SequenceEqual(Linear()));
            var allocation = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            for (var i = 0; i < 30; i++) index.Window(first, second, false);
            watch.Stop(); var indexedMs = watch.Elapsed.TotalMilliseconds; var indexedBytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
            allocation = GC.GetAllocatedBytesForCurrentThread(); watch.Restart(); for (var i = 0; i < 30; i++) Linear(); watch.Stop();
            var linearBytes = GC.GetAllocatedBytesForCurrentThread() - allocation; Check(indexedBytes < linearBytes / 100);
            var output = $"WINDOW 100000 lines / 30 warmed queries: indexed={indexedMs:0.###} ms; linear={watch.Elapsed.TotalMilliseconds:0.###} ms; indexedAllocated={indexedBytes}; linearAllocated={linearBytes}; coldIndex={cold.Elapsed.TotalMilliseconds:0.###} ms; candidates={query.CandidateCount}; nodes={query.VisitedNodes}; matching=true";
            Console.WriteLine(output); var file = Environment.GetEnvironmentVariable("CADSPACE_BENCHMARK_OUTPUT"); if (!string.IsNullOrEmpty(file)) File.AppendAllText(file, output + Environment.NewLine);
        });
    }
}
