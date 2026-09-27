using CadSpace.Dxf;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace CadSpace.App;

public sealed partial class App
{
    private RecoveryJournal? _recovery;
    private readonly SemaphoreSlim _recoveryGate = new(1, 1);
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _recoveryStarted;
    private async void StartRecovery()
    {
        if (_recoveryStarted) return; _recoveryStarted = true;
        try
        {
            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("CadSpaceRecovery", CreationCollisionOption.OpenIfExists);
            _recovery = new(new LocalRecoveryStorage(folder));
            var found = await _recovery.ReadAsync();
            if (found.Snapshots.Length > 0) _workspace?.CommandLine.AddMessage($"Recovery: {found.Snapshots.Length} unsaved drawing(s) available. Choose Recover to restore them.");
            foreach (var warning in found.Warnings) _workspace?.CommandLine.AddMessage(warning);
            _recoveryTimer.Tick += async (_, _) => await CheckpointDrawings(); _recoveryTimer.Start();
        }
        catch (Exception error) { _workspace?.CommandLine.AddMessage("Automatic recovery is unavailable: " + error.Message); }
    }
    private async Task CheckpointDrawings()
    {
        if (_recovery == null || _fileOperation || !await _recoveryGate.WaitAsync(0)) return;
        try
        {
            foreach (var document in _documents.ToArray())
            {
                var drawing = document.Session.Document.Drawing;
                if (!document.Session.Document.IsDirty)
                {
                    if (document.Checkpoint != null)
                    {
                        try { await _recovery.RemoveAsync(document.RecoveryKey); document.Checkpoint = null; }
                        catch (Exception error) { _workspace?.CommandLine.AddMessage("Could not clear a clean drawing checkpoint: " + error.Message); }
                    }
                    continue;
                }
                if (ReferenceEquals(document.Checkpoint, drawing)) continue;
                try
                {
                    var generation = checked(document.RecoveryGeneration + 1);
                    await _recovery.SaveAsync(document.RecoveryKey, generation, document.DisplayName, drawing, document.Source);
                    document.RecoveryGeneration = generation; document.Checkpoint = drawing;
                    Console.WriteLine($"CADSPACE_RECOVERY: saved generation={generation} objects={drawing.Entities.Length}");
                }
                catch (Exception error) { _workspace?.CommandLine.AddMessage("Recovery checkpoint failed: " + error.Message); }
            }
        }
        finally { _recoveryGate.Release(); }
    }
    private async Task ForgetRecovery(OpenDrawing document)
    {
        if (_recovery == null) return;
        await _recoveryGate.WaitAsync();
        try { await _recovery.RemoveAsync(document.RecoveryKey); document.Checkpoint = null; }
        catch (Exception error) { _workspace?.CommandLine.AddMessage("Could not remove an old recovery checkpoint: " + error.Message); }
        finally { _recoveryGate.Release(); }
    }
    private async Task RecoverDrawings()
    {
        if (_recovery == null || _workspace?.XamlRoot == null) { await Dialog("Drawing recovery", "Recovery storage is not available. Use native project backups."); return; }
        await _recoveryGate.WaitAsync();
        try
        {
            var found = await _recovery.ReadAsync();
            var ownKeys = _documents.Select(d => d.RecoveryKey).ToHashSet();
            var candidates = found.Snapshots.Where(s => !ownKeys.Contains(s.Key)).ToArray();
            foreach (var warning in found.Warnings) _workspace.CommandLine.AddMessage(warning);
            if (candidates.Length == 0) { await Dialog("Drawing recovery", "No closed-session checkpoints were found. Open dirty drawings are checkpointed every five seconds when local storage is available."); return; }
            var entries = new StackPanel { Spacing = 10 };
            var boxes = candidates.Select(s => new CheckBox { IsChecked = true, Content = $"{s.DisplayName} — {s.SavedAt.ToLocalTime():g}" }).ToArray();
            foreach (var box in boxes) entries.Children.Add(box);
            var dialog = new ContentDialog { XamlRoot = _workspace.XamlRoot, Title = "Drawing recovery", Content = new ScrollViewer { MaxHeight = 360, Content = entries }, PrimaryButtonText = "Restore selected", CloseButtonText = "Keep for later", DefaultButton = ContentDialogButton.Primary };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (boxes[i].IsChecked != true) continue;
                var snapshot = candidates[i]; Open(snapshot.Project.Drawing, snapshot.Project.DxfSource, displayName: snapshot.DisplayName);
                var document = _active!; document.Session.Document.MarkUnsaved();
                // Establish a new session-unique checkpoint before removing the abandoned session's slots.
                await _recovery.SaveAsync(document.RecoveryKey, 1, document.DisplayName, document.Session.Document.Drawing, document.Source);
                document.RecoveryGeneration = 1; document.Checkpoint = document.Session.Document.Drawing;
                await _recovery.RemoveAsync(snapshot.Key);
                Console.WriteLine($"CADSPACE_RECOVERY: restored objects={document.Session.Document.Drawing.Entities.Length}");
            }
            _workspace.CommandLine.AddMessage("Recovered drawings are unsaved. Save each one to a native project.");
        }
        finally { _recoveryGate.Release(); }
    }
    private sealed class LocalRecoveryStorage(StorageFolder folder) : IRecoveryStorage
    {
        public async Task<IReadOnlyList<string>> ListAsync() => (await folder.GetFilesAsync()).Select(f => f.Name).ToArray();
        public async Task<string?> ReadAsync(string key)
        {
            try
            {
                var file = await folder.GetFileAsync(key);
                if ((await file.GetBasicPropertiesAsync()).Size > (ulong)RecoveryJournal.MaximumEnvelopeCharacters * 4) throw new IOException("Checkpoint exceeds the size limit.");
                return await FileIO.ReadTextAsync(file);
            }
            catch (FileNotFoundException) { return null; }
        }
        public async Task WriteAsync(string key, string text) => await FileIO.WriteTextAsync(await folder.CreateFileAsync(key, CreationCollisionOption.ReplaceExisting), text);
        public async Task DeleteAsync(string key) { try { await (await folder.GetFileAsync(key)).DeleteAsync(); } catch (FileNotFoundException) { } }
    }
}
