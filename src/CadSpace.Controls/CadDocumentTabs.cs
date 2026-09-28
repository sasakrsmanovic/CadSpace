using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Open-document tabs with overflow, activation, dirty indicators, close and reorder requests.</summary>
public sealed class CadDocumentTabs : UserControl
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly ScrollViewer _scroll;
    private readonly Button _list, _previous, _next;
    private (object Key, string Name, bool Dirty)[] _documents = [];
    private object? _active;
    public IReadOnlyList<(object Key, string Name, bool Dirty)> Documents => _documents;
    public event Action<object>? ActivateRequested;
    public event Action<object>? CloseRequested;
    public event Action<object, int>? MoveRequested;
    public event Action? NewRequested;
    public event Action? DocumentsChanged;
    public CadDocumentTabs()
    {
        var root = new Grid { Background = CadTheme.Brush(0xFF1D2632) };
        foreach (var size in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) root.ColumnDefinitions.Add(new() { Width = size });
        _list = CadUi.TextButton("☰", () => { }, "documents.list"); root.Children.Add(_list);
        _scroll = new ScrollViewer { Content = _tabs, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _previous = CadUi.TextButton("‹", () => _scroll.ChangeView(Math.Max(0, _scroll.HorizontalOffset - 200), null, null), "documents.previous"); Grid.SetColumn(_previous, 1); root.Children.Add(_previous);
        Grid.SetColumn(_scroll, 2); root.Children.Add(_scroll);
        _next = CadUi.TextButton("›", () => _scroll.ChangeView(_scroll.HorizontalOffset + 200, null, null), "documents.next"); Grid.SetColumn(_next, 3); root.Children.Add(_next);
        var add = CadUi.TextButton("+", () => NewRequested?.Invoke(), "documents.new"); add.Width = 33; Grid.SetColumn(add, 4); root.Children.Add(add);
        _scroll.SizeChanged += (_, _) => Overflow(); _scroll.ViewChanged += (_, _) => Overflow(); _tabs.SizeChanged += (_, _) => Overflow(); Content = root;
    }
    public void RequestActivation(object key) => ActivateRequested?.Invoke(key);
    private void Overflow() => _previous.Visibility = _next.Visibility = _scroll.ExtentWidth > _scroll.ViewportWidth + 2 ? Visibility.Visible : Visibility.Collapsed;
    public void SetDocuments(IEnumerable<(object Key, string Name, bool Dirty)> documents, object active)
    {
        var next = documents.ToArray();
        if (Equals(_active, active) && _documents.SequenceEqual(next)) return;
        _active = active; _documents = next; _tabs.Children.Clear(); var menu = new MenuFlyout();
        for (var i = 0; i < _documents.Length; i++)
        {
            var d = _documents[i]; var index = i; var row = new StackPanel { Orientation = Orientation.Horizontal };
            var button = CadUi.TextButton(d.Name + (d.Dirty ? " *" : ""), () => ActivateRequested?.Invoke(d.Key), "documents.activate." + i);
            button.MaxWidth = 240; button.Height = 29; button.Padding = new Thickness(15, 3, 9, 3); button.Background = CadTheme.Brush(Equals(d.Key, active) ? 0xFF455366 : 0xFF2A3544); ToolTipService.SetToolTip(button, d.Name);
            var close = CadUi.TextButton("×", () => CloseRequested?.Invoke(d.Key), "documents.close." + i); close.Height = 29; close.Width = 25; close.Background = button.Background;
            row.Children.Add(button); row.Children.Add(close); _tabs.Children.Add(row);
            var context = new MenuFlyout();
            foreach (var (name, move) in new[] { ("Move tab left", -1), ("Move tab right", 1) })
            { var item = new MenuFlyoutItem { Text = name, IsEnabled = index + move >= 0 && index + move < _documents.Length }; item.Click += (_, _) => MoveRequested?.Invoke(d.Key, index + move); context.Items.Add(item); }
            var closeItem = new MenuFlyoutItem { Text = "Close drawing" }; closeItem.Click += (_, _) => CloseRequested?.Invoke(d.Key); context.Items.Add(closeItem); button.ContextFlyout = context;
            var choose = CadUi.Identify(new MenuFlyoutItem { Text = d.Name + (d.Dirty ? " *" : "") }, "documents.choose." + i, d.Name); choose.Click += (_, _) => ActivateRequested?.Invoke(d.Key); menu.Items.Add(choose);
        }
        _list.Flyout = menu; DocumentsChanged?.Invoke();
    }
}
