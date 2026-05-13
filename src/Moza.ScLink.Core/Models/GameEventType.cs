namespace Moza.ScLink.Core.Models;

/// <summary>Canonical game event types emitted by the fusion engine.</summary>
public enum GameEventType
{
    // Quantum
    /// <summary>Quantum drive spool sequence has started.</summary>
    QuantumSpoolStarted,
    /// <summary>Quantum drive spool sequence has ended (cancelled or completed).</summary>
    QuantumSpoolEnded,
    /// <summary>Quantum jump has initiated.</summary>
    QuantumJumpStarted,
    /// <summary>Ship has exited quantum travel.</summary>
    QuantumJumpExit,

    // Atmosphere
    /// <summary>Ship has entered an atmosphere.</summary>
    AtmosphereEntered,
    /// <summary>Ship has exited an atmosphere.</summary>
    AtmosphereExited,
    /// <summary>Ship is experiencing atmospheric turbulence.</summary>
    AtmosphericBuffet,

    // Landing / impact
    /// <summary>Landing gear has made ground contact.</summary>
    LandingGearContact,
    /// <summary>Ship hull has sustained an impact.</summary>
    HullImpact,
    /// <summary>Vehicle has been destroyed.</summary>
    VehicleDestruction,

    // Combat
    /// <summary>Ballistic weapon has fired.</summary>
    WeaponFireBallistic,
    /// <summary>Energy weapon has fired.</summary>
    WeaponFireEnergy,
    /// <summary>Missile has been launched.</summary>
    MissileLaunch,
    /// <summary>Shield has absorbed a hit.</summary>
    ShieldHit,
    /// <summary>Hull has taken damage.</summary>
    HullDamage,

    // Misc
    /// <summary>Thruster has activated or changed state.</summary>
    ThrusterActivation,

    // System events (not haptic)
    /// <summary>A play session has started.</summary>
    SessionStarted,
    /// <summary>A play session has ended.</summary>
    SessionEnded,
    /// <summary>Emergency stop has been triggered — all effects suppressed.</summary>
    EmergencyStop,
}
