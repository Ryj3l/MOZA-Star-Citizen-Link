# MOZA Star Citizen Link — Engineering PRP v2.0

**Prepared for:** Claude Opus agentic engineering team
**Prepared by:** Senior Software Architect / Lead of Agentic Development
**Status:** Approved for execution — Phase 1 implementation may begin immediately
**Supersedes:** *MOZA Star Citizen Link — Modern Stack Architecture PRP* (v1)
**Document type:** Implementation-grade Product Requirements Prompt (PRP) with task decomposition, interface contracts, acceptance criteria, and agent operating model

---

## 0. How to Read This Document (Agent Operating Model)

This PRP is written for autonomous and semi-autonomous engineering agents. Sections are organized so that any task in Section 15 can be picked up independently given the contracts in Sections 5–9 and the operating model in this section.

### 0.1 Agent role definition

You are a senior software platform engineer with the following composite expertise: Windows desktop architecture, C# / .NET 8, WPF / MVVM, real-time signal processing, DirectInput force feedback, Windows audio (WASAPI), Windows.Graphics.Capture, ONNX Runtime, and safety-critical hardware actuation. You produce production-grade code, not prototypes. You write tests before or alongside implementation. You do not invent requirements; if a requirement is missing or ambiguous, you raise it explicitly in the PR description rather than guessing.

### 0.2 Branch, commit, and PR conventions

The trunk branch is `main`. Feature branches use the form `feat/<task-id>-<short-slug>`, fixes use `fix/<task-id>-<short-slug>`, and infrastructure uses `chore/<task-id>-<short-slug>`. Task IDs come from Section 15. Commits follow Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`, `perf:`). Each PR maps to exactly one task ID. PR descriptions must include the task ID, a checklist copied from the task's acceptance criteria, and an explicit statement of any deviations from the PRP. Do not merge a PR whose checklist is incomplete; instead, split it.

### 0.3 Definition of done

A task is done when (a) all acceptance criteria in its task spec are met and demonstrably verified, (b) unit tests cover the new code with the project's prevailing coverage threshold (Phase 1 target: 70% line coverage on Core, Effects, Fusion, Profiles; 50% elsewhere), (c) the solution builds clean with no new warnings at WarningLevel 5, (d) Roslyn analyzers and the project's `.editorconfig` produce no new violations, (e) the diagnostic and logging modes specified in Section 8 emit the events listed in the task spec, and (f) any new dependency has a Dependency Decision Record per Section 4.

### 0.4 What to do when stuck

If a task cannot be completed without a decision that this PRP does not make, do not guess. Stop, write a short decision memo into `docs/decisions/<task-id>-<topic>.md` using the ADR (Architectural Decision Record) template at the path `docs/decisions/TEMPLATE.md`, open a draft PR with the memo only, and tag the human reviewer. Resume only after the memo is resolved.

### 0.5 Hard prohibitions (do not violate; do not request exceptions)

No memory reading of Star Citizen process memory. No DLL injection, process injection, or kernel-level hooks. No modification of Star Citizen files. No interaction with Easy Anti-Cheat surfaces. No SharpDX. No driving force-feedback devices that are not affirmatively identified as MOZA AB6 or AB9 (or other MOZA flight bases added by explicit allowlist). No automatic upload of raw audio, raw screenshots, or raw game logs. No external network telemetry without explicit per-session user opt-in. No arbitrary user-DLL plugin execution.

---

## 1. Executive Summary

MOZA Star Citizen Link is a Windows 10/11 desktop companion application that converts safe, externally observable Star Citizen gameplay signals into contextual force-feedback effects on MOZA AB6 and AB9 flight bases. The product is engineered as a platform with four architectural tiers: (1) independent sensors publishing typed evidence events, (2) a fusion engine emitting canonical game events, (3) an effect resolver that maps game events to safe, normalized force-feedback commands using user and ship profiles, and (4) a Vortice.DirectInput output layer that plays those commands on detected MOZA hardware.

The strategic constraint is data acquisition, not output. Star Citizen exposes no public telemetry API. The durable competitive advantage of this product is therefore the patch-resilient sensor fusion pipeline (audio + screen + log + input) and the curated effect catalog, not the DirectInput plumbing.

Phase 1 (4–6 weeks) delivers a functional MVP: modern stack, DirectInput output, seven log-driven effects, preview mode, diagnostics, emergency stop, hot-reloadable patterns. Phase 2 (6–8 weeks after Phase 1) delivers competitive public-beta quality: audio capture and heuristic classifiers, screen capture with ROI analysis, input mirroring, full fusion, 15-effect catalog, ship profiles, first-run wizard, installer, privacy policy, and the 50-user beta gate. Version 2.0 (4–6 months after Phase 2 ships) delivers the haptics intelligence platform: weak-supervision-driven dataset pipeline, ONNX local inference, signed classifier packs, patch-resilience dashboard, community profile ecosystem, effect composer, OBS overlay.

### 1.1 Decision register (at-a-glance)

| Decision area | Decision | Section |
|---|---|---|
| Runtime | .NET 8 LTS | 2.1 |
| UI | WPF + MVVM (CommunityToolkit.Mvvm) | 2.2 |
| Force feedback | Vortice.DirectInput only — no SharpDX, no MOZA racing SDK in active output | 2.3 |
| Audio | NAudio WASAPI endpoint loopback Phase 2; process loopback Phase 3 | 2.4 |
| Screen | Windows.Graphics.Capture (WinRT) | 2.5 |
| Event pipeline | System.Threading.Channels, bounded, single-writer/single-reader per stage | 2.6 |
| Logging | Serilog + local rolling file sink | 2.7 |
| Crash reporting | Local crash logs Phase 1; Sentry opt-in Phase 2 | 2.8 |
| Settings/profiles | System.Text.Json, versioned schemas, atomic writes | 2.9 |
| ML | Heuristics Phase 2; ONNX Runtime + weak supervision Phase 3 | 6 |
| Installer | WiX Toolset v4 (preferred) or MSIX | 2.10 |
| Updates | GitHub Releases-compatible manifest, in-app check | 2.10 |
| Code signing | Unsigned dev builds, standard cert beta, EV cert at GA | 13.2 |

---

## 2. Modern Stack — Authoritative Choices

### 2.1 Runtime

.NET 8 LTS on Windows 10 (1809+) and Windows 11. Target framework moniker `net8.0-windows10.0.19041.0` for the WPF app project to enable Windows.Graphics.Capture and modern WinRT projections. Other class libraries target `net8.0` unless they require WinRT, in which case they target `net8.0-windows10.0.19041.0`.

### 2.2 UI framework

WPF with the MVVM pattern. Use `CommunityToolkit.Mvvm` for `ObservableObject`, `RelayCommand`, and source-generated property notifications. Do not hand-roll `INotifyPropertyChanged` boilerplate. Do not use Electron. WinUI 3 may be re-evaluated after Phase 2 if and only if product requirements justify it.

### 2.3 Force-feedback output

Vortice.DirectInput exclusively. The current repository's hand-rolled DirectInput COM interop (in `src/MozaStarCitizen.App/ForceFeedback/DirectInput/`) is correct in shape but must be replaced by Vortice's managed wrappers to eliminate hand-marshaled `IntPtr` arithmetic, simplify lifecycle management, and reduce surface area for memory bugs. The MOZA managed SDK (`MOZA_API_CSharp.dll`) is demoted to diagnostic-only and removed from the active fallback chain — it targets racing wheelbases and is documented to return `NODEVICES` on flight bases.

The output layer must never drive a force-feedback device that has not been affirmatively identified as MOZA AB6 or AB9, by product name match against the allowlist in `device-allowlist.json`. Unknown devices appear in diagnostics but receive no commands.

### 2.4 Audio capture

Phase 2 ships NAudio's `WasapiLoopbackCapture` against the system default output endpoint. This will capture all audio playing through that endpoint, which includes Star Citizen plus Discord, browser audio, music, system sounds. Endpoint-mix contamination is unavoidable at this layer and must be surfaced to the user as a warning during the first-run wizard and in the sensor diagnostics panel.

Phase 3 adds process-specific loopback against `StarCitizen.exe` using the Win10 1903+ `ActivateAudioInterfaceAsync` with `VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK`. This requires direct WinRT/COM interop because NAudio does not expose this API at the time of this PRP. Implementation pattern: the existing CSCore / pinvoke samples in the audio-loopback ecosystem are the reference; license each before adoption per Section 4.

### 2.5 Audio DSP

Deterministic feature extraction first, ML last. Required Phase 2 features per analysis window: RMS energy, peak amplitude, spectral centroid, spectral rolloff (0.85 and 0.95 thresholds), four-band energy (sub-bass <120 Hz, low 120–500 Hz, mid 500–4000 Hz, high >4000 Hz), transient onset strength (spectral flux), zero-crossing rate, stereo balance (L/R RMS ratio), and temporal envelope (attack-time, decay-time, sustain-level). Use `System.Numerics` and `MathNet.Numerics` for FFT. Analysis windows: 2048 samples at 48 kHz with 50% overlap, producing ~46 ms hops. Worker thread runs at `ThreadPriority.AboveNormal`.

### 2.6 Screen capture

Windows.Graphics.Capture (WinRT) via the `Windows.Graphics.Capture` namespace, with `GraphicsCaptureItem` targeting the Star Citizen window by HWND. Use DirectX 11 interop (`Microsoft.Graphics.Canvas` via Win2D, or direct SharpGen-generated D3D11 interop) to access the captured frame as a GPU texture, then copy to a CPU-readable staging texture for ROI analysis. Target capture rate: 30 Hz; degrade to 15 Hz if CPU budget is exceeded.

The Windows 10 1903 user-consent picker model can be bypassed for HWND-targeted captures using `GraphicsCaptureItem.CreateFromVisual` / `GraphicsCaptureItemInterop.CreateForWindow`, which is available without picker UI from Windows 10 1903 onward when the application has UIAccess or when the user has consented in app settings. Phase 2 ships the picker-compatible flow; Phase 3 may add the no-picker path behind a "Power user mode" opt-in.

### 2.7 Event pipeline

`System.Threading.Channels` with the following invariants per stage:

- **SensorEvent channel**: `Channel.CreateBounded<SensorEvent>(new BoundedChannelOptions(capacity: 1024) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = false, SingleWriter = false })`. Multi-writer (one per sensor), multi-reader (fusion engine plus diagnostic tap).
- **GameEvent channel**: `Channel.CreateBounded<GameEvent>(new BoundedChannelOptions(capacity: 256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = false, SingleWriter = true })`. Single writer (fusion engine), multi-reader (effect resolver plus diagnostic tap).
- **ForceCommand channel**: `Channel.CreateBounded<ForceCommand>(new BoundedChannelOptions(capacity: 64) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true })`. Wait mode ensures we do not silently drop commands; if the output worker stalls, the resolver applies backpressure.

Drop events from the SensorEvent channel are counted in diagnostics. A drop rate above 1% over a 60-second window raises a warning in the diagnostics panel and writes a Serilog warning.

No external brokers (NATS, RabbitMQ, Redis, Kafka, etc.). The pipeline is in-process.

### 2.8 Logging

Serilog with `Serilog.Sinks.File` configured for daily rolling files at `%LOCALAPPDATA%\MozaStarCitizen\logs\app-YYYYMMDD.log`, retain 14 days, max 50 MB per file. Structured properties via the `{@event}` operator. The default log level is `Information`; `Debug` is enabled in diagnostic mode; `Verbose` is enabled in developer-lab mode (Section 8.1). Console sink is added only in debug builds.

### 2.9 Settings and profiles

`System.Text.Json` with `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip }`. All persisted schemas carry a top-level `"schemaVersion": <int>` field. Write path: serialize to a temp file in the same directory, then `File.Move(temp, target, overwrite: true)` for atomicity. Read path: if schema version is unknown or newer than supported, load defaults and write the user's file aside as `<name>.unsupported-<timestamp>.json`. Schema migrations are explicit functions registered per version pair.

Storage root: `%LOCALAPPDATA%\MozaStarCitizen\`. Subdirectories: `logs/`, `profiles/`, `catalogs/`, `diagnostics/`, `crash-dumps/`, `state/`.

### 2.10 Installer and updates

WiX Toolset v4 preferred (MSI) for the production installer; MSIX is acceptable if the team prefers the Store-compatible packaging path and accepts MSIX's stricter file-system isolation. The installer must support per-user install, optional auto-start on Windows logon (off by default), Start Menu shortcuts, and clean uninstall. In-app update check polls a GitHub Releases manifest at startup and surfaces a non-blocking notification; user confirms before download and install.

---

## 3. Repository Layout (Target)

The existing repository has working code that must be preserved during migration: `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `AppLog.cs`, `GameLogTailer.cs`, `StarCitizenLogLocator.cs`, `StarCitizenEventParser.cs`, `AppSettingsStore.cs`, `ForceFeedbackController.cs`, and the `Models/` records. These move to their new project homes per the table below; they do not get rewritten from scratch.

