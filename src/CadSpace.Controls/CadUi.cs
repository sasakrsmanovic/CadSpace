using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Shared compact control construction and stable accessibility identifiers.</summary>
public static class CadUi
{
    public static T Identify<T>(T element, string id, string name) where T : DependencyObject
    { AutomationProperties.SetAutomationId(element, id); AutomationProperties.SetName(element, name); return element; }
    public static Button IconButton(string icon, string title, Action action, string? id = null, double size = 28)
    {
        var button = CadTheme.Button(title, action, size); button.Height = size; button.MinHeight = size;
        button.Padding = new Thickness(4); button.Background = CadTheme.Brush(0x00252B34);
        button.Content = new CadIcon { Kind = icon, Width = size - 8, Height = size - 8, IsHitTestVisible = false };
        ToolTipService.SetToolTip(button, title); return Identify(button, id ?? "ui." + icon, title);
    }
    public static Button TextButton(string text, Action action, string id)
    {
        var button = CadTheme.Button(text, action); button.FontSize = 11; button.Height = 26; button.MinHeight = 24;
        button.Padding = new Thickness(7, 2, 7, 2); button.CornerRadius = new CornerRadius(0);
        return Identify(button, id, text);
    }
    public static string Label(string command) => command switch {
        "LAYOUT_NEW" => "New Layout", "LAYOUT_RENAME" => "Rename", "LAYOUT_DELETE" => "Delete Layout", "CLEANSCREENON" => "Clean Screen",
        "PLINE" => "Polyline", "RECTANG" => "Rectangle", "DIMALIGNED" => "Dimension", "3DORBIT" => "Orbit",
        "QSELECT" => "Quick Select", "SELECTSIMILAR" => "Select Similar", "PEDIT" => "Edit Polyline", "PLINEWID" => "Width",
        "LAYER" => "Layer Properties", "LINETYPE" => "Linetypes", "TOOLPALETTES" => "Tool Palettes", "PROPERTIES" => "Properties",
        "RENDERSTATS" => "Render Statistics", "UISTATS" => "UI Diagnostics", "VSCURRENT" => "Visual Style",
        "EXPORT_BINARY" => "Binary DXF", "EXPORT" => "Export DXF", "NEW" => "New", "OPEN" => "Open", "SAVE" => "Save",
        _ => command.Length > 1 ? char.ToUpper(command[0]) + command[1..].ToLowerInvariant() : command
    };
}
