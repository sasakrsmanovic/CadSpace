using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

/// <summary>Atomic line-based editing operations. Curved boundaries and arbitrary UCS are deliberately not inferred.</summary>
public static class LineEditing
{
    public static void TrimOrExtend(this CadSession session, Vec3 pick, double tolerance, bool extend)
    {
        var boundaries = session.EditableSelection().Select(e => e as LineEntity ?? throw new ArgumentException("Select line boundaries only.")).ToArray();
        var target = session.Document.Drawing.Entities.OfType<LineEntity>()
            .Where(l => !session.Selection.Contains(l.Id) && session.IsVisible(l))
            .Select(l => (Line: l, Distance: GeometryMath.NearestOnSegment(pick, l.Start, l.End).DistanceTo(pick)))
            .Where(p => p.Distance <= tolerance).OrderBy(p => p.Distance).Select(p => p.Line).FirstOrDefault()
            ?? throw new ArgumentException("Pick an unselected line close to the pointer.");
        if (session.Document.Drawing.LayerFor(target).Locked) throw new InvalidOperationException("The target line is on a locked layer.");
        Planar([target, .. boundaries]);
        var vector = target.End - target.Start; var length2 = vector.Dot(vector);
        if (length2 <= 1e-18) throw new ArgumentException("The target line is degenerate.");
        double Parameter(Vec3 point) => (point - target.Start).Dot(vector) / length2;
        var intersections = new List<double>();
        foreach (var boundary in boundaries)
        {
            if (!GeometryMath.IntersectLinesXY(target.Start, target.End, boundary.Start, boundary.End, out var point, false)) continue;
            var direction = boundary.End - boundary.Start; var n = direction.Dot(direction);
            if (n <= 1e-18) continue;
            var t = (point - boundary.Start).Dot(direction) / n;
            if (t >= -1e-9 && t <= 1 + 1e-9) intersections.Add(Parameter(point));
        }
        var replacements = new List<Entity>();
        if (extend)
        {
            var start = pick.DistanceTo(target.Start) < pick.DistanceTo(target.End);
            var choices = intersections.Where(t => start ? t < -1e-9 : t > 1 + 1e-9).ToArray();
            if (choices.Length == 0) throw new ArgumentException("No boundary intersects beyond the picked line end.");
            var t = start ? choices.Max() : choices.Min();
            replacements.Add(start ? target with { Start = target.Start + vector * t } : target with { End = target.Start + vector * t });
        }
        else
        {
            var cuts = intersections.Where(t => t > 1e-9 && t < 1 - 1e-9).OrderBy(t => t).ToArray();
            if (cuts.Length == 0) throw new ArgumentException("No selected boundary cuts the target segment.");
            var p = Math.Clamp(Parameter(pick), 0, 1);
            var left = cuts.Where(t => t < p - 1e-9).DefaultIfEmpty(0).Max();
            var right = cuts.Where(t => t >= p - 1e-9).DefaultIfEmpty(1).Min();
            if (left > 1e-9) replacements.Add(target with { End = target.Start + vector * left });
            if (right < 1 - 1e-9)
                replacements.Add(target with { Start = target.Start + vector * right, Id = replacements.Count == 0 ? target.Id : Guid.NewGuid(), Handle = replacements.Count == 0 ? target.Handle : "" });
        }
        session.Document.Edit(extend ? "Extend line" : "Trim line", state => state with
        {
            Entities = state.Entities.SelectMany(e => e.Id == target.Id ? replacements : new List<Entity> { e }).ToImmutableArray()
        });
    }
    public static void Fillet(this CadSession session, double radius) => Corner(session, radius, true);
    public static void Chamfer(this CadSession session, double distance) => Corner(session, distance, false);
    private static void Corner(CadSession session, double value, bool fillet)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentException("The corner parameter must be finite and nonnegative.");
        var selection = session.EditableSelection();
        if (selection.Length != 2 || selection.Any(e => e is not LineEntity)) throw new ArgumentException("Select exactly two lines.");
        var first = (LineEntity)selection[0]; var second = (LineEntity)selection[1]; Planar([first, second]);
        if (!GeometryMath.IntersectLinesXY(first.Start, first.End, second.Start, second.End, out var corner, false)) throw new ArgumentException("The two lines are parallel or degenerate.");
        Vec3 Far(LineEntity line) => line.Start.DistanceTo(corner) > line.End.DistanceTo(corner) ? line.Start : line.End;
        var far1 = Far(first); var far2 = Far(second); var u = (far1 - corner).Normalized; var v = (far2 - corner).Normalized;
        var angle = Math.Acos(Math.Clamp(u.Dot(v), -1, 1));
        if (angle < 1e-8 || Math.PI - angle < 1e-8) throw new ArgumentException("The corner angle is degenerate.");
        var distance = fillet && value != 0 ? value / Math.Tan(angle / 2) : value;
        if (distance > Math.Min(corner.DistanceTo(far1), corner.DistanceTo(far2)) - 1e-9) throw new ArgumentException("The radius or distance exceeds the available line arms.");
        var p1 = corner + u * distance; var p2 = corner + v * distance;
        LineEntity Shorten(LineEntity line, Vec3 far, Vec3 end) => far == line.Start ? line with { End = end } : line with { Start = end };
        var a = Shorten(first, far1, p1); var b = Shorten(second, far2, p2);
        Entity? connector = null;
        if (value > 0 && fillet)
        {
            var center = corner + (u + v).Normalized * (value / Math.Sin(angle / 2));
            var start = GeometryMath.Angle(p1 - center); var end = GeometryMath.Angle(p2 - center);
            if (GeometryMath.NormalizeAngle(end - start) > 180) (start, end) = (end, start);
            connector = new ArcEntity(center, value, start, end) { Layer = first.Layer, TrueColor = first.TrueColor, ColorIndex = first.ColorIndex, LineWeight = first.LineWeight, Layout = first.Layout };
        }
        else if (value > 0) connector = new LineEntity(p1, p2) { Layer = first.Layer, TrueColor = first.TrueColor, ColorIndex = first.ColorIndex, LineWeight = first.LineWeight, Layout = first.Layout };
        session.Document.Edit(fillet ? "Fillet lines" : "Chamfer lines", state =>
        {
            var entities = state.Entities.Select(e => e.Id == a.Id ? a : e.Id == b.Id ? b : e).ToImmutableArray();
            return state with { Entities = connector == null ? entities : entities.Add(connector) };
        });
    }
    public static void JoinLines(this CadSession session, double tolerance = 1e-7)
    {
        var selected = session.EditableSelection();
        if (selected.Length < 2 || selected.Any(e => e is not LineEntity)) throw new ArgumentException("Select at least two connected lines.");
        var lines = selected.Cast<LineEntity>().ToList(); Planar(lines);
        var vertices = new LinkedList<Vec3>(new Vec3[] { lines[0].Start, lines[0].End }); lines.RemoveAt(0);
        while (lines.Count > 0)
        {
            var index = lines.FindIndex(l => l.Start.DistanceTo(vertices.First!.Value) <= tolerance || l.End.DistanceTo(vertices.First!.Value) <= tolerance || l.Start.DistanceTo(vertices.Last!.Value) <= tolerance || l.End.DistanceTo(vertices.Last!.Value) <= tolerance);
            if (index < 0) throw new ArgumentException("The selected lines do not form a single connected chain.");
            var line = lines[index]; lines.RemoveAt(index);
            if (line.Start.DistanceTo(vertices.Last!.Value) <= tolerance) vertices.AddLast(line.End);
            else if (line.End.DistanceTo(vertices.Last.Value) <= tolerance) vertices.AddLast(line.Start);
            else if (line.Start.DistanceTo(vertices.First!.Value) <= tolerance) vertices.AddFirst(line.End);
            else vertices.AddFirst(line.Start);
        }
        var points = vertices.ToList(); var closed = points[0].DistanceTo(points[^1]) <= tolerance;
        if (closed) points.RemoveAt(points.Count - 1);
        for (var i = 0; i < points.Count; i++) for (var j = i + 1; j < points.Count; j++)
            if (points[i].DistanceTo(points[j]) <= tolerance) throw new ArgumentException("The chain contains a repeated/branching vertex.");
        var ids = selected.Select(e => e.Id).ToHashSet(); var first = selected[0];
        var polyline = PolylineEntity.FromPoints(points, closed) with { Layer = first.Layer, TrueColor = first.TrueColor, ColorIndex = first.ColorIndex, LineWeight = first.LineWeight, Layout = first.Layout };
        session.Document.Edit("Join lines", state => state with { Entities = state.Entities.Where(e => !ids.Contains(e.Id)).Append(polyline).ToImmutableArray() });
    }
    public static void BreakLine(this CadSession session, Vec3 first, Vec3 second)
    {
        var selected = session.EditableSelection();
        if (selected.Length != 1 || selected[0] is not LineEntity line) throw new ArgumentException("Select exactly one line to break.");
        var d = line.End - line.Start; var length2 = d.Dot(d);
        if (length2 < 1e-18) throw new ArgumentException("The selected line is degenerate.");
        var a = Math.Clamp((first - line.Start).Dot(d) / length2, 0, 1); var b = Math.Clamp((second - line.Start).Dot(d) / length2, 0, 1);
        if (a > b) (a, b) = (b, a);
        var replacements = new List<Entity>();
        if (a > 1e-9) replacements.Add(line with { End = line.Start + d * a });
        if (b < 1 - 1e-9) replacements.Add(line with { Start = line.Start + d * b, Id = replacements.Count == 0 ? line.Id : Guid.NewGuid(), Handle = replacements.Count == 0 ? line.Handle : "" });
        session.Document.Edit("Break line", state => state with { Entities = state.Entities.SelectMany(e => e.Id == line.Id ? replacements : new List<Entity> { e }).ToImmutableArray() });
    }
    private static void Planar(IEnumerable<LineEntity> lines)
    {
        var points = lines.SelectMany(l => new[] { l.Start, l.End }).ToArray(); var elevation = points[0].Z;
        if (points.Any(p => Math.Abs(p.Z - elevation) > 1e-7)) throw new ArgumentException("This operation requires coplanar XY lines.");
    }
}
