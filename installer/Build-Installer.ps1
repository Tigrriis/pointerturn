<#
.SYNOPSIS
  Build the PointerPath installer: artifacts\PointerPath-<version>-setup.exe.

.DESCRIPTION
  Run from the repo folder, on a PC with the .NET 8 SDK:

    powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1

  1. Runs the unit tests.
  2. Builds the add-in four times:
       AutoCAD   .NET Framework 4.8   AutoCAD 2021 to 2024
       AutoCAD   .NET 8               AutoCAD 2025 and later
       BricsCAD  .NET Framework 4.8   BricsCAD V24 and V25
       BricsCAD  .NET 8               BricsCAD V26 and later
     AutoCAD's API comes from NuGet, so AutoCAD needn't be installed. BricsCAD's API files
     (BrxMgd.dll, TD_Mgd.dll) only come with BricsCAD, so each BricsCAD build needs a BricsCAD
     of its kind on this PC (or a folder holding those two files, see -BricsCADNet48Dir and
     -BricsCADNet8Dir). Each build is compiled against the oldest BricsCAD of its kind found.
  3. Lays the builds out in artifacts\stage the way the installer installs them.
  4. Compiles installer\PointerPath.iss with Inno Setup, which is fetched from NuGet into
     artifacts\tools the first time (nothing to install).

  A release must hold all four builds. To try the installer on a PC without both kinds of
  BricsCAD, add -AllowMissing: the builds that can't be made are left out, and that installer
  does nothing for the BricsCAD versions it has no build for.

.EXAMPLE
  .\installer\Build-Installer.ps1

.EXAMPLE
  .\installer\Build-Installer.ps1 -Version 1.2.0

.EXAMPLE
  .\installer\Build-Installer.ps1 -BricsCADNet48Dir "D:\Apps\BricsCAD V25 en_US" -BricsCADNet8Dir "D:\Apps\BricsCAD V26 en_US"

.EXAMPLE
  .\installer\Build-Installer.ps1 -AllowMissing -SkipTests

.EXAMPLE
  .\installer\Build-Installer.ps1 -Unlicensed
#>
[CmdletBinding()]
param(
    # The version to stamp on the add-in and the installer. Default: <Version> in Directory.Build.props.
    [string]$Version,

    # A BricsCAD V24 or V25 folder (or any folder with its BrxMgd.dll and TD_Mgd.dll), for the .NET Framework build.
    [string]$BricsCADNet48Dir,

    # A BricsCAD V26 or later folder (or any folder with its BrxMgd.dll and TD_Mgd.dll), for the .NET 8 build.
    [string]$BricsCADNet8Dir,

    # Build the installer even if a BricsCAD build can't be made on this PC. Not for a release.
    [switch]$AllowMissing,

    # Don't run the unit tests first.
    [switch]$SkipTests,

    # Build an installer whose add-in doesn't ask for a licence (no licence server and key are
    # compiled in). Not for a release.
    [switch]$Unlicensed,

    # Inno Setup's command-line compiler, ISCC.exe. Default: the pinned version from NuGet.
    [string]$ISCC,

    # Where the installer goes. Default: artifacts, in the repo.
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

# The Inno Setup fetched from NuGet (package Tools.InnoSetup) when -ISCC isn't given.
$innoSetupVersion = "6.7.3"

$onWindows = $env:OS -eq "Windows_NT"
$repo = Split-Path -Parent $PSScriptRoot
$artifacts = [IO.Path]::Combine($repo, "artifacts")
if (-not $OutputDir) { $OutputDir = $artifacts }
$stage = [IO.Path]::Combine($artifacts, "stage")
$bundle = [IO.Path]::Combine($stage, "AutoCAD", "PointerPath.bundle")

function Write-Step([string]$text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }

function Stop-Build([string]$text) {
    Write-Host ""
    Write-Host $text -ForegroundColor Red
    exit 1
}

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { Stop-Build "dotnet $($args[0]) failed (see above). No installer was built." }
}

# Copies a build's output to the staging folder, without the debug symbols.
function Copy-Build([string]$from, [string]$to, [string]$dll) {
    if (-not (Test-Path ([IO.Path]::Combine($from, $dll)))) { Stop-Build "The build finished but $dll is not in $from." }
    New-Item -ItemType Directory -Path $to -Force | Out-Null
    Copy-Item -Path ([IO.Path]::Combine($from, "*")) -Destination $to -Recurse -Force
    Get-ChildItem -Path $to -Recurse -Filter "*.pdb" | Remove-Item -Force
}

# A BricsCAD folder is usable when it holds the two API files the add-in compiles against.
function Test-BricsCADApi([string]$dir) {
    return $dir -and (Test-Path ([IO.Path]::Combine($dir, "BrxMgd.dll"))) -and (Test-Path ([IO.Path]::Combine($dir, "TD_Mgd.dll")))
}

