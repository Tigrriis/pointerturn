# PointerPath design notes

## Three projects

| Project | What it is | Targets |
|---|---|---|
| `PointerPath.Core` | Everything that doesn't need a CAD program: design vehicles, the simulation, the swept path, the solver, conflicts, the ground clearance check, and turning the results into entities to draw. All of it is unit-tested. | `netstandard2.0` and `net8.0` |
| `PointerPath.BricsCAD` | The add-in: commands, dialogs, the live preview, the ribbon, reading and drawing in the CAD program, and saving each path's inputs in the drawing. | `net48` for BricsCAD V24/V25, `net8.0-windows` for V26 and later |
| `PointerPath.AutoCAD` | The same add-in for AutoCAD. A project file and nothing else: it compiles the source in `PointerPath.BricsCAD` with `AUTOCAD` defined. | `net48` for AutoCAD 2021 to 2024, `net8.0-windows` for 2025 and later |

Core has two targets because BricsCAD V24/V25 and AutoCAD up to 2024 run on .NET Framework 4.8,
and BricsCAD V26 and AutoCAD 2025 onwards on .NET 8. The BricsCAD add-in builds against one
BricsCAD version at a time (see the comment at the top of `PointerPath.BricsCAD.csproj`), from
the API files in a BricsCAD install. The AutoCAD add-in builds both of its targets at once, from
Autodesk's `AutoCAD.NET` NuGet package, each against the oldest release it supports.

### One source, two CAD programs

BricsCAD's .NET API mirrors AutoCAD's name for name, in different namespaces (`Teigha.*` and
`Bricscad.*` for `Autodesk.AutoCAD.*`). So each file that touches the CAD program imports one
set or the other under `#if AUTOCAD`, and the code below the imports is shared. What actually
differs is small, and is either in `Host.cs` or under `#if AUTOCAD` where it is used:

- the wording that names the program;
- the ribbon button that shows its text under a large icon (a button style in BricsCAD, an
  orientation in AutoCAD);
- the linetype files to load from (`iso.lin` in BricsCAD, `acadiso.lin` in AutoCAD);
- TIN surfaces, reached by reflection in both: BricsCAD's `TinSurfaceStatic`, Civil 3D's
  `TinSurface`. Plain AutoCAD has no surfaces, so there the check works on a long section only.

The namespace in the source is `PointerPath.BricsCAD` for both builds; only the DLL is named for
the program (`PointerPath.BricsCAD.dll`, `PointerPath.AutoCAD.dll`).

Core works in **metres**, always. `DrawingUnits` converts at the edges: points, curves, obstructions
and surface levels from the drawing are scaled on the way in, and everything drawn is scaled on the
way out. Nothing in between knows what the drawing is drawn in.

## Core

- **Trailers are switched off.** The engine below still tows and reverses them, and the tests
  still run it, but the add-in leaves out any vehicle with trailers when it reads the vehicle file
  (`TurnPathConventions.RemoveVehiclesWithTrailers`), and the shipped file has none: the Austroads
  combinations are kept in `tests/PointerPath.Core.Tests/articulated-vehicles.json`. To bring them
  back, move them into `turn-paths.json` and take that call out of `PointerPathSettings`.
- **Vehicles** (`DesignVehicle`, `TrailerUnit`): dimensions, axle groups, the minimum turn radius
  (measured at the outer front wheel), clearances and any trailers. They are data in
  `turn-paths.json`; B99 and B85 are also built in as a fallback.
- **Simulation** (`TurnPathSimulator`): a kinematic bicycle model. A reference point on the vehicle
  follows the path and the rear axle tracks in behind it with no side slip. Each trailer is dragged
  by its coupling under the same rule.
- **Reversing trailers** (`TurnPathSimulator.Push`): a pushed trailer is unstable, so a reverse is
  driven, not traced. The line to hold is the forward drive along the path from its far end, played
  backwards (driving is reversible), which finishes with the combination in line. Started on that
  line, the reverse is exactly that. Started off it, the vehicle backs up a step at a time, steered
  within its lock from the back of the combination forwards: the last trailer is turned onto its
  line, which asks for a fold at its coupling, which asks the unit in front to turn, and so on to
  the steering. A fold takes metres to wind on and longer to wind off, so this only closes small
  gaps: getting the trailers set up is the leg's job.
- **Swept path**: the exact boundary of the union of the body over every pose, not an offset of the
  path. For a combination it is the union over every unit.
- **Templates and driving** (`TurnPathBuilder`, `ManoeuvrePlanner`): a standard turn from a start
  point, a vehicle along an existing curve, or a drive leg by leg (turn at the radius, then
  straight; forwards or in reverse; optionally arriving on a set heading). Forwards it is the
  front axle that follows the leg's path, and it travels the way its wheels point, which coming
  out of a turn is up to the full lock away from the way the vehicle faces. So each result says
  which way it left the wheels (`EndWheelHeading`) and the next forward leg's path starts that
  way: the front axle's path has no kink at a click, and a turn driven in several legs is the turn
  driven in one. The other way to drive it is the record's `turn_wheels_when_stationary`: the
  wheels are set before each forward leg moves off, so the leg is an arc about one centre on the
  line of the rear axle (the front axle at `sqrt(r² + wheelbase²)` from it) and the vehicle is in
  its steady turn from the first step, whatever the last leg left. A reverse with trailers
  (`TurnPathBuilder.ReverseTowed`) takes the last trailer's axle group to the destination, and
  starts with a swing of the vehicle, one way then if need be the other, whose lengths are found
  so that the leg's line starts with the trailers folded exactly as they are. Onto a heading, the
  last turn is a pivot turn (`PivotTurns`): a swing and a turn that take the combination from in
  line on the straight before it to in line at the end, so the straight is backed down in line and
  the trailers fold only in the turn. The swings for each angle of turn are found once per
  combination and radius and kept.
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

