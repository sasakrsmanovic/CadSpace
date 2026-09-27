using CadSpace.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace CadSpace.Controls;

/// <summary>CAD console with bounded history, clickable completion, Tab acceptance and shared engine prompts.</summary>
public sealed class CadCommandLine : UserControl
{
    private readonly TextBlock _history=CadTheme.Text("CadSpace ready. Type a command; Tab accepts a suggestion.",11,CadTheme.Muted);
    private readonly StackPanel _suggestions=new(){Orientation=Orientation.Horizontal,Spacing=4,Margin=new Thickness(12,4,0,4),Visibility=Visibility.Collapsed};
    private readonly List<string> _messages=new(), _commands=new();
    private readonly Grid _root=CadTheme.Grid(42,32);
    private readonly ScrollViewer _historyScroll;
    private CommandEngine? _engine;
    private int _historyIndex;
    private bool _expanded;
    public event Action<bool>? HistoryExpanded;
    public TextBox Input {get;}=new(){PlaceholderText="Type a command",FontFamily=new FontFamily("Consolas"),FontSize=12,BorderThickness=new Thickness(0),Background=CadTheme.Brush(0xFF20262E),Foreground=CadTheme.Brush(CadTheme.TextColor),Padding=new Thickness(8,5,8,5),MinHeight=30};
    public CadCommandLine()
    {
        _root.Background=CadTheme.Brush(0xFF252C35);
        _history.FontFamily=new FontFamily("Consolas");_history.Margin=new Thickness(14,4,12,2);_history.TextWrapping=TextWrapping.Wrap;_history.MaxLines=2;
        _historyScroll=new ScrollViewer{Content=_history,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        CadTheme.At(_root,_historyScroll,0);CadTheme.At(_root,_suggestions,0);
        var entry=new Grid();entry.ColumnDefinitions.Add(new(){Width=new GridLength(88)});entry.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        entry.Children.Add(CadTheme.Button("Command ↑",ToggleHistory,88));Grid.SetColumn(Input,1);entry.Children.Add(Input);CadTheme.At(_root,entry,1);
        Input.KeyDown+=OnKeyDown;Input.TextChanged+=(_,_)=>Complete();Content=_root;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Input,"CAD command input");
    }
    public void Bind(CommandEngine engine)
    {
        if(_engine!=null){_engine.Message-=AddMessage;_engine.Changed-=Update;}
        _engine=engine;engine.Message+=AddMessage;engine.Changed+=Update;Input.Text="";Update();
    }
    public void ToggleHistory()
    {
        _expanded=!_expanded;_root.RowDefinitions[0].Height=new GridLength(_expanded?170:42);_history.MaxLines=_expanded?0:2;
        RefreshHistory();HistoryExpanded?.Invoke(_expanded);Complete();
    }
    public void AddMessage(string message)
    {
        _messages.Add(message);if(_messages.Count>200)_messages.RemoveAt(0);RefreshHistory();
    }
    private void RefreshHistory(){_history.Text=string.Join("\n",_expanded?_messages:_messages.TakeLast(2));_historyScroll.ChangeView(null,double.MaxValue,null);}
    public void FocusInput()=>Input.Focus(FocusState.Programmatic);
    private void Update(){Input.PlaceholderText=_engine?.Prompt??"Type a command";Complete();}
    private void Complete()
    {
        var candidates=_engine?.IsActive==false?CommandCatalog.Suggest(Input.Text,5):[];
        _suggestions.Children.Clear();
        foreach(var item in candidates)
        {
            var button=CadTheme.Button(item.Name+"  "+item.Alias,()=>{Input.Text=item.Name;Input.SelectionStart=Input.Text.Length;FocusInput();});
            button.FontSize=11;ToolTipService.SetToolTip(button,item.Description);_suggestions.Children.Add(button);
        }
        var show=candidates.Count>0 && !_expanded;
        _suggestions.Visibility=show?Visibility.Visible:Visibility.Collapsed;_historyScroll.Visibility=show?Visibility.Collapsed:Visibility.Visible;
    }
    private void OnKeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(_engine==null)return;
        if(e.Key==VirtualKey.Tab && !_engine.IsActive && CommandCatalog.Suggest(Input.Text,1).FirstOrDefault() is { } completion)
        {Input.Text=completion.Name;Input.SelectionStart=Input.Text.Length;e.Handled=true;}
        else if(e.Key==VirtualKey.Enter)
        {
            var text=Input.Text;Input.Text="";
            if(!string.IsNullOrWhiteSpace(text)){_commands.Add(text);if(_commands.Count>200)_commands.RemoveAt(0);_historyIndex=_commands.Count;}
            _engine.Submit(text);e.Handled=true;
        }
        else if(e.Key==VirtualKey.Escape){_engine.Cancel();Input.Text="";e.Handled=true;}
        else if(e.Key is VirtualKey.Up or VirtualKey.Down && _commands.Count>0)
        {_historyIndex=Math.Clamp(_historyIndex+(e.Key==VirtualKey.Up?-1:1),0,_commands.Count);Input.Text=_historyIndex<_commands.Count?_commands[_historyIndex]:"";Input.SelectionStart=Input.Text.Length;e.Handled=true;}
    }
}
