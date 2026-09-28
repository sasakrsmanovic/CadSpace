using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using CadSpace.Engine;
using Windows.Storage;

namespace CadSpace.App;

public sealed partial class App
{
    private bool _preferencesLoaded, _preferencesSaving;
    private long _preferencesRevision;
    private string? _pendingPreferences;
    private string WorkspacePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "workspace-v1.json");
    private async void LoadWorkspacePreferences()
    {
        if (_preferencesLoaded || _workspace == null) return; _preferencesLoaded = true; var revision = _preferencesRevision;
        try
        {
            var text = OperatingSystem.IsBrowser() ? BrowserWorkspacePreferences.Read() : File.Exists(WorkspacePath) ? await File.ReadAllTextAsync(WorkspacePath) : "";
            if (text.Length > 0 && revision == _preferencesRevision) _workspace.RestorePreferences(WorkspaceLayout.Decode(text));
        }
        catch (Exception e) { _workspace.CommandLine.AddMessage("Workspace preferences were not restored: " + e.Message); }
    }
    private async void QueueWorkspaceSave(WorkspaceLayout layout)
    {
        _preferencesRevision++; _pendingPreferences = layout.Encode();
        if (_preferencesSaving) return; _preferencesSaving = true;
        try
        {
            while (_pendingPreferences is string text)
            {
                _pendingPreferences = null;
                if (OperatingSystem.IsBrowser()) BrowserWorkspacePreferences.Write(text);
                else { Directory.CreateDirectory(Path.GetDirectoryName(WorkspacePath)!); await File.WriteAllTextAsync(WorkspacePath, text); }
            }
        }
        catch (Exception e) { _workspace?.CommandLine.AddMessage("Workspace preferences were not saved: " + e.Message); }
        finally { _preferencesSaving = false; }
    }
}

[SupportedOSPlatform("browser")]
internal static partial class BrowserWorkspacePreferences
{
    [JSImport("globalThis.CadSpaceWorkspaceSettings.read")] internal static partial string Read();
    [JSImport("globalThis.CadSpaceWorkspaceSettings.write")] internal static partial void Write(string value);
}
