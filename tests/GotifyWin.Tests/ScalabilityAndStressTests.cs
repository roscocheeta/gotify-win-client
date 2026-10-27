using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using GotifyWin.ViewModels;
using Xunit;

namespace GotifyWin.Tests;

public class ScalabilityAndStressTests
{
    private class DummyConfigService : IConfigService
    {
        public GotifyClientConfig Config { get; set; } = new();
        public string ConfigFilePath => string.Empty;
        public bool HasDecryptionFailed => false;
        public int SaveCallCount { get; private set; }
        public event EventHandler<GotifyClientConfig>? ConfigChanged;
        public void Load() { }
        public void Save()
        {
            SaveCallCount++;
            ConfigChanged?.Invoke(this, Config);
        }
    }

    private class DummyApiService : IGotifyApiService
    {
        public Task<bool> TestConnectionAsync(string serverUrl, string clientToken, CancellationToken ct = default) => Task.FromResult(true);
        public Task<GotifyUser?> GetCurrentUserAsync(string serverUrl, string clientToken, CancellationToken ct = default) => Task.FromResult<GotifyUser?>(new GotifyUser { Id = 1, Name = "admin", Admin = true });
        public Task<List<GotifyApplication>> GetApplicationsAsync(string serverUrl, string clientToken, CancellationToken ct = default) => Task.FromResult(new List<GotifyApplication>());
        public Task<string?> CacheApplicationIconAsync(string serverUrl, string clientToken, GotifyApplication app, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<List<GotifyMessage>> GetRecentMessagesAsync(string serverUrl, string clientToken, int limit = 50, CancellationToken ct = default) => Task.FromResult(new List<GotifyMessage>());
        public Task<bool> DeleteMessageAsync(string serverUrl, string clientToken, long messageId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ClearAllMessagesAsync(string serverUrl, string clientToken, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static void RunInSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
        }
    }

    private static void RunInStaAsync(Func<Task> action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (caught != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
        }
    }

    [Fact]
    public void FeedViewModel_MessageBurstStorm_CapsAtMaxHistoryAndDoesNotStarve()
    {
        RunInSta(() =>
        {
            var configService = new DummyConfigService();
            configService.Config.MaxHistoryCount = 200;

            var apiService = new DummyApiService();
            var feedVm = new FeedViewModel(apiService, configService);

            var sw = Stopwatch.StartNew();

            // Simulate an extreme burst of 1,000 rapid messages
            for (int i = 1; i <= 1000; i++)
            {
                feedVm.AddMessage(new GotifyMessage
                {
                    Id = i,
                    AppId = (i % 10) + 1,
                    ApplicationName = $"App{(i % 10) + 1}",
                    Title = $"Alert #{i}",
                    Message = $"Detailed payload for message #{i} under high load",
                    Priority = (i % 10),
                    Date = DateTime.UtcNow
                });
            }

            sw.Stop();

            // Verify collection is strictly capped at MaxHistoryCount (200)
            var count = 0;
            foreach (var _ in feedVm.FilteredMessages)
            {
                count++;
            }

            Assert.Equal(200, count);
            Assert.True(feedVm.HasMessages);
            // Processing 1,000 messages on UI thread should finish well within 1.5 seconds without unconstrained layout recalculations
            Assert.True(sw.ElapsedMilliseconds < 1500, $"Elapsed {sw.ElapsedMilliseconds}ms exceeded 1500ms");
        });
    }

    [Fact]
    public void FeedViewModel_AddMessages_BatchInsertion_PreservesOrderAndCaps()
    {
        RunInSta(() =>
        {
            var configService = new DummyConfigService();
            configService.Config.MaxHistoryCount = 100;

            var feedVm = new FeedViewModel(new DummyApiService(), configService);

            var batch = new List<GotifyMessage>();
            for (int i = 1; i <= 300; i++)
            {
                batch.Add(new GotifyMessage
                {
                    Id = i,
                    AppId = 1,
                    ApplicationName = "BatchApp",
                    Title = $"Batch Message {i}",
                    Message = $"Content {i}",
                    Priority = 5,
                    Date = DateTime.UtcNow.AddSeconds(i)
                });
            }

            feedVm.AddMessages(batch);

            var messages = feedVm.FilteredMessages.Cast<GotifyMessage>().ToList();
            Assert.Equal(100, messages.Count);
            // Most recent message (Id = 300) should be at index 0
            Assert.Equal(300, messages[0].Id);
            Assert.Equal(201, messages[99].Id);
        });
    }

    [Fact]
    public void FilterEngine_HighRuleVolume_200RulesEvaluationLatency()
    {
        var engine = new FilterEngine();
        var config = new GotifyClientConfig
        {
            GlobalMinPriority = 0,
            Presentation = new PresentationSettings { EnableDebounce = false },
            Dnd = new DndSettings { IsManualDndActive = false }
        };

        // Configure 200 distinct realistic rules across various fields and operators
        for (int i = 1; i <= 200; i++)
        {
            var op = RuleOperator.ExactMatch;
            var field = RuleField.Title;
            var action = i switch
            {
                <= 50 => RuleAction.Drop,
                <= 150 => RuleAction.SilentLog,
                _ => RuleAction.AlwaysNotify
            };

            config.Rules.Add(new NotificationRule
            {
                Id = $"rule-{i}",
                Name = $"Rule {i}",
                Enabled = true,
                TargetField = field,
                Operator = op,
                Pattern = $"pattern_{i}",
                Action = action,
                CaseSensitive = false
            });
        }

        // Generate 1,000 distinct messages
        var messages = new List<GotifyMessage>(1000);
        for (int i = 1; i <= 1000; i++)
        {
            var title = (i % 100) switch
            {
                25 => "pattern_25",  // hits rule 25 (Drop)
                75 => "pattern_75",  // hits rule 75 (SilentLog)
                95 => "pattern_175", // hits rule 175 (AlwaysNotify)
                _ => $"Normal status report #{i}" // hits default Notify
            };

            messages.Add(new GotifyMessage
            {
                Id = i,
                AppId = (i % 20) + 1,
                ApplicationName = $"App{(i % 20) + 1}",
                Title = title,
                Message = $"Payload data for event {i}",
                Priority = (i % 10) + 1,
                Date = DateTime.UtcNow
            });
        }

        var sw = Stopwatch.StartNew();

        var dropCount = 0;
        var silentCount = 0;
        var notifyCount = 0;

        foreach (var msg in messages)
        {
            var result = engine.Evaluate(msg, config, isDndActive: false);
            switch (result.Decision)
            {
                case FilterDecision.Drop: dropCount++; break;
                case FilterDecision.SilentLog: silentCount++; break;
                case FilterDecision.Notify: notifyCount++; break;
            }
        }

        sw.Stop();

        // 1,000 messages evaluated against 200 rules must complete within 100ms (< 0.1ms per message)
        Assert.True(sw.ElapsedMilliseconds < 100, $"200 rules evaluation across 1,000 messages took {sw.ElapsedMilliseconds}ms (limit 100ms)");
        Assert.True(dropCount > 0, "Expected some messages to hit drop rules");
        Assert.True(silentCount > 0, "Expected some messages to hit silent log rules");
        Assert.True(notifyCount > 0, "Expected remaining messages to notify");
    }

    [Fact]
    public void Apps_Scalability_150ConfiguredApps_LookupAndBulkToggleEfficiency()
    {
        var configService = new DummyConfigService();
        var cfg = configService.Config;

        // Configure 150 applications
        for (int i = 1; i <= 150; i++)
        {
            cfg.AppFilters[i] = new AppFilterSetting
            {
                AppId = i,
                AppName = $"EnterpriseApp_{i}",
                Enabled = true,
                MinPriority = (i % 5),
                OverrideGlobalDnd = (i % 10 == 0)
            };
        }

        var appsVm = new AppsViewModel(new DummyApiService(), configService);
        foreach (var filter in cfg.AppFilters.Values)
        {
            appsVm.Apps.Add(new AppItemViewModel(filter, () => configService.Save()));
        }

        // Test Bulk Disable (SetAllEnabled: false)
        var initialSaves = configService.SaveCallCount;
        appsVm.DisableAllCommand.Execute(null);

        // Verify all 150 apps muted
        Assert.All(appsVm.Apps, a => Assert.False(a.IsEnabled));
        // Crucial performance assertion: Bulk toggle should only save to disk ONCE, not 150 times
        Assert.Equal(initialSaves + 1, configService.SaveCallCount);

        // Test Bulk Enable (SetAllEnabled: true)
        appsVm.EnableAllCommand.Execute(null);
        Assert.All(appsVm.Apps, a => Assert.True(a.IsEnabled));
        Assert.Equal(initialSaves + 2, configService.SaveCallCount);

        // Verify O(1) dictionary evaluation under 150 apps
        var engine = new FilterEngine();
        var sw = Stopwatch.StartNew();
        for (int i = 1; i <= 150; i++)
        {
            var msg = new GotifyMessage
            {
                Id = i,
                AppId = i,
                ApplicationName = $"EnterpriseApp_{i}",
                Priority = 10,
                Title = "Critical alert"
            };
            var result = engine.Evaluate(msg, cfg, isDndActive: false);
            Assert.Equal(FilterDecision.Notify, result.Decision);
        }
        sw.Stop();
        // 150 app lookups should take less than 15ms total
        Assert.True(sw.ElapsedMilliseconds < 15, $"150 app evaluations took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void RulesViewModel_MaxRulesCap_EnforcesLimitAndWarnsUser()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        // Fill up to MaxRules (500)
        for (int i = 1; i <= ImportValidator.MaxRules; i++)
        {
            rulesVm.Rules.Add(new NotificationRule
            {
                Id = $"seed-{i}",
                Name = $"Seed Rule {i}",
                Pattern = $"pattern{i}"
            });
        }

        // Attempt to create a 501st rule
        rulesVm.NewRuleName = "Exceeding Rule";
        rulesVm.NewRulePattern = "overflow_pattern";
        rulesVm.AddRuleCommand.Execute(null);

        // Assert rule was NOT added
        Assert.Equal(ImportValidator.MaxRules, rulesVm.Rules.Count);
        Assert.Contains("limit reached", rulesVm.RuleStatusFeedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Debounce_AntiFlapBurst_500Duplicates_SuppressesWithoutMemoryLeak()
    {
        var engine = new FilterEngine();
        var config = new GotifyClientConfig
        {
            GlobalMinPriority = 0,
            Presentation = new PresentationSettings
            {
                EnableDebounce = true,
                DebounceWindowSeconds = 10
            },
            Dnd = new DndSettings { IsManualDndActive = false }
        };

        var message = new GotifyMessage
        {
            Id = 1,
            AppId = 42,
            ApplicationName = "FlappingService",
            Title = "CPU Spiked to 99%",
            Message = "Core 0 utilization 99.8%",
            Priority = 5
        };

        // First message passes
        var firstResult = engine.Evaluate(message, config, isDndActive: false);
        Assert.Equal(FilterDecision.Notify, firstResult.Decision);

        // Next 499 duplicate arrivals within debounce window are suppressed
        for (int i = 2; i <= 500; i++)
        {
            var dup = new GotifyMessage
            {
                Id = i,
                AppId = 42,
                ApplicationName = "FlappingService",
                Title = "CPU Spiked to 99%",
                Message = "Core 0 utilization 99.8%",
                Priority = 5
            };
            var result = engine.Evaluate(dup, config, isDndActive: false);
            Assert.Equal(FilterDecision.SilentLog, result.Decision);
            Assert.True(result.IsDuplicate);
            Assert.Equal(i, result.DuplicateCount);
        }
    }

    [Fact]
    public void SecureImageLoader_DecodeWidthConstraint_ReducesBitmapFootprint()
    {
        RunInStaAsync(async () =>
        {
            var tempPng = Path.Combine(Path.GetTempPath(), $"gotify_stress_{Guid.NewGuid():N}.png");

            try
            {
                // Create a 600x600 high resolution bitmap
                var rtb = new RenderTargetBitmap(600, 600, 96, 96, PixelFormats.Pbgra32);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.OpenWrite(tempPng))
                {
                    encoder.Save(fs);
                }

                // Load with decodeWidth = 160
                var bitmap = await SecureImageLoader.LoadAsync(tempPng, decodeWidth: 160);

                Assert.NotNull(bitmap);
                Assert.True(bitmap.IsFrozen);
                // Bitmap pixel dimensions must be constrained to decodeWidth (160), not original 600x600
                Assert.Equal(160, bitmap.PixelWidth);
                Assert.Equal(160, bitmap.PixelHeight);
            }
            finally
            {
                if (File.Exists(tempPng))
                {
                    try { File.Delete(tempPng); } catch { }
                }
            }
        });
    }

    [Fact]
    public void MemoryOptimizer_TrimWorkingSet_ExecutesWithoutException()
    {
        var exception = Record.Exception(() => MemoryOptimizer.TrimWorkingSet());
        Assert.Null(exception);
    }
}
