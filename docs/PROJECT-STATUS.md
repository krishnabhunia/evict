# Evict Uninstaller — Project Status & Spec

**Goal:** a Windows uninstaller equivalent to IObit Uninstaller, owned by Krishna, delivered as a portable EXE and an installer.
**Decisions (16 Sep 2026):** C# / .NET 8 / WPF · near-full clone in 3 incremental builds · cloud-built EXE, no local toolchain needed · portable first, Inno Setup installer in Build 3 · code in GitHub `krishnabhunia/evict` with Actions CI · name stays "Evict".

## Delivery — Build 3 (v1.2.0), 16 Sep 2026 — all three builds complete

| Item | Location |
|---|---|
| `Evict.exe` 1.2.0 (66 MB, self-contained win-x64, single file) | `G:\My Drive\Windows Software and Apps\Evict\` — as 4 parts + `Join-Evict.cmd` (double-click once to join and clean up; uploads are capped at 20–30 MB per file) |
| `Evict-Setup-1.2.0.exe` installer | Built by GitHub Actions only (Inno Setup runs on the Windows runner): **Actions → latest run → Artifacts → `Evict-1.2.0-<sha>`**, or the **Release `v1.2.0`** assets once the tag build finishes |
| Source code | GitHub `https://github.com/krishnabhunia/evict` (branch `main`, tags `v1.1.0`, `v1.2.0`) and `Evict-source-v1.2.0.zip` in the Drive folder |
| README / CHANGELOG / this status | repo root + Drive folder |
| SHA-256 of Evict.exe 1.2.0 (Linux cross-build) | see `Evict.exe.sha256` in the Drive folder (the CI-built exe has a different hash – different build machine, same source) |

## Architecture

| Layer | Project | Notes |
|---|---|---|
| Platform logic | `src/Evict.Core` (net8.0, `SupportedOSPlatform=windows`) | Registry (HKLM 64/32 + HKCU Uninstall keys), UserAssist last-used, WMI restore points, PowerShell (Appx, Get-HotFix), winget, FileSystemWatcher + registry snapshot (Install Monitor), shell Recycle Bin (SHFileOperation), services/tasks, GitHub Releases update check + download + self-replace |
| UI | `src/Evict.App` (net8.0-windows, WPF, CommunityToolkit.Mvvm) | Custom Fluent-style theme (Light/Dark), sidebar nav, 9 pages, 9 dialog windows, custom title bar via WindowChrome, zoom 90–140 % |
| Tests | `tests/Evict.Core.Tests` (xunit, 157 tests) | Pure logic: name normaliser/matching, uninstall-string parser, winget table parser, Chromium prefs parser, path utils, command line, update-check parsing/version compare/asset selection |
| Installer | `installer/Evict.iss` (Inno Setup 6.3+) | per-user default (no UAC) / all-users; tasks: desktop icon, context menu, Send to; registry marker `Software\Evict\InstallDir` tells the app it is an installed copy; `/EVICTUPDATE=1` relaunches after a silent self-update |
| CI | `.github/workflows/build.yml` | windows-latest: restore → tests → xaml_check → publish → sign (optional, secrets `SIGN_PFX_BASE64`/`SIGN_PFX_PASSWORD`) → ISCC → sha256 → artifact; tag `v*` → GitHub Release with `Evict.exe`, `Evict-Setup-x.y.z.exe` + `.sha256` |
| Build (local) | `build/publish.sh`, `build/publish.ps1`, `build/xaml_check.py` | Cross-compiled from Linux with `EnableWindowsTargeting`; single-file compressed publish |

## Module status

