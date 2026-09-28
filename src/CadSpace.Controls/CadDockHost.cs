using System.Collections.Immutable;
using CadSpace.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace CadSpace.Controls;

/// <summary>Reusable palette chrome with docking, pinning, close, drag and resize affordances.</summary>
public sealed class CadDockPane : Border
{
    public string Id { get; }
    public Border DragHandle { get; }
    public Border SizeGrip { get; }
    public event Action<string>? ActionRequested;
    private readonly Button _pin;
    public CadDockPane(string id, string title, UIElement body)
    {
        Id = id; Background = CadTheme.Brush(CadTheme.Background); BorderBrush = CadTheme.Brush(CadTheme.Edge); BorderThickness = new Thickness(1);
        var root = CadTheme.Grid(26, -1, 8); var header = new Grid { Background = CadTheme.Brush(0xFF313D4C) };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        DragHandle = new Border { Child = CadTheme.Text(title.ToUpperInvariant(), 10), Padding = new Thickness(7, 0, 0, 0), Background = CadTheme.Brush(0x00313D4C) };
        CadUi.Identify(DragHandle, "dock." + id + ".drag", "Drag " + title); header.Children.Add(DragHandle);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        Button Add(string text, string action, string hint)
        {
            var b = CadUi.TextButton(text, () => ActionRequested?.Invoke(action), "dock." + id + "." + action);
            b.Width = 22; b.Padding = new Thickness(0); ToolTipService.SetToolTip(b, hint); actions.Children.Add(b); return b;
        }
        Add("‹", "left", "Dock left"); Add("□", "float", "Float in this window"); Add("›", "right", "Dock right");
        _pin = Add("●", "pin", "Pin / auto-hide"); Add("×", "close", "Close palette");
        Grid.SetColumn(actions, 1); header.Children.Add(actions); CadTheme.At(root, header, 0); CadTheme.At(root, body, 1);
        SizeGrip = new Border { Background = CadTheme.Brush(CadTheme.Edge), Width = 36, HorizontalAlignment = HorizontalAlignment.Right };
        CadUi.Identify(SizeGrip, "dock." + id + ".resize", "Resize " + title); CadTheme.At(root, SizeGrip, 2); Child = root;
    }
    public void SetPinned(bool pinned) { _pin.Content = pinned ? "●" : "○"; _pin.Foreground = CadTheme.Brush(pinned ? CadTheme.TextColor : CadTheme.Accent); }
}

