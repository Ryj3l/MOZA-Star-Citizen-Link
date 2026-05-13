namespace Moza.ScLink.Core.Effects;

/// <summary>Commands the output worker to immediately stop all active effects (emergency stop).</summary>
public sealed record StopAllCommand() : ForceCommand;
