# CadSpace

### A modular CAD workspace for desktop and the browser

[![Build, test and deploy](https://github.com/wieslawsoltes/CadSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/CadSpace/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**[Browser application](https://wieslawsoltes.github.io/CadSpace/)** · **[Feature coverage](docs/FEATURES.md)** · **[Architecture](docs/ARCHITECTURE.md)** · **[Command reference](docs/COMMANDS.md)** · **[Build artifacts](https://github.com/wieslawsoltes/CadSpace/actions/workflows/build.yml)**

CadSpace is an independent, open-source CAD application built with **Uno Platform 6.7**, **.NET 10**, **SkiaSharp**, and **OpenGL/WebGL**. It combines a familiar dark ribbon workspace, a double-precision drafting model, command-line editing, blocks, layers, and triangle-mesh modeling. The same C# application targets Windows, macOS, Linux, and WebAssembly.

> **Early implementation, not full AutoCAD parity.** CadSpace implements the workflows described below; it is not an exact or production-qualified AutoCAD replacement. In particular, triangle meshes are not ACIS/B-rep solids. DWG, solid Boolean operations, dynamic blocks, constraints, paper-space plotting, and many advanced drafting tools are not implemented. Read the [coverage matrix](docs/FEATURES.md) before using engineering files. Keep original DXF files and independent backups.

## Workspace

The application opens an editable studio floor plan with walls, glazing, doors, furniture blocks, annotations, and dimensions. **3D example** opens a separate model document containing editable mesh primitives.

The custom Uno controls include a grouped command ribbon, document tabs, a drafting viewport, a 3D viewport, a command console with history, property/layer/block palettes, a resizable palette splitter, and drafting status controls. These controls use original vector artwork and do not depend on the application project.

## Implemented capabilities

| Area | Available now |
| --- | --- |
| Drafting | Lines, points, polylines, rectangles, circles, three-point arcs, plain text, aligned dimensions, and single-boundary hatching |
| Editing | Selection and crossing/window selection; move, copy, rotate, scale, mirror, line/arc/circle offset, erase, rectangular arrays, and supported explode operations |
| Precision | Double-precision coordinates, absolute/relative/polar input, endpoint/midpoint/center/quadrant/nearest snaps, grid snap, orthographic and polar guidance |
| Organization | Named layers, colors, visibility, locking, current layer, block definitions, nested references, and insertion |
| Modeling | Triangle-mesh boxes, cylinders, cones, spheres, extrusion of closed XY profiles, and revolved polyline surfaces |
| Viewing | Pointer-centered zoom, middle-button pan, zoom extents, 3D orbit, perspective viewing, shaded meshes with edges, and top/front/isometric navigation |
| Document lifecycle | Multiple documents, transactional undo/redo, dirty-state tracking, native project save/open, and close-tab discard confirmation |
| Interchange | Bounded ASCII DXF reader/writer, original-record preservation for untouched objects, unsupported-record retention, and explicit export-loss reports |

## Save a project; export an interchange copy

**Save** writes a versioned **`.cadspace`** JSON project. It retains all implemented entity types, mesh topology, dimensions, hatch parameters, layers, block definitions, persistent IDs, and imported DXF provenance. **Open** accepts `.cadspace` and ASCII `.dxf` files.

**Export DXF** writes a DXF interchange copy. Meshes become `3DFACE` records. CadSpace dimensions and hatches currently become display geometry, not equivalent native DXF editing objects. Export reports these conversions and asks for confirmation. An export does not mark the native project saved.

An unchanged imported drawing can be returned as its original DXF text. Unchanged imported records retain their group data after unrelated edits. This does **not** establish lossless editing of arbitrary DXF files: metadata on modified objects or regenerated tables may not survive. Unsupported records are retained as opaque data, not rendered as invented geometry.

There is no automatic browser-refresh recovery yet. Save a native project before refreshing, closing the browser, or closing the desktop window.

## Quick start

Use **New** for a blank drawing. Type each of these lines in the command box and press Enter after each line:

```text
RECTANG
0,0
240,140
CIRCLE
120,70
30
ZOOM
```

For a mesh extrusion, select the rectangle, run `EXTRUDE`, and enter a height. The original profile is retained. Press **TOP** to return to drafting. Use **Save** to retain native mesh editing semantics.

Click to select an object. Drag left-to-right to select objects entirely within a window; drag right-to-left for crossing selection. Shift/Ctrl modifies the selection. Select objects **before** starting a modification command.

| Input | Action |
| --- | --- |
| Mouse wheel / middle-drag | Zoom about the pointer / pan |
| Left-drag in 3D | Orbit the model |
| Enter / Escape | Submit or finish the current command / cancel |
| Up / Down in command input | Recall command history |
| `@20,10` / `@50<30` | Relative Cartesian / relative polar point |
| Ctrl+N / Ctrl+O / Ctrl+S | New / open / native save |
| Ctrl+Shift+E | Export DXF |
| Ctrl+Z / Ctrl+Y / Ctrl+A | Undo / redo / select all, without stealing active text editing |
| F3 / F7 / F8 / F9 / F10 | Object snap / grid / ortho / grid snap / polar |

Browser-reserved keyboard shortcuts can take precedence; all primary operations are also available as controls. See the [command reference](docs/COMMANDS.md) for aliases and exact prompts.

## Build and run

Install the .NET 10 SDK. The repository pins **Uno.Sdk 6.7.30**, with the **6.7.135** Uno graphics integration, **SkiaSharp 3.119.2**, and **Silk.NET 2.23.0**. `global.json` permits newer stable .NET 10 feature bands.

```sh
git clone https://github.com/wieslawsoltes/CadSpace.git
cd CadSpace
dotnet workload install wasm-tools

# Desktop: Windows, macOS, or Linux/X11
dotnet run --project src/CadSpace.App -f net10.0-desktop

# Browser development host
dotnet run --project src/CadSpace.App -f net10.0-browserwasm

# Static browser distribution for GitHub Pages
dotnet publish src/CadSpace.App -c Release -f net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/CadSpace/
```

The 3D viewport requires a suitable hardware-accelerated OpenGL/GLES/WebGL context. If initialization fails, CadSpace reports the failure and returns to the 2D view. Linux desktop hosting requires an X11 display and the native libraries required by Uno/Skia. macOS uses Uno's platform graphics integration. Software renderers and remote desktops may behave differently from physical GPUs.

## Reusable libraries

| Package | Responsibility | Dependencies within CadSpace |
| --- | --- | --- |
| `CadSpace.Geometry` | Double-precision vectors, transforms, intersections, bounds, triangulation | None |
| `CadSpace.Model` | Immutable entities, documents, scene construction, blocks, mesh generation | Geometry |
| `CadSpace.Engine` | Editing sessions, selection, snapping, commands, sample drawings | Model |
| `CadSpace.Dxf` | ASCII DXF exchange and native project persistence | Model |
| `CadSpace.Rendering` | Cameras, host-agnostic Skia drawing, OpenGL scene renderer | Model |
| `CadSpace.Controls` | Composable Uno controls and workspace | Engine, Rendering |

Every library is packable independently. `CadSpace.Controls` has no reference to the application or its file dialogs. The pure geometry/model/editor/exchange packages do not require a UI runtime.

```csharp
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

var session = new CadSession();
session.Add("Create line", new LineEntity(new Vec3(0, 0), new Vec3(100, 50)));
session.SelectAll();
session.TransformSelection("Move", Transform3.Translation(new Vec3(20, 10)));
session.Document.Undo();

// In an Uno host:
var workspace = new CadSpace.Controls.CadWorkspace();
workspace.Bind(session, new CommandEngine(session));
// The host handles workspace.FileRequested and owns storage/document lifetime.
```

## Verification and delivery

```sh
dotnet run --project tests/CadSpace.Tests -c Release
dotnet run --project tests/CadSpace.Exchange.Tests -c Release
dotnet run --project tests/CadSpace.Persistence.Tests -c Release
```

The executable regression suites test geometry degeneracies, mesh winding/volume, transaction atomicity, undo/redo, selection, snaps, layer locks, block transforms/cycles, DXF preservation, and native persistence. CI also builds the desktop application on three operating systems, publishes WebAssembly, and captures browser smoke-test diagnostics. See each workflow run for actual results; a build is not physical-GPU or native-AutoCAD qualification.

**Build, test and deploy** runs on pushes and pull requests. Successful main-branch core/browser jobs publish the static application to GitHub Pages; desktop builds are reported independently. Packages, source, browser output, and smoke-test diagnostics are retained as workflow artifacts. Pages must be available to the repository and the workflow must have Pages deployment permission.

**Release** runs for `vX.Y.Z` tags or a manually supplied version. It verifies the regression suites, builds self-contained desktop distributions, packages all reusable libraries, and attaches browser/source archives and checksums to a GitHub release. It does not automatically publish packages to NuGet.org. Desktop signing/notarization and independent installer qualification are not configured.

## Rendering design

The drafting renderer draws directly into Uno's Skia canvas. Model coordinates stay in doubles and are rebased before conversion to GPU floats. The 3D renderer uses explicit vertex buffers, shaders, lighting, and depth testing, uploading geometry only when the scene or floating origin changes.

The current 3D host is Uno's `GLCanvasElement`. Its platform integration can involve a framebuffer readback/composition copy; this is **not** a custom zero-copy WebGPU/Vulkan engine. WebGPU, Vulkan-specific CAD rendering, GPU picking, spatial indexing for very large drawings, and production-scale performance qualification remain future work. The on-screen timing is CPU draw-recording time, not a fabricated GPU duration or FPS counter.

## Project information

Read [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). CadSpace is MIT licensed. Autodesk, AutoCAD, DXF, and related names belong to their respective owners. CadSpace is independent and does not include Autodesk source code, icons, fonts, or proprietary modeling components.
