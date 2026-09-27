# Architecture

## Dependency direction

```text
CadSpace.App ──────────────► CadSpace.Controls ──────► CadSpace.Engine
     │                             │                       │
     ▼                             ▼                       ▼
CadSpace.Dxf                CadSpace.Rendering ─────► CadSpace.Model
     │                                                     │
     └────────────────────────────► CadSpace.Model ────────► CadSpace.Geometry
```

Geometry, model, commands and exchange run without Uno. Rendering owns no window or document storage. Controls own interaction and presentation, not file dialogs. The application owns documents, file operations, dialogs and platform launchers.

## Document transactions

Entities and drawings are immutable records. `CadDocument.Edit` computes a replacement drawing, validates it, and only then publishes a revision. Errors leave the original drawing and history untouched. Undo/redo retain previous immutable snapshots, capped at 256 undo entries. Shared immutable collections avoid copying unmodified geometry, although large edit sequences still retain memory proportional to changed data.

`CadSession` contains transient selection and drafting modes. It caches a `DrawingScene` by document revision. `CommandEngine` consumes both command-line coordinates and viewport points through one prompt state machine. Commands emit atomic document operations and view requests; no command references Uno controls.

Geometry uses doubles. `Transform3` is an affine column-basis transform; `a.Then(b)` applies `a` then `b`. Analytic curved entities reject transforms that cannot be represented by their current model rather than silently deforming into a different entity. Meshes accept affine transforms and reverse triangle winding for reflections.

## Drawing scene and rendering

`EntityGeometry.BuildScene` expands nested block references, resolves visible layers and basic colors, tessellates supported curves, builds annotation display paths, and produces paths/text/triangles. Nesting is bounded and document validation rejects block cycles. Curve tessellation is bounded but is not an exact geometry kernel or an adaptive screen-space error solution for every curve.

`SkiaDraftRenderer` accepts a host-owned `SKCanvas`, a camera, a scene and a selection. `CadViewport` supplies that canvas through Uno `SKCanvasElement`. The camera subtracts its double-precision origin before converting coordinates to floats. Grid resolution changes with zoom to avoid excessive grid lines.

`GlSceneRenderer` accepts a host-owned Silk.NET OpenGL context and scene. Its shaders use the shared OpenGL 3.3 / GLES 3.0 subset. Geometry is packed into position/normal/color vertex buffers, rebased around the camera origin. Uploads happen only after scene/origin changes. The renderer preserves the major pipeline bindings and uses explicit depth testing. The current host is Uno `GLCanvasElement`, whose platform composition can include readback; do not describe this backend as zero-copy or as WebGPU/Vulkan.

The pipeline is demand-driven: input/document/view changes invalidate the viewport. Recording a CPU draw time does not enqueue an additional frame. Reported CPU time excludes actual GPU completion, compositor scheduling, upload/network startup, and display presentation.

## Storage boundaries

`DxfCodec` retains original input text, original parsed entities, section group pairs and entity-to-record mappings. Equality with the original graph permits exact unchanged-text return. Unchanged individual records survive unrelated editing. Canonical output is used only for new/changed supported objects. Unsupported data remains opaque and is never executed.

`CadProjectCodec` is an explicit versioned serializer for the native model. It stores original DXF text and original entity identity mappings as provenance. On read, it reparses that text, restores identities, and interns equivalent imported records, preserving untouched-record behavior across a native save/open cycle. This intentionally uses manual serialization rather than runtime type-name deserialization.

DXF export is an interchange operation, not a native project save. The application presents export-loss reports before writing and does not clear native dirty state as a side effect of export.

## Reusing the components

A headless tool can reference Geometry/Model/Engine/Dxf only. A non-Uno host can use `SkiaDraftRenderer` or `GlSceneRenderer` with its own surface and input handling. An Uno application can instantiate `CadRibbon`, `CadViewport`, `CadPalette`, `CadCommandLine`, `CadDocumentTabs`, or `CadStatusBar` independently, or use `CadWorkspace` as a composition. Commands, file requests and document-tab actions are exposed through events.

Each control is reusable independently from the app, but the controls currently ship in one assembly/package. They are not separate NuGet packages per visual widget. The package split follows dependency and runtime boundaries rather than creating an assembly for every button.

## Limits and next qualification gates

There is no spatial index: selection/snapping and scene construction traverse current drawing data. Very large drawings need indexing, revision-delta scene updates, tessellation caching, GPU picking, allocation profiling and measured budgets. Mesh topology is explicit triangles, not an industrial B-rep kernel. Full typography, native DXF semantics, arbitrary UCS/OCS, desktop docking, paper space and plotting require additional subsystems and independent fixtures.

Builds and self-roundtrip tests are necessary, not sufficient. Native AutoCAD interoperability, physical-GPU driver testing, large-document benchmarks, accessibility and security qualification are separate release gates.
