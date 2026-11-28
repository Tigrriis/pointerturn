# Packaging PointerPath

One installer, `PointerPath-<version>-setup.exe`, for BricsCAD and AutoCAD. It holds four builds
of the add-in and decides nothing at install time: every build is installed, and each CAD program
loads the one meant for it.

| Build | For | Runs on |
|---|---|---|
| `BricsCAD\net48\PointerPath.BricsCAD.dll` | BricsCAD V24 and V25 | .NET Framework 4.8 |
| `BricsCAD\net8\PointerPath.BricsCAD.dll` | BricsCAD V26 and later | .NET 8 |
| `PointerPath.bundle\Contents\net48\PointerPath.AutoCAD.dll` | AutoCAD 2021 to 2024 | .NET Framework 4.8 |
| `PointerPath.bundle\Contents\net8\PointerPath.AutoCAD.dll` | AutoCAD 2025 and later | .NET 8 |

Each DLL sits with `PointerPath.Core.dll`, `turn-paths.json` and, for .NET Framework, the
`System.Text.Json` libraries. No debug symbols are shipped.

## What is installed, and where

The installer asks whether to install for everyone on the PC or just the current user.

| | All users (needs an administrator) | Just me |
|---|---|---|
| BricsCAD builds, uninstaller | `C:\Program Files\PointerPath` | `%LOCALAPPDATA%\Programs\PointerPath` |
| AutoCAD bundle | `%ProgramData%\Autodesk\ApplicationPlugins\PointerPath.bundle` | `%APPDATA%\Autodesk\ApplicationPlugins\PointerPath.bundle` |
| BricsCAD registry entries | under `HKEY_LOCAL_MACHINE` | under `HKEY_CURRENT_USER` |

Nothing is written to `%APPDATA%\PointerPath`, where the add-in keeps a user's saved defaults, and
uninstalling leaves that folder alone.

### How AutoCAD finds it

AutoCAD, and the products built on it, load every `.bundle` folder in an `ApplicationPlugins`
folder when they start. `PackageContents.xml` in the bundle says which DLL goes with which
release: `R24.0` to `R24.3` (2021 to 2024) get the .NET Framework build, `R25.0` (2025) and later
the .NET 8 build. Nothing is registered, so an AutoCAD installed after PointerPath loads it too.

### How BricsCAD finds it

BricsCAD is told in the registry, with the entry its developer reference describes for loading an
add-in at startup. The installer writes one for every BricsCAD version (V24 and later) and
language it finds, looking in both `HKEY_LOCAL_MACHINE` and `HKEY_CURRENT_USER`:

```
Software\Bricsys\BricsCAD\V25x64\en_US\Applications\PointerPath
    LOADER      C:\Program Files\PointerPath\BricsCAD\net48\PointerPath.BricsCAD.dll
    LOADCTRLS   2      load at startup
    MANAGED     1      a .NET assembly
    DESCRIPTION PointerPath (vehicle turning paths)
```

V26 and later get the `net8` path. Uninstalling removes the entries, and any registry key that
registering created and nothing else uses.

BricsCAD does have an AutoCAD-style bundle loader (`APPAUTOLOADER`), but reports of it working
are mixed, so it isn't relied on. The cost of the registry is that **a BricsCAD installed after
PointerPath isn't registered until the installer is run again.**

### The .NET 8 Desktop Runtime

BricsCAD V26 and later run add-ins on Microsoft's .NET 8 Desktop Runtime (x64) and don't install
it. When such a BricsCAD is found and the runtime isn't (no
`C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\8.*`), an all-users install downloads
it from Microsoft (`aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe`) and runs it quietly.
A just-me install can't, and says so. If the download fails, PointerPath is still installed and
the last page says where to get the runtime. AutoCAD 2025 and later install their own.

### Unattended installs

```
PointerPath-<version>-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /ALLUSERS /LOG=C:\Temp\pointerpath.log
```

`/CURRENTUSER` instead of `/ALLUSERS` installs for one user without an administrator. The log
records which BricsCAD versions were registered and ends with the same summary the last page shows.

## Building the installer

On a Windows PC with the .NET 8 SDK, from the repo folder:

```
powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
```

It runs the unit tests, builds the four add-ins, lays them out in `artifacts\stage`, and compiles
`installer\PointerPath.iss` into `artifacts\PointerPath-<version>-setup.exe`. It prints the
file's SHA-256 and which builds are in it.

What it needs:

- **For the AutoCAD builds, nothing.** The AutoCAD API comes from Autodesk's `AutoCAD.NET` NuGet
  package: 24.0.0 (AutoCAD 2021) for .NET Framework, 25.0.1 (AutoCAD 2025) for .NET 8.
- **For the BricsCAD builds, BricsCAD.** Its API files, `BrxMgd.dll` and `TD_Mgd.dll`, only come
  with BricsCAD. The .NET Framework build needs a V24 or V25 under `C:\Program Files\Bricsys`, the
  .NET 8 build a V26 or later; the oldest of each is used. Or pass `-BricsCADNet48Dir` and
  `-BricsCADNet8Dir` with any folder that holds the two files.
- **Inno Setup, fetched for you.** The first run downloads the pinned version (6.7.3) from NuGet
  (package `Tools.InnoSetup`) into `artifacts\tools`. Pass `-ISCC` to use another.

A release has to hold all four builds, so the script stops if a BricsCAD build can't be made.
`-AllowMissing` builds the installer without it, for trying things out: that installer leaves the
BricsCAD versions it has no build for unregistered. `-SkipTests` skips the unit tests.

