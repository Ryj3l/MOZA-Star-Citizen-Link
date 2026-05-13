using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Events;

namespace Moza.ScLink.Core.Resolver;

/// <summary>Resolves canonical game events into force-feedback commands using the gain stack, profiles, and safety limiter.</summary>
public interface IEffectResolver
{
    /// <summary>
    /// Resolves a canonical game event into zero or more <see cref="ForceCommand"/>s, applying the gain
    /// stack, profile selection, envelope generation, and the safety limiter.
    /// </summary>
    IReadOnlyList<ForceCommand> Resolve(GameEvent gameEvent, ResolverContext context);
}
