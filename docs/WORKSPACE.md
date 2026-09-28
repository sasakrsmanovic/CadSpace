# CAD workspace controls

The workspace follows the Windows AutoCAD ribbon layout with original CadSpace artwork. This is not pixel-exact AutoCAD or CUI/API compatibility. Controls route to implemented tools; unsupported plotting, cloud-account, constraint and sheet-set features are not represented by inert buttons.

## Reusable components

`CadMenuBar`, `CadWorkspaceOptions`, `CadApplicationMenu`, `CadQuickAccessToolbar`, `CadCommandSearch`, `CadApplicationBar`, `CadRibbonPanel`, `CadRibbonButton`, `CadPropertySelectors`, `CadDocumentTabs`, `CadLayoutTabs`, `CadDockPane`, `CadDockHost`, `CadToolPalette`, `CadViewCube`, `CadViewportControls` and `CadNavigationBar` are independent public controls in CadSpace.Controls. The workspace integrates these with the existing viewport, command console, Properties, Layer/Linetype managers and drafting status controls. All six libraries remain independently packable.

The application menu operates on actual open drawings and host-owned file actions. Search discovers implemented commands by name, alias or description. Quick Access buttons can be hidden through their customization dropdown (that customization is session-only). Ribbon panels have large command buttons, separate alternatives dropdowns, small tool columns, dialog launchers, horizontal overflow and selection-context tabs. Layer/color/linetype/lineweight selectors reflect selection or defaults for new geometry; mixed selections are displayed without applying accidental changes. Only the edited property is changed, in one undo transaction.

Palette headers support left/right docking, in-window floating, pin/auto-hide, close, drag-to-edge docking and resizing. Hold Ctrl during a header drag to prevent edge docking. Escape and capture loss cancel an in-progress drag or resize and restore its prior placement without committing preferences. The right-docked resize handle sits on the inside edge. The generic host supports multiple independently registered panes; CadSpace registers Properties and Tool Palettes. Auto-hide tabs open on click and close with Escape or another tab click. Narrow windows use overlay palettes rather than consuming the drawing area. Floating bounds are constrained to the host without rewriting saved dimensions on resize. This is not native separate-window docking, arbitrary split/group docking, or a complete Visual Studio/AutoCAD docking implementation.

Tool Palettes provide virtualized searchable drafting, modification, modeling and actual drawing-block lists. Clicking a tool starts its command; clicking a block starts INSERT. Drag-and-drop palette authoring and saved custom tool catalogs are not implemented.

ViewCube orientation tracks the real camera, supports face/edge/corner picking and drag-to-orbit, and exposes standard views through a keyboard-accessible menu. View/style menus and the vertical navigation bar control real camera, projection, zoom/pan/orbit and display-only clipping state. Top/bottom camera matrices are nonsingular. This does not add arbitrary UCS authoring, SteeringWheels or 3D editing gizmos.

Model/Layout tabs switch the actual active document layout. New layouts have unique paper-block mappings; rename preserves object IDs and refuses opaque records; only empty layouts can be deleted. These operations are undoable. Embedded paper-space model viewports, sheet setup and plotting remain separate unfinished features. Document tabs expose dirty state, close confirmation through App, activation, overflow selection and context-menu reorder.

## Shortcuts and preferences

Ctrl+1 toggles Properties; Ctrl+3 toggles Tool Palettes; Ctrl+0 toggles clean screen. F2 expands command history, F6 focuses the command box, and Ctrl+K focuses command search where browser shortcuts permit. Drag the divider above the command line to resize history. Ctrl+Shift+R opens drawing recovery. Existing drafting and file shortcuts remain.

PROPERTIES, PROPERTIESCLOSE, TOOLPALETTES, TOOLPALETTESCLOSE, RIBBON, RIBBONCLOSE, CLEANSCREENON, CLEANSCREENOFF and OPTIONS use the same dispatcher as visible controls. MENUBAR accepts 1 to show the optional File/Edit/View/Draw/Modify/Tools/Help bar and 0 to hide it. Menu actions use that same dispatcher rather than a second tool implementation.