# The installed BricsCAD versions that have the API files: objects with Version and Dir, oldest first.
function Get-BricsCAD {
    $found = @()
    $roots = @()
    if ($env:ProgramW6432) { $roots += [IO.Path]::Combine($env:ProgramW6432, "Bricsys") }
    if ($env:ProgramFiles) { $roots += [IO.Path]::Combine($env:ProgramFiles, "Bricsys") }
    foreach ($root in ($roots | Select-Object -Unique)) {
        if (-not (Test-Path $root)) { continue }
        foreach ($dir in (Get-ChildItem -Path $root -Directory -Filter "BricsCAD V*")) {
            if ($dir.Name -match "V(\d+)" -and (Test-BricsCADApi $dir.FullName)) {
                $found += New-Object PSObject -Property @{ Version = [int]$Matches[1]; Dir = $dir.FullName }
            }
        }
    }

    return @($found | Sort-Object Version)
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Stop-Build "The .NET SDK (dotnet) is not installed or not on PATH." }

# ---------------------------------------------------------------- version
if (-not $Version) {
    $props = Get-Content ([IO.Path]::Combine($repo, "Directory.Build.props")) -Raw
    if ($props -notmatch "<Version>\s*([^<\s]+)\s*</Version>") { Stop-Build "Directory.Build.props has no <Version>. Pass -Version." }
    $Version = $Matches[1]
}

if ($Version -notmatch "^\d+\.\d+\.\d+$") { Stop-Build "The version must be three numbers, like 1.2.0 (got '$Version')." }
$versionProp = "-p:Version=$Version"
Write-Host "PointerPath $Version"

