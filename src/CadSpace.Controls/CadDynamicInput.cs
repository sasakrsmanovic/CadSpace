using CadSpace.Engine;
using CadSpace.Geometry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace CadSpace.Controls;

/// <summary>Cursor-adjacent command prompt and editable input, sharing the command engine rather than duplicating geometry logic.</summary>
public sealed class CadDynamicInput : UserControl
{
    private readonly TextBlock _prompt=CadTheme.Text("",11),_measurement=CadTheme.Text("",10,CadTheme.Muted);
    private readonly TextBox _input=new(){PlaceholderText="Coordinate or value…",FontSize=11,MinHeight=26,Padding=new Thickness(5,2,5,2),BorderThickness=new Thickness(0),Background=CadTheme.Brush(0xFF202731),Foreground=CadTheme.Brush(CadTheme.TextColor)};
    private CommandEngine? _commands;
    public CadDynamicInput()
    {
        Width=260;HorizontalAlignment=HorizontalAlignment.Left;VerticalAlignment=VerticalAlignment.Top;Visibility=Visibility.Collapsed;
        var body=new StackPanel{Spacing=4};_prompt.TextTrimming=TextTrimming.CharacterEllipsis;body.Children.Add(_prompt);body.Children.Add(_input);body.Children.Add(_measurement);
        Content=new Border{Child=body,Padding=new Thickness(8),Background=CadTheme.Brush(0xF0303844),BorderBrush=CadTheme.Brush(0xFF60778C),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(3)};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_input,"Dynamic CAD input");
        _input.KeyDown+=(_,e)=>
        {
            if(e.Key==VirtualKey.Enter){var text=_input.Text;_input.Text="";_commands?.Submit(text);e.Handled=true;}
            else if(e.Key==VirtualKey.Escape){_input.Text="";_commands?.Cancel();e.Handled=true;}
        };
    }
    public void Bind(CommandEngine commands)
    {
        if(_commands!=null)_commands.Changed-=Refresh;_commands=commands;commands.Changed+=Refresh;Refresh();
    }
    public void Refresh()
    {
        Visibility=_commands?.IsActive==true && _commands.Session.DynamicInput?Visibility.Visible:Visibility.Collapsed;
        _prompt.Text=_commands?.Prompt??"";
    }
    public void Position(double x,double y,double width,double height,Vec3 world)
    {
        Refresh();Margin=new Thickness(Math.Clamp(x+30,0,Math.Max(0,width-Width-8)),Math.Clamp(y+24,0,Math.Max(0,height-100)),0,0);
        var delta=world-(_commands?.ReferencePoint??world);
        _measurement.Text=_commands?.ReferencePoint!=null?$"Δ {delta.Length:0.###}    ∠ {GeometryMath.Angle(delta):0.##}°":world.ToString();
    }
}
