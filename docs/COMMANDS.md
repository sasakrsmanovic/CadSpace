# Command reference

Enter one command or prompted value at a time. Coordinates use invariant decimal syntax: `10,20`, `10,20,30`, `@5,-2`, or `@50<30`. Enter finishes a line/polyline sequence; Escape cancels. Select objects before starting a modification command. Extra pointer clicks at a numeric-only prompt are ignored instead of corrupting command state.

| Command | Alias | Inputs / behavior |
| --- | --- | --- |
| LINE | L | Start point; following points; Enter finishes. Each segment is an undo transaction. |
| PLINE | PL | Points; Enter finishes open; C closes after at least three points. |
| RECTANG | REC | Two opposite corners in the XY plane. |
| CIRCLE | C | Center; numeric radius or a point on the circle. |
| ARC | A | Three points on an XY arc; collinear points are rejected. |
| ELLIPSE | EL | Center; XY major-axis endpoint; minor radius no larger than the major radius. |
| POINT | PO | One coordinate. |
| TEXT | T | Insertion point; text. Initial text height is 12 units; edit it in Properties. |
| DIMALIGNED | DAL | Two extension points; dimension-line location. |
| HATCH | H | Select closed polylines first. Adds a basic 45-degree hatch with 10-unit spacing. |
| MOVE | M | Selected objects; base point; destination point. |
| COPY | CO | Selected objects; base point; destination point. |
| ROTATE | RO | Selected objects; base point; numeric angle in degrees. |
| SCALE | SC | Selected objects; base point; positive uniform scale factor. |
| MIRROR | MI | Selected objects; two points defining the XY mirror axis. Replaces originals. |
| OFFSET | O | Selected lines/circles/arcs; signed distance. Positive is line-left or circle/arc-outward. |
| TRIM | TR | Selected line boundaries; point on an unselected line portion to remove. |
| EXTEND | EX | Selected line boundaries; point near the unselected line end to extend. |
| FILLET | F | Exactly two selected coplanar XY lines; radius. |
| CHAMFER | CHA | Exactly two selected coplanar XY lines; equal distance. |
| JOIN | J | Selected connected line chain; creates a polyline. |
| BREAK | BR | One selected line; two projected break points. |
| ERASE | E | Deletes selected editable objects. Locked/opaque objects prevent the transaction. |
| EXPLODE | X | Selected supported blocks or polylines, including bulged segments. |
| ARRAY | AR | Selected objects; `columns,rows,x-spacing,y-spacing`. At most 10,000 instances and 100,000 resulting objects. |
| BLOCK | B | Selected objects; unique block name; base point. Replaces selection with an insert. |
| INSERT | I | Existing block name; insertion point. Edit scale/rotation in Properties. |
| BOX | BOX | Two base corners; numeric height. Creates a triangle mesh. |
| CYLINDER | CYL | Center; radius or radius point; numeric height. |
| SPHERE | SPH | Center; radius or radius point. |
| CONE | CONE | Center; radius or radius point; positive height. |
| EXTRUDE | EXT | Selected closed XY polylines/circles; height. Retains original profiles. |
| REVOLVE | REV | Selected polylines; axis start; axis end; angle up to 360 degrees. Creates a sampled surface, not a capped solid. |
| DIST | DI | Two points; reports distance and coordinate delta. |
| AREA | AA | Reports area of selected closed polylines using their tessellated boundary. |
| UNDO | U | Undo the most recent document transaction. |
| REDO | REDO | Redo the most recently undone transaction. |
| SELECTALL | ALL | Select visible entities in the active layout. |
| ZOOM | Z | Zoom extents; no additional ZOOM command options yet. |
| TOP | TOP | Switch to the top drafting viewport. |
| 3DORBIT | 3DO | Switch to the 3D viewport; click selects, drag orbits. |
| HELP | ? | Print implemented command names and aliases. |

See [line-editing details and boundaries](LINE-EDITING.md). Modification errors are reported in command history and do not partially update the drawing. Many standard AutoCAD options/subcommands are not implemented; command-name familiarity does not imply full option parity.


