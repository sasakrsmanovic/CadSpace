using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public enum SelectionMode { Replace, Add, Remove, Toggle }
public readonly record struct SelectionQueryResult(ImmutableArray<Guid> EntityIds, int CandidateCount, int VisitedNodes);

/// <summary>Root-level selection index. All visible children, including text and faces, participate in containment.</summary>
public sealed class SelectionQueries
{
    private static readonly ConditionalWeakTable<DrawingScene, SelectionQueries> Cache = new();
    public static SelectionQueries For(DrawingScene scene) => Cache.GetValue(scene, s => new(s));
    private sealed class Root(Guid id)
    {
        public Guid Id { get; } = id;
        public Bounds3 Bounds = Bounds3.Empty;
        public readonly List<ScenePath> Paths = new();
        public readonly List<SceneText> Texts = new();
        public readonly List<SceneTriangle> Triangles = new();
    }
    private readonly Root[] _roots;
    private readonly SpatialIndex _index;
    private SelectionQueries(DrawingScene scene)
    {
        var roots = new Dictionary<Guid, Root>();
        Root Get(Guid id) { if (!roots.TryGetValue(id, out var root)) roots.Add(id, root = new(id)); return root; }
        foreach (var path in scene.Paths)
        {
            if (path.Points.IsEmpty) continue;
            var root = Get(path.EntityId); root.Paths.Add(path); root.Bounds = root.Bounds.Union(Bounds3.From(path.Points));
        }
        foreach (var text in scene.Texts)
        {
            var root = Get(text.EntityId); root.Texts.Add(text); root.Bounds = root.Bounds.Union(SceneAcceleration.TextBounds(text));
        }
        foreach (var face in scene.Triangles)
        {
            var root = Get(face.EntityId); root.Triangles.Add(face); root.Bounds = root.Bounds.Include(face.A).Include(face.B).Include(face.C);
        }
        _roots = roots.Values.ToArray(); _index = new(_roots.Select(r => r.Bounds));
    }
    public SelectionQueryResult Window(Vec3 first, Vec3 second, bool crossing)
    {
        if (!first.IsFinite || !second.IsFinite) throw new ArgumentException("Selection corners must be finite.");
        var box = Bounds3.From([first, second]); var candidates = new List<int>();
        var visited = _index.Query(box, candidates, xyOnly: true); candidates.Sort();
        var result = ImmutableArray.CreateBuilder<Guid>();
        foreach (var i in candidates)
        {
            var root = _roots[i];
            var contained = box.ContainsXY(root.Bounds.Min) && box.ContainsXY(root.Bounds.Max);
            if (contained || crossing && (root.Paths.Any(p => Crosses(p.Points, p.Closed, p.Filled, box)) ||
                root.Texts.Any(t => Crosses(TextCorners(t), true, true, box)) ||
                root.Triangles.Any(t => Crosses([t.A, t.B, t.C], true, true, box)))) result.Add(root.Id);
        }
        return new(result.ToImmutable(), candidates.Count, visited);
    }
    public ImmutableArray<Guid> Pick(Vec3 point, double tolerance)
    {
        if (!point.IsFinite || !double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentException("Invalid pick point/tolerance.");
        var box = new Bounds3(point - new Vec3(tolerance, tolerance, tolerance), point + new Vec3(tolerance, tolerance, tolerance));
        var candidates = new List<int>(); _index.Query(box, candidates, true);
        var result = new List<(Guid Id, double Distance, int Order)>();
        foreach (var i in candidates)
        {
            var root = _roots[i]; var distance = double.PositiveInfinity;
            foreach (var path in root.Paths)
            {
                if (path.Filled && path.Closed && GeometryMath.PointInPolygon(point, path.Points)) distance = 0;
                for (var j = 0; j < path.Points.Length; j++)
                {
                    var a = path.Points[j] with { Z = point.Z };
                    var b = path.Points[(j + 1) % path.Points.Length] with { Z = point.Z };
                    var d = j + 1 < path.Points.Length || path.Closed ? GeometryMath.NearestOnSegment(point, a, b).DistanceTo(point) : a.DistanceTo(point);
                    distance = Math.Min(distance, d);
                }
            }
            if (root.Texts.Any(t => GeometryMath.PointInPolygon(point, TextCorners(t))) ||
                root.Triangles.Any(t => GeometryMath.PointInPolygon(point, new[] { t.A, t.B, t.C }))) distance = 0;
            if (distance <= tolerance) result.Add((root.Id, distance, i));
        }
        return result.OrderBy(p => p.Distance).ThenByDescending(p => p.Order).Select(p => p.Id).ToImmutableArray();
    }
    private static ImmutableArray<Vec3> TextCorners(SceneText text)
    {
        // Conservative glyph-cell envelope; exact shaped font metrics are not available in the engine.
        var lines = text.Text.Split('\n'); var x = text.AxisX * (text.Height * Math.Max(1, lines.Max(l => l.Length)) * 1.5);
        var top = text.Position + text.AxisY * text.Height;
        var bottom = text.Position - text.AxisY * (text.Height * (.3 + (lines.Length - 1) * 1.3));
        return [top, top + x, bottom + x, bottom];
    }
    public static bool Crosses(IReadOnlyList<Vec3> points, bool closed, bool filled, Bounds3 box)
    {
        if (points.Any(box.ContainsXY)) return true;
        for (var i = 0; i < points.Count - (closed ? 0 : 1); i++)
            if (SegmentIntersects(points[i], points[(i + 1) % points.Count], box)) return true;
        return filled && closed && points.Count >= 3 && GeometryMath.PointInPolygon(box.Center, points);
    }
    private static bool SegmentIntersects(Vec3 a, Vec3 b, Bounds3 box)
    {
        double near = 0, far = 1;
        bool Slab(double start, double delta, double min, double max)
        {
            if (delta == 0) return start >= min && start <= max;
            var x = (min - start) / delta; var y = (max - start) / delta;
            if (x > y) (x, y) = (y, x);
            near = Math.Max(near, x); far = Math.Min(far, y); return near <= far;
        }
        return Slab(a.X, b.X - a.X, box.Min.X, box.Max.X) && Slab(a.Y, b.Y - a.Y, box.Min.Y, box.Max.Y);
    }
}

/// <summary>Selection operations are transient; drawing edits still go through CadDocument transactions.</summary>
public sealed partial class CadSession
{
    private ImmutableArray<Entity> _indexedEntities;
    private readonly Dictionary<Guid, (Entity Entity, int Order)> _entityLookup = new();
    public long SelectionRevision { get; private set; }
    public SelectionQueryResult LastWindowQuery { get; private set; }
    public bool SelectionCycling { get; set; }
    private void EnsureEntityLookup()
    {
        var entities = Document.Drawing.Entities; if (_indexedEntities == entities) return;
        _entityLookup.Clear(); for (var i = 0; i < entities.Length; i++) _entityLookup.Add(entities[i].Id, (entities[i], i));
        _indexedEntities = entities;
    }
    public Entity? FindEntity(Guid id) { EnsureEntityLookup(); return _entityLookup.TryGetValue(id, out var value) ? value.Entity : null; }
    public Entity[] SelectedEntities()
    {
        EnsureEntityLookup();
        return _selection.Where(_entityLookup.ContainsKey).Select(id => _entityLookup[id]).OrderBy(p => p.Order).Select(p => p.Entity).ToArray();
    }
    private void PruneSelection()
    {
        if (!Document.Drawing.Layers.ContainsKey(CurrentLayer)) CurrentLayer = "0";
        if (!CurrentLinetype.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) &&
            !CurrentLinetype.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase) &&
            !Document.Drawing.Linetypes.ContainsKey(CurrentLinetype)) CurrentLinetype = "BYLAYER";
        EnsureEntityLookup();
        if (_selection.RemoveWhere(id => !_entityLookup.TryGetValue(id, out var e) || !IsVisible(e.Entity)) != 0) SelectionRevision++;
        Changed?.Invoke();
    }
    public void ApplySelection(IEnumerable<Guid> ids, SelectionMode mode = SelectionMode.Replace)
    {
        ArgumentNullException.ThrowIfNull(ids); EnsureEntityLookup();
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var valid = ids.Where(id => _entityLookup.TryGetValue(id, out var e) && IsVisible(e.Entity)).ToHashSet();
        var next = mode == SelectionMode.Replace ? valid : new HashSet<Guid>(_selection);
        switch (mode)
        {
            case SelectionMode.Add: next.UnionWith(valid); break;
            case SelectionMode.Remove: next.ExceptWith(valid); break;
            case SelectionMode.Toggle: next.SymmetricExceptWith(valid); break;
        }
        if (_selection.SetEquals(next)) return;
        _selection.Clear(); _selection.UnionWith(next); SelectionRevision++; Changed?.Invoke();
    }
    public void SelectWindow(Vec3 a, Vec3 b, bool crossing, SelectionMode mode)
    {
        LastWindowQuery = SelectionQueries.For(Scene).Window(a, b, crossing); ApplySelection(LastWindowQuery.EntityIds, mode);
    }
    public void QuickSelect(string? kind = null, string? layer = null, SelectionMode mode = SelectionMode.Replace, bool selectionOnly = false)
    {
        var entities = selectionOnly ? SelectedEntities().AsEnumerable() : Document.Drawing.Entities;
        ApplySelection(entities.Where(e => IsVisible(e) && (string.IsNullOrWhiteSpace(kind) || kind == "*" || e.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(layer) || layer == "*" || e.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase))).Select(e => e.Id), mode);
    }
    public void SelectSimilar()
    {
        var keys = SelectedEntities().Select(e => (e.Kind.ToUpperInvariant(), e.Layer.ToUpperInvariant())).ToHashSet();
        if (keys.Count == 0) throw new InvalidOperationException("Select a source object first.");
        ApplySelection(Document.Drawing.Entities.Where(e => IsVisible(e) && keys.Contains((e.Kind.ToUpperInvariant(), e.Layer.ToUpperInvariant()))).Select(e => e.Id));
    }
}
