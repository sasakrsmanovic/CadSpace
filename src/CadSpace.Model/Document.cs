using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>Atomic, immutable document revisions with bounded undo history. No UI or persistence dependency.</summary>
public sealed class CadDocument
{
    private readonly Stack<(string Name, Drawing State)> _undo = new();
    private readonly Stack<(string Name, Drawing State)> _redo = new();
    private Drawing _saved;
    public CadDocument(Drawing? drawing = null) { Drawing = drawing ?? Drawing.Empty; _saved = Drawing; Validate(Drawing); }
    public Drawing Drawing { get; private set; }
    public long Revision { get; private set; }
    public bool IsDirty => Drawing != _saved;
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public event Action? Changed;
    public void Edit(string name, Func<Drawing, Drawing> edit)
    {
        var next = edit(Drawing);
        if (next == Drawing) return;
        Validate(next);
        _undo.Push((name, Drawing)); _redo.Clear(); Drawing = next; Notify();
        if (_undo.Count > 256)
        {
            var retained = _undo.Take(256).Reverse().ToArray(); _undo.Clear();
            foreach (var item in retained) _undo.Push(item);
        }
    }
    public void Add(string name, params Entity[] entities) => Edit(name, s => s with { Entities = s.Entities.AddRange(entities) });
    public void Undo() { if (_undo.TryPop(out var p)) { _redo.Push((p.Name, Drawing)); Drawing = p.State; Notify(); } }
    public void Redo() { if (_redo.TryPop(out var p)) { _undo.Push((p.Name, Drawing)); Drawing = p.State; Notify(); } }
    public void MarkSaved() { _saved = Drawing; Changed?.Invoke(); }
    public void Load(Drawing drawing) { Validate(drawing); Drawing = drawing; _saved = drawing; _undo.Clear(); _redo.Clear(); Notify(); }
    private void Notify() { Revision++; Changed?.Invoke(); }
    public static void Validate(Drawing drawing)
    {
        if (!drawing.Layers.ContainsKey("0")) throw new ArgumentException("Layer 0 is required.");
        if (drawing.Entities.Select(e => e.Id).Distinct().Count() != drawing.Entities.Length) throw new ArgumentException("Duplicate entity IDs.");
        foreach (var layer in drawing.Layers.Values)
            if (string.IsNullOrWhiteSpace(layer.Name) || !double.IsFinite(layer.LineWeight) || layer.LineWeight < 0) throw new ArgumentException("Invalid layer.");
        foreach (var entity in drawing.Entities.Concat(drawing.Blocks.Values.SelectMany(b => b.Entities)))
        {
            if (!drawing.Layers.ContainsKey(entity.Layer)) throw new ArgumentException($"Missing layer: {entity.Layer}");
            if (EntityGeometry.Anchors(entity).Any(p => !p.IsFinite)) throw new ArgumentException("Coordinates must be finite.");
            if (entity is CircleEntity c && (!double.IsFinite(c.Radius) || c.Radius <= 0)) throw new ArgumentException("Circle radius must be positive.");
            if (entity is ArcEntity a && (!double.IsFinite(a.Radius) || a.Radius <= 0 || !double.IsFinite(a.StartAngle) || !double.IsFinite(a.EndAngle))) throw new ArgumentException("Invalid arc.");
            if (entity is EllipseEntity ell && (ell.MajorAxis.Length <= 1e-9 || !double.IsFinite(ell.Ratio) || ell.Ratio <= 0)) throw new ArgumentException("Invalid ellipse.");
            if (entity is TextEntity t && (!double.IsFinite(t.Height) || t.Height <= 0)) throw new ArgumentException("Text height must be positive.");
            if (entity is PolylineEntity p && (p.Vertices.Length < 2 || p.Vertices.Any(v => !double.IsFinite(v.Bulge)))) throw new ArgumentException("Invalid polyline.");
            if (entity is MeshEntity m && (m.Triangles.Length % 3 != 0 || m.Triangles.Any(i => i < 0 || i >= m.Vertices.Length))) throw new ArgumentException("Invalid mesh indices.");
            if (entity is BlockReferenceEntity b && (!b.Scale.IsFinite || Math.Abs(b.Scale.X * b.Scale.Y * b.Scale.Z) < 1e-15 || !double.IsFinite(b.Rotation))) throw new ArgumentException("Invalid block transform.");
        }
        void Visit(string name, HashSet<string> ancestors)
        {
            if (!drawing.Blocks.TryGetValue(name, out var block)) return; // External/opaque references can be retained.
            if (!ancestors.Add(name)) throw new ArgumentException($"Cyclic block reference: {name}");
            if (ancestors.Count > 32) throw new ArgumentException("Block nesting exceeds 32 levels.");
            foreach (var insert in block.Entities.OfType<BlockReferenceEntity>()) Visit(insert.Name, ancestors);
            ancestors.Remove(name);
        }
        foreach (var name in drawing.Blocks.Keys) Visit(name, new(StringComparer.OrdinalIgnoreCase));
    }
}
