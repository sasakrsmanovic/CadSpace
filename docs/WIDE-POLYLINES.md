# Native polyline widths and editing

CadSpace stores `PolylineEntity.ConstantWidth` and `PolyVertex.StartWidth`/`EndWidth`, separately from lineweight. Widths are drawing-unit geometry, not screen-pixel strokes. Zero width uses the existing centerline. New native JSON fields default to zero when opening older projects.

## Commands and Properties

`PLINEWID` sets the nonnegative width for newly created PLINE/RECTANG geometry. `PEDIT` (`PE`) operates on preselected 2D polylines (including placed OCS polylines): `Width` (`W`) sets a uniform width; `Open` (`O`), `Close` (`C`) and `Reverse` (`R`) preserve supported geometric properties. Reversal swaps segment direction, width endpoints and bulge signs; a double reversal restores dormant final-open-vertex data too. Each edit is one undo transaction and respects locked layers.

Properties exposes global width and a bounded vertex-index editor for start width, end width and bulge. Editing a segment converts a former global width to explicit widths on the remaining segments, rather than erasing them. A stale captured entity is rejected. This is not the full interactive AutoCAD PEDIT option set; Join/Fit/Spline/Decurve and every edit-vertex workflow are not implemented here.

## Exchange and geometry

LWPOLYLINE constant width maps to group 43; vertex widths map to groups 40/41. Legacy 2D POLYLINE header defaults and VERTEX overrides are interpreted. Uniform transforms scale widths; reflections preserve width while reversing bulge orientation. General affine placements stay native in projects. DXF export of nonuniformly transformed wide geometry uses sampled mesh faces with a conversion warning, not silently widthless centerlines. EXPLODE rejects wide polylines unless the user explicitly sets width to zero first.

Straight and circular-bulge centerline segments produce fill strips; width varies linearly along a segment's sampled arclength parameter. Each strip has one filled 2D outline and explicit 3D triangles. Selection uses the outside fill, not just the centerline. Closed paths retain unfilled interiors. Zero-width segments remain visible paths.

**Rendering limits:** joins use bevel fills, not exact AutoCAD miter/endpoint fitting; curved boundaries use up to 256 samples per full turn. Self-overlap, cusp/very wide arc behavior, overlapping translucent fills, patterned wide-strip rendering and every PLINEGEN/lineweight combination are not production-qualified. Wide fills currently display continuously even when a linetype is assigned; style metadata still survives exchange. Extremely large widths/coordinates and vertex counts are bounded. There is no solid/B-rep topology implied by the fill triangles.

## Performance and verification

A one-pass LWPOLYLINE vertex decoder avoids per-vertex suffix scans/slices and stops at XDATA. Immutable scene bounds use a weak identity cache, with the original fit calculation retained exactly. Record copies cannot inherit stale bounds. The benchmark compares 100 warmed bounds queries on 100,000 paths to the former LINQ scan, checking identical results and allocated bytes. This excludes cold construction, mutation, rendering and GPU presentation; it is not an application-FPS claim.

Independent ezdxf-generated fixtures include constant/tapered widths, bulges, closing segments, OCS and R12 defaults. Separate ezdxf audits check native width groups in both ASCII and binary exports and require zero errors/repairs. Headless tests cover geometry, transforms, persistence, picking, atomic edits, locked/stale selections and bounds-cache behavior. Published-browser checks require visible filled pixels, a change to centerline display after PEDIT, GPU faces, undo/reversal/closure and the real native recovery checkpoint. Native Autodesk interoperability and physical-GPU qualification remain separate gates.

## Format references

Autodesk LWPOLYLINE group-code reference: https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm

Autodesk PEDIT 2D polyline options: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-MAC-Core/files/GUID-648BDC21-E835-478F-A318-9D201479998D.htm
