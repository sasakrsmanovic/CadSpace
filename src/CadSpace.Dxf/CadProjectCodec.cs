using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

public sealed record CadProjectReadResult(Drawing Drawing, DxfSource? DxfSource);

/// <summary>Versioned, lossless native persistence for the implemented document model. No reflection or executable payloads.</summary>
public static class CadProjectCodec
{
    public const int MaximumCharacters = 128 * 1024 * 1024;
    public static string Write(Drawing drawing, DxfSource? source = null)
    {
        CadDocument.Validate(drawing);
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteString("format", "CadSpace"); writer.WriteNumber("version", 1);
            writer.WritePropertyName("drawing"); WriteDrawing(writer, drawing);
            if (source != null)
            {
                writer.WriteString("dxfOriginal", source.Text);
                writer.WritePropertyName("sourceIds"); writer.WriteStartObject();
                writer.WritePropertyName("model"); WriteIds(writer, source.Original.Entities);
                writer.WritePropertyName("blocks"); writer.WriteStartObject();
                foreach (var block in source.Original.Blocks.Values) { writer.WritePropertyName(block.Name); WriteIds(writer, block.Entities); }
                writer.WriteEndObject(); writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        if (memory.Length > MaximumCharacters) throw new InvalidOperationException("Native project exceeds the 128 MiB save limit.");
        return Encoding.UTF8.GetString(memory.ToArray());
    }
    public static CadProjectReadResult Read(string text)
    {
        if (text.Length > MaximumCharacters) throw new FormatException("Native project exceeds the 128 Mi-character read limit.");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != "CadSpace" || root.GetProperty("version").GetInt32() != 1) throw new FormatException("Unsupported CadSpace project format/version.");
        var drawing = ReadDrawing(root.GetProperty("drawing"));
        DxfSource? source = null;
        if (root.TryGetProperty("dxfOriginal", out var raw))
        {
            var parsed = DxfCodec.Read(raw.GetString() ?? throw new FormatException("Missing original DXF."), drawing.Name);
            var ids = root.GetProperty("sourceIds"); var map = new Dictionary<Guid, Guid>();
            ImmutableArray<Entity> Remap(ImmutableArray<Entity> entities, JsonElement identifiers)
            {
                var saved = identifiers.EnumerateArray().Select(i => i.GetGuid()).ToArray();
                if (saved.Length != entities.Length) throw new FormatException("The original DXF entity map is inconsistent.");
                return entities.Select((entity, i) => { if (!map.TryAdd(entity.Id, saved[i])) throw new FormatException("Duplicate source entity identifier."); return entity with { Id = saved[i] }; }).ToImmutableArray();
            }
            var original = parsed.Drawing with { Entities = Remap(parsed.Drawing.Entities, ids.GetProperty("model")) };
            var blocks = original.Blocks;
            foreach (var block in parsed.Drawing.Blocks.Values) blocks = blocks.SetItem(block.Name, block with { Entities = Remap(block.Entities, ids.GetProperty("blocks").GetProperty(block.Name)) });
            original = original with { Blocks = blocks };
            if (map.Values.Distinct().Count() != map.Count) throw new FormatException("Duplicate persistent source entity identifiers.");
            source = new(parsed.Source.Text, original, parsed.Source.Sections, parsed.Source.Records.ToImmutableDictionary(p => map[p.Key], p => p.Value));
            // Reuse equal imported instances so untouched group data and exact original-text pass-through remain available.
            drawing = Intern(drawing, original);
        }
        CadDocument.Validate(drawing); return new(drawing, source);
    }
    private static Drawing Intern(Drawing drawing, Drawing original)
    {
        var originals = original.Entities.Concat(original.Blocks.Values.SelectMany(b => b.Entities)).ToDictionary(e => e.Id);
        Entity Canonical(Entity entity) => originals.TryGetValue(entity.Id, out var old) && Equivalent(entity, old) ? old : entity;
        var entities = drawing.Entities.Select(Canonical).ToImmutableArray();
        if (entities.SequenceEqual(original.Entities)) entities = original.Entities;
        var layers = drawing.Layers;
        if (layers.Count == original.Layers.Count && layers.All(p => original.Layers.TryGetValue(p.Key, out var l) && l == p.Value)) layers = original.Layers;
        var blocks = drawing.Blocks;
        foreach (var block in drawing.Blocks.Values)
        {
            var children = block.Entities.Select(Canonical).ToImmutableArray();
            var updated = block with { Entities = children };
            if (original.Blocks.TryGetValue(block.Name, out var old) && old.BasePoint == block.BasePoint && children.SequenceEqual(old.Entities)) updated = old;
            blocks = blocks.SetItem(block.Name, updated);
        }
        if (blocks.Count == original.Blocks.Count && blocks.All(p => original.Blocks.TryGetValue(p.Key, out var b) && b == p.Value)) blocks = original.Blocks;
        return drawing with { Entities = entities, Layers = layers, Blocks = blocks };
    }
    private static bool Equivalent(Entity a, Entity b) => (a, b) switch
    {
        (PolylineEntity x, PolylineEntity y) => x.Vertices.SequenceEqual(y.Vertices) && x with { Vertices = y.Vertices } == y,
        (HatchEntity x, HatchEntity y) => x.Boundary.SequenceEqual(y.Boundary) && x with { Boundary = y.Boundary } == y,
        (MeshEntity x, MeshEntity y) => x.Vertices.SequenceEqual(y.Vertices) && x.Triangles.SequenceEqual(y.Triangles) && x with { Vertices = y.Vertices, Triangles = y.Triangles } == y,
        _ => a == b
    };
    private static void WriteIds(Utf8JsonWriter writer, IEnumerable<Entity> entities)
    {
        writer.WriteStartArray(); foreach (var entity in entities) writer.WriteStringValue(entity.Id); writer.WriteEndArray();
    }
    private static void WriteDrawing(Utf8JsonWriter w, Drawing drawing)
    {
        w.WriteStartObject(); w.WriteString("name", drawing.Name); w.WriteNumber("units", drawing.Units);
        w.WritePropertyName("layers"); w.WriteStartArray();
        foreach (var layer in drawing.Layers.Values.OrderBy(l => l.Name, StringComparer.Ordinal))
        {
            w.WriteStartObject(); w.WriteString("name", layer.Name); w.WriteNumber("color", layer.Color); w.WriteBoolean("visible", layer.Visible); w.WriteBoolean("locked", layer.Locked); w.WriteNumber("weight", layer.LineWeight); w.WriteEndObject();
        }
        w.WriteEndArray(); w.WritePropertyName("blocks"); w.WriteStartArray();
        foreach (var block in drawing.Blocks.Values.OrderBy(b => b.Name, StringComparer.Ordinal))
        {
            w.WriteStartObject(); w.WriteString("name", block.Name); Point(w, "base", block.BasePoint); w.WritePropertyName("entities"); Entities(w, block.Entities); w.WriteEndObject();
        }
        w.WriteEndArray(); w.WritePropertyName("entities"); Entities(w, drawing.Entities); w.WriteEndObject();
    }
    private static Drawing ReadDrawing(JsonElement root)
    {
        var layers = ImmutableDictionary.Create<string, Layer>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in root.GetProperty("layers").EnumerateArray())
        {
            var layer = new Layer(S(l, "name"), l.GetProperty("color").GetUInt32(), l.GetProperty("visible").GetBoolean(), l.GetProperty("locked").GetBoolean(), N(l, "weight")); layers = layers.Add(layer.Name, layer);
        }
        var blocks = ImmutableDictionary.Create<string, BlockDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in root.GetProperty("blocks").EnumerateArray()) { var block = new BlockDefinition(S(b, "name"), P(b, "base"), ReadEntities(b.GetProperty("entities"))); blocks = blocks.Add(block.Name, block); }
        return new(ReadEntities(root.GetProperty("entities")), layers, blocks) { Name = S(root, "name"), Units = root.GetProperty("units").GetInt32() };
    }
    private static void Point(Utf8JsonWriter w, string name, Vec3 p) { w.WritePropertyName(name); Point(w, p); }
    private static void Point(Utf8JsonWriter w, Vec3 p) { w.WriteStartArray(); w.WriteNumberValue(p.X); w.WriteNumberValue(p.Y); w.WriteNumberValue(p.Z); w.WriteEndArray(); }
    private static void Points(Utf8JsonWriter w, string name, IEnumerable<Vec3> points) { w.WritePropertyName(name); w.WriteStartArray(); foreach (var p in points) Point(w, p); w.WriteEndArray(); }
    private static void Entities(Utf8JsonWriter w, IEnumerable<Entity> entities)
    {
        w.WriteStartArray();
        foreach (var entity in entities)
        {
            w.WriteStartObject(); w.WriteString("type", entity is OpaqueEntity ? "OPAQUE" : entity.Kind);
            w.WriteString("id", entity.Id); w.WriteString("handle", entity.Handle); w.WriteString("layer", entity.Layer); w.WriteNumber("aci", entity.ColorIndex); w.WriteNumber("weight", entity.LineWeight);
            if (entity.TrueColor is uint color) w.WriteNumber("color", color);
            switch (entity)
            {
                case LineEntity e: Point(w, "a", e.Start); Point(w, "b", e.End); break;
                case PointEntity e: Point(w, "point", e.Position); break;
                case CircleEntity e: Point(w, "center", e.Center); w.WriteNumber("radius", e.Radius); break;
                case ArcEntity e: Point(w, "center", e.Center); w.WriteNumber("radius", e.Radius); w.WriteNumber("start", e.StartAngle); w.WriteNumber("end", e.EndAngle); break;
                case PolylineEntity e:
                    w.WriteBoolean("closed", e.Closed); w.WritePropertyName("vertices"); w.WriteStartArray();
                    foreach (var v in e.Vertices) { w.WriteStartObject(); Point(w, "point", v.Position); w.WriteNumber("bulge", v.Bulge); w.WriteEndObject(); } w.WriteEndArray(); break;
                case EllipseEntity e: Point(w, "center", e.Center); Point(w, "major", e.MajorAxis); w.WriteNumber("ratio", e.Ratio); w.WriteNumber("start", e.StartParameter); w.WriteNumber("end", e.EndParameter); break;
                case TextEntity e: Point(w, "point", e.Position); w.WriteString("text", e.Text); w.WriteNumber("height", e.Height); w.WriteNumber("rotation", e.Rotation); break;
                case DimensionEntity e: Point(w, "a", e.First); Point(w, "b", e.Second); Point(w, "location", e.Location); break;
                case HatchEntity e: Points(w, "boundary", e.Boundary); w.WriteNumber("spacing", e.Spacing); w.WriteNumber("angle", e.Angle); w.WriteBoolean("solid", e.Solid); break;
                case MeshEntity e: Points(w, "vertices", e.Vertices); w.WritePropertyName("triangles"); w.WriteStartArray(); foreach (var index in e.Triangles) w.WriteNumberValue(index); w.WriteEndArray(); w.WriteString("operation", e.Operation); break;
                case BlockReferenceEntity e: w.WriteString("name", e.Name); Point(w, "point", e.Position); Point(w, "scale", e.Scale); w.WriteNumber("rotation", e.Rotation); break;
                case OpaqueEntity e: w.WriteString("dxfType", e.DxfType); w.WriteString("raw", e.RawRecord); break;
                default: throw new NotSupportedException($"Native persistence does not know entity {entity.Kind}.");
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
    private static ImmutableArray<Entity> ReadEntities(JsonElement array)
    {
        return array.EnumerateArray().Select(e =>
        {
            var type = S(e, "type");
            Entity entity = type switch
            {
                "LINE" => new LineEntity(P(e, "a"), P(e, "b")),
                "POINT" => new PointEntity(P(e, "point")),
                "CIRCLE" => new CircleEntity(P(e, "center"), N(e, "radius")),
                "ARC" => new ArcEntity(P(e, "center"), N(e, "radius"), N(e, "start"), N(e, "end")),
                "LWPOLYLINE" => new PolylineEntity(e.GetProperty("vertices").EnumerateArray().Select(v => new PolyVertex(P(v, "point"), N(v, "bulge"))).ToImmutableArray(), e.GetProperty("closed").GetBoolean()),
                "ELLIPSE" => new EllipseEntity(P(e, "center"), P(e, "major"), N(e, "ratio"), N(e, "start"), N(e, "end")),
                "TEXT" or "MTEXT" => new TextEntity(P(e, "point"), S(e, "text"), N(e, "height"), N(e, "rotation"), type == "MTEXT"),
                "DIMENSION" => new DimensionEntity(P(e, "a"), P(e, "b"), P(e, "location")),
                "HATCH" => new HatchEntity(Points(e.GetProperty("boundary")), N(e, "spacing"), N(e, "angle"), e.GetProperty("solid").GetBoolean()),
                "MESH" => new MeshEntity(Points(e.GetProperty("vertices")), e.GetProperty("triangles").EnumerateArray().Select(i => i.GetInt32()).ToImmutableArray(), S(e, "operation")),
                "INSERT" => new BlockReferenceEntity(S(e, "name"), P(e, "point"), P(e, "scale"), N(e, "rotation")),
                "OPAQUE" => new OpaqueEntity(S(e, "dxfType"), S(e, "raw")),
                _ => throw new FormatException($"Unknown native entity type: {type}")
            };
            return entity with { Id = e.GetProperty("id").GetGuid(), Handle = S(e, "handle"), Layer = S(e, "layer"), ColorIndex = e.GetProperty("aci").GetInt32(), LineWeight = N(e, "weight"), TrueColor = e.TryGetProperty("color", out var c) ? c.GetUInt32() : null };
        }).ToImmutableArray();
    }
    private static string S(JsonElement e, string key) => e.GetProperty(key).GetString() ?? throw new FormatException($"Missing string: {key}");
    private static double N(JsonElement e, string key) { var value = e.GetProperty(key).GetDouble(); return double.IsFinite(value) ? value : throw new FormatException("Nonfinite value."); }
    private static Vec3 P(JsonElement e, string key) => P(e.GetProperty(key));
    private static Vec3 P(JsonElement e)
    {
        var values = e.EnumerateArray().Select(v => v.GetDouble()).ToArray();
        if (values.Length != 3 || values.Any(v => !double.IsFinite(v))) throw new FormatException("Invalid 3D point.");
        return new(values[0], values[1], values[2]);
    }
    private static ImmutableArray<Vec3> Points(JsonElement array) => array.EnumerateArray().Select(P).ToImmutableArray();
}
