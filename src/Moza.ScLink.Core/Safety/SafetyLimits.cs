namespace Moza.ScLink.Core.Safety;

/// <summary>Mandatory safety floors and ceilings for force-feedback output per PRP §5.8. Not user-overridable.</summary>
public static class SafetyLimits
{
    /// <summary>Minimum allowable effect intensity (floor).</summary>
    public const double MinIntensity = 0.0;

    /// <summary>Maximum allowable effect intensity (ceiling).</summary>
    public const double MaxIntensity = 1.0;

    /// <summary>Conservative master gain applied on first run.</summary>
    public const double DefaultMasterGain = 0.6;

    /// <summary>Maximum intensity swing per second to prevent sudden force spikes.</summary>
    public const double MaxIntensityRateOfChangePerSecond = 4.0;

    /// <summary>Intensity ceiling for effects sustained longer than 5 seconds.</summary>
    public const double MaxSustainedIntensity = 0.7;

    /// <summary>Maximum number of simultaneously active effects; oldest non-sustained is preempted above this.</summary>
    public const int MaxSimultaneousEffects = 4;

    /// <summary>All effects ramp from zero over this many milliseconds on start.</summary>
    public const int StartupRampMs = 250;

    /// <summary>All effects ramp to zero over this many milliseconds on stop.</summary>
    public const int StopRampMs = 150;

    /// <summary>Emergency stop must complete within this many milliseconds.</summary>
    public const int EmergencyStopMaxLatencyMs = 50;

    /// <summary>Sustained effects are automatically stopped after this duration.</summary>
    public static readonly TimeSpan AbsoluteMaxEffectDuration = TimeSpan.FromMinutes(10);
}