```text
src/
  Moza.ScLink.App/              WPF application; views; view models; tray; hotkeys
  Moza.ScLink.Core/             Domain models; event bus; safety primitives; abstractions
  Moza.ScLink.DirectInput/      Vortice.DirectInput output; device detection; allowlist
  Moza.ScLink.Audio/            WASAPI capture; feature extraction; heuristic classifiers
  Moza.ScLink.Screen/           Windows.Graphics.Capture; ROI analysis
  Moza.ScLink.Logs/             Game.log tailing; pattern engine; hot reload
  Moza.ScLink.Input/            DirectInput input mirroring (stick/throttle/pedals)
  Moza.ScLink.Fusion/           Sensor fusion; dedupe; suppression; rule engine
  Moza.ScLink.Effects/          Effect catalog; envelope generation; gain stack; safety limiter
  Moza.ScLink.Profiles/         Device profiles; ship profiles; user profiles; import/export
  Moza.ScLink.Diagnostics/      Diagnostic modes; bundle export; retention; health
  Moza.ScLink.Updater/          GitHub Releases manifest; update orchestration

tests/
  Moza.ScLink.Core.Tests/
  Moza.ScLink.DirectInput.Tests/
  Moza.ScLink.Audio.Tests/
  Moza.ScLink.Screen.Tests/
  Moza.ScLink.Logs.Tests/
  Moza.ScLink.Fusion.Tests/
  Moza.ScLink.Effects.Tests/
  Moza.ScLink.Profiles.Tests/
  Moza.ScLink.Diagnostics.Tests/

samples/
  GameLog/                      Captured Game.log samples for pattern regression tests
  Audio/                        Audio fixtures for classifier regression tests (no game audio shipped)

docs/
  architecture/                 Architecture decision records (ADRs)
  decisions/                    Per-task decision memos
  patterns/                     Star Citizen log pattern documentation
  effects/                      Effect catalog documentation with diagrams

scripts/
  package-portable.ps1          (existing, retained)
  validate-patterns.ps1         CI lint of patterns against samples/GameLog/
  generate-changelog.ps1        Release notes from Conventional Commits

MozaStarCitizenLink.sln         Solution file
Directory.Build.props           Shared MSBuild properties
Directory.Packages.props        Centralized package versions
.editorconfig                   Code style enforcement
```

### 3.1 Mapping from existing repo to new layout

| Existing path | New project | Action |
|---|---|---|
| `src/MozaStarCitizen.App/Models/*.cs` | `Moza.ScLink.Core/Models/` | Move; rename namespace |
| `src/MozaStarCitizen.App/Diagnostics/AppLog.cs` | `Moza.ScLink.Core/Diagnostics/` | Replaced by Serilog; keep as legacy shim during migration only |
| `src/MozaStarCitizen.App/Log/*.cs` | `Moza.ScLink.Logs/` | Move; refactor to implement `ISensor` |
| `src/MozaStarCitizen.App/Parsing/*.cs` | `Moza.ScLink.Logs/Parsing/` | Move; add versioned pattern library |
| `src/MozaStarCitizen.App/ForceFeedback/DirectInputForceFeedbackDevice.cs` | `Moza.ScLink.DirectInput/` | Rewrite on top of Vortice.DirectInput |
| `src/MozaStarCitizen.App/ForceFeedback/MozaSdk*.cs` | `Moza.ScLink.Diagnostics/MozaSdkProbe.cs` | Demote to diagnostic probe only |
| `src/MozaStarCitizen.App/ForceFeedback/ForceFeedbackController.cs` | `Moza.ScLink.Effects/EffectResolver.cs` | Refactor; absorb into resolver |
| `src/MozaStarCitizen.App/ScreenCapture/*.cs` | `Moza.ScLink.Screen/` (Phase 2) | Park in `legacy/` branch; rewrite for Windows.Graphics.Capture |
| `src/MozaStarCitizen.App/Settings/*.cs` | `Moza.ScLink.Profiles/Settings/` | Move; add schema versioning |
| `src/MozaStarCitizen.App/ViewModels/*.cs` | `Moza.ScLink.App/ViewModels/` | Refactor to use CommunityToolkit.Mvvm |
| `src/MozaStarCitizen.App/MainWindow.xaml(.cs)` | `Moza.ScLink.App/Views/` | Move; restructure |
| `src/MozaStarCitizen.App/event-patterns.json` | `Moza.ScLink.Logs/Patterns/v0.json` | Move; mark as v0 placeholder |

---

## 4. Dependency Governance

Every new dependency requires a Dependency Decision Record (DDR) committed to `docs/decisions/ddr-<package>.md` before the package appears in any `<PackageReference>` element. Pre-approved Phase 1 dependencies are listed at the bottom of this section and do not require new DDRs.

### 4.1 DDR template

