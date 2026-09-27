global using GLCanvasElement = CadSpace.Controls.PortableGlCanvasElement;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.UI.Xaml;

namespace CadSpace.Controls;

/// <summary>
/// Uno 6.7.135 assumes its JavaScript shim translates BGRA readback, but the browser
/// can resolve the native GLES entry point instead. Select the framework's existing
/// RGBA + red/blue-swap path, which works with either resolver. Desktop is unchanged.
/// This version-pinned adapter fails explicitly when its upstream contract changes.
/// </summary>
public abstract class PortableGlCanvasElement : Uno.WinUI.Graphics3DGL.GLCanvasElement
{
    protected PortableGlCanvasElement(Func<Window>? window) : base(window)
    {
        Loaded += (_, _) => EnablePortableReadback();
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(Uno.WinUI.Graphics3DGL.GLCanvasElement))]
    private void EnablePortableReadback()
    {
        if (!OperatingSystem.IsBrowser()) return;
        var field = typeof(Uno.WinUI.Graphics3DGL.GLCanvasElement).GetField(
            "_readbackAsRgbaWithSwap", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.FieldType != typeof(bool))
            throw new NotSupportedException("The Uno GPU readback contract changed. Requalify the RGBA adapter before upgrading Uno.");
        field.SetValue(this, true);
        Console.WriteLine("CADSPACE_READBACK: RGBA with BGRA conversion");
    }
}
