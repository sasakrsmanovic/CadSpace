using CadSpace.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CadSpace.Controls;

public sealed class CadDocumentTabs : UserControl
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    public event Action<object>? ActivateRequested;
    public event Action<object>? CloseRequested;
    public event Action? NewRequested;
    public CadDocumentTabs() => Content = new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = CadTheme.Brush(0xFF20262F) };
    public void SetDocuments(IEnumerable<(object Key, string Name, bool Dirty)> documents, object active)
    {
        _tabs.Children.Clear();
        foreach (var document in documents)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var title = CadTheme.Button(document.Name + (document.Dirty ? " *" : ""), () => ActivateRequested?.Invoke(document.Key)); title.Background = CadTheme.Brush(Equals(document.Key, active) ? 0xFF414C5B : 0xFF29313B); title.Padding = new Thickness(18, 5, 12, 5); row.Children.Add(title);
            var close = CadTheme.Button("×", () => CloseRequested?.Invoke(document.Key)); close.Background = title.Background; close.MinWidth = 23; close.Padding = new Thickness(5, 3, 5, 3); row.Children.Add(close); _tabs.Children.Add(row);
        }
        _tabs.Children.Add(CadTheme.Button("+", () => NewRequested?.Invoke(), 34));
    }
}

public sealed class CadStatusBar : UserControl
{
    private readonly StackPanel _toggles = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly TextBlock _coordinates = CadTheme.Text("0.000, 0.000, 0.000", 10, CadTheme.Muted);
    private CadSession? _session;
    public CadStatusBar()
    {
        var root = new Grid { Background = CadTheme.Brush(0xFF202630) }; root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); root.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _coordinates.Margin = new Thickness(12, 0, 12, 0); root.Children.Add(_coordinates); Grid.SetColumn(_toggles, 1); root.Children.Add(_toggles); Content = root;
    }
    public void Bind(CadSession session) { _session = session; Refresh(); }
    public void SetCoordinates(CadSpace.Geometry.Vec3 point) => _coordinates.Text = FormattableString.Invariant($"{point.X:0.000}, {point.Y:0.000}, {point.Z:0.000}     |     {_session?.ActiveLayout ?? "Model"}");
    public void Toggle(string name)
    {
        if (_session == null) return;
        switch (name) { case "GRID": _session.GridVisible = !_session.GridVisible; break; case "SNAP": _session.GridSnap = !_session.GridSnap; break; case "ORTHO": _session.Ortho = !_session.Ortho; break; case "POLAR": _session.Polar = !_session.Polar; break; case "OSNAP": _session.ObjectSnap = !_session.ObjectSnap; break; }
        _session.Invalidate(); Refresh();
    }
    private void Refresh()
    {
        if (_session == null) return; _toggles.Children.Clear();
        foreach (var (name, state) in new[] { ("GRID", _session.GridVisible), ("SNAP", _session.GridSnap), ("ORTHO", _session.Ortho), ("POLAR", _session.Polar), ("OSNAP", _session.ObjectSnap) })
        {
            var button = CadTheme.Button(name, () => Toggle(name)); button.FontSize = 10; button.MinHeight = 25; button.Padding = new Thickness(8, 2, 8, 2); button.Background = CadTheme.Brush(state ? 0xFF345B7B : 0xFF29323D); _toggles.Children.Add(button);
        }
        _toggles.Children.Add(CadTheme.Text("  WCS  |  DECIMAL  ", 10, CadTheme.Muted));
    }
}