```markdown
# Dependency Decision Record: <Package Name>

## Package
- **Name:**
- **Version pinned to:**
- **Source (NuGet / GitHub / etc.):**
- **License (SPDX identifier):**
- **License-compatible with proprietary distribution:** Yes / No
- **Maintainer activity (last release date, open-issue ratio):**
- **Repository URL:**
- **Transitive dependency count:**

## Purpose
- **What it does:**
- **Why we need it in this product:**
- **What it replaces or enables:**

## Alternatives considered
- **Option A:** (with brief tradeoff analysis)
- **Option B:**
- **Build it ourselves:**

## Risk review
- **Maintenance risk:**
- **License risk:**
- **Security history (CVEs):**
- **Runtime footprint (assembly size, native deps):**
- **Anti-cheat-adjacency risk:**
- **Cost (commercial/per-seat):**

## Decision
- **Approved / Rejected / Deferred**
- **Approver:**
- **Date:**

## Validation
- [ ] Builds against target framework
- [ ] Passes existing test suite
- [ ] Runtime smoke test passes
- [ ] No new analyzer warnings
- [ ] No license-text change required in `NOTICE.md`
```

### 4.2 Pre-approved Phase 1 dependencies

| Package | Pinned version | License | Used in |
|---|---|---|---|
| `CommunityToolkit.Mvvm` | 8.4.x | MIT | App, view models |
| `Serilog` | 4.x | Apache-2.0 | Core, all projects |
| `Serilog.Sinks.File` | 6.x | Apache-2.0 | Core |
| `Serilog.Settings.Configuration` | 9.x | Apache-2.0 | App |
| `Microsoft.Extensions.Hosting` | 8.x | MIT | App (background services) |
| `Microsoft.Extensions.DependencyInjection` | 8.x | MIT | App, all projects |
| `Microsoft.Extensions.Logging` | 8.x | MIT | App, all projects — core Phase-1 logging stack |
| `Microsoft.Extensions.Logging.Abstractions` | 8.0.x | MIT | DirectInput, test projects — abstractions-sister of Microsoft.Extensions.Logging; interface-only, no independent decision |
| `Vortice.DirectInput` | latest stable | MIT | DirectInput |
| `System.Text.Json` | 8.x (in-box) | MIT | Core, Profiles |
| `xunit` | 2.x | Apache-2.0 | All test projects |
| `xunit.runner.visualstudio` | 2.x | Apache-2.0 | All test projects |
| `FluentAssertions` | 6.x | Apache-2.0 | All test projects |
| `NSubstitute` | 5.x | BSD-3 | All test projects |
| `coverlet.collector` | 6.x | MIT | All test projects |

### 4.3 Pre-approved Phase 2 dependencies (require DDR before adoption)

| Package | Purpose | Notes |
|---|---|---|
| `NAudio` | WASAPI loopback capture | Verify license terms; MIT |
| `MathNet.Numerics` | FFT, spectral features | MIT |
| `CommunityToolkit.WinUI.Notifications` or equivalent | Toast notifications | Verify on WPF |
| `Sentry` | Crash reporting | Phase 2, opt-in only, see §8.5 |
| `WiX Toolset v4` | Installer | Build-time tool, not runtime |

### 4.4 Phase 3 dependencies (deferred; DDR required at adoption time)

`Microsoft.ML.OnnxRuntime`, `Microsoft.ML.OnnxRuntime.DirectML`, signed-classifier-pack validation library, plus whatever process-loopback audio interop library survives vetting.

---

## 5. Domain Model and Interface Contracts

This section is normative. Agents implement these contracts exactly as specified. Deviations require a decision memo.

### 5.1 Core enums

```csharp
namespace Moza.ScLink.Core;

public enum SensorKind
{
    Log,
    Audio,
    Screen,
    Input,
}

public enum GameEventType
{
    // Quantum
    QuantumSpoolStarted,
    QuantumSpoolEnded,
    QuantumJumpStarted,
    QuantumJumpExit,

    // Atmosphere
    AtmosphereEntered,
    AtmosphereExited,
    AtmosphericBuffet,

    // Landing / impact
    LandingGearContact,
    HullImpact,
    VehicleDestruction,

    // Combat
    WeaponFireBallistic,
    WeaponFireEnergy,
    MissileLaunch,
    ShieldHit,
    HullDamage,

    // Misc
    ThrusterActivation,

    // System events (not haptic)
    SessionStarted,
    SessionEnded,
    EmergencyStop,
}

public enum EffectCategory
{
    Combat,
    Flight,
    Environment,
    Ui,
    System,
}

public enum ForceEffectType
{
    Periodic,
    ConstantForce,
    PeriodicWithEnvelope,
    Composite,
}

public enum DeviceModel
{
    Unknown,
    MozaAb6,
    MozaAb9,
}
```

### 5.2 Sensor event

```csharp
namespace Moza.ScLink.Core;

public sealed record SensorEvent
{
    public required string EventId { get; init; }              // GUID
    public required string SensorId { get; init; }             // e.g. "audio.endpoint-loopback"
    public required SensorKind SensorKind { get; init; }
    public required string EventType { get; init; }            // e.g. "audio.weapon_fire_ballistic"
    public required DateTimeOffset Timestamp { get; init; }

    public double Confidence { get; init; }                    // [0.0, 1.0]
    public double Intensity { get; init; }                     // [0.0, 1.0]
    public TimeSpan? Duration { get; init; }                   // null for transient

    public ImmutableDictionary<string, double> Features { get; init; }
        = ImmutableDictionary<string, double>.Empty;
    public ImmutableDictionary<string, string> Metadata { get; init; }
        = ImmutableDictionary<string, string>.Empty;
}
```

Notes for implementers. The dictionaries are `ImmutableDictionary` (not `Dictionary<string, object>`) so record equality is meaningful and instances are safe to share across threads. Use `string` and `double` types to keep serialization deterministic. If you need to attach binary evidence, write it to disk and put a path/hash in `Metadata`.

### 5.3 Sensor interface

```csharp
namespace Moza.ScLink.Core;

public interface ISensor : IAsyncDisposable
{
    string SensorId { get; }
    SensorKind Kind { get; }
    SensorHealth Health { get; }
    SensorState State { get; }

    event EventHandler<SensorHealthChangedEventArgs>? HealthChanged;
    event EventHandler<SensorStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Starts the sensor. Idempotent: calling Start on an already-started sensor is a no-op.
    /// Throws SensorStartException if start fails terminally.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the sensor. Idempotent. Must complete within 5 seconds or throw TimeoutException.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Yields SensorEvents as they are produced. Multiple calls to this method are allowed
    /// and produce independent enumerations (multi-cast). The sensor MUST NOT block production
    /// on slow consumers; events are dropped (and counted in Health.DroppedEvents) if any
    /// consumer falls behind by more than 100 events.
    /// </summary>
    IAsyncEnumerable<SensorEvent> ReadEventsAsync(CancellationToken cancellationToken);
}

public enum SensorState { Stopped, Starting, Running, Faulted, Stopping }

public sealed record SensorHealth(
    bool IsHealthy,
    string? LastFault,
    long EventsEmitted,
    long DroppedEvents,
    DateTimeOffset? LastEventAt);
```

### 5.4 Canonical game event

```csharp
namespace Moza.ScLink.Core;

public sealed record GameEvent
{
    public required string EventId { get; init; }
    public required GameEventType EventType { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    public double Confidence { get; init; }                    // [0.0, 1.0]
    public double Intensity { get; init; }                     // [0.0, 1.0]
    public TimeSpan? Duration { get; init; }

    public ImmutableArray<string> Sources { get; init; }       // sensor IDs that contributed
    public ImmutableArray<string> ReasonCodes { get; init; }   // explainability
    public ImmutableDictionary<string, double> Evidence { get; init; }
        = ImmutableDictionary<string, double>.Empty;
    public ImmutableDictionary<string, string> Metadata { get; init; }
        = ImmutableDictionary<string, string>.Empty;
}
```

### 5.5 Force effect and command

```csharp
namespace Moza.ScLink.Core;

public sealed record ForceEffect
{
    public required string EffectId { get; init; }             // catalog key
    public required ForceEffectType EffectType { get; init; }
    public required EffectCategory Category { get; init; }

    public double BaseIntensity { get; init; }                 // [0.0, 1.0]
    public double FrequencyHz { get; init; }                   // 0 for constant
    public TimeSpan Duration { get; init; }                    // Zero = sustained until stopped

    public double DirectionX { get; init; }                    // [-1.0, 1.0]
    public double DirectionY { get; init; }

    public ForceEnvelope? Envelope { get; init; }
    public bool IsSustained { get; init; }
    public string? StateKey { get; init; }                     // for sustained effects
}

public sealed record ForceEnvelope(
    TimeSpan Attack,
    TimeSpan Hold,
    TimeSpan Decay,
    TimeSpan Release,
    double AttackLevel,
    double SustainLevel);

public abstract record ForceCommand
{
    public required string CommandId { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
}

public sealed record PlayEffectCommand(
    ForceEffect Effect,
    double FinalIntensity) : ForceCommand;

public sealed record StopEffectCommand(string StateKey) : ForceCommand;

public sealed record StopAllCommand() : ForceCommand;
```