## Additional 3D and view workflows

| Command | Alias | Inputs and boundary |
| --- | --- | --- |
| 3DPOLY | 3P | WCS points; Enter finishes, C closes. |
| SPLINE | SPL | Control points; Enter creates a clamped spline of degree up to 3. Not fit-point interpolation. |
| ROTATE3D | 3R | Selected objects; two axis points and angle in degrees. |
| MIRROR3D | 3M | Selected objects; three noncollinear mirror-plane points. |
| ALIGN3D | 3A | Three source frame points followed by three target frame points; rigid, no scaling. |
| LOFT | LOFT | Selected closed planar profiles with matching sampled vertex counts, in drawing order; capped polygon mesh. |
| SWEEP | SW | One closed profile and one open polyline path; bounded parallel-transport mesh. |
| UNION | UNI | Union selected closed triangle meshes. |
| SUBTRACT | SU | Subtract others from the first selected mesh in drawing order. |
| INTERSECT | IN | Intersection of selected closed triangle meshes. |
| VSCURRENT | VS | Wireframe / HiddenLine / Shaded / ShadedEdges. |
| PERSPECTIVE | PERSPECTIVE | 1 perspective, 0 orthographic. |
| CLIP3D | CLIP3D | x,y,z,nx,ny,nz or OFF; retains normal·(point-origin) <= 0, uncapped display only. |

The complete registry has 59 commands. EXPLODE now handles analytic bulged-polyline segments as arcs. SelectAll is limited to visible entities of the active layout. Mesh tools do not imply ACIS/B-rep or every AutoCAD option.

Tab accepts completion in an idle command box; F2 expands history, F12 toggles dynamic input, and Ctrl+1 toggles Properties. Snap options expose per-mode choices including line intersections/perpendiculars and circle/arc tangents. Browser shortcuts may take precedence.

## Selection and grip workflows

| Command | Alias | Inputs / behavior |
| --- | --- | --- |
| STRETCH | S | First crossing-window corner; opposite corner; base point; displacement point. Fully enclosed roots translate. Partial LINE/LWPOLYLINE/3D POLYLINE/SPLINE/DIMENSION vertices or supported insertion points inside the box move. |
| QSELECT | QS | `kind,layer[,mode[,scope]]`, for example `LINE,*,Replace,All`. `*` matches any whole field, not a general wildcard expression. Mode is Replace/Add/Remove/Toggle. Scope is All (visible active-layout objects) or Selection. The ribbon instead opens a type/layer dialog. |
| SELECTSIMILAR | SE | Select visible active-layout objects matching the selected roots' kind and layer. No other property-match options yet. |
| RENDERSTATS | RS | Report actual scene, overlay and model RenderOverride counters. These are recording/render counts, not presented frames or GPU timings. |

STRETCH uses an explicit crossing box regardless of drag direction. It does not support fence/lasso/multiple-box selection, arbitrary UCS, arc deformation, NURBS surface deformation, constraint propagation or every AutoCAD option. Partial objects without an eligible grip in the box are ignored; unsupported partial deformation with an included grip is rejected. Locked candidates reject the transaction. A partial XY polyline remains planar; bulges are retained, not constraint-solved. Invalid or unsupported multi-object edits leave the document unchanged.

In the 2D viewport, normal clicks/windows add, Shift removes and Ctrl toggles. Clicking empty space without a modifier clears selection. SC enables an overlap menu capped at 25 roots; Ctrl+W is an alternative where the browser does not reserve it. Text uses conservative envelopes. The 3D click-selection behavior remains separate.

Visible blue grips edit line endpoints/midpoints, circle center/radius, polyline vertices, spline control points, dimension extension/location points, point/text/block insertions and placed equivalents. Arc/ellipse grips currently move only the center. Meshes, composite imported inserts and opaque geometry do not have editable subobject grips. Grip drag previews are transient; release creates one undo step, Escape and capture loss cancel. Locked objects expose no grips. To keep pointer work bounded, more than 200 selected roots or 4,096 handles hides grips; commands remain available.
