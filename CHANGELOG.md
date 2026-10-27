# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