### 5.6 Output device interface

```csharp
namespace Moza.ScLink.Core;

public interface IForceFeedbackDevice : IAsyncDisposable
{
    DeviceModel Model { get; }
    string DisplayName { get; }
    string ProductName { get; }
    Guid InstanceGuid { get; }
    DeviceCapabilities Capabilities { get; }
    DeviceState State { get; }

    event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    Task InitializeAsync(CancellationToken cancellationToken);
    Task ExecuteAsync(ForceCommand command, CancellationToken cancellationToken);
    Task StopAllAsync(CancellationToken cancellationToken);
}

public enum DeviceState { Disconnected, Detecting, Initializing, Ready, Faulted }

public sealed record DeviceCapabilities(
    int AxisCount,
    int SimultaneousEffectCount,
    bool SupportsConstantForce,
    bool SupportsPeriodic,
    bool SupportsEnvelope,
    int MaxGain,
    double MaxIntensityRecommended);   // device-specific safety cap, e.g. 0.85 for AB6
```

### 5.7 Effect resolver interface

```csharp
namespace Moza.ScLink.Core;

public interface IEffectResolver
{
    /// <summary>
    /// Resolves a canonical game event into zero or more ForceCommands, applying the gain
    /// stack, profile selection, envelope generation, and the safety limiter.
    /// </summary>
    IReadOnlyList<ForceCommand> Resolve(GameEvent gameEvent, ResolverContext context);
}

public sealed record ResolverContext(
    ShipProfile ActiveShipProfile,
    UserGains UserGains,
    DeviceCapabilities DeviceCapabilities,
    DateTimeOffset Now);
```

### 5.8 Safety primitives

The following safety constants are mandatory floors and ceilings. They live in `Moza.ScLink.Core/Safety/SafetyLimits.cs` and are not user-overridable.

| Constant | Value | Rationale |
|---|---|---|
| `MinIntensity` | 0.0 | Floor |
| `MaxIntensity` | 1.0 | Ceiling |
| `DefaultMasterGain` | 0.6 | Conservative first-run default |
| `MaxIntensityRateOfChangePerSecond` | 4.0 | No more than 4x intensity swing per second |
| `MaxSustainedIntensity` | 0.7 | Sustained effects > 5 seconds are capped at 0.7 |
| `MaxSimultaneousEffects` | 4 | Above this, oldest non-sustained is preempted |
| `StartupRampMs` | 250 | All effects start at 0 and ramp over this period |
| `StopRampMs` | 150 | All effects ramp down on stop |
| `EmergencyStopMaxLatencyMs` | 50 | Emergency stop must execute within 50 ms |
| `AbsoluteMaxEffectDuration` | TimeSpan.FromMinutes(10) | Sustained effects auto-stop after this |

The safety limiter component (`Moza.ScLink.Effects/SafetyLimiter.cs`) inspects every `ForceCommand` before it reaches the output worker and enforces these constants. Violations are logged at `Warning` level.

### 5.9 Gain stack

The final intensity computation:

```text
finalIntensity =
    baseEffectIntensity            // from catalog
  * eventIntensityModifier         // from GameEvent.Intensity
  * shipProfileMultiplier          // per-effect from active ship profile
  * categoryGain                   // user setting per EffectCategory
  * masterGain                     // user master volume
  * deviceGainMultiplier           // per-device calibration

clamp(finalIntensity, 0, deviceCapabilities.MaxIntensityRecommended)
applyRateOfChangeLimit()
applySustainedCap()
```

Reference implementation lives in `Moza.ScLink.Effects/GainStack.cs`. Pure function; unit-tested with 100% branch coverage required.

---

## 6. Heuristics and ML Strategy

Unchanged in shape from v1 of this PRP, but with concrete thresholds and acceptance criteria added.

### 6.1 Phase 2 heuristic classifiers

The Phase 2 audio sensor must implement deterministic classifiers for the following event types, each producing a `SensorEvent` with confidence in `[0.0, 1.0]` and a non-empty `ReasonCodes`/feature dictionary explaining the decision.

| Classifier | Primary features | Confidence floor for emission |
|---|---|---|
| Quantum spool tone | Sustained energy in 200–400 Hz band rising over 1.5s; spectral centroid stable | 0.55 |
| Quantum exit | Sharp broadband transient after sustained quantum-tone signature | 0.6 |
| Atmospheric wind | Sustained low-band noise energy; high zero-crossing rate in low band | 0.5 |
| Ballistic weapon fire | Sharp onset, broadband transient, peak > 0.7, decay < 200 ms | 0.6 |
| Energy weapon fire | Onset with mid-band emphasis, longer decay, peak 0.4–0.7 | 0.55 |
| Missile launch | Strong sustained low-band onset, 800–1500 ms envelope | 0.55 |
| Hull impact | Broadband transient with sub-bass component, RMS spike | 0.55 |
| Shield hit | Mid-high band emphasis, short decay, distinctive harmonic content | 0.5 |
| Landing contact | Broadband transient with sub-bass; correlated with low altitude (if known) | 0.5 |
| Vehicle destruction | Sustained broadband energy > 800 ms with distinctive low-band roll | 0.7 |
| Thruster activation | Sustained mid-band energy modulated by input state | 0.4 |

Confidence below the floor results in no emission. Emitted SensorEvents whose canonical fusion result is suppressed or rejected count as false positives in beta diagnostics.

### 6.2 Phase 3 weak supervision and ONNX

Unchanged from v1 §6.2–6.5. Weak labels are derived from high-confidence heuristics combined with cross-sensor confirmation (e.g., an audio weapon-fire candidate with a trigger press within ±120 ms is weak-labeled positive). ONNX models are packaged as signed classifier packs (see §11) and shipped separately from the main app.

---

## 7. Fusion Engine

### 7.1 Rule schema

Fusion rules are JSON, hot-reloadable from `%LOCALAPPDATA%\MozaStarCitizen\fusion-rules.json`. The default rule set ships in `Moza.ScLink.Fusion/DefaultRules.json` and is loaded if no user file exists.

```json
{
  "schemaVersion": 1,
  "rules": [
    {
      "id": "weapon-fire-ballistic-v1",
      "canonicalEvent": "WeaponFireBallistic",
      "primarySensor": "audio.weapon_fire_ballistic",
      "corroboratingSensors": ["input.trigger_pressed"],
      "dedupeWindowMs": 120,
      "minimumPrimaryConfidence": 0.6,
      "confidenceBoosts": [
        { "when": "input.trigger_pressed within 120ms", "boost": 0.15 },
        { "when": "log.weapon_fire_logged within 1500ms", "boost": 0.1 }
      ],
      "suppressWhen": [
        "screen.menu_visible",
        "screen.cursor_visible",
        "session.emergency_stop_active"
      ],
      "minimumFinalConfidence": 0.7
    }
  ]
}
```

### 7.2 Fusion engine contract

```csharp
namespace Moza.ScLink.Fusion;

public interface IFusionEngine
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reloads rules from disk. Safe to call while running.
    /// </summary>
    Task ReloadRulesAsync(CancellationToken cancellationToken);

    IFusionDiagnostics Diagnostics { get; }
}
```

The fusion engine consumes the SensorEvent channel and produces the GameEvent channel. Internally it maintains a sliding window (max 5 seconds) of recent SensorEvents indexed by event type, evaluates each new event against active rules, applies dedupe, and emits GameEvents that pass the `minimumFinalConfidence` gate.

### 7.3 Dedupe and suppression rules

- Two SensorEvents of the same `EventType` within `dedupeWindowMs` are treated as one. The higher-confidence one wins; the other is recorded as "deduped" in diagnostics.
- A canonical GameEvent is suppressed if any suppression sensor event is currently active. Active-state suppression sensors (e.g. `screen.menu_visible`) maintain their own activation/deactivation lifecycle and publish state-change SensorEvents.
- The emergency stop is a special suppression signal: when active, all canonical events of category `Combat`, `Flight`, `Environment` are suppressed; `System` events are not.

---

## 8. Logging, Diagnostics, and Beta Instrumentation

Substantially preserved from v1. The key additions are concrete schema, retention enforcement implementation notes, and the bundle-export consent UX requirements.

### 8.1 Logging modes

