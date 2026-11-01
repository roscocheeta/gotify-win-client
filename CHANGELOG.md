# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **Priority-Aware Custom Audio Profiles & Sound Schemes**:
  - Implemented 4 curated, high-fidelity sound schemes: **Modern Fluent** (crystalline harmonic chords), **Homelab & Tech** (crisp digital synth pulses), **Subtle Acoustic** (gentle wooden taps and marimba plucks), and **Classic Windows** (standard bell tones).
  - Dynamic Priority Tier Audio Mapping: Automatically plays distinct sound variants depending on Gotify priority (Low 1-3, Normal 4-6, High 7-8, Urgent 9-10).
  - In-Memory PCM Caching: Loads embedded audio assets directly into memory streams with zero disk I/O, playing in under 1 millisecond.
  - In-Memory Volume Attenuation: Fast 16-bit PCM volume scaling via SoundService.AttenuatePcmVolume.
  - Acoustic Rate Limiter (AudioRateLimiter): 150ms cooldown prevents overlapping audio popping and distortion during notification bursts while allowing high-priority alerts to supersede immediately.
  - Security-Hardened Audio Validation (SoundValidator): Validates RIFF/WAVE header, 16-bit PCM format, channels, and sample rate, limits files to 5MB, and strictly rejects UNC network share paths (\\server\share) to prevent SMB NTLM hash leaks.
  - Interactive UI in Settings: Added sound scheme selector, volume slider (0-100%), 1-click sound tier preview buttons, and custom sound file picker with validation feedback.
  - Per-Application Sound Overrides: Added custom sound override and preview directly on application cards in the Apps tab.
  - Added 33 new automated tests in CustomAudioProfilesTests.cs, bringing total suite to **257 passing tests** (100% pass rate).
