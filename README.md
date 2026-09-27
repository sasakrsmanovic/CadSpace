# CadSpace

### Modular CAD for desktop and the browser

[![Build, test and deploy](https://github.com/wieslawsoltes/CadSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/CadSpace/actions/workflows/build.yml)
[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**[Open the browser app](https://wieslawsoltes.github.io/CadSpace/)** · **[Commands](docs/COMMANDS.md)** · **[Coverage](docs/FEATURES.md)** · **[Architecture](docs/ARCHITECTURE.md)** · **[Performance](docs/PERFORMANCE.md)**

CadSpace is an independent C# CAD workspace built with Uno Platform, Skia and OpenGL/WebGL. It combines double-precision drafting, layers and blocks, rational splines, mesh modeling, ASCII/binary DXF exchange, and reusable desktop/browser controls.

> **Development preview—not full AutoCAD parity.** The tools below are implemented within documented boundaries. Mesh modeling is not an analytic ACIS/B-rep kernel; arbitrary DXF editing is not universally lossless. Preserve original drawings, review export warnings, and save a native project before closing or refreshing the application. Automatic crash recovery is not implemented.

## Workspace

The startup document is an editable studio floor plan. **3D example** opens a separate model study. The custom Uno workspace includes a dense ribbon with stacked command groups, document tabs, command completion/history, cursor-adjacent dynamic input, properties/layers/blocks, model/layout selection, a resizable or hideable Properties palette, and view navigation.

| Area | Available workflows |
| --- | --- |
| Drafting | Lines, 2D/3D polylines, rectangles, circles, arcs, ellipses, control-point splines, points, plain text, aligned dimensions and basic hatching |
| Editing | Move/copy/rotate/scale/mirror; 3D rotation, plane reflection and rigid alignment; offset, arrays, supported explode including bulged segments; line trim/extend/fillet/chamfer/join/break; undo/redo |
| Precision | Absolute/relative/polar coordinates; indexed nested-block/OCS anchors; line intersection/perpendicular and circle/arc tangent snaps; grid, ortho and polar guidance |
| Organization | Layers, visibility, locking, block definitions/nesting/insertion, separate model and paper-space entity roots |
| Mesh modeling | Box/cylinder/cone/sphere, extrusion, revolved surfaces, capped matching-profile loft, parallel-transport sweep, bounded closed-mesh union/subtraction/intersection |
| 3D viewing | Depth-tested shading, feature edges, face/edge/point picking, highlights, world-plane plain text, perspective/orthographic projection, uncapped clipping, pan/orbit and anchored zoom |
| Files | ASCII and binary DXF, supported code pages and typed geometry, native indexed MESH/SPLINE/HATCH export, original-record preservation, versioned native projects |

The registry contains **55 command workflows**. See [Commands](docs/COMMANDS.md) for exact inputs and restrictions. A familiar name does not imply every AutoCAD command option is implemented.

## Performance work

Immutable scene roots reuse unchanged tessellation. Lazy bounding-volume hierarchies accelerate click picking, 3D ray picking, snap searches and visible-path culling. Selection highlights use a separate GPU attribute stream: selecting an object does not regenerate its geometry buffer. Packed geometry uploads avoid a temporary array per vertex; redraw requests are coalesced and timing-label updates are throttled.

A reproducible 100,000-line benchmark compares 150 warmed indexed picks against a linear scan, verifies identical results, and records both timings in CI. It is **not a whole-application FPS, cold-start, GPU or million-entity scalability claim**. See [methodology and remaining costs](docs/PERFORMANCE.md).

## Files: Save versus Export

**Open** accepts `.cadspace` and ASCII or binary `.dxf`. **Save** writes the native project. **Export DXF** and **Binary DXF** write interchange copies without clearing native dirty state.

Typed DXF support includes OCS/affine circles, arcs, polylines and ellipses; legacy 2D/3D POLYLINE sequences; polyface/polygon meshes; rational SPLINE; MESH; SOLID/TRACE/3DFACE; supported HATCH loops, edge lists, islands and patterns; and display children for supported attributed/array INSERTs. Native MESH, SPLINE and HATCH output retains the modeled topology/control data. Model/paper-space roots and supported ownership/layout links are reconstructed.

Untouched imported records preserve their original groups. An unchanged byte import can return its original bytes, including binary/legacy encoding. Native version 2 retains expanded geometry and DXF provenance; provenance is checked against reparsed source data before raw-record reuse. Basic version 1 projects remain readable.

Unknown entities remain opaque, not invented geometry. Modified compound objects, generated dimensions, sampled boundaries and unmodeled metadata can be lossy or unsupported; review the reports. There is no DWG or ACIS decoder. Native AutoCAD open/AUDIT/save/reopen qualification has not been performed.

## Try a drawing

Enter one line at a time in the command box:

```text
RECTANG
0,0
240,140
CIRCLE
120,70
30
ZOOM
```

Select a closed profile before `EXTRUDE`. For `LOFT`, select closed planar profiles with matching sampled vertex counts in drawing order. For `SWEEP`, select one closed profile and one open polyline. Mesh Booleans require closed, consistently oriented operands. `SUBTRACT` uses the first selected mesh **in drawing order**, not click order, as its base.

Click a 3D face to select it; drag to orbit. Middle-drag pans, and the wheel zooms around the pointer. Choose Wireframe, HiddenLine, Shaded or ShadedEdges from the view selector. Clipping removes a display half-space without altering the mesh or generating caps.

| Input | Action |
| --- | --- |
| Tab in an idle command box | Accept the highest-ranked command completion |
| `@20,10` / `@50<30` | Relative Cartesian / relative polar point |
| Enter / Escape; Up / Down | Submit or finish / cancel; command history |
| Ctrl+N / Ctrl+O / Ctrl+S | New / open / native save |
| Ctrl+Shift+E | ASCII DXF export |
| Ctrl+Z / Ctrl+Y / Ctrl+A | Undo / redo / select visible entities, respecting active text editing |
| F2 / Ctrl+1 / F12 | Expanded command history / Properties palette / dynamic input |
| F3 / F7 / F8 / F9 / F10 | Object snap / grid / ortho / grid snap / polar |

Use **Snap options** to enable individual modes, including perpendicular and tangent. Browser-reserved shortcuts may take precedence; primary operations also have visible controls.

## Build and run

Pinned dependencies: **Uno.Sdk 6.7.30**, **Uno graphics 6.7.135**, **SkiaSharp 3.119.2**, **Silk.NET.OpenGL 2.23.0**, **.NET 10**. The SDK pin permits newer stable .NET 10 feature bands.

```sh
git clone https://github.com/wieslawsoltes/CadSpace.git
cd CadSpace
dotnet workload install wasm-tools

# Desktop: Windows, macOS or Linux/X11
dotnet run --project src/CadSpace.App -f net10.0-desktop -p:CadSpaceDesktopOnly=true

# Browser development host
dotnet run --project src/CadSpace.App -f net10.0-browserwasm

# GitHub Pages distribution
dotnet publish src/CadSpace.App -c Release -f net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/CadSpace/
```

A compatible OpenGL/GLES/WebGL context is required for 3D. Linux also needs the native Uno/Skia dependencies and a display. Release browser builds enable IL/XAML resource trimming and the jiterpreter; `-p:CadSpaceUntrimmed=true` disables the trimming configuration for diagnosis. Test the published build, not only Debug, when changing reflective APIs or upgrading Uno.

## Reusable libraries

| Package | Responsibility |
| --- | --- |
| `CadSpace.Geometry` | Double vectors/transforms, OCS, rays/planes, intersections, triangulation and spatial index |
| `CadSpace.Model` | Immutable documents, blocks, curves/hatches, scene construction/cache, mesh generation/Booleans/surfaces |
| `CadSpace.Engine` | Editing sessions, commands/completion, selection, indexed snapping and API-independent picking |
| `CadSpace.Dxf` | ASCII/binary transport, typed interpreters/writers and native project persistence |
| `CadSpace.Rendering` | Cameras, Skia drafting, OpenGL geometry/text/selection passes |
| `CadSpace.Controls` | Reusable Uno ribbon, viewport, palettes, console, dynamic input, tabs and status/workspace composition |

All six are independently packable. Controls share one package rather than an assembly per widget. Core/editing/exchange do not require Uno or a graphics context. The application owns file dialogs and document lifetime.

```csharp
var session = new CadSpace.Engine.CadSession();
session.Add("Line", new CadSpace.Model.LineEntity(new(0, 0), new(100, 50)));
var commands = new CadSpace.Engine.CommandEngine(session);
var workspace = new CadSpace.Controls.CadWorkspace();
workspace.Bind(session, commands);
```

## Verification and delivery

```sh
# Independent fixture generator: test-only, not shipped with the application.
python -m pip install ezdxf==1.4.4
python tests/fixtures/generate.py

dotnet run --project tests/CadSpace.Tests -c Release
dotnet run --project tests/CadSpace.Exchange.Tests -c Release
dotnet run --project tests/CadSpace.Persistence.Tests -c Release
dotnet run --project tests/CadSpace.Advanced.Tests -c Release
dotnet run --project tests/CadSpace.Performance.Tests -c Release
```

The five executable suites currently contain **169 tests**. CI regenerates independent fixtures and requires zero ezdxf-reported errors **and zero repairs** for tested ASCII/binary exports. This is a synthetic independent-library audit, not Autodesk qualification.

CI builds three desktop hosts, publishes WebAssembly, packages all six libraries, and exercises rendered browser pixels, selection upload behavior and CAD interactions. Screenshots, diagnostics, source and benchmark output are retained as artifacts. Chromium uses a software-backed graphics context; this does not qualify physical GPU drivers or performance.

Successful current-main builds deploy to GitHub Pages and verify the served revision. Tagged releases verify the same headless suites/audits and package desktop/browser/source distributions with checksums. NuGet.org publication, signing and notarization are not automatic.

The graphics host remains Uno `GLCanvasElement`, with framebuffer readback and a pinned RGBA conversion adapter. It is **not zero-copy WebGPU/Vulkan**. See [Coverage](docs/FEATURES.md) for remaining model, DXF, UI and qualification boundaries.

MIT licensed. Read [Contributing](CONTRIBUTING.md), [Security](SECURITY.md) and [Third-party notices](THIRD-PARTY-NOTICES.md). CadSpace is independent of Autodesk and contains no Autodesk source, icons, fonts, ACIS or RealDWG components.