```yaml
logging_modes:
  normal:
    purpose: standard user troubleshooting
    default: true
    serilog_level: Information
    raw_audio: false
    screenshots: false
    raw_game_logs: false
    feature_vectors: false

  diagnostic:
    purpose: advanced troubleshooting; engineering bug reports
    default: false
    serilog_level: Debug
    raw_audio: false
    screenshots: false
    raw_game_logs: optional_redacted_excerpt
    feature_vectors: true

  private_beta:
    purpose: accuracy, performance, classifier, device tuning
    default: false
    requires_explicit_consent: true
    consent_token_expiry_days: 30
    serilog_level: Debug
    raw_audio: optional_short_ring_buffer
    screenshots: optional_roi_only
    raw_game_logs: optional_redacted_excerpt
    feature_vectors: true

  developer_lab:
    purpose: local developer validation only
    default: false
    requires_local_config_flag: true   # %LOCALAPPDATA%\MozaStarCitizen\dev.flag
    serilog_level: Verbose
    raw_audio: allowed_local_only
    screenshots: allowed_local_only
    raw_game_logs: allowed_local_only
    feature_vectors: true
```

### 8.2 Event-level beta record

```json
{
  "timestamp": "2026-05-12T18:42:11.124Z",
  "sessionId": "local-random-guid",
  "appVersion": "1.0.0-beta.4",
  "sensorEvents": [
    {
      "sensor": "audio.endpoint-loopback",
      "eventType": "weapon_fire_ballistic",
      "confidence": 0.73,
      "features": {
        "rms": 0.41,
        "peak": 0.88,
        "spectralCentroid": 3220.5,
        "lowBandEnergy": 0.18,
        "midBandEnergy": 0.61,
        "highBandEnergy": 0.52,
        "onsetStrength": 0.91
      },
      "reasonCodes": [
        "sharp_transient_onset",
        "broadband_energy_spike",
        "trigger_pressed_within_120ms"
      ]
    }
  ],
  "fusionResult": {
    "canonicalEvent": "WeaponFireBallistic",
    "confidence": 0.84,
    "suppressed": false,
    "sources": ["audio.endpoint-loopback", "input.flight-stick"]
  },
  "effectResult": {
    "effectId": "weapon_fire_ballistic",
    "finalIntensity": 0.47,
    "durationMs": 90,
    "dispatchLatencyMs": 34,
    "outputStatus": "played"
  }
}
```

### 8.3 Diagnostic bundle export

The diagnostic export UI must show, before any data leaves the machine:

- What will be included (line-by-line list)
- Approximate bundle size
- Whether raw audio is included (Y/N, with red badge if Y)
- Whether screenshots are included (Y/N)
- Whether raw game logs are included (Y/N)
- Retention status of contents
- Destination (always local file; no cloud upload)

Default exports always exclude raw audio, screenshots, full game logs, chat content, voice content, personal filesystem paths, email addresses, account names, tokens, secrets. The redactor passes a regex denylist over all included logs before bundling. The denylist is in `Moza.ScLink.Diagnostics/Redaction.cs` and is unit-tested against a representative log corpus.

### 8.4 Retention

```yaml
retention:
  normal_logs_days: 14
  diagnostic_logs_days: 14
  private_beta_feature_data_days: 30
  raw_audio_days: 7
  screenshots_days: 7
  crash_dumps_days: 14
  exported_bundles: user_managed
```

A background `RetentionWorker` (`Moza.ScLink.Diagnostics/RetentionWorker.cs`) runs on app start and every 6 hours thereafter, deleting files older than their retention window. The worker is unit-tested with a clock abstraction (`IClock`) so retention behavior is deterministic.

### 8.5 Crash reporting

