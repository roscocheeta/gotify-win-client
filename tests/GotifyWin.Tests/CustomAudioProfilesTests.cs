using System.IO;
using System.Text;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using GotifyWin.ViewModels;
using Xunit;

namespace GotifyWin.Tests;

public class CustomAudioProfilesTests : IDisposable
{
    private readonly string _testTempDir;

    public CustomAudioProfilesTests()
    {
        _testTempDir = Path.Join(Path.GetTempPath(), "GotifyWin_AudioTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testTempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Test Cleanup] {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Test Cleanup] {ex.Message}");
        }
    }

    private string CreateValidWav(string fileName, short formatTag = 1, short channels = 1, int sampleRate = 44100, int sampleCount = 50)
    {
        var filePath = Path.Join(_testTempDir, fileName);
        var subchunk2Size = sampleCount * 2;
        var chunkSize = 36 + subchunk2Size;

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // RIFF header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(chunkSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // subchunk1 size
        writer.Write(formatTag); // 1 = PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2); // byte rate
        writer.Write((short)(channels * 2)); // block align
        writer.Write((short)16); // bits per sample

        // data chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(subchunk2Size);

        for (int i = 0; i < sampleCount; i++)
        {
            writer.Write((short)10000);
        }

        return filePath;
    }

    [Fact]
    public void SoundValidator_ValidRiffWav_ReturnsTrue()
    {
        var wavPath = CreateValidWav("valid.wav");
        var isValid = SoundValidator.IsValidWavFile(wavPath, out var err);

        Assert.True(isValid);
        Assert.Null(err);
    }

    [Fact]
    public void SoundValidator_NonWavExtension_ReturnsFalse()
    {
        var txtPath = Path.Join(_testTempDir, "sound.mp3");
        File.WriteAllText(txtPath, "fake audio");

        var isValid = SoundValidator.IsValidWavFile(txtPath, out var err);

        Assert.False(isValid);
        Assert.Contains(".wav", err);
    }

    [Theory]
    [InlineData(@"\\192.168.1.1\share\alert.wav")]
    [InlineData(@"\\attackerserver\share\chime.wav")]
    [InlineData("//10.0.0.1/share/alert.wav")]
    public void SoundValidator_UncPath_IsRejectedForSecurity(string uncPath)
    {
        var isValid = SoundValidator.IsValidWavFile(uncPath, out var err);

        Assert.False(isValid);
        Assert.Contains("UNC network paths are rejected", err);
    }

    [Fact]
    public void SoundValidator_NonExistentFile_ReturnsFalse()
    {
        var missingPath = Path.Join(_testTempDir, "does_not_exist.wav");
        var isValid = SoundValidator.IsValidWavFile(missingPath, out var err);

        Assert.False(isValid);
        Assert.Contains("does not exist", err);
    }

    [Fact]
    public void SoundValidator_CorruptedRiffHeader_ReturnsFalse()
    {
        var badPath = Path.Join(_testTempDir, "corrupted.wav");
        File.WriteAllBytes(badPath, Encoding.ASCII.GetBytes("NOPE" + new string('0', 50)));

        var isValid = SoundValidator.IsValidWavFile(badPath, out var err);

        Assert.False(isValid);
        Assert.Contains("RIFF", err);
    }

    [Fact]
    public void SoundValidator_OversizedFile_ReturnsFalse()
    {
        var largePath = Path.Join(_testTempDir, "huge.wav");
        using (var fs = new FileStream(largePath, FileMode.Create, FileAccess.Write))
        {
            fs.SetLength(SoundValidator.MaxAudioSizeBytes + 1024);
        }

        var isValid = SoundValidator.IsValidWavFile(largePath, out var err);

        Assert.False(isValid);
        Assert.Contains("exceeds the 5 MB limit", err);
    }

    [Fact]
    public void AudioRateLimiter_WithinWindow_ThrottlesSameOrLowerPriority()
    {
        var limiter = new AudioRateLimiter();

        Assert.True(limiter.ShouldPlay(SoundTier.Normal, 150));
        // Immediate repeat with same tier should be throttled
        Assert.False(limiter.ShouldPlay(SoundTier.Normal, 150));
        // Immediate call with lower tier should be throttled
        Assert.False(limiter.ShouldPlay(SoundTier.Low, 150));
    }

    [Fact]
    public void AudioRateLimiter_WithinWindow_AllowsHigherPriority()
    {
        var limiter = new AudioRateLimiter();

        Assert.True(limiter.ShouldPlay(SoundTier.Low, 150));
        // Higher urgency supersedes lower within the window
        Assert.True(limiter.ShouldPlay(SoundTier.Urgent, 150));
    }

    [Fact]
    public void AudioRateLimiter_Reset_ClearsCooldown()
    {
        var limiter = new AudioRateLimiter();

        Assert.True(limiter.ShouldPlay(SoundTier.High, 150));
        Assert.False(limiter.ShouldPlay(SoundTier.High, 150));

        limiter.Reset();
        Assert.True(limiter.ShouldPlay(SoundTier.High, 150));
    }

    [Theory]
    [InlineData(0, SoundTier.Low)]
    [InlineData(1, SoundTier.Low)]
    [InlineData(3, SoundTier.Low)]
    [InlineData(4, SoundTier.Normal)]
    [InlineData(6, SoundTier.Normal)]
    [InlineData(7, SoundTier.High)]
    [InlineData(8, SoundTier.High)]
    [InlineData(9, SoundTier.Urgent)]
    [InlineData(10, SoundTier.Urgent)]
    public void AudioProfileConfig_MapPriorityToTier_MapsCorrectly(int priority, SoundTier expectedTier)
    {
        Assert.Equal(expectedTier, AudioProfileConfig.MapPriorityToTier(priority));
    }

    [Fact]
    public void AudioProfileConfig_VolumeClamping_ClampsBetweenZeroAndOne()
    {
        var config = new AudioProfileConfig();

        config.Volume = 1.5;
        Assert.Equal(1.0, config.Volume);

        config.Volume = -0.2;
        Assert.Equal(0.0, config.Volume);

        config.Volume = 0.65;
        Assert.Equal(0.65, config.Volume);
    }

    [Fact]
    public void SoundService_VolumeAttenuation_Attenuates16BitPcmBytes()
    {
        var wavPath = CreateValidWav("attenuate.wav", sampleCount: 10);
        var originalBytes = File.ReadAllBytes(wavPath);

        // At 1.0 volume, returns exact bytes
        var fullBytes = SoundService.AttenuatePcmVolume(originalBytes, 1.0);
        Assert.Equal(originalBytes, fullBytes);

        // At 0.5 volume, samples should be scaled approximately by half
        var halfBytes = SoundService.AttenuatePcmVolume(originalBytes, 0.5);
        Assert.Equal(originalBytes.Length, halfBytes.Length);

        // Locate data chunk and verify sample attenuation
        short originalSample = BitConverter.ToInt16(originalBytes, 44);
        short halfSample = BitConverter.ToInt16(halfBytes, 44);

        Assert.Equal(10000, originalSample);
        Assert.InRange(halfSample, 4990, 5010);
    }

    [Theory]
    [InlineData(SoundPreset.ModernFluent, SoundTier.Low, "modern_fluent_low.wav")]
    [InlineData(SoundPreset.ModernFluent, SoundTier.Normal, "modern_fluent_normal.wav")]
    [InlineData(SoundPreset.HomelabTech, SoundTier.Urgent, "homelab_tech_urgent.wav")]
    [InlineData(SoundPreset.SubtleAcoustic, SoundTier.High, "subtle_acoustic_high.wav")]
    [InlineData(SoundPreset.ClassicWindows, SoundTier.Normal, "classic_normal.wav")]
    public void SoundService_GetPresetFileName_ReturnsExpectedFileNames(SoundPreset preset, SoundTier tier, string expectedFileName)
    {
        Assert.Equal(expectedFileName, SoundService.GetPresetFileName(preset, tier));
    }

    private class DummyHudManager : IHudManager
    {
        public void ShowNotification(GotifyMessage message, PresentationSettings settings, Action<string>? onUrlOpen) { }
        public void DismissAll() { }
        public void Dispose() { }
    }

    private class RecordingSoundService : ISoundService
    {
        public List<(GotifyMessage Message, AudioProfileConfig Config, AppFilterSetting? AppFilter)> PlayedNotifications { get; } = new();
        public List<(SoundPreset Preset, SoundTier Tier, double Volume)> PlayedPreviews { get; } = new();

        public void PlayNotification(string? customSoundPath = null, bool isMuted = false) { }
        public void PlayAlert(string? customSoundPath = null, bool isMuted = false) { }

        public void PlayNotification(GotifyMessage message, AudioProfileConfig audioConfig, AppFilterSetting? appFilter = null, bool isMuted = false)
        {
            if (!isMuted)
            {
                PlayedNotifications.Add((message, audioConfig, appFilter));
            }
        }

        public void PlayPreview(SoundPreset preset, SoundTier tier, double volume)
        {
            PlayedPreviews.Add((preset, tier, volume));
        }

        public void PlayCustomFilePreview(string filePath, double volume) { }
        public bool ValidateSoundFile(string filePath, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }
    }

    private class DummyConfigService : IConfigService
    {
        public GotifyClientConfig Config { get; set; } = new();
        public string ConfigFilePath => string.Empty;
        public bool HasDecryptionFailed => false;
#pragma warning disable CS0067
        public event EventHandler<GotifyClientConfig>? ConfigChanged;
#pragma warning restore CS0067
        public void Load() { }
        public void Save() { ConfigChanged?.Invoke(this, Config); }
    }

    [Fact]
    public void NotificationPresenter_WithSoundEnabled_CallsSoundService()
    {
        var soundService = new RecordingSoundService();
        var presenter = new NotificationPresenter(soundService, new DummyHudManager());

        var config = new GotifyClientConfig();
        config.Presentation.PlaySound = true;
        config.Presentation.Audio.Preset = SoundPreset.HomelabTech;

        var message = new GotifyMessage { Id = 1, Title = "Test", Message = "Body", Priority = 5 };

        presenter.Present(message, config);

        Assert.Single(soundService.PlayedNotifications);
        Assert.Equal(5, soundService.PlayedNotifications[0].Message.Priority);
        Assert.Equal(SoundPreset.HomelabTech, soundService.PlayedNotifications[0].Config.Preset);
    }

    [Fact]
    public void NotificationPresenter_WithSoundDisabled_DoesNotCallSoundService()
    {
        var soundService = new RecordingSoundService();
        var presenter = new NotificationPresenter(soundService, new DummyHudManager());

        var config = new GotifyClientConfig();
        config.Presentation.PlaySound = false;

        var message = new GotifyMessage { Id = 1, Title = "Test", Message = "Body", Priority = 5 };

        presenter.Present(message, config);

        Assert.Empty(soundService.PlayedNotifications);
    }

    [Fact]
    public void NotificationPresenter_WithAppFilter_PassesFilterToSoundService()
    {
        var soundService = new RecordingSoundService();
        var presenter = new NotificationPresenter(soundService, new DummyHudManager());

        var config = new GotifyClientConfig();
        config.Presentation.PlaySound = true;
        var appFilter = new AppFilterSetting { AppId = 42, CustomSoundPath = "C:\\audio\\custom.wav" };
        config.AppFilters[42] = appFilter;

        var message = new GotifyMessage { Id = 1, AppId = 42, Title = "App Alert", Message = "Body", Priority = 8 };

        presenter.Present(message, config);

        Assert.Single(soundService.PlayedNotifications);
        Assert.NotNull(soundService.PlayedNotifications[0].AppFilter);
        Assert.Equal(42, soundService.PlayedNotifications[0].AppFilter!.AppId);
    }

    [Fact]
    public void SettingsViewModel_AudioProperties_SaveToConfigAndNotify()
    {
        var configService = new DummyConfigService();
        var soundService = new RecordingSoundService();
        var vm = new SettingsViewModel(configService, null!, null!, null!, soundService: soundService);

        vm.SelectedSoundPreset = SoundPreset.SubtleAcoustic;
        Assert.Equal(SoundPreset.SubtleAcoustic, configService.Config.Presentation.Audio.Preset);

        vm.SoundVolume = 60;
        Assert.Equal(0.6, configService.Config.Presentation.Audio.Volume, 2);
        Assert.Equal("60%", vm.SoundVolumePercentageText);

        vm.MuteLowPriority = true;
        Assert.True(configService.Config.Presentation.Audio.MuteLowPriority);

        vm.AcousticDebounceEnabled = false;
        Assert.False(configService.Config.Presentation.Audio.AcousticDebounceEnabled);
    }

    [Fact]
    public void SettingsViewModel_TestSoundTier_PlaysPreview()
    {
        var configService = new DummyConfigService();
        var soundService = new RecordingSoundService();
        var vm = new SettingsViewModel(configService, null!, null!, null!, soundService: soundService);

        vm.SelectedSoundPreset = SoundPreset.ClassicWindows;
        vm.SoundVolume = 80;

        vm.TestSoundTierCommand.Execute("Urgent");

        Assert.Single(soundService.PlayedPreviews);
        Assert.Equal(SoundPreset.ClassicWindows, soundService.PlayedPreviews[0].Preset);
        Assert.Equal(SoundTier.Urgent, soundService.PlayedPreviews[0].Tier);
        Assert.Equal(0.8, soundService.PlayedPreviews[0].Volume, 2);
    }

    [Fact]
    public void AppItemViewModel_CustomSoundProperties_UpdateAndNotify()
    {
        var filter = new AppFilterSetting { AppId = 1, AppName = "HomeAssistant" };
        bool changedNotified = false;
        var vm = new AppItemViewModel(filter, () => changedNotified = true);

        Assert.False(vm.HasCustomSound);
        Assert.Equal("Default / Global", vm.CustomSoundFileName);

        vm.CustomSoundPath = @"C:\Sounds\ha_alert.wav";

        Assert.True(changedNotified);
        Assert.True(vm.HasCustomSound);
        Assert.Equal("ha_alert.wav", vm.CustomSoundFileName);

        vm.SoundPresetOverride = SoundPreset.HomelabTech;
        Assert.Equal("Homelab & Tech", vm.SoundPresetOverrideText);
    }
}
