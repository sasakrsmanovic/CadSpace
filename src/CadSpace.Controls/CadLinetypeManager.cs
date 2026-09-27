using System.Collections.Immutable;
using System.Globalization;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Reusable linetype definition and assignment UI; simple signed DXF patterns are editable.</summary>
public sealed class CadLinetypeManager : UserControl
{
    private readonly ListView _list = new() { Height = 180, FontSize = 12 };
    private readonly TextBox _name = new() { Header = "Name", Width = 180, FontSize = 12 };
    private readonly TextBox _pattern = new() { Header = "Pattern: dash, -gap, 0 dot", Width = 330, FontSize = 12 };
    private readonly TextBox _scale = new() { Header = "Object scale", Text = "1", Width = 120, FontSize = 12 };
    private readonly TextBox _global = new() { Header = "Global scale (LTSCALE)", Text = "1", Width = 180, FontSize = 12 };
    private readonly TextBlock _status = CadTheme.Text("", 11, CadTheme.Muted);
    private CadSession? _session;
    private Linetype[] _types = [];
    public CadLinetypeManager()
    {
        var body = new StackPanel { Width = 550, Spacing = 12 };
        body.Children.Add(CadTheme.Text("Simple CAD patterns • drawing-unit lengths • undoable definitions", 11, CadTheme.Muted));
        body.Children.Add(_list);
        var fields = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; fields.Children.Add(_name); fields.Children.Add(_pattern); body.Children.Add(fields);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(CadTheme.Button("Load standard patterns", () => Run(s =>
        {
            s.Document.Edit("Load linetypes", d => d with { Linetypes = Linetype.BuiltIns.Aggregate(d.Linetypes, (all, t) => all.ContainsKey(t.Name) ? all : all.Add(t.Name, t)) }); Refresh();
        })));
        actions.Children.Add(CadTheme.Button("Create / update", () => Run(s =>
        {
            var values = _pattern.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var numbers = values.Select(v => GeometryMath.Number(v, out var n) ? n : throw new ArgumentException("Enter comma-separated finite dash lengths.")).ToImmutableArray();
            s.LoadLinetype(new(_name.Text.Trim(), "User-defined pattern", numbers)); Refresh();
        })));
        body.Children.Add(actions);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        bottom.Children.Add(_scale); bottom.Children.Add(_global); body.Children.Add(bottom);
        var assignments = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        assignments.Children.Add(CadTheme.Button("Set for new objects", () => Run(s => { var scale = Number(_scale); s.SetCurrentLinetype(_name.Text); s.CurrentLinetypeScale = scale; s.Invalidate(); })));
        assignments.Children.Add(CadTheme.Button("Apply to selection", () => Run(s => s.SetSelectedLinetype(_name.Text, Number(_scale)))));
        assignments.Children.Add(CadTheme.Button("Set global scale", () => Run(s => { var scale = Number(_global); s.Document.Edit("Global linetype scale", d => d with { LinetypeScale = scale }); })));
        body.Children.Add(assignments); _status.TextWrapping = TextWrapping.Wrap; body.Children.Add(_status); Content = body;
        _list.SelectionChanged += (_, _) => { if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _types.Length) { var t = _types[_list.SelectedIndex]; _name.Text = t.Name; _pattern.Text = string.Join(", ", t.Elements.Select(e => e.ToString(CultureInfo.InvariantCulture))); _status.Text = t.IsComplex ? "Text/shape linetype: original data is retained; complex glyph rendering is not implemented." : t.Description; } };
    }
    public void Bind(CadSession session) { _session = session; _scale.Text = session.CurrentLinetypeScale.ToString(CultureInfo.InvariantCulture); _global.Text = session.Document.Drawing.LinetypeScale.ToString(CultureInfo.InvariantCulture); Refresh(); }
    private void Refresh()
    {
        if (_session == null) return;
        _types = _session.Document.Drawing.Linetypes.Values.OrderBy(t => t.Name).ToArray();
        _list.ItemsSource = _types.Select(t => $"{t.Name}  —  {t.Description}").ToArray();
        _name.Text = _session.CurrentLinetype;
    }
    private static double Number(TextBox box) => GeometryMath.Number(box.Text, out var n) && n > 0 && n <= 1e9 ? n : throw new ArgumentException("Scale must be positive and no larger than 1e9.");
    private void Run(Action<CadSession> action)
    {
        if (_session == null) return;
        try { action(_session); _status.Text = "Applied. Drawing changes support Undo."; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { _status.Text = e.Message; }
    }
}
