using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

var tests = new List<(string, Action)>();
void Test(string name, Action test) => tests.Add((name, test));
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
Test("studio native roundtrip", () => { var drawing = SampleDrawings.StudioPlan(); var text = CadProjectCodec.Write(drawing); var read = CadProjectCodec.Read(text); Check(CadProjectCodec.Write(read.Drawing) == text); Check(read.Drawing.Layers.ContainsKey("a-wall")); Check(read.Drawing.Blocks.ContainsKey("workstation")); });
Test("mesh topology and semantics survive native save", () => { var drawing = SampleDrawings.ModelStudy(); var result = CadProjectCodec.Read(CadProjectCodec.Write(drawing)); Check(result.Drawing.Entities.Length == 5); Check(result.Drawing.Entities.All(e => e is MeshEntity)); var a = (MeshEntity)drawing.Entities[0]; var b = (MeshEntity)result.Drawing.Entities[0]; Check(a.Id == b.Id && a.Operation == b.Operation && a.Vertices.SequenceEqual(b.Vertices) && a.Triangles.SequenceEqual(b.Triangles)); });
Test("dimension hatch and bulges survive", () => { var drawing = Drawing.Empty with { Entities = [new DimensionEntity(default, new(50, 0), new(25, 10)), new HatchEntity([new(0, 0), new(50, 0), new(50, 50), new(0, 50)], 7, 31, true), new PolylineEntity([new(new(0, 0), 0.5), new(new(100, 0))])] }; var text = CadProjectCodec.Write(drawing); Check(CadProjectCodec.Write(CadProjectCodec.Read(text).Drawing) == text); });
Test("all implemented native entity records", () => { var drawing = Drawing.Empty with { Entities = [new PointEntity(new(1, 2, 3)), new EllipseEntity(new(5, 5), new(20, 0), 0.5), new TextEntity(default, "A\nB Ω", 4, 30, true), new OpaqueEntity("ACAD_PROXY_ENTITY", "0\nACAD_PROXY_ENTITY\n310\n1234\n")] }; var text = CadProjectCodec.Write(drawing); Check(CadProjectCodec.Write(CadProjectCodec.Read(text).Drawing) == text); });
Test("DXF original byte pass-through after native save", () => { var raw = "0\r\nSECTION\r\n2\r\nENTITIES\r\n0\r\nLWPOLYLINE\r\n5\r\nAB\r\n90\r\n2\r\n10\r\n1\r\n20\r\n2\r\n10\r\n3\r\n20\r\n4\r\n0\r\nENDSEC\r\n0\r\nEOF\r\n"; var dxf = DxfCodec.Read(raw); var project = CadProjectCodec.Read(CadProjectCodec.Write(dxf.Drawing, dxf.Source)); Check(DxfCodec.Write(project.Drawing, project.DxfSource).Text == raw); });
Test("untouched DXF metadata survives native edited save", () => { var raw = "0\nSECTION\n2\nENTITIES\n0\nLINE\n5\nAB\n10\n0\n20\n0\n11\n10\n21\n10\n1001\nMYDATA\n1000\nKEEP THIS\n0\nENDSEC\n0\nEOF\n"; var dxf = DxfCodec.Read(raw); var edited = dxf.Drawing with { Entities = dxf.Drawing.Entities.Add(new CircleEntity(default, 20)) }; var project = CadProjectCodec.Read(CadProjectCodec.Write(edited, dxf.Source)); Check(DxfCodec.Write(project.Drawing, project.DxfSource).Text.Contains("1000\nKEEP THIS")); });
Test("blocks preserve DXF provenance across native save", () => { var raw = DxfCodec.Write(SampleDrawings.StudioPlan()).Text; var dxf = DxfCodec.Read(raw); var project = CadProjectCodec.Read(CadProjectCodec.Write(dxf.Drawing, dxf.Source)); Check(DxfCodec.Write(project.Drawing, project.DxfSource).Text == raw); });
Test("future native version rejected", () => { var text = CadProjectCodec.Write(Drawing.Empty).Replace("\"version\": 2", "\"version\": 999"); var rejected = false; try { CadProjectCodec.Read(text); } catch (FormatException) { rejected = true; } Check(rejected); });
var failed = 0;
foreach (var (name, run) in tests) try { run(); Console.WriteLine($"PASS {name}"); } catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
Console.WriteLine($"{tests.Count - failed}/{tests.Count} persistence tests passed.");
return failed == 0 ? 0 : 1;
