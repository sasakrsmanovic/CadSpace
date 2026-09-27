# Feature coverage and qualification

This document describes implemented behavior, not an assertion of AutoCAD parity. A command appearing in the ribbon has a working implementation; unsupported command names report an error rather than pretending to execute.

## Drafting and editing

| Feature | Status and boundary |
| --- | --- |
| Lines, points, circles, arcs | Editable. ARC accepts three points in the XY drawing plane. |
| Polylines and rectangles | Editable; DXF bulges are retained and tessellated. UI creates straight segments. |
| Ellipses | Model/render/DXF/native persistence support; no dedicated creation command or general transform editor yet. |
| Text | Plain text and basic multiline display. No full MTEXT grammar, SHX font engine, shaping qualification, fields, or style inheritance. |
| Dimensions | Nonassociative aligned dimensions with fixed display sizing. DXF export currently explodes display geometry. |
| Hatching | A single closed polyline boundary, generated line pattern or model-level solid fill. No nested islands, arbitrary PAT definitions, gradient editor, or associativity. |
| Selection | Click, window, crossing, additive/toggle selection. 3D viewport is orbit-only; select mesh wireframes from Top view. |
| Snapping | Endpoint, midpoint, center, quadrant and nearest; grid, ortho and 45-degree polar guidance. No complete intersection/tangent/perpendicular/extension snap engine. |
| Modify | Move, copy, rotate, uniform scale, XY mirror, erase, rectangular arrays. |
| Offset | Lines, circles and arcs only; signed numeric offset, not a complete side-picking offset workflow. |
| Explode | Straight polylines and supported block children. Bulged polyline explode, arbitrary affine block decomposition, and unsupported children are rejected atomically. |
| Blocks | Definitions, base points, nested inserts, transforms, layer-0/by-block inheritance and insertion. No attributes, dynamic parameters/actions, constraints, block editor, or xref management. |
| Layers | Current layer, color, visibility and locking. No complete linetype engine, per-viewport overrides, layer filters/states, or exhaustive ACI palette. |
| Undo/redo | Immutable document transactions, bounded to 256 undo entries. Selection/view changes are not document transactions. |
| Advanced 2D tools | Trim, extend, fillet, chamfer, stretch, spline editing, region Boolean operations, constraints, and comprehensive grips are not implemented. |

## Three-dimensional modeling

Boxes, cylinders, cones and spheres are explicit triangle meshes. Extrusion triangulates a simple planar XY profile and adds side/cap triangles. Concave simple profiles are supported; holes, self-intersections and degeneracies are rejected. Revolve creates a sampled polyline surface around a two-point axis and does not claim a watertight B-rep solid.

There is no ACIS/SAT kernel, analytic solid topology, Boolean union/subtract/intersect, solid fillet/chamfer, shelling, NURBS surface kernel, arbitrary sweep/loft, direct face modeling, associative feature tree, or watertight-manifold certification. The signed-volume display is meaningful only for consistently oriented closed meshes; it is not a physical mass-properties certificate.

GPU rendering includes triangle lighting, depth testing, wire edges, floating-origin coordinates, orbit/pan/zoom, and top/front/isometric navigation. GPU picking, section planes, clipping caps, textures, materials, shadows, physically based rendering and full 3D annotation are not implemented. The navigation widget is a set of view controls, not an exact reproduction of AutoCAD's ViewCube.

## DXF behavior

The ASCII reader recognizes LINE, POINT, CIRCLE, ARC, supported LWPOLYLINE, XY ELLIPSE, basic TEXT/MTEXT, simple INSERT, and 3DFACE. It reads layer definitions, block definitions, entity colors and selected metadata. Width-bearing polylines, non-world object-coordinate systems, thickness, paper-space records and unsupported entities remain opaque.

An unchanged imported drawing can be emitted as the original input text. Untouched imported entities retain their group-pair records after unrelated edits. Unknown sections are retained. Modified supported objects are regenerated and can lose unmodeled object metadata; changed layer/block tables can also lose unmodeled metadata. Such conversions are reported. Handle ownership, dictionary references and all cross-object constraints are not comprehensively regenerated, so edited files require independent downstream validation.

New/edited DXF output identifies as AC1027. Meshes export as independent 3DFACE records. CadSpace dimensions/hatches export as line/polyline/text display geometry with warnings. Native DIMENSION/HATCH editing semantics are not inferred from that display. Unsupported opaque objects do not become editable merely because they survive export.

Binary DXF, DWG, all-version DXF conversion, arbitrary code pages, AutoCAD vertical-product object semantics, proxy graphics decoding, embedded fonts/images, and full original application behavior are not supported. A 64 MiB application import limit and additional geometry limits apply.

## Native projects

`.cadspace` version 1 is a manually serialized JSON format for the complete implemented model. It preserves persistent IDs, all implemented geometry, mesh topology, hatch/dimension parameters, layers, nested blocks, and the original DXF provenance/map. The reader rejects unsupported versions and validates the resulting model. It does not use unrestricted polymorphic deserialization or execute drawing data.

Undo history, open-tab state, selection, cameras, and transient command input are session state and are not stored. Automatic crash recovery, journal replay, browser-refresh protection, shared editing, encrypted storage, and enterprise permissions are not implemented. Save before closing a browser or desktop window.

## UI and platform qualification

The desktop and browser application share a custom Uno ribbon/viewport/command/palette workspace. The layout follows familiar CAD workflows but is not pixel-exact AutoCAD UI or complete interaction parity. It does not yet provide docking/floating palettes, paper-space layouts, plotting, a customizable CUI system, AutoLISP/VBA/.NET AutoCAD APIs, tool palettes, standards management, Sheet Set Manager, or every dialog/property editor.

Core regression tests and platform builds are automated. Browser smoke tests use Chromium with a software-backed graphics context in CI and are not physical-GPU performance qualification. Native AutoCAD open/AUDIT/save/reopen, large industrial DXF corpora, accessibility audits, touch/pen qualification, multi-monitor desktop operation, driver matrices, and independent security reviews have not been completed.