/// <summary>Multi-palette host: left/right stacks, in-window floating, auto-hide and bounded geometry.</summary>
public sealed class CadDockHost : Grid
{
    private readonly Grid _left = new(), _right = new();
    private readonly StackPanel _leftTabs = new(), _rightTabs = new();
    private readonly Canvas _floating = new();
    private readonly Dictionary<string, CadDockPane> _panes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PalettePlacement> _placements = new(StringComparer.Ordinal);
    private readonly TextBlock _hintText = CadTheme.Text("", 13);
    private readonly Border _hint;
    private string? _peek;
    private bool _building, _suspended;
    public event Action? LayoutChanged;
    public CadDockHost(UIElement document)
    {
        for (var i = 0; i < 5; i++) ColumnDefinitions.Add(new() { Width = i == 2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0) });
        Children.Add(_leftTabs); Grid.SetColumn(_left, 1); Children.Add(_left); Grid.SetColumn(document, 2); Children.Add(document);
        Grid.SetColumn(_right, 3); Children.Add(_right); Grid.SetColumn(_rightTabs, 4); Children.Add(_rightTabs);
        Grid.SetColumnSpan(_floating, 5); Children.Add(_floating);
        _hint = CadTheme.Box(_hintText, 0xEE2B607D, 12); _hint.IsHitTestVisible = false; _hint.Visibility = Visibility.Collapsed;
        SizeChanged += (_, _) => Rebuild();
    }
    public void AddPane(string id, string title, UIElement content, PalettePlacement initial)
    {
        if (initial.Id != id) throw new ArgumentException("Palette identity mismatch.");
        WorkspaceLayout.Validate(new() { Palettes = [initial] });
        var pane = new CadDockPane(id, title, content); _panes.Add(id, pane); _placements.Add(id, initial);
        pane.ActionRequested += action => {
            var p = _placements[id];
            SetPlacement(action switch {
                "left" => p with { Dock = PaletteDock.Left, AutoHide = false },
                "right" => p with { Dock = PaletteDock.Right, AutoHide = false },
                "float" => p with { Dock = PaletteDock.Floating, AutoHide = false },
                "pin" => p with { AutoHide = !p.AutoHide, Dock = p.Dock == PaletteDock.Floating ? PaletteDock.Right : p.Dock },
                "close" => p with { Visible = false }, _ => p });
        };
        WireGestures(pane); Rebuild();
    }
    public ImmutableArray<PalettePlacement> Capture() => _placements.Values.OrderBy(p => p.Id).ToImmutableArray();
    public void Restore(IEnumerable<PalettePlacement> placements)
    {
        var array = placements.ToImmutableArray(); WorkspaceLayout.Validate(new() { Palettes = array });
        foreach (var p in array) if (_panes.ContainsKey(p.Id)) _placements[p.Id] = p;
        _peek = null; Rebuild();
    }
    public void Suspend(bool suspended) { _suspended = suspended; _peek = null; Rebuild(); }
    public void Toggle(string id) => SetVisible(id, !_placements[id].Visible);
    public void SetVisible(string id, bool visible) => SetPlacement(_placements[id] with { Visible = visible });
    public void DismissPeek() { if (_peek == null) return; _peek = null; Rebuild(); }
    public void SetPlacement(PalettePlacement placement)
    {
        WorkspaceLayout.Validate(new() { Palettes = [placement] });
        if (!_panes.ContainsKey(placement.Id)) throw new ArgumentException("Unknown palette.");
        _placements[placement.Id] = placement; _peek = null; Rebuild(); LayoutChanged?.Invoke();
    }
    private void Rebuild()
    {
        if (_building) return; _building = true;
        try
        {
            _left.Children.Clear(); _right.Children.Clear(); _floating.Children.Clear(); _leftTabs.Children.Clear(); _rightTabs.Children.Clear();
            _left.RowDefinitions.Clear(); _right.RowDefinitions.Clear();
            foreach (var c in new[] { 0, 1, 3, 4 }) ColumnDefinitions[c].Width = new GridLength(0);
            if (_suspended) return;
            var width = Math.Max(1, ActualWidth); var height = Math.Max(1, ActualHeight);
            foreach (var p in _placements.Values.Where(p => p.Visible))
            {
                var pane = _panes[p.Id]; pane.SetPinned(!p.AutoHide); pane.HorizontalAlignment = HorizontalAlignment.Stretch; pane.VerticalAlignment = VerticalAlignment.Stretch;
                var constrained = p.Constrain(width, height); var narrow = width < 850;
                if (p.AutoHide || narrow && p.Dock != PaletteDock.Floating)
                {
                    var left = p.Dock == PaletteDock.Left; var edge = left ? _leftTabs : _rightTabs;
                    ColumnDefinitions[left ? 0 : 4].Width = new GridLength(26);
                    var tab = CadUi.TextButton(p.Id == "properties" ? "P" : "T", () => { _peek = _peek == p.Id ? null : p.Id; Rebuild(); }, "dock." + p.Id + ".tab");
                    tab.Width = 26; tab.Height = 42; tab.Padding = new Thickness(0); ToolTipService.SetToolTip(tab, p.Id); edge.Children.Add(tab);
                    if (_peek != p.Id) continue;
                    constrained = constrained with { X = left ? 26 : Math.Max(0, width - constrained.Width - 26), Y = 0, Height = height };
                    Float(pane, constrained);
                }
                else if (p.Dock == PaletteDock.Floating) Float(pane, constrained);
                else
                {
                    var left = p.Dock == PaletteDock.Left; var area = left ? _left : _right; var column = ColumnDefinitions[left ? 1 : 3];
                    column.Width = new GridLength(Math.Max(column.Width.Value, Math.Min(p.Width, width * .38)));
                    pane.Width = pane.Height = double.NaN; Grid.SetRow(pane, area.RowDefinitions.Count);
                    area.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); area.Children.Add(pane);
                }
            }
            _floating.Children.Add(_hint);
        }
        finally { _building = false; }
    }
    private void Float(CadDockPane pane, PalettePlacement p)
    {
        pane.Width = p.Width; pane.Height = p.Height; Canvas.SetLeft(pane, p.X); Canvas.SetTop(pane, p.Y); _floating.Children.Add(pane);
    }
    private void WireGestures(CadDockPane pane)
    {
        Point start = default; PalettePlacement? before = null; bool dragged = false;
        pane.DragHandle.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; start = e.GetCurrentPoint(this).Position; before = _placements[pane.Id]; dragged = false; pane.DragHandle.CapturePointer(e.Pointer); e.Handled = true; };
        pane.DragHandle.PointerMoved += (_, e) => {
            if (before == null) return; var p = e.GetCurrentPoint(this).Position;
            if (Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) < 8 && !dragged) return; dragged = true;
            _hintText.Text = p.X < 48 ? "Dock left" : p.X > ActualWidth - 48 ? "Dock right" : "Float palette here";
            _hint.Visibility = Visibility.Visible; Canvas.SetLeft(_hint, Math.Clamp(p.X - 60, 0, Math.Max(0, ActualWidth - 180))); Canvas.SetTop(_hint, Math.Clamp(p.Y + 12, 0, Math.Max(0, ActualHeight - 50)));
            if (before.Dock == PaletteDock.Floating) { var next = (before with { X = before.X + p.X - start.X, Y = before.Y + p.Y - start.Y }).Constrain(ActualWidth, ActualHeight); Canvas.SetLeft(pane, next.X); Canvas.SetTop(pane, next.Y); }
            e.Handled = true;
        };
        pane.DragHandle.PointerReleased += (_, e) => {
            var old = before; var moved = dragged; var point = e.GetCurrentPoint(this).Position; before = null; dragged = false; _hint.Visibility = Visibility.Collapsed; pane.DragHandle.ReleasePointerCapture(e.Pointer);
            if (old != null && moved)
            {
                var dock = point.X < 48 ? PaletteDock.Left : point.X > ActualWidth - 48 ? PaletteDock.Right : PaletteDock.Floating;
                var x = old.Dock == PaletteDock.Floating ? old.X + point.X - start.X : point.X - 90;
                var y = old.Dock == PaletteDock.Floating ? old.Y + point.Y - start.Y : point.Y - 13;
                SetPlacement(old with { Dock = dock, AutoHide = false, X = Math.Clamp(x, 0, 100000), Y = Math.Clamp(y, 0, 100000) });
            }
            e.Handled = true;
        };
        pane.DragHandle.PointerCaptureLost += (_, _) => { if (before == null) return; before = null; _hint.Visibility = Visibility.Collapsed; Rebuild(); };
        Point resizeStart = default; PalettePlacement? resize = null;
        pane.SizeGrip.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; resize = _placements[pane.Id]; resizeStart = e.GetCurrentPoint(this).Position; pane.SizeGrip.CapturePointer(e.Pointer); e.Handled = true; };
        pane.SizeGrip.PointerMoved += (_, e) => {
            if (resize == null) return; var p = e.GetCurrentPoint(this).Position;
            var w = Math.Clamp(resize.Width + (p.X - resizeStart.X) * (resize.Dock == PaletteDock.Right ? -1 : 1), 220, 640);
            var h = Math.Clamp(resize.Height + p.Y - resizeStart.Y, 180, 900);
            _placements[pane.Id] = resize with { Width = w, Height = h };
            if (resize.Dock == PaletteDock.Floating) { var actual = _placements[pane.Id].Constrain(ActualWidth, ActualHeight); pane.Width = actual.Width; pane.Height = actual.Height; }
            else ColumnDefinitions[resize.Dock == PaletteDock.Left ? 1 : 3].Width = new GridLength(Math.Min(w, ActualWidth * .38));
            e.Handled = true;
        };
        pane.SizeGrip.PointerReleased += (_, e) => { if (resize == null) return; resize = null; pane.SizeGrip.ReleasePointerCapture(e.Pointer); Rebuild(); LayoutChanged?.Invoke(); e.Handled = true; };
        pane.SizeGrip.PointerCaptureLost += (_, _) => { if (resize == null) return; _placements[pane.Id] = resize; resize = null; Rebuild(); };
    }
}
