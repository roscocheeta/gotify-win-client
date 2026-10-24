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
  2. Run `dotnet test GotifyWin.sln --configuration Release` locally and ensure a 100% pass rate.
  3. Push branch to GitHub: `git push -u origin <branch-name>`.
  4. Open a Pull Request targeting `dev` using the GitHub CLI:
     ```powershell
     gh pr create --base dev --title "type: short summary" --body "..."
     ```
  5. Wait for GitHub Actions (`ci.yml`) to complete and pass all automated tests.
  6. Merge PR into `dev` using squash-and-merge (`gh pr merge --squash`).
- **Release Protocol**:
  1. Open a PR merging `dev` into `master` when preparing a milestone release.
  2. Bump versions in `src/GotifyWin/GotifyWin.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`) and `src/GotifyWin/app.manifest`.
  3. Update `CHANGELOG.md` with release notes adhering to Keep a Changelog.
  4. Merge into `master`.
  5. Tag release semantically from `master`:
     ```powershell
     git tag -a vX.Y.Z -m "Release vX.Y.Z - <summary>"
     git push origin vX.Y.Z
     ```
  6. This automatically triggers `.github/workflows/release.yml` to compile and publish standalone and framework-dependent `.zip` release assets with SHA256 checksums.

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
- **Windows DPAPI**: All credentials stored on disk must use Windows DPAPI (`ProtectedData.Protect`) with salted application entropy.
- **URL Whitelisting**: Any URL clicked from toasts or UI must be strictly validated for `http` or `https` schemes before invoking `Process.Start`.
- **XML Sanitization**: Native toast XML payloads must strip invalid XML 1.0 control characters and XML-escape entities.
- **WPF MVVM Binding**: Use `Mode=OneWay` on read-only XAML bindings (especially `<Run>` text blocks) to prevent two-way binding reflection errors.