| # | Module | Status | Notes |
|---|---|---|---|
| 1 | Programs list + tabs (All / Recent / Large / Infrequent / Bundleware / Broken) | ✅ B1 | Bundleware = installs within 4 min from a different, non-trusted publisher (heuristic) + known-name list |
| 2 | Uninstall wizard (restore point → uninstaller → Powerful Scan → review → clean → summary) | ✅ B1 | Silent mode for MSI / Inno / NSIS / InstallShield |
| 3 | Powerful Scan leftovers (files, shortcuts, registry, services, tasks) | ✅ B1 | Confidence Safe/Likely/Review; protected paths never proposed |
| 4 | Batch uninstall | ✅ B1 | Sequential, one combined review |
| 5 | Force Uninstall | ✅ B1 | Program or folder/exe target; kills processes |
| 6 | Windows Apps (Appx) incl. bloatware list, de-provision | ✅ B1 | PowerShell `Get/Remove-AppxPackage` |
| 7 | Browser Extensions (Chromium family + Firefox) | ✅ B1 | Removal needs browser closed; prefs backed up |
| 8 | Software Updater (winget) | ✅ B1 | Requires App Installer; live output |
| 9 | Install Monitor + "Uninstall using this log" | ✅ B1 | Watcher may miss events under extreme load (flagged) |
| 10 | Tools: File Shredder, Windows Updates, Restore Point, shortcuts | ✅ B1 | |
| 11 | History + CSV export + rescan | ✅ B1 | |
| 12 | Settings, Light/Dark theme | ✅ B1 | |
| 13 | Software Health dashboard (home page, score + 8 tiles) | ✅ B2 | Auto-scan on start (setting) |
| 14 | Easy Uninstall drag-to-window widget | ✅ B2 | WindowFromPoint → process → program; drop .exe/.lnk |
| 15 | Explorer right-click "Uninstall with Evict" + CLI + single-instance forwarding | ✅ B2 | HKCU by the app; Setup can also write HKLM (all users) – app recognises both |
| 16 | Startup Apps manager | ✅ B2 | StartupApproved switch like Task Manager |
| 17 | Residual Cleaner (history / broken entries / unmatched folders) | ✅ B2 | Unmatched folders always "Review" |
| 18 | Known-bundleware list | ✅ B2 | user-extensible JSON |
| 19 | Text size 90–140 %, themed ComboBox/ScrollBar/Tab/menus | ✅ B2 | |
| 20 | GitHub repo + Actions workflow | ✅ B2/B3 | pushed with Krishna's fine-grained token; first CI result not yet viewed (API blocked from the cloud session) |
| 21 | Inno Setup installer | ✅ B3 | compiled by CI; not yet run on a real machine |
| 22 | In-app update check + download + install/self-replace | ✅ B3 | needs the repo (or its releases) to be **public** — a private repo answers 404 → "no published release" |
| 23 | Code signing | ✅ B3 (optional CI step) | no certificate yet → SmartScreen warning stays; options in README |
| 24 | Real-time Install Monitor (filter driver) | ✗ out of scope | |

## Verification done / not done

| Check | Result |
|---|---|
| C# compile (Release), 0 warnings | ✅ |
| 157 xunit tests | ✅ all pass (run on Linux) |
| XAML static checks (resources, theme parity, binding roots, XML well-formed) | ✅ 0 problems |
| PE inspection of EXE (x64, GUI, version 1.2.0.0, manifest, icon) | ✅ |
| Workflow YAML structure | ✅ parsed; step order verified |
| Inno Setup script compiled | ❌ only possible on the Windows CI runner — check the first Actions run |
| **Running the UI on Windows** (any build) | ❌ not yet — Krishna chose to skip QA; all builds are cross-compiled blind |
| Update flow end-to-end (banner → download → swap → restart) | ❌ needs a public release newer than the running version |

## Known limitations / risks to test first

- Icon glyphs use Segoe MDL2 / Fluent Icons code points; a wrong code point shows as a box (cosmetic).
- Bundleware and Infrequently-Used are heuristics (registry timestamps, UserAssist) — labelled as such in the UI.
- Non-admin mode: HKLM leftovers, services, restore points and Program Files cleanup fail with "access denied" → use *Restart as administrator*.
- Chrome may show "settings were reset" once after an extension is removed externally (integrity MAC removed along with the entry).
- winget table parsing assumes ASCII-width columns; names with wide (CJK) characters may mis-parse (row is skipped).
- Portable self-update needs write access to the folder holding `Evict.exe` (fails in Program Files without admin — the dialog says so).
- Unsigned binaries: SmartScreen "Windows protected your PC" on first run and possible antivirus heuristics until a certificate is added (README → Code signing).

## Next steps (after the 3 builds)

1. **Look at GitHub Actions** (`https://github.com/krishnabhunia/evict/actions`): the `v1.2.0` tag run should produce the installer and a Release. If the Inno Setup step fails, paste the log here.
2. **Make the repository public** (or at least publish releases) so the in-app update check works; alternatively keep it private and distribute the Actions artifacts manually.
3. **QA on the laptop** — still not done for any build: open every page/dialog, try the widget, one real uninstall via Install Monitor, install with `Evict-Setup-1.2.0.exe`, then a hot-fix `1.2.x`.
4. **Decide on the GitHub token**: it is still valid and is only used for pushes from the build session; revoke it at `https://github.com/settings/personal-access-tokens` when the project is closed.
5. Optional: a code-signing certificate (Azure Trusted Signing is the cheapest route with immediate SmartScreen trust).
