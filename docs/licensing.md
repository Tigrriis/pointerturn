# Licensing

How PointerTurn checks that it is licensed. The website side (accounts, sales, the licence API)
is in [website/README.md](../website/README.md); this is the add-in side.

## What the user sees

- The first time a drawing command (`PTPATH`, `PTSOLVE`, `PTINOUT`, `PTEDIT`, `PTCLEARANCE`) is
  run on a computer, a dialog asks for the email address and password of a PointerTurn account.
  Signing in succeeds if the account has an active licence with a free place.
- After that the commands just run. The computer stays signed in across restarts.
- `PTLICENCE` (ribbon: **Account > Licence**) shows who is signed in and until when PointerTurn
  works without a connection, and signs the computer out. `PTLICENSE` is the same command.
- `PTRIBBON` needs no licence.

## How it works

Signing in sends the email, password, an identifier for the computer and a name for it to the
website (`POST /api/v1/activate`). The website sends back two things, which are saved in
`%LOCALAPPDATA%\PointerTurn\licence.json`. The password is not kept.

- **A signed licence.** A small signed statement of which account and which computer it is for,
  and when it runs out: 14 days from when it was issued, unless the website is set otherwise.
  PointerTurn checks the signature against a public key compiled into it, so it needs no
  connection to know the licence is real.
- **An activation token.** What "signed in" means. PointerTurn trades it for a fresh signed
  licence (`POST /api/v1/refresh`) in the background, when it loads and when a command runs, if
  the one it holds is more than 20 hours old.

So a computer that is online every so often never notices. One that is offline keeps working
until the licence it holds runs out, then asks to be connected.

A command is allowed to run when the saved licence has a valid signature, is for this computer,
and has not run out. If it has run out but the computer is signed in, PointerTurn tries one
refresh before asking for the password again.

The licence is given up straight away, without waiting for it to run out, when a refresh is
answered with:

- `invalid_activation`: the computer was signed out from the account page. It is signed out here.
- `licence_inactive`: the licence ended (cancelled, refunded). The computer stays signed in, so
  the next refresh after a renewal brings the licence back without the password.

With no connection, or an answer that makes no sense, nothing changes.

The website can be set to offer PointerTurn in some countries only (Australia, as deployed).
Signing in from anywhere else is refused. A computer already signed in that asks for a fresh
licence from elsewhere is refused one (`region_not_allowed`) but keeps the one it holds until
it runs out, so a short trip abroad doesn't cut anyone off. The add-in itself doesn't try to
work out where it is: the website does, from the connection.

Two details:

- **The computer's identifier** is a hash of Windows' machine GUID and the Windows user, so
  nothing about the PC is sent, and each Windows user on a shared PC takes their own place on
  the licence. The name shown on the account page is the computer's name and the user's name.
- **The clock.** The latest time PointerTurn has seen is saved with the licence. Setting the
  clock back to before it does not make an expired licence work again.

## The code

| | |
|---|---|
| `src/PointerTurn.Core/Licensing/LicenceBuild.cs` | The website's address and public key this build is tied to |
| `…/SignedLicence.cs` | Reads a signed licence and checks its signature (ES256) |
| `…/LicenceStore.cs` | The saved file |
| `…/LicenceServer.cs` | The three calls to the website |
| `…/LicenceManager.cs` | Where a computer stands; signing in, refreshing, signing out |
| `src/PointerTurn.BricsCAD/Licence.cs` | The check in front of the commands, the computer's identifier, the background refresh |
| `src/PointerTurn.BricsCAD/LicenceDialog.cs` | The sign-in dialog |

Everything in Core is unit-tested (`tests/PointerTurn.Core.Tests/LicensingTests.cs`), including
reading a licence signed the way the website signs them.

## Turning it on

A build asks for a licence only when `LicenceBuild.cs` has all three values:

```csharp
public const string Website = "https://your-site";   // no trailing slash
public const string PublicKeyX = "…";
public const string PublicKeyY = "…";
```

`PublicKeyX` and `PublicKeyY` are the `x` and `y` of the website's licence key: printed by
`npm run licence-key` when the key is made, and served by the running website at
`/api/v1/licence-key`. They are the public half of the key and are safe to commit.

While they are empty the add-in runs without a licence, which is how it is built for
development. `installer/Build-Installer.ps1` refuses to build an installer in that state unless
it is passed `-Unlicensed`.

The website's key must not change once builds are in customers' hands: a build only accepts
licences signed by the key compiled into it.

## Limits

- This keeps honest users honest. The check runs on the user's computer, so someone determined
  can patch it out of the add-in; the add-in is not obfuscated.
- The saved file is not encrypted. Copied to another computer it does nothing there: the signed
  licence names the computer it is for, and the website issues fresh ones only for that computer.
- A company buying for several people needs one account, and one licence, per person. There is
  no way yet to buy several places on one account and hand them out.
