using CadSpace.Engine;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Model/paper-space root tabs; no implied embedded paper-space viewport or plotting engine.</summary>
public sealed class CadLayoutTabs : UserControl
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private CadSession? _session; private Drawing? _drawing; private string _active = ""; private bool _refreshing;
    public event Action? LayoutActivated;
    public event Action<string>? RenameRequested;
    public event Action<string>? Message;
    public CadLayoutTabs() => Content = new ScrollViewer { Content = _tabs, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    public void Bind(CadSession session)
    { if (_session != null) _session.Changed -= Refresh; _session = session; _drawing = null; session.Changed += Refresh; Refresh(); }
    public void NewLayout() { if (_session == null) return; Try(() => { _session.AddLayout(); LayoutActivated?.Invoke(); }); }
    public void DeleteLayout(string? name = null) { if (_session != null) Try(() => { _session.DeleteEmptyLayout(name ?? _session.ActiveLayout); LayoutActivated?.Invoke(); }); }
    private void Try(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { Message?.Invoke(e.Message); } }
    private void Refresh()
    {
        if (_session == null || _refreshing) return;
        if (_drawing == _session.Document.Drawing && _active == _session.ActiveLayout) return;
        _refreshing = true;
        try
        {
            var s = _session; var names = s.AvailableLayouts.ToArray();
            if (!names.Contains(s.ActiveLayout, StringComparer.OrdinalIgnoreCase)) s.ActiveLayout = "Model";
            _tabs.Children.Clear();
            foreach (var name in names)
            {
                var button = CadUi.TextButton(name, () => { s.ActiveLayout = name; LayoutActivated?.Invoke(); }, "layout." + name);
                button.Background = CadTheme.Brush(s.ActiveLayout == name ? 0xFF50657A : 0xFF273241);
                var menu = new MenuFlyout();
                var rename = new MenuFlyoutItem { Text = "Rename layout…", IsEnabled = name != "Model" }; rename.Click += (_, _) => RenameRequested?.Invoke(name); menu.Items.Add(rename);
                var delete = new MenuFlyoutItem { Text = "Delete empty layout", IsEnabled = name != "Model" }; delete.Click += (_, _) => DeleteLayout(name); menu.Items.Add(delete); button.ContextFlyout = menu;
                _tabs.Children.Add(button);
            }
            _tabs.Children.Add(CadUi.TextButton("+", NewLayout, "layout.new")); _drawing = s.Document.Drawing; _active = s.ActiveLayout;
        }
        finally { _refreshing = false; }
    }
}