# ---------------------------------------------------------------- licence
# The installer's add-in asks for a licence: it is built with -p:Licensed=true, which compiles
# in the licence server and key from LicenceBuild.cs. -Unlicensed leaves them out.
$licensedProp = "-p:Licensed=true"
if ($Unlicensed) {
    $licensedProp = "-p:Licensed=false"
    Write-Warning "This installer's add-in does not ask for a licence (-Unlicensed). Not for a release."
} else {
    $licenceBuild = Get-Content ([IO.Path]::Combine($repo, "src", "PointerPath.Core", "Licensing", "LicenceBuild.cs")) -Raw
    $licensedPart = ($licenceBuild -split "#else")[0]
    $unset = @("Website", "PublicKeyX", "PublicKeyY") | Where-Object { $licensedPart -notmatch "const string $_ = `"[^`"]+`";" }
    if ($unset) {
        Stop-Build ("src\PointerPath.Core\Licensing\LicenceBuild.cs has no value for " + ($unset -join ", ") + " in its licensed part. Fill them in (see docs/licensing.md), or pass -Unlicensed.")
    }
}

# ---------------------------------------------------------------- tests
if (-not $SkipTests) {
    Write-Step "Running the unit tests"
    Invoke-DotNet test ([IO.Path]::Combine($repo, "tests", "PointerPath.Core.Tests")) -c Release -nologo -v q
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$built = @()
$missing = @()

# ---------------------------------------------------------------- AutoCAD
Write-Step "Building for AutoCAD (2021 to 2024, and 2025 and later)"
$autocad = [IO.Path]::Combine($repo, "src", "PointerPath.AutoCAD")
Invoke-DotNet build $autocad -c Release -nologo -v q $versionProp $licensedProp
$autocadOut = [IO.Path]::Combine($autocad, "bin", "Release")
Copy-Build ([IO.Path]::Combine($autocadOut, "net48")) ([IO.Path]::Combine($bundle, "Contents", "net48")) "PointerPath.AutoCAD.dll"
Copy-Build ([IO.Path]::Combine($autocadOut, "net8.0-windows")) ([IO.Path]::Combine($bundle, "Contents", "net8")) "PointerPath.AutoCAD.dll"
$built += "AutoCAD 2021 to 2024", "AutoCAD 2025 and later"

# The bundle's manifest. Its product code is made from the version, so it is the same every
# time a version is built and different for every version.
$md5 = [Security.Cryptography.MD5]::Create()
$productCode = (New-Object Guid (, $md5.ComputeHash([Text.Encoding]::UTF8.GetBytes("PointerPath $Version")))).ToString().ToUpperInvariant()
$manifest = Get-Content ([IO.Path]::Combine($PSScriptRoot, "PackageContents.xml")) -Raw
$manifest = $manifest.Replace("@VERSION@", $Version).Replace("@PRODUCTCODE@", $productCode)
[IO.File]::WriteAllText([IO.Path]::Combine($bundle, "PackageContents.xml"), $manifest, (New-Object Text.UTF8Encoding $false))

# Every build the manifest tells AutoCAD to load has to be in the bundle.
foreach ($module in [regex]::Matches($manifest, 'ModuleName="\./([^"]+)"')) {
    $file = [IO.Path]::Combine($bundle, $module.Groups[1].Value)
    if (-not (Test-Path $file)) { Stop-Build "PackageContents.xml names $($module.Groups[1].Value), which is not in the bundle." }
}

# ---------------------------------------------------------------- BricsCAD
$installed = Get-BricsCAD
$bricscad = [IO.Path]::Combine($repo, "src", "PointerPath.BricsCAD")
$kinds = @(
    @{ Name = "BricsCAD V24 and V25"; Folder = "net48"; Dir = $BricsCADNet48Dir; Default = 25; Param = "-BricsCADNet48Dir"
       Pick = { param($v) $v -ge 24 -and $v -le 25 } },
    @{ Name = "BricsCAD V26 and later"; Folder = "net8"; Dir = $BricsCADNet8Dir; Default = 26; Param = "-BricsCADNet8Dir"
       Pick = { param($v) $v -ge 26 } }
)
foreach ($kind in $kinds) {
    Write-Step "Building for $($kind.Name)"
    $dir = $kind.Dir
    $api = $kind.Default
    if ($dir) {
        if (-not (Test-BricsCADApi $dir)) { Stop-Build "$($kind.Param): BrxMgd.dll and TD_Mgd.dll are not in '$dir'." }
        if ((Split-Path -Leaf $dir) -match "V(\d+)" -and (& $kind.Pick ([int]$Matches[1]))) { $api = [int]$Matches[1] }
    }
    else {
        $match = @($installed | Where-Object { & $kind.Pick $_.Version }) | Select-Object -First 1
        if ($match) {
            $dir = $match.Dir
            $api = $match.Version
        }
    }

    if (-not $dir) {
        $missing += $kind.Name
        Write-Host "No $($kind.Name -replace ' and later', ' or later' -replace ' and ', ' or ') was found on this PC, so this build can't be made." -ForegroundColor Yellow
        Write-Host "Install one, or pass $($kind.Param) with a folder that holds its BrxMgd.dll and TD_Mgd.dll." -ForegroundColor Yellow
        continue
    }

    Write-Host "Using the BricsCAD V$api API in $dir"
    Invoke-DotNet build $bricscad -c Release -nologo -v q $versionProp $licensedProp "-p:BricsCADVersion=$api" "-p:BricsCADDir=$dir"
    Copy-Build ([IO.Path]::Combine($bricscad, "bin", "Release", "V$api")) ([IO.Path]::Combine($stage, "BricsCAD", $kind.Folder)) "PointerPath.BricsCAD.dll"
    $built += $kind.Name
}

if ($missing.Count -gt 0 -and -not $AllowMissing) {
    Stop-Build "No installer was built: it would be missing the add-in for $($missing -join ' and for '). See above, or add -AllowMissing to build it without (not for a release)."
}

# ---------------------------------------------------------------- Inno Setup
Write-Step "Building the installer"
if ($ISCC) {
    if (-not (Test-Path $ISCC)) { Stop-Build "-ISCC: '$ISCC' doesn't exist." }
}
else {
    $tool = [IO.Path]::Combine($artifacts, "tools", "innosetup-$innoSetupVersion")
    $ISCC = [IO.Path]::Combine($tool, "tools", "ISCC.exe")
    if (-not (Test-Path $ISCC)) {
        Write-Host "Getting Inno Setup $innoSetupVersion from NuGet"
        New-Item -ItemType Directory -Path $tool -Force | Out-Null
        $package = [IO.Path]::Combine($tool, "innosetup.zip")
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        try {
            Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/tools.innosetup/$innoSetupVersion/tools.innosetup.$innoSetupVersion.nupkg" -OutFile $package -UseBasicParsing
            Expand-Archive -Path $package -DestinationPath $tool -Force
        }
        catch {
            Stop-Build "Couldn't get Inno Setup from NuGet: $($_.Exception.Message). Install Inno Setup 6 and pass -ISCC with the path of its ISCC.exe."
        }
        finally {
            if (Test-Path $package) { Remove-Item $package -Force }
        }
    }
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$script = [IO.Path]::Combine($PSScriptRoot, "PointerPath.iss")
$setup = [IO.Path]::Combine($OutputDir, "PointerPath-$Version-setup.exe")
if (Test-Path $setup) { Remove-Item $setup -Force }

if ($onWindows) {
    & $ISCC /Qp "/DAppVersion=$Version" "/DStageDir=$stage" "/DOutputDir=$OutputDir" $script
}
else {
    # Inno Setup is a Windows program: elsewhere it runs under Wine, which wants Windows paths.
    if (-not (Get-Command wine -ErrorAction SilentlyContinue)) { Stop-Build "Inno Setup needs Windows, or Wine: 'wine' is not on PATH." }
    $stageW = (& winepath -w $stage)
    $outputW = (& winepath -w $OutputDir)
    & wine $ISCC /Qp "/DAppVersion=$Version" "/DStageDir=$stageW" "/DOutputDir=$outputW" (& winepath -w $script)
}

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $setup)) { Stop-Build "Inno Setup failed (see above). No installer was built." }

# ---------------------------------------------------------------- done
$size = [Math]::Round((Get-Item $setup).Length / 1MB, 1)
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host ""
Write-Host "Done: $setup ($size MB)" -ForegroundColor Green
Write-Host "SHA-256: $hash"
Write-Host "Holds the add-in for: $($built -join '; ')."
if ($missing.Count -gt 0) {
    Write-Host "NOT a release: it has no add-in for $($missing -join ' or for ')." -ForegroundColor Yellow
}
