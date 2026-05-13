using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Moza.ScLink.Core;
using Moza.ScLink.Core.Diagnostics;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Diagnostics.MozaSdkProbe;

public sealed class MozaSdkManagedForceFeedbackDevice : IForceFeedbackDevice
{
    private readonly string _sdkDirectory;
    private readonly Dictionary<string, object> _activeEffects = [];
    private readonly object _sync = new();
    private Assembly? _assembly;
    private Type? _apiType;
    private Type? _errorCodeType;

    private MozaSdkManagedForceFeedbackDevice(string sdkDirectory)
    {
        _sdkDirectory = sdkDirectory;
    }

    public string Name => "MOZA SDK";

    public string Status => $"Using MOZA SDK from {_sdkDirectory}.";

    public static IForceFeedbackDevice? CreateIfAvailable()
    {
        return IsRuntimeAvailable(DefaultSdkDirectory)
            ? new MozaSdkManagedForceFeedbackDevice(DefaultSdkDirectory)
            : null;
    }

    public static string DefaultSdkDirectory =>
        Path.Combine(AppContext.BaseDirectory, "drivers", "moza-sdk", "x64");

    public static bool IsRuntimeAvailable(string sdkDirectory) =>
        File.Exists(Path.Combine(sdkDirectory, "MOZA_API_CSharp.dll")) &&
        File.Exists(Path.Combine(sdkDirectory, "MOZA_API_C.dll")) &&
        File.Exists(Path.Combine(sdkDirectory, "MOZA_SDK.dll"));

    public static IReadOnlyList<string> GetSdkDeviceDiagnostics()
    {
        var lines = new List<string>();
        var sdkDirectory = DefaultSdkDirectory;
        if (!IsRuntimeAvailable(sdkDirectory))
        {
            lines.Add("MOZA SDK product query skipped: runtime DLLs are not bundled.");
            return lines;
        }

        try
        {
            SetDllDirectory(sdkDirectory);
            var assembly = Assembly.LoadFrom(Path.Combine(sdkDirectory, "MOZA_API_CSharp.dll"));
            var apiType = assembly.GetType("mozaAPI.mozaAPI", throwOnError: true)!;
            var productType = assembly.GetType("mozaAPI.PRODUCTTYPE", throwOnError: true)!;
            var errorCodeType = assembly.GetType("mozaAPI.ERRORCODE", throwOnError: true)!;

            apiType.GetMethod("installMozaSDK", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, []);

            var method = apiType.GetMethod("getDeviceParent", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(apiType.FullName, "getDeviceParent");

            foreach (var product in Enum.GetValues(productType))
            {
                var err = Enum.ToObject(errorCodeType, 0);
                object?[] parameters = [product, err];
                var name = method.Invoke(null, parameters) as string;
                var error = FormatError(errorCodeType, parameters[1]);
                lines.Add($"MOZA SDK {product}: '{name}' err {error}");
            }
        }
        catch (Exception ex)
        {
            lines.Add($"MOZA SDK product query failed: {Unwrap(ex).Message}");
        }

        return lines;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_assembly is not null)
        {
            return Task.CompletedTask;
        }

        SetDllDirectory(_sdkDirectory);
        var assemblyPath = Path.Combine(_sdkDirectory, "MOZA_API_CSharp.dll");
        _assembly = Assembly.LoadFrom(assemblyPath);
        NativeLibrary.SetDllImportResolver(_assembly, ResolveNativeDependency);

        _apiType = _assembly.GetType("mozaAPI.mozaAPI", throwOnError: true);
        _errorCodeType = _assembly.GetType("mozaAPI.ERRORCODE", throwOnError: true);
        InvokeApi("installMozaSDK");
        var wheelbaseProbe = InvokeApi("stopForceFeedback");
        ThrowIfError(wheelbaseProbe, "MOZA SDK initialized but did not report a wheelbase force-feedback device");
        AppLog.Write("MOZA SDK initialized.");
        return Task.CompletedTask;
    }

