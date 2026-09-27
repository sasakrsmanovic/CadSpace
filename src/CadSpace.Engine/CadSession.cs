using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public enum SnapKind { None, Endpoint, Midpoint, Center, Quadrant, Nearest, Grid, Intersection, Perpendicular, Tangent }
public readonly record struct SnapResult(Vec3 Point, SnapKind Kind, Guid EntityId = default);

/// <summary>Reusable editing context; all document changes are transactions on CadDocument.</summary>
public sealed partial class CadSession
{
    private DrawingScene? _scene;
    private readonly DrawingSceneCache _sceneCache = new();
    private SnapIndex? _snaps;
    public int RebuiltSceneRoots => _sceneCache.RebuiltRoots;
    public ObjectSnapModes SnapModes { get; set; } = ObjectSnapModes.Default;
    public bool DynamicInput { get; set; } = true;
    private long _sceneRevision = -1;
    private readonly HashSet<Guid> _selection = new();
    public CadSession(CadDocument? document = null)
    {
        Document = document ?? new();
        Document.Changed += PruneSelection;
    }
    public CadDocument Document { get; }
    public IReadOnlySet<Guid> Selection => _selection;
    private string _activeLayout = "Model";
    public string ActiveLayout
    {
        get => _activeLayout;
        set { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A layout name is required."); if (_activeLayout == value) return; _activeLayout = value; _scene = null; _snaps = null; _selection.Clear(); SelectionRevision++; Changed?.Invoke(); }
    }
    public IEnumerable<string> AvailableLayouts => Document.Drawing.LayoutBlockNames.Keys.Concat(Document.Drawing.Entities.Select(e => e.Layout)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n == "Model" ? 0 : 1).ThenBy(n => n);
    public bool IsVisible(Entity entity) => entity.Visible && entity.Layout.Equals(ActiveLayout, StringComparison.OrdinalIgnoreCase) && Document.Drawing.LayerFor(entity).Visible;
    public string CurrentLayer { get; set; } = "0";
    public bool GridVisible { get; set; } = true;
    public bool GridSnap { get; set; }
    public bool ObjectSnap { get; set; } = true;
    public bool Ortho { get; set; }
    public bool Polar { get; set; }
    public double GridSpacing { get; set; } = 10;
    public event Action? Changed;
    public DrawingScene Scene
    {
        get { if (_scene == null || _sceneRevision != Document.Revision) { _scene = _sceneCache.Build(Document.Drawing, ActiveLayout); _sceneRevision = Document.Revision; _snaps = null; } return _scene; }
    }
    public void Invalidate() => Changed?.Invoke();
    public void Select(Guid? id, bool additive = false) => ApplySelection(id is Guid value ? [value] : [], additive ? SelectionMode.Toggle : SelectionMode.Replace);
    public void SelectAll() => ApplySelection(Document.Drawing.Entities.Where(IsVisible).Select(e => e.Id), SelectionMode.Add);
    public void SelectWindow(Vec3 a, Vec3 b, bool crossing, bool additive = false) => SelectWindow(a, b, crossing, additive ? SelectionMode.Add : SelectionMode.Replace);
    public Guid? HitTest(Vec3 point, double tolerance)
    {
        if(!point.IsFinite || !double.IsFinite(tolerance) || tolerance<=0)throw new ArgumentException("Invalid pick coordinate or tolerance.");
        Guid? nearest = null; var distance = tolerance;
        var candidates = new List<int>();
        var box = new Bounds3(point - new Vec3(tolerance,tolerance,tolerance), point + new Vec3(tolerance,tolerance,tolerance));
        var scene = Scene; var index = SceneAcceleration.For(scene);
        index.Paths.Query(box,candidates,xyOnly:true); candidates.Sort();
        foreach (var number in candidates)
        {
            var path = scene.Paths[number];
            if (path.Filled && GeometryMath.PointInPolygon(point, path.Points)) { nearest = path.EntityId; distance = 0; }
            for (var i = 0; i < path.Points.Length; i++)
            {
                var a = path.Points[i] with { Z = point.Z }; var b = path.Points[(i + 1) % path.Points.Length] with { Z = point.Z };
                var candidate = i + 1 < path.Points.Length || path.Closed ? GeometryMath.NearestOnSegment(point, a, b) : a;
                var d = candidate.DistanceTo(point); if (d <= distance) { distance = d; nearest = path.EntityId; }
            }
        }
        candidates.Clear(); index.Texts.Query(box,candidates,xyOnly:true); candidates.Sort();
        foreach (var number in candidates)
        {
            var text = scene.Texts[number];
            var delta = point - text.Position; var det = GeometryMath.Cross2(text.AxisX, text.AxisY);
            if (Math.Abs(det) < 1e-12) continue;
            var x = GeometryMath.Cross2(delta, text.AxisY) / det; var y = GeometryMath.Cross2(text.AxisX, delta) / det;
            var lines = text.Text.Split('\n');
            if (x >= 0 && x <= Math.Max(1, lines.Max(l => l.Length)) * text.Height * .65 && y >= -text.Height * (.3 + (lines.Length - 1) * 1.3) && y <= text.Height) nearest = text.EntityId;
        }
        return nearest;
    }
    public SnapResult Snap(Vec3 point, double tolerance, Vec3? reference = null)
    {
        if(!point.IsFinite || !double.IsFinite(tolerance) || tolerance<=0)throw new ArgumentException("Invalid snap coordinate or tolerance.");
        var result = new SnapResult(point, SnapKind.None);
        if (ObjectSnap)
        {
            var scene = Scene; // establishes revision and invalidates the old snap index first
            _snaps ??= new SnapIndex(Document.Drawing, scene, ActiveLayout);
            result = _snaps.Find(point, tolerance, reference, SnapModes);
        }
        if (result.Kind != SnapKind.None) return result;
        if (reference is Vec3 origin && (Ortho || Polar))
        {
            var delta = point - origin; var angle = GeometryMath.Angle(delta);
            var step = Ortho ? 90 : 45; var target = Math.Round(angle / step) * step;
            if (Ortho || Math.Abs(GeometryMath.NormalizeAngle(angle - target + 180) - 180) < 5)
            {
                var axis = GeometryMath.OnCircle(default, 1, target); point = origin + axis * delta.Dot(axis);
            }
        }
        if (GridSnap && GridSpacing > 0) return new(new(Math.Round(point.X / GridSpacing) * GridSpacing, Math.Round(point.Y / GridSpacing) * GridSpacing, point.Z), SnapKind.Grid);
        return new(point, SnapKind.None);
    }
    public Entity[] EditableSelection()
    {
        var selected = SelectedEntities();
        if (selected.Length == 0) throw new InvalidOperationException("Select objects first.");
        if (selected.Any(e => Document.Drawing.LayerFor(e).Locked)) throw new InvalidOperationException("Selection contains objects on locked layers.");
        if (selected.Any(e => e is OpaqueEntity)) throw new NotSupportedException("Opaque DXF objects cannot be edited.");
        return selected;
    }
    public void Add(string command, params Entity[] entities)
    {
        if (!Document.Drawing.Layers.TryGetValue(CurrentLayer, out var layer)) CurrentLayer = "0";
        else if (layer.Locked) throw new InvalidOperationException("The current layer is locked.");
        Document.Add(command, entities.Select(e => e with { Layer = CurrentLayer, Layout = ActiveLayout }).ToArray());
    }
    public void TransformSelection(string name, Transform3 transform, bool copy = false)
    {
        var selected = EditableSelection(); var changed = selected.Select(e => EntityGeometry.Transform(e, transform, copy)).ToArray();
        var replacements = changed.ToDictionary(e => e.Id);
        Document.Edit(name, s => s with { Entities = copy ? s.Entities.AddRange(changed) : s.Entities.Select(e => replacements.GetValueOrDefault(e.Id) ?? e).ToImmutableArray() });
    }
    public void Erase()
    {
        var ids = EditableSelection().Select(e => e.Id).ToHashSet();
        Document.Edit("Erase", s => s with { Entities = s.Entities.Where(e => !ids.Contains(e.Id)).ToImmutableArray() });
    }
    public void CreateBlock(string name, Vec3 basePoint)
    {
        var selected = EditableSelection();
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\r', '\n', '*', '/', '\\', ':']) >= 0) throw new ArgumentException("Enter a valid block name.");
        if (Document.Drawing.Blocks.ContainsKey(name)) throw new ArgumentException("That block name already exists.");
        var ids = selected.Select(e => e.Id).ToHashSet();
        Document.Edit("Block", s => s with { Blocks = s.Blocks.Add(name, new(name, basePoint, selected.ToImmutableArray())), Entities = s.Entities.Where(e => !ids.Contains(e.Id)).Append(new BlockReferenceEntity(name, basePoint, new(1, 1, 1)) { Layer = CurrentLayer, Layout = ActiveLayout }).ToImmutableArray() });
    }
    public void Explode()
    {
        var selected = EditableSelection(); var result = new List<Entity>();
        foreach (var entity in selected)
        {
            switch (entity)
            {
                case PolylineEntity poly:
                    for (var i = 0; i < poly.Vertices.Length - (poly.Closed ? 0 : 1); i++)
                    {
                        var a = poly.Vertices[i]; var b = poly.Vertices[(i + 1) % poly.Vertices.Length];
                        result.Add(AdvancedEditing.ExplodeSegment(a,b) with { Layer=poly.Layer, TrueColor=poly.TrueColor, ColorIndex=poly.ColorIndex, LineWeight=poly.LineWeight, Layout=poly.Layout });
                    }
                    break;
                case BlockReferenceEntity insert when Document.Drawing.Blocks.TryGetValue(insert.Name, out var block):
                    var transform = Transform3.Translation(-block.BasePoint).Then(Transform3.Scaling(insert.Scale)).Then(Transform3.RotationZ(insert.Rotation)).Then(Transform3.Translation(insert.Position));
                    foreach (var child in block.Entities) result.Add(EntityGeometry.Transform(child, transform, true) with { Layer = child.Layer == "0" ? insert.Layer : child.Layer, Layout = insert.Layout });
                    break;
                default: throw new NotSupportedException($"Explode is not implemented for {entity.Kind}.");
            }
        }
        var ids = selected.Select(e => e.Id).ToHashSet();
        Document.Edit("Explode", s => s with { Entities = s.Entities.Where(e => !ids.Contains(e.Id)).Concat(result).ToImmutableArray() });
    }
    public void Offset(double distance)
    {
        if (!double.IsFinite(distance) || Math.Abs(distance) < 1e-9) throw new ArgumentException("Offset must be nonzero.");
        var copies = EditableSelection().Select(e => e switch
        {
            LineEntity line => EntityGeometry.Transform(line, Transform3.Translation(new Vec3(-(line.End - line.Start).Y, (line.End - line.Start).X).Normalized * distance), true),
            CircleEntity circle when circle.Radius + distance > 0 => circle with { Radius = circle.Radius + distance, Id = Guid.NewGuid(), Handle = "" },
            ArcEntity arc when arc.Radius + distance > 0 => arc with { Radius = arc.Radius + distance, Id = Guid.NewGuid(), Handle = "" },
            _ => throw new NotSupportedException("Offset supports lines, circles and arcs with a positive resulting radius.")
        }).ToArray();
        Document.Add("Offset", copies);
    }
    public void BooleanSelection(MeshBooleanOperation operation)
    {
        var selected = EditableSelection();
        if (selected.Length < 2 || selected.Any(e => e is not MeshEntity)) throw new ArgumentException("Select at least two closed meshes. Subtract uses the first mesh in drawing order as its base.");
        var result = (MeshEntity)selected[0];
        foreach (var operand in selected.Skip(1).Cast<MeshEntity>())
        {
            if (result.Triangles.IsEmpty && operation != MeshBooleanOperation.Union) break;
            result = MeshBoolean.Apply(result, operand, operation);
        }
        result = result with { Layer = selected[0].Layer, Layout = selected[0].Layout, TrueColor = selected[0].TrueColor, ColorIndex = selected[0].ColorIndex, LineWeight = selected[0].LineWeight };
        var ids = selected.Select(e => e.Id).ToHashSet();
        Document.Edit("Mesh " + operation, d => d with { Entities = d.Entities.Where(e => !ids.Contains(e.Id)).Concat(result.Triangles.IsEmpty ? Array.Empty<Entity>() : new Entity[] { result }).ToImmutableArray() });
        Select(result.Triangles.IsEmpty ? null : result.Id);
    }
    public void Extrude(double height)
    {
        var meshes = EditableSelection().Select(e => e switch
        {
            PolylineEntity { Closed: true } p => MeshFactory.Extrude(EntityGeometry.PolylinePoints(p), height) with { Layer = p.Layer, TrueColor = p.TrueColor, Layout = p.Layout },
            CircleEntity c => MeshFactory.Cylinder(c.Center, c.Radius, height) with { Layer = c.Layer, TrueColor = c.TrueColor, Layout = c.Layout },
            _ => throw new NotSupportedException("Extrude requires closed XY polylines or circles.")
        }).ToArray();
        Document.Add("Extrude", meshes);
    }
}
