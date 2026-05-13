namespace Moza.ScLink.Core.Models;

/// <summary>The DirectInput effect type used to render a <see cref="Moza.ScLink.Core.Effects.ForceEffect"/>.</summary>
public enum ForceEffectType
{
    /// <summary>Periodic sinusoidal or sawtooth force.</summary>
    Periodic,
    /// <summary>Constant unidirectional force.</summary>
    ConstantForce,
    /// <summary>Periodic force with an ADSR envelope applied.</summary>
    PeriodicWithEnvelope,
    /// <summary>Multiple simultaneous effects played as a unit.</summary>
    Composite,
}
