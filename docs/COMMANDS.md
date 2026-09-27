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
| EXPLODE | X | Selected supported blocks or straight polylines. |
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
| SELECTALL | ALL | Select visible model-space entities. |
| ZOOM | Z | Zoom extents; no additional ZOOM command options yet. |
| TOP | TOP | Switch to the top drafting viewport. |
| 3DORBIT | 3DO | Switch to perspective 3D; drag to orbit. |
| HELP | ? | Print implemented command names and aliases. |

See [line-editing details and boundaries](LINE-EDITING.md). Modification errors are reported in command history and do not partially update the drawing. Many standard AutoCAD options/subcommands are not implemented; command-name familiarity does not imply full option parity.
