namespace Moza.ScLink.App.ForceFeedback;

/// <summary>
/// Implemented by a force-feedback device that is running in preview mode and exposes a live stream of
/// the commands it would have driven (T-17). The view model obtains the source by casting the
/// already-injected canonical <see cref="Core.Devices.IForceFeedbackDevice"/> to this interface: a
/// <see cref="PreviewForceFeedbackDevice"/> implements it (→ non-null → preview banner shown and the
/// stream subscribed), while a real hardware device does not (→ null → banner hidden). This avoids a
/// separate nullable DI registration while keeping the preview concern off the canonical device contract.
/// </summary>
public interface IPreviewCommandSource
{
    /// <summary>
    /// Hot stream of <see cref="PreviewedCommand"/>s, one per command the device processed. Multicast and
    /// thread-safe; subscribe to populate the diagnostics preview list. Subscribers receive only commands
    /// published after they subscribe (no replay).
    /// </summary>
    IObservable<PreviewedCommand> Commands { get; }
}
