# Architecture

Dependency direction: App → Controls → Engine/Rendering → Model → Geometry. Dxf depends on Model, not the UI. Controls expose events; App owns file dialogs and document lifetime.

## Documents, commands and scenes

Entities/drawings are immutable records with double-precision geometry, visibility and layout identities. `CadDocument.Edit` validates before publishing and retains up to 256 undo states. `CadSession` owns selection and drafting modes. The command engine accepts typed values and pointer points through one state machine; completion is a headless catalog service.

`PlacedEntity` retains affine/OCS geometry without flattening Z. `CompositeEntity` holds explicit display children of supported compound records. Rational splines use homogeneous de Boor evaluation; hatch interpretation preserves bulges and reports sampled edge-list conversions. Mesh Boolean/surface operations are bounded, explicit triangle algorithms—not an analytic B-rep.

`DrawingSceneCache` reuses tessellation by immutable root identity and restitches aggregate arrays after edits. Each cached root records its layer, nested block and resolved linetype dependencies. Changed dictionaries invalidate matching roots, not unrelated geometry; global linetype scale changes invalidate patterned roots. Aggregate root traversal is still O(N), and layout switching rebuilds that layout. `SceneAcceleration` attaches lazy path/text/triangle BVHs to scene identity through a weak table, so record copies do not share stale indexes. The existing public `DrawingScene.Bounds` accessor still computes its bounds independently.

`SnapIndex` expands fixed anchors and analytic circle/arc data through visible nested references/placements, then indexes them. Exact fixed candidates precede nearest sampled-curve fallback. Intersection work is bounded for crowded candidate sets. `ScenePicking` uses BVH candidates plus double-precision ray/projected-edge narrow phases, honoring display clipping.

## Exchange

`DxfBinary` handles typed byte transport and code pages. `DxfEntityReader` groups/interprets logical records; `DxfEntityWriter` emits modeled geometry and loss reports; `DxfDocumentWriter` reconstructs supported table/owner/layout relationships. `DxfCodec` coordinates sections and preserves original records/identities.

Unknown records stay opaque. Unchanged byte imports can return original bytes. Native project v2 explicitly serializes geometry, layouts, nested identities and provenance; it checks saved provenance against reparsed source before reuse. There is no unrestricted runtime type-name deserialization. None of this establishes universal lossless DXF editing or exhaustive ownership repair.

Linetype definitions and per-object/global scales are explicit model data, serialized in native projects and mapped to DXF tables/common groups. Unsupported complex patterns retain their source entries; export without their provenance is rejected rather than manufacturing an empty pattern. Native provenance validation includes linetype tables and scale.

`RecoveryJournal` provides alternating checksummed slots through `IRecoveryStorage`. App uses native LocalFolder on desktop and a scoped IndexedDB JavaScript adapter in browsers. The adapter resolves only when the transaction commits; it is explicitly embedded as an Uno bootstrap resource and retained through Release trimming. Recovery is best-effort, not an unload-save or power-loss guarantee.

## Graphics and controls

Skia drafting receives a host-owned canvas and culls paths/text through the spatial index. The OpenGL renderer rebases double coordinates before float packing, maintains geometry separately from per-vertex selection flags, and sends only changed selection ranges via buffer sub-updates. Scene/origin changes still rebuild the packed geometry buffer. Linetypes add an accumulated-distance attribute and shader pattern batches, without emitting dash triangles. Skia expands only visible clipped dashes. The text renderer uses bounded Skia-rasterized atlases on world-plane quads.

Renderer state is isolated from its host. Uno `GLCanvasElement` performs platform composition/readback. `PortableGlCanvasElement` selects an existing RGBA/channel-conversion path on browsers with a version-pinned reflective field and an explicit trimming dependency. Requalify the adapter and published Release build on Uno upgrades; this is not zero-copy WebGPU/Vulkan.

The reusable workspace combines a dense ribbon, document tabs, command console/history/completion, dynamic input, palettes, virtualized Layer Properties and Linetype managers, and status/navigation controls. Dynamic input submits to the same command engine. Redraw requests are coalesced; diagnostic text updates are throttled, not labeled GPU time or FPS. The controls share one assembly/package but do not reference App.

## Verification

Five executable headless suites cover model/editing/exchange/index behavior. Pinned ezdxf is a test-only fixture/audit dependency. Browser tests execute the published application and check visible pixels, upload counters and interaction outcomes. Their software-backed context does not qualify physical GPU performance or Autodesk interoperability. See [Performance](PERFORMANCE.md) and [Coverage](FEATURES.md).