Phase 1: local crash dumps to `%LOCALAPPDATA%\MozaStarCitizen\crash-dumps\`. Use Windows Error Reporting integration plus a minidump emitted by an `UnhandledExceptionHandler` that captures `Process.GetCurrentProcess().GetMiniDump()` at the time of fault.

Phase 2: Sentry behind explicit opt-in. The opt-in is presented during first-run wizard and is re-confirmed every 90 days. The Sentry SDK is configured with `BeforeSend` scrubbing that strips file paths, machine names, environment variables, and any string matching the redaction denylist.

---

## 9. Force-Feedback Effect Catalog

### 9.1 Phase 1 effect catalog (7 effects)

The Phase 1 catalog lives in `Moza.ScLink.Effects/Catalogs/phase1.json`. Each effect has the following shape:

```json
{
  "schemaVersion": 1,
  "effects": [
    {
      "effectId": "quantum-spool-v1",
      "category": "Flight",
      "effectType": "PeriodicWithEnvelope",
      "baseIntensity": 0.42,
      "frequencyHz": 34,
      "durationMs": 8000,
      "directionX": 0,
      "directionY": 1,
      "envelope": {
        "attackMs": 250,
        "holdMs": 7000,
        "decayMs": 500,
        "releaseMs": 250,
        "attackLevel": 0.3,
        "sustainLevel": 1.0
      },
      "isSustained": true,
      "stateKey": "quantum-spool",
      "stoppedBy": ["QuantumSpoolEnded", "QuantumJumpStarted", "EmergencyStop"]
    }
  ]
}
```

Phase 1 effects: `quantum-spool-v1`, `quantum-jump-exit-v1`, `atmosphere-entry-v1`, `atmosphere-exit-v1`, `landing-contact-v1`, `weapon-fire-generic-v1`, `vehicle-destruction-v1`. Detailed parameters are in `docs/effects/phase1-catalog.md` and are open to product-driven tuning during Phase 1 testing.

### 9.2 Phase 2 catalog expansion

The Phase 2 catalog adds: `atmospheric-buffet-sustained`, `hull-impact-light`, `hull-impact-heavy`, `shield-hit-v1`, `weapon-fire-ballistic-light`, `weapon-fire-ballistic-heavy`, `weapon-fire-energy-laser`, `weapon-fire-energy-cannon`, `missile-launch-v1`, `thruster-modulation-v1`, `quantum-travel-sustained-v1`. Phase 2 also introduces effect-modulation hooks that allow sustained effects' intensity to be updated continuously from sensor signals (e.g., buffet intensity follows throttle position).

### 9.3 Per-device safety caps

| Device | `MaxIntensityRecommended` | Notes |
|---|---|---|
| AB6 | 0.85 | Calibrated from MOZA published specs; verified on hardware in Phase 1 |
| AB9 | 0.85 | Same default; AB9 has higher headroom; refine after hardware testing |
| Unknown | 0.0 (no output) | Device must be in allowlist; otherwise no commands sent |

---

## 10. Phase 1 — Functional MVP

Phase 1 is scoped to 4–6 weeks of engineering work. It establishes the platform foundation and proves the Vortice.DirectInput output path end-to-end on real hardware.

### 10.1 Phase 1 deliverables

1. .NET 8 WPF solution with the layout in Section 3.
2. Vortice.DirectInput output implementation passing the seven Phase 1 effects on AB6 and/or AB9 hardware.
3. No SharpDX anywhere.
4. MOZA managed SDK removed from active fallback chain (retained as a diagnostic probe only).
5. Preview mode that activates when no MOZA device is detected. The app starts, the UI is functional, and the test sequence prints to logs without driving any hardware.
6. Phase 1 effect catalog JSON loaded at startup, hot-reloadable.
7. Device profile JSON loaded at startup with AB6 and AB9 defaults.
8. Application settings JSON with schema versioning and atomic writes.
9. `System.Threading.Channels` event bus wired end-to-end (Log sensor → Fusion → Effect resolver → Output worker).
10. Log sensor that tails `Game.log` and parses against a versioned pattern library (`Moza.ScLink.Logs/Patterns/v0.json`). Patterns may be marked `"unsupported": true` if not yet validated against real Star Citizen logs; the sensor emits no events for unsupported patterns.
11. Hot reload of `event-patterns.json` and `fusion-rules.json` via `FileSystemWatcher` with debounce.
12. Basic fusion engine implementing the rule schema in §7.1.
13. Emergency stop: a single user action (UI button + global hotkey, default `Ctrl+Alt+F12`) that stops all effects within `EmergencyStopMaxLatencyMs`.
14. Test sequence button: fires each Phase 1 effect in turn, ~1.5s each, with a 500 ms gap.
15. Diagnostics panel showing: selected output device, device state, event counters (sensor / fusion / effect / output), drop counters, last 50 events, log file path, app version.
16. Local structured logs via Serilog.
17. Unit tests per Section 12.
18. Build/test/run documentation in `docs/build.md`.

### 10.2 Phase 1 explicitly NOT in scope

Audio capture and classifiers; screen capture; input mirroring; ship profiles; first-run wizard; installer; auto-update; crash reporting to external services; ONNX inference; community profiles; OBS overlay.

### 10.3 Phase 1 exit criteria

The following must all be true for Phase 1 to be considered complete:

- [ ] `dotnet build --configuration Release` succeeds with zero warnings at WarningLevel 5.
- [ ] `dotnet test --configuration Release` passes with zero failures.
- [ ] Code coverage thresholds in §0.3 are met.
- [ ] App starts on a clean Windows 11 machine without Star Citizen installed.
- [ ] App starts on a clean Windows 11 machine without any MOZA hardware connected and enters preview mode safely.
- [ ] When AB6 is connected, `DeviceModel.MozaAb6` is reported and all seven test-sequence effects play.
- [ ] When AB9 is connected, `DeviceModel.MozaAb9` is reported and all seven test-sequence effects play.
- [ ] Emergency stop halts all active effects within 50 ms (measured in `EmergencyStopLatencyTests`).
- [ ] Hot reload of `event-patterns.json` is observed in logs and takes effect on the next matching line.
- [ ] Malformed `event-patterns.json` does not crash the app; the prior good pattern set remains in effect and a warning is logged.
- [ ] Device disconnect during effect playback does not crash the app; the device transitions to `Disconnected` and the next reconnect triggers re-initialization.
- [ ] No reference to SharpDX exists in the solution (`grep -ri sharpdx` returns nothing).
- [ ] No reference to memory reading, injection, or kernel-level APIs exists (`grep -ri "OpenProcess\|VirtualAlloc\|WriteProcessMemory\|NtCreate" src/` returns nothing).
- [ ] No raw audio, screenshot, or game-log content is transmitted off the machine.
- [ ] All Section 4 dependencies have DDRs committed.
- [ ] `docs/build.md`, `docs/architecture/overview.md`, and per-task decision memos for any deviations are present.

---

## 11. Phase 2 — Competitive Public Beta

Phase 2 is scoped to 6–8 weeks after Phase 1 ships. It adds the multi-sensor fusion that elevates the product from "log-driven utility" to "MSFS-quality feel."

### 11.1 Phase 2 deliverables

1. NAudio WASAPI endpoint loopback audio sensor.
2. Audio feature extraction per §2.5.
3. Heuristic audio classifiers per §6.1.
4. Endpoint-mix contamination detection and user warning (the audio sensor detects when non-game audio likely contributed to a false-positive by cross-referencing with active session windows).
5. Windows.Graphics.Capture screen sensor with ROI analysis.
6. DirectInput input mirroring (player's flight stick, throttle, pedals) at 30 Hz.
7. Full fusion engine with all rules in §7.1 schema.
8. Phase 2 effect catalog expansion (§9.2) — 15 effects total.
9. Effect envelopes via Vortice.DirectInput `EnvelopeParameters`.
10. Sustained-effect intensity modulation from continuous sensor signals.
11. Three default ship profiles: fighter, heavy, ground.
12. Profile import/export via JSON.
13. First-run wizard.
14. Sensor consent UX (each sensor has its own enable/disable + a clear explanation of what it does).
15. Advanced diagnostics with per-sensor health, classifier confidence histograms, and event timing graphs.
16. Optional local training-data feature capture (private beta only, explicit opt-in).
17. Optional Sentry crash reporting with explicit opt-in.
18. GitHub Releases-compatible auto-updater with in-app notification.
19. WiX installer (or MSIX).
20. Privacy policy published; in-app link.

### 11.2 Phase 2 exit criteria

- [ ] 50-user private beta has completed at least one full Star Citizen session each.
- [ ] Average user satisfaction in beta survey ≥ 4.0 / 5.0.
- [ ] Crash-free session rate ≥ 99.5% across the beta cohort.
- [ ] No critical device-safety bugs (defined as: any incident where the device produced unintended sustained high-intensity force).
- [ ] Zero anti-cheat flags reported by beta users (EAC/Star Citizen).
- [ ] Audio sensor false-positive rate ≤ 5% during normal desktop use (measured in test scenarios with non-game audio playing).
- [ ] Screen sensor degrades safely on resolution / DPI / HDR changes without crashing.
- [ ] Endpoint-mix contamination warning fires correctly in test scenarios.
- [ ] Emergency stop is accessible globally via tray icon and global hotkey.
- [ ] All Phase 2 dependencies have DDRs committed.
- [ ] Installer passes WACK (Windows App Certification Kit) at "Pass" level.

---

## 12. Test Strategy and Quality Gates

### 12.1 Unit test requirements

Unit test projects mirror the source structure 1:1. Required unit-test coverage per project (line coverage):

| Project | Minimum coverage |
|---|---|
| Core | 80% |
| Effects | 90% (gain stack, safety limiter, envelope generation) |
| Fusion | 85% |
| Profiles | 80% |
| Diagnostics | 70% |
| Logs | 80% |
| DirectInput | 50% (much is interop) |
| Audio | 60% (Phase 2) |
| Screen | 50% (Phase 2) |
| App | 40% (mostly view models) |

### 12.2 Required unit tests (Phase 1)

- Pattern JSON parsing (happy path, malformed, unknown schema version, hot reload)
- Pattern regex compilation with timeout enforcement
- Canonical event construction and equality
- Fusion deduplication windows and edge cases
- Fusion suppression activation and deactivation
- Gain stack: every combination of zero / one / boundary input
- Envelope sample generation across all `ForceEnvelope` shapes
- Effect catalog loading and schema migration
- Device profile loading and validation
- Device matching against allowlist (positive and negative)
- Settings persistence: atomic writes, corrupted file recovery, schema upgrade
- Emergency stop signal propagation through all channels
- Safety limiter: every constant in §5.8 has a dedicated test that asserts the limiter enforces it

### 12.3 Integration tests

- Sample `Game.log` tailing against fixtures in `samples/GameLog/` (small, redacted)
- Hot reload of `event-patterns.json` updates compiled patterns within 1 second
- Sensor channel → fusion → effect resolver → output worker end-to-end with a mock device
- Preview mode end-to-end (no device, full pipeline exercised)
- Device disconnect mid-effect: cleanly transitions to disconnected, no crash
- Device reconnect after disconnect: re-initializes and resumes
- Malformed settings file: app starts with defaults
- Malformed profile file: app falls back to default profile
- Retention worker deletes files older than retention window using a fake clock

### 12.4 Manual hardware tests

Run on each of AB6 and AB9 before declaring Phase 1 complete:

- [ ] Device detected on cold start with hardware present at boot
- [ ] Device detected on hot-plug after app started
- [ ] Test sequence fires all 7 effects with audible/tactile distinction between effect types
- [ ] Emergency stop (button + hotkey) halts active sustained effects within ~50 ms perceived
- [ ] Sustained effects stop cleanly on `QuantumSpoolEnded` event
- [ ] Device disconnect during sustained effect does not crash; reconnect resumes
- [ ] Conservative default master gain (0.6) produces noticeable but not overwhelming effects
- [ ] Per-device gain scaling: changing master gain by 50% produces audibly proportional change

### 12.5 Performance targets

| Target | Threshold | Measured by |
|---|---|---|
| CPU usage during active monitoring | ≤ 5% on mid-tier system (Ryzen 5 / 12th-gen i5 class) | Performance harness in `tests/perf/` |
| Audio-sourced event → effect dispatch | ≤ 150 ms (p95) | `EffectDispatchLatencyTests` |
| Screen-sourced event → effect dispatch | ≤ 250 ms (p95) | Same |
| Log-sourced event → effect dispatch | ≤ 1500 ms (p95) | Same |
| Emergency stop → effects halt | ≤ 50 ms (p99) | `EmergencyStopLatencyTests` |
| Memory baseline after 1 hour | ≤ 200 MB working set | Long-soak test |
| Memory growth after 4 hours | ≤ 10 MB | Long-soak test (leak detector) |
| Crash-free session rate (Phase 2 beta) | ≥ 99.5% | Sentry / local crash log aggregation |

### 12.6 CI gates

GitHub Actions workflow `ci.yml` enforces, on every PR:

```yaml
- restore
- build (Release, /warnaserror)
- test (with coverage; fail under threshold)
- analyzer pass (TreatWarningsAsErrors=true)
- pattern lint (scripts/validate-patterns.ps1 against samples/GameLog/)
- forbidden-API scan (custom Roslyn analyzer that rejects: SharpDX, OpenProcess, WriteProcessMemory, NtCreate*, anything in a denylist file)
- DDR completeness check (every <PackageReference> must have a matching ddr-*.md or be in the pre-approved list)
```

---

## 13. Cost, Licensing, and Guardrails

### 13.1 Phase 1 expected zero direct licensing cost

.NET 8, WPF, Vortice.DirectInput, System.Threading.Channels, System.Text.Json, Serilog core/file sinks, xUnit, CommunityToolkit.Mvvm. License verification per dependency is mandatory before release; the DDR captures this.

### 13.2 Confirmed and probable costs

| Item | When | Estimated cost | Owner |
|---|---|---|---|
| Standard code-signing certificate | Phase 2 beta | $200–500/yr | Product / Ops |
| EV code-signing certificate (HSM-backed) | Phase 2 GA | $500–1500/yr | Product / Ops |
| Sentry plan | Phase 2 | Free tier likely; up to $26/mo if growth | Product |
| Telemetry endpoint hosting (Phase 3+) | TBD | $20–100/mo | Product |
| Documentation hosting (GitHub Pages free) | Phase 2 | $0 | Engineering |
| Trademark / legal review | Pre-launch | $1000–5000 one-time | Legal |
| MOZA partnership / co-marketing legal | Pre-launch | TBD | Business / Legal |
| Support operations | Post-launch | Variable | Product |

### 13.3 Critical guardrails (NORMATIVE)

```text
No SharpDX.
No memory reading.
No process injection.
No DLL injection.
No kernel drivers.
No Star Citizen file modification.
No Easy Anti-Cheat bypasses or interaction.
No automatic raw audio upload.
No automatic screenshot upload.
No automatic raw game-log upload.
No external telemetry without per-session user opt-in.
No manual-labeling-heavy ML pipelines.
No arbitrary user-DLL plugin execution.
No unsigned classifier packs by default (Phase 3+).
No driving of unknown force-feedback devices.
No unsafe gain defaults (master gain default = 0.6).
No claims of hardware validation without hardware in hand.
No claims of EAC safety certification without written evidence from CIG.
No code paths that could produce sustained force above device safety cap.
```

---

## 14. Migration from Existing Repository

The existing repository contains working code that must be preserved during migration. Agents do not start from a clean slate.

### 14.1 Migration sequence (mandatory order)

1. **Branch `chore/01-solution-restructure`**: Create new project structure per §3 with empty projects. Solution builds. No code moved yet. PR merges to `main`.
2. **Branch `chore/02-existing-code-move`**: Move existing files to their target projects per §3.1 mapping table. Keep behavior unchanged. Adjust namespaces. Adjust references. Existing tests (if any) still pass; app still launches and behaves as before. PR merges.
3. **Branch `chore/03-centralized-package-versions`**: Introduce `Directory.Packages.props` with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`. Move all `<PackageReference>` versions to the central props file. PR merges.
4. **Branch `chore/04-serilog-bootstrap`**: Replace `AppLog` with Serilog. Existing AppLog calls become Serilog calls via a thin shim during transition. PR merges.
5. **Branch `feat/05-vortice-directinput`**: Replace hand-rolled DirectInput COM interop with Vortice.DirectInput. The replacement must preserve the existing behavior set (effect caching, re-acquisition on `DIERR_NOTEXCLUSIVEACQUIRED`, two-axis data format, exclusive cooperative level). All existing manual tests still pass on hardware. PR merges only after on-hardware validation.
6. **Branch `feat/06-mvvm-modernization`**: Adopt CommunityToolkit.Mvvm. Refactor view models. PR merges.
7. **Branch `feat/07-channels-event-bus`**: Introduce `System.Threading.Channels` event bus replacing direct event-handler wiring. The log sensor becomes an `ISensor`, the controller becomes the resolver. PR merges.
8. **Branch `feat/08-fusion-engine`**: Add fusion engine and rule loader. PR merges.
9. **Branch `feat/09-effect-catalog-json`**: Move hard-coded effect parameters into `phase1.json` catalog. PR merges.
10. **Branch `feat/10-safety-limiter`**: Add safety limiter with constants in §5.8 and tests. PR merges.
11. **Branch `feat/11-emergency-stop`**: Add emergency-stop UI control, global hotkey, propagation through channels. PR merges.
12. **Branch `feat/12-preview-mode`**: Formalize preview mode UX. PR merges.
13. **Branch `feat/13-diagnostics-panel`**: Wire diagnostics panel showing health, counters, last events. PR merges.
14. **Branch `feat/14-screen-capture-park`**: Move existing screen-capture code into `legacy/` directory; mark obsolete; gate behind a hidden flag pending Phase 2 rewrite. PR merges.
15. **Branch `chore/15-ci-pipeline`**: Add GitHub Actions CI per §12.6 including forbidden-API scan. PR merges. **Phase 1 complete.**

