using System.Diagnostics.CodeAnalysis;
using Vortice.DirectInput;

namespace Moza.ScLink.DirectInput;

/// <summary>
/// Thin abstraction over the <see cref="Vortice.DirectInput.IDirectInput8"/> root object so that
/// <see cref="VorticeDirectInputDevice"/> can be unit-tested against a mocked seam instead of requiring
/// real hardware. <see cref="VorticeDirectInputAdapter"/> is the production implementation.
/// </summary>
/// <remarks>
/// Inherits <see cref="IDisposable"/> for symmetry with <see cref="IDirectInputDeviceAbstraction"/> and
/// <see cref="IDirectInputEffectAbstraction"/>. The production implementation owns the
/// <see cref="Vortice.DirectInput.IDirectInput8"/> COM object and must release it at app shutdown.
/// </remarks>
public interface IDirectInputAbstraction : IDisposable
{
    /// <summary>
    /// Enumerates currently attached DirectInput game controllers that support force-feedback.
    /// </summary>
    /// <returns>One <see cref="DirectInputDeviceInfo"/> per attached force-feedback device.</returns>
    IReadOnlyList<DirectInputDeviceInfo> EnumerateForceFeedbackDevices();

    /// <summary>
    /// Enumerates currently attached DirectInput game controllers regardless of force-feedback capability.
    /// Used by the Diagnostics project (<c>ForceFeedbackDiagnostics.cs</c>) to list every detected controller
    /// alongside whether it supports force-feedback.
    /// </summary>
    /// <returns>One <see cref="DirectInputDeviceInfo"/> per attached game controller (force-feedback or not).</returns>
    IReadOnlyList<DirectInputDeviceInfo> EnumerateAllGameControllers();

    /// <summary>
    /// Opens a per-device abstraction for the DirectInput device identified by <paramref name="instanceGuid"/>.
    /// Caller owns the returned object's lifetime and must dispose it.
    /// </summary>
    /// <param name="instanceGuid">The instance GUID reported by <see cref="DirectInputDeviceInfo.InstanceGuid"/>.</param>
    IDirectInputDeviceAbstraction CreateDevice(Guid instanceGuid);
}

/// <summary>
/// Thin abstraction over <see cref="Vortice.DirectInput.IDirectInputDevice8"/>. Wraps the device-level
/// operations <see cref="VorticeDirectInputDevice"/> uses: cooperative-level setup, data-format selection,
/// acquisition lifecycle, effect creation, gain control, and force-feedback commands.
/// </summary>
public interface IDirectInputDeviceAbstraction : IDisposable
{
    /// <summary>The device's product name as reported by DirectInput at enumeration time.</summary>
    string ProductName { get; }

    /// <summary>The device's instance GUID — stable identifier across process restarts on the same machine.</summary>
    Guid InstanceGuid { get; }

    /// <summary>
    /// Configures the cooperative level. T-07 uses <c>Exclusive | Background</c> per PRP §14.2 preservation.
    /// </summary>
    /// <param name="hwnd">Window handle for cooperative-level scoping. May be <see cref="IntPtr.Zero"/> in headless tests.</param>
    /// <param name="level">Cooperative-level flags. Use <see cref="CooperativeLevel.Exclusive"/> bitwise-or <see cref="CooperativeLevel.Background"/>.</param>
    void SetCooperativeLevel(IntPtr hwnd, CooperativeLevel level);

    /// <summary>
    /// Selects Vortice's canonical two-axis Joystick data format. Wraps
    /// <c>device.SetDataFormat&lt;RawJoystickState&gt;()</c>. Vortice 3.6.2 does not expose a
    /// <c>DataFormat.Joystick</c> static; the typed-generic API selects the layout.
    /// </summary>
    void SetJoystickDataFormat();

    /// <summary>Acquires the device for exclusive control under the cooperative level set above.</summary>
    void Acquire();

    /// <summary>Releases exclusive control. Idempotent at the abstraction level.</summary>
    void Unacquire();

    /// <summary>
    /// Creates a force-feedback effect on this device. The effect is implicitly downloaded by Vortice;
    /// <see cref="IDirectInputEffectAbstraction.Download"/> may still be called explicitly for
    /// re-download recovery on <c>DIERR_NOTDOWNLOADED</c>.
    /// </summary>
    /// <param name="effectGuid">Effect GUID. Use <see cref="EffectGuid.Sine"/> for periodic; <see cref="EffectGuid.ConstantForce"/> for constant-force.</param>
    /// <param name="parameters">Fully-populated <see cref="EffectParameters"/> including axes, directions, envelope, and the type-specific <c>Parameters</c> sub-struct.</param>
    IDirectInputEffectAbstraction CreateEffect(Guid effectGuid, EffectParameters parameters);

    /// <summary>
    /// Sets the device-level force-feedback gain. T-07 hardcodes <c>10000</c> (DI_FF_NOMINAL_MAX);
    /// T-14 will compute the per-device multiplier. Wraps <c>device.Properties.ForceFeedbackGain</c>.
    /// </summary>
    /// <param name="gain">Gain in DirectInput nominal units (0–10000).</param>
    void SetGain(int gain);

    /// <summary>
    /// Issues a device-wide force-feedback command. T-07 uses <see cref="ForceFeedbackCommand.Reset"/>
    /// and <see cref="ForceFeedbackCommand.SetActuatorsOn"/> at initialize time, and
    /// <see cref="ForceFeedbackCommand.StopAll"/> at emergency-stop time.
    /// </summary>
    void SendForceFeedbackCommand(ForceFeedbackCommand command);
}

