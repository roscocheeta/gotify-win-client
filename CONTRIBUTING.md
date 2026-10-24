# Contributing to GotifyWin

Thank you for your interest in contributing to GotifyWin! Whether you are fixing a bug, improving documentation, or proposing a new feature, your help is warmly welcomed.

---

## 🛠️ Development Setup

### Prerequisites
* **Operating System**: Windows 10 (version 19041+) or **Windows 11** (recommended).
* **SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
* **IDE**: Visual Studio 2022 (with *.NET Desktop Development* workload) or VS Code / JetBrains Rider.

### Clone and Build

1. Fork and clone the repository:
   ```powershell
   git clone https://github.com/<your-username>/gotify-win-client.git
   cd gotify-win-client
   ```

2. Restore and build the solution (this automatically runs `WpfAnalyzers`, `SecurityCodeScan`, and `.NET 8 NuGetAudit`):
   ```powershell
   dotnet build GotifyWin.sln
   ```

3. Run the automated test suite:
   ```powershell
   dotnet test GotifyWin.sln
   ```

4. Verify dependency security:
   ```powershell
   dotnet list package --vulnerable --include-transitive
   ```

5. Launch the application:
   ```powershell
   dotnet run --project src/GotifyWin/GotifyWin.csproj
   ```

---

## 🏗️ Architecture Overview

GotifyWin follows the **Model-View-ViewModel (MVVM)** pattern with dependency injection and clean separation of concerns:

* **`src/GotifyWin/Models/`**: Data models for Gotify API messages, applications, and settings (`GotifyClientConfig`).
* **`src/GotifyWin/Services/`**: Core services:
  * `GotifyWebSocketService`: Resilient WebSocket client with automatic reconnection, power-event resume, and heartbeat.
  * `GotifyApiService`: REST API client for testing connections, fetching apps, and icon caching.
  * `NotificationPresenter`: WinRT Windows 11 Action Center toast integration.
  * `HudNotificationManager`: Floating Acrylic/Mica HUD notification banner overlay with stacking queue.
  * `FilterEngine`: 4-tier filtering, multi-condition rules, and flap debounce cache.
  * `NotificationPatternAnalyzer`: Deterministic noise pattern recognition and clustering engine.
  * `DndManager`: Do Not Disturb manager (quiet hours, emergency breakthrough, fullscreen suppression).
  * `ConfigService`: Configuration storage with Windows DPAPI credential protection.
* **`src/GotifyWin/ViewModels/`**: ViewModels bound to the UI (`MainViewModel`, `FeedViewModel`, `AppsViewModel`, `RulesViewModel`, `SettingsViewModel`).
* **`src/GotifyWin/Views/`**: WPF XAML views styled with Windows 11 Fluent 2 aesthetics (`WPF-UI`).
* **`tests/GotifyWin.Tests/`**: Complete xUnit test suite (165 tests) verifying rules, parsing, encryption, debounce logic, WebSocket lifecycle, and OS integration contracts.

---

## 🔒 Security & Code Quality Standards

Before submitting a Pull Request, please ensure:

1. **Tests Pass**: Run `dotnet test GotifyWin.sln --configuration Release` locally and verify all 165+ automated tests pass with 0 failures.
2. **Clean Security Scan**: Ensure `dotnet build` reports 0 warnings from `SecurityCodeScan` (`SCSxxxx`) and `WpfAnalyzers` (`WPFxxxx`).
3. **No Vulnerable Dependencies**: Ensure `dotnet list package --vulnerable --include-transitive` returns 0 vulnerable packages.
4. **No Hardcoded Secrets**: Ensure no personal server URLs, API tokens, or test credentials are committed. PRs are automatically checked by Gitleaks.
5. **Code Style & Security**:
   * Follow standard C# coding conventions.
   * Prefer early returns and cancellation token propagation.
   * Never instantiate `new Random()` for tokens or security calculations; use `RandomNumberGenerator`.
   * In XAML, use explicit `Mode=OneWay` on bindings involving read-only properties or `<Run>` elements.
6. **Describe Your Changes**: Fill out the Pull Request template detailing what was changed and why.

---

## 💬 Community & Questions

Feel free to open an issue or discussion on GitHub for feedback, ideas, or architectural discussions.
