using Vortice.DirectInput;

namespace Moza.ScLink.DirectInput;

/// <summary>
/// Production implementation of <see cref="IDirectInputEffectAbstraction"/> wrapping a single
/// <see cref="IDirectInputEffect"/>. Constructed by <see cref="VorticeDirectInputDeviceAdapter.CreateEffect"/>;
/// owned by the effect cache in <see cref="VorticeDirectInputDevice"/> for the device's lifetime.
/// </summary>
/// <remarks>
/// <see cref="GetStatus"/> wraps Vortice's <see cref="IDirectInputEffect.Status"/> property — Vortice exposes
/// it as a property whereas the legacy COM interface had <c>GetEffectStatus(out int)</c>. All other methods are
/// direct passthroughs. Vortice exceptions bubble out unmodified for <see cref="DirectInputErrorClassifier"/>
/// to classify.
/// </remarks>
internal sealed class VorticeDirectInputEffectAdapter : IDirectInputEffectAbstraction
{
    private readonly IDirectInputEffect _effect;
    private bool _disposed;

    public VorticeDirectInputEffectAdapter(IDirectInputEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _effect = effect;
    }

    /// <inheritdoc />
    public void Start(int iterations, EffectPlayFlags flags)
    {
        ThrowIfDisposed();
        _effect.Start(iterations, flags);
    }

    /// <inheritdoc />
    public void Stop()
    {
        ThrowIfDisposed();
        _effect.Stop();
    }

    /// <inheritdoc />
    public void Download()
    {
        ThrowIfDisposed();
        _effect.Download();
    }

    /// <inheritdoc />
    public void SetParameters(EffectParameters parameters, EffectParameterFlags flags)
    {
        ThrowIfDisposed();
        _effect.SetParameters(parameters, flags);
    }

    /// <inheritdoc />
    public EffectStatus GetStatus()
    {
        ThrowIfDisposed();
        return _effect.Status;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _effect.Stop();
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            // Already stopped or device released — disposal proceeds regardless.
        }

        _effect.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
