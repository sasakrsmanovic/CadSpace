using System.Collections.Immutable;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

internal static class StyleReviewRegression
{
    public static void Register(Action<string, Action> test)
    {
        static void Check(bool value) { if (!value) throw new Exception("Style review regression failed."); }
        static void Reject(Action action)
        {
            try { action(); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
            throw new Exception("Expected input rejection.");
        }
        var type = new Linetype("DASHED", "Test", [10, -5, 0, -5]);
        Drawing Styled(params Entity[] entities) => Drawing.Empty with
        {
            Linetypes = Linetype.Defaults.Add(type.Name, type), Entities = entities.ToImmutableArray()
        };
        test("spline linetype remains continuous across tessellation", () =>
        {
            var spline = AdvancedEditing.ControlSpline([new(0, 0), new(40, 60), new(80, -60), new(120, 0)]) with { Linetype = "DASHED" };
            var paths = EntityGeometry.BuildScene(Styled(spline)).Paths;
            Check(paths.Length == 1 && paths[0].Points.Length > 4 && paths[0].Pattern != null);
        });
        test("3D polyline generation flag controls pattern resets", () =>
        {
            var poly = new Polyline3DEntity([default, new(15, 0, 3), new(30, 5, 6)]) { Linetype = "DASHED" };
            Check(EntityGeometry.BuildScene(Styled(poly)).Paths.Length == 2);
            Check(EntityGeometry.BuildScene(Styled(poly with { ContinuousLinetype = true })).Paths.Length == 1);
        });
        foreach (var binary in new[] { false, true })
            test($"3D polyline DXF generation flag roundtrip binary={binary}", () =>
            {
                var poly = new Polyline3DEntity([default, new(10, 0, 3), new(20, 10, 6)]) { Linetype = "DASHED", ContinuousLinetype = true };
                var result = DxfBinary.Read(DxfBinary.Write(Styled(poly), binary: binary).Bytes);
                Check(result.Drawing.Entities[0] is Polyline3DEntity { ContinuousLinetype: true });
                var native = CadProjectCodec.Read(CadProjectCodec.Write(result.Drawing, result.Source));
                Check(native.Drawing.Entities[0] is Polyline3DEntity { ContinuousLinetype: true });
                Check(DxfCodec.Write(native.Drawing, native.DxfSource).Text == result.Source.Text);
            });
        test("affine sampled polyline keeps linetype generation flag", () =>
        {
            var poly = new PolylineEntity([new(default, .3), new(new(20, 0)), new(new(20, 20))]) { Linetype = "DASHED", ContinuousLinetype = true };
            var placed = EntityGeometry.Transform(poly, Transform3.Scaling(new(2, 1, 1)));
            var result = DxfCodec.Read(DxfCodec.Write(Styled(placed)).Text);
            Check(result.Drawing.Entities[0] is Polyline3DEntity { ContinuousLinetype: true });
        });
        test("type-only selection assignment preserves mixed object scales", () =>
        {
            var session = new CadSession(new(Styled(new LineEntity(default, new(20, 0)) { LinetypeScale = .5 }, new CircleEntity(default, 10) { LinetypeScale = 3 })));
            var before = session.Document.Drawing; session.SelectAll(); session.SetSelectedLinetype("DASHED");
            Check(session.Document.Drawing.Entities.Select(e => e.LinetypeScale).SequenceEqual(new[] { .5, 3 }));
            Check(session.Document.Drawing.Entities.All(e => e.Linetype == "DASHED"));
            session.Document.Undo(); Check(session.Document.Drawing == before);
        });
        test("undoing a current linetype definition restores valid session settings", () =>
        {
            var session = new CadSession(); session.SetCurrentLinetype("DASHED"); session.Document.Undo();
            Check(session.CurrentLinetype == "BYLAYER"); session.Add("Line", new LineEntity(default, new(10, 0)));
            Check(session.Document.Drawing.Entities[0].Linetype == "BYLAYER");
        });
        test("renaming current layer and undoing leaves a valid current layer", () =>
        {
            var session = new CadSession(); session.AddLayer("OLD"); session.CurrentLayer = "OLD";
            session.UpdateLayer("OLD", new("NEW")); Check(session.CurrentLayer == "NEW"); session.Document.Undo();
            Check(session.CurrentLayer == "0"); session.Add("Line", new LineEntity(default, new(10, 0)));
            Check(session.Document.Drawing.Entities[0].Layer == "0");
        });
        test("standalone stroke patterns reject invalid definitions and scales", () =>
        {
            var path = new ScenePath(Guid.NewGuid(), "0", 0xFFFFFFFF, [default, new(20, 0)], false);
            var view = new Bounds3(new(-1, -1), new(21, 1));
            foreach (var scale in new[] { 0, -1, double.NaN, double.PositiveInfinity })
                Reject(() => new StrokePattern(type, scale).VisibleStrokes(path, view).ToArray());
            Reject(() => new StrokePattern(new("ZERO", "", [0, 0]), 1).IsInk(1));
            Reject(() => new StrokePattern(type, 1).IsInk(double.NaN));
            Reject(() => new StrokePattern(type, 1).IsInk(1, -1));
            Reject(() => Linetype.Validate(new("DEFAULT", "", default)));
        });
        test("all-gap pattern beyond exact integer range terminates", () =>
        {
            var gap = new StrokePattern(new("GAP", "", [-1]), 1);
            var path = new ScenePath(Guid.NewGuid(), "0", 0xFFFFFFFF, [new(-1e16, 0), new(1e16, 0)], false);
            Check(!gap.VisibleStrokes(path, new(new(-1, -1), new(1, 1))).Any());
        });
        test("stroke expansion enforces geometry and work budgets", () =>
        {
            var pattern = new StrokePattern(type, 1);
            var path = new ScenePath(Guid.NewGuid(), "0", 0xFFFFFFFF, [default, new(10000, 0)], false);
            Reject(() => pattern.VisibleStrokes(path, new(new(-1, -1), new(10001, 1)), 1).ToArray());
            Reject(() => pattern.VisibleStrokes(path, new(new(-1, -1), new(1, 1)), 0).ToArray());
            Reject(() => pattern.VisibleStrokes(path with { Points = [default, new(double.NaN, 0)] }, new(new(-1, -1), new(1, 1))).ToArray());
        });
        test("hatch pattern origin overflow is rejected without an unbounded loop", () =>
        {
            foreach (var origin in new[] { new Vec3(1e30, 0), new Vec3(0, 1e30) })
            {
                var hatch = new HatchRegionEntity([[new(default), new(new(20, 0)), new(new(20, 20)), new(new(0, 20))]], false,
                    [new(0, origin, new(0, 1), [-1])]);
                Reject(() => AdvancedGeometry.Expand(hatch).ToArray());
            }
        });
        test("clipped pattern matches independent unbounded small-path reference", () =>
        {
            var random = new Random(919);
            var pattern = new StrokePattern(type, .73);
            for (var trial = 0; trial < 100; trial++)
            {
                var origin = -500 + random.NextDouble() * 200;
                var length = 400 + random.NextDouble() * 300;
                var low = origin + random.NextDouble() * length / 2;
                var high = low + random.NextDouble() * length / 3;
                var path = new ScenePath(Guid.NewGuid(), "0", 0xFFFFFFFF, [new(origin, 0), new(origin + length, 0)], false);
                var expected = new List<StrokePattern.Stroke>();
                for (double start = 0; start <= length; start += pattern.Length)
                {
                    var cursor = start;
                    foreach (var element in type.Elements)
                    {
                        var end = Math.Min(length, cursor + Math.Abs(element) * pattern.Scale);
                        var a = Math.Max(low, origin + cursor); var b = Math.Min(high, origin + end);
                        if (element > 0 && b > a || element == 0 && b >= a && cursor <= length)
                            expected.Add(new(new(a, 0), new(b, 0), element == 0));
                        cursor += Math.Abs(element) * pattern.Scale;
                    }
                }
                var actual = pattern.VisibleStrokes(path, new(new(low, -1), new(high, 1))).ToArray();
                Check(actual.Length == expected.Count && actual.Zip(expected).All(p => p.First.Dot == p.Second.Dot && p.First.Start.DistanceTo(p.Second.Start) < 1e-7 && p.First.End.DistanceTo(p.Second.End) < 1e-7));
            }
        });
    }
}
