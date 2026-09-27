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
| Click and 3D picking | Flat stackless BVH candidates before existing narrow phases | Cold O(N log²N)-style median-sort construction; unfavorable projected bounds can admit many candidates |
| Snapping | Indexed visible anchors/segments/curves through nested blocks and placements | Revision/layout changes rebuild snap data; complex combinations and crowded intersections are limited |
| Tessellation | Cache immutable root scenes and reuse unchanged paths/text/triangles | Aggregate arrays are restitched; layer/block changes invalidate all relevant roots |
| Draft drawing | Indexed visible-path/text queries; reusable candidate lists | Visible path construction and raster recording remain CPU work |
| GPU upload | Direct packed float buffer; separate per-vertex selection attribute ranges | Real scene/origin changes still upload full geometry; text updates have their own cost |
| UI invalidation | Coalesced redraw requests and throttled diagnostic-label updates | Host composition/readback and some inactive pointer changes still cause work |
| Bulk transform | ID dictionary instead of per-entity linear replacement search | Immutable collection replacement and document validation still traverse data |

The selection-stream unit test checks that changing the last 10 of 10,000 vertices touches only those ranges. The browser test separately checks a visible mesh highlight without increasing the geometry-upload counter. Neither means zero total work on selection: text color buffers, UI state and picking still update.

## Browser build

Release enables IL and XAML resource trimming plus the runtime jiterpreter. `-p:CadSpaceUntrimmed=true` retains the diagnostic untrimmed configuration. Trimmed builds must pass published-app tests, especially the reflective Uno readback adapter. Artifact ZIP size is not the same as HTTP transfer size or measured startup time.

## Next performance qualification

Measure cold index/tessellation time and peak memory, real imported block-heavy/hatch-heavy files, long polylines, incremental layer/block invalidation, window selection, drag previews, text atlas pressure, viewport readback and physical display timing. Introduce budgets from those measurements before claiming broad CAD-scale performance.