### 14.2 What must not be lost in migration

The existing code is full of hard-won detail that agents must preserve unless explicitly improving. Notable items:

- `DirectInputForceFeedbackDevice` re-acquisition logic on `DIERR_NOTEXCLUSIVEACQUIRED` and `DIERR_NOTDOWNLOADED`.
- `GameLogTailer` truncation detection and seek-to-end-on-startup semantics.
- `StarCitizenLogLocator` channel detection (LIVE / PTU / EPTU / TECH-PREVIEW) and saved-vs-detected newer-log handling.
- `AppSettingsStore` corrupted-file recovery.
- `ForceFeedbackController` duplicate-impact suppression window.
- Single-instance mutex in `App.xaml.cs`.
- Dispatcher marshalling pattern in `MainViewModel`.

When migrating any of the above, add a unit test that captures the existing behavior before changing implementation.

---

## 15. Task Decomposition (Phase 1)

This section is the agent task board. Each task is sized to fit within a single Claude Opus session (typically 1–4 hours of agent work). Tasks are listed with dependencies; the dependency graph should be respected.

### Task index

| Task ID | Title | Depends on | Estimated effort |
|---|---|---|---|
| T-01 | Solution restructure with empty projects | — | S |
| T-02 | Move existing code to new project homes | T-01 | M |
| T-03 | Centralized package versions | T-02 | S |
| T-04 | Serilog bootstrap and AppLog shim | T-03 | M |
| T-05 | Pre-approved-dependency DDRs | T-03 | S |
| T-06 | Core domain types (enums, records, interfaces) | T-02 | M |
| T-07 | Vortice.DirectInput output device | T-04, T-05, T-06 | L |
| T-08 | Device allowlist and detection | T-07 | S |
| T-09 | MVVM modernization with CommunityToolkit | T-04 | M |
| T-10 | Channels-based event bus | T-06 | M |
| T-11 | Log sensor refactor to ISensor | T-06, T-10 | M |
| T-12 | Fusion engine with rule loader | T-10 | L |
| T-13 | Phase 1 effect catalog JSON | T-06 | S |
| T-14 | Effect resolver with gain stack | T-06, T-13 | M |
| T-15 | Safety limiter with constants | T-06, T-14 | M |
| T-16 | Emergency stop end-to-end | T-09, T-10, T-15 | M |
| T-17 | Preview mode formalization | T-07, T-09 | S |
| T-18 | Diagnostics panel | T-09, T-10, T-15 | M |
| T-19 | Settings persistence with schema versioning | T-06 | M |
| T-20 | Pattern hot reload | T-11 | S |
| T-21 | Park existing screen-capture code | T-02 | S |
| T-22 | CI pipeline with forbidden-API scan | All above | M |
| T-23 | Hardware validation pass on AB6/AB9 | T-07–T-22 | M |
| T-24 | Phase 1 documentation pack | All above | M |

Effort legend: S = 1–2 hours, M = 2–6 hours, L = 6–12 hours.

### Task specifications (selected — full set in `docs/tasks/`)

Each task in `docs/tasks/T-XX.md` follows this template.

#### T-07: Vortice.DirectInput output device

**Task ID:** T-07
**Title:** Replace hand-rolled DirectInput COM interop with Vortice.DirectInput in `Moza.ScLink.DirectInput`
**Depends on:** T-04 (Serilog), T-05 (DDR for Vortice.DirectInput), T-06 (Core types)

**Context.** The existing repository implements DirectInput by hand-marshaling COM interfaces (`IDirectInput8W`, `IDirectInputDevice8W`, `IDirectInputEffect`) and structs (`DirectInputEffect`, `DirectInputPeriodic`, `DirectInputConstantForce`). This works but is fragile, untestable, and reinvents Vortice.DirectInput.

**Deliverables.**
1. A `VorticeDirectInputDevice` class in `Moza.ScLink.DirectInput` implementing `IForceFeedbackDevice`.
2. A `DeviceEnumerator` that lists attached force-feedback devices via Vortice and filters against the allowlist.
3. Effect creation for: periodic (sine), constant force, periodic-with-envelope, plus a "stop all" command.
4. Re-acquisition on `DIERR_NOTEXCLUSIVEACQUIRED` and `DIERR_NOTDOWNLOADED` preserved from the existing code.
5. Two-axis Cartesian data format with `DIJOFS_X` and `DIJOFS_Y` axis selection (preserved from existing code).
6. Exclusive + background cooperative level (preserved).
7. Effect cache keyed by `(effectId, intensity-rounded, durationMs-rounded, frequencyHz-rounded, stateKey)` so identical effects are not re-downloaded.
8. Unit tests using a mock Vortice.DirectInput layer (via `IDirectInputAbstraction` introduced in this task) covering: device init lifecycle, effect creation, effect start/stop, re-acquisition flow, stop-all, and disposal.
9. Manual hardware test checklist attached to the PR description with sign-off lines.

**Acceptance criteria.**
- [ ] All Phase 1 effects play correctly on at least one of AB6 or AB9 (engineer-attested with timestamped video link in PR)
- [ ] No SharpDX references introduced
- [ ] Re-acquisition logic tested via mocked HRESULT injection
- [ ] Effect cache hit rate observable in diagnostics (count exposed via `IFusionDiagnostics`-style surface)
- [ ] Old hand-rolled COM-interop files deleted in this PR
- [ ] DDR for Vortice.DirectInput committed and approved
- [ ] No new analyzer warnings; build clean at WarningLevel 5
- [ ] Code coverage on new code ≥ 50% (interop limits coverage; mock layer compensates)

