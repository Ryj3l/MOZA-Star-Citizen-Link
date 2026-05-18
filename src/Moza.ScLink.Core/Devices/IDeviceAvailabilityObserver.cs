namespace Moza.ScLink.Core.Devices;

/// <summary>
/// Observes OS-level device-availability transitions. Implementations react to USB hot-plug and
/// hot-remove events by re-evaluating force-feedback device selection. Pass-2 adds this surface as
/// the boundary between the WPF window's WM_DEVICECHANGE hook and the chain-composition layer
/// that owns re-selection logic.
/// </summary>
/// <remarks>
/// The signature is deliberately payload-free: per T-07 Issue #27 Pass-2 §F2a, the WM_DEVICECHANGE
/// payload shape for the AB9 is not yet hardware-verified. The probe-output-driven filter (Section
/// E of the Pass-2 plan) determines which events trigger which calls; the observer surface itself
/// stays payload-free so Pass 2 does not bake an unverified payload format into a Core interface.
/// If post-probe data shows device-identity discrimination is needed, a payload-bearing successor
/// goes through an ADR.
/// </remarks>
public interface IDeviceAvailabilityObserver
{
    /// <summary>A device-arrival event (DBT_DEVICEARRIVAL) was observed.</summary>
    void OnDeviceArrived();

    /// <summary>A device-removal event (DBT_DEVICEREMOVECOMPLETE) was observed.</summary>
    void OnDeviceRemoved();
}
