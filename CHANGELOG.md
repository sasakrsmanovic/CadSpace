# Changelog

## Unreleased — CAD expansion, performance and workspace

Expanded DXF transport/interpreters to ASCII/code pages and binary R12/R13+, OCS/affine geometry, legacy polylines/meshes, rational splines, supported hatch loops/patterns/islands, compound INSERT display and model/paper separation. Added native MESH/SPLINE/HATCH export and supported ownership/layout reconstruction. Native project v2 retains new geometry and original bytes/provenance, now checked against reparsed source. Corrected MTEXT radians and final orientation precedence.

Added bounded mesh Booleans, matching-profile capped loft, parallel-transport sweep, control-point spline/3D polyline creation, 3D rotation/reflection/alignment and analytic bulge explode. The command registry now has 55 workflows.

Added BVH picking/snapping/culling, incremental root tessellation, dictionary-based bulk transform replacement, packed geometry buffers and independent changed-range GPU selection flags. Added deterministic performance/equivalence regressions and a recorded 100,000-line warmed-picking comparison.

Refined the Uno workspace with stacked ribbon groups, minimize/restore, command completion/Tab, expandable history, cursor-adjacent input, context actions, hideable Properties and individual snap settings. The 3D viewport includes world-plane text, styles, picking/highlights, projection and uncapped clipping; browser readback uses the pinned RGBA adapter. Release browser trimming is exercised through published-app tests.

Verification comprises 169 headless tests, independent ASCII/binary DXF audits, three desktop builds and browser pixel/interaction tests. CI packages all six libraries and validates the deployed Pages revision. This remains a preview, not full AutoCAD, analytic B-rep or engineering qualification; see docs/FEATURES.md.

## 0.1.0 — initial implementation

Introduced geometry/model/editing/exchange/rendering/controls libraries, Uno desktop/browser workspace, basic drafting, layers/blocks, mesh primitives, native storage and build/Pages/release workflows.
