using CadSpace.Engine;
using CadSpace.Geometry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

public sealed class CadStatusBar : UserControl
{
    private readonly TextBlock _coordinates = CadTheme.Text("0.000, 0.000, 0.000", 10, CadTheme.Muted);
    private readonly Dictionary<string, Button> _toggles = new();
    private readonly Button _snapOptions;
    private readonly ComboBox _workspace = new() { ItemsSource = WorkspaceLayout.Presets, SelectedIndex = 0, Width = 174, Height = 25, MinHeight = 25, FontSize = 10, Padding = new Thickness(5, 0, 22, 0) };
    private CadSession? _session; private bool _building;
    public event Action<string>? WorkspaceRequested;
    public event Action? OptionsRequested;
    public CadStatusBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Background = CadTheme.Brush(0xFF202A36) };
        _coordinates.MinWidth = 190; _coordinates.Margin = new Thickness(8, 0, 8, 0); row.Children.Add(_coordinates);
        foreach (var (name, title) in new[] { ("GRID", "Grid display (F7)"), ("SNAP", "Grid snap (F9)"), ("ORTHO", "Ortho (F8)"), ("POLAR", "Polar tracking (F10)"), ("OSNAP", "Object snap (F3)"), ("DYN", "Dynamic input (F12)"), ("SC", "Selection cycling") })
        { var button = CadUi.IconButton(name, title, () => Toggle(name), "status." + name, 25); _toggles.Add(name, button); row.Children.Add(button); }
        _snapOptions = CadUi.TextButton("▾", () => { }, "status.snapOptions"); _snapOptions.Padding = new Thickness(3, 0, 3, 0); row.Children.Add(_snapOptions);
        _workspace.SelectionChanged += (_, _) => { if (!_building && _workspace.SelectedItem is string name) WorkspaceRequested?.Invoke(name); };
        CadUi.Identify(_workspace, "status.workspace", "Workspace"); row.Children.Add(_workspace);
        row.Children.Add(CadUi.IconButton("OPTIONS", "Workspace options", () => OptionsRequested?.Invoke(), "status.options", 25));
        Content = row; SizeChanged += (_, _) => _coordinates.Visibility = XamlRoot?.Size.Width < 1150 ? Visibility.Collapsed : Visibility.Visible;
    }
    public void Bind(CadSession session)
    { if (_session != null) _session.Changed -= Refresh; _session = session; session.Changed += Refresh; Refresh(); }
    public void SetWorkspace(string workspace) { _building = true; try { _workspace.SelectedItem = workspace; } finally { _building = false; } }
    public void SetCoordinates(Vec3 point)
    { var text = FormattableString.Invariant($"{point.X:0.000}, {point.Y:0.000}, {point.Z:0.000}"); if (_coordinates.Text != text) _coordinates.Text = text; }
    public void Toggle(string name)
    {
        if (_session == null) return;
        switch (name) { case "GRID": _session.GridVisible = !_session.GridVisible; break; case "SNAP": _session.GridSnap = !_session.GridSnap; break; case "ORTHO": _session.Ortho = !_session.Ortho; break; case "POLAR": _session.Polar = !_session.Polar; break; case "OSNAP": _session.ObjectSnap = !_session.ObjectSnap; break; case "DYN": _session.DynamicInput = !_session.DynamicInput; break; case "SC": _session.SelectionCycling = !_session.SelectionCycling; break; }
        _session.Invalidate();
    }
    private ObjectSnapModes? _builtModes;
    private void Refresh()
    {
        if (_session == null) return; var s = _session;
        foreach (var (name, active) in new[] { ("GRID", s.GridVisible), ("SNAP", s.GridSnap), ("ORTHO", s.Ortho), ("POLAR", s.Polar), ("OSNAP", s.ObjectSnap), ("DYN", s.DynamicInput), ("SC", s.SelectionCycling) })
        { _toggles[name].Background = CadTheme.Brush(active ? 0xFF285881 : 0xFF293444); Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(_toggles[name], active ? "On" : "Off"); }
        if (_builtModes == s.SnapModes) return; _builtModes = s.SnapModes;
        var menu = new MenuFlyout();
        foreach (var mode in new[] { ObjectSnapModes.Endpoint, ObjectSnapModes.Midpoint, ObjectSnapModes.Center, ObjectSnapModes.Quadrant, ObjectSnapModes.Intersection, ObjectSnapModes.Perpendicular, ObjectSnapModes.Tangent, ObjectSnapModes.Nearest })
        {
            var item = new ToggleMenuFlyoutItem { Text = mode.ToString(), IsChecked = (s.SnapModes & mode) != 0 };
            item.Click += (_, _) => { if (item.IsChecked) s.SnapModes |= mode; else s.SnapModes &= ~mode; s.Invalidate(); }; menu.Items.Add(item);
        }
        _snapOptions.Flyout = menu;
    }
}
