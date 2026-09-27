using System.Globalization;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace CadSpace.Controls;

/// <summary>Virtualized Layer Properties Manager. All changes use undoable headless editing operations.</summary>
public sealed partial class CadLayerManager : UserControl
{
    private CadSession? _session;
    private object? _lastLayers, _lastTypes;
    private string? _lastCurrent;
    private bool _refreshing;
    public CadLayerManager() => InitializeComponent();
    public void Bind(CadSession session) { Unbind(); _session = session; session.Changed += Refresh; Refresh(); }
    public void Unbind() { if (_session != null) _session.Changed -= Refresh; _session = null; _lastLayers = _lastTypes = null; }
    private void Refresh()
    {
        if (_session == null || _refreshing) return;
        var d = _session.Document.Drawing;
        if (ReferenceEquals(_lastLayers, d.Layers) && ReferenceEquals(_lastTypes, d.Linetypes) && _lastCurrent == _session.CurrentLayer) return;
        _lastLayers = d.Layers; _lastTypes = d.Linetypes; _lastCurrent = _session.CurrentLayer;
        Filter();
    }
    private void Filter()
    {
        if (_session == null || _refreshing) return;
        _refreshing = true;
        try
        {
            var name = (Layers.SelectedItem as LayerRow)?.Name ?? _session.CurrentLayer;
            var rows = _session.Document.Drawing.Layers.Values.Where(l => l.Name.Contains(Search.Text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Select(l => new LayerRow(l, _session.CurrentLayer)).ToArray();
            Layers.ItemsSource = rows; Layers.SelectedItem = rows.FirstOrDefault(l => l.Name == name) ?? rows.FirstOrDefault();
            LineType.ItemsSource = _session.Document.Drawing.Linetypes.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            Status.Text = $"Current layer: {_session.CurrentLayer}   •   {rows.Length} shown / {_session.Document.Drawing.Layers.Count} total   •   Changes support Undo";
        }
        finally { _refreshing = false; }
        EditSelection();
    }
    private void EditSelection()
    {
        if (Layers.SelectedItem is not LayerRow row) return;
        var l = row.Layer; LayerName.Text = l.Name; ColorValue.Text = row.ColorText;
        WeightValue.Text = l.LineWeight.ToString(CultureInfo.InvariantCulture); LineType.SelectedItem = l.Linetype;
        Visible.IsChecked = l.Visible; Locked.IsChecked = l.Locked;
    }
    private void Execute(Action<CadSession> action)
    {
        if (_session == null) return;
        try { action(_session); Refresh(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { Status.Text = error.Message; }
    }
    private void OnSearch(object sender, TextChangedEventArgs args) => Filter();
    private void OnSelected(object sender, SelectionChangedEventArgs args) { if (!_refreshing) EditSelection(); }
    private void OnAdd(object sender, RoutedEventArgs args) => Execute(s => { s.AddLayer(NewName.Text.Trim()); NewName.Text = ""; });
    private void OnCurrent(object sender, RoutedEventArgs args) => Execute(s => { if (Layers.SelectedItem is LayerRow row) { s.CurrentLayer = row.Name; s.Invalidate(); } });
    private void OnDelete(object sender, RoutedEventArgs args) => Execute(s => { if (Layers.SelectedItem is LayerRow row) s.DeleteLayer(row.Name); });
    private void OnApply(object sender, RoutedEventArgs args) => Execute(s =>
    {
        if (Layers.SelectedItem is not LayerRow row) return;
        var colorText = ColorValue.Text.Trim().TrimStart('#');
        if (colorText.Length != 6 || !uint.TryParse(colorText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color)) throw new ArgumentException("Use a six-digit RGB color.");
        if (!GeometryMath.Number(WeightValue.Text, out var weight) || weight < 0) throw new ArgumentException("Weight must be a finite nonnegative number.");
        s.UpdateLayer(row.Name, row.Layer with { Name = LayerName.Text.Trim(), Color = 0xFF000000u | color, LineWeight = weight, Linetype = (string?)LineType.SelectedItem ?? "CONTINUOUS", Visible = Visible.IsChecked == true, Locked = Locked.IsChecked == true });
    });
    [Bindable]
    public sealed class LayerRow(Layer layer, string current)
    {
        public Layer Layer { get; } = layer;
        public string Name => Layer.Name;
        public string Current => Name.Equals(current, StringComparison.OrdinalIgnoreCase) ? "✓" : "";
        public string VisibilityText => Layer.Visible ? "On" : "Off";
        public string LockText => Layer.Locked ? "Lock" : "—";
        public string Linetype => Layer.Linetype;
        public string ColorText => (Layer.Color & 0xFFFFFF).ToString("X6");
        public string Weight => Layer.LineWeight.ToString("0.##", CultureInfo.InvariantCulture);
        public SolidColorBrush Swatch { get; } = CadTheme.Brush(layer.Color);
    }
}
