# Evict Uninstaller

A complete Windows uninstaller in the spirit of IObit Uninstaller — written in C# / .NET 8 / WPF,
delivered as a single portable `Evict.exe`.

| Module | What it does | Status (Build 1) |
|---|---|---|
| **Programs** | All installed programs (64-bit, 32-bit, per-user) with icons, size, install date, last-used; tabs for *All / Recently Installed / Large / Infrequently Used / Bundleware / Broken Entries*; search, sort, multi-select | ✅ |
| **Uninstall wizard** | Optional System Restore point → runs the program's own uninstaller (interactive or silent) → **Powerful Scan** for leftovers → review with confidence rating → delete (Recycle Bin optional) → summary | ✅ |
| **Powerful Scan** | Finds leftover folders/files (Program Files, ProgramData, AppData of every user), Start-menu / desktop shortcuts, registry keys & values (vendor keys, App Paths, Run entries, AppCompat, MuiCache…), services and scheduled tasks | ✅ |
| **Batch uninstall** | Queue several programs; one review step for all leftovers | ✅ |
| **Force Uninstall** | For broken/missing uninstallers: pick a program or point at a folder/exe, kill its processes, remove everything it owns | ✅ |
| **Windows Apps** | Store / UWP / MSIX packages incl. pre-installed bloatware; remove per user or all users, de-provision | ✅ |
| **Browser Extensions** | Chrome, Edge, Brave, Vivaldi, Opera (all profiles) + Firefox; flags broad permissions; removes with browser closed | ✅ |
| **Software Updater** | Outdated programs via `winget upgrade`; one-click update with live output | ✅ |
| **Install Monitor** | Records files, folders and registry keys created by an installer (FileSystemWatcher + registry snapshot diff); later "Uninstall using this log" | ✅ |
| **Tools** | File Shredder (1 / 3 / 7 passes), Windows Updates uninstall (wusa), Create Restore Point, shortcuts to Windows tools | ✅ |
| **History** | Every operation with leftovers found/removed and bytes reclaimed; CSV export; rescan leftovers | ✅ |
| **Settings** | Light/Dark theme, defaults for the wizard, thresholds for the tabs | ✅ |
| Easy Uninstall drag-target widget, Explorer context-menu, installer (Inno Setup) | Planned for Build 2 / 3 | ⏳ |

## Running it

`Evict.exe` is self-contained (no .NET install needed). Double-click to run. It starts **without** a UAC
prompt; use *Restart as administrator* (sidebar or the yellow banner) to unlock machine-wide operations
(Program Files leftovers, services, restore points, all-user Store apps, Windows updates).

Settings, history, install logs and the diagnostic log live in `%LocalAppData%\Evict`.

## Building from source

Requirements: .NET 8 SDK (Windows, Linux or macOS — the project sets `EnableWindowsTargeting`).

```bash
dotnet test tests/Evict.Core.Tests            # 98 unit tests for the pure logic
dotnet publish src/Evict.App -c Release -o publish   # → publish/Evict.exe (single file, win-x64)
```

or run `build/publish.ps1` (Windows) / `build/publish.sh` (Linux/macOS).

## Project layout

```
Evict.sln
Directory.Build.props        shared build settings, product/version info
src/Evict.Core/              platform logic, no UI (net8.0, Windows-only APIs)
  Models/                    InstalledProgram, LeftoverItem, AppxPackageInfo, …
  Services/                  InstalledProgramsService, LeftoverScanner, LeftoverCleaner, UninstallRunner,
                             UninstallOrchestrator, RestorePointService, AppxService, BrowserExtensionService,
                             WingetService, WindowsUpdatesService, InstallMonitorService, ForceUninstallService,
                             FileShredder, UserAssistReader, BundlewareDetector, SettingsStore, HistoryStore
  Util/                      NameNormalizer (matching heuristics), UninstallCommandParser, PathUtil, …
src/Evict.App/               WPF UI (net8.0-windows), MVVM with CommunityToolkit.Mvvm
  Themes/                    Light.xaml / Dark.xaml brush sets (same keys)
  Styles/                    Controls.xaml (buttons, text boxes, checkbox, progress…), DataGrid.xaml, Converters.xaml
  ViewModels/                one per page + wizard / force-uninstall / shredder / updates
  Views/                     XAML pages and dialog windows
tests/Evict.Core.Tests/      xunit tests (run on any OS)
build/                       publish scripts, xaml_check.py (static XAML sanity checks)
```

## Safety design

* The leftover scanner never proposes protected locations (Windows, Program Files root, user profile roots,
  Documents/Pictures…), never a folder that *contains* the install folder, and rates every item
  **Safe / Likely / Review**. *Review* items are unchecked by default.
* Registry deletion refuses hive roots and well-known containers (`Microsoft`, `Classes`, `Uninstall`, …).
* Files go to the Recycle Bin by default; locked files can be scheduled for deletion at reboot (admin).
* Browser preference files are backed up (`*.evict-backup`) before an extension entry is removed.
