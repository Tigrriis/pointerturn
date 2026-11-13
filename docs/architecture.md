# PointerTurn design notes

## Two projects

| Project | What it is | Targets |
|---|---|---|
| `PointerTurn.Core` | Everything that doesn't need BricsCAD: design vehicles, the simulation, the swept path, the solver, conflicts, the ground clearance check, and turning the results into entities to draw. All of it is unit-tested. | `netstandard2.0` and `net8.0` |
| `PointerTurn.BricsCAD` | The add-in: commands, dialogs, the live preview, the ribbon, reading and drawing in BricsCAD, and saving each path's inputs in the drawing. | `net48` for BricsCAD V24/V25, `net8.0-windows` for V26 and later |

Core has two targets because BricsCAD V24/V25 run on .NET Framework 4.8 and V26 onwards on .NET 8.
The add-in builds against one BricsCAD version at a time (see the comment at the top of
`PointerTurn.BricsCAD.csproj`).

Core works in **metres**, always. `DrawingUnits` converts at the edges: points, curves, obstructions
and surface levels from the drawing are scaled on the way in, and everything drawn is scaled on the
way out. Nothing in between knows what the drawing is drawn in.

## Core

- **Vehicles** (`DesignVehicle`, `TrailerUnit`): dimensions, axle groups, the minimum turn radius
  (measured at the outer front wheel), clearances and any trailers. They are data in
  `turn-paths.json`; B99 and B85 are also built in as a fallback.
- **Simulation** (`TurnPathSimulator`): a kinematic bicycle model. A reference point on the vehicle
  follows the path and the rear axle tracks in behind it with no side slip. Each trailer is dragged
  by its coupling under the same rule.
- **Swept path**: the exact boundary of the union of the body over every pose, not an offset of the
  path. For a combination it is the union over every unit.
- **Templates and driving** (`TurnPathBuilder`, `ManoeuvrePlanner`): a standard turn from a start
  point, a vehicle along an existing curve, or a drive leg by leg (turn at the radius, then
  straight; forwards or in reverse; optionally arriving on a set heading).
- **Solver** (`ManoeuvreSolver`): a hybrid A* search over position, heading and direction of travel,
  on a 0.25 m and 5 degree grid, stepping along full-lock arcs and straights, and finishing with
  turn-straight-turn paths to the target or to a straight run-in to it. Every step is tested by
  sweeping the body's true outline, enlarged by the clearance, against the obstruction linework. It
  prefers fewer movements, then less driving. The 3-point solve runs it twice, into a stop and out
  of it, with the first and last movements held forwards and optional boundary lines the body may
  only be over while moving forwards. It is for vehicles without trailers.
- **Conflicts** (`ConflictChecker`): where the swept path (a hit) or its clearance (an
  encroachment) meets obstruction linework, with the gap that is left.
- **Ground clearance** (`GroundClearanceChecker`): the AS/NZS 2890.1 Appendix C template slid along
  a ground profile, or placed on each side's wheels along a path on a surface.
- **Drafting** (`TurnPathDrafter`, `GroundClearanceDrafter`): results become a list of plain entity
  descriptions (`EntitySpec`), each on its layer. Core never touches a drawing.
- **Records** (`ManoeuvreRecord`): everything needed to draw a path again, as JSON.

## The add-in

A command runs like this, all on BricsCAD's main thread and as one UNDO step:

1. The dialog collects the vehicle, radius, clearances, layers and what to draw
   (`TurnPathDialog`, `GroundClearanceDialog`).
2. The command prompts for the geometry in the drawing (`TurnPathCommand`). While a point is being
   picked, `TurnPathJig` simulates the manoeuvre under the cursor at a coarser step and shows it.
3. Core plans and drafts the result.
4. `CadDrawing` makes any missing layers, creates the entities, and gathers them into a block if
   asked. `ObstructionScanner` reads the linework on the obstruction layers (in blocks and xrefs
   too) for the conflict check and the solver.
5. `ManoeuvreStore` saves the record in the drawing, so `PTEDIT` can regenerate the path.

### What is kept in the drawing

- Every entity of a path carries the path's id as extended data, under the application name
  `POINTERTURN`.
- The path's record is an Xrecord in the dictionary `POINTERTURN_PATHS` in the drawing's named
  objects dictionary, keyed by that id. The JSON is split into 240 character pieces.
- Paths drawn by CSD-Bridge used `CSDBRIDGE_TURN` and `CSDBRIDGE_TURNPATHS`. Those are still read,
  so the paths can be edited. An edited path is saved under the new names.

### Settings

The turning path conventions are read from the first of these that exists:

1. the file named by `turn_paths_file` in `%APPDATA%\PointerTurn\settings.json`;
2. `%APPDATA%\PointerTurn\turn-paths.json`, written by **Save as defaults**;
3. `%APPDATA%\CsdBridge\turn-paths.json`, a copy saved from CSD-Bridge (read only);
4. `turn-paths.json` next to the add-in DLL.

Vehicles that the file found doesn't have are added from the shipped file, so new vehicles appear
after an update.

### Libraries the add-in ships with

- On .NET 8 (V26 and later) the add-in needs nothing beyond the runtime.
- On .NET Framework (V24/V25) it ships `System.Text.Json` and what that depends on. Every add-in
  shares one AppDomain there and an add-in's own binding redirects are ignored, so
  `DependencyResolver` loads them from the add-in folder by name.
- The `System.Text.Json` version is the one CSD-Bridge ships, so the two add-ins can be loaded in
  the same BricsCAD.

## First things to test

None of the add-in has been run in BricsCAD yet. In this order:

1. **It builds** against the installed BricsCAD, for V26 and for V24/V25 if both are in use.
2. **It loads**: `NETLOAD` the DLL. The command line should say `PointerTurn loaded` and the ribbon
   tab should appear.
3. **`PTPATH`, template**: a B99 90 degree turn in a metre drawing, then the same in a millimetre
   drawing. Check the layers, the radius and that it is one UNDO step.
4. **`PTPATH`, follow and drive**: along a polyline, then click by click, with the live preview.
5. **`PTSOLVE` and `PTINOUT`**: between kerb lines on a named layer, with and without the boundary
   line.
6. **`PTEDIT`**: regenerate a path, change its vehicle, and edit one drawn as a block that has been
   moved. Then a path that was drawn by CSD-Bridge.
7. **`PTCLEARANCE`**: on a plotted long section, then on a BricsCAD TIN surface.
8. **Both add-ins together**: load CSD-Bridge and PointerTurn in the same session.
9. **A truck**: the 19 m semi-trailer at 12.5 m, overlaid on the Austroads turning template.

## Known limits

- Vehicles with trailers drive forwards only. Reversing them, and solving for them, are refused.
- The solver finds a good manoeuvre, not always the one with the very fewest movements.
- The Austroads vehicles were entered from the guide's dimension drawings and have not been
  compared against the published turning templates.
- The tool applies the standards' vehicles, radii and clearances. It does not reproduce the
  standards, and its results are a design aid to be checked by the engineer.
