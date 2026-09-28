# Performance: measured paths and remaining costs

## Reproducible picking comparison

```sh
dotnet run --project tests/CadSpace.Performance.Tests -c Release
```

The suite constructs 100,000 independent lines on a regular grid, builds the scene and spatial index, warms picking, and compares 150 indexed click queries with a linear scan. Both results must match. A separate small-box query asserts fewer than 128 hierarchy-node visits and at most four candidate lines. Timing is printed; `CADSPACE_BENCHMARK_OUTPUT` optionally writes it to a file. CI retains `artifacts/performance.txt`.

One local .NET 10 Linux execution measured **1.544 ms indexed versus 191.415 ms linear** for the 150 warmed picks (about 124× for this specific query set). This is an illustrative observed run, not a portable minimum or a flaky wall-clock test gate. Hardware, JIT, load, distributions and drawing structure change the timings.

The comparison excludes initial validation, cold scene/index construction, file loading, GPU work, browser startup and presentation. It does not establish a whole-app speedup, an FPS target, a million-entity guarantee or performance on real industrial drawings.

## Implemented optimizations

| Path | Change | Remaining cost |
| --- | --- | --- |
| Click and 3D picking | Flat stackless BVH candidates before existing narrow phases | Cold median partitioning with bounded iteration and sort fallback; unfavorable projected bounds can admit many candidates |
| Window/crossing selection | Identity-cached root BVH, deterministic candidate order and whole-root geometry/text/face containment | Cold root aggregation; large windows and overlapping blocks can still visit most objects |
| Snapping | Indexed visible anchors/segments/curves through nested blocks and placements | Revision/layout changes rebuild snap data; complex combinations and crowded intersections are limited |
| Tessellation | Per-root layer/block/linetype dependencies reuse unaffected immutable scenes | O(N) root traversal and aggregate array restitching remain; actual scene changes still upload the complete GPU geometry buffer |
| Draft drawing | Indexed visible-path/text queries; reusable candidate lists | Visible path construction and raster recording remain CPU work |
| GPU upload | Direct packed float buffer; separate per-vertex selection attribute ranges | Real scene/origin changes still upload full geometry; text updates have their own cost |
| UI invalidation | Separate static-drawing/interaction Skia elements, model invalidation stamps, coalescing and throttled labels | Cursor work and host composition still occur; this does not eliminate framebuffer readback for actual 3D changes |
| Bulk transform | ID dictionary instead of per-entity linear replacement search | Immutable collection replacement and document validation still traverse data |

The selection-stream unit test checks that changing the last 10 of 10,000 vertices touches only those ranges. The browser test separately checks a visible mesh highlight without increasing the geometry-upload counter. Neither means zero total work on selection: text color buffers, UI state and picking still update.

## Window-query and allocation comparison

The same suite compares 30 warmed small contained-window queries on 100,000 independent lines with the previous `GroupBy`/point-array/containment scan. Both return identical IDs. It records current-thread allocated bytes for the complete query loop, candidate count and visited nodes; cold root-index construction is recorded separately. The query is deliberately narrow and repeated, not a broad industrial-drawing workload. Allocated bytes are cumulative allocation, not peak live memory.

The deterministic gates require matching IDs, a bounded candidate/node count and substantially fewer allocations. Timings are observations, not wall-clock pass/fail budgets. All reported values are available in the run's `performance.txt`; do not extrapolate a query-specific speedup to application FPS, import speed or physical GPUs.

## Invalidation verification

`RENDERSTATS` prints actual static scene, interaction overlay and model RenderOverride counts. Published-browser tests compare counters before/after idle 3D pointer movement and 2D cursor movement; they also verify that a committed grip edit changes pixels and one undo restores them. These counts verify avoided re-recording/render calls. They do not measure GPU execution, compositor cost, presentation cadence or frame latency.

Properties and layout lists now skip rebuilds when their relevant immutable drawing/selection/tab inputs are unchanged. Selected-entity access uses a document-ID dictionary plus drawing-order sorting of the selection, rather than scanning every entity on each grip preview.

## Layer edits and linetype rendering

The 3,000-circle benchmark changes the color of a layer used by one circle. It compares dependency-aware reuse against a new full root cache, verifies every resulting path/color/point, and asserts exactly one rebuilt root and 2,999 reused roots. Time and current-thread allocated bytes are recorded separately. A local run measured 1.368 ms / 318,784 bytes for reuse versus 200.792 ms / 32,985,688 bytes for a full rebuild. These are observations from that narrow warmed workload, not cross-machine guarantees or whole-application latency.

Linetype paths retain their source geometry. Skia clips segments to the visible box before generating dash/dot strokes, preventing a billion-unit line from expanding its entire offscreen pattern. The GPU renderer batches equal patterns and evaluates signed elements in its fragment shader. This avoids dash-mesh inflation, but adds a distance attribute and pattern-dependent draw batches. Dense subpixel patterns intentionally display continuously; extreme lengths and tiny patterns are not a precision qualification.

## Browser build

Release enables IL and XAML resource trimming plus the runtime jiterpreter. `-p:CadSpaceUntrimmed=true` retains the diagnostic untrimmed configuration. Trimmed builds must pass published-app tests, especially the reflective Uno readback adapter. Artifact ZIP size is not the same as HTTP transfer size or measured startup time.

## Next performance qualification

Measure cold index/tessellation time and peak memory, real imported block-heavy/hatch-heavy files, long polylines, large block-dependent edits, large/crowded window selection, complex drag previews, text atlas pressure, viewport readback and physical display timing. Introduce budgets from those measurements before claiming broad CAD-scale performance.


## Immutable scene bounds

`DrawingScene.Bounds` now caches its original fit calculation by immutable scene identity in a weak table. The first read still scans geometry; repeated reads reuse the result without retaining dead scenes. Record copies have independent cache entries. The conservative text bounds used for selection remain separate, so this optimization does not change fit behavior.

The bounds regression compares 100 warmed reads on a 100,000-path scene with the previous LINQ implementation, asserting exactly equal bounds and recording cumulative current-thread allocations. One CI run recorded **0.019 ms and 0 allocated bytes** for cached reads versus **1537.878 ms and 560,049,600 allocated bytes** for the former repeated scan. This intentionally repeated-query workload measures avoided work, not cold scene construction, whole-application speed, import latency, rendering or GPU frame rate. Timing is not a pass/fail gate.

The native-width increment also introduces a one-pass LWPOLYLINE vertex decoder that retains widths and stops before XDATA. No measured end-to-end DXF import speedup is claimed. Wide polyline rendering emits one filled outline per strip and explicit GPU triangles; geometric edits still pay tessellation and upload costs.
