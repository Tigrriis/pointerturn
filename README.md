# PointerPath

Vehicle turning paths for BricsCAD and AutoCAD. PointerPath simulates a design vehicle through a
manoeuvre and draws its swept path and clearance template in the drawing. It runs entirely inside
the CAD program, with no other add-in needed. A released build is licensed: you sign in once with
a PointerPath account, and after that it works without an internet connection for up to two weeks
at a time. See [Licence](#licence).

These tools began as part of CSD-Bridge, Arete Engineering's BricsCAD assistant, and were moved
here to be an add-in of their own.

## What it does

- **Turning paths.** A B99 or B85 car (AS/NZS 2890.1), or an Austroads truck (8.8 m service
  vehicle and 12.5 m truck), drawn as a standard turn
  from a picked start point, driven along a line or polyline you select (forwards or in
  reverse), or driven click by click.
- **Solve turn.** Pick where the vehicle starts and where it ends up, and the forward and
  reverse movements between the kerbs, walls and columns are found for you.
- **In and out.** The same through a stop on a site, so the vehicle enters forwards and leaves
  forwards.
- **Conflicts.** Where the swept path or its clearance meets linework on the layers you name is
  marked on the drawing.
- **Editable paths.** Every path remembers how it was made, so its vehicle, radius, clearances,
  layers or geometry can be changed and the path regenerated.
- **Ground clearance.** The AS/NZS 2890.1 Appendix C check of a car's underside over a grade
  change, on a plotted long section or along a path on a TIN surface (BricsCAD's own, or
  Civil 3D's in AutoCAD).
- **Metres or millimetres.** It reads the drawing's units and works in either.

Vehicles, clearances, layers and what is drawn are all set in `turn-paths.json`, or in the
dialogs with **Save as defaults**.

## Status

This is a first cut as a separate add-in, not a finished product.

| Part | State |
|---|---|
| Core (vehicles, simulation, swept path, solver, conflicts, ground clearance, licence check) | Built and unit-tested: 123 tests |
| BricsCAD add-in | Built against BricsCAD and run in it: a licensed build loaded, signed in, and the drawing commands ran. Which BricsCAD version that was, and so which of the two builds (.NET Framework or .NET 8), was not noted. **Not yet worked through "First things to test"** |
| AutoCAD add-in | The same source, compiled against Autodesk's AutoCAD 2021 and AutoCAD 2025 APIs: it builds clean for both. **Not yet run in AutoCAD.** Reading Civil 3D surfaces is written from the API reference and untried |
| Installer | Builds, and has been run under Wine: install for all users and for one user, upgrade, uninstall, and the BricsCAD registry entries, against stand-in BricsCAD and AutoCAD registry keys. **Not yet run on Windows, or with a real BricsCAD or AutoCAD.** Not code-signed |
| Licence check | Unit-tested, and used for real once: a licensed build signed in from BricsCAD against the live website, and the commands then ran. **Not yet seen inside the CAD program: the daily refresh, signing out, the computer limit and a licence running out. Nothing has been run in AutoCAD.** Only the installer's build asks for a licence; a build from source doesn't: see [docs/licensing.md](docs/licensing.md) |
| Cars (B85, B99) | Front and rear overhangs corrected to AS/NZS 2890.1: they were first shipped back to front. The B85's (920 front, 1190 rear) are confirmed against a published copy of the template; the B99's (950 front, 1200 rear) and both cars' effective overhangs follow the same correction and **should be checked against Figures B1 and B2** |
| Austroads vehicles | Entered from the dimension drawings in AP-G34-23. **Not yet compared against the published turning templates.** Vehicles with trailers are switched off for now |

See [docs/architecture.md](docs/architecture.md) for the design, and "First things to test" there.

## Repository layout

```
src/PointerPath.Core/        CAD-independent: design vehicles, simulation, swept path, solver,
                             conflicts, ground clearance, the licence check
                             (netstandard2.0 + net8.0)
src/PointerPath.BricsCAD/    The add-in: commands, dialogs, ribbon, drawing in the CAD program,
                             settings. Builds for BricsCAD
  turn-paths.json            The vehicles and the defaults, shipped next to the add-in
src/PointerPath.AutoCAD/     A project file only: builds the same source for AutoCAD
tests/PointerPath.Core.Tests Unit tests (no CAD program needed)
tools/icons/                 Generates the ribbon icons (Python + Pillow)
installer/                   The installer: Inno Setup script, AutoCAD bundle manifest, build script
install/                     For developers: update from source (pull, build, copy) and autoload
docs/architecture.md         Design notes, what is kept in the drawing, first things to test
docs/packaging.md            What the installer installs and where, building it, what is left to do
docs/licensing.md            How the add-in checks its licence, and how to turn the check on
brand/                       Logo, icons and colours for the website and advertising
website/                     The website: accounts, licence sales, the download and the licence API
render.yaml                  How the website is deployed on Render
```

## Installing

Run `PointerPath-<version>-setup.exe`. It asks whether to install for everyone on the PC (needs
an administrator) or just for you, and then:

- **BricsCAD V24 and later**: registers PointerPath with every BricsCAD version and language on
  the PC, so it loads when BricsCAD starts. A BricsCAD installed afterwards isn't registered
  until you run the installer again. BricsCAD V26 and later run add-ins on Microsoft's .NET 8
  Desktop Runtime, which BricsCAD doesn't bring: when it is missing, the installer downloads and
  installs it (installing for all users), or tells you where to get it.
- **AutoCAD 2021 and later** (and Civil 3D, Map 3D and the other AutoCAD-based products): puts
  PointerPath where AutoCAD loads add-ins from by itself, so it also loads in an AutoCAD
  installed afterwards. AutoCAD LT can't run add-ins of this kind.

Start the CAD program and look for the **PointerPath** ribbon tab, or type `PTPATH`. Remove it
from Windows' installed apps. Your saved defaults (`%APPDATA%\PointerPath`) are kept.

For an unattended install: `PointerPath-<version>-setup.exe /VERYSILENT /ALLUSERS` (or
`/CURRENTUSER`), with `/LOG=file` to keep a log.

To build the installer, see [docs/packaging.md](docs/packaging.md).

## Quick start (from source)

### 1. Run the tests (any OS)

Needs the .NET 8 SDK. From the repo folder:

```bash
dotnet test tests/PointerPath.Core.Tests
```

### 2. Build the add-in (Windows, BricsCAD installed)

From the repo folder:

```bash
dotnet build src/PointerPath.BricsCAD -c Release
```

The build finds the newest BricsCAD under `C:\Program Files\Bricsys` and prints which one it
used. V24/V25 build for .NET Framework 4.8 and V26 onwards for .NET 8. To pick a version, add
`-p:BricsCADVersion=25`. If BricsCAD is installed elsewhere, add
`-p:BricsCADDir="D:\Apps\BricsCAD V25 en_US"`. If it can't find BricsCAD, the build stops
with one message listing the BricsCAD folders it did find.

The output goes to `src/PointerPath.BricsCAD/bin/Release/V<version>/`. V26 and later need the
x64 .NET 8 Desktop Runtime on the user's machine.

The AutoCAD add-in is the same source built by another project. It needs no AutoCAD installed
(the AutoCAD API comes from NuGet) and builds on any OS with Microsoft's .NET SDK (the SDK some
Linux distributions package leaves out the Windows desktop parts it needs):

```bash
dotnet build src/PointerPath.AutoCAD -c Release
```

That makes `bin/Release/net48/` for AutoCAD 2021 to 2024 and `bin/Release/net8.0-windows/` for
AutoCAD 2025 and later.

### 3. Load it

In BricsCAD: `NETLOAD`, then pick `PointerPath.BricsCAD.dll` from the output folder. A
**PointerPath** ribbon tab appears, with the commands on its **Vehicles** panel. In AutoCAD it
is the same with `PointerPath.AutoCAD.dll` (AutoCAD asks before loading an add-in from a folder
it doesn't trust).

To load automatically at startup:
`install\Register-Autoload.ps1 -DllPath <full path to dll> -BricsCADVersion 25`.

### 4. Updating after changes

From the repo folder:

```bash
powershell -ExecutionPolicy Bypass -File install\Update.ps1
```

The script does four things:
- pulls the latest code
- builds for the newest BricsCAD installed (BricsCAD can stay open while it builds)
- if the add-in is in use, asks you to close BricsCAD
- copies the build to `C:\PointerPath`

Then start BricsCAD again, and `NETLOAD` unless autoload is registered. Useful options:
- `-BricsCADVersion 25` builds for a specific version.
- `-RegisterAutoload` also sets up loading at startup.
- `-InstallDir D:\Tools\PointerPath` installs somewhere else.
- `-SkipPull` builds what you already have, without pulling.

If `C:\PointerPath\turn-paths.json` was edited, the old copy is saved as `turn-paths.json.bak`.
Defaults saved from the dialogs live in `%APPDATA%\PointerPath` and are not touched.

`install\Register-AutoUpdate.ps1` sets up a Windows scheduled task, "PointerPath auto-update",
that runs the same update when you log in and daily at 07:30. Run it with `-Remove` to turn it off.

## Commands

| Command | Ribbon button | What it does |
|---|---|---|
| `PTPATH` | Turn path | Draw a design vehicle's swept path: a turning template, a vehicle driven along a line you pick, or driven click by click. See [Turning paths](#turning-paths). |
| `PTSOLVE` | Solve turn | Work out a multi-point turn: pick where the vehicle starts and where it ends up, and the movements between the linework on the solve boundary layers are found and drawn. The same as `PTPATH` with **Solve (2 points)** chosen. See [Solving a multi-point turn](#solving-a-multi-point-turn). |
| `PTINOUT` | In and out | Show forward entry and exit from a site: pick where the vehicle arrives, where it stops on the site and where it leaves. The same as `PTPATH` with **Solve in and out (3 points)** chosen. See [In and out of a site](#in-and-out-of-a-site-the-3-point-solve). |
| `PTEDIT` | Edit path | Change a turning path (its vehicle, radius, clearances, layers or geometry) and regenerate it. See [Editing a turning path](#editing-a-turning-path). |
| `PTCLEARANCE` | Clearance | Check a car's ground clearance along a plotted long section, or on a surface along a path. See [Ground clearance](#ground-clearance-appendix-c). |
| `PTLICENCE` | Licence | Sign in with your PointerPath account, see the licence on this computer, or sign out. `PTLICENSE` works too. See [Licence](#licence). |
| `PTRIBBON` | | Add the PointerPath ribbon tab again, if a workspace change removed it. |

## Licence

PointerPath installed from the installer needs a licence, which belongs to a PointerPath account
on the website. A build made from source doesn't ask for one.

- **Signing in.** The first time you run a command, PointerPath asks for your account's email
  address and password. You stay signed in on that computer after that.
- **Without a connection.** PointerPath checks the licence with the website in the background
  when the computer is online, about once a day. Offline, it keeps working for up to two weeks
  from the last check, then asks you to connect.
- **Another computer.** A licence can be in use on a limited number of computers at once. To move
  it, run `PTLICENCE` and choose **Sign out** on the old computer, or sign that computer out on
  your account page on the website, then sign in on the new one.

- **Australia only.** PointerPath is sold for use in Australia. Signing in works only from an
  Australian internet connection, and the licence is not renewed from anywhere else.

What is sent to the website is your email address and password (when you sign in), the
computer's name and Windows user name, and a scrambled identifier for the computer. Nothing from
your drawings is sent. [docs/licensing.md](docs/licensing.md) has the detail.

## Coming from CSD-Bridge

The commands were `AITURN`, `AITURNSOLVE`, `AITURNINOUT`, `AITURNEDIT` and `AICLEARANCE` there.
Two things carry over:

- **Paths already in drawings.** A path drawn by CSD-Bridge can be edited with `PTEDIT`. Once
  edited it is saved under PointerPath's own names in the drawing.
- **Saved defaults.** Until you use **Save as defaults** in PointerPath, a copy saved from
  CSD-Bridge (`%APPDATA%\CsdBridge\turn-paths.json`) is used. PointerPath never writes to it.

## Turning paths

`PTPATH` (ribbon: **Vehicles > Turn path**) opens a dialog, then prompts in the drawing:

| Manoeuvre | What you pick | What it does |
|---|---|---|
| **Standard turn template** | Start point (centre of the rear axle of the parked car) and the direction of travel | A single turn at the nominated radius, measured at the outer front wheel as in Paragraph B3.1 (default the vehicle's minimum: B99 6.3 m, B85 5.8 m), through the turn angle(s) you enter (`90`, or `30,60,90,120,150,180` for a fan like Figures B3 to B6), left or right, with straight travel before and after. |
| **Follow a line, arc or polyline** | The curve, near the end the vehicle starts from | Drives the vehicle along it, forwards (the front axle centre follows the line, the rear axle tracks in behind) or in reverse (the rear axle follows the line). Use it to check a driveway, ramp or aisle you have already drawn. |
| **Drive** | The start point (the centre of the front axle) and direction, then where the vehicle goes next, click by click | Each click is a destination. The car drives there on one arc when that arc is no tighter than the turn radius; otherwise it turns at the turn radius until it points at the destination, then drives straight (the **Drive turns** option makes it always do the latter). A click ahead goes forward, a click behind reverses. With **arrival direction** on (a dialog tick box, or `D` while driving), a second pick sets which way the car faces at the destination, and the leg becomes turn, straight, turn so it arrives exactly on that heading: square into a bay, parallel to an aisle. A leg never turns more than a half turn, so a point inside the turning circle can't be reached in one leg: drive past and reverse. Driving on forwards, a leg starts with the front wheels as the last leg left them, so a turn clicked in several legs is the same turn as one click at its end; after a reverse, and at the start, the wheels are straight and the lock winds on as the car rolls. With **Turn wheels when stationary** ticked, the wheels are instead set before each forward move, so every leg is on its turning circle from the first metre: tighter, and how a tight manoeuvre is usually driven, but it is steering on the spot at each click. Every leg keeps its own swept path, so a three-point turn is three clicks. `U` takes back the last leg, Enter finishes. |

| **Solve (2 points)** | The start position and direction, then the target position and direction | Finds the movements for you: forward and reverse legs from the start to the target that keep the clearance to everything on the solve boundary layers. See [Solving a multi-point turn](#solving-a-multi-point-turn). |
| **Solve in and out (3 points)** | The entry, the stop on the site and the exit (position and direction at each), then optionally the site boundary across the driveway | Finds the way in to the stop and the way out again, so the vehicle enters forwards and leaves forwards. See [In and out of a site](#in-and-out-of-a-site-the-3-point-solve). |

While you pick the direction, or the next destination, the swept path, clearances, driven path and vehicle
are previewed live under the cursor (at a coarser step than the final result), with the earlier legs kept on screen.

What is drawn, each part on its own configurable layer:

- **Swept path**: the envelope of the body, two solid lines from the parked car's rear corners to the
  front corners at the end (or one closed outline). It is the exact boundary of everything the body
  covered, including the rear swinging out at the start of a turn.
- **Clearances**: the swept path offset by the standard's clearances, dashed: 300 mm manoeuvring on
  both sides, plus 300 mm circulation clearance on the outside for the B99 (Paragraph B3.2). The
  values are editable per run.
- **Vehicle** outline with wheels and axles at the start and end, and optionally every *n* metres along the path.
- The **driven path**, optional **wheel paths**, a **label** (e.g. `B99 R6.3 m`) and an optional **hatch** of the swept area.
- Optionally the whole result is made into a **block** (`B99 turning path`, numbered if the name is taken),
  inserted at the vehicle's start point on the Block layer, so it moves, copies and deletes as one. The
  entities keep their own layers inside the block.

The command reports the tightest turn the manoeuvre needed (as an outer-front-wheel radius) against
the vehicle's minimum, and warns when a path is too tight for the vehicle or a template radius is below
the standard's minimum. Reminders from the standard: B99 is required for access driveways, ramps and
circulation roadways; B85 may only be used for parking spaces and aisles (B2.2, B2.3); turns on
circulating roadways should be at least 8 m radius (A4). Aisle widths for parking manoeuvres come
from Tables B2/B3 and Figure 2.2, not from swept paths.

**How it works.** The vehicles are the base dimensions of Figures B1 and B2 (B99: 5.2 × 1.94 m,
wheelbase 3.05 m, overhangs 0.95 m front and 1.2 m rear; B85: 4.91 × 1.87 m, wheelbase 2.8 m, overhangs
0.92 m front and 1.19 m rear), with the effective front and rear overhangs and effective width that the
standard uses for swept paths (rounded corners). The front of a car is its shorter, rounder end. It is
the front wheels that steer, so in reverse at full lock the rear axle backs round the arc and the front
swings out past where the car stood, by about a metre for these cars. The rear axle cannot
slip sideways, so when a point of the vehicle follows the path the heading changes by that point's
sideways movement divided by the wheelbase: the classic tractrix. That is why a template starts with
the car straight and the inner line only gradually tightens, as in the standard's figures. The swept
path is the boundary of the union of the body over every position: every corner's and side's trace,
minus whatever another position of the body covers.

**Drawing units.** The tools work in drawings in metres, millimetres or any other unit. The dialogs
have a **Drawing units** box: *Automatic* reads the drawing's `INSUNITS` (and takes metres when that
isn't set), or choose *Metres* or *Millimetres* yourself; **Save as defaults** keeps the choice.
Everything you type stays in metres whatever the drawing: vehicle sizes, radii, clearances, outline
intervals and the label text height (0.3 is 300 in a millimetre drawing). Points you pick, the kerbs
and walls on the obstruction layers, a line the vehicle follows and surface levels are read in the
drawing's units, and the result is drawn at the drawing's scale. If the units look wrong for the
size of the drawing (a "millimetre" drawing only 300 units across, say) the dialog says so in red
and asks before carrying on.
A path remembers the units it was drawn with; changing them in `PTEDIT` keeps it where it is and
redraws the vehicle at the new scale.

**Configuration.** `turn-paths.json` next to the add-in holds the vehicles (add your own with the
same fields), the drawing units, the default clearances, lead-in/lead-out, text height, what to draw and the layer for
each part (`{vehicle}` in a layer name is replaced by the vehicle's name). Layer styles apply only
when a layer doesn't exist yet. The dialog can change all of it per run; **Save as defaults** writes
`%APPDATA%\PointerPath\turn-paths.json`, which is then used instead of the shipped file (or point
`turn_paths_file` in `%APPDATA%\PointerPath\settings.json` at a shared copy). Vehicles that a saved or shared copy doesn't
have are still taken from the shipped file, so new vehicles appear after an update.

### Trucks

The Austroads rigid design vehicles are included, from the dimension drawings of the *Design Vehicles and
Turning Path Templates Guide* (AP-G34-23, Edition 4.0):

| Vehicle | Guide | Dimensions (m) | Smallest template radius |
|---|---|---|---|
| **Service 8.8 m** | Figure 2.3, service vehicle | 2.3 rear overhang, 5.0 wheelbase, 1.5 front overhang | 9 m |
| **Truck 12.5 m** | Figure 2.4, single unit truck/bus | 3.45, 6.85, 2.2; two rear axles | 12.5 m |

Both are 2.5 m wide. As for the cars, the radius is measured at the outer front wheel, which is how the guide
measures its templates. They default to 0.5 m clearance on both sides (Section 3.4.1: 0.5 m from the swept
path to kerbs, edge lines and objects). The guide's usual choices (Table 2.3): the single unit truck at
12.5 m for collector streets, the service vehicle at 9 m for local residential streets. The track widths
aren't dimensioned in the guide's figures; they are scaled from them and don't move the swept path.

Any other rigid vehicle can be added to `turn-paths.json`. Give the real body; the `effective_*` fields can
be left out, because a square body sweeps with its real corners. With a group of rear axles, measure the
wheelbase and overhangs from the centre of the group.

Turns are tangential, as the guide's templates are: the vehicle arrives straight and the front wheel
follows the arc. There is no limit on how fast the steering is wound on.

**Vehicles with trailers are switched off for now.** The 19 m semi-trailer and the B-doubles are no longer
in the vehicle list, and a vehicle with trailers in your own `turn-paths.json` is left out when the file is
read (the command line says so). They come back once the turning of rigid vehicles is settled. A path
drawn with one can't be edited until then.

### Conflicts with kerbs, walls and columns

Tick **Check against kerbs, walls and columns** in the dialog and name the layers they are on (commas between,
`*` as a wildcard; `A-WALL*` also finds xref layers such as `arch|A-WALL-EXT`). Leave the box blank to pick
objects on those layers after OK. Lines, arcs, circles and polylines on those layers are checked, in the drawing
and inside blocks and xrefs; frozen and off layers are ignored. After drawing, the command reports:

- a **hit** where linework is inside the swept path, with how far inside;
- an **encroachment** where linework is between the swept path and the clearance line, with the gap left and the
  gap required on that side;
- otherwise, how close the nearest obstruction comes to the swept path.

Each conflict is marked on the Conflicts layer: the offending stretch of the obstruction, a ring at the worst
point and a note (`HIT 120 mm`, or `215 mm (needs 300)`).

### Solving a multi-point turn

`PTSOLVE` (ribbon: **Vehicles > Solve turn**, or **Solve (2 points)** in the `PTPATH` dialog) answers "can it
turn round here, and in how many movements?" without you driving it click by click.

1. In the dialog, choose the vehicle, the turn radius (default its minimum) and the clearance, and name the
   **solve boundary layers**: the kerbs, walls, columns and bay lines that bound the space. Leave the layers
   blank to pick objects on them after OK. See [Solve boundary layers](#solve-boundary-layers) for setting the
   office's standard list once.
2. Pick the **start**: the centre of the rear axle, then a point ahead of the car for the way it faces.
3. Pick the **target** the same way: where the rear axle ends up, and the way the car faces there.

The solver then searches for the forward and reverse movements between the two, turning no tighter than the
radius, and draws each movement as its own swept path, with the car shown where it stops between them. It
reports what it found, for example `Solved in 3 movements (forward 7.4 m, reverse 3.3 m, forward 8.6 m)`. It
looks for the fewest movements first, then the shortest driving.

**Clearance.** The clearance (the *inside* box in the dialog; the same is kept on both sides, 300 mm by default,
Paragraph B3.2) is kept between the body and everything on the obstruction layers, for the whole manoeuvre. Two
things can reduce it, and the report always says so:

- Linework that is already closer than the clearance where the car starts or ends, such as the sides of a 2.4 m
  bay, can't have it. That linework keeps the distance it has there, or half the clearance where passing that
  close saves movements. Everything else still keeps the full clearance: `...keeping 300 mm clearance except to
  what is already closer than that at the target, which keeps 150 mm`.
- If there is no way through at all, the clearance is halved, then dropped: `...but only with the clearance
  reduced from 300 to 150 mm`.

The conflict check runs on the result as usual, so wherever the clearance couldn't be kept is marked on the
drawing with the gap that is left. A tight space can take a lot of shuffling at full clearance (reversing a
B85 into a 2.6 m bay off a 6.2 m aisle takes six movements keeping 300 mm to the bay lines, two keeping 150 mm),
so if the answer looks excessive, try a smaller clearance.

If there is no solution the command says why (the car overlaps an obstruction at the start or target, or no
route was found) and draws nothing. Things to know:

- Only lines, arcs, circles and polylines on the solve boundary layers count (in blocks and xrefs too). Anything
  not on those layers is ignored, so an open side of the space needs a line across it, or the car will use it.
- The car may go up to `solver_search_margin` (15 m) beyond the start and target, and the search stops after
  `solver_time_limit` (10 s); both are in `turn-paths.json`. Most manoeuvres solve in under a second.
- The movements are made of full-lock turns and straights, as a careful driver would make them. It finds a
  good manoeuvre, not always the one with the very fewest movements.

### In and out of a site (the 3-point solve)

`PTINOUT` (ribbon: **Vehicles > In and out**, or **Solve in and out (3 points)** in the `PTPATH` dialog)
shows that a vehicle can enter a site forwards and leave it forwards, which is what most councils ask of a
service or waste vehicle. Left to itself, the 2-point solve will happily drive past a driveway and reverse in
off the street. This one will not.

1. Pick the **entry**: where the vehicle arrives on the street, and the way it is travelling.
2. Pick the **stop on the site**: the loading dock, bin store or bay, and the way the vehicle faces there.
3. Pick the **exit**: where the vehicle ends up back on the street, and the way it is travelling.
4. Optionally pick the **site boundary** across the driveway, as two points. Enter skips it.

It then solves two manoeuvres, the way in to the stop and the way out from it, and draws them as one path.
The rules it keeps:

- the first movement on the way in is forwards, and the last movement on the way out is forwards;
- with a boundary line picked, no part of the body is over that line while the vehicle is reversing, so all
  the shunting happens on the site.

Without the boundary line the first and last movements are still forwards, but a long reverse in the middle
could reach back over the boundary, so pick the line whenever the driveway is short. The report gives both
parts, for example `Way in: 2 movements (forward 21.3 m, reverse 7.5 m). Way out: 1 movement (forward 23.3 m).
It drives in forwards and out forwards.` Each movement in the command line report is marked `in` or `out`
(`Leg 2 (in, reverse 7.5 m)`). If either
part can't be done, nothing is drawn and the message says which (`on the way in, ...`).

The clearance, the search limits and the things to know above apply to both parts. Like the 2-point solve it
is for cars and rigid trucks.

### Solve boundary layers

Both solves stay between the linework on the **solve boundary layers**, which are set apart from the conflict
check's layers so the office's standard list can be set once:

- In the dialog, **Solve boundary layers** (under Conflicts) takes the list, separated by commas, with `*` and
  `?` as wildcards: `C-KERB*, A-WALL*, A-COLS, C-BDY*`. **Save as defaults** keeps it.
- Or set `solve_layers` in `turn-paths.json`, which is the place for a shared office copy.

Leave the list empty and a solve uses the conflict check's **Layers** instead, as it did before. Leave both
empty and you are asked to pick objects on the layers after OK. A solved manoeuvre remembers the layers it
was solved between.

### Editing a turning path

Every path remembers how it was made. `PTEDIT` (ribbon: **Vehicles > Edit path**): pick any part of a path,
or its block. The dialog opens with that path's settings; change the vehicle, radius, clearances, what is drawn,
the layers or the conflict layers, then choose:

- **Regenerate** (Enter): redraw it with the new settings and the same geometry;
- **Pick**: pick the start, direction, line or destinations again;
- **Continue** (drive): add more legs from where the drive ended;
- **Solve** (solved manoeuvres): solve it again, for when the kerbs or walls around it have changed.

A solved manoeuvre is also solved again by itself when you change its vehicle, radius, clearance or solve
boundary layers, pick new positions, or regenerate a block that has been moved.

The old version is only erased once the new one has been worked out, so a change that can't be driven (a bigger
vehicle that can't reach a destination, say) leaves the path as it was and says which destination failed. A path
drawn as a block that has since been moved or rotated is regenerated where the block now is. Paths drawn before
this version carry no settings and can't be edited: draw them again.

### Ground clearance (Appendix C)

`PTCLEARANCE` (ribbon: **Vehicles > Clearance**) checks that the car's underside clears a grade change: a
ramp, hump, dip or gutter. The template is Figure C1: 120 mm clearance under the body between the axles,
and an underside rising at the 10° approach and departure angles over the overhangs (Paragraph B5). It is
placed with both wheels on the ground and slid along in small steps; it must never fall below the ground.

- **2D, on a plotted long section**: select the plotted profile polyline and give its vertical exaggeration
  (and its horizontal scale, if it isn't plotted at natural scale: 1 m of chainage drawn 1 m long in the
  drawing's units). The template is run both ways along the
  section, since the two overhangs differ. One line shows the lowest the underside ever gets along the whole
  section (it must stay above the ground everywhere); the template is drawn at its worst position in each
  direction, the ground it touches is marked, and a label gives the smallest gap (or how far it grounds).
- **3D, on a surface**: select the line or polyline the vehicle drives (forwards or in reverse). At every
  position the surface is sampled along each side of the car, from the rear extremity through both wheels to
  the front extremity, and the template is placed on that side's wheels. Grounding spots are ringed on the plan,
  and each wheel path can be plotted as a long section with the underside line and the template at its worst
  position. It reads
  BricsCAD TIN surfaces; for a Civil Site Design surface, make a BricsCAD copy with CSD's Surface Exchange first.
- The clearance and angles are editable per run (Appendix C, Note: lower the underside for a lesser clearance),
  and saved with the other defaults. Layers: templates and plots, where it grounds, labels.

## Troubleshooting

| Problem | Fix |
|---|---|
| The build stops with "BricsCAD V.. API files (BrxMgd.dll, TD_Mgd.dll) were not found" | Build with `-p:BricsCADVersion=NN` to pick an installed version, or `-p:BricsCADDir="full path to your BricsCAD folder"`. |
| `NU1100` or `NU1101`: a package can't be found | NuGet can't reach nuget.org. The repo's `nuget.config` adds it as a source. If the error persists, run `dotnet nuget enable source nuget.org`. |
| The ribbon tab is missing | Run `PTRIBBON`. If the ribbon itself is off, turn it on with `RIBBON` first. The commands work without it. |
| "Sign in with your PointerPath account to use this command" | Run `PTLICENCE` and sign in. If it says the licence is in use on other computers, sign one of them out on your account page. |
| "PointerPath has not been able to check your licence since ..." | The computer has been offline for longer than a licence lasts. Connect to the internet and run the command again. |
| "The turning path conventions couldn't be loaded" | The message names the file and the problem, usually a typo in an edited `turn-paths.json`. The built-in B99 and B85 are used until it is fixed. |
