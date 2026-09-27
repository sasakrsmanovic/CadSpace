using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using CadSpace.Dxf;

namespace CadSpace.App;

/// <summary>Browser persistence acknowledges the IndexedDB transaction, not an in-memory filesystem write.</summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserRecoveryStorage : IRecoveryStorage
{
    public async Task<IReadOnlyList<string>> ListAsync()
    {
        using var json = JsonDocument.Parse(await ListKeys());
        return json.RootElement.EnumerateArray().Select(key => key.GetString()!).ToArray();
    }
    public async Task<string?> ReadAsync(string key)
    {
        var text = await ReadValue(key);
        return text.Length == 0 ? null : text;
    }
    public Task WriteAsync(string key, string text) => WriteValue(key, text);
    public Task DeleteAsync(string key) => DeleteValue(key);

    [JSImport("globalThis.CadSpaceRecoveryStorage.list")]
    private static partial Task<string> ListKeys();
    [JSImport("globalThis.CadSpaceRecoveryStorage.read")]
    private static partial Task<string> ReadValue(string key);
    [JSImport("globalThis.CadSpaceRecoveryStorage.write")]
    private static partial Task WriteValue(string key, string text);
    [JSImport("globalThis.CadSpaceRecoveryStorage.remove")]
    private static partial Task DeleteValue(string key);
}
