using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool value, string reason = "Assertion failed") { if (!value) throw new Exception(reason); }
void Near(double a, double b, double epsilon = 1e-7) => Check(Math.Abs(a - b) <= epsilon, $"Expected {b}, got {a}");
void Reject(Action action) { var rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, "Expected validation failure"); }
SplineEntity QuarterCircle() => new(2, [new(1, 0), new(1, 1), new(0, 1)], [0, 0, 0, 1, 1, 1], [1, Math.Sqrt(.5), 1]);
ImmutableArray<PolyVertex> Rect(double a, double b) => [new(new(a, a)), new(new(b, a)), new(new(b, b)), new(new(a, b))];
double Area(MeshEntity m) { double area = 0; for (var i = 0; i < m.Triangles.Length; i += 3) area += (m.Vertices[m.Triangles[i + 1]] - m.Vertices[m.Triangles[i]]).Cross(m.Vertices[m.Triangles[i + 2]] - m.Vertices[m.Triangles[i]]).Length / 2; return area; }
Test("rational spline exact quarter-circle samples", () => { var s = QuarterCircle(); for (var i = 0; i <= 100; i++) Near(AdvancedGeometry.Evaluate(s, i / 100.0).Length, 1); });
Test("rational spline midpoint", () => { var p = AdvancedGeometry.Evaluate(QuarterCircle(), .5); Near(p.X, Math.Sqrt(.5)); Near(p.Y, Math.Sqrt(.5)); });
Test("spline clamps domain endpoints", () => { var s = QuarterCircle(); Check(AdvancedGeometry.Evaluate(s, -1) == new Vec3(1, 0)); Check(AdvancedGeometry.Evaluate(s, 2) == new Vec3(0, 1)); });
Test("spline rejects descending knots", () => Reject(() => AdvancedGeometry.Evaluate(QuarterCircle() with { Knots = [0, 0, 0, 1, .5, 1] }, .5)));
Test("spline rejects zero weights", () => Reject(() => AdvancedGeometry.Evaluate(QuarterCircle() with { Weights = [1, 0, 1] }, .5)));
Test("spline rejects nonfinite parameter", () => Reject(() => AdvancedGeometry.Evaluate(QuarterCircle(), double.NaN)));
Test("spline tessellation respects circle geometry", () => { var points = AdvancedGeometry.Tessellate(QuarterCircle()); Check(points.Length > 10 && points.Length < 1000); foreach (var p in points) Near(p.Length, 1); });
Test("spline affine transformation is analytic", () => { var s = QuarterCircle(); var t = Transform3.Scaling(new(3, 2, 1)).Then(Transform3.RotationAxis(new(1, 2, 3), 37)).Then(Transform3.Translation(new(1e7, -2e7, 50))); var transformed = (SplineEntity)EntityGeometry.Transform(s, t); for (var i = 0; i <= 10; i++) Near(AdvancedGeometry.Evaluate(transformed, i / 10.0).DistanceTo(t.Point(AdvancedGeometry.Evaluate(s, i / 10.0))), 0, 1e-7); });
Test("arbitrary-axis OCS frame", () => { foreach (var n in new Vec3[] { Vec3.UnitZ, -Vec3.UnitZ, Vec3.UnitY, new(.01, .01, 1), new(1, 2, 3) }) { var f = Coordinates3D.ObjectCoordinateSystem(n); Near(f.Determinant, 1); Near(f.X.Dot(f.Z), 0); Near(f.Y.Dot(f.Z), 0); Near(f.Z.DistanceTo(n.Normalized), 0); } });
Test("OCS threshold axes", () => { var f = Coordinates3D.ObjectCoordinateSystem(Vec3.UnitZ); Check(f.X == Vec3.UnitX && f.Y == Vec3.UnitY); });
Test("affine inverse restores large coordinates", () => { var t = Transform3.Scaling(new(2, 3, -4)).Then(Transform3.RotationAxis(new(1, 3, 2), 58)).Then(Transform3.Translation(new(1e6, -2e6, 3e6))); var p = new Vec3(42, 78, -90); Near(Coordinates3D.Inverse(t).Point(t.Point(p)).DistanceTo(p), 0, 1e-8); });
Test("tilted circle remains in its OCS plane", () => { var f = Coordinates3D.ObjectCoordinateSystem(new(1, 2, 3)); var scene = EntityGeometry.BuildScene(Drawing.Empty with { Entities = [new PlacedEntity(new CircleEntity(default, 5), f)] }); foreach (var p in scene.Paths[0].Points) { Near(p.Dot(f.Z), 0); Near(p.Length, 5); } });
Test("general affine circle preserves geometry with placement", () => { var circle = new CircleEntity(default, 5); var transformed = EntityGeometry.Transform(circle, Transform3.Scaling(new(3, 2, 1))); Check(transformed is PlacedEntity); var bounds = EntityGeometry.BuildScene(Drawing.Empty with { Entities = [transformed] }).Bounds; Near(bounds.Size.X, 30); Near(bounds.Size.Y, 20); });
Test("nonplanar polyline retains Z", () => { var p = new Polyline3DEntity([new(0, 0, 0), new(5, 2, 7), new(10, 3, -2)]); var scene = EntityGeometry.BuildScene(Drawing.Empty with { Entities = [p] }); Check(scene.Paths[0].Points[1].Z == 7); Near(scene.Bounds.Size.Z, 9); });
Test("hatch odd-even hole area", () => { var h = new HatchRegionEntity([Rect(0, 10), Rect(2, 8)], true, []); Near(AdvancedGeometry.Expand(h).OfType<MeshEntity>().Sum(Area), 64); });
Test("hatch nested island area", () => { var h = new HatchRegionEntity([Rect(0, 10), Rect(2, 8), Rect(4, 6)], true, []); Near(AdvancedGeometry.Expand(h).OfType<MeshEntity>().Sum(Area), 68); });
Test("hatch winding does not change odd-even fill", () => { var h = new HatchRegionEntity([Rect(0, 10).Reverse().ToImmutableArray(), Rect(2, 8)], true, []); Near(AdvancedGeometry.Expand(h).OfType<MeshEntity>().Sum(Area), 64); });
Test("pattern lines do not cross hatch holes", () => { var h = new HatchRegionEntity([Rect(0, 10), Rect(2, 8)], false, [new(0, default, new(0, 1), [])]); var lines = AdvancedGeometry.Expand(h).OfType<LineEntity>().ToArray(); Check(lines.Length > 10); foreach (var line in lines) { var p = (line.Start + line.End) / 2; Check(!(p.X > 2 && p.X < 8 && p.Y > 2 && p.Y < 8)); } });
Test("hatch invalid line spacing rejected", () => Reject(() => AdvancedGeometry.Expand(new HatchRegionEntity([Rect(0, 10)], false, [new(0, default, new(1, 0), [])])).ToArray()));
Test("ray chooses forward triangle", () => { var ray = new Ray3(new(1, 1, 10), new(0, 0, -1)); Check(ray.IntersectTriangle(default, new(5, 0), new(0, 5), out var distance)); Near(distance, 10); Check(!new Ray3(new(1, 1, 10), Vec3.UnitZ).IntersectTriangle(default, new(5, 0), new(0, 5), out _)); });
Test("ray misses outside triangle", () => Check(!new Ray3(new(4, 4, 10), new(0, 0, -1)).IntersectTriangle(default, new(5, 0), new(0, 5), out _)));
Test("ray plane intersection", () => { var ray = new Ray3(new(2, 3, 5), new(0, 0, -1)); Check(ray.IntersectPlane(default, Vec3.UnitZ, out var p)); Check(p == new Vec3(2, 3)); });
Test("mesh internal coplanar diagonals are hidden", () => { var mesh = MeshFactory.Box(default, new(10, 20, 30)); var scene = EntityGeometry.BuildScene(Drawing.Empty with { Entities = [mesh] }); Check(scene.Triangles.Length == 12); Check(scene.Paths.Length == 12, $"Expected 12 feature edges, got {scene.Paths.Length}"); });
Test("invalid advanced edit remains atomic", () => { var d = new CadDocument(); Reject(() => d.Add("Bad spline", QuarterCircle() with { Degree = 0 })); Check(d.Drawing.Entities.IsEmpty && !d.CanUndo); });
var failures = 0;
foreach (var (name, run) in tests) try { run(); Console.WriteLine($"PASS {name}"); } catch (Exception e) { failures++; Console.Error.WriteLine($"FAIL {name}: {e}"); }
Console.WriteLine($"{tests.Count - failures}/{tests.Count} advanced geometry tests passed.");
return failures == 0 ? 0 : 1;
