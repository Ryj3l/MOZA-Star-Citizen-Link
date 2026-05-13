namespace Moza.ScLink.Core.Models;

/// <summary>Classifies force-feedback effects by broad category for gain scaling and suppression logic.</summary>
public enum EffectCategory
{
    /// <summary>Weapon fire, impacts, and explosions.</summary>
    Combat,
    /// <summary>Flight dynamics: quantum, atmosphere, thruster.</summary>
    Flight,
    /// <summary>Environmental effects: turbulence, weather.</summary>
    Environment,
    /// <summary>UI feedback effects.</summary>
    Ui,
    /// <summary>System-level effects: startup, shutdown, emergency stop.</summary>
    System,
}
