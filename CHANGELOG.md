# Changelog

## 1.3.0 — Build 4 (16 Sep 2026)

- **Text size 80–300 %** (default now **120 %**; existing settings that still had the old 100 % default are raised once).
  Ctrl + / − step 10 % up to 150 %, then 25 %; Ctrl 0 returns to 120 %. At large sizes the window scrolls instead of squeezing pages.
- **Notification-area (tray) icon** with menu: open, Health scan, widget, record an installation, detection on/off, exit.
  Settings: show icon, close to tray, minimize to tray, **start with Windows** (`Evict.exe --tray`).
- **Installer detection + automatic Install Monitor**: while Evict (or its tray icon) runs, new installer processes are
  recognised (setup/install file names, Inno Setup / NSIS temp stubs, `msiexec /i`, installer descriptions, Downloads folder);
  a notification offers to record the installation – or recording starts automatically (setting Off / Ask / Automatic).
  Recording waits for the installer's whole process tree and msiexec, then saves an Install Monitor log and notifies.
- **Scheduled Software Health scan**: daily or weekly through Task Scheduler (`Evict.exe --scheduled-scan`, per user, no
  admin); result arrives as a notification; missed runs (PC off) are caught up at the next start.
- **System Cleanup tool**: orphaned Windows Installer packages/patches (`C:\Windows\Installer`, cross-checked against every
  product's `LocalPackage`; moved to `ProgramData\Evict\InstallerCacheBackup` rather than deleted), data folders of removed
  Store apps, Windows Update download cache (services stopped/started), Delivery Optimization cache, Windows/user Temp
  (older than 24 h), Windows Error Reporting files, crash dumps, Windows.old (measured; opens Storage settings).
- **Software Updater runs updates in parallel** (1–6 at a time, default 3; overall progress bar). Windows Installer
  packages can only install one at a time, so "another installation is in progress" results are retried automatically.
- Installer: optional "Start Evict with Windows" task; uninstall removes the scheduled task and autostart entry.
- Documentation no longer references other products or companies.

## 1.2.0 — Build 3 (16 Sep 2026)

- **Installer**: `Evict-Setup-1.2.0.exe` built with Inno Setup on every CI run — per-user by default (no UAC) or
  all-users; optional desktop icon, Explorer context menu and *Send to* entry; Start-menu shortcuts for the app,
  the Easy Uninstall widget and a Software Health scan; clean uninstall (offers to delete `%LocalAppData%\Evict`)
- **Update check**: at start-up (setting) and via *Settings → Updates → Check now*, against GitHub Releases;
  blue banner + dialog with release notes; downloads the matching file, verifies its SHA-256, then either runs the
  new installer silently (installed copies) or swaps `Evict.exe` in place and restarts (portable copies);
  "Skip this version"; `--updated` shows a one-time "Evict was updated" notice
- **Code signing (optional)**: CI signs `Evict.exe` and the installer when the `SIGN_PFX_BASE64` /
  `SIGN_PFX_PASSWORD` secrets exist (see README → *Code signing*)
- Context-menu setting recognises an entry registered for all users by Setup
- Text size range is 90–140 %

## 1.1.0 — Build 2 (16 Sep 2026)

- Software Health dashboard (new home page) with one-click fixes
- Easy Uninstall floating widget (drag a target onto any window)
- Explorer right-click "Uninstall with Evict" + command line (`--uninstall-file`, `--uninstall`, `--scan`)
- Startup Apps manager
- Residual cleaner: leftovers of programs that were already uninstalled
- Known-bundleware database on top of the timing heuristic
- Text size / zoom setting
- Dark-theme polish: ComboBox, ScrollBar, TabControl, menus, RadioButton
- GitHub Actions CI: build, test, publish, release on tags

## 1.0.0 — Build 1 (16 Sep 2026)

- Programs list with All / Recently Installed / Large / Infrequently Used / Bundleware / Broken Entries tabs
- Uninstall wizard: restore point → uninstaller (interactive or silent) → Powerful Scan → review → clean → summary
- Batch uninstall, Force Uninstall
- Windows Apps (Appx) with bloatware flags and de-provisioning
- Browser Extensions (Chrome, Edge, Brave, Vivaldi, Opera, Firefox)
- Software Updater (winget)
- Install Monitor with "Uninstall using this log"
- Tools: File Shredder, Windows Updates, Restore Point
- History with CSV export, Settings with Light/Dark theme
