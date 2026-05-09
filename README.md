# MOZA Star Citizen Link

Windows desktop app that watches Star Citizen `Game.log` and maps selected game events to force-feedback effects on a MOZA AB6 FFB flight base.

Current first-pass effects:

- Quantum spool vibration
- Landing and impact bumps
- In-atmosphere vibration

## Current Status

The working hardware path is Windows DirectInput force feedback. The AB6 should appear to Windows as:

```text
MOZA AB6 FFB Base
```

The MOZA SDK runtime is bundled by the package script when available, but the SDK force-feedback APIs in the tested SDK are wheelbase-oriented (`createWheelbaseET...`) and currently return `NODEVICES` for the AB6. For the AB6, use `Run-Auto.cmd` or `Run-DirectInput.cmd`.

## Download And Run

For normal users, download the portable release ZIP from this repo's GitHub Releases page. Do not use GitHub's "Source code" ZIP if you only want to run the app.

Extract the portable ZIP and run:

```text
Run-Auto.cmd
```

If needed, the ZIP also includes launchers for specific output paths:

- `Run-Auto.cmd` - recommended; prefers DirectInput AB6 output
- `Run-DirectInput.cmd` - force Windows DirectInput
- `Run-MozaSdk.cmd` - force the MOZA SDK path; not currently expected to work with AB6
- `Run-Preview.cmd` - no hardware output; useful for parser/UI testing
- `Run-Screen.cmd` - experimental DirectInput mode with screen-capture impact detection enabled

No installer is required. The release build is self-contained and does not require users to install the .NET runtime.

## Star Citizen Log Setup

The app tries to auto-detect `Game.log` in common Star Citizen install locations. If your game is installed somewhere else, use `Browse` once and select the file manually.

When monitoring starts, the app tails new log lines from that point forward. It does not replay old log lines.

## Experimental Screen Capture

`Run-Screen.cmd` enables `MOZA_SC_SCREEN=1`. In this mode the app samples the visible Star Citizen window and looks for sudden visual impact flashes or screen-motion changes, then maps those candidates to the normal landing/impact bump effect.

The same experimental screen path also looks for the in-atmosphere altimeter HUD shape. When the altimeter is detected for several consecutive frames it starts the in-atmosphere rumble; when the altimeter is absent for several seconds it stops the rumble.

This does not read Star Citizen memory or inject into the game process. It is experimental and may need threshold tuning for different displays, graphics settings, and HUD scenes.

## Diagnostics

Click `Refresh` in the app to see:

- Selected output mode
- DirectInput game controllers
- DirectInput force-feedback devices
- Log file path
- Bundled MOZA SDK runtime status

For AB6 output to work, diagnostics should list `MOZA AB6 FFB Base` under DirectInput force-feedback devices.

Runtime logs are written to:

```text
%LOCALAPPDATA%\MozaStarCitizen\app.log
```

## Development

Requirements:

- Windows
- .NET SDK 8 or newer
- MOZA AB6 FFB Base for hardware testing

Build:

```powershell
dotnet build MozaStarCitizen.sln --configuration Release
```

Run from source:

```powershell
dotnet run --project src\MozaStarCitizen.App\MozaStarCitizen.App.csproj
```

Create a portable ZIP:

```powershell
.\scripts\package-portable.ps1
```

By default, the package script looks for the local MOZA C# SDK runtime at:

```text
D:\MOZA_SDK\MOZA_SDK\SDK_CSharp\x64
```

The SDK DLLs are not committed to this repository. If present locally, these files are copied into the portable build under `drivers\moza-sdk\x64`.

## Event Patterns

Event matching lives in:

```text
src\MozaStarCitizen.App\event-patterns.json
```

The file is copied next to the executable, so patterns can be adjusted without changing code. Current patterns are provisional and should be refined with real Star Citizen log lines from current builds.

## Known Limitations

- Event patterns need validation against real Star Citizen `Game.log` output.
- Effect strength, direction, duration, and frequency are first-pass values.
- Sustained effects currently stop through matching end events or monitor stop; a dedicated `Stop Effects` UI control is a likely next improvement.
- The MOZA SDK wheelbase force-feedback path is retained for diagnostics/experimentation, but AB6 output should use DirectInput.

## Project Layout

```text
src/MozaStarCitizen.App/        WPF app
src/MozaStarCitizen.App/ForceFeedback/
                                DirectInput, MOZA SDK, fallback, and preview output
src/MozaStarCitizen.App/Parsing/
                                Game.log event parser
src/MozaStarCitizen.App/Log/    Game.log discovery and tailing
scripts/package-portable.ps1    Self-contained Windows release package
docs/                           Lower-level implementation notes
```
