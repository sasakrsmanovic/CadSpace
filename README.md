# CadSpace

### Modular CAD for desktop and the browser

[![Build, test and deploy](https://github.com/wieslawsoltes/CadSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/CadSpace/actions/workflows/build.yml)
[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**[Open the browser app](https://wieslawsoltes.github.io/CadSpace/)** · **[Commands](docs/COMMANDS.md)** · **[Coverage](docs/FEATURES.md)** · **[Architecture](docs/ARCHITECTURE.md)** · **[Performance](docs/PERFORMANCE.md)**

CadSpace is an independent C# CAD workspace built with Uno Platform, Skia and OpenGL/WebGL. It combines double-precision drafting, layers and blocks, rational splines, mesh modeling, ASCII/binary DXF exchange, and reusable desktop/browser controls.

> **Development preview—not full AutoCAD parity.** Mesh modeling is not an analytic ACIS/B-rep kernel, and arbitrary DXF editing is not universally lossless. Preserve originals, review export warnings, and save native projects. Best-effort five-second recovery checkpoints do not replace saved files or backups.

## Integrated CAD workspace

The ribbon now includes large/split command buttons, compact tool columns, inline layer/color/linetype/lineweight selectors, dialog launchers and contextual selection tabs. The shell adds an application menu, Quick Access toolbar, command search, document overflow/reordering, true model/layout tabs, dockable/floating/auto-hide palettes, searchable Tool Palettes, camera-synchronized ViewCube, viewport menus and a vertical navigation bar. Workspace preferences are saved separately from drawings. Every visible tool routes to implemented behavior; this is not a claim of full AutoCAD/CUI compatibility.

Read [controls, shortcuts, persistence and remaining UI boundaries](docs/WORKSPACE.md).

## Workspace

The startup document is an editable studio floor plan. **3D example** opens a separate model study. The custom Uno workspace includes a dense, minimizable ribbon, document tabs, command completion/history, cursor-adjacent dynamic input, editable blue grips, Quick Select, overlap cycling, properties/layers/blocks, model/layout selection and 3D navigation.

| Area | Implemented workflows |
| --- | --- |
| Drafting | Lines, 2D/3D polylines, rectangles, circles, arcs, ellipses, control-point splines, points, plain text, aligned dimensions and supported hatching |
| Editing | Move/copy/rotate/scale/mirror, 3D transforms/alignment, offset, arrays, bulge explode, line trim/extend/fillet/chamfer/join/break, crossing STRETCH, supported grips and undo/redo |
| Precision | Absolute/relative/polar coordinates; indexed block/OCS anchors, line intersection/perpendicular and circle/arc tangent snaps; grid, ortho and polar |
| Organization | Layer creation/rename/color/visibility/locking/weight, block definitions/nesting/insertion, model and paper-space entity roots |
| Linetypes | Signed dash/gap/dot patterns, ByLayer/ByBlock, object/global scales, continuous polyline generation, Skia drafting and GPU model-view patterns |
| Mesh modeling | Primitives, extrusion, revolved surfaces, capped matching-profile loft, parallel-transport sweep and bounded closed-mesh Booleans |
| 3D viewing | Depth-tested shading, feature edges, picking/highlights, world-plane plain text, projection, uncapped clipping, pan/orbit and anchored zoom |
| Files | ASCII/binary DXF and supported code pages; native MESH/SPLINE/HATCH/style output; original-record preservation; native projects and local recovery |

The registry contains **76 command workflows**, not every option of their AutoCAD namesakes. Exact inputs and restrictions are in [Commands](docs/COMMANDS.md).

### Layer and linetype managers

`LAYER` (`LA`) opens the virtualized **Layer Properties Manager**. Search layer names, create or rename layers, change RGB color/weight/visibility/locking/linetype, choose the current layer and delete unused noncurrent layers. Edits use shared undoable engine operations. Layer 0, used layers and opaque DXF references have explicit protection rules.

`LINETYPE` (`LT`) opens the **Linetype Manager**. Load standard patterns, define simple signed lengths, choose the type/scale for new objects, assign to a selection and change global scale. Opening either manager preserves the current 3D view. Layer row fields use compiled bindings so they survive Release trimming.

`CELTYPE`, `CELTSCALE` and `LTSCALE` expose the corresponding command workflows. A type-only Properties change preserves each selected object's own scale. Spline dash phase continues across tessellation; 2D/3D polyline generation flags survive DXF/native round trips. Complex text/shape/SHX linetypes remain source-backed fallbacks, not fully rendered or editable patterns.

## Performance

Per-root layer/block/linetype dependencies avoid tessellating unaffected geometry. The reproducible **3,000-circle / one-layer color edit** benchmark requires exactly **one rebuilt root and 2,999 reused roots**, then compares every output path/color/point with a full rebuild. CI records time and allocated bytes for both paths.

Lazy spatial indexes accelerate picking, snapping, visible geometry and whole-root window/crossing queries. Selection uses a separate GPU attribute stream, static drafting is cached separately from cursor/grip feedback, idle 3D pointer movement avoids model redraws, and redraw requests are coalesced. GPU linetypes evaluate dash patterns in shaders rather than expanding dash meshes; Skia clips before generating visible dash strokes. Pattern expansion has finite-value, numerical-range and work-budget guards.

The suites also compare warmed indexed picking/window queries on 100,000 lines against linear implementations, requiring identical results. **These are narrow workload comparisons, not whole-app FPS, cold-start, physical-GPU or million-entity scalability claims.** Root traversal, validation, aggregate array rebuilding and actual geometry uploads still have drawing-size costs. See [methodology and remaining costs](docs/PERFORMANCE.md).

## Precision selection and grips

Normal 2D clicks/windows add to selection; Shift removes and Ctrl toggles. Drag left-to-right for containment or right-to-left for crossing. Complete-root containment includes block text and triangle children. **SC** enables overlap cycling; text envelopes are conservative rather than shaped glyph outlines.

Drag supported blue grips for line endpoints/midpoints, circles, polyline vertices, spline controls, dimensions or insertions. Preview is transient; release commits one undo transaction. Escape/capture loss cancels; stale and locked edits are rejected. Grips are bounded to 200 selected roots and 4,096 handles, not a complete 3D/subobject grip system.

The ribbon's **Quick Select** exposes type/layer/scope/mode filtering. Textual `QSELECT` accepts `LINE,*,Replace,All`. `SELECTSIMILAR` matches kind/layer. `STRETCH` uses two crossing-box corners, a base point and a displacement point, with supported partial-vertex edits and complete-root translation. See [restrictions](docs/COMMANDS.md).

## Files and recovery

**Open** accepts `.cadspace` and ASCII/binary `.dxf`. **Save** writes a native project. **Export DXF** and **Binary DXF** produce interchange copies without clearing native dirty state.

Typed exchange includes supported OCS/affine entities, legacy polylines and meshes, rational splines, hatch boundaries/patterns/islands and attributed/array INSERT display children. Linetypes map to `LTYPE`, common groups 6/48, `$LTSCALE` and generation flags. Supported model/paper-space ownership/layout links are reconstructed. Native version 2 stores implemented geometry and original DXF provenance/bytes, validates provenance against reparsed source data, and preserves supported untouched source records. Basic version 1 projects remain readable.

Unknown entities stay opaque. Modified compounds, generated dimensions, sampled boundaries and unmodeled metadata can be lossy or unsupported. Complex linetype export without original provenance is rejected. There is no DWG/ACIS decoder or Autodesk open/AUDIT/save/reopen qualification.

Dirty drawings checkpoint every five seconds after storage initialization into alternating checksummed native-project slots. Browser writes use **IndexedDB transaction completion**, not an unflushed in-memory filesystem write; desktop uses LocalFolder. **Recover** restores closed-session checkpoints as unsaved documents without silently overwriting originals. Native save, a clean undo state or explicit tab discard clears the applicable checkpoints.

Recovery does not persist undo, cameras or tab layout. Limits are 32 Mi-characters per payload and 256 discovered slots. Storage denial/eviction, private mode, power loss and changes after the last checkpoint remain risks; there is no unload-save or crash-durability guarantee.

## Try a drawing

Enter one line at a time:

```text
CELTYPE
DASHED
RECTANG
0,0
240,140
CELTYPE
BYLAYER
CIRCLE
120,70
30
ZOOM
```

Select a closed profile before `EXTRUDE`. `LOFT` requires matching closed profiles in drawing order; `SWEEP` needs one closed profile and one open polyline. Mesh Booleans require closed, consistently oriented operands. `SUBTRACT` uses the first selected mesh **in drawing order**, not click order.

Click a 3D face to select; drag to orbit. Middle-drag pans and the wheel zooms about the pointer. View styles are Wireframe, HiddenLine, Shaded and ShadedEdges. Section clipping is uncapped and display-only.

| Input | Action |
| --- | --- |
| Tab in an idle command box | Accept command completion |
| `@20,10` / `@50<30` | Relative Cartesian / polar point |
| Enter / Escape; Up / Down | Submit or finish / cancel; command history |
| Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+E | New / open / native save / ASCII DXF export |
| Ctrl+Z / Ctrl+Y / Ctrl+A | Undo / redo / select all, respecting text editing |
| F2 / Ctrl+1 / F12 | Command history / Properties palette / dynamic input |
| F3 / F7 / F8 / F9 / F10 | Object snap / grid / ortho / grid snap / polar |

**Snap options** exposes individual modes. Ctrl+W is an overlap-cycling alternative in non-text-editing contexts where the browser does not reserve it. Primary operations also have visible controls.

## Build and run

Pinned dependencies: **Uno.Sdk 6.7.30**, **Uno graphics 6.7.135**, **SkiaSharp 3.119.2**, **Silk.NET.OpenGL 2.23.0**, **.NET 10**. The SDK pin accepts newer stable .NET 10 feature bands.

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

3D requires a compatible OpenGL/GLES/WebGL context. Linux needs the native Uno/Skia dependencies and a display. Release browser builds enable IL/XAML trimming and the jiterpreter; `-p:CadSpaceUntrimmed=true` retains the diagnostic untrimmed configuration. Requalify the published build on framework upgrades.

## Reusable libraries

| Package | Responsibility |
| --- | --- |
| `CadSpace.Geometry` | Double vectors/transforms, OCS, rays/planes, intersections, triangulation and spatial index |
| `CadSpace.Model` | Immutable documents/styles/blocks/curves/hatches, scene cache and mesh algorithms |
| `CadSpace.Engine` | Sessions, commands, selection, grips, indexed snapping and undoable style operations |
| `CadSpace.Dxf` | ASCII/binary transport, interpreters/writers, native projects and recovery journal |
| `CadSpace.Rendering` | Cameras, Skia drafting, OpenGL geometry/text/pattern/selection passes |
| `CadSpace.Controls` | Uno ribbon, viewport, managers, palettes, console, dynamic input and workspace |

All six are independently packable; Controls shares one package rather than an assembly per widget. Core/editing/exchange do not require Uno or a graphics context. The application owns file dialogs and document lifetime.

```csharp
var session = new CadSpace.Engine.CadSession();
session.Add("Line", new CadSpace.Model.LineEntity(new(0, 0), new(100, 50)));
var commands = new CadSpace.Engine.CommandEngine(session);
var workspace = new CadSpace.Controls.CadWorkspace();
workspace.Bind(session, commands);
```

## Verification and delivery

```sh
# Test-only dependency: not shipped with the application.
python -m pip install ezdxf==1.4.4
python tests/fixtures/generate.py

dotnet run --project tests/CadSpace.Tests -c Release
dotnet run --project tests/CadSpace.Exchange.Tests -c Release
dotnet run --project tests/CadSpace.Persistence.Tests -c Release
dotnet run --project tests/CadSpace.Advanced.Tests -c Release
dotnet run --project tests/CadSpace.Performance.Tests -c Release
```

The five suites contain **327 headless regressions**. CI generates independent fixtures and requires **zero errors and zero repairs** for tested ASCII/binary geometry and style exports. It builds Windows/macOS/Linux, publishes the trimmed browser app, packs all six libraries and tests rendered pixels, grip/selection workflows, recovery after reload, patterns and layer-manager edits. Artifact screenshots and native checkpoints make visual and persistence failures inspectable.

Current-main builds deploy to GitHub Pages and verify the served commit. Tagged releases run headless checks/audits and package desktop/browser/source distributions with checksums. NuGet.org publication, signing and notarization are not automatic.

Software-backed Chromium is not physical-GPU qualification. The Uno graphics host still uses framebuffer readback with a pinned RGBA adapter; it is **not zero-copy WebGPU/Vulkan**. Full AutoCAD UI/API, analytic solids, typography, dynamic blocks, constraints, paper-space viewports/plotting and industrial interoperability remain substantial work. See [Coverage](docs/FEATURES.md).

MIT licensed. Read [Contributing](CONTRIBUTING.md), [Security](SECURITY.md) and [Third-party notices](THIRD-PARTY-NOTICES.md). CadSpace is independent of Autodesk and contains no Autodesk source, icons, fonts, ACIS or RealDWG components.

### Native wide polylines

`PLINEWID` sets the width of new polylines and rectangles. Select polylines and use `PEDIT` → `Width`, `Open`, `Close` or `Reverse`. Properties exposes global width and a vertex-indexed segment editor without allocating one control per vertex. Native DXF and project files retain constant/tapered widths and bulges; OCS widths render as 3D faces. Read [supported width semantics and limits](docs/WIDE-POLYLINES.md).
