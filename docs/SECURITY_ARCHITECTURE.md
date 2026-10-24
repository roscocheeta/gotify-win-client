# Security Architecture & Threat Model

This document outlines the security architecture, threat model, and defense-in-depth safeguards implemented in GotifyWin.

GotifyWin is designed to process notifications from local and remote networks safely, protecting the host system from untrusted payloads, credential exfiltration, and local tampering.

---

## 🛡️ Core Security Principles

GotifyWin adheres to **Kerckhoffs's Principle**: the client's security does not rely on obscurity, but on cryptographically sound primitives, strict input validation, and least-privilege resource isolation.

```
┌────────────────────────────────────────────────────────────────────────┐
│                        Threat Model & Defense Layers                   │
├───────────────────────┬──────────────────────┬─────────────────────────┤
│ Credential Storage    │ Network & Origin     │ Input & Presentation    │
├───────────────────────┼──────────────────────┼─────────────────────────┤
│ • Windows DPAPI       │ • Header-Only Auth   │ • Scheme Whitelisting   │
│ • Per-User Entropy    │ • Origin Isolation   │ • XML 1.0 Sanitization  │
│ • No Plaintext Export │ • No Leak on CDNs    │ • ReDoS Timeouts (50ms) │
│ • Memory Obfuscation  │ • 512KB Frame Cap    │ • SHA-256 Flap Defense  │
└───────────────────────┴──────────────────────┴─────────────────────────┘
```

---

## 1. Credential Security at Rest & In Memory

### Windows Data Protection API (DPAPI)
* Server client tokens are encrypted at rest in `%AppData%\GotifyWin\config.json` using Windows DPAPI (`System.Security.Cryptography.ProtectedData.Protect`).
* **Entropy**: Encryption utilizes `DataProtectionScope.CurrentUser` combined with application-specific salted entropy bytes.
* **Access Boundary**: Tokens cannot be decrypted by other user accounts on the same machine, or by offline disk access from another operating system.
* **Corrupt File Protection**: If DPAPI ciphertext fails decryption (e.g. user roaming profile corruption), GotifyWin retains the raw ciphertext in memory without overwriting or destroying it with empty strings, preventing silent token erasure.

### Settings Backup / Export Protection
* When exporting settings profiles (`Export Settings`), client tokens are **strictly excluded** from the exported JSON (`exportConfig.ClientToken = string.Empty`).
* Export files can be safely shared, committed to dotfile repositories, or backed up to cloud drives without exposing access credentials.

---

## 2. Network & Origin Isolation

### Header-Only Authentication
* The Gotify client token is transmitted exclusively via standard HTTP/WebSocket headers (`X-Gotify-Key`).
* Tokens are **never passed as URL query parameters** in production, preventing token leakage in intermediary proxy logs, reverse-proxy access logs (NGINX/Traefik), and browser history.

### Strict Origin Isolation for Icons & Attachments
* Gotify notifications often contain external image links or app icons (`/application/{id}/image`).
* `GotifyApiService` and `SecureImageLoader` enforce an origin check:
  * Authentication headers (`X-Gotify-Key`) are **only attached** to requests matching the configured Gotify server origin (`Scheme` + `Host` + `Port`).
  * Any request redirecting or pointing to third-party CDNs (e.g., Imgur, GitHub, TMDB) has authentication headers stripped, preventing credential exfiltration to external web servers.

### WebSocket Denial of Service Protections
* **Frame Limits**: Incoming WebSocket messages enforce a hard ceiling of **512 KB** per frame. Messages exceeding this threshold are dropped, preventing Large Object Heap (LOH) exhaustion and memory denial of service.
* **Connection Resiliency**: Connection attempts use an exponential backoff jitter (2s up to 60s) to prevent hammering server endpoints during network outages.

---

## 3. Presentation & Execution Safety

### Protocol Whitelisting (Click URLs)
* Gotify messages may contain click action links (`client::notification` extras).
* When the user clicks an action button or toast banner, the target URL is strictly validated:
  * Only `http://` and `https://` schemes are permitted.
  * System protocols (e.g. `file://`, `cmd://`, `powershell://`, `ms-msdt:`) and local file paths are rejected.
  * Links are launched via the Windows Shell default browser with `UseShellExecute = true` without shell argument injection.

### XML 1.0 Sanitization for Native Windows Toasts
* Windows 11 Action Center toasts consume XML payloads via `Windows.UI.Notifications`.
* Raw notification titles and messages are stripped of non-printable ASCII control characters (`[\x00-\x08\x0B\x0C\x0E-\x1F]`) and safely XML entity-encoded (`&amp;`, `&lt;`, `&gt;`, `&quot;`, `&apos;`).
* This eliminates the risk of toast XML injection or malformed payload crashes in the Windows Shell.

### ReDoS (Regular Expression Denial of Service) Protection
* User-defined filter rules support regex pattern matching.
* All regex evaluations enforce an explicit **50ms timeout** (`TimeSpan.FromMilliseconds(50)`).
* Regex instances are bounded in a thread-safe LRU cache (capped at 100 patterns) to prevent unbounded memory growth from dynamic pattern compilation.

### Anti-Spam / Flap Debounce Protection
* Rapidly flapping monitoring alerts (e.g. Uptime Kuma flapping every second) can cause notification storms.
* Debounce cache keys are computed using **SHA-256 cryptographic hashes** of the normalized notification payload.
* The debounce cache is bounded to 500 active entries with time-based eviction, preventing memory exhaustion under sustained flood attacks.

---

## 4. Verification & Automated Security Tests

The security architecture is verified on every pull request and release via automated security tests in `tests/GotifyWin.Tests/`:
* `SecurityTests.cs`: Origin isolation, XML toast escaping, safe URL protocol whitelisting.
* `ConfigServiceTests.cs`: DPAPI encryption, roundtrip decryption, tamper resiliency.
* `TransportSecurityTests.cs`: Export token stripping, header-only authentication.
* `FilterEngineTests.cs`: ReDoS timeout enforcement, debounce cache bounds.