UISTATS and Ctrl+Shift+F12 report read-only rendered-control bounds (including open popup controls) and camera/workspace state. Diagnostics cannot mutate drawings, manufacture geometry or click controls; browser tests use the reported positions with ordinary pointer/keyboard input.

Options is a staged editor with Display, Workspace and Status Bar tabs. Checkbox/field changes do not affect the live workspace until Apply succeeds; Cancel leaves it untouched. Invalid heights keep the dialog open with an error. Reset restores the default workspace.

Display settings include the classic menu bar, ViewCube/navigation visibility, ribbon minimization, clean screen and command-window height (74–350 layout units). Workspace settings expose palette side, visibility and auto-hide, plus Drafting & Annotation, 3D Basics or 3D Modeling presets. The status-bar customization menu and Options can hide individual mode controls without switching those modes off. Drafting shortcuts remain usable.

The status-bar preset selector chooses drafting/modeling controls and view; 3D Modeling also opens Tool Palettes. Options exposes the preset and palette choices separately. Applying display-only Options preserves the current camera and 2D/3D mode. This is not a CUI editor or an independent saved layout for every preset. The active preset, ribbon/clean-screen state, palette positions/visibility/pinning, menu/navigation visibility, status customization and command height are serialized by the bounded, explicit `WorkspaceLayout` codec. Older version-1 preferences receive compatible defaults for newly added fields. App stores UI preferences separately from drawings: localStorage in browsers and LocalFolder on desktop. Denial/corruption is reported without preventing drawing use. There is no power-loss guarantee for these preferences. Quick Access visibility, camera positions, per-document snap settings and custom tools are not persisted. Snap-menu handlers are rebuilt when the active document changes, even when two documents initially have identical modes. Document tabs skip reconstruction when their names, dirty indicators, order and active identity have not changed.

## Reuse without the application

```csharp
var session = new CadSpace.Engine.CadSession();
var commands = new CadSpace.Engine.CommandEngine(session);
var workspace = new CadSpace.Controls.CadWorkspace();
workspace.Bind(session, commands);
workspace.FileRequested += action => { /* Host supplies NEW/OPEN/SAVE/export/recovery. */ };
workspace.PreferencesChanged += value => SaveUiPreferences(value.Encode());
// On startup, after validating/loading your own preferences:
workspace.RestorePreferences(CadSpace.Engine.WorkspaceLayout.Decode(savedJson));
```

The host owns the persistence functions and initial `savedJson`. Individual controls expose events and can be hosted separately: register a `CadDockHost.AddPane` with an initial `PalettePlacement`; connect menu/ribbon/search requests to a `CommandEngine`; bind `CadPropertySelectors` to the session. `CadWorkspaceOptions.Capture()` returns a validated preference record and never edits the document. All components ship in the independently packable Controls library, not one package per widget.

## Verification

Headless regressions cover preference round trips, invalid settings, constrained floating bounds, layout lifecycle and protection, atomic property changes and canonical cube picking/camera matrices. Published-browser tests use UISTATS only to locate actual controls, then send normal pointer/keyboard input. They exercise ribbon invocation, contextual editing, palette float/drag/pin, Tool Palettes modeling, view menus/cube corners, layout creation/deletion/undo, reload persistence, clean screen and a narrow viewport. The polish suite covers classic-menu geometry creation, staged Options cancel/apply, display-only view preservation, status visibility without mode changes, snap-menu document rebinding, console/palette Escape rollback and Ctrl-to-float. Existing rendering, DXF, selection, grip and recovery checks remain enabled. Physical-GPU, screen-reader, exhaustive touch and native-window qualification are not implied by software Chromium testing.

Reference layout: Autodesk's AutoCAD 2026 UI tour and interface tutorial:
- https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-OnBoarding/files/ACD_FOUNDATIONS_MAIN1.html
- https://www.autodesk.com/learn/ondemand/course/autocad-quick-start-guide/module/664vb7UrX2mnfZCfTsOnfO
