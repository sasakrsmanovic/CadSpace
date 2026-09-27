using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action action) => tests.Add((name, action));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Near(double actual, double expected, double epsilon = 1e-7) => Check(Math.Abs(actual - expected) <= epsilon, $"Expected {expected}, got {actual}");
void Throws(Action action) { var thrown = false; try { action(); } catch (ArgumentException) { thrown = true; } Check(thrown, "Expected an argument error"); }
Test("double precision at survey coordinates", () => Near(new Vec3(1e9, 1e9).DistanceTo(new(1e9 + 0.125, 1e9)), 0.125));
Test("relative coordinates", () => { Check(GeometryMath.TryParsePoint("@10,20,3", new(2, 4, 6), out var p)); Check(p == new Vec3(12, 24, 9)); });
Test("polar coordinates", () => { Check(GeometryMath.TryParsePoint("@20<90", new(5, 3), out var p)); Near(p.X, 5); Near(p.Y, 23); });
Test("reject nonfinite coordinates", () => Check(!GeometryMath.TryParsePoint("NaN,2", default, out _)));
Test("line intersection", () => { Check(GeometryMath.IntersectLinesXY(new(0, 0), new(10, 10), new(0, 10), new(10, 0), out var p)); Near(p.X, 5); Near(p.Y, 5); });
Test("parallel lines", () => Check(!GeometryMath.IntersectLinesXY(new(0, 0), new(1, 0), new(0, 1), new(1, 1), out _)));
Test("three point circle", () => { var c = GeometryMath.CircleThrough(new(1, 0), new(0, 1), new(-1, 0)); Near(c.Radius, 1); Near(c.Center.Length, 0); });
Test("collinear circle rejected", () => Throws(() => GeometryMath.CircleThrough(new(0, 0), new(1, 0), new(2, 0))));
Test("transform composition", () => { var t = Transform3.Translation(new(10, 0)).Then(Transform3.RotationZ(90)); var p = t.Point(new(2, 0)); Near(p.X, 0); Near(p.Y, 12); });
Test("reflection", () => { var p = Transform3.MirrorXY(new(0, 0), new(1, 0)).Point(new(4, 5)); Near(p.X, 4); Near(p.Y, -5); });
Test("concave triangulation area", () => { Vec3[] p = [new(0, 0), new(5, 0), new(5, 2), new(2, 2), new(2, 5), new(0, 5)]; var t = Triangulation.Polygon(p); double area = 0; for (var i = 0; i < t.Length; i += 3) area += Math.Abs(GeometryMath.Cross2(p[t[i + 1]] - p[t[i]], p[t[i + 2]] - p[t[i]])) / 2; Near(area, 16); });
Test("bowtie profile rejected", () => Throws(() => Triangulation.Polygon(new Vec3[] { new(0, 0), new(5, 5), new(0, 5), new(5, 0) })));
Test("box volume and winding", () => Near(MeshFactory.SignedVolume(MeshFactory.Box(new(0, 0, 0), new(10, 20, 30))), 6000));
Test("negative extrusion winding", () => Near(MeshFactory.SignedVolume(MeshFactory.Extrude(new Vec3[] { new(0, 0), new(10, 0), new(10, 10), new(0, 10) }, -5)), 500));
Test("sphere volume", () => { var v = MeshFactory.SignedVolume(MeshFactory.Sphere(default, 10)); Check(v > 4100 && v < 4190, v.ToString()); });
Test("cylinder volume", () => { var v = MeshFactory.SignedVolume(MeshFactory.Cylinder(default, 10, 20)); Check(v > 6250 && v < 6284); });
Test("undo redo identity", () => { var d = new CadDocument(); var original = d.Drawing; d.Add("Line", new LineEntity(default, new(10, 10))); Check(d.IsDirty); d.Undo(); Check(d.Drawing == original && !d.IsDirty); d.Redo(); Check(d.Drawing.Entities.Length == 1); });
Test("invalid edit is atomic", () => { var d = new CadDocument(); Throws(() => d.Add("Bad circle", new CircleEntity(default, -1))); Check(d.Drawing.Entities.IsEmpty && !d.CanUndo); });
Test("bulge tessellation", () => { var p = new PolylineEntity([new(new(0, 0), 1), new(new(10, 0))]); var points = EntityGeometry.PolylinePoints(p); Near(points.Min(v => v.Y), -5, 0.01); });
Test("mirror reverses bulges", () => { var p = new PolylineEntity([new(new(0, 0), 1), new(new(10, 0))]); var mirrored = (PolylineEntity)EntityGeometry.Transform(p, Transform3.MirrorXY(default, Vec3.UnitX)); Near(mirrored.Vertices[0].Bulge, -1); });
Test("block basepoint transform", () => { var state = Drawing.Empty; var block = new BlockDefinition("B", new(10, 10), [new LineEntity(new(10, 10), new(20, 10))]); state = state with { Blocks = state.Blocks.Add("B", block), Entities = [new BlockReferenceEntity("B", new(100, 100), new(2, 2, 2), 90)] }; var p = EntityGeometry.BuildScene(state).Paths[0]; Near(p.Points[0].X, 100); Near(p.Points[1].Y, 120); });
Test("block cycle rejected", () => { var s = Drawing.Empty; s = s with { Blocks = s.Blocks.Add("A", new("A", default, [new BlockReferenceEntity("A", default, new(1, 1, 1))])) }; Throws(() => CadDocument.Validate(s)); });
Test("hidden layer is not rendered", () => { var s = Drawing.Empty; s = s with { Layers = s.Layers.Add("Hidden", new("Hidden", Visible: false)), Entities = [new CircleEntity(default, 5) { Layer = "Hidden" }] }; Check(EntityGeometry.BuildScene(s).Paths.IsEmpty); });
Test("copied entities get fresh identity", () => { var e = new LineEntity(default, new(5, 0)) { Handle = "10" }; var c = EntityGeometry.Transform(e, Transform3.Translation(new(4, 4)), true); Check(e.Id != c.Id && c.Handle == ""); });
var failed = 0;
foreach (var test in tests) { try { test.Run(); Console.WriteLine($"PASS {test.Name}"); } catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {error}"); } }
Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
return failed == 0 ? 0 : 1;
