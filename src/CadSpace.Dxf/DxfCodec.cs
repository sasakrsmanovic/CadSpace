using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

public readonly record struct DxfPair(int Code, string Value);
public sealed record DxfSection(string Name, ImmutableArray<DxfPair> Pairs);
public sealed record DxfSource(string Text, Drawing Original, ImmutableArray<DxfSection> Sections, ImmutableDictionary<Guid, ImmutableArray<DxfPair>> Records);
public sealed record DxfReadResult(Drawing Drawing, DxfSource Source, ImmutableArray<string> Warnings);
public sealed record DxfWriteResult(string Text, ImmutableArray<string> Warnings);

/// <summary>Bounded ASCII DXF exchange. Unsupported records remain opaque and untouched records retain their group data.</summary>
public static class DxfCodec
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    public const int MaximumCharacters = 64 * 1024 * 1024;
    public static DxfReadResult Read(string text, string name = "Drawing.dxf")
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumCharacters) throw new FormatException("DXF exceeds the 64 Mi-character import limit.");
        if (text.StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal)) throw new NotSupportedException("Binary DXF is not supported. Save an ASCII DXF first.");
        var pairs = ParsePairs(text); var sections = ImmutableArray.CreateBuilder<DxfSection>();
        for (var i = 0; i < pairs.Length; i++)
        {
            if (pairs[i].Code != 0 || pairs[i].Value.Trim() != "SECTION") continue;
            if (++i >= pairs.Length || pairs[i].Code != 2) throw new FormatException("SECTION must be followed by a section name.");
            var sectionName = pairs[i].Value.Trim(); var content = ImmutableArray.CreateBuilder<DxfPair>();
            while (++i < pairs.Length && !(pairs[i].Code == 0 && pairs[i].Value.Trim() == "ENDSEC")) content.Add(pairs[i]);
            if (i == pairs.Length) throw new FormatException($"Unterminated {sectionName} section.");
            sections.Add(new(sectionName, content.ToImmutable()));
        }
        if (!pairs.Any(p => p.Code == 0 && p.Value.Trim() == "EOF")) throw new FormatException("DXF EOF marker is missing.");
        if (sections.GroupBy(s => s.Name).Any(g => g.Count() > 1)) throw new FormatException("Duplicate DXF sections are not supported.");
        var state = Drawing.Empty with { Name = name };
        var warnings = ImmutableArray.CreateBuilder<string>();
        var sourceRecords = ImmutableDictionary.CreateBuilder<Guid, ImmutableArray<DxfPair>>();
        foreach (var record in Records(sections.FirstOrDefault(s => s.Name == "TABLES")?.Pairs ?? []))
        {
            if (Type(record) != "LAYER") continue;
            var layerName = String(record, 2, "0"); var aci = Integer(record, 62, 7); var flags = Integer(record, 70);
            var color = Has(record, 420) ? 0xFF000000u | (uint)Integer(record, 420) : EntityGeometry.AciColor(Math.Abs(aci));
            var layer = new Layer(layerName, color, aci >= 0 && (flags & 1) == 0, (flags & 4) != 0, Math.Max(0, Number(record, 370, 25)) / 100);
            state = state with { Layers = state.Layers.SetItem(layerName, layer) };
        }
        Entity Parse(ImmutableArray<DxfPair> record)
        {
            var type = Type(record); Entity entity;
            var normal = Point(record, 210, Vec3.UnitZ);
            var unsupportedCoordinates = normal.DistanceTo(Vec3.UnitZ) > 1e-9 || Math.Abs(Number(record, 39)) > 1e-9 || Integer(record, 67) != 0;
            try
            {
                entity = unsupportedCoordinates ? Opaque(type, record) : type switch
                {
                    "LINE" => new LineEntity(Point(record, 10), Point(record, 11)),
                    "POINT" => new PointEntity(Point(record, 10)),
                    "CIRCLE" => new CircleEntity(Point(record, 10), Positive(record, 40)),
                    "ARC" => new ArcEntity(Point(record, 10), Positive(record, 40), Number(record, 50), Number(record, 51)),
                    "LWPOLYLINE" when !record.Any(p => p.Code is 40 or 41 or 43 && GeometryMath.Number(p.Value, out var w) && w != 0) => ReadPolyline(record),
                    "ELLIPSE" when Math.Abs(Point(record, 11).Z) < 1e-9 => new EllipseEntity(Point(record, 10), Point(record, 11), Positive(record, 40), Number(record, 41), Number(record, 42, Math.PI * 2)),
                    "TEXT" when Integer(record, 72) == 0 && Integer(record, 73) == 0 && Number(record, 41, 1) == 1 && Number(record, 51) == 0 && Integer(record, 71) == 0 => new TextEntity(Point(record, 10), String(record, 1), Positive(record, 40), Number(record, 50)),
                    "MTEXT" => new TextEntity(Point(record, 10), string.Concat(record.Where(p => p.Code == 3).Select(p => p.Value)) + String(record, 1), Positive(record, 40), GeometryMath.Degrees(Number(record, 50)), true),
                    "INSERT" when Integer(record, 66) == 0 && Integer(record, 70, 1) <= 1 && Integer(record, 71, 1) <= 1 => new BlockReferenceEntity(String(record, 2), Point(record, 10), new(Number(record, 41, 1), Number(record, 42, 1), Number(record, 43, 1)), Number(record, 50)),
                    "3DFACE" => ReadFace(record),
                    _ => Opaque(type, record)
                };
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
            {
                warnings.Add($"{type} was retained as opaque: {ex.Message}"); entity = Opaque(type, record);
            }
            var layerName = String(record, 8, "0");
            if (!state.Layers.ContainsKey(layerName)) state = state with { Layers = state.Layers.Add(layerName, new(layerName)) };
            entity = entity with { Handle = String(record, 5), Layer = layerName, ColorIndex = Integer(record, 62, 256), TrueColor = Has(record, 420) ? 0xFF000000u | (uint)Integer(record, 420) : null, LineWeight = Number(record, 370, -100) / 100 };
            sourceRecords[entity.Id] = record; return entity;
        }
        var blockRecords = Records(sections.FirstOrDefault(s => s.Name == "BLOCKS")?.Pairs ?? []).ToArray();
        for (var i = 0; i < blockRecords.Length; i++)
        {
            if (Type(blockRecords[i]) != "BLOCK") continue;
            var header = blockRecords[i]; var blockName = String(header, 2); var entities = ImmutableArray.CreateBuilder<Entity>();
            while (++i < blockRecords.Length && Type(blockRecords[i]) != "ENDBLK") entities.Add(Parse(blockRecords[i]));
            state = state with { Blocks = state.Blocks.SetItem(blockName, new(blockName, Point(header, 10), entities.ToImmutable())) };
        }
        var model = Records(sections.FirstOrDefault(s => s.Name == "ENTITIES")?.Pairs ?? []).Select(Parse).ToImmutableArray();
        state = state with { Entities = model };
        var headerPairs = sections.FirstOrDefault(s => s.Name == "HEADER")?.Pairs ?? [];
        for (var i = 0; i + 1 < headerPairs.Length; i++) if (headerPairs[i].Code == 9 && headerPairs[i].Value == "$INSUNITS") state = state with { Units = int.TryParse(headerPairs[i + 1].Value, out var units) ? units : 4 };
        var opaque = model.Count(e => e is OpaqueEntity) + state.Blocks.Values.Sum(b => b.Entities.Count(e => e is OpaqueEntity));
        if (opaque > 0) warnings.Add($"{opaque} unsupported records were preserved but are not rendered or editable.");
        if (model.Any(e => e is TextEntity { Multiline: true })) warnings.Add("MTEXT display uses plain text; full formatting and attachment semantics are not implemented.");
        CadDocument.Validate(state);
        var source = new DxfSource(text, state, sections.ToImmutable(), sourceRecords.ToImmutable());
        return new(state, source, warnings.ToImmutable());
    }
    public static DxfWriteResult Write(Drawing drawing, DxfSource? source = null)
    {
        CadDocument.Validate(drawing);
        if (source != null && drawing == source.Original) return new(source.Text, []);
        var warnings = ImmutableArray.CreateBuilder<string>();
        ulong handle = 0x100;
        if (source != null) foreach (var pair in source.Sections.SelectMany(s => s.Pairs)) if (pair.Code is 5 or 105 && ulong.TryParse(pair.Value, NumberStyles.HexNumber, Culture, out var value)) handle = Math.Max(handle, value);
        string NewHandle() => checked(++handle).ToString("X", Culture);
        var originals = source?.Original.Entities.Concat(source.Original.Blocks.Values.SelectMany(b => b.Entities)).ToDictionary(e => e.Id) ?? new();
        string Emit(Entity entity)
        {
            if (source != null && originals.TryGetValue(entity.Id, out var original) && entity == original && source.Records.TryGetValue(entity.Id, out var raw)) return Encode(raw);
            if (entity is OpaqueEntity opaque) return opaque.RawRecord;
            var buffer = new StringBuilder();
            void Pair(int code, object value) => buffer.Append(code.ToString(Culture)).Append('\n').Append(Convert.ToString(value, Culture)).Append('\n');
            void Position(int code, Vec3 p) { Pair(code, p.X); Pair(code + 10, p.Y); Pair(code + 20, p.Z); }
            void Start(string type, string subclass, string? forcedHandle = null)
            {
                Pair(0, type); Pair(5, forcedHandle ?? (string.IsNullOrEmpty(entity.Handle) ? NewHandle() : entity.Handle)); Pair(100, "AcDbEntity"); Pair(8, entity.Layer);
                Pair(62, entity.ColorIndex); if (entity.TrueColor is uint c) Pair(420, c & 0xFFFFFF);
                if (entity.LineWeight >= 0) Pair(370, Math.Round(entity.LineWeight * 100)); Pair(100, subclass);
            }
            switch (entity)
            {
                case LineEntity line: Start("LINE", "AcDbLine"); Position(10, line.Start); Position(11, line.End); break;
                case PointEntity point: Start("POINT", "AcDbPoint"); Position(10, point.Position); break;
                case CircleEntity circle: Start("CIRCLE", "AcDbCircle"); Position(10, circle.Center); Pair(40, circle.Radius); break;
                case ArcEntity arc: Start("ARC", "AcDbCircle"); Position(10, arc.Center); Pair(40, arc.Radius); Pair(100, "AcDbArc"); Pair(50, arc.StartAngle); Pair(51, arc.EndAngle); break;
                case PolylineEntity poly:
                    if (poly.Vertices.Any(v => Math.Abs(v.Position.Z - poly.Vertices[0].Position.Z) > 1e-8)) throw new NotSupportedException("LWPOLYLINE requires a constant elevation.");
                    Start("LWPOLYLINE", "AcDbPolyline"); Pair(90, poly.Vertices.Length); Pair(70, poly.Closed ? 1 : 0); Pair(38, poly.Vertices[0].Position.Z);
                    foreach (var v in poly.Vertices) { Pair(10, v.Position.X); Pair(20, v.Position.Y); if (v.Bulge != 0) Pair(42, v.Bulge); } break;
                case EllipseEntity ellipse: Start("ELLIPSE", "AcDbEllipse"); Position(10, ellipse.Center); Position(11, ellipse.MajorAxis); Pair(40, ellipse.Ratio); Pair(41, ellipse.StartParameter); Pair(42, ellipse.EndParameter); break;
                case TextEntity text:
                    Start(text.Multiline ? "MTEXT" : "TEXT", text.Multiline ? "AcDbMText" : "AcDbText"); Position(10, text.Position); Pair(40, text.Height);
                    Pair(1, text.Text.Replace("\r", "").Replace("\n", text.Multiline ? "\\P" : " ")); Pair(50, text.Multiline ? GeometryMath.Radians(text.Rotation) : text.Rotation);
                    if (text.Multiline) { Pair(41, 0); Pair(71, 1); } else Pair(100, "AcDbText"); break;
                case BlockReferenceEntity insert: Start("INSERT", "AcDbBlockReference"); Pair(2, insert.Name); Position(10, insert.Position); Pair(41, insert.Scale.X); Pair(42, insert.Scale.Y); Pair(43, insert.Scale.Z); Pair(50, insert.Rotation); break;
                case MeshEntity mesh:
                    warnings.Add("Triangle meshes are exported as 3DFACE entities, not ACIS solids or editable mesh topology.");
                    for (var i = 0; i < mesh.Triangles.Length; i += 3)
                    {
                        Start("3DFACE", "AcDbFace", NewHandle()); Position(10, mesh.Vertices[mesh.Triangles[i]]); Position(11, mesh.Vertices[mesh.Triangles[i + 1]]); Position(12, mesh.Vertices[mesh.Triangles[i + 2]]); Position(13, mesh.Vertices[mesh.Triangles[i + 2]]);
                    }
                    break;
                case DimensionEntity or HatchEntity:
                    warnings.Add($"{entity.Kind} is exported as display geometry; its CadSpace editing semantics are not retained in DXF.");
                    var scene = EntityGeometry.BuildScene(drawing with { Entities = [entity] });
                    foreach (var path in scene.Paths)
                    {
                        if (path.Points.Length >= 2) buffer.Append(Emit(PolylineEntity.FromPoints(path.Points, path.Closed) with { Layer = entity.Layer, ColorIndex = entity.ColorIndex, TrueColor = entity.TrueColor }));
                    }
                    foreach (var label in scene.Texts) buffer.Append(Emit(new TextEntity(label.Position, label.Text, label.Height, label.Rotation) { Layer = entity.Layer, TrueColor = label.Color }));
                    break;
                default: throw new NotSupportedException($"Export of {entity.Kind} is not implemented.");
            }
            if (source != null && source.Records.ContainsKey(entity.Id)) warnings.Add($"Edited {entity.Kind} {entity.Handle}: unmodeled per-entity metadata is not retained. Keep the original DXF.");
            return buffer.ToString();
        }
        var entitiesText = string.Concat(drawing.Entities.Select(Emit));
        var blocksText = new StringBuilder();
        foreach (var block in drawing.Blocks.Values)
        {
            blocksText.Append($"0\nBLOCK\n5\n{NewHandle()}\n100\nAcDbEntity\n8\n0\n100\nAcDbBlockBegin\n2\n{block.Name}\n70\n0\n10\n{F(block.BasePoint.X)}\n20\n{F(block.BasePoint.Y)}\n30\n{F(block.BasePoint.Z)}\n3\n{block.Name}\n1\n\n");
            foreach (var e in block.Entities) blocksText.Append(Emit(e));
            blocksText.Append($"0\nENDBLK\n5\n{NewHandle()}\n100\nAcDbEntity\n8\n0\n100\nAcDbBlockEnd\n");
        }
        var layerTable = new StringBuilder($"0\nTABLE\n2\nLAYER\n70\n{drawing.Layers.Count}\n");
        foreach (var layer in drawing.Layers.Values)
            layerTable.Append($"0\nLAYER\n5\n{NewHandle()}\n100\nAcDbSymbolTableRecord\n100\nAcDbLayerTableRecord\n2\n{layer.Name}\n70\n{(layer.Locked ? 4 : 0)}\n62\n{(layer.Visible ? 7 : -7)}\n420\n{layer.Color & 0xFFFFFF}\n6\nCONTINUOUS\n370\n{Math.Round(layer.LineWeight * 100)}\n");
        layerTable.Append("0\nENDTAB\n");
        string tables = layerTable.ToString();
        var oldTables = source?.Sections.FirstOrDefault(s => s.Name == "TABLES");
        if (oldTables != null)
        {
            if (drawing.Layers == source!.Original.Layers) tables = Encode(oldTables.Pairs);
            else
            {
                var kept = new List<DxfPair>(); var skip = false;
                foreach (var record in Records(oldTables.Pairs))
                {
                    if (Type(record) == "TABLE") skip = String(record, 2) == "LAYER";
                    if (!skip) kept.AddRange(record);
                    if (Type(record) == "ENDTAB") skip = false;
                }
                tables = Encode(kept) + tables;
                warnings.Add("Layer table was regenerated; unmodeled layer metadata may not be retained.");
            }
        }
        var oldBlocks = source?.Sections.FirstOrDefault(s => s.Name == "BLOCKS");
        var blocks = oldBlocks != null && drawing.Blocks == source!.Original.Blocks ? Encode(oldBlocks.Pairs) : blocksText.ToString();
        if (oldBlocks != null && drawing.Blocks != source!.Original.Blocks) warnings.Add("Block definitions were regenerated; unmodeled block-header metadata may not be retained.");
        var header = new List<DxfPair>();
        var oldHeader = source?.Sections.FirstOrDefault(s => s.Name == "HEADER");
        var skipHeader = false;
        if (oldHeader != null) foreach (var pair in oldHeader.Pairs)
        {
            if (pair.Code == 9) skipHeader = pair.Value is "$ACADVER" or "$HANDSEED" or "$INSUNITS";
            if (!skipHeader) header.Add(pair);
        }
        header.AddRange([new(9, "$ACADVER"), new(1, "AC1027"), new(9, "$INSUNITS"), new(70, drawing.Units.ToString(Culture)), new(9, "$HANDSEED"), new(5, NewHandle())]);
        var replacements = new Dictionary<string, string> { ["HEADER"] = Encode(header), ["TABLES"] = tables, ["BLOCKS"] = blocks, ["ENTITIES"] = entitiesText };
        var output = new StringBuilder();
        void Section(string section, string data) => output.Append("0\nSECTION\n2\n").Append(section).Append('\n').Append(data).Append("0\nENDSEC\n");
        // Required sections precede OBJECTS; preserve unmodeled sections verbatim at group-pair level.
        foreach (var section in new[] { "HEADER", "CLASSES", "TABLES", "BLOCKS", "ENTITIES", "OBJECTS" })
        {
            if (replacements.Remove(section, out var data)) Section(section, data);
            else if (source?.Sections.FirstOrDefault(s => s.Name == section) is { } old) Section(section, Encode(old.Pairs));
        }
        if (source != null) foreach (var section in source.Sections.Where(s => s.Name is not ("HEADER" or "CLASSES" or "TABLES" or "BLOCKS" or "ENTITIES" or "OBJECTS"))) Section(section.Name, Encode(section.Pairs));
        output.Append("0\nEOF\n");
        return new(output.ToString(), warnings.Distinct().ToImmutableArray());
    }
    public static ImmutableArray<DxfPair> ParsePairs(string text)
    {
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        var result = ImmutableArray.CreateBuilder<DxfPair>(); string? line; var number = 0;
        while ((line = reader.ReadLine()) != null)
        {
            number++;
            if (string.IsNullOrWhiteSpace(line) && reader.Peek() == -1) break;
            if (!int.TryParse(line.Trim(), NumberStyles.Integer, Culture, out var code) || code < 0 || code > 1071) throw new FormatException($"Invalid group code at line {number}.");
            var value = reader.ReadLine() ?? throw new FormatException($"Missing group value at line {number + 1}."); number++;
            result.Add(new(code, value));
        }
        return result.ToImmutable();
    }
    private static IEnumerable<ImmutableArray<DxfPair>> Records(ImmutableArray<DxfPair> pairs)
    {
        var record = ImmutableArray.CreateBuilder<DxfPair>();
        foreach (var pair in pairs)
        {
            if (pair.Code == 0 && record.Count != 0) { yield return record.ToImmutable(); record.Clear(); }
            record.Add(pair);
        }
        if (record.Count != 0) yield return record.ToImmutable();
    }
    private static Entity ReadPolyline(ImmutableArray<DxfPair> pairs)
    {
        var vertices = ImmutableArray.CreateBuilder<PolyVertex>();
        var elevation = Number(pairs, 38);
        for (var i = 0; i < pairs.Length; i++) if (pairs[i].Code == 10)
        {
            var x = ParseNumber(pairs[i].Value); double y = 0, bulge = 0;
            for (var j = i + 1; j < pairs.Length && pairs[j].Code != 10; j++) { if (pairs[j].Code == 20) y = ParseNumber(pairs[j].Value); if (pairs[j].Code == 42) bulge = ParseNumber(pairs[j].Value); }
            vertices.Add(new(new(x, y, elevation), bulge));
        }
        if (vertices.Count < 2) throw new FormatException("Polyline has fewer than two vertices.");
        return new PolylineEntity(vertices.ToImmutable(), (Integer(pairs, 70) & 1) != 0);
    }
    private static MeshEntity ReadFace(ImmutableArray<DxfPair> pairs)
    {
        var a = Point(pairs, 10); var b = Point(pairs, 11); var c = Point(pairs, 12); var d = Point(pairs, 13, c);
        return c.DistanceTo(d) < 1e-9 ? new([a, b, c], [0, 1, 2], "3DFACE") : new([a, b, c, d], [0, 1, 2, 0, 2, 3], "3DFACE");
    }
    private static OpaqueEntity Opaque(string type, ImmutableArray<DxfPair> pairs) => new(type, Encode(pairs));
    private static bool Has(ImmutableArray<DxfPair> p, int c) => p.Any(v => v.Code == c);
    private static string Type(ImmutableArray<DxfPair> p) => String(p, 0).Trim();
    private static string String(ImmutableArray<DxfPair> p, int c, string fallback = "") => p.FirstOrDefault(v => v.Code == c).Value ?? fallback;
    private static double ParseNumber(string s) => GeometryMath.Number(s, out var value) ? value : throw new FormatException($"Invalid numeric value: {s}");
    private static double Number(ImmutableArray<DxfPair> p, int c, double fallback = 0) => Has(p, c) ? ParseNumber(String(p, c)) : fallback;
    private static double Positive(ImmutableArray<DxfPair> p, int c) { var value = Number(p, c); return value > 0 ? value : throw new FormatException("Value must be positive."); }
    private static int Integer(ImmutableArray<DxfPair> p, int c, int fallback = 0) => Has(p, c) ? int.Parse(String(p, c), Culture) : fallback;
    private static Vec3 Point(ImmutableArray<DxfPair> p, int c, Vec3 fallback = default) => new(Number(p, c, fallback.X), Number(p, c + 10, fallback.Y), Number(p, c + 20, fallback.Z));
    private static string Encode(IEnumerable<DxfPair> p) => string.Concat(p.Select(v => $"{v.Code.ToString(Culture)}\n{v.Value}\n"));
    private static string F(double value) => value.ToString("R", Culture);
}
