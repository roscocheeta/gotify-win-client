namespace GotifyWin.Models.Config;

public enum SoundPreset
{
    ModernFluent,
    HomelabTech,
    SubtleAcoustic,
    ClassicWindows,
    Custom
}

public enum SoundTier
{
    Low,
    Normal,
    High,
    Urgent
}

public class AudioProfileConfig
{
    private double _volume = 0.8;

    public SoundPreset Preset { get; set; } = SoundPreset.ModernFluent;

    public double Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0, 1.0);
    }

    public bool AcousticDebounceEnabled { get; set; } = true;
    public int AcousticDebounceWindowMs { get; set; } = 150;
    public bool MuteLowPriority { get; set; } = false;

    // Optional custom .wav file paths when Preset == SoundPreset.Custom
    public string? CustomLowSoundPath { get; set; }
    public string? CustomNormalSoundPath { get; set; }
    public string? CustomHighSoundPath { get; set; }
    public string? CustomUrgentSoundPath { get; set; }

    public static SoundTier MapPriorityToTier(int priority)
    {
        if (priority <= 3) return SoundTier.Low;
        if (priority <= 6) return SoundTier.Normal;
        if (priority <= 8) return SoundTier.High;
        return SoundTier.Urgent;
    }
}
