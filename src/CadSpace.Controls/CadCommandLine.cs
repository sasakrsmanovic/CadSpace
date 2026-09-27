using CadSpace.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace CadSpace.Controls;

public sealed class CadCommandLine : UserControl
{
    private readonly TextBlock _history = CadTheme.Text("CadSpace is ready. Type HELP for commands.", 11, CadTheme.Muted);
    private readonly List<string> _messages = new();
    private readonly List<string> _commands = new();
    private CommandEngine? _engine;
    private int _historyIndex;
    public TextBox Input { get; } = new() { PlaceholderText = "Type a command", FontFamily = new FontFamily("Consolas"), FontSize = 12, BorderThickness = new Thickness(0), Background = CadTheme.Brush(0xFF20262E), Foreground = CadTheme.Brush(CadTheme.TextColor), Padding = new Thickness(8, 5, 8, 5), MinHeight = 30 };
    public CadCommandLine()
    {
        var root = CadTheme.Grid(42, 32); root.Background = CadTheme.Brush(0xFF252C35);
        _history.FontFamily = new FontFamily("Consolas"); _history.Margin = new Thickness(14, 4, 12, 2); _history.TextWrapping = TextWrapping.Wrap; _history.MaxLines = 2; CadTheme.At(root, _history, 0);
        var entry = new Grid(); entry.ColumnDefinitions.Add(new() { Width = new GridLength(88) }); entry.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var label = CadTheme.Text("Command:", 11); label.Margin = new Thickness(14, 0, 0, 0); entry.Children.Add(label); Grid.SetColumn(Input, 1); entry.Children.Add(Input); CadTheme.At(root, entry, 1);
        Input.KeyDown += OnKeyDown; Content = root;
    }
    public void Bind(CommandEngine engine)
    {
        if (_engine != null) { _engine.Message -= AddMessage; _engine.Changed -= Update; }
        _engine = engine; _engine.Message += AddMessage; _engine.Changed += Update; Update();
    }
    public void AddMessage(string message)
    {
        _messages.Add(message); if (_messages.Count > 200) _messages.RemoveAt(0);
        _history.Text = string.Join("\n", _messages.TakeLast(2));
    }
    public void FocusInput() => Input.Focus(FocusState.Programmatic);
    private void Update() => Input.PlaceholderText = _engine?.Prompt ?? "Type a command";
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_engine == null) return;
        if (e.Key == VirtualKey.Enter)
        {
            var text = Input.Text; Input.Text = "";
            if (!string.IsNullOrWhiteSpace(text)) { _commands.Add(text); _historyIndex = _commands.Count; }
            _engine.Submit(text); e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape) { _engine.Cancel(); Input.Text = ""; e.Handled = true; }
        else if (e.Key is VirtualKey.Up or VirtualKey.Down && _commands.Count > 0)
        {
            _historyIndex = Math.Clamp(_historyIndex + (e.Key == VirtualKey.Up ? -1 : 1), 0, _commands.Count);
            Input.Text = _historyIndex < _commands.Count ? _commands[_historyIndex] : ""; Input.SelectionStart = Input.Text.Length; e.Handled = true;
        }
    }
}
