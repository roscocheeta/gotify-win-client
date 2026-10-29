# Agent Guidelines for GotifyWin

This document defines mandatory development workflows, branching policies, testing protocols, and architectural rules for AI agents and human contributors working on GotifyWin.

---

## 🌿 1. Branching & Git Workflow

- **Never push directly to `master`**: The `master` branch represents stable, released software. Direct pushes (even with admin permissions) are prohibited.
- **Base branch is `dev`**: All ongoing development, new features, and non-emergency bug fixes must branch off and merge into `dev`.
- **Branch Naming Conventions**:
  - `feat/<feature-name>` for new features or capabilities.
  - `fix/<bug-description>` for bug repairs.
  - `docs/<topic>` for documentation improvements.
  - `chore/<task>` for build, dependency, or maintenance updates.
- **Pull Request Protocol**:
  1. Complete code changes on the dedicated branch.
  2. Run `dotnet test GotifyWin.sln --configuration Release` locally and ensure:
     - 100% test pass rate.
     - 0 compiler warnings from `WpfAnalyzers` and `SecurityCodeScan`.
     - 0 dependency vulnerabilities reported by `<NuGetAudit>`.
  3. Push branch to GitHub: `git push -u origin <branch-name>`.
  4. Open a Pull Request targeting `dev` using the GitHub CLI:
     ```powershell
     gh pr create --base dev --title "type: short summary" --body "..."
     ```
  5. Verify PR outcomes, check-run annotations, and bot feedback:
     - Run `gh pr checks <id>` and ensure 100% check pass rate.
     - Inspect PR review comments from GitHub Advanced Security and automated bots:
       ```powershell
       gh api repos/:owner/:repo/pulls/<id>/comments
       ```
     - Check CodeQL check-run annotations for warnings or notices:
       ```powershell
       gh api repos/:owner/:repo/check-runs/<run_id>/annotations
       ```
     - Ensure zero CodeQL warnings/notes (e.g. unhandled IDisposable disposal, empty catch blocks, or overly broad exception catching).
  6. Merge PR into `dev` using squash-and-merge (`gh pr merge --squash`).
- **Release Protocol**:
  1. Prepare release metadata on a dedicated branch (`chore/prepare-vX.Y.Z-release`) off `dev`:
     - Bump versions in `src/GotifyWin/GotifyWin.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`) and `src/GotifyWin/app.manifest`.
     - Update `CHANGELOG.md` with release notes adhering to Keep a Changelog.
     - Run test suite locally, push branch, open PR into `dev`, verify CI, and squash-merge.
  2. Open a PR merging `dev` into `master` using the GitHub CLI:
     ```powershell
     gh pr create --base master --head dev --title "chore(release): release vX.Y.Z" --body "..."
     ```
  3. Verify all checks pass on the release PR (`Build & Test`, `CodeQL`, `Secret Scan`).
  4. Merge into `master` using squash-and-merge with admin override if required by linear history rules (`gh pr merge --squash --admin`).
  5. Tag release semantically from `master`:
     ```powershell
     git checkout master
     git pull origin master
     git tag -a vX.Y.Z -m "Release vX.Y.Z - <summary>"
     git push origin vX.Y.Z
     ```
  6. This automatically triggers `.github/workflows/release.yml` to compile and publish standalone and framework-dependent `.zip` release assets with SHA256 checksums.
  7. **Post-Release Reconcile (Mandatory)**: Synchronize `master` back into `dev` using the `ours` strategy to reconcile the squashed release commit and prevent future 3-way merge conflicts:
     ```powershell
     git checkout dev
     git merge -s ours origin/master -m "chore: sync master into dev post-vX.Y.Z release"
     git push origin dev
     ```
     *Why this is required*: Merging `dev` into `master` via squash creates a single new commit on `master`. Without back-merging, Git keeps the previous release as the common ancestor, leading to artificial merge conflicts on subsequent release PRs.

---

## 🧪 2. Testing Protocols & Blind Spot Prevention

- **Sequential WPF Testing**: All test collections must run sequentially (`[assembly: CollectionBehavior(DisableTestParallelization = true)]` and `xunit.runner.json`) to prevent Win32 `HwndSubclass` message pump collisions across MTA worker threads.
- **No Mocking Gaps on OS Boundaries**: When integrating with Win32, COM, or WinRT APIs:
  - Do not assume a call succeeded just because it did not throw.
  - Verify underlying system state (e.g., registry keys, shortcut property stores, AUMID registration).
  - Avoid empty `catch (Exception ex) { Debug.WriteLine(...); }` blocks that swallow OS failures during tests.
- **WPF STA Teardown**: Any test that creates UI elements, windows, or dispatchers must run on an STA thread and explicitly invoke `manager.DismissAll()` and `Dispatcher.CurrentDispatcher.InvokeShutdown()` upon completion.
- **ViewModel Testing**: Use lightweight stubs (e.g., `DummyPresenter`) rather than full window-spawning presenters when testing ViewModels to avoid background thread message loop hooks.

---

## 🔒 3. Security & Coding Standards

- **Zero Secrets**: Never commit personal Gotify server URLs, API tokens, or homelab credentials.
- **Automated Secret Scanning**: Gitleaks runs in CI and should be run on staged commits locally (`gitleaks protect --staged`).
- **Compile-Time SAST**: `SecurityCodeScan.VS2019` is integrated into the build. Code changes must not introduce any security warnings (`SCSxxxx`).
- **Dependency Auditing**: .NET 8 `<NuGetAudit>` is configured for all direct and transitive dependencies at `moderate` severity. Pulling in vulnerable packages fails the build. Run `dotnet list package --vulnerable --include-transitive` to verify.
- **Semantic Taint Tracking**: GitHub CodeQL runs continuously in CI, tracing untrusted server payloads to system sinks.
- **Win32 P/Invoke & CodeQL Interop**: Native Windows desktop shell and window management APIs (e.g. `SetProcessWorkingSetSize`, `SHQueryUserNotificationState`, `GetForegroundWindow`, `GetWindowRect`) have no pure managed C# BCL alternatives. Informational rules `cs/unmanaged-code` and `cs/call-to-unmanaged-code` are excluded in `.github/codeql/codeql-config.yml` to prevent false-positive PR review notes while maintaining 100% active security-extended vulnerability scanning.
- **Cryptographic Randomness**: Never instantiate `new Random()` for jitter, tokens, or security calculations. Use `System.Security.Cryptography.RandomNumberGenerator`.
- **Windows DPAPI**: All credentials stored on disk must use Windows DPAPI (`ProtectedData.Protect`) with salted application entropy.
- **URL Whitelisting**: Any URL clicked from toasts or UI must be strictly validated for `http` or `https` schemes before invoking `Process.Start`.
- **XML Sanitization**: Native toast XML payloads must strip invalid XML 1.0 control characters and XML-escape entities.
- **WPF MVVM Binding**: Use `Mode=OneWay` on read-only XAML bindings (especially `<Run>` text blocks) to prevent two-way binding reflection errors.
