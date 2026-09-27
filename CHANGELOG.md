# Changelog

## Unreleased — selection, grips, recovery and invalidation

Added root-indexed window/crossing selection, whole-root text/face containment, deterministic overlap picking, add/remove/toggle selection, kind/layer Quick Select and Select Similar. Added transactional blue grip dragging with preview/cancel/stale-object guards and bounded crossing STRETCH for supported vertices. The registry contains 59 workflows; unrestricted AutoCAD option parity is not implied.

Separated static drafting from cursor/grip feedback, gated 3D invalidation by drawing/camera/selection state, cached Properties/layout rebuilding, indexed selected entities and replaced recursive full-subtree sorting with median partitioning and bounded fallback. Tests record warmed window-query allocation/time and cold construction separately; RenderOverride counters support real browser invalidation checks.

Added reusable two-slot checksummed recovery journaling and an asynchronous desktop/browser storage adapter. Dirty drawings checkpoint every five seconds when storage is available; recovery opens unsaved documents and does not silently replace files. Payload/discovery limits, browser storage eviction and deferred synchronization remain explicit boundaries. Escape from the command box also cancels captured grip gestures.

211 headless regressions cover these paths alongside the existing geometry/DXF suites. Published-browser checks exercise the new interaction/recovery workflows in addition to the existing 3D/interop pixel tests. See the PR and workflow artifacts for the exact verified revision and results.

## Earlier preview — CAD expansion, performance and workspace

Expanded DXF transport/interpreters to ASCII/code pages and binary R12/R13+, OCS/affine geometry, legacy polylines/meshes, rational splines, supported hatch loops/patterns/islands, compound INSERT display and model/paper separation. Added native MESH/SPLINE/HATCH export and supported ownership/layout reconstruction. Native project v2 retains new geometry and original bytes/provenance, now checked against reparsed source. Corrected MTEXT radians and final orientation precedence.

Added bounded mesh Booleans, matching-profile capped loft, parallel-transport sweep, control-point spline/3D polyline creation, 3D rotation/reflection/alignment and analytic bulge explode. The command registry now has 55 workflows.

Added BVH picking/snapping/culling, incremental root tessellation, dictionary-based bulk transform replacement, packed geometry buffers and independent changed-range GPU selection flags. Added deterministic performance/equivalence regressions and a recorded 100,000-line warmed-picking comparison.

Refined the Uno workspace with stacked ribbon groups, minimize/restore, command completion/Tab, expandable history, cursor-adjacent input, context actions, hideable Properties and individual snap settings. The 3D viewport includes world-plane text, styles, picking/highlights, projection and uncapped clipping; browser readback uses the pinned RGBA adapter. Release browser trimming is exercised through published-app tests.

Verification comprises 169 headless tests, independent ASCII/binary DXF audits, three desktop builds and browser pixel/interaction tests. CI packages all six libraries and validates the deployed Pages revision. This remains a preview, not full AutoCAD, analytic B-rep or engineering qualification; see docs/FEATURES.md.

## 0.1.0 — initial implementation

Introduced geometry/model/editing/exchange/rendering/controls libraries, Uno desktop/browser workspace, basic drafting, layers/blocks, mesh primitives, native storage and build/Pages/release workflows.
