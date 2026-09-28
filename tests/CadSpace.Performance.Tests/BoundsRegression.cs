using System.Collections.Immutable;
using System.Diagnostics;
using CadSpace.Geometry;
using CadSpace.Model;

internal static class BoundsRegression
{
    private static Bounds3 PreviousBounds(DrawingScene scene) => Bounds3.From(scene.Paths.SelectMany(p => p.Points)
        .Concat(scene.Texts.SelectMany(t => new[] { t.Position, t.Position + t.AxisX * (t.Height * Math.Max(1, t.Text.Length) * .65) + t.AxisY * t.Height }))
        .Concat(scene.Triangles.SelectMany(t => new[] { t.A, t.B, t.C })));
    public static void Register(Action<string, Action> test)
    {
        void Check(bool c) { if (!c) throw new Exception("Scene bounds assertion failed."); }
        test("bounds preserve prior text path and face fit semantics", () =>
        {
            var scene = new DrawingScene([new(Guid.NewGuid(), "0", 0, [new(-50, 30, -3), new(10, 20, 20)], false)],
                [new(Guid.NewGuid(), "0", 0, new(100, 50, 60), "Text\nline", 4, 20) { AxisX = new(.5, .5, .7), AxisY = new(.2, .8, -.2) }],
                [new(Guid.NewGuid(), 0, new(-10, -20, -70), new(5, 40, 6), new(30, 50, 40))]);
            Check(scene.Bounds == PreviousBounds(scene));
        });
        test("empty scene cached bounds remain empty", () => { var scene = new DrawingScene([], [], []); Check(scene.Bounds.IsEmpty && scene.Bounds == PreviousBounds(scene)); });
        test("record copy has independent bounds cache", () =>
        {
            var scene = EntityGeometry.BuildScene(Drawing.Empty with { Entities = [new LineEntity(default, new(10, 0))] }); var before = scene.Bounds;
            var copy = scene with { Paths = [scene.Paths[0] with { Points = [new(-100, 0), new(250, 0)] }] };
            Check(copy.Bounds.Min.X == -100 && copy.Bounds.Max.X == 250 && scene.Bounds == before);
        });
        test("parallel bounds reads agree", () => { var scene = EntityGeometry.BuildScene(Drawing.Empty with { Entities = [MeshFactory.Box(default, new(20, 30, 40))] }); Parallel.For(0, 128, _ => Check(scene.Bounds == PreviousBounds(scene))); });
        test("100000-line repeated bounds benchmark matches previous algorithm", () =>
        {
            var paths = Enumerable.Range(0, 100000).Select(i => new ScenePath(Guid.NewGuid(), "0", 0, [new(i, i % 70, i % 13), new(i + 5, i % 70 + 2, i % 13 + 3)], false)).ToImmutableArray();
            var scene = new DrawingScene(paths, [], []); var expected = PreviousBounds(scene); Check(scene.Bounds == expected);
            var watch = Stopwatch.StartNew(); long start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) Check(scene.Bounds == expected);
            watch.Stop(); var cachedTime = watch.Elapsed.TotalMilliseconds; var cachedBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            start = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
            for (var i = 0; i < 100; i++) Check(PreviousBounds(scene) == expected);
            watch.Stop(); var previousBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Check(cachedBytes < 1024 && previousBytes > cachedBytes);
            var line = $"BOUNDS 100000 paths / 100 warmed queries: cached={cachedTime:0.###} ms / {cachedBytes} bytes; previous={watch.Elapsed.TotalMilliseconds:0.###} ms / {previousBytes} bytes; matching=true";
            Console.WriteLine(line); var file = Environment.GetEnvironmentVariable("CADSPACE_BENCHMARK_OUTPUT");
            if (!string.IsNullOrEmpty(file)) File.AppendAllText(file, line + Environment.NewLine);
        });
    }
}
