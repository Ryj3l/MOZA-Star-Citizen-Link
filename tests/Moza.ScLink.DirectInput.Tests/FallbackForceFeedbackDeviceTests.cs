using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Models;
using NSubstitute;
using LegacyDevice = Moza.ScLink.Core.IForceFeedbackDevice;
using LegacyForceEffect = Moza.ScLink.Core.Models.ForceEffect;
using NewDevice = Moza.ScLink.Core.Devices.IForceFeedbackDevice;

// The chain composes LegacyForceFeedbackDeviceAdapter (Obsolete) instances; the entire file exercises
// that path, so the warning is disabled file-wide rather than peppered at every instantiation.
#pragma warning disable CS0618

namespace Moza.ScLink.DirectInput.Tests;

/// <summary>
/// Unit tests for the T-07 Issue #27 Pass-2 device-availability state machine on
/// <see cref="FallbackForceFeedbackDevice"/>. Covers the eight scenarios listed in the
/// Pass-2 investigation plan §D2 — hot-loss → Null transition; Policy 3 DI-scope ignore;
/// hot-arrival via the factory delegate; null-provider no-op; new-instance-GUID handling
/// (F2b); disposal-before-event ordering; underlying-device StateChanged propagation;
/// race-safety with concurrent <c>PlayAsync</c>.
/// </summary>
public sealed class FallbackForceFeedbackDeviceTests
{
    // ── Fixture helpers ────────────────────────────────────────────────────────────

    private static NewDevice ASubstituteNewDevice(string productName = "Test FFB Base")
    {
        var device = Substitute.For<NewDevice>();
        device.ProductName.Returns(productName);
        device.DisplayName.Returns(productName);
        device.InstanceGuid.Returns(Guid.NewGuid());
        device.Model.Returns(DeviceModel.MozaAb9);
        return device;
    }

    private static LegacyForceFeedbackDeviceAdapter AnAdapter(NewDevice wrapped) =>
        new(wrapped, NullLogger<LegacyForceFeedbackDeviceAdapter>.Instance);

    private static NullForceFeedbackDevice ANullTier() =>
        new("test-null-tier");

    /// <summary>
    /// Waits up to <paramref name="timeoutMs"/> ms for <see cref="FallbackForceFeedbackDevice.ChainStateChanged"/>
    /// to fire at least once after this helper is wired. Returns the captured args; throws on timeout.
    /// </summary>
    private static async Task<ChainStateChangedEventArgs> WaitForChainStateChangedAsync(
        FallbackForceFeedbackDevice fallback,
        int timeoutMs = 2000)
    {
        var tcs = new TaskCompletionSource<ChainStateChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, ChainStateChangedEventArgs e)
        {
            tcs.TrySetResult(e);
        }

