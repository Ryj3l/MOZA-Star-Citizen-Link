namespace Moza.ScLink.Core.Effects;

/// <summary>Commands the output worker to stop the sustained effect identified by <paramref name="StateKey"/>.</summary>
/// <param name="StateKey">The state key of the sustained effect to stop.</param>
public sealed record StopEffectCommand(string StateKey) : ForceCommand;