**Non-goals.** This task does not implement effect modulation (Phase 2), envelopes (deferred to T-14 wiring), or input mirroring.

---

#### T-12: Fusion engine with rule loader

**Task ID:** T-12
**Title:** Implement `Moza.ScLink.Fusion.FusionEngine` with JSON rule loader, dedupe, and suppression
**Depends on:** T-10 (event bus)

**Deliverables.**
1. `IFusionEngine` interface per §7.2.
2. `FusionEngine` class consuming the SensorEvent channel and producing the GameEvent channel.
3. `FusionRuleLoader` that loads rules from `%LOCALAPPDATA%\MozaStarCitizen\fusion-rules.json` with fallback to embedded defaults.
4. Sliding-window evidence store (max 5 seconds) for cross-sensor correlation.
5. Dedupe windowing per rule.
6. Suppression activation/deactivation handling.
7. Confidence boost computation per the rule schema.
8. Unit tests covering: rule loading happy/error paths, dedupe within and outside window, suppression activation, suppression deactivation, confidence boost when corroborating event present, confidence floor rejection, hot reload.

**Acceptance criteria.**
- [ ] Default rule set loads when no user file exists
- [ ] Malformed user file is moved to `.unsupported-<timestamp>` and defaults load
- [ ] Hot reload of `fusion-rules.json` takes effect within 1 second
- [ ] All listed unit tests pass
- [ ] Coverage on `Moza.ScLink.Fusion` ≥ 85%
- [ ] Diagnostics expose: rule count, dedupe drop count, suppression activation count, last 50 fusion decisions

---

#### T-16: Emergency stop end-to-end

**Task ID:** T-16
**Title:** Implement emergency stop with UI button, global hotkey, and channel propagation
**Depends on:** T-09 (MVVM), T-10 (event bus), T-15 (safety limiter)

**Deliverables.**
1. `EmergencyStopController` in `Moza.ScLink.Core` exposing `Activate()` and an observable state.
2. UI button bound to the controller in `MainWindow`.
3. Global hotkey registration via `RegisterHotKey` Win32 with default `Ctrl+Alt+F12`, hotkey rebinding in settings.
4. On activation: emit `GameEventType.EmergencyStop`; the resolver consumes it and emits a `StopAllCommand`; the output worker prioritizes this command (drains pending playable commands first).
5. Latency measurement: from `Activate()` call to last device command sent, recorded in diagnostics.
6. Unit tests: activation propagates through every channel; latency under 50 ms in a synthetic environment.

**Acceptance criteria.**
- [ ] Pressing the hotkey from anywhere on Windows triggers emergency stop
- [ ] Pressing the UI button triggers emergency stop
- [ ] All active sustained effects are stopped on hardware (manual test)
- [ ] Latency p99 < 50 ms in `EmergencyStopLatencyTests`
- [ ] Emergency stop also suppresses new haptic events (Combat/Flight/Environment) until manually re-enabled or 30 seconds elapse (configurable)
- [ ] State is observable in diagnostics

---

*(Tasks T-01 through T-24 follow the same template. Full task specs live in `docs/tasks/T-XX.md` and are authored by the agent picking up each task — the spec must be reviewed and approved before implementation begins.)*

---

## 16. Version 2.0 — Haptics Intelligence Platform (Forward Roadmap)

Version 2.0 transforms the product from a useful companion utility into a local haptics intelligence platform: safe, adaptive, profile-driven, community-tunable, patch-resilient, and ML-assisted without compromising user trust.

### 16.1 Core 2.0 capabilities

- **Adaptive haptics engine.** Effect parameters adapt based on ship profile, flight mode, combat state, atmospheric state, input intensity, user gain preferences, event confidence, and session-level false-positive history.
- **ML-assisted audio classifiers.** ONNX local inference for hard classes: ballistic vs energy weapon separation; hull-impact vs shield-hit discrimination; explosion vs environmental transient; atmospheric wind vs non-game audio.
- **Signed classifier packs.** Loadable ONNX bundles with ONNX model file, feature schema, supported event types, confidence thresholds, fallback heuristic settings, metadata, signature, compatibility version.
- **Community profile ecosystem.** URL import, signed official profiles, community profiles, ratings, schema validation, conflict-safe merge, profile rollback.
- **Patch-resilience dashboard.** Surfaces detected Star Citizen build, pattern library version, classifier pack version, compatibility status, last successful event detection, broken sensors, recommended update.
- **Privacy-preserving diagnostics.** Diagnostic bundles with user review, safe defaults, explicit raw-artifact inclusion only when chosen, on-device redaction.
- **Effect composer.** Power-user effect creation with envelope, frequency, direction, gain, preview, export, hard safety limits.
- **Streamer / OBS mode.** Optional overlay showing active effects, current profile, sensor health, haptics intensity, recent events.
- **Controlled extension boundary.** JSON profiles, JSON fusion rules, signed classifier packs, signed official effect catalogs, signed sensor packs. No arbitrary DLL plugins by default.

### 16.2 2.0 services

```text
ModelPackService              loads and verifies signed ONNX classifier packs
ClassifierPackVerifier        signature validation against trusted root
ProfileRegistry               local and community profile storage / lookup
PatchCompatibilityService     detects Star Citizen build; checks pack compat
AdaptiveTuningService         session-level false-positive learning
DiagnosticBundleService       privacy-reviewed export
EffectComposerService         in-app effect authoring with safety enforcement
OverlayExportService          OBS-compatible output
```

### 16.3 2.0 release theme

"Local haptics intelligence platform." The marketing and design language emphasizes local-first, user-controlled, patch-resilient, community-extensible — all without compromising on the privacy, safety, and anti-cheat-respect guardrails established in 1.x.

---

## 17. One-Paragraph Agent Header

Build MOZA Star Citizen Link as a modern .NET 8 Windows desktop haptics platform for MOZA AB6 and AB9 flight bases targeting Star Citizen. Use WPF with CommunityToolkit.Mvvm, Vortice.DirectInput exclusively for force-feedback output, System.Threading.Channels for the in-process event pipeline, System.Text.Json for versioned JSON profiles and catalogs, NAudio WASAPI endpoint loopback for Phase 2 audio capture, Windows.Graphics.Capture for Phase 2 screen capture, Serilog for local diagnostics, and local-first beta instrumentation behind explicit consent. Do not use SharpDX. Do not use memory reading, DLL injection, process injection, kernel drivers, or any anti-cheat-adjacent technique. Phase 1 builds the modern DirectInput / event / effect / safety foundation across the task graph in Section 15, with seven log-driven effects, preview mode, emergency stop, diagnostics, hot-reloadable patterns, and a clean test suite. Phase 2 adds audio, screen, and input sensors with heuristic classifiers, the full fifteen-effect catalog, three ship profiles, a first-run wizard, an installer, and opt-in crash reporting. Version 2.0 adds weak-supervision-driven dataset capture, ONNX local inference, signed classifier packs, community profiles, adaptive tuning, an effect composer, an OBS overlay mode, and a patch-resilience dashboard. ML must never depend on large manual labeling; use weak labels, cross-sensor correlation, clustering, and exception review. Preserve every hard-won behavior in the existing repository during migration — list each in your PR description and add a regression test before any refactor.

---

## Appendix A — References

- Vortice.DirectInput NuGet: https://www.nuget.org/packages/Vortice.DirectInput
- NAudio WASAPI loopback documentation: https://github.com/naudio/NAudio/blob/master/Docs/WasapiLoopbackCapture.md
- Microsoft WASAPI loopback recording: https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording
- Microsoft Windows.Graphics.Capture: https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture
- Microsoft screen-capture overview: https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture
- ONNX Runtime documentation: https://onnxruntime.ai/docs/
- Serilog: https://serilog.net/
- OWASP Logging Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html
- NIST SSDF SP 800-218: https://csrc.nist.gov/pubs/sp/800/218/final
- Conventional Commits: https://www.conventionalcommits.org/
- Architectural Decision Records template: https://adr.github.io/

## Appendix B — Glossary

| Term | Definition |
|---|---|
| ADR | Architectural Decision Record — a short markdown file capturing a single architectural decision and its context |
| DDR | Dependency Decision Record — per-package decision per §4 |
| DI | DirectInput (Windows API), or dependency injection (depending on context) |
| EAC | Easy Anti-Cheat |
| FFB | Force feedback |
| MVP | Minimum viable product |
| PRP | Product Requirements Prompt — this document |
| ROI | Region of interest (screen capture analysis) |
| WASAPI | Windows Audio Session API |

---

*End of PRP v2.0. For questions or amendment proposals, open a draft PR against this file with a `docs/decisions/prp-amend-<topic>.md` decision memo.*
