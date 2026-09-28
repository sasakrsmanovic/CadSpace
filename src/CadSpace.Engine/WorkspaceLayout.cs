using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace CadSpace.Engine;

public enum PaletteDock { Left, Right, Floating }
public sealed record PalettePlacement(string Id, PaletteDock Dock = PaletteDock.Right, bool Visible = true,
    bool AutoHide = false, double Width = 272, double Height = 430, double X = 80, double Y = 60)
{
    public PalettePlacement Constrain(double width, double height)
    {
        width = double.IsFinite(width) ? Math.Max(1, width) : 1;
        height = double.IsFinite(height) ? Math.Max(1, height) : 1;
        var w = Math.Min(Math.Clamp(Width, 220, 640), width);
        var h = Math.Min(Math.Clamp(Height, 180, 900), height);
        return this with { Width = w, Height = h, X = Math.Clamp(X, 0, Math.Max(0, width - w)), Y = Math.Clamp(Y, 0, Math.Max(0, height - h)) };
    }
}

/// <summary>UI preferences only: independent of documents, undo, graphics and Uno.</summary>
public sealed record WorkspaceLayout
{
    public static readonly string[] Presets = ["Drafting & Annotation", "3D Basics", "3D Modeling"];
    public string Workspace { get; init; } = Presets[0];
    public bool RibbonMinimized { get; init; }
    public bool CleanScreen { get; init; }
    public bool MenuBarVisible { get; init; }
    public bool ViewCubeVisible { get; init; } = true;
    public bool NavigationBarVisible { get; init; } = true;
    public double CommandHeight { get; init; } = 74;
    public ImmutableArray<string> HiddenStatusItems { get; init; } = [];
    public static IReadOnlyList<string> StatusItems { get; } = Array.AsReadOnly(new[] { "GRID", "SNAP", "ORTHO", "POLAR", "OSNAP", "DYN", "SC" });
    public ImmutableArray<PalettePlacement> Palettes { get; init; } = [new("properties"), new("tools", PaletteDock.Left, false)];
    public static WorkspaceLayout Default => new();
    public static void Validate(WorkspaceLayout layout)
    {
        if (!Presets.Contains(layout.Workspace) || layout.Palettes.IsDefault || layout.Palettes.Length > 16 ||
            layout.Palettes.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != layout.Palettes.Length)
            throw new ArgumentException("Invalid workspace layout.");
        if (!double.IsFinite(layout.CommandHeight) || layout.CommandHeight < 74 || layout.CommandHeight > 350 ||
            layout.HiddenStatusItems.IsDefault || layout.HiddenStatusItems.Length > StatusItems.Count ||
            layout.HiddenStatusItems.Any(id => !StatusItems.Contains(id)) || layout.HiddenStatusItems.Distinct().Count() != layout.HiddenStatusItems.Length)
            throw new ArgumentException("Invalid workspace display preferences.");
        foreach (var p in layout.Palettes)
            if (string.IsNullOrWhiteSpace(p.Id) || p.Id.Length > 64 || p.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
                !Enum.IsDefined(p.Dock) || new[] { p.Width, p.Height, p.X, p.Y }.Any(v => !double.IsFinite(v)) ||
                p.Width < 220 || p.Width > 640 || p.Height < 180 || p.Height > 900 || Math.Abs(p.X) > 100000 || Math.Abs(p.Y) > 100000)
                throw new ArgumentException("Invalid palette placement.");
    }
    public string Encode()
    {
        Validate(this);
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject(); w.WriteNumber("version", 1); w.WriteString("workspace", Workspace);
            w.WriteBoolean("ribbonMinimized", RibbonMinimized); w.WriteBoolean("cleanScreen", CleanScreen);
            w.WriteBoolean("menuBarVisible", MenuBarVisible); w.WriteBoolean("viewCubeVisible", ViewCubeVisible);
            w.WriteBoolean("navigationBarVisible", NavigationBarVisible); w.WriteNumber("commandHeight", CommandHeight);
            w.WriteStartArray("hiddenStatusItems"); foreach (var id in HiddenStatusItems) w.WriteStringValue(id); w.WriteEndArray();
            w.WriteStartArray("palettes");
            foreach (var p in Palettes)
            {
                w.WriteStartObject(); w.WriteString("id", p.Id); w.WriteString("dock", p.Dock.ToString());
                w.WriteBoolean("visible", p.Visible); w.WriteBoolean("autoHide", p.AutoHide);
                w.WriteNumber("width", p.Width); w.WriteNumber("height", p.Height); w.WriteNumber("x", p.X); w.WriteNumber("y", p.Y); w.WriteEndObject();
            }
            w.WriteEndArray(); w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
    public static WorkspaceLayout Decode(string text)
    {
        if (text.Length > 32768) throw new FormatException("Workspace preferences exceed 32 Ki-characters.");
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 }); var r = doc.RootElement;
        if (r.GetProperty("version").GetInt32() != 1) throw new FormatException("Unsupported workspace preferences version.");
        var layout = new WorkspaceLayout {
            Workspace = r.GetProperty("workspace").GetString()!, RibbonMinimized = r.GetProperty("ribbonMinimized").GetBoolean(),
            CleanScreen = r.GetProperty("cleanScreen").GetBoolean(),
            MenuBarVisible = r.TryGetProperty("menuBarVisible", out var menu) && menu.GetBoolean(),
            ViewCubeVisible = !r.TryGetProperty("viewCubeVisible", out var cube) || cube.GetBoolean(),
            NavigationBarVisible = !r.TryGetProperty("navigationBarVisible", out var nav) || nav.GetBoolean(),
            CommandHeight = r.TryGetProperty("commandHeight", out var height) ? height.GetDouble() : 74,
            HiddenStatusItems = r.TryGetProperty("hiddenStatusItems", out var hidden) ? hidden.EnumerateArray().Select(x => x.GetString()!).ToImmutableArray() : [],
            Palettes = r.GetProperty("palettes").EnumerateArray().Select(p => new PalettePlacement(p.GetProperty("id").GetString()!,
                Enum.Parse<PaletteDock>(p.GetProperty("dock").GetString()!), p.GetProperty("visible").GetBoolean(), p.GetProperty("autoHide").GetBoolean(),
                p.GetProperty("width").GetDouble(), p.GetProperty("height").GetDouble(), p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToImmutableArray()
        };
        Validate(layout); return layout;
    }
}
