using System.Globalization;
using CadSpace.Engine;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Selection-aware layer and property selectors. Mixed values never overwrite other properties.</summary>
public sealed class CadPropertySelectors : UserControl
{
    private readonly ComboBox _layers = Picker("ribbon.layer", "Layer");
    private readonly ComboBox _colors = Picker("ribbon.color", "Color");
    private readonly ComboBox _types = Picker("ribbon.linetype", "Linetype");
    private readonly ComboBox _weights = Picker("ribbon.lineweight", "Lineweight");
    private CadSession? _session; private bool _building;
    private Drawing? _drawing; private long _selection = -1; private string _current = "";
    private static readonly string[] Colors = ["ByLayer", "ByBlock", "Red", "Yellow", "Green", "Cyan", "Blue", "Magenta", "White"];
    private static readonly double[] Weights = [-1, 0, .05, .09, .13, .18, .25, .35, .50, .70, 1, 1.40, 2.11];
    private readonly bool _layerOnly;
    public event Action<string>? Message;
    public event Action<string>? CommandRequested;
    public CadPropertySelectors(bool layerOnly = false)
    {
        _layerOnly = layerOnly; var body = new StackPanel { Spacing = 2, Width = layerOnly ? 204 : 174 };
        if (layerOnly)
        {
            var manager = CadUi.TextButton("Layer Properties", () => CommandRequested?.Invoke("LAYER"), "ribbon.layerManager");
            manager.HorizontalAlignment = HorizontalAlignment.Stretch; body.Children.Add(manager); body.Children.Add(_layers);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            actions.Children.Add(CadUi.IconButton("LAYER", "Create layer", () => CommandRequested?.Invoke("LAYER"), "ribbon.newLayer", 22));
            actions.Children.Add(CadUi.IconButton("LIGHT", "Toggle current layer visibility", () => Change(() => EditLayer(l => l with { Visible = !l.Visible })), "ribbon.layerVisibility", 22));
            actions.Children.Add(CadUi.IconButton("LOCK", "Toggle current layer lock", () => Change(() => EditLayer(l => l with { Locked = !l.Locked })), "ribbon.layerLock", 22));
            actions.Children.Add(CadTheme.Text("Current / selected layer", 10, CadTheme.Muted)); body.Children.Add(actions);
        }
        else { body.Children.Add(_colors); body.Children.Add(_types); body.Children.Add(_weights); }
        _colors.ItemsSource = Colors; _weights.ItemsSource = Weights.Select(WeightName).ToArray();
        _layers.SelectionChanged += (_, _) => { if (!_building && _layers.SelectedItem is string n) Change(() => _session!.AssignLayer(n)); };
        _types.SelectionChanged += (_, _) => { if (!_building && _types.SelectedItem is string n) Change(() => { if (_session!.Selection.Count == 0) _session.SetCurrentLinetype(n); else _session.SetSelectedLinetype(n); }); };
        _colors.SelectionChanged += (_, _) => { if (!_building && _colors.SelectedIndex >= 0 && _colors.SelectedIndex < Colors.Length) Change(() => _session!.AssignColor(_colors.SelectedIndex == 0 ? 256 : _colors.SelectedIndex - 1)); };
        _weights.SelectionChanged += (_, _) => { if (!_building && _weights.SelectedIndex >= 0 && _weights.SelectedIndex < Weights.Length) Change(() => _session!.AssignWeight(Weights[_weights.SelectedIndex])); };
        Content = body;
    }
    private static ComboBox Picker(string id, string name) => CadUi.Identify(new ComboBox {
        FontSize = 11, MinHeight = 23, Height = 23, Padding = new Thickness(6, 0, 26, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
        PlaceholderText = "*Varies*", Background = CadTheme.Brush(0xFF252E3B), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(0)
    }, id, name);
    private static string WeightName(double n) => n < 0 ? "ByLayer" : n.ToString("0.00", CultureInfo.InvariantCulture) + " mm";
    private void EditLayer(Func<Layer, Layer> update)
    {
        var name = _session!.CurrentLayer; _session.UpdateLayer(name, update(_session.Document.Drawing.Layers[name]));
    }
    private void Change(Action action)
    {
        if (_session == null) return;
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { Message?.Invoke(e.Message); }
        _drawing = null; Refresh();
    }
    public void Bind(CadSession session)
    {
        if (_session != null) _session.Changed -= Refresh;
        _session = session; session.Changed += Refresh; _drawing = null; Refresh();
    }
    public void Refresh()
    {
        if (_building || _session == null) return; var s = _session; var d = s.Document.Drawing;
        var current = $"{s.CurrentLayer}|{s.CurrentLinetype}|{s.CurrentColorIndex}|{s.CurrentTrueColor}|{s.CurrentLineWeight}";
        if (ReferenceEquals(d, _drawing) && _selection == s.SelectionRevision && current == _current) return;
        _building = true;
        try
        {
            if (!ReferenceEquals(d.Layers, _drawing?.Layers)) _layers.ItemsSource = d.Layers.Keys.Order().ToArray();
            if (!ReferenceEquals(d.Linetypes, _drawing?.Linetypes)) _types.ItemsSource = new[] { "BYLAYER", "BYBLOCK" }.Concat(d.Linetypes.Keys.Order()).ToArray();
            var entities = s.SelectedEntities();
            string? Common(Func<Entity, string> get, string fallback) => entities.Length == 0 ? fallback : entities.All(e => get(e) == get(entities[0])) ? get(entities[0]) : null;
            _layers.SelectedItem = Common(e => e.Layer, s.CurrentLayer);
            _types.SelectedItem = Common(e => e.Linetype, s.CurrentLinetype);
            var color = Common(e => ColorName(e.ColorIndex, e.TrueColor), ColorName(s.CurrentColorIndex, s.CurrentTrueColor));
            _colors.ItemsSource = color != null && !Colors.Contains(color) ? Colors.Append(color).ToArray() : Colors;
            _colors.SelectedItem = color;
            var weight = Common(e => WeightName(e.LineWeight), WeightName(s.CurrentLineWeight));
            var weights = Weights.Select(WeightName).ToArray();
            _weights.ItemsSource = weight != null && !weights.Contains(weight) ? weights.Append(weight).ToArray() : weights;
            _weights.SelectedItem = weight;
            var editable = !entities.Any(e => e is OpaqueEntity || d.LayerFor(e).Locked);
            _layers.IsEnabled = _colors.IsEnabled = _types.IsEnabled = _weights.IsEnabled = editable;
            _drawing = d; _selection = s.SelectionRevision; _current = current;
        }
        finally { _building = false; }
    }
    private static string ColorName(int aci, uint? rgb) => rgb != null ? "#" + (rgb.Value & 0xFFFFFF).ToString("X6") : aci == 256 ? "ByLayer" : aci is >= 0 and <= 7 ? Colors[aci + 1] : "ACI " + aci;
}