/// <summary>Composable CAD shell. File persistence and document ownership remain with the hosting app.</summary>
public sealed class CadWorkspace : UserControl
{
    public CadRibbon Ribbon { get; } = new();
    public CadViewport Viewport { get; } = new();
    public CadCommandLine CommandLine { get; } = new();
    public CadPalette Palette { get; } = new();
    public CadDocumentTabs DocumentTabs { get; } = new();
    public CadStatusBar StatusBar { get; } = new();
    public event Action<string>? FileRequested;
    private CommandEngine? _commands;
    private CadSession? _session;
    private readonly ComboBox _layoutSelector = new() { MinWidth = 100, MinHeight = 25, FontSize = 10 };
    private bool _updatingLayouts;
    private readonly TextBlock _title = CadTheme.Text("CadSpace  —  Drafting & Modeling", 12);
    private readonly ColumnDefinition _paletteColumn = new() { Width = new GridLength(272) };
    private double _paletteWidth = 272;
    public CadWorkspace()
    {
        RequestedTheme = ElementTheme.Dark;
        var root = CadTheme.Grid(34, 120, 30, -1, 27, 74, 27); root.Background = CadTheme.Brush(CadTheme.Background);
        var titlebar = new Grid { Background = CadTheme.Brush(0xFF20252D) }; titlebar.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); titlebar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); titlebar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        var brand = CadTheme.Text("CS", 17, 0xFFFFFFFF); brand.Margin = new Thickness(11, 0, 11, 0); quick.Children.Add(CadTheme.Box(brand, 0xFFB44547));
        foreach (var (label, command) in new[] { ("New", "NEW"), ("Open", "OPEN"), ("Save", "SAVE"), ("Export DXF", "EXPORT"), ("Binary DXF", "EXPORT_BINARY") }) quick.Children.Add(CadTheme.Button(label, () => FileRequested?.Invoke(command)));
        quick.Children.Add(CadTheme.Button("↶", () => _commands?.Start("UNDO"))); quick.Children.Add(CadTheme.Button("↷", () => _commands?.Start("REDO")));
        titlebar.Children.Add(quick); _title.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(_title, 1); titlebar.Children.Add(_title);
        var extras = new StackPanel { Orientation = Orientation.Horizontal }; extras.Children.Add(CadTheme.Button("Studio plan", () => FileRequested?.Invoke("STUDIO"))); extras.Children.Add(CadTheme.Button("3D example", () => FileRequested?.Invoke("MODEL"))); extras.Children.Add(CadTheme.Button("About", () => FileRequested?.Invoke("ABOUT"))); Grid.SetColumn(extras, 2); titlebar.Children.Add(extras);
        CadTheme.At(root, titlebar, 0); CadTheme.At(root, Ribbon, 1); CadTheme.At(root, DocumentTabs, 2);
        var area = new Grid(); area.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); area.ColumnDefinitions.Add(new() { Width = new GridLength(5) }); area.ColumnDefinitions.Add(_paletteColumn);
        area.Children.Add(Viewport); Grid.SetColumn(Palette, 2); area.Children.Add(Palette);
        var splitter = new Border { Background = CadTheme.Brush(CadTheme.Edge) }; Grid.SetColumn(splitter, 1); area.Children.Add(splitter); var dragging = false; double previousX = 0;
        splitter.PointerPressed += (_, e) => { dragging = true; previousX = e.GetCurrentPoint(root).Position.X; splitter.CapturePointer(e.Pointer); e.Handled = true; };
        splitter.PointerMoved += (_, e) => { if (!dragging) return; var x = e.GetCurrentPoint(root).Position.X; _paletteWidth = Math.Clamp(_paletteWidth + previousX - x, 245, 520); _paletteColumn.Width = new GridLength(_paletteWidth); previousX = x; };
        splitter.PointerReleased += (_, e) => { dragging = false; splitter.ReleasePointerCapture(e.Pointer); }; splitter.PointerCaptureLost += (_, _) => dragging = false;
        CadTheme.At(root, area, 3);
        var modelbar = new StackPanel { Orientation = Orientation.Horizontal, Background = CadTheme.Brush(0xFF252C36), Spacing = 4 }; modelbar.Children.Add(CadTheme.Button("MODEL", () => { if (_session != null) _session.ActiveLayout = "Model"; Viewport.Set3D(false); Viewport.Fit(); })); modelbar.Children.Add(CadTheme.Button("3D VIEW", () => Viewport.Set3D(true))); modelbar.Children.Add(_layoutSelector);
        _layoutSelector.SelectionChanged += (_, _) => { if (!_updatingLayouts && _session != null && _layoutSelector.SelectedItem is string layout) { _commands?.Cancel(); _session.ActiveLayout = layout; Viewport.Fit(); } }; CadTheme.At(root, modelbar, 4);
        CadTheme.At(root, CommandLine, 5); CadTheme.At(root, StatusBar, 6); Content = root;
        Ribbon.CommandRequested += command => { if (_commands == null) return; if (command is not ("TOP" or "3DORBIT" or "ZOOM" or "UNDO" or "REDO" or "HELP")) Viewport.Set3D(false); _commands.Start(command); CommandLine.FocusInput(); };
        Viewport.CoordinatesChanged += StatusBar.SetCoordinates; Viewport.Message += CommandLine.AddMessage; Palette.Message += CommandLine.AddMessage;
        Palette.InsertRequested += name => { Viewport.Set3D(false); _commands?.Start("INSERT"); _commands?.Submit(name); CommandLine.FocusInput(); };
        DocumentTabs.NewRequested += () => FileRequested?.Invoke("NEW");
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control, () => FileRequested?.Invoke("NEW")); AddShortcut(VirtualKey.O, VirtualKeyModifiers.Control, () => FileRequested?.Invoke("OPEN")); AddShortcut(VirtualKey.S, VirtualKeyModifiers.Control, () => FileRequested?.Invoke("SAVE")); AddShortcut(VirtualKey.E, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => FileRequested?.Invoke("EXPORT"));
        AddShortcut(VirtualKey.Z, VirtualKeyModifiers.Control, () => _commands?.Start("UNDO"), true); AddShortcut(VirtualKey.Y, VirtualKeyModifiers.Control, () => _commands?.Start("REDO"), true); AddShortcut(VirtualKey.A, VirtualKeyModifiers.Control, () => _commands?.Start("SELECTALL"), true);
        AddShortcut(VirtualKey.Delete, VirtualKeyModifiers.None, () => { if (_commands?.Session.Selection.Count > 0) _commands.Start("ERASE"); }, true);
        AddShortcut(VirtualKey.F3, VirtualKeyModifiers.None, () => StatusBar.Toggle("OSNAP")); AddShortcut(VirtualKey.F7, VirtualKeyModifiers.None, () => StatusBar.Toggle("GRID")); AddShortcut(VirtualKey.F8, VirtualKeyModifiers.None, () => StatusBar.Toggle("ORTHO")); AddShortcut(VirtualKey.F9, VirtualKeyModifiers.None, () => StatusBar.Toggle("SNAP")); AddShortcut(VirtualKey.F10, VirtualKeyModifiers.None, () => StatusBar.Toggle("POLAR"));
        KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { _commands?.Cancel(); CommandLine.Input.Text = ""; e.Handled = true; } };
        SizeChanged += (_, _) => { var narrow = ActualWidth < 850; _paletteColumn.Width = narrow ? new GridLength(0) : new GridLength(_paletteWidth); Palette.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible; _title.Visibility = ActualWidth < 1200 ? Visibility.Collapsed : Visibility.Visible; };
    }
    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action, bool preserveTextEditing = false)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) =>
        {
            if (preserveTextEditing && XamlRoot != null && FocusManager.GetFocusedElement(XamlRoot) is TextBox text && (text != CommandLine.Input || text.Text.Length != 0)) return;
            action(); e.Handled = true;
        };
        KeyboardAccelerators.Add(accelerator);
    }
    private void RefreshLayouts()
    {
        if (_session == null || _updatingLayouts) return; _updatingLayouts = true;
        try { var names = _session.AvailableLayouts.ToArray(); if (_layoutSelector.ItemsSource is not string[] previous || !previous.SequenceEqual(names)) _layoutSelector.ItemsSource = names; _layoutSelector.SelectedItem = _session.ActiveLayout; }
        finally { _updatingLayouts = false; }
    }
    public void SetTitle(string name) => _title.Text = name + "  —  CadSpace";
    public void Bind(CadSession session, CommandEngine commands)
    {
        if (_session != null) _session.Changed -= RefreshLayouts;
        _session = session; _session.Changed += RefreshLayouts; RefreshLayouts();
        _commands = commands; Viewport.Bind(session, commands); Palette.Bind(session); CommandLine.Bind(commands); StatusBar.Bind(session); SetTitle(session.Document.Drawing.Name);
    }
}
