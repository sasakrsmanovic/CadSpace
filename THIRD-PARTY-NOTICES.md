# Third-party notices

CadSpace source is MIT licensed. Its major direct dependencies are independently licensed:

| Dependency | Use | License / source |
| --- | --- | --- |
| Uno Platform | Cross-platform WinUI-compatible UI, hosting and graphics controls | Apache-2.0; https://github.com/unoplatform/uno |
| SkiaSharp | Vector/text drawing and Skia bindings | MIT; https://github.com/mono/SkiaSharp |
| Skia | Native rasterization/GPU implementation used by SkiaSharp | BSD-style; https://skia.googlesource.com/skia/ |
| Silk.NET | OpenGL bindings | MIT; https://github.com/dotnet/Silk.NET |
| .NET | Runtime and base class libraries | MIT and component notices; https://github.com/dotnet/runtime |

Native platform assets can include additional permissively licensed components such as ANGLE. Redistributors must preserve the notices supplied with the actual NuGet/runtime packages they distribute. Inspect the resolved dependency graph for a complete platform-specific inventory; this table is not an exhaustive software bill of materials.

CadSpace contains original code and original vector command artwork. Autodesk, AutoCAD, DXF and related names belong to their respective owners. CadSpace is independent, is not endorsed by Autodesk, and does not bundle Autodesk code, icons, fonts, ACIS, RealDWG, or other proprietary components.
