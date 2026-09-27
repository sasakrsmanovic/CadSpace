using CadSpace.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

public sealed class CadRibbon : UserControl
{
    private readonly Grid _root = CadTheme.Grid(28, 92);
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<string, Button> _tabs = new();
    public event Action<string>? CommandRequested;
    public CadRibbon()
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Background = CadTheme.Brush(CadTheme.Background), Spacing = 1 };
        foreach (var tab in new[] { "Home", "Insert", "Annotate", "3D Modeling", "View" })
        {
            var button = CadTheme.Button(tab, () => Show(tab)); button.MinWidth = 65; _tabs.Add(tab, button); tabs.Children.Add(button);
        }
        CadTheme.At(_root, tabs, 0);
        var scroll = new ScrollViewer { Content = _groups, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled, Background = CadTheme.Brush(CadTheme.Panel) };
        CadTheme.At(_root, scroll, 1); Content = _root; Show("Home");
    }
    public void Show(string tab)
    {
        foreach (var pair in _tabs) pair.Value.Background = CadTheme.Brush(pair.Key == tab ? CadTheme.Panel : CadTheme.Background);
        _groups.Children.Clear();
        switch (tab)
        {
            case "Home": Group("Draw", "LINE", "PLINE", "CIRCLE", "ARC", "RECTANG"); Group("Modify", "MOVE", "COPY", "ROTATE", "SCALE", "MIRROR", "OFFSET"); Group("Annotation", "TEXT", "DIMALIGNED"); Group("Block", "BLOCK", "INSERT"); Group("Utilities", "ERASE", "ZOOM"); break;
            case "Insert": Group("Block definitions", "BLOCK", "INSERT", "EXPLODE"); Group("Pattern", "ARRAY"); Group("Drawing", "POINT", "TEXT"); break;
            case "Annotate": Group("Text and dimensions", "TEXT", "DIMALIGNED"); Group("Fill", "HATCH"); Group("Inquiry", "DIST", "AREA"); break;
            case "3D Modeling": Group("Mesh primitives", "BOX", "CYLINDER", "SPHERE", "CONE"); Group("Mesh surfaces", "EXTRUDE", "REVOLVE"); Group("Modify", "MOVE", "COPY", "ROTATE", "SCALE"); Group("View", "3DORBIT", "TOP", "ZOOM"); break;
            case "View": Group("Viewport", "TOP", "3DORBIT", "ZOOM"); Group("Selection", "SELECTALL"); Group("History", "UNDO", "REDO"); Group("Command reference", "HELP"); break;
        }
    }
    private void Group(string title, params string[] commands)
    {
        var root = CadTheme.Grid(68, 18); var items = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var command in commands)
        {
            var info = CommandEngine.Commands.First(c => c.Name == command);
            var label = command switch { "RECTANG" => "Rectangle", "PLINE" => "Polyline", "DIMALIGNED" => "Dimension", "3DORBIT" => "Orbit", "SELECTALL" => "Select all", _ => char.ToUpper(command[0]) + command[1..].ToLowerInvariant() };
            items.Children.Add(new CadToolButton(command, label, info.Description, () => CommandRequested?.Invoke(command)));
        }
        CadTheme.At(root, items, 0); var caption = CadTheme.Text(title, 10, CadTheme.Muted); caption.HorizontalAlignment = HorizontalAlignment.Center; CadTheme.At(root, caption, 1);
        _groups.Children.Add(new Border { Child = root, Padding = new Thickness(6, 1, 6, 0), BorderBrush = CadTheme.Brush(CadTheme.Edge), BorderThickness = new Thickness(0, 0, 1, 0) });
    }
}
