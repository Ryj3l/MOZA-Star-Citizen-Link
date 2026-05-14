using FluentAssertions;
using Moza.ScLink.DirectInput;
using SharpGen.Runtime;

namespace Moza.ScLink.DirectInput.Tests;

/// <summary>
/// Tests for <see cref="DirectInputErrorClassifier"/>'s three-tier fallback strategy.
/// HRESULT constants chosen to match real DIERR_* values that Vortice surfaces in practice
/// (verified empirically during T-07 Milestone 0 against the operator's AB9 hardware).
/// </summary>
public sealed class DirectInputErrorClassifierTests
{
    // ── Tier 2: System.Exception.HResult on a non-SharpGen exception ─────────────────────

    [Fact]
    public void ClassifierReturnsNeedsReacquireForHresult80040205()
    {
        var ex = new HResultException(unchecked((int)0x80040205));

        DirectInputErrorClassifier.Classify(ex).Should().Be(DirectInputErrorClass.NeedsReacquire);
    }

    [Fact]
    public void ClassifierReturnsNeedsRedownloadForHresult80040203()
    {
        var ex = new HResultException(unchecked((int)0x80040203));

        DirectInputErrorClassifier.Classify(ex).Should().Be(DirectInputErrorClass.NeedsRedownload);
    }

    [Fact]
    public void ClassifierReturnsTransientForHresult80040208EffectPlaying()
    {
        var ex = new HResultException(unchecked((int)0x80040208));

        DirectInputErrorClassifier.Classify(ex).Should().Be(DirectInputErrorClass.Transient);
    }

    [Fact]
    public void ClassifierReturnsFatalForUnknownHresult()
    {
        var ex = new HResultException(unchecked((int)0x80004005));  // E_FAIL, not a DIERR_* we recognize

        DirectInputErrorClassifier.Classify(ex).Should().Be(DirectInputErrorClass.Fatal);
    }

    // ── Tier 3: message-substring fallback when HResult is 0 ──────────────────────────────

    [Fact]
    public void ClassifierFallsBackToMessageMatchWhenHresultIsZero()
    {
        // HResult left at the default 0; classifier must read the ApiCode mnemonic from Message.
        // Mnemonics match the format Vortice produces: "ApiCode: [DIERR_NOTEXCLUSIVEACQUIRED/NotExclusiveAcquired]".
        var reacquire = new MessageOnlyException("HRESULT: [0x80040205], ApiCode: [DIERR_NOTEXCLUSIVEACQUIRED/NotExclusiveAcquired]");
        var redownload = new MessageOnlyException("HRESULT: [0x80040203], ApiCode: [DIERR_NOTDOWNLOADED/NotDownloaded]");
        var transient = new MessageOnlyException("HRESULT: [0x80040208], ApiCode: [DIERR_EFFECTPLAYING/EffectPlaying]");
        var fatal = new MessageOnlyException("HRESULT: [0x80004005], ApiCode: [E_FAIL/Failure]");

        DirectInputErrorClassifier.Classify(reacquire).Should().Be(DirectInputErrorClass.NeedsReacquire);
        DirectInputErrorClassifier.Classify(redownload).Should().Be(DirectInputErrorClass.NeedsRedownload);
        DirectInputErrorClassifier.Classify(transient).Should().Be(DirectInputErrorClass.Transient);
        DirectInputErrorClassifier.Classify(fatal).Should().Be(DirectInputErrorClass.Fatal);
    }

    // ── Tier 1: SharpGenException is the production exception type ───────────────────────
    // (Plan §H named 5 tests by HRESULT but did not name a SharpGenException-specific case.
    // This 6th test covers Tier 1, which is the actual production path verified at M0.)

    [Fact]
    public void ClassifierReturnsNeedsReacquireForSharpGenExceptionWithResultCodeNotExclusiveAcquired()
    {
        var ex = new SharpGenException(new Result(unchecked((int)0x80040205)), "DI re-acquire test", innerException: null);

        DirectInputErrorClassifier.Classify(ex).Should().Be(DirectInputErrorClass.NeedsReacquire);
    }

    // ── Guard: null argument ─────────────────────────────────────────────────────────────

    [Fact]
    public void ClassifierThrowsArgumentNullExceptionForNullException()
    {
        var act = () => DirectInputErrorClassifier.Classify(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Exception subclass exposing a public <c>HResult</c> setter. System.Exception declares the setter as
    /// <c>protected</c>, so tests cannot set the value directly on a base <see cref="Exception"/> instance.
    /// </summary>
    private sealed class HResultException : Exception
    {
        public HResultException(int hresult)
            : base("test exception")
        {
            HResult = hresult;
        }
    }

    /// <summary>
    /// Exception subclass with <c>HResult = 0</c> and a caller-provided message — exercises the Tier-3
    /// message-substring fallback path. Direct <c>new Exception(string)</c> trips CA2201 (too-generic
    /// exception type) under analyzer level 5. Also, <see cref="Exception"/>'s default HResult is
    /// <c>0x80131500</c> (COR_E_EXCEPTION), not 0 — so we must explicitly zero it here for the Tier-3
    /// fallback to actually be reached.
    /// </summary>
    private sealed class MessageOnlyException : Exception
    {
        public MessageOnlyException(string message)
            : base(message)
        {
            HResult = 0;
        }
    }
}