1. The dialog collects the vehicle, radius, clearances, layers and what to draw. The dialogs are
   being simplified one command at a time: `DriveDialog`, `TemplateDialog` (both a
   `VehicleDialog`: vehicle buttons, their own options, then Draw settings, Units and GO) and
   `DrawSettingsDialog` and `ConflictSettingsDialog`, built from `DialogKit`, are the new ones, and keep each choice as it is
   made (`PointerPathSettings.RememberDrive`, `RememberTemplate`); `PTQDRIVE` uses those choices
   without a dialog. `TurnPathDialog` and `GroundClearanceDialog` are the earlier ones, with every
   option, still behind `PTPATH`, `PTSOLVE`, `PTINOUT`, `PTEDIT` and `PTCLEARANCE`.
2. The command prompts for the geometry in the drawing (`TurnPathCommand`). While a point is being
   picked, `TurnPathJig` simulates the manoeuvre under the cursor at a coarser step and shows it.
3. Core plans and drafts the result.
4. `CadDrawing` makes any missing layers, creates the entities, and gathers them into a block if
   asked. `ObstructionScanner` reads the linework on the obstruction layers (in blocks and xrefs
   too) for the conflict check and the solver.
5. `ManoeuvreStore` saves the record in the drawing, so `PTEDIT` can regenerate the path.

### What is kept in the drawing

- Every entity of a path carries the path's id as extended data, under the application name
  `POINTERPATH`.
- The path's record is an Xrecord in the dictionary `POINTERPATH_PATHS` in the drawing's named
  objects dictionary, keyed by that id. The JSON is split into 240 character pieces.
- Paths drawn by CSD-Bridge used `CSDBRIDGE_TURN` and `CSDBRIDGE_TURNPATHS`. Those are still read,
  so the paths can be edited. An edited path is saved under the new names.

### The licence check

Each drawing command first asks `Licence` whether this computer may run it; if not, the sign-in
dialog is shown and the command goes ahead only once signed in. The check reads a signed licence
saved in the user's profile and needs no connection. [licensing.md](licensing.md) describes it.

### Settings

The turning path conventions are read from the first of these that exists:

1. the file named by `turn_paths_file` in `%APPDATA%\PointerPath\settings.json`;
2. `%APPDATA%\PointerPath\turn-paths.json`, written by **Save as defaults**;
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

The add-in has been loaded in BricsCAD, signed in and its commands run, but it has not been
worked through the list below, and nothing has been run in AutoCAD. In this order, in BricsCAD first
(then steps 2 to 7 again in AutoCAD, with a Civil 3D surface for step 7):

1. **It builds** against the installed BricsCAD, for V26 and for V24/V25 if both are in use.
2. **It loads**: `NETLOAD` the DLL. The command line should say `PointerPath loaded` and the ribbon
   tab should appear.
3. **`PTPATH`, template**: a B99 90 degree turn in a metre drawing, then the same in a millimetre
   drawing. Check the layers, the radius and that it is one UNDO step.
4. **`PTPATH`, follow and drive**: along a polyline, then click by click, with the live preview.
   Then **`PTDRIVE`**: each vehicle button, both tick boxes (the wheels one goes red), Draw settings
   and Units; close and reopen it to see the choices kept; and **`PTQDRIVE`** drives with them.
   Then **`PTTEMPLATE`**: a 90 degree turn left, a fan of angles to the right, and the straight
   lengths; the vehicle chosen there is the one Drive then opens on.
5. **`PTSOLVE` and `PTINOUT`**: between kerb lines on a named layer, with and without the boundary
   line.
6. **`PTEDIT`**: regenerate a path, change its vehicle, and edit one drawn as a block that has been
   moved. Then a path that was drawn by CSD-Bridge.
7. **`PTCLEARANCE`**: on a plotted long section, then on a BricsCAD TIN surface.
8. **Both add-ins together**: load CSD-Bridge and PointerPath in the same session.
9. **A truck**: the 12.5 m truck at 12.5 m, overlaid on the Austroads turning template.
10. **Reversing a car**: `PTPATH`, Drive, the B85: point it one way, click behind it and to one side.
    The front (the shorter, rounder end, with the wheels that steer) should swing out about a metre
    past where the car stood, on the outside of the turn.

## Known limits

- The solver is for rigid vehicles. A vehicle with trailers is driven leg by leg.
- A reverse with trailers is one a driver could make, found by a simple rule (swing, turn, straight,
  turn), not a search: where that can't start from the way the trailers are, no leg is offered.
- Reversing with trailers has been checked against the forward simulation (the same tracks, played
  backwards), not against another program or a real vehicle.
- The solver finds a good manoeuvre, not always the one with the very fewest movements.
- The Austroads vehicles were entered from the guide's dimension drawings and have not been
  compared against the published turning templates.
- The tool applies the standards' vehicles, radii and clearances. It does not reproduce the
  standards, and its results are a design aid to be checked by the engineer.
