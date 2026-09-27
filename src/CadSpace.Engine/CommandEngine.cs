using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public sealed record CommandInfo(string Name, string Alias, string Description, string Category);

/// <summary>Stateful CAD prompts shared by pointer input, command line, and scripts.</summary>
public sealed class CommandEngine(CadSession session)
{
    private readonly List<Vec3> _points = new();
    private string _active = "";
    private string _text = "";
    private double _radius;
    public CadSession Session { get; } = session;
    public string ActiveCommand => _active;
    public bool IsActive => _active.Length != 0;
    public Vec3? ReferencePoint => _points.Count > 0 ? _points[^1] : null;
    public string Prompt { get; private set; } = "Type a command";
    public event Action<string>? Message;
    public event Action? Changed;
    public event Action<string>? ViewRequested;
    public static IReadOnlyList<CommandInfo> Commands { get; } = new CommandInfo[]
    {
        new("LINE", "L", "Connected line segments", "Draw"), new("PLINE", "PL", "Polyline; Enter finishes, C closes", "Draw"), new("RECTANG", "REC", "Rectangle from two corners", "Draw"),
        new("CIRCLE", "C", "Center and radius", "Draw"), new("ARC", "A", "Arc through three points", "Draw"), new("POINT", "PO", "Model-space point", "Draw"),
        new("TEXT", "T", "Single-line text", "Annotate"), new("DIMALIGNED", "DAL", "Aligned dimension", "Annotate"), new("HATCH", "H", "Hatch selected closed polyline", "Annotate"),
        new("MOVE", "M", "Move selected objects", "Modify"), new("COPY", "CO", "Copy selected objects", "Modify"), new("ROTATE", "RO", "Rotate about base point", "Modify"),
        new("SCALE", "SC", "Uniform scaling about base point", "Modify"), new("MIRROR", "MI", "Mirror about two-point XY axis", "Modify"), new("OFFSET", "O", "Signed line/arc/circle offset", "Modify"),
        new("ERASE", "E", "Erase selected objects", "Modify"), new("EXPLODE", "X", "Explode blocks or straight polylines", "Modify"), new("ARRAY", "AR", "Rectangular array", "Modify"),
        new("BLOCK", "B", "Create block from selection", "Blocks"), new("INSERT", "I", "Insert block definition", "Blocks"),
        new("BOX", "BOX", "Triangle-mesh box", "Model"), new("CYLINDER", "CYL", "Triangle-mesh cylinder", "Model"), new("SPHERE", "SPH", "Triangle-mesh sphere", "Model"), new("CONE", "CONE", "Triangle-mesh cone", "Model"),
        new("EXTRUDE", "EXT", "Extrude closed XY profiles", "Model"), new("REVOLVE", "REV", "Revolve polyline surface about an axis", "Model"),
        new("DIST", "DI", "Distance between points", "Measure"), new("AREA", "AA", "Selected polyline area", "Measure"),
        new("UNDO", "U", "Undo transaction", "Edit"), new("REDO", "REDO", "Redo transaction", "Edit"), new("SELECTALL", "ALL", "Select visible objects", "Edit"),
        new("ZOOM", "Z", "Zoom extents", "View"), new("TOP", "TOP", "Top drafting view", "View"), new("3DORBIT", "3DO", "3D model viewport", "View"), new("HELP", "?", "List supported commands", "Help")
    };
    public void Cancel() { _active = ""; _points.Clear(); _text = ""; Prompt = "Type a command"; Changed?.Invoke(); }
    public void Start(string command)
    {
        Cancel(); Submit(command);
    }
    public void Submit(string input)
    {
        input = input.Trim();
        try
        {
            if (!IsActive)
            {
                if (input.Length == 0) return;
                var definition = Commands.FirstOrDefault(c => c.Name.Equals(input, StringComparison.OrdinalIgnoreCase) || c.Alias.Equals(input, StringComparison.OrdinalIgnoreCase));
                if (definition == null) throw new ArgumentException($"Unknown command: {input}. Type HELP for implemented commands.");
                _active = definition.Name; Message?.Invoke($"Command: {_active}");
                switch (_active)
                {
                    case "UNDO": Session.Document.Undo(); Cancel(); return;
                    case "REDO": Session.Document.Redo(); Cancel(); return;
                    case "ERASE": Session.Erase(); Cancel(); return;
                    case "EXPLODE": Session.Explode(); Cancel(); return;
                    case "SELECTALL": Session.SelectAll(); Cancel(); return;
                    case "HELP": Message?.Invoke(string.Join("  ·  ", Commands.Select(c => $"{c.Name} ({c.Alias})"))); Cancel(); return;
                    case "ZOOM": case "TOP": case "3DORBIT": ViewRequested?.Invoke(_active); Cancel(); return;
                    case "AREA":
                        foreach (var poly in Session.EditableSelection().OfType<PolylineEntity>()) Message?.Invoke($"Area = {Math.Abs(GeometryMath.SignedArea(EntityGeometry.PolylinePoints(poly))):0.###} square units"); Cancel(); return;
                    case "HATCH":
                        var hatches = Session.EditableSelection().Select(e => e is PolylineEntity { Closed: true } p ? new HatchEntity(EntityGeometry.PolylinePoints(p)) { Layer = p.Layer } : throw new ArgumentException("Hatch requires closed polylines.")).ToArray();
                        Session.Document.Add("Hatch", hatches); Cancel(); return;
                    case "MOVE": case "COPY": case "ROTATE": case "SCALE": case "MIRROR": case "OFFSET": case "ARRAY": case "EXTRUDE": case "REVOLVE": case "BLOCK": Session.EditableSelection(); break;
                }
                UpdatePrompt(); return;
            }
            if (input.Length == 0)
            {
                if (_active == "PLINE" && _points.Count >= 2) Session.Add("Polyline", PolylineEntity.FromPoints(_points));
                Cancel(); return;
            }
            if (_active == "PLINE" && input.Equals("C", StringComparison.OrdinalIgnoreCase))
            {
                if (_points.Count < 3) throw new ArgumentException("At least three vertices are required to close the polyline.");
                Session.Add("Polyline", PolylineEntity.FromPoints(_points, true)); Cancel(); return;
            }
            if (_active is "BLOCK" or "INSERT" && _text.Length == 0) { _text = input; if (_active == "INSERT" && !Session.Document.Drawing.Blocks.ContainsKey(_text)) throw new ArgumentException("Block not found."); UpdatePrompt(); return; }
            if (_active == "TEXT" && _points.Count == 1) { Session.Add("Text", new TextEntity(_points[0], input, 12)); Cancel(); return; }
            if (_active == "ARRAY") { Array(input); Cancel(); return; }
            if (GeometryMath.TryParsePoint(input, ReferencePoint ?? default, out var p)) { Point(p); return; }
            if (GeometryMath.Number(input, out var number)) { Number(number); return; }
            throw new ArgumentException("Enter a coordinate (x,y or @dx,dy), number, or the prompted keyword.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Message?.Invoke(ex.Message); Cancel();
        }
    }
    public void Point(Vec3 point)
    {
        if (!IsActive || !point.IsFinite) return;
        try
        {
            if (_active is "OFFSET" or "EXTRUDE" or "ARRAY" || (_active is "BLOCK" or "INSERT" && _text.Length == 0)) { Message?.Invoke(Prompt); return; }
            if (_active == "TEXT" && _points.Count == 1) { Message?.Invoke("Enter the text in the command line."); return; }
            _points.Add(point);
            switch (_active)
            {
                case "POINT": Session.Add("Point", new PointEntity(point)); Cancel(); return;
                case "LINE" when _points.Count == 2:
                    if (_points[0].DistanceTo(point) > 1e-9) Session.Add("Line", new LineEntity(_points[0], point)); _points.RemoveAt(0); break;
                case "RECTANG" when _points.Count == 2:
                    var a = _points[0]; var b = point; Session.Add("Rectangle", PolylineEntity.FromPoints(new Vec3[] { a, new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z) }, true)); Cancel(); return;
                case "CIRCLE" when _points.Count == 2: Session.Add("Circle", new CircleEntity(_points[0], _points[0].DistanceTo(point))); Cancel(); return;
                case "ARC" when _points.Count == 3:
                    var circle = GeometryMath.CircleThrough(_points[0], _points[1], _points[2]); var start = GeometryMath.Angle(_points[0] - circle.Center); var end = GeometryMath.Angle(point - circle.Center); var middle = GeometryMath.Angle(_points[1] - circle.Center);
                    if (GeometryMath.NormalizeAngle(middle - start) > GeometryMath.NormalizeAngle(end - start)) (start, end) = (end, start);
                    Session.Add("Arc", new ArcEntity(circle.Center, circle.Radius, start, end)); Cancel(); return;
                case "DIMALIGNED" when _points.Count == 3: Session.Add("Dimension", new DimensionEntity(_points[0], _points[1], point)); Cancel(); return;
                case "MOVE" or "COPY" when _points.Count == 2: Session.TransformSelection(_active, Transform3.Translation(point - _points[0]), _active == "COPY"); Cancel(); return;
                case "MIRROR" when _points.Count == 2: Session.TransformSelection("Mirror", Transform3.MirrorXY(_points[0], point)); Cancel(); return;
                case "BLOCK": Session.CreateBlock(_text, point); Cancel(); return;
                case "INSERT": Session.Add("Insert", new BlockReferenceEntity(_text, point, new(1, 1, 1))); Cancel(); return;
                case "SPHERE" when _points.Count == 2: Session.Add("Sphere", MeshFactory.Sphere(_points[0], _points[0].DistanceTo(point))); ViewRequested?.Invoke("3DORBIT"); Cancel(); return;
                case "CYLINDER" or "CONE" when _points.Count == 2: _radius = _points[0].DistanceTo(point); break;
                case "DIST" when _points.Count == 2: Message?.Invoke($"Distance = {_points[0].DistanceTo(point):0.######}; delta = {point - _points[0]}"); Cancel(); return;
            }
            UpdatePrompt();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { Message?.Invoke(ex.Message); Cancel(); }
    }
    private void Number(double value)
    {
        switch (_active)
        {
            case "CIRCLE" when _points.Count == 1: Session.Add("Circle", new CircleEntity(_points[0], value)); break;
            case "ROTATE" when _points.Count == 1: Session.TransformSelection("Rotate", Transform3.RotationZ(value, _points[0])); break;
            case "SCALE" when _points.Count == 1:
                if (value <= 0) throw new ArgumentException("Scale must be positive."); Session.TransformSelection("Scale", Transform3.Scaling(new(value, value, value), _points[0])); break;
            case "OFFSET": Session.Offset(value); break;
            case "EXTRUDE": Session.Extrude(value); ViewRequested?.Invoke("3DORBIT"); break;
            case "BOX" when _points.Count == 2: Session.Add("Box", MeshFactory.Box(_points[0], new(_points[1].X, _points[1].Y, _points[0].Z + value))); ViewRequested?.Invoke("3DORBIT"); break;
            case "SPHERE" when _points.Count == 1: Session.Add("Sphere", MeshFactory.Sphere(_points[0], value)); ViewRequested?.Invoke("3DORBIT"); break;
            case "CYLINDER" or "CONE" when _points.Count == 1:
                if (value <= 0) throw new ArgumentException("Radius must be positive."); _radius = value; _points.Add(_points[0] + Vec3.UnitX * value); UpdatePrompt(); return;
            case "CYLINDER" when _points.Count == 2: Session.Add("Cylinder", MeshFactory.Cylinder(_points[0], _radius, value)); ViewRequested?.Invoke("3DORBIT"); break;
            case "CONE" when _points.Count == 2: Session.Add("Cone", MeshFactory.Cone(_points[0], _radius, value)); ViewRequested?.Invoke("3DORBIT"); break;
            case "REVOLVE" when _points.Count == 2:
                var meshes = Session.EditableSelection().Select(e => e is PolylineEntity p ? MeshFactory.Revolve(EntityGeometry.PolylinePoints(p), _points[0], _points[1], value) with { Layer = p.Layer } : throw new ArgumentException("Revolve requires polylines.")).ToArray();
                Session.Document.Add("Revolve", meshes); ViewRequested?.Invoke("3DORBIT"); break;
            default: throw new ArgumentException("A point is required before the numeric parameter.");
        }
        Cancel();
    }
    private void Array(string input)
    {
        var values = input.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length != 4 || !int.TryParse(values[0], out var columns) || !int.TryParse(values[1], out var rows) || !GeometryMath.Number(values[2], out var dx) || !GeometryMath.Number(values[3], out var dy) || columns < 1 || rows < 1 || (long)columns * rows > 10000) throw new ArgumentException("Use columns,rows,x-spacing,y-spacing; maximum 10,000 instances.");
        var selection = Session.EditableSelection();
        if ((long)selection.Length * rows * columns > 100000) throw new ArgumentException("The array exceeds 100,000 objects.");
        var copies = new List<Entity>();
        for (var y = 0; y < rows; y++) for (var x = 0; x < columns; x++) if (x != 0 || y != 0) copies.AddRange(selection.Select(e => EntityGeometry.Transform(e, Transform3.Translation(new(x * dx, y * dy)), true)));
        Session.Document.Add("Array", copies.ToArray());
    }
    private void UpdatePrompt()
    {
        Prompt = _active switch
        {
            "PLINE" => _points.Count == 0 ? "Specify start point" : "Specify next point or [Close]; Enter to finish",
            "LINE" => _points.Count == 0 ? "Specify first point" : "Specify next point; Enter to finish",
            "CIRCLE" or "SPHERE" or "CYLINDER" or "CONE" => _points.Count == 0 ? "Specify center point" : _points.Count == 1 ? "Specify radius or radius point" : "Specify height",
            "TEXT" => _points.Count == 0 ? "Specify insertion point" : "Enter text",
            "ROTATE" => _points.Count == 0 ? "Specify base point" : "Specify rotation angle in degrees",
            "SCALE" => _points.Count == 0 ? "Specify base point" : "Specify scale factor",
            "OFFSET" => "Specify signed offset distance (positive is left / outward)",
            "EXTRUDE" => "Specify extrusion height",
            "ARRAY" => "Enter columns,rows,x-spacing,y-spacing",
            "BLOCK" or "INSERT" => _text.Length == 0 ? "Enter block name" : "Specify insertion/base point",
            "BOX" => _points.Count == 0 ? "Specify first base corner" : _points.Count == 1 ? "Specify opposite base corner" : "Specify height",
            "REVOLVE" => _points.Count == 0 ? "Specify axis start point" : _points.Count == 1 ? "Specify axis end point" : "Specify revolution angle in degrees",
            "DIMALIGNED" => _points.Count < 2 ? $"Specify extension point {_points.Count + 1}" : "Specify dimension-line location",
            _ => $"Specify point {_points.Count + 1}"
        };
        Changed?.Invoke();
    }
    public IReadOnlyList<Entity> Preview(Vec3 cursor)
    {
        if (_points.Count == 0) return [];
        var a = _points[0];
        return _active switch
        {
            "LINE" => [new LineEntity(_points[^1], cursor)],
            "PLINE" => [PolylineEntity.FromPoints(_points.Append(cursor))],
            "RECTANG" or "BOX" when _points.Count == 1 => [PolylineEntity.FromPoints(new Vec3[] { a, new(cursor.X, a.Y, a.Z), cursor, new(a.X, cursor.Y, a.Z) }, true)],
            "CIRCLE" or "CYLINDER" or "CONE" or "SPHERE" when _points.Count == 1 => [new CircleEntity(a, Math.Max(1e-8, a.DistanceTo(cursor)))],
            "MOVE" or "COPY" => Session.Document.Drawing.Entities.Where(e => Session.Selection.Contains(e.Id)).Select(e => EntityGeometry.Transform(e, Transform3.Translation(cursor - a))).ToArray(),
            "DIMALIGNED" when _points.Count == 2 => [new DimensionEntity(a, _points[1], cursor)],
            _ => [new LineEntity(_points[^1], cursor)]
        };
    }
}
