# Line editing and ellipse creation

The following commands extend the initial command reference. They are implemented for the stated entity types, not every AutoCAD option.

| Command | Alias | Workflow |
| --- | --- | --- |
| TRIM | TR | Select one or more line boundaries first. Start TRIM and pick an unselected target line portion. The interval between the neighboring boundary intersections is removed; remaining portions retain line geometry. |
| EXTEND | EX | Select line boundaries first. Pick an unselected line near the end to extend. The nearest valid intersection beyond that end is used. Boundary segments are bounded, not infinite extensions. |
| FILLET | F | Select exactly two nonparallel coplanar XY lines. Enter a nonnegative radius. Line arms farther from their intersection are retained; the nearer ends are shortened and an analytic tangent arc is added. A radius larger than the available arms is rejected. |
| CHAMFER | CHA | Select exactly two coplanar XY lines and enter a nonnegative equal distance. Adds a bevel line and shortens the two nearer ends. |
| JOIN | J | Select a single connected, nonbranching chain of lines. Reversed/unordered input is supported. Produces a closed polyline if the chain closes. Disconnected/repeated-node chains are rejected. |
| BREAK | BR | Select exactly one line, then supply two break points. Points are projected onto the line and bounded to its segment. The intervening portion is removed. |
| ELLIPSE | EL | Supply the center, XY major-axis endpoint, and a positive minor radius no larger than the major radius. Creates an analytic full ellipse. |

Line editing is exposed in the **Modify** ribbon tab. Every operation validates all affected objects and commits atomically, supports undo, and respects locked layers. Trim/extend target picking uses the command engine's `PickTolerance` in model units (default 5). No curved cutting boundaries, polyline filleting, 3D curve editing, unequal-distance chamfer options, or complete edge-extension modes are claimed.

At numeric prompts such as box height or rotation angle, extra pointer clicks do not corrupt the command state. Numeric values still need to be entered in the command line.
