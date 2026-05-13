namespace Moza.ScLink.Core.Models;

/// <summary>Identifies the kind of sensor that produced a <see cref="Moza.ScLink.Core.Sensors.SensorEvent"/>.</summary>
public enum SensorKind
{
    /// <summary>Game.log tail sensor.</summary>
    Log,
    /// <summary>Audio loopback capture sensor.</summary>
    Audio,
    /// <summary>Screen capture / ROI analysis sensor.</summary>
    Screen,
    /// <summary>DirectInput input-mirroring sensor.</summary>
    Input,
}
