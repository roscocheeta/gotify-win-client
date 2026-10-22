# GotifyWin

> A sleek, minimal, and deeply configurable [Gotify](https://gotify.net) push notification client designed for Windows 11.

GotifyWin is built specifically for homelab operators, sysadmins, and developers who route notifications from **Home Assistant, Uptime Kuma, Proxmox, TrueNAS, Docker, Sonarr, Plex, and CI/CD pipelines** to their Windows 11 desktop.

Unlike legacy tray wrappers, GotifyWin brings **Windows 11 Fluent 2 aesthetics**, a **4-tier granular filtering engine**, an **ultra-sleek floating HUD overlay**, and an intelligent **Do Not Disturb (DND) suite**.

---

## Key Features

### 📱 Selective App Management
* **App Auto-Discovery**: Automatically syncs applications from your Gotify server (`GET /application`).
* **Per-App Controls**: Toggle notifications per application on/off with one click.
* **Local Icon Caching**: Automatically downloads and caches application icons (`/application/{id}/image`) so notifications display service logos (Home Assistant, Docker, Uptime Kuma, etc.).
* **Per-App Priority Thresholds**: Configure individual minimum priority filters per application.

### ⚡ Intelligent Noise Reduction & Smart Recommendations
* **Automated Pattern Detection**: Analyzes local notification history using deterministic variable-masking clustering (normalizes timestamps, durations, numbers, IPs, and hashes) to detect repetitive operational noise (minimum 5 occurrences).
* **1-Click Muting & Progressive Disclosure**:
  * **💡 Smart Recommendations**: One-click `[🔕 Silent Log]` (suppresses desktop popups/chimes while keeping the record) or `[🔇 Drop]` directly from automated suggestion cards.
  * **Interactive Match Preview**: Expandable drawer allows users to review the exact past messages matching any recommendation before applying it.
  * **🛡️ Active Rules Management**: Clean overview to toggle, monitor, and delete active rules.
  * **⚙️ Advanced Rule Builder**: Collapsible power-user drawer for custom regex, multi-condition matching, and priority overrides.
* **Audit Tagging & Log Flagging**: Silently logged messages are visually tagged in the feed with an interactive badge (`🔕 Filtered: <RuleName>`), providing complete transparency.
* **Safety First**: Intelligent safeguards prevent muting high-priority alerts ($\ge 7$) or notifications containing failure keywords (`error`, `down`, `critical`, `failed`, `corrupt`).
* **Anti-Spam / Flap Debounce**: Deduplicates flapping monitor alerts within a configurable time window (3s–60s) with duplicate counters (`x5`).

### 🔔 Maximum Presentation Customization
* **Delivery Modes**:
  * **Custom Fluent HUD Pill (Recommended)**: An ultra-clean, rounded, borderless floating window with Mica/Acrylic material. Features a visual 2px countdown progress bar that pauses on mouse hover, and quick action buttons (*Open Link*, *Dismiss*).
  * **Windows 11 Native Action Center Toasts**: Integrates into the Windows 11 notification drawer (`Win + N`) with deep history and protocol activation.
  * **Hybrid Mode**: Instant glanceable floating HUD + Action Center history.
* **Custom Positioning**: Screen placement in `Bottom-Right`, `Top-Right`, `Top-Center`, or `Bottom-Center`.
* **Adjustable Duration**: Configure auto-dismiss timeout from 2 to 20 seconds.
* **Audio Alerts**: Standard Windows chimes, custom `.wav` sound files, or completely silent mode.

### 🌙 Comprehensive Do Not Disturb (DND) Suite
* **System Tray Quick Snooze**: Toggle DND or select timed snooze presets (*30 minutes*, *1 hour*, *4 hours*, or *Until Tomorrow 8:00 AM*).
* **Scheduled Quiet Hours**: Automatic silent logging during defined sleep/focus periods (e.g., 22:00 to 07:00).
* **Emergency Breakthrough**: Configurable threshold (e.g., Priority $\ge$ 8) that breaks through DND for critical emergencies (e.g. water leak, disk failure, power outage).
* **Smart Gaming & Fullscreen Suppression**: Win32 `SHQueryUserNotificationState` detection automatically suppresses popups during fullscreen DirectX games, movies, and presentations.

### 🔒 Security-Hardened Architecture
* **Windows DPAPI Credential Protection**: Server client tokens are encrypted at rest using Windows Data Protection API (DPAPI) with salted application entropy.
* **Origin Isolation**: Client tokens are strictly restricted to the configured Gotify origin and never transmitted when fetching external app icons.
* **Protocol Whitelisting**: Click action links strictly enforce `http://` and `https://` schemes, preventing arbitrary OS protocol execution.
* **XML 1.0 Sanitization**: Native toast payloads are stripped of invalid ASCII control characters and safely XML-escaped.
* **ReDoS Protection**: Regex evaluation is constrained by a 50ms timeout and cached via pre-compiled expressions.
* **Frame Limits**: WebSocket streams enforce a 512 KB payload ceiling to prevent Large Object Heap memory exhaustion.
* **Atomic File Writes**: Configuration updates use atomic temporary file replacement, preventing config corruption on crashes.

---

## Screenshots & UI Overview

```
+-----------------------------------------------------------------------------------+
|  [Gotify Client]       ● Connected (192.168.1.50)      [🌙 DND: Off]   [⚙ Settings] |
+-----------------------------------------------------------------------------------+
|  [ 🔍 Search notifications...                             ] [ Filter: All Apps ▼ ] |
+-----------------------------------------------------------------------------------+
|  +-----------------------------------------------------------------------------+  |
|  | [Icon]  Uptime Kuma  •  2m ago                             [Priority: High] |  |
|  | AdGuard Home is DOWN                                                        |  |
|  | Connection timed out after 5000ms.                                          |  |
|  | [🔗 Open Status Page]  [📋 Copy]  [✕ Dismiss]                               |  |
|  +-----------------------------------------------------------------------------+  |
|  +-----------------------------------------------------------------------------+  |
|  | [Icon]  Home Assistant  •  14m ago                         [Priority: Low]  |  |
|  | Washing machine cycle completed.                                            |  |
|  +-----------------------------------------------------------------------------+  |
+-----------------------------------------------------------------------------------+
|  Tabs: [Notifications] [Apps] [Filter Rules] [Settings]                            |
+-----------------------------------------------------------------------------------+
```

---

## Getting Started

### Prerequisites
* Windows 10 (version 19041+) or **Windows 11** (recommended).
* [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

### Installation & Running

1. Clone the repository:
   ```powershell
   git clone https://github.com/your-username/gotify-win-client.git
   cd gotify-win-client
   ```
2. Build the project:
   ```powershell
   dotnet build -c Release
   ```
3. Run the client:
   ```powershell
   dotnet run --project src/GotifyWin/GotifyWin.csproj
   ```

### Publishing a Standalone Executable
To create a single portable `.exe` with zero installation required:
```powershell
dotnet publish src/GotifyWin/GotifyWin.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/
```

---

## Configuration

Settings are stored securely in `%AppData%\GotifyWin\config.json`:

```json
{
  "ServerUrl": "https://gotify.homelab.lan",
  "ClientToken": "enc:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA...",
  "StartWithWindows": false,
  "MinimizeToTrayOnClose": true,
  "StartMinimized": false,
  "MaxHistoryCount": 200,
  "GlobalMinPriority": 0,
  "Presentation": {
    "Mode": 0,
    "Position": 0,
    "DisplayDurationSeconds": 5.0,
    "ShowCountdownBar": true,
    "PlaySound": true,
    "EnableDebounce": true,
    "DebounceWindowSeconds": 10
  },
  "Dnd": {
    "IsManualDndActive": false,
    "EmergencyBypassPriority": 8,
    "EnableEmergencyBypass": true,
    "QuietHoursEnabled": false,
    "QuietHoursStart": "22:00:00",
    "QuietHoursEnd": "07:00:00",
    "SuppressOnFullscreenApps": true
  }
}
```

---

## Testing & Verification

The project includes an automated test suite covering filter routing, debounce caches, quiet hour timespans, message parsing, and security validations:

```powershell
dotnet test
```

```
Test run for GotifyWin.Tests.dll (.NETCoreApp,Version=v8.0)
Total tests: 44
Passed: 44
Failed: 0
Skipped: 0
```

---

## Technology Stack

* **UI Framework**: WPF on .NET 8.0 (`net8.0-windows10.0.19041.0`)
* **Styling**: Modern Fluent 2 Design (`WPF-UI`)
* **Notifications**: WinRT `Windows.UI.Notifications` + Custom Acrylic/Mica HUD Overlay
* **System Tray**: Windows Shell Tray with Context Menu & DND Presets
* **Security**: Windows DPAPI (`System.Security.Cryptography.ProtectedData`)

---

## License

This project is licensed under the [MIT License](LICENSE).
