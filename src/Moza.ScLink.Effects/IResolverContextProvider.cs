using Moza.ScLink.Core.Resolver;

namespace Moza.ScLink.Effects;

/// <summary>
/// Supplies the <see cref="ResolverContext"/> the <see cref="EffectResolver"/> needs for each game event
/// (active ship profile, user gains, device capabilities, current time).
/// <para>
/// This is an Effects-local seam, not a Core cross-layer contract: the Phase-1 default
/// (<see cref="DefaultResolverContextProvider"/>) returns placeholder context, and the #43/T-16 convergence
/// swaps in a provider that sources the live ship profile, settings-driven gains, and the active device's
/// capabilities. Keeping it here (rather than in Core) avoids inflating the locked domain surface.
/// </para>
/// </summary>
public interface IResolverContextProvider
{
    /// <summary>Returns the current resolver context.</summary>
    ResolverContext GetContext();
}