    public Task PrepareAsync(IEnumerable<ForceEffect> effects, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PlayAsync(ForceEffect effect, CancellationToken cancellationToken)
    {
        EnsureInitialized();

        var key = effect.StateKey ?? $"transient-{Guid.NewGuid():N}";
        StopEffect(key);

        var sdkEffect = effect.Kind == ForceEffectKind.Bump
            ? CreateConstantEffect(effect)
            : CreateSineEffect(effect);

        AppLog.Write($"MOZA SDK starting effect '{effect.Name}' intensity={effect.Intensity:0.###} durationMs={effect.Duration.TotalMilliseconds:0} frequencyHz={effect.FrequencyHz:0.###}.");
        InvokeEffect(sdkEffect, "start");

        lock (_sync)
        {
            _activeEffects[key] = sdkEffect;
        }

        if (effect.StateKey is null && effect.Duration > TimeSpan.Zero)
        {
            _ = StopTransientAsync(key, effect.Duration);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(string stateKey, CancellationToken cancellationToken)
    {
        StopEffect(stateKey);
        return Task.CompletedTask;
    }

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        List<string> keys;
        lock (_sync)
        {
            keys = _activeEffects.Keys.ToList();
        }

        foreach (var key in keys)
        {
            StopEffect(key);
        }

        if (_apiType is not null)
        {
            try
            {
                var result = InvokeApi("stopForceFeedback");
                ThrowIfError(result, "MOZA SDK stopForceFeedback failed");
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "MOZA SDK stopForceFeedback failed");
            }
        }

        return Task.CompletedTask;
    }

    private object CreateSineEffect(ForceEffect effect)
    {
        var sdkEffect = CreateEffect("createWheelbaseETSine", effect.Name);
        InvokeEffect(sdkEffect, "setMagnitude", ToUnsignedMagnitude(effect.Intensity));
        InvokeEffect(sdkEffect, "setDuration", ToSdkDuration(effect.Duration));
        InvokeEffect(sdkEffect, "setGain", 10_000UL);
        InvokeEffect(sdkEffect, "setPeriod", ToSdkPeriod(effect.FrequencyHz));
        InvokeEffect(sdkEffect, "setPhase", 0UL);
        InvokeEffect(sdkEffect, "setOffset", 0L);
        return sdkEffect;
    }

    private object CreateConstantEffect(ForceEffect effect)
    {
        var sdkEffect = CreateEffect("createWheelbaseETConstantForce", effect.Name);
        InvokeEffect(sdkEffect, "setMagnitude", ToSignedMagnitude(effect.Intensity));
        InvokeEffect(sdkEffect, "setDuration", ToSdkDuration(effect.Duration));
        InvokeEffect(sdkEffect, "setGain", 10_000UL);
        return sdkEffect;
    }

    private object CreateEffect(string factoryMethodName, string effectName)
    {
        EnsureInitialized();

        var err = Enum.ToObject(_errorCodeType!, 0);
        object?[] parameters = [GetMainWindowHandle(), err];
        var effect = InvokeApi(factoryMethodName, parameters);
        AppLog.Write($"MOZA SDK {factoryMethodName} for '{effectName}' returned err {FormatError(_errorCodeType!, parameters[1])}; effect null={effect is null}.");
        ThrowIfError(parameters[1], $"MOZA SDK could not create {effectName}");

        if (effect is null)
        {
            throw new InvalidOperationException($"MOZA SDK returned no effect for {effectName}.");
        }

        InvokeEffect(effect, "setEffectName", effectName);
        return effect;
    }

    private object? InvokeApi(string methodName, params object?[] parameters)
    {
        var method = _apiType!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(_apiType.FullName, methodName);
        try
        {
            return method.Invoke(null, parameters);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static object? InvokeEffect(object effect, string methodName, params object?[] parameters)
    {
        var method = effect.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException(effect.GetType().FullName, methodName);
        try
        {
            return method.Invoke(effect, parameters);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private void ThrowIfError(object? error, string message)
    {
        if (error is null || _errorCodeType is null)
        {
            return;
        }

        var value = Convert.ToInt32(error, CultureInfo.InvariantCulture);
        if (value != 0)
        {
            var name = Enum.GetName(_errorCodeType, error) ?? value.ToString(CultureInfo.InvariantCulture);
            throw new InvalidOperationException($"{message}: {name} ({value}).");
        }
    }

    private static string FormatError(Type errorCodeType, object? error)
    {
        if (error is null)
        {
            return "null";
        }

        var value = Convert.ToInt32(error, CultureInfo.InvariantCulture);
        var name = Enum.GetName(errorCodeType, error) ?? value.ToString(CultureInfo.InvariantCulture);
        return $"{name} ({value})";
    }

    private static Exception Unwrap(Exception ex) =>
        ex is TargetInvocationException { InnerException: not null } ? ex.InnerException : ex;

    private async Task StopTransientAsync(string key, TimeSpan duration)
    {
        try
        {
            await Task.Delay(duration);
            StopEffect(key);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void StopEffect(string key)
    {
        object? effect;
        lock (_sync)
        {
            if (!_activeEffects.Remove(key, out effect))
            {
                return;
            }
        }

        try
        {
            InvokeEffect(effect, "stop");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "MOZA SDK effect stop failed");
        }
    }

    private void EnsureInitialized()
    {
        if (_assembly is null || _apiType is null || _errorCodeType is null)
        {
            throw new InvalidOperationException("MOZA SDK has not been initialized.");
        }
    }

    private IntPtr ResolveNativeDependency(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var path = Path.Combine(_sdkDirectory, libraryName);
        return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
    }

    private static ulong ToUnsignedMagnitude(double intensity) =>
        (ulong)Math.Round(Math.Clamp(intensity, 0, 1) * 10_000);

    private static long ToSignedMagnitude(double intensity) =>
        (long)Math.Round(Math.Clamp(intensity, 0, 1) * 10_000);

    private static ulong ToSdkDuration(TimeSpan duration)
    {
        if (duration == TimeSpan.Zero)
        {
            return 0xffff;
        }

        return (ulong)Math.Clamp(duration.TotalMilliseconds, 1, 0xffff);
    }

    private static ulong ToSdkPeriod(double frequencyHz)
    {
        var frequency = frequencyHz <= 0 ? 20 : frequencyHz;
        return (ulong)Math.Clamp(1_000_000 / frequency, 1, uint.MaxValue);
    }

    private static IntPtr GetMainWindowHandle()
    {
        var window = System.Windows.Application.Current?.MainWindow;
        return window is null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);
}