- **Workstation Unlock Missed Notifications Summary Alert (#37)**:
  - Added session state tracking via `SystemEvents.SessionSwitch` (`SessionLock` and `SessionUnlock`).
  - Implemented `ISessionNotificationTracker` and `SessionNotificationTracker` to aggregate notifications arriving while the PC is locked for at least 10 seconds.
  - Automatically displays a consolidated summary alert upon workstation unlock (e.g., *"You missed 3 notifications from HomeAssistant, BackupServer, Plex while away"*).
  - Added `EnableMissedSummaryOnUnlock` user toggle under Settings.

### Fixed
- **Message History Feed Chronological Sorting & Live Relative Times (#36)**:
  - Enforced strict descending order (`Date` descending, `Id` descending) on `FeedViewModel.FilteredMessages` `SortDescriptions` and internal collection insertion.
  - Out-of-order network messages, delayed queue replays, or clock skew no longer cause older notifications to appear at the head of the feed.
  - Normalized `GotifyMessage.Date` deserialization with unspecified `DateTimeKind` to UTC.
  - Handled future clock skew gracefully in `RelativeTime` (returns *"Just now"*).
  - Added a 30-second background dispatcher timer to dynamically update relative timestamps (*"Just now"*, *"5m ago"*, *"2h ago"*) across the entire feed.
  - Implemented `INotifyPropertyChanged` on `GotifyMessage`.
- **Auto-Updater UAC Elevation & Tray Crash Prevention (#35)**:
  - Added `HasWriteAccess` permission inspection to detect write-restricted installation directories (e.g. `C:\Program Files`).
  - Added UAC elevation support (`ApplyElevatedUpdate` using `Verb = "runas"`) allowing standard non-admin users to apply updates seamlessly with native Windows UAC prompting.
  - Deferred system tray context menu quit handling via `UiDispatcher.RunOnUI` to eliminate unhandled `ObjectDisposedException` crashes when right-clicking and closing lingering or active tray icons.
  - Explicitly bound `Application.Current.MainWindow = _mainWindow` on startup so instances started minimized or hidden to tray cleanly teardown without leaving orphaned tray icons.
- **Extended Test Suite**:
  - Added 7 new automated tests in `FeedSortingAndMissedSummaryTests.cs` verifying chronological feed sorting, delayed insertion, deduplication, clock skew resilience, workstation lock tracking, and write permissions, bringing the total suite to **224 passing tests** (100% pass rate).

---

## [1.3.1] - 2026-10-30

### Fixed
- **Settings Auto-Scroll to About & Updates (#30)**:
  - Clicking the update pill in the main window header bar switches to the Settings tab and automatically scrolls down to the 'About & Updates' card via `BringIntoView()`.
- **Auto-Updater Clean Termination with Minimize to Tray Enabled (#31)**:
  - Fixed auto-updater restart logic where `MinimizeToTrayOnClose` intercepted the shutdown, leaving the previous application running in the tray alongside the new instance. Added `MainWindow.ExplicitQuit()` and configurable shutdown handler ensuring clean exit.
- **WPF Pack URI Assembly Resolution**:
  - Standardized pack URIs in `MainWindow.xaml` and `MainWindow.xaml.cs` to include explicit component assembly names (`/GotifyWin;component/`), preventing resource lookup failures during test runner execution.
- **Extended Test Suite**:
  - Added 5 new automated tests verifying update navigation, scroll events, explicit shutdown flags, and tray icon disposal, bringing total suite to **217 passing tests** (100% pass rate).

---

## [1.3.0] - 2026-10-29

### Added
- **Alt-Tab Gaming DND Session Retention (#24)**:
  - Preserves Do Not Disturb (DND) gaming suppression during temporary Alt-Tab switches away from active games.
  - Automatically captures the running game process PID and name upon game detection.
  - Retains DND suppression for a configurable grace period (`RetainGameDndOnAltTab`, 300 seconds default) while the game process remains active.
  - Suppresses transient toast and HUD interruptions when multitasking, immediately releasing DND suppression when the game process terminates.
- **Borderless Fullscreen Game Detection**:
  - Enhanced gaming detection heuristics in `DndManager` combining `SHQueryUserNotificationState` with Win32 foreground window evaluation (`GetForegroundWindow`, `GetWindowRect`, window style examination via `WS_CAPTION`, and monitor dimension matching).
  - Accurately detects modern borderless windowed games without relying solely on exclusive DirectX mode.
- **Gaming HUD Overlay Prevention (#23)**:
  - Prevents notification popups (HUD) from overlaying full-screen games.
  - Dynamically lowers HUD window Z-order (`Topmost = false` via `KeepHudBehindFullscreen`) when a full-screen game or application is in focus so game rendering is never obscured.
- **Subtle In-App Version Indicator (#22)**:
  - Added subtle, clean version indicator badge in the main window UI header/footer for immediate visual verification of the running application version.
- **Extended Test Suite**:
  - Added 13 new unit tests across DND Alt-Tab retention, borderless game detection, HUD topmost layering, and stacking offset calculations, bringing total suite to **212 sequential passing tests** (100% pass rate).

### Fixed
- **HUD Countdown Progress Bar Animation (#20)**:
  - Corrected WPF `Storyboard` target resolution for `ScaleTransform.ScaleX` on the `CountdownBar` element namescope.
  - Progress bar smoothly and continuously animates down from full width to zero across the notification's display lifetime.
- **Notification Taskbar Overlap & Excessive Stacking Gaps (#21)**:
  - Pre-measures dynamic notification height (`MeasureHeight()`) prior to anchoring `Window.Top` and `Window.Left`.
  - Accurately anchors bottom-right notifications against the Windows 11 working area bounds to eliminate taskbar overlap.
  - Corrects cumulative height calculations to ensure uniform, consistent vertical spacing between stacked notification HUD windows.

---

## [1.2.0] - 2026-10-28

### Added
- **In-App Version Monitoring & Auto-Update Service (Tier 1 - Feature 1)**:
  - Automatic background monitoring for new GitHub releases on startup with user-friendly toggle in Settings.
  - Interactive top header pill notification (🚀 Update vX.Y.Z) routing directly to update details.
  - Integrated "About & Updates" section in Settings featuring current version display, manual "Check for Updates" trigger, release notes viewer, and real-time download progress.
  - In-place executable replacement leveraging Windows NTFS executable renaming semantics (GotifyWin.exe -> GotifyWin.exe.old), followed by seamless process relaunch and silent startup cleanup of old executables.
  - Cryptographic integrity verification streaming SHA-256 validation against SHA256SUMS.txt prior to extraction.
  - Deployment-aware asset selection automatically picking between self-contained (standalone.zip) and lightweight framework-dependent (light.zip) distributions.
- **Comprehensive Updater Test Suite**: 21 new tests bringing total suite to **199 sequential passing tests** (100% pass rate) covering SemVer parsing, checksum parsing, SHA-256 digest validation, mock GitHub API handling, and UI state transitions.

### Fixed
- **Workstation Lock Auto-Dismiss Freeze & Inconsistent Dismiss Button (#14)**:
  - Standardized NotificationHudWindow layout so every notification presents a reliable, uniform Dismiss button alongside custom action links.
  - Added thread-pool fallback timer (350ms) to ensure notifications dismiss cleanly even during OS message loop suspension or rapid pointer exit events.
  - Added SystemEvents.SessionSwitch detection in HudManager with automatic queue purging on workstation lock (SessionSwitchReason.SessionLock).
- **CodeQL Quality Hardening**:
  - Replaced Path.Combine with Path.Join to eliminate silent path root argument dropping warnings.
  - Replaced implicit sequence filtering and mapping loops with LINQ .Where and .Select.
  - Converted all generic and empty catch blocks to typed exception filters with structured diagnostic logging.

---
## [1.1.0] - 2026-10-27

### Added
- **Automated Stress & Scalability Test Suite**: 178 passing automated tests (up from 148), including high-load stress testing for 1,000 rapid message burst storms, 200 custom rules evaluation latency (< 18ms), 150 apps lookups, and anti-flap duplicate debounce under heavy ingest.
- **Working Set Compaction (MemoryOptimizer)**: Added Win32 `SetProcessWorkingSetSize` and forced generational GC heap compaction when minimized or closed to the system tray, keeping idle 24/7 background memory at ~25–35 MB.
- **Batch Message Ingestion**: Added `AddMessages(IEnumerable<GotifyMessage>)` to `FeedViewModel` for chronological bulk ingestion with single-pass FIFO capping.
- **Security & CI/CD Pipeline Hardening**: Integrated `SecurityCodeScan.VS2019`, .NET 8 `<NuGetAudit>`, Gitleaks, and continuous GitHub CodeQL SAST taint analysis in CI.

### Changed
- **Recycling UI Virtualization**: Replaced un-virtualized `ScrollViewer` + `ItemsControl` in `FeedPage` and `AppsPage` with native recycling `ListView` controls (`VirtualizingPanel.ScrollUnit="Pixel"`). Reduces active visual elements from 35,000+ down to ~8–10 viewport cards, eliminating UI thread stutter.
- **Bulk App Update Efficiency**: Batched `AppsViewModel.SetAllEnabled` so toggling 150+ applications writes to disk once rather than generating repeated synchronous disk writes.
- **Rule Limits**: Enforced safe ceiling of 500 rules in the Rule Builder with user warning feedback.

### Fixed
- **High Memory Bloat Exceeding 700MB+ (#6)**: Hardware-clamped image decode widths (`160px` for feed thumbnails, `72px` for app icons) in `SecureImageLoader` and `SecureImageConverter`, reducing per-image memory footprint from 33.1 MB down to ~0.10 MB (99.7% reduction).
- **Message History Feed Freezes (#4)**: Eliminated O(M × N) collection view refresh storms on incoming messages and resolved visual tree exhaustion via recycling virtualization.
- **Noise Analyzer False-Positive Suggestions (#5)**: Noise pattern analyzer now strictly ignores disabled applications (by `AppId` and `AppName`) and priority-suppressed clusters. Added dynamic recommendation pruning on app toggle.
- **HUD Countdown Progress Bar Stutter (#7)**: Replaced stepped `DispatcherTimer` ticks and `ProgressBar.Value` with GPU-accelerated `ScaleTransform.ScaleX` WPF `Storyboard` animation, delivering continuous 60+ FPS motion with interactive hover pause/resume and zero layout re-measures.

---
## [1.0.1] - 2026-10-25

### Fixed
- **Windows Notification Center Delivery**: Resolved issue where native toast notifications were silently discarded by Windows 11 due to missing COM `IPropertyStore` registration on unpackaged desktop shortcuts. Added `AumidHelper` with explicit `PKEY_AppUserModel_ID` shell registration and `SetCurrentProcessExplicitAppUserModelID`.
- **DND State Differentiation**: Correctly differentiated `SHQueryUserNotificationState` results so background/media busy states (`QUNS_BUSY`) are no longer erroneously labeled as "Gaming".
- **Presentation Options UX**: Automatically disable and dim floating banner settings (position, duration, progress bar) when "Windows Notification Center" is selected, and added an informative note explaining that native toasts are managed by Windows Accessibility settings.
- **Showcase & Tests**: Re-rendered clean screenshots with connected status and DND off; expanded automated test suite to 148 passing tests.

---

## [1.0.0] - 2026-10-24

### Initial Release

#### 🌟 Presentation & Desktop Experience
- **Windows 11 Fluent 2 Design**: Clean mica/acrylic background materials, high-contrast dark/light/system theme switching, and per-monitor high-DPI scaling.
- **Custom Floating HUD Banner**: Borderless, rounded acrylic notification banner with countdown progress bar, mouse-hover pause, directional screen stacking (Bottom-Right, Top-Right, Top-Left, Bottom-Left), and inline action buttons (*Open Link*, *Dismiss*).
- **Windows Action Center Integration**: Native WinRT toast notifications appearing in the Windows 11 notification center (`Win + N`) with deep history and protocol activation.
- **Hybrid Delivery Mode**: Support for instant HUD banner + persistent Action Center history simultaneously.
- **System Tray Integration**: Background minimize-to-tray, real-time connection status indicators, and one-click quick actions.

#### ⚡ Intelligent Filtering & Noise Control
- **Smart Noise Pattern Analyzer**: Deterministic clustering engine that analyzes notification history, masks transient variables (timestamps, durations, hashes, IPs, numbers), and surfaces 1-click recommendations for repetitive background noise.
- **Manual Rule Builder**: Multi-condition filter rules supporting Application scope, Target fields (Title, Message, Application Name), evaluation operators (Contains, StartsWith, EndsWith, Exact, Regex), and custom actions (*Silent Log*, *Drop Message*).
- **1-Click Rule Creation**: "⚡ Create Rule" button directly on message history cards to pre-fill rule parameters.
- **Anti-Spam / Flap Debounce**: Configurable debounce window (3s–60s) with SHA-256 duplicate aggregation counters (`x5`).
- **Audit Tagging**: Silently logged messages are visibly tagged in history with interactive badges (`🔕 Filtered: <RuleName>`).

#### 🗂️ Message History Feed
- **Dual View Modes**:
  - **Detailed View**: Rich multi-tier cards with application icons, priority badges, full message formatting, image previews, and direct action bars.
  - **Compact View**: High-density 36px single-line layout displaying 4× more notifications on screen with inline action buttons.
- **Hide Filtered Messages**: One-click toggle in the header bar to filter out silenced/dropped messages.
- **Search & App Filtering**: Real-time text search and per-application filter dropdown.

#### 🌙 Comprehensive Do Not Disturb (DND) Suite
- **Quick Snooze Presets**: 30 minutes, 1 hour, 4 hours, and until tomorrow (8:00 AM).
- **Indefinite Silence**: "On until I turn it off" toggle in both system tray and header flyout.
- **Scheduled Quiet Hours**: Automatic silent logging during designated sleep/focus hours (e.g. 22:00 to 07:00).
- **Emergency Breakthrough**: Configurable priority threshold (e.g. Priority $\ge$ 8) to break through DND for critical homelab alerts.
- **DirectX / Fullscreen Suppression**: Win32 `SHQueryUserNotificationState` detection to prevent notification popups during fullscreen gaming and presentations.

#### 🔒 Security & Privacy
- **Windows DPAPI Credential Storage**: Client tokens are encrypted at rest using Windows DPAPI with salted application entropy.
- **Origin-Isolated Icon Caching**: Application icons and attachments strictly restrict authentication headers to the configured Gotify origin.
- **Protocol Whitelisting**: Click URLs strictly enforce safe web schemes (`http://` and `https://`), preventing arbitrary OS protocol execution.
- **XML Sanitization**: Windows Action Center toast payloads are stripped of control characters and safely XML-escaped.
- **ReDoS Protection**: Regex evaluation is constrained by a 50ms timeout and cached via pre-compiled expressions.
- **WebSocket Frame Ceiling**: Maximum 512 KB payload ceiling to prevent memory exhaustion.
- **Atomic File Writes**: Atomic temporary file replacement for all settings and configuration updates.
