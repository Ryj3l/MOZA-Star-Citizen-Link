using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Diagnostics;

/// <summary>
/// Maps the canonical <see cref="DeviceState"/> lifecycle enum to a short user-facing string. Shared by
/// the Diagnostics tab (<see cref="ForceFeedbackDiagnostics"/>) and the App's Output-status row so both
/// surfaces render device state identically (T-27). Deliberately terse — rich device diagnostics are
/// T-18's home; this preserves an honest functional baseline through the migration.
/// </summary>
public static class DeviceStateExtensions
{
    public static string ToUserFacingString(this DeviceState state) => state switch
    {
        DeviceState.Disconnected => "No device",
        DeviceState.Detecting => "Detecting",
        DeviceState.Initializing => "Initializing",
        DeviceState.Ready => "Ready",
        DeviceState.Faulted => "Faulted",
        _ => state.ToString(),
    };
}