The script also runs under PowerShell 7 on Linux or macOS with Wine (for Inno Setup), which is
how the installer was developed. There the BricsCAD builds need the two folder options.

### A new version

Set `<Version>` in `Directory.Build.props` (or pass `-Version 1.2.0`) and build. The version goes
on the DLLs, the bundle and the installer. Running a newer installer over an older one upgrades in
place; it is the `AppId` in `PointerPath.iss` that makes Windows see them as the same product, so
that must never change. Neither must the `UpgradeCode` in `PackageContents.xml`.

## Signing

Without a signature Windows shows "Unknown publisher" when the installer runs, SmartScreen warns
on download, and some companies' policies block it. The build signs when it is given the command
that signs one file:

```powershell
.\installer\Build-Installer.ps1 -SignCommand '<the sign command, with $f where the file goes>'
```

or, set once on the build PC, in the environment variable `POINTERPATH_SIGN_COMMAND`. Without
either, nothing is signed, and the build says so at the end.

With a sign command the build:

1. **Signs PointerPath's own DLLs** (`PointerPath.Core.dll`, `PointerPath.BricsCAD.dll`,
   `PointerPath.AutoCAD.dll`) in every build, as they are staged. The other DLLs beside them are
   Microsoft's and already signed.
2. **Has Inno Setup sign the uninstaller and then the installer** with the same command
   (`SignTool=sign` in `PointerPath.iss`).
3. **Checks every signature** and stops if a file came out unsigned. It warns if a signature has
   no timestamp, or isn't trusted on the build PC (a test certificate).
4. **Says who signed it** at the end, beside the SHA-256.

The command is whatever the certificate's provider says to run. Three things about it:

- **`$f` is the file.** It is put in quotes for you. Give the whole command in single quotes, so
  PowerShell leaves the `$f` alone.
- **It must start with a program** (`signtool`, or the full path of its `.exe`), not a batch file:
  Inno Setup runs it directly.
- **Keep the timestamp** (`/tr` and `/td` with signtool). A signature without one stops counting
  the day the certificate expires.

Two examples, to check against the provider's current instructions:

```powershell
# A certificate on a hardware token or in the Windows certificate store, picked by its name:
-SignCommand 'signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /n "Arete Engineering Pty Ltd" $f'

# Azure Artifact Signing (formerly Trusted Signing), after az login:
-SignCommand 'signtool sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib "C:\Tools\ArtifactSigning\Azure.CodeSigning.Dlib.dll" /dmdf "C:\Tools\ArtifactSigning\metadata.json" $f'
```

Neither holds a secret: a token asks for its PIN, and Azure signs in separately. Don't put a
password in the command.

Signing needs Windows (`signtool` comes with the Windows SDK), so a signed installer can't be
built through Wine.

**The certificate.** One in the company's name, from Azure Artifact Signing if it will take an
Australian company, otherwise an OV code-signing certificate from a certificate authority, kept
on a hardware token or the authority's signing service. An EV certificate no longer gets past
SmartScreen any sooner. Either way SmartScreen may still warn for a while: a new certificate has
no reputation until its downloads have built one.

## Checked, and not

The installer was built and run under Wine, with registry keys made up to look like BricsCAD V23
to V26 and AutoCAD 2024 and 2025. Checked there:

- all-users and just-me installs put the files and registry entries where the tables above say;
- BricsCAD V23 is skipped, V24 and V25 get the .NET Framework build and V26 the .NET 8 build, for
  each language;
- the runtime is downloaded and run when missing (from a local stand-in server), skipped when
  present, and a failed download doesn't stop the install;
- installing over an existing install replaces it and clears out files no longer shipped;
- uninstalling removes every file and leaves the registry exactly as it was before installing.

Since then the installer has been built on Windows, with BricsCAD V26's API files, and run
there: it installed into a real BricsCAD V26, which loaded the add-in and ran its commands.
That is the one case checked on Windows.

Not checked, because it needs Windows and the real programs:

1. **The other programs loading it.** BricsCAD V24 and V25 (the .NET Framework build) and
   AutoCAD have not loaded it. Upgrading over an install and uninstalling have not been tried
   on Windows.
2. **The registry layout.** `Software\Bricsys\BricsCAD\V25x64\en_US` is taken from the existing
   `Register-Autoload.ps1` and the developer reference. Confirm it in `regedit` for V24, V25 and
   V26, and that BricsCAD loads from the `HKEY_LOCAL_MACHINE` entry as well as the
   `HKEY_CURRENT_USER` one.
3. **One build for several versions.** The .NET Framework BricsCAD build, compiled against the
   oldest of V24 and V25 on the build PC, is assumed to load in both; the .NET 8 build, compiled
   against V26, in later versions. The same is assumed of AutoCAD within 2021 to 2024 and from
   2025 on, which is Autodesk's stated compatibility.
4. **Closing a running CAD program.** If BricsCAD or AutoCAD has PointerPath loaded, Windows
   holds its files open. Setup should list the program and offer to close it.
5. **The real runtime download**, through a company proxy in particular.

## Not done yet

- **A code-signing certificate.** The build can sign (see [Signing](#signing)), but there is no
  certificate yet, so releases are unsigned, and the signing step has only been run against
  stand-ins: not yet with a real certificate, signtool, or Inno Setup's own signing.
- **A licence agreement** page in the installer.
- **Accounts and licensing.** The add-in runs for anyone who installs it.
- **Updates.** A user gets a new version by running a new installer.
- **A build server.** A hosted Windows build can make the AutoCAD half, but the BricsCAD half
  needs BricsCAD's API files, which can't go in the repository.
