# Changelog

## 0.1.0 — initial implementation

### CAD core

Double-precision geometry, immutable drawing/entity records, atomic edits, bounded undo/redo, layer visibility/locking, nested blocks, selection windows, object/grid snapping, coordinate parsing and mesh modeling are implemented as headless libraries.

The command registry includes 42 workflows. In addition to the initial drawing/transform/block/model commands, the line-editing increment adds TRIM, EXTEND, FILLET, CHAMFER, JOIN, BREAK and ELLIPSE. These commands implement the documented line-based/XY behaviors, not all AutoCAD options. Numeric-only prompts now ignore extra pointer clicks. Degenerate rectangles and repeated adjacent interactive polyline vertices are rejected.

### Files

ASCII DXF exchange preserves original text for unchanged imports and original record data for untouched imported objects. Unsupported records remain opaque. Export-loss reports distinguish display geometry from native editing semantics.

Versioned `.cadspace` projects preserve all implemented entity types, mesh topology, layers, blocks and imported DXF provenance. Native Save and DXF Export are separate operations. Browser/desktop file dialogs, multiple document tabs, unsaved-tab warnings and dirty-state display are connected to the document model.

### UI and rendering

The Uno application includes an original dark CAD ribbon, command history/input, drafting/3D viewports, layer/block/property palettes, document tabs, status modes, navigation controls and a resizable palette boundary. Skia draws the 2D scene; a shader/VBO OpenGL renderer draws depth-tested shaded mesh geometry. GPU-context failures are reported and return to 2D.

### Delivery

CI runs geometry/document, exchange/editing, and native-persistence regression suites, builds desktop targets, publishes WebAssembly, and retains browser-test screenshots and console logs. Per-job concurrency avoids blocking browser validation behind cancellation of unrelated desktop jobs. Pages deployment checks the current main revision. The release workflow packages reusable libraries, self-contained desktop distributions and browser/source archives, with checksums for all assets.

### Important boundaries

This is not full AutoCAD parity or an industrial solid-modeling kernel. There is no DWG, ACIS/B-rep, solid Boolean/fillet/chamfer, NURBS kernel, full DXF semantics, dynamic blocks/attributes, complete typography, arbitrary UCS, paper-space plotting, plugin/API parity, full docking or enterprise collaboration. Native AutoCAD and physical-GPU qualification have not been completed. See `docs/FEATURES.md`.
