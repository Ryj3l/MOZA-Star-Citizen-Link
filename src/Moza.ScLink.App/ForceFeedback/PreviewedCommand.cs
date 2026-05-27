namespace Moza.ScLink.App.ForceFeedback;

/// <summary>
/// Immutable UI/diagnostics projection of a <see cref="Core.Effects.ForceCommand"/> as the
/// <see cref="PreviewForceFeedbackDevice"/> would have driven it (T-17). Published on the device's
/// <see cref="IPreviewCommandSource.Commands"/> stream and rendered in the diagnostics preview list.
/// This is a presentation projection local to the App layer — deliberately NOT a Core cross-layer
/// contract: it flattens the command hierarchy into display fields and adds the post-command active count.
/// </summary>
/// <param name="WallClock">
/// The command's <see cref="Core.Effects.ForceCommand.IssuedAt"/> — reused rather than re-sampled so the
/// projection is clock-free and deterministic in tests (no <c>TimeProvider</c>).
/// </param>
/// <param name="CommandType">The runtime command type name (e.g. <c>PlayEffectCommand</c>).</param>
/// <param name="EffectId">Catalog effect id; <see langword="null"/> for Stop/StopAll (they carry no effect).</param>
/// <param name="FinalIntensity">Gain-resolved intensity in [0,1]; <see langword="null"/> for Stop/StopAll.</param>
/// <param name="FrequencyHz">Effect frequency in Hz; <see langword="null"/> for Stop/StopAll.</param>
/// <param name="Duration">Effect duration (<see cref="System.TimeSpan.Zero"/> = sustained); <see langword="null"/> for Stop/StopAll.</param>
/// <param name="DirectionX">Horizontal direction in [-1,1]; <see langword="null"/> for Stop/StopAll.</param>
/// <param name="DirectionY">Vertical direction in [-1,1]; <see langword="null"/> for Stop/StopAll.</param>
/// <param name="ActiveCount">Count of active sustained effects after this command was processed.</param>
public sealed record PreviewedCommand(
    DateTimeOffset WallClock,
    string CommandType,
    string? EffectId,
    double? FinalIntensity,
    double? FrequencyHz,
    TimeSpan? Duration,
    double? DirectionX,
    double? DirectionY,
    int ActiveCount);