        fallback.ChainStateChanged += Handler;
        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            completed.Should().Be(tcs.Task, $"ChainStateChanged did not fire within {timeoutMs} ms");
            return await tcs.Task;
        }
        finally
        {
            fallback.ChainStateChanged -= Handler;
        }
    }

    // ── §D2 tests 1-8 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnDeviceRemovedSwapsDirectInputSlotToNullWhenDirectInputIsCurrent()
    {
        var underlying = ASubstituteNewDevice("MOZA AB9 FFB Base");
        var diAdapter = AnAdapter(underlying);
        var nullTier = ANullTier();
        var fallback = new FallbackForceFeedbackDevice(new LegacyDevice[] { diAdapter, nullTier });
        await fallback.InitializeAsync(CancellationToken.None);

        // Sanity: DI tier won initialization and is current.
        fallback.Name.Should().Contain("DirectInput");

        var waitTask = WaitForChainStateChangedAsync(fallback);
        fallback.OnDeviceRemoved();
        var args = await waitTask;

        args.IsReady.Should().BeFalse();
        args.OutputName.Should().Contain("Preview");
        fallback.Name.Should().Contain("Preview");
        await underlying.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task OnDeviceRemovedIgnoresWhenNonDirectInputIsCurrent()
    {
        // Policy 3 DI-scope: only the DirectInput tier responds to hot-loss. If the currently-active
        // tier is anything else (here: the Null fallback because the DI init throws), OnDeviceRemoved
        // is a no-op — the SDK/Null tiers have no WM_DEVICECHANGE observability and stay init-time
        // selected. The test fails-loud if the chain raises ChainStateChanged from this path.
        var failingUnderlying = ASubstituteNewDevice("Failing FFB");
        failingUnderlying
            .InitializeAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("simulated DI init failure")));
        var diAdapter = AnAdapter(failingUnderlying);
        var nullTier = ANullTier();
        var fallback = new FallbackForceFeedbackDevice(new LegacyDevice[] { diAdapter, nullTier });
        await fallback.InitializeAsync(CancellationToken.None);

        // Sanity: DI failed; Null tier is current.
        fallback.Name.Should().Contain("Preview");

        var raised = 0;
        fallback.ChainStateChanged += (_, _) => Interlocked.Increment(ref raised);
        fallback.OnDeviceRemoved();
        await Task.Delay(200);  // generous wait for any async dispatch to settle

        raised.Should().Be(0);
        fallback.Name.Should().Contain("Preview");
    }

    [Fact]
    public async Task OnDeviceArrivedRetriesChainTopWhenDirectInputProviderReturnsDevice()
    {
        // Chain starts with no DI tier — only Null. On arrival, the provider creates a fresh adapter
        // and the chain promotes it to current.
        var nullTier = ANullTier();
        NewDevice? freshUnderlying = null;
        LegacyDevice? Provider()
        {
            freshUnderlying = ASubstituteNewDevice("Fresh AB9");
            return AnAdapter(freshUnderlying);
        }

        var fallback = new FallbackForceFeedbackDevice(new LegacyDevice[] { nullTier }, Provider);
        await fallback.InitializeAsync(CancellationToken.None);
        fallback.Name.Should().Contain("Preview");

        var waitTask = WaitForChainStateChangedAsync(fallback);
        fallback.OnDeviceArrived();
        var args = await waitTask;

        args.IsReady.Should().BeTrue();
        args.OutputName.Should().Contain("DirectInput");
        args.OutputName.Should().Contain("Fresh AB9");
        fallback.Name.Should().Contain("Fresh AB9");
        freshUnderlying.Should().NotBeNull();
        await freshUnderlying!.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDeviceArrivedNoOpsWhenDirectInputProviderReturnsNull()
    {
        // USB-stack race: WM_DEVICECHANGE fires before the device is enumerable. Provider returns
        // null; chain must stay in Null state silently — the next DBT_DEVICEARRIVAL retries.
        var nullTier = ANullTier();
        LegacyDevice? Provider() => null;

        var fallback = new FallbackForceFeedbackDevice(new LegacyDevice[] { nullTier }, Provider);
        await fallback.InitializeAsync(CancellationToken.None);

        var raised = 0;
        fallback.ChainStateChanged += (_, _) => Interlocked.Increment(ref raised);
        fallback.OnDeviceArrived();
        await Task.Delay(200);

        raised.Should().Be(0);
        fallback.Name.Should().Contain("Preview");
    }

    [Fact]
    public async Task OnDeviceArrivedHandlesNewInstanceGuid()
    {
        // F2b: USB re-enumeration yields a different DirectInput instance GUID on replug. The provider
        // returns a fresh adapter on each call; the chain accepts the new identity and re-initializes
        // against it. The test exercises arrive → remove → arrive and asserts the second arrival's
        // adapter (with a different ProductName as a proxy for "different identity") becomes current.
        var nullTier = ANullTier();
        var callCount = 0;
        NewDevice? firstUnderlying = null;
        NewDevice? secondUnderlying = null;
        LegacyDevice? Provider()
        {
            callCount++;
            if (callCount == 1)
            {
                firstUnderlying = ASubstituteNewDevice("AB9-Instance-One");
                return AnAdapter(firstUnderlying);
            }
            secondUnderlying = ASubstituteNewDevice("AB9-Instance-Two");
            return AnAdapter(secondUnderlying);
        }

        var fallback = new FallbackForceFeedbackDevice(new LegacyDevice[] { nullTier }, Provider);
        await fallback.InitializeAsync(CancellationToken.None);

        var firstArrival = WaitForChainStateChangedAsync(fallback);
        fallback.OnDeviceArrived();
        (await firstArrival).OutputName.Should().Contain("Instance-One");

        var removal = WaitForChainStateChangedAsync(fallback);
        fallback.OnDeviceRemoved();
        (await removal).IsReady.Should().BeFalse();

        var secondArrival = WaitForChainStateChangedAsync(fallback);
        fallback.OnDeviceArrived();
        var second = await secondArrival;

        second.IsReady.Should().BeTrue();
        second.OutputName.Should().Contain("Instance-Two");
        fallback.Name.Should().Contain("Instance-Two");
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task OnDeviceRemovedDisposesPreviousAdapterBeforeReplacement()
    {
        // Ordering invariant: subscribers that observe ChainStateChanged with IsReady=false must see
        // a chain in which the prior adapter's underlying device is already disposed — never a
        // half-disposed state. The handler snapshots NSubstitute's received-calls log; the
        // post-event assertion uses Received() for the formal call-count check.
        var underlying = ASubstituteNewDevice("AB9-PreReplace");
        var diAdapter = AnAdapter(underlying);
        var nullTier = ANullTier();
        var fallback = new FallbackForceFeedbackDevice(new LegacyDevice[] { diAdapter, nullTier });
        await fallback.InitializeAsync(CancellationToken.None);

        var disposedAtEventTime = false;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fallback.ChainStateChanged += (_, _) =>
        {
            disposedAtEventTime = underlying.ReceivedCalls()
                .Any(c => c.GetMethodInfo().Name == nameof(IAsyncDisposable.DisposeAsync));
            tcs.TrySetResult(true);
        };

        fallback.OnDeviceRemoved();
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        completed.Should().Be(tcs.Task);

        disposedAtEventTime.Should().BeTrue("ChainStateChanged subscribers must never observe a half-disposed state");
        await underlying.Received(1).DisposeAsync();
    }

    // Plan D2 test 7 (ChainStateChangedFiresOnUnderlyingDeviceStateChanged) removed: B2 #4
    // StateChanged aggregation was descoped from Pass-2 (issue #34) because the test exercised
    // empirically-dead code — DeviceState.Faulted is not emitted in production and the chain's
    // subscribe-after-init / unsubscribe-before-dispose pattern misses the only two transitions
    // (Ready, Disconnected) that VorticeDirectInputDevice actually fires. Re-add when Faulted is
    // wired (see #34 for the re-entry plan).

    [Fact]
    public async Task ChainReSelectionIsRaceSafeWithConcurrentPlay()
    {
        // Smoke test: fire OnDeviceRemoved while a PlayAsync is in flight. Both either complete
        // cleanly OR the play falls through to the next tier via the existing TryDevicesAsync catch.
        // The acceptance bar is: no exception escapes either operation; chain ends in the expected
        // post-removal state (Null/Preview).
        var slowUnderlying = ASubstituteNewDevice("AB9-Slow");
        slowUnderlying
            .ExecuteAsync(Arg.Any<Moza.ScLink.Core.Effects.ForceCommand>(), Arg.Any<CancellationToken>())
            .Returns(async _ => { await Task.Delay(100); });
        var diAdapter = AnAdapter(slowUnderlying);
        var nullTier = ANullTier();
        var fallback = new FallbackForceFeedbackDevice(new LegacyDevice[] { diAdapter, nullTier });
        await fallback.InitializeAsync(CancellationToken.None);

        var effect = new LegacyForceEffect(
            ForceEffectKind.PeriodicVibration,
            "race-test",
            0.5,
            TimeSpan.FromMilliseconds(50),
            30,
            StateKey: null);

        var playTask = fallback.PlayAsync(effect, CancellationToken.None);
        await Task.Delay(20);  // ensure play is mid-flight

        var removalAct = () => { fallback.OnDeviceRemoved(); return Task.CompletedTask; };
        await removalAct.Should().NotThrowAsync();

        var playAct = async () => await playTask;
        await playAct.Should().NotThrowAsync();

        // Allow the async removal dispatch to settle.
        await Task.Delay(200);
        fallback.Name.Should().Contain("Preview");
    }

    // ── D2 tests 9 and 10: A6 bug-class regression guards ─────────────────────────────────
    // V9 hardware acceptance (Issue #27 Pass-2) surfaced a dual-source bug: `_devices`
    // (immutable, ctor-set) and `_directInputSlot` (mutable, hot-plug-updated) silently
    // diverge after hot-arrival. PlayAsync iterating `_devices` hits the STALE adapter
    // whose wrapped device is disposed → ObjectDisposedException → chain demotes to Preview.
    // The operator's A/B re-test on real hardware caught it; 161 green unit tests and the
    // full Stage-3 verify all missed. Tests 9 and 10 close the V1 gap.

    [Fact]
    public async Task PlayAsyncAfterHotArrivalRoutesToFreshSlotNotStaleArrayEntry()
    {
        // Reproduces the A6 hardware bug at unit-test scale. RED against current HEAD: the
        // initialUnderlying ExecuteAsync receives the play call (because TryDevicesAsync
        // iterates _devices and hits the stale initialAdapter first); freshUnderlying gets
        // nothing. After C: AllDevices() prepends the live _directInputSlot (freshAdapter),
        // so freshUnderlying receives the play.
        var initialUnderlying = ASubstituteNewDevice("AB9-initial");
        var initialAdapter = AnAdapter(initialUnderlying);
        var freshUnderlying = ASubstituteNewDevice("AB9-fresh");
        var freshAdapter = AnAdapter(freshUnderlying);
        var nullTier = ANullTier();
        LegacyDevice? Provider() => freshAdapter;

        var fallback = new FallbackForceFeedbackDevice(new LegacyDevice[] { initialAdapter, nullTier }, Provider);
        await fallback.InitializeAsync(CancellationToken.None);

        var removalWait = WaitForChainStateChangedAsync(fallback);
        fallback.OnDeviceRemoved();
        await removalWait;

        var arrivalWait = WaitForChainStateChangedAsync(fallback);
        fallback.OnDeviceArrived();
        await arrivalWait;

        var effect = new LegacyForceEffect(
            ForceEffectKind.PeriodicVibration,
            "post-arrival-test",
            0.5,
            TimeSpan.FromMilliseconds(50),
            30,
            StateKey: null);
        await fallback.PlayAsync(effect, CancellationToken.None);

        await freshUnderlying.Received(1).ExecuteAsync(
            Arg.Any<Moza.ScLink.Core.Effects.ForceCommand>(),
            Arg.Any<CancellationToken>());
        await initialUnderlying.DidNotReceive().ExecuteAsync(
            Arg.Any<Moza.ScLink.Core.Effects.ForceCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StatusReflectsLiveSlotAfterHotArrival()
    {
        // Second symptom of the same dual-source bug class: Fallback.Status iterates _devices,
        // which still contains the stale initialAdapter post-hot-arrival. RED against current
        // HEAD: Status contains "AB9-initial". After C: AllDevices() routing means Status
        // contains "AB9-fresh" (the live slot).
        var initialUnderlying = ASubstituteNewDevice("AB9-initial");
        var initialAdapter = AnAdapter(initialUnderlying);
        var freshUnderlying = ASubstituteNewDevice("AB9-fresh");
        var freshAdapter = AnAdapter(freshUnderlying);
        var nullTier = ANullTier();
        LegacyDevice? Provider() => freshAdapter;

        var fallback = new FallbackForceFeedbackDevice(new LegacyDevice[] { initialAdapter, nullTier }, Provider);
        await fallback.InitializeAsync(CancellationToken.None);

        var removalWait = WaitForChainStateChangedAsync(fallback);
        fallback.OnDeviceRemoved();
        await removalWait;

        var arrivalWait = WaitForChainStateChangedAsync(fallback);
        fallback.OnDeviceArrived();
        await arrivalWait;

        fallback.Status.Should().Contain("AB9-fresh");
        fallback.Status.Should().NotContain("AB9-initial");
    }
}
