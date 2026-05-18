using SharpGen.Runtime;

namespace Moza.ScLink.DirectInput;

/// <summary>
/// Coarse classification of DirectInput error conditions surfaced as exceptions by Vortice.
/// Maps every recoverable error to one of three retry strategies; everything else is <see cref="Fatal"/>.
/// </summary>
public enum DirectInputErrorClass
{
    /// <summary>Effect is already playing or transiently busy. Single-retry safe (Stop+Start pattern from PRP §14.2).</summary>
    Transient,

    /// <summary>Device lost exclusive acquisition (<c>DIERR_NOTEXCLUSIVEACQUIRED</c> / <c>0x80040205</c>). Re-acquire and retry (up to 3 attempts with backoff per plan §F).</summary>
    NeedsReacquire,

    /// <summary>Effect is not currently downloaded to the device (<c>DIERR_NOTDOWNLOADED</c> / <c>0x80040203</c>). Call <see cref="IDirectInputEffectAbstraction.Download"/> and retry once.</summary>
    NeedsRedownload,

    /// <summary>Unrecognized or unrecoverable error. Log and give up.</summary>
    Fatal,
}

/// <summary>
/// Maps a Vortice / SharpGen exception to a <see cref="DirectInputErrorClass"/> via a three-tier fallback
/// strategy. The classifier is the single place in the codebase that knows about HRESULT values; every
/// caller (notably <c>VorticeDirectInputDevice.ExecuteWithReacquireAsync</c>) consumes the typed enum.
/// </summary>
/// <remarks>
/// Tier 1: <see cref="SharpGenException.ResultCode"/> carries a <see cref="Result"/> whose <see cref="Result.Code"/>
/// is the original HRESULT — most reliable path; verified empirically against SharpGen.Runtime 2.2.0-beta at T-07 M3
/// implementation time (plan §B noted the chain needed verification; <c>Descriptor</c> from the spec sample was wrong
/// — the actual property is <c>ResultCode</c>). M0 probe confirmed the value reaches us cleanly: probe runtime crashed
/// on <c>SendForceFeedbackCommand(Reset)</c> against the connected AB9 with <c>HRESULT: [0x80040205], ApiCode: [DIERR_NOTEXCLUSIVEACQUIRED/NotExclusiveAcquired]</c>.
/// <para/>
/// Tier 2: <see cref="Exception.HResult"/> on any exception type — useful when a non-SharpGen exception wraps
/// the failure (e.g., custom wrapping in <c>VorticeDirectInputAdapter</c>).
/// <para/>
/// Tier 3: substring match of the ApiCode mnemonic in <see cref="Exception.Message"/> — last-resort when both
/// HRESULT paths fail (e.g., a future SharpGen rename or a deserialized exception). Case-sensitive ordinal match
/// against the mnemonics surfaced by Vortice in its message format <c>"HRESULT: [...], ApiCode: [APICODE_NAME/MnemonicForm]"</c>.
/// <para/>
/// Tier 1 always short-circuits on <see cref="SharpGenException"/>, even if <c>ResultCode.Code</c> is zero or an
/// unrecognized HRESULT — in that case the classification is <see cref="DirectInputErrorClass.Fatal"/>. We intentionally
/// do NOT fall through to Tier 2 from a <see cref="SharpGenException"/>: a SharpGenException with a missing
/// ResultCode is a malformed exception that should surface as Fatal rather than be silently routed through fallbacks.
/// </remarks>
public static class DirectInputErrorClassifier
{
    /// <summary>HRESULT constants centralized so the unit tests can reference them by name instead of magic numbers.</summary>
    internal static class HResults
    {
        public const int DierrNotExclusiveAcquired = unchecked((int)0x80040205);
        public const int DierrNotDownloaded = unchecked((int)0x80040203);
        public const int DierrEffectPlaying = unchecked((int)0x80040208);

        /// <summary>
        /// Win32 <c>ERROR_DEVICE_NOT_CONNECTED</c> surfaced through Vortice on a stop-path call against
        /// a device whose USB handle has been invalidated (cable unplug, suspend/resume). Not a
        /// <c>DIERR_*</c> code — it predates DirectInput's own error space — so it continues to be
        /// classified as <see cref="DirectInputErrorClass.Fatal"/> via the <c>ClassifyByHResult</c>
        /// default arm. The constant exists so <c>HandleStopAllAsync</c> (Issue #27 Pass 1) and its
        /// regression tests can discriminate this specific Fatal HRESULT by name and dispose of it as
        /// "no device to stop" at Information level, rather than as a generic Warning-level failure.
        /// </summary>
        public const int DeviceNotConnected = unchecked((int)0x8007048F);
    }

    /// <summary>Classifies an exception into a <see cref="DirectInputErrorClass"/> via the three-tier fallback.</summary>
    /// <param name="ex">The exception thrown by a Vortice call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ex"/> is <see langword="null"/>.</exception>
    public static DirectInputErrorClass Classify(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        // Tier 1: SharpGenException carries the HRESULT in ResultCode.Code.
        // (Verified against SharpGen.Runtime 2.2.0-beta at M3 time — the plan's spec sample said
        // `Descriptor.Result.Code` which does not exist on this version.)
        if (ex is SharpGenException sge)
        {
            return ClassifyByHResult(sge.ResultCode.Code);
        }

        // Tier 2: any exception with a non-zero HResult.
        if (ex.HResult != 0)
        {
            return ClassifyByHResult(ex.HResult);
        }

        // Tier 3: substring match against the ApiCode mnemonic in the message.
        var message = ex.Message ?? string.Empty;
        if (message.Contains("NotExclusiveAcquired", StringComparison.Ordinal))
        {
            return DirectInputErrorClass.NeedsReacquire;
        }

        if (message.Contains("NotDownloaded", StringComparison.Ordinal))
        {
            return DirectInputErrorClass.NeedsRedownload;
        }

        if (message.Contains("EffectPlaying", StringComparison.Ordinal))
        {
            return DirectInputErrorClass.Transient;
        }

        return DirectInputErrorClass.Fatal;
    }

    private static DirectInputErrorClass ClassifyByHResult(int hr) => hr switch
    {
        HResults.DierrNotExclusiveAcquired => DirectInputErrorClass.NeedsReacquire,
        HResults.DierrNotDownloaded => DirectInputErrorClass.NeedsRedownload,
        HResults.DierrEffectPlaying => DirectInputErrorClass.Transient,
        _ => DirectInputErrorClass.Fatal,
    };
}
