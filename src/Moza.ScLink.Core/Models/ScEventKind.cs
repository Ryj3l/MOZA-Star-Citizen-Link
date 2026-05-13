namespace Moza.ScLink.Core.Models;

/// <summary>Legacy pre-migration event kind enum. Superseded by GameEventType in T-06.</summary>
public enum ScEventKind
{
    /// <summary>Quantum spool started.</summary>
    QuantumSpoolStarted,
    /// <summary>Quantum spool ended.</summary>
    QuantumSpoolEnded,
    /// <summary>Landing impact detected.</summary>
    LandingImpact,
    /// <summary>Atmosphere entered.</summary>
    AtmosphereEntered,
    /// <summary>Atmosphere exited.</summary>
    AtmosphereExited,
}
