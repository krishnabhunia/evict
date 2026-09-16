# Evict Uninstaller — Project Status & Spec

**Goal:** a Windows uninstaller equivalent to IObit Uninstaller, owned by Krishna, delivered as a portable EXE.
**Decisions (16 Sep 2026):** C# / .NET 8 / WPF · near-full clone in 3 incremental builds · cloud-built EXE, no local toolchain needed · portable first, Inno Setup installer later.

## Delivery — Build 1 (v1.0.0)

| Item | Location |
|---|---|
| `Evict.exe` (66 MB, self-contained win-x64, single file) | `G:\My Drive\Windows Software and Apps\Evict\` — as 4 parts + `Join-Evict.cmd` (double-click once to join; chat and Drive uploads are capped at 20–30 MB per file) |
| Source code | same folder: `Evict-source-v1.0.0.zip`; also attached in chat |
| README (features, build, layout, safety design) | same folder: `README.md` |
| SHA-256 of Evict.exe | `175c3929854648c8630db29585a241bf3949675280722a431979b0124ff74faa` |

## Architecture

| Layer | Project | Notes |
|---|---|---|
| Platform logic | `src/Evict.Core` (net8.0, `SupportedOSPlatform=windows`) | Registry (HKLM 64/32 + HKCU Uninstall keys), UserAssist last-used, WMI restore points, PowerShell (Appx, Get-HotFix), winget, FileSystemWatcher + registry snapshot (Install Monitor), shell Recycle Bin (SHFileOperation), services/tasks |
| UI | `src/Evict.App` (net8.0-windows, WPF, CommunityToolkit.Mvvm) | Custom Fluent-style theme (Light/Dark), sidebar nav, 8 pages, 4 dialog windows, custom title bar via WindowChrome |
| Tests | `tests/Evict.Core.Tests` (xunit, 98 tests) | Pure logic: name normaliser/matching, uninstall-string parser, winget table parser, Chromium prefs parser, path utils |
| Build | `build/publish.sh`, `build/publish.ps1`, `build/xaml_check.py` | Cross-compiled from Linux with `EnableWindowsTargeting`; single-file compressed publish |

## Module status

| # | Module | Build 1 | Notes |
|---|---|---|---|
| 1 | Programs list + tabs (All / Recent / Large / Infrequent / Bundleware / Broken) | ✅ | Bundleware = installs within 4 min from a different, non-trusted publisher (heuristic) |
| 2 | Uninstall wizard (restore point → uninstaller → Powerful Scan → review → clean → summary) | ✅ | Silent mode for MSI / Inno / NSIS / InstallShield |
| 3 | Powerful Scan leftovers (files, shortcuts, registry, services, tasks) | ✅ | Confidence Safe/Likely/Review; protected paths never proposed |
| 4 | Batch uninstall | ✅ | Sequential, one combined review |
| 5 | Force Uninstall | ✅ | Program or folder/exe target; kills processes |
| 6 | Windows Apps (Appx) incl. bloatware list, de-provision | ✅ | PowerShell `Get/Remove-AppxPackage` |
| 7 | Browser Extensions (Chromium family + Firefox) | ✅ | Removal needs browser closed; prefs backed up |
| 8 | Software Updater (winget) | ✅ | Requires App Installer; live output |
| 9 | Install Monitor + "Uninstall using this log" | ✅ | Watcher may miss events under extreme load (flagged) |
| 10 | Tools: File Shredder, Windows Updates, Restore Point, shortcuts | ✅ | |
| 11 | History + CSV export + rescan | ✅ | |
| 12 | Settings, Light/Dark theme | ✅ | |
| 13 | Easy Uninstall drag-to-window widget | ⏳ Build 2 | Win32 WindowFromPoint → process → program |
| 14 | Explorer right-click "Uninstall with Evict" | ⏳ Build 2 | needs installer / registry integration |
| 15 | Inno Setup installer, code signing | ⏳ Build 3 | |
| 16 | Real-time Install Monitor (filter driver) | ✗ out of scope | |

## Verification done / not done

| Check | Result |
|---|---|
| C# compile (Release), 0 warnings | ✅ |
| 98 xunit tests | ✅ all pass (run on Linux) |
| XAML static checks (resources, theme parity, binding roots, XML well-formed) | ✅ 0 problems |
| PE inspection of EXE (x64, GUI, manifest asInvoker + PerMonitorV2, icon, version info) | ✅ |
| **Running the UI on Windows** | ❌ not yet — cross-compiled on Linux; first launch/visual test happens on Krishna's laptop (Claude can drive it via computer-use with approval) |

## Known limitations / risks to test first

- Icon glyphs use Segoe MDL2 / Fluent Icons code points; a wrong code point shows as a box (cosmetic).
- Bundleware and Infrequently-Used are heuristics (registry timestamps, UserAssist) — labelled as such in the UI.
- Non-admin mode: HKLM leftovers, services, restore points and Program Files cleanup fail with "access denied" → use *Restart as administrator*.
- Chrome may show "settings were reset" once after an extension is removed externally (integrity MAC removed along with the entry).
- winget table parsing assumes ASCII-width columns; names with wide (CJK) characters may mis-parse (row is skipped).

## Next steps

1. Krishna runs `Join-Evict.cmd`, launches `Evict.exe`, reports (or lets Claude screenshot via computer-use) any crash/visual issue → hot-fix build 1.0.1.
2. Build 2: Easy Uninstall widget, Explorer context menu, ComboBox/ScrollBar dark-theme polish, per-row expander details like IObit.
3. Build 3: Inno Setup installer with optional context-menu integration, auto-update check, code-signing discussion.
