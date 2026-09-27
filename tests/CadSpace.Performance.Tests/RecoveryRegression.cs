using CadSpace.Dxf;
using CadSpace.Model;
using CadSpace.Geometry;

internal static class RecoveryRegression
{
    private sealed class MemoryStorage : IRecoveryStorage
    {
        public readonly Dictionary<string, string> Files = new();
        public bool TearWrite;
        public Task<IReadOnlyList<string>> ListAsync() => Task.FromResult<IReadOnlyList<string>>(Files.Keys.ToArray());
        public Task<string?> ReadAsync(string key) => Task.FromResult(Files.GetValueOrDefault(key));
        public Task WriteAsync(string key, string text) { Files[key] = TearWrite ? text[..(text.Length / 2)] : text; return Task.CompletedTask; }
        public Task DeleteAsync(string key) { Files.Remove(key); return Task.CompletedTask; }
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Recovery assertion failed."); }
    public static void Register(Action<string, Action> test)
    {
        void Async(string name, Func<Task> run) => test(name, () => run().GetAwaiter().GetResult());
        Async("recovery selects newest of two valid generations", async () =>
        {
            var storage = new MemoryStorage(); var journal = new RecoveryJournal(storage); var key = Guid.NewGuid();
            await journal.SaveAsync(key, 1, "Before", Drawing.Empty); await journal.SaveAsync(key, 2, "After", Drawing.Empty with { Entities = [new PointEntity(new(3, 4))] });
            var result = await new RecoveryJournal(storage).ReadAsync(); Check(result.Snapshots.Length == 1 && result.Warnings.IsEmpty);
            Check(result.Snapshots[0].Generation == 2 && result.Snapshots[0].DisplayName == "After" && result.Snapshots[0].Project.Drawing.Entities.Length == 1);
        });
        Async("torn checkpoint preserves previous generation and same-generation retry", async () =>
        {
            var storage = new MemoryStorage(); var journal = new RecoveryJournal(storage); var key = Guid.NewGuid();
            await journal.SaveAsync(key, 1, "Original", Drawing.Empty); storage.TearWrite = true;
            var failed = false; try { await journal.SaveAsync(key, 2, "New", Drawing.Empty); } catch (IOException) { failed = true; } Check(failed);
            var recovered = await journal.ReadAsync(); Check(recovered.Snapshots.Single().Generation == 1 && recovered.Warnings.Length == 1);
            storage.TearWrite = false; await journal.SaveAsync(key, 2, "New", Drawing.Empty); Check((await journal.ReadAsync()).Snapshots.Single().Generation == 2);
        });
        Async("checksum tampering rejects only the damaged slot", async () =>
        {
            var storage = new MemoryStorage(); var journal = new RecoveryJournal(storage); var key = Guid.NewGuid();
            await journal.SaveAsync(key, 1, "Old", Drawing.Empty); await journal.SaveAsync(key, 2, "New", Drawing.Empty);
            var file = storage.Files.Keys.Single(k => k.Contains(".0.")); storage.Files[file] += " ";
            var result = await journal.ReadAsync(); Check(result.Snapshots.Single().Generation == 1 && result.Warnings.Length == 1);
        });
        Async("recovery validates filename identity and schema version", async () =>
        {
            var storage = new MemoryStorage(); var journal = new RecoveryJournal(storage); await journal.SaveAsync(Guid.NewGuid(), 1, "One", Drawing.Empty);
            var old = storage.Files.Single(); storage.Files.Clear(); storage.Files[Guid.NewGuid().ToString("N") + ".1.recovery"] = old.Value;
            Check((await journal.ReadAsync()).Snapshots.IsEmpty); storage.Files.Clear(); storage.Files[old.Key] = old.Value.Replace("\"version\":1", "\"version\":99"); Check((await journal.ReadAsync()).Snapshots.IsEmpty);
        });
        Async("recovery retains native DXF source provenance", async () =>
        {
            var storage = new MemoryStorage(); var journal = new RecoveryJournal(storage); var original = DxfCodec.Read(DxfCodec.Write(Drawing.Empty with { Entities = [new CircleEntity(default, 5)] }).Text);
            await journal.SaveAsync(Guid.NewGuid(), 1, "Import.dxf", original.Drawing, original.Source);
            var restored = (await journal.ReadAsync()).Snapshots.Single().Project; Check(restored.DxfSource != null); Check(DxfCodec.Write(restored.Drawing, restored.DxfSource).Text == original.Source.Text);
        });
        Async("recovery removal deletes both slots and leaves other drawings", async () =>
        {
            var storage = new MemoryStorage(); var journal = new RecoveryJournal(storage); var key = Guid.NewGuid(); var other = Guid.NewGuid();
            await journal.SaveAsync(key, 1, "A", Drawing.Empty); await journal.SaveAsync(key, 2, "A", Drawing.Empty); await journal.SaveAsync(other, 1, "B", Drawing.Empty);
            await journal.RemoveAsync(key); Check((await journal.ReadAsync()).Snapshots.Single().Key == other);
        });
        Async("independent recovery keys do not overwrite each other", async () =>
        {
            var storage = new MemoryStorage(); var journal = new RecoveryJournal(storage);
            await journal.SaveAsync(Guid.NewGuid(), 1, "Same name", Drawing.Empty); await journal.SaveAsync(Guid.NewGuid(), 1, "Same name", Drawing.Empty);
            Check((await journal.ReadAsync()).Snapshots.Length == 2);
        });
        Async("recovery rejects invalid identity before writes", async () =>
        {
            var storage = new MemoryStorage(); var journal = new RecoveryJournal(storage); var failed = false;
            try { await journal.SaveAsync(Guid.Empty, 1, "A", Drawing.Empty); } catch (ArgumentException) { failed = true; }
            Check(failed && storage.Files.Count == 0);
        });
    }
}
