using System.Collections.Concurrent;
using System.IO;
using System.Media;
using System.Text;
using System.Windows;
using GotifyWin.Models;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public class SoundService : ISoundService
{
    private readonly ConcurrentDictionary<string, byte[]> _audioCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly AudioRateLimiter _rateLimiter = new();

    public void PlayNotification(string? customSoundPath = null, bool isMuted = false)
    {
        if (isMuted) return;

        try
        {
            if (!string.IsNullOrWhiteSpace(customSoundPath) && SoundValidator.IsValidWavFile(customSoundPath, out _))
            {
                using var player = new SoundPlayer(customSoundPath);
                player.Play();
                return;
            }

            PlayPreset(SoundPreset.ModernFluent, SoundTier.Normal, 0.8);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Audio playback I/O note: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Audio playback auth note: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Audio playback invalid op note: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Audio playback timeout note: {ex.Message}");
        }
    }

    public void PlayAlert(string? customSoundPath = null, bool isMuted = false) => PlayNotification(customSoundPath, isMuted);

    public void PlayNotification(GotifyMessage message, AudioProfileConfig audioConfig, AppFilterSetting? appFilter = null, bool isMuted = false)
    {
        if (isMuted || message.Priority <= 0) return;

        var tier = AudioProfileConfig.MapPriorityToTier(message.Priority);

        if (audioConfig.MuteLowPriority && tier == SoundTier.Low) return;

        if (audioConfig.AcousticDebounceEnabled && !_rateLimiter.ShouldPlay(tier, audioConfig.AcousticDebounceWindowMs))
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Acoustic debounce throttled notification (Priority={message.Priority}, Tier={tier})");
            return;
        }

        var volume = audioConfig.Volume;

        // 1. App-specific custom sound override
        if (!string.IsNullOrWhiteSpace(appFilter?.CustomSoundPath) &&
            SoundValidator.IsValidWavFile(appFilter.CustomSoundPath, out _))
        {
            PlayCustomFile(appFilter.CustomSoundPath, volume);
            return;
        }

        // 2. App-specific preset scheme override
        if (appFilter?.SoundPresetOverride.HasValue == true)
        {
            PlayPreset(appFilter.SoundPresetOverride.Value, tier, volume);
            return;
        }

        // 3. Global Custom scheme tier-specific paths
        if (audioConfig.Preset == SoundPreset.Custom)
        {
            var customPath = tier switch
            {
                SoundTier.Low => audioConfig.CustomLowSoundPath,
                SoundTier.Normal => audioConfig.CustomNormalSoundPath,
                SoundTier.High => audioConfig.CustomHighSoundPath,
                SoundTier.Urgent => audioConfig.CustomUrgentSoundPath,
                _ => audioConfig.CustomNormalSoundPath
            };

            if (!string.IsNullOrWhiteSpace(customPath) && SoundValidator.IsValidWavFile(customPath, out _))
            {
                PlayCustomFile(customPath, volume);
                return;
            }
        }

        // 4. Global Built-in Scheme
        PlayPreset(audioConfig.Preset, tier, volume);
    }

    public void PlayPreview(SoundPreset preset, SoundTier tier, double volume)
    {
        PlayPreset(preset, tier, volume);
    }

    public void PlayCustomFilePreview(string filePath, double volume)
    {
        if (SoundValidator.IsValidWavFile(filePath, out _))
        {
            PlayCustomFile(filePath, volume);
        }
        else
        {
            SystemSounds.Hand.Play();
        }
    }

    public bool ValidateSoundFile(string filePath, out string? errorMessage)
    {
        return SoundValidator.IsValidWavFile(filePath, out errorMessage);
    }

    private void PlayPreset(SoundPreset preset, SoundTier tier, double volume)
    {
        if (volume <= 0.01) return;

        try
        {
            var fileName = GetPresetFileName(preset, tier);
            var audioBytes = GetOrCreateCachedBytes(fileName);
            if (audioBytes == null || audioBytes.Length == 0)
            {
                SystemSounds.Asterisk.Play();
                return;
            }

            PlayWavBytes(audioBytes, volume);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Preset playback note: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Preset playback auth note: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Preset playback invalid op note: {ex.Message}");
        }
    }

    private void PlayCustomFile(string filePath, double volume)
    {
        if (volume <= 0.01) return;

        try
        {
            var audioBytes = File.ReadAllBytes(filePath);
            PlayWavBytes(audioBytes, volume);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Custom playback note: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Custom playback auth note: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Custom playback invalid op note: {ex.Message}");
        }
    }

    private void PlayWavBytes(byte[] rawBytes, double volume)
    {
        var clampedVolume = Math.Clamp(volume, 0.0, 1.0);
        if (clampedVolume <= 0.01) return;

        byte[] finalBytes = clampedVolume >= 0.99 ? rawBytes : AttenuatePcmVolume(rawBytes, clampedVolume);

        using var ms = new MemoryStream(finalBytes);
        using var player = new SoundPlayer(ms);
        player.Play();
    }

    public static byte[] AttenuatePcmVolume(byte[] wavBytes, double volume)
    {
        if (volume >= 0.99 || wavBytes.Length < 44) return wavBytes;

        // Locate data chunk
        int dataIndex = -1;
        for (int i = 12; i < wavBytes.Length - 8; i++)
        {
            if (wavBytes[i] == 'd' && wavBytes[i + 1] == 'a' && wavBytes[i + 2] == 't' && wavBytes[i + 3] == 'a')
            {
                dataIndex = i;
                break;
            }
        }

        if (dataIndex < 0) return wavBytes;

        int chunkSize = BitConverter.ToInt32(wavBytes, dataIndex + 4);
        int sampleStart = dataIndex + 8;
        int sampleEnd = Math.Min(wavBytes.Length, sampleStart + chunkSize);

        var copy = new byte[wavBytes.Length];
        Buffer.BlockCopy(wavBytes, 0, copy, 0, wavBytes.Length);

        // Attenuate 16-bit PCM samples
        for (int i = sampleStart; i + 1 < sampleEnd; i += 2)
        {
            short sample = (short)(copy[i] | (copy[i + 1] << 8));
            short scaled = (short)Math.Clamp(Math.Round(sample * volume), short.MinValue, short.MaxValue);
            copy[i] = (byte)(scaled & 0xFF);
            copy[i + 1] = (byte)((scaled >> 8) & 0xFF);
        }

        return copy;
    }

    private byte[]? GetOrCreateCachedBytes(string fileName)
    {
        return _audioCache.GetOrAdd(fileName, name =>
        {
            using var stream = LoadAssetAudioStream(name);
            if (stream == null) return Array.Empty<byte>();

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        });
    }

    private static Stream? LoadAssetAudioStream(string fileName)
    {
        // 1. Try Application.GetResourceStream (pack URI)
        try
        {
            if (System.Windows.Application.Current != null)
            {
                var packUri = new Uri($"pack://application:,,,/GotifyWin;component/Assets/Audio/{fileName}", UriKind.Absolute);
                var streamInfo = System.Windows.Application.GetResourceStream(packUri);
                if (streamInfo?.Stream != null) return streamInfo.Stream;
            }
        }
        catch (InvalidOperationException) { }
        catch (IOException) { }

        // 2. Direct manifest resources (.g.resources) fallback
        try
        {
            var asm = typeof(SoundService).Assembly;
            var resName = asm.GetName().Name + ".g.resources";
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream != null)
            {
                using var reader = new System.Resources.ResourceReader(stream);
                var targetKey = "assets/audio/" + fileName.ToLowerInvariant();
                reader.GetResourceData(targetKey, out _, out var data);
                if (data != null && data.Length > 4)
                {
                    var length = BitConverter.ToInt32(data, 0);
                    var audioBytes = new byte[length];
                    Buffer.BlockCopy(data, 4, audioBytes, 0, length);
                    return new MemoryStream(audioBytes);
                }
            }
        }
        catch (InvalidOperationException) { }
        catch (IOException) { }
        catch (ArgumentException) { }

        // 3. Fallback to direct file on disk (development / test runner)
        var diskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Audio", fileName);
        if (File.Exists(diskPath))
        {
            return File.OpenRead(diskPath);
        }

        return null;
    }

    public static string GetPresetFileName(SoundPreset preset, SoundTier tier)
    {
        var tierSuffix = tier switch
        {
            SoundTier.Low => "low",
            SoundTier.Normal => "normal",
            SoundTier.High => "high",
            SoundTier.Urgent => "urgent",
            _ => "normal"
        };

        return preset switch
        {
            SoundPreset.ModernFluent => $"modern_fluent_{tierSuffix}.wav",
            SoundPreset.HomelabTech => $"homelab_tech_{tierSuffix}.wav",
            SoundPreset.SubtleAcoustic => $"subtle_acoustic_{tierSuffix}.wav",
            SoundPreset.ClassicWindows => $"classic_{tierSuffix}.wav",
            _ => $"modern_fluent_{tierSuffix}.wav"
        };
    }
}