/// <summary>
/// Thin abstraction over <see cref="Vortice.DirectInput.IDirectInputEffect"/>. Wraps effect-level
/// lifecycle: start, stop, status query, parameter updates, and the explicit download/unload
/// that the re-acquisition retry loop requires for <c>DIERR_NOTDOWNLOADED</c> recovery (PRP §14.2).
/// </summary>
/// <remarks>
/// T-07.md's spec sample (deliverable 1) omitted <see cref="Download"/> and <see cref="Unload"/>, but the
/// legacy <c>DirectInputForceFeedbackDevice</c> called both explicitly (see commit c62aaf2; M12 deleted the
/// legacy COM-interop files). They are required to preserve the behaviors in PRP §14.2 — re-downloading
/// after the device re-acquires, and unloading on dispose so the device's effect-slot count does not grow.
/// </remarks>
public interface IDirectInputEffectAbstraction : IDisposable
{
    /// <summary>Starts effect playback.</summary>
    /// <param name="iterations">Number of times to play; T-07 always uses <c>1</c>.</param>
    /// <param name="flags">Playback flags; T-07 uses <see cref="EffectPlayFlags.None"/>.</param>
    void Start(int iterations, EffectPlayFlags flags);

    /// <summary>Stops effect playback. Idempotent.</summary>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "Stop() matches Vortice.DirectInput.IDirectInputEffect.Stop() one-to-one. " +
                        "Renaming the abstraction's method would force a semantic translation in the " +
                        "production adapter for no benefit; the rule guards against VB.NET interop " +
                        "concerns that do not apply to this Windows-only WPF project.")]
    void Stop();

    /// <summary>
    /// Explicitly downloads the effect to the device. Vortice's <c>CreateEffect</c> auto-downloads on
    /// success, but after the device loses exclusive acquisition the effect's downloaded state is lost
    /// and must be re-established by calling this method before the next <see cref="Start"/>.
    /// </summary>
    void Download();

    /// <summary>Updates the effect's parameters in-place. Used by adapter code for direction/magnitude tweaks.</summary>
    /// <param name="parameters">Updated <see cref="EffectParameters"/>.</param>
    /// <param name="flags">Which parameter groups to update. Use bitwise-or combinations of <see cref="EffectParameterFlags"/>.</param>
    void SetParameters(EffectParameters parameters, EffectParameterFlags flags);

    /// <summary>Reads the effect's current status. Wraps Vortice's <c>IDirectInputEffect.Status</c> property.</summary>
    EffectStatus GetStatus();
}

/// <summary>
/// Lightweight POCO describing a DirectInput device. Produced by <see cref="IDirectInputAbstraction"/>
/// enumeration; consumed by <c>DeviceDetector</c>/<c>DeviceAllowlist</c>, the App factory, and Diagnostics.
/// Public surface (not internal) so <c>Moza.ScLink.Diagnostics.ForceFeedbackDiagnostics</c> can consume it
/// without an <c>InternalsVisibleTo</c> declaration.
/// </summary>
/// <param name="InstanceGuid">Stable device instance identifier.</param>
/// <param name="ProductName">Manufacturer's product name (e.g., <c>"MOZA AB9 Base"</c>). Matched against the allowlist.</param>
/// <param name="InstanceName">DirectInput's per-attachment instance name; often equal to <see cref="ProductName"/>.</param>
public sealed record DirectInputDeviceInfo(
    Guid InstanceGuid,
    string ProductName,
    string InstanceName);

/// <summary>
/// Well-known byte offsets within Vortice's <c>RawJoystickState</c> for the X and Y axes. Used as
/// <see cref="EffectParameters.Axes"/> entries when building two-axis Cartesian effects.
/// Equivalent to the legacy <c>DIJOFS_X</c> (0) and <c>DIJOFS_Y</c> (4) constants from the
/// hand-rolled <c>DirectInputConstants.cs</c> that T-07 replaces.
/// </summary>
/// <remarks>
/// Vortice 3.6.2 does not expose <c>ObjectId.X</c> / <c>ObjectId.Y</c> as a managed enum (the T-07.md
/// spec sample referenced them but they are not part of Vortice's public surface). The well-known
/// DirectInput offsets are stable across SDK versions, so hardcoding them is preferable to inventing
/// a wrapper enum.
/// <para>
/// When passed in <see cref="EffectParameters.Axes"/>, these offsets must be paired with the
/// <see cref="EffectFlags.ObjectOffsets"/> flag (not <see cref="EffectFlags.ObjectIds"/>) so
/// DirectInput interprets the array values as byte offsets rather than object identifiers.
/// Mismatched pairing produces <c>DIERR_INVALIDPARAM</c> at <c>CreateEffect</c> time
/// (Issue #26, surfaced in T-07 M14 hardware validation).
/// </para>
/// </remarks>
public static class JoystickAxisOffsets
{
    /// <summary>X-axis byte offset within <c>RawJoystickState</c>. Equivalent to <c>DIJOFS_X</c>.</summary>
    public const int DijofsX = 0;

    /// <summary>Y-axis byte offset within <c>RawJoystickState</c>. Equivalent to <c>DIJOFS_Y</c>.</summary>
    public const int DijofsY = 4;
}
