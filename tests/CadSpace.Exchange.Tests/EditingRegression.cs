using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

internal static class EditingRegression
{
    public static void Register(Action<string, Action> test, Action<bool> check, Action<double, double> near)
    {
        CadSession Corners()
        {
            var session = new CadSession(); session.Add("Lines", new LineEntity(default, new(20, 0)), new LineEntity(default, new(0, 20))); session.SelectAll(); return session;
        }
        test("trim removes picked internal interval", () =>
        {
            var s = new CadSession(); var a = new LineEntity(new(3, -2), new(3, 2)); var b = new LineEntity(new(7, -2), new(7, 2)); var line = new LineEntity(default, new(10, 0));
            s.Add("Lines", a, b, line); s.Select(a.Id); s.Select(b.Id, true); var original = s.Document.Drawing;
            s.TrimOrExtend(new(5, 0), 0.2, false);
            var pieces = s.Document.Drawing.Entities.OfType<LineEntity>().Where(e => e.Id != a.Id && e.Id != b.Id).ToArray();
            check(pieces.Length == 2); near(pieces.Sum(l => l.Start.DistanceTo(l.End)), 6); check(pieces.Select(l => l.Id).Distinct().Count() == 2);
            s.Document.Undo(); check(s.Document.Drawing == original);
        });
        test("trim removes picked terminal interval", () =>
        {
            var s = new CadSession(); var boundary = new LineEntity(new(4, -2), new(4, 2)); var line = new LineEntity(default, new(10, 0)); s.Add("Lines", boundary, line); s.Select(boundary.Id);
            s.TrimOrExtend(new(1, 0), 0.2, false); var result = (LineEntity)s.Document.Drawing.Entities.Single(e => e.Id == line.Id); near(result.Start.X, 4); near(result.End.X, 10);
        });
        test("extend chooses nearest bounded boundary", () =>
        {
            var s = new CadSession(); var a = new LineEntity(new(15, -2), new(15, 2)); var b = new LineEntity(new(20, -2), new(20, 2)); var line = new LineEntity(default, new(10, 0)); s.Add("Lines", a, b, line); s.Select(a.Id); s.Select(b.Id, true);
            s.TrimOrExtend(new(9, 0), 0.2, true); near(((LineEntity)s.Document.Drawing.Entities.Single(e => e.Id == line.Id)).End.X, 15);
        });
        test("extend rejects intersection outside boundary segment", () =>
        {
            var s = new CadSession(); var boundary = new LineEntity(new(20, 5), new(20, 10)); s.Add("Lines", boundary, new LineEntity(default, new(10, 0))); s.Select(boundary.Id); var original = s.Document.Drawing;
            var rejected = false; try { s.TrimOrExtend(new(9, 0), 0.2, true); } catch (ArgumentException) { rejected = true; } check(rejected && s.Document.Drawing == original);
        });
        test("fillet tangency and radius", () =>
        {
            var s = Corners(); s.Fillet(4); var arc = s.Document.Drawing.Entities.OfType<ArcEntity>().Single(); near(arc.Center.X, 4); near(arc.Center.Y, 4); near(arc.Radius, 4);
            var endpoints = s.Document.Drawing.Entities.OfType<LineEntity>().SelectMany(l => new[] { l.Start, l.End }).Where(p => p.DistanceTo(default) < 10).ToArray(); check(endpoints.Length == 2);
            foreach (var p in endpoints) near(p.DistanceTo(arc.Center), 4);
        });
        test("oversized fillet leaves history unchanged", () =>
        {
            var s = Corners(); var original = s.Document.Drawing; var revision = s.Document.Revision; var rejected = false;
            try { s.Fillet(30); } catch (ArgumentException) { rejected = true; } check(rejected && s.Document.Drawing == original && s.Document.Revision == revision);
        });
        test("chamfer equal distances", () =>
        {
            var s = Corners(); s.Chamfer(3); var connector = (LineEntity)s.Document.Drawing.Entities[^1]; near(connector.Start.Length, 3); near(connector.End.Length, 3); near(connector.Start.DistanceTo(connector.End), Math.Sqrt(18));
        });
        test("join unordered reversed chain", () =>
        {
            var s = new CadSession(); s.Add("Lines", new LineEntity(new(10, 10), new(10, 0)), new LineEntity(default, new(10, 0)), new LineEntity(new(10, 10), new(0, 10)), new LineEntity(new(0, 10), default)); s.SelectAll(); s.JoinLines();
            check(s.Document.Drawing.Entities.Length == 1 && s.Document.Drawing.Entities[0] is PolylineEntity { Closed: true, Vertices.Length: 4 });
        });
        test("join disconnected selection is atomic", () =>
        {
            var s = new CadSession(); s.Add("Lines", new LineEntity(default, new(10, 0)), new LineEntity(new(20, 0), new(30, 0))); s.SelectAll(); var original = s.Document.Drawing; var rejected = false;
            try { s.JoinLines(); } catch (ArgumentException) { rejected = true; } check(rejected && original == s.Document.Drawing);
        });
        test("break with reversed points", () =>
        {
            var s = new CadSession(); s.Add("Line", new LineEntity(default, new(10, 0))); s.SelectAll(); s.BreakLine(new(7, 0), new(3, 0));
            check(s.Document.Drawing.Entities.Length == 2); near(s.Document.Drawing.Entities.Cast<LineEntity>().Sum(l => l.Start.DistanceTo(l.End)), 6);
        });
        test("numeric prompt ignores extra pointer input", () =>
        {
            var s = new CadSession(); var c = new CommandEngine(s); c.Start("BOX"); c.Point(default); c.Point(new(20, 10)); c.Point(new(99, 99)); c.Submit("5");
            check(s.Document.Drawing.Entities.Length == 1); near(MeshFactory.SignedVolume((MeshEntity)s.Document.Drawing.Entities[0]), 1000);
        });
        test("rotation numeric prompt remains valid after click", () =>
        {
            var s = new CadSession(); s.Add("Line", new LineEntity(default, new(10, 0))); s.SelectAll(); var c = new CommandEngine(s); c.Start("ROTATE"); c.Point(default); c.Point(new(50, 60)); c.Submit("90"); near(((LineEntity)s.Document.Drawing.Entities[0]).End.Y, 10);
        });
        test("ellipse command creates analytic ellipse", () =>
        {
            var s = new CadSession(); var c = new CommandEngine(s); foreach (var value in new[] { "EL", "10,20", "40,20", "15" }) c.Submit(value); var ellipse = (EllipseEntity)s.Document.Drawing.Entities.Single(); near(ellipse.MajorAxis.Length, 30); near(ellipse.Ratio, 0.5);
        });
        test("zero width rectangle rejected atomically", () =>
        {
            var s = new CadSession(); var c = new CommandEngine(s); foreach (var value in new[] { "REC", "10,20", "10,40" }) c.Submit(value); check(s.Document.Drawing.Entities.IsEmpty && !s.Document.CanUndo);
        });
    }
}
