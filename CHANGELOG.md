# Changelog

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
