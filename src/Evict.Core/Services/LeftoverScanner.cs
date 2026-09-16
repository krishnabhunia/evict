using System.Diagnostics;
using Evict.Core.Models;
using Evict.Core.Util;
using Microsoft.Win32;

namespace Evict.Core.Services;

/// <summary>
/// "Powerful Scan": finds files, folders, shortcuts, registry keys/values, services and scheduled
/// tasks that belong to a program – used after its own uninstaller ran, and by Force Uninstall.
/// Conservative by design: only proposes items that match the program's name / publisher / install
/// folder, never protected system locations.
/// </summary>
public sealed class LeftoverScanner
{
    private const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    // ───────────────────────────── fingerprint ─────────────────────────────

    public static ProgramFingerprint Fingerprint(InstalledProgram p)
    {
        var exeNames = new List<string>();
        void AddExe(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string name;
            try { name = Path.GetFileName(path); } catch { return; }
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !exeNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                exeNames.Add(name);
        }

        AddExe(p.PrimaryExecutable);
        var (iconPath, _) = PathUtil.SplitIconPath(p.DisplayIcon);
        AddExe(iconPath);
        var uninstCmd = UninstallCommandParser.Parse(p.UninstallString);
        string? uninstallExe = uninstCmd != null && !UninstallCommandParser.IsMsiExec(uninstCmd) ? uninstCmd.FileName : null;

        if (!string.IsNullOrEmpty(p.InstallLocation))
        {
            try
            {
                if (Directory.Exists(p.InstallLocation))
                {
                    foreach (var f in Directory.EnumerateFiles(p.InstallLocation, "*.exe", new EnumerationOptions { IgnoreInaccessible = true }).Take(60))
                        AddExe(f);
                }
            }
            catch { /* ignore */ }
        }

        var keys = NameNormalizer.CandidateKeys(p.DisplayName);
        AddInstallLeafKey(keys, p.InstallLocation);

        return new ProgramFingerprint
        {
            DisplayName = p.DisplayName,
            Publisher = p.Publisher,
            InstallLocation = p.InstallLocation,
            KeyName = p.KeyName,
            MsiProductCode = p.MsiProductCode,
            UninstallExePath = uninstallExe,
            PrimaryExecutable = p.PrimaryExecutable,
            IconPath = iconPath,
            RegistryPath = p.RegistryPath,
            Scope = p.Scope,
            ExecutableNames = exeNames,
            NameKeys = keys,
            PublisherToken = NameNormalizer.PublisherKey(p.Publisher),
        };
    }

    /// <summary>The install folder's leaf name is a strong extra key ("C:\Program Files\VideoLAN\VLC" → "vlc").</summary>
    private static void AddInstallLeafKey(List<CandidateKey> keys, string? installLocation)
    {
        if (string.IsNullOrEmpty(installLocation)) return;
        string leaf;
        try { leaf = Path.GetFileName(PathUtil.NormalizeForCompare(installLocation)); } catch { return; }
        var leafKey = NameNormalizer.ToKey(leaf);
        if (leafKey.Length >= 3 && !NameNormalizer.ProtectedNames.Contains(leaf) && !NameNormalizer.StopWords.Contains(leafKey)
            && !keys.Any(k => k.Key == leafKey))
            keys.Add(new CandidateKey(leafKey, LeftoverConfidence.Medium));
    }

    /// <summary>Fingerprint for Force Uninstall of an arbitrary folder / executable.</summary>
    public static ProgramFingerprint FingerprintFromPath(string path, string? displayName = null)
    {
        string? folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        var name = displayName ?? Path.GetFileNameWithoutExtension(Directory.Exists(path) ? path.TrimEnd('\\') : path);
        var exes = new List<string>();
        if (File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exes.Add(Path.GetFileName(path));
        if (folder != null && Directory.Exists(folder))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(folder, "*.exe", new EnumerationOptions { IgnoreInaccessible = true }).Take(60))
                {
                    var n = Path.GetFileName(f);
                    if (!exes.Contains(n, StringComparer.OrdinalIgnoreCase)) exes.Add(n);
                }
            }
            catch { /* ignore */ }
        }
        var keys = NameNormalizer.CandidateKeys(name);
        AddInstallLeafKey(keys, folder);
        return new ProgramFingerprint
        {
            DisplayName = name,
            InstallLocation = folder,
            PrimaryExecutable = File.Exists(path) ? path : null,
            ExecutableNames = exes,
            NameKeys = keys,
        };
    }

    // ───────────────────────────── scan ─────────────────────────────

    public Task<LeftoverScanResult> ScanAsync(ProgramFingerprint fp, LeftoverScanOptions options, IProgress<ProgressReport>? progress, CancellationToken ct) =>
        Task.Run(() => Scan(fp, options, progress, ct), ct);

    public LeftoverScanResult Scan(ProgramFingerprint fp, LeftoverScanOptions options, IProgress<ProgressReport>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = new LeftoverScanResult();
        var ctx = new ScanContext(fp, options, result, ct);

        try
        {
            progress?.Report(new ProgressReport("Checking install folder…", 5));
            ScanInstallLocation(ctx);

            progress?.Report(new ProgressReport("Scanning Program Files and application data…", 15));
            ScanFileSystemRoots(ctx);

            if (options.ScanShortcuts)
            {
                progress?.Report(new ProgressReport("Scanning shortcuts…", 45));
                ScanShortcuts(ctx);
            }

            if (options.ScanRegistry)
            {
                progress?.Report(new ProgressReport("Scanning registry…", 60));
                ScanRegistry(ctx);
            }

            if (options.ScanServices)
            {
                progress?.Report(new ProgressReport("Scanning services…", 80));
                ScanServices(ctx);
            }

            if (options.ScanScheduledTasks)
            {
                progress?.Report(new ProgressReport("Scanning scheduled tasks…", 90));
                ScanScheduledTasks(ctx);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            result.Warnings.Add("Scan aborted early: " + ex.Message);
            Log.Error("Leftover scan failed", ex);
        }

        // Drop low-confidence items when asked, and children of already-included folders.
        var items = result.Items
            .Where(i => options.IncludeLowConfidence || i.Confidence != LeftoverConfidence.Low)
            .OrderBy(i => i.Kind).ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        result.Items.Clear();
        result.Items.AddRange(items);
        result.Elapsed = sw.Elapsed;
        progress?.Report(new ProgressReport($"Found {result.Items.Count} leftover item(s).", 100));
        return result;
    }

    // ───────────────────────────── context ─────────────────────────────

    private sealed class ScanContext
    {
        public ProgramFingerprint Fp { get; }
        public LeftoverScanOptions Options { get; }
        public LeftoverScanResult Result { get; }
        public CancellationToken Ct { get; }
        public HashSet<string> SeenPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> IncludedFolders { get; } = new();
        public IReadOnlyList<CandidateKey> Keys => Fp.NameKeys;
        public string PublisherKey => Fp.PublisherToken ?? "";
        public bool HasPublisher => PublisherKey.Length >= 3 && !NameNormalizer.StopWords.Contains(PublisherKey);

        public ScanContext(ProgramFingerprint fp, LeftoverScanOptions options, LeftoverScanResult result, CancellationToken ct)
        {
            Fp = fp; Options = options; Result = result; Ct = ct;
        }

        public bool AddFolder(string path, LeftoverConfidence confidence, string detail, bool isInstallLocation = false)
        {
            var norm = PathUtil.NormalizeForCompare(path);
            if (!SeenPaths.Add(norm)) return false;
            if (IncludedFolders.Any(f => PathUtil.IsUnder(norm, f))) return false;

            // Never propose a folder that *contains* the install folder (e.g. "…\Python" when the program
            // lived in "…\Python\Python312") – it would take sibling products with it.
            if (!isInstallLocation && !string.IsNullOrEmpty(Fp.InstallLocation)
                && PathUtil.IsUnder(Fp.InstallLocation, norm) && !norm.Equals(PathUtil.NormalizeForCompare(Fp.InstallLocation), StringComparison.OrdinalIgnoreCase))
                return false;

            if (PathUtil.IsProtectedRoot(norm, checkProtectedNames: !isInstallLocation))
            {
                Result.Warnings.Add($"Skipped protected location: {norm}");
                return false;
            }
            long size = DirectorySizeCalculator.Measure(norm, Ct) ?? 0;
            IncludedFolders.Add(norm);
            Result.Items.Add(new LeftoverItem
            {
                Kind = LeftoverKind.Folder,
                Path = norm,
                SizeBytes = size,
                Detail = detail,
                Confidence = confidence,
                ProgramName = Fp.DisplayName,
            });
            return true;
        }

        public void AddFile(string path, LeftoverKind kind, LeftoverConfidence confidence, string detail)
        {
            var norm = PathUtil.NormalizeForCompare(path);
            if (!SeenPaths.Add(norm)) return;
            if (IncludedFolders.Any(f => PathUtil.IsUnder(norm, f))) return;
            long size = 0;
            try { size = new FileInfo(norm).Length; } catch { /* ignore */ }
            Result.Items.Add(new LeftoverItem
            {
                Kind = kind, Path = norm, SizeBytes = size, Detail = detail, Confidence = confidence, ProgramName = Fp.DisplayName,
            });
        }

        public void AddRegistryKey(RegistryHive hive, RegistryView view, string subKey, LeftoverConfidence confidence, string detail)
        {
            var display = RegistryPaths.Display(hive, view, subKey);
            if (!SeenPaths.Add("REG:" + display)) return;
            Result.Items.Add(new LeftoverItem
            {
                Kind = LeftoverKind.RegistryKey, Path = display, Detail = detail, Confidence = confidence,
                Hive = hive, RegView = view, SubKey = subKey, ProgramName = Fp.DisplayName,
            });
        }

        public void AddRegistryValue(RegistryHive hive, RegistryView view, string subKey, string valueName, LeftoverKind kind, LeftoverConfidence confidence, string detail)
        {
            var display = RegistryPaths.Display(hive, view, subKey, valueName);
            if (!SeenPaths.Add("REG:" + display)) return;
            Result.Items.Add(new LeftoverItem
            {
                Kind = kind, Path = display, Detail = detail, Confidence = confidence,
                Hive = hive, RegView = view, SubKey = subKey, ValueName = valueName, ProgramName = Fp.DisplayName,
            });
        }
    }

    // ───────────────────────────── file system ─────────────────────────────

    private static void ScanInstallLocation(ScanContext ctx)
    {
        var loc = ctx.Fp.InstallLocation;
        if (string.IsNullOrEmpty(loc)) return;
        try
        {
            if (Directory.Exists(loc))
            {
                if (PathUtil.IsProtectedRoot(loc, checkProtectedNames: false))
                    ctx.Result.Warnings.Add($"Install location {loc} is a protected folder and was not added.");
                else
                    ctx.AddFolder(loc, LeftoverConfidence.High, "Install folder still exists", isInstallLocation: true);
            }
        }
        catch { /* ignore */ }
    }

    private static IEnumerable<string> UserProfileRoots(bool allUsers)
    {
        var mine = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>();
        if (!string.IsNullOrEmpty(mine)) roots.Add(mine);

        if (allUsers && ElevationHelper.IsElevated)
        {
            try
            {
                var usersDir = Path.GetDirectoryName(mine);
                if (usersDir != null && Directory.Exists(usersDir))
                {
                    foreach (var d in Directory.EnumerateDirectories(usersDir))
                    {
                        var leaf = Path.GetFileName(d);
                        if (leaf.Equals("Public", StringComparison.OrdinalIgnoreCase) || leaf.StartsWith("Default", StringComparison.OrdinalIgnoreCase)
                            || leaf.Equals("All Users", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!roots.Contains(d, StringComparer.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(d, "AppData"))) roots.Add(d);
                    }
                }
            }
            catch { /* ignore */ }
        }
        return roots;
    }

    private static IEnumerable<string> FileSystemRoots(ScanContext ctx)
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        // User data folders (Documents, Pictures…) are deliberately NOT scanned – a program called
        // "Photos" must never cause the user's photo folder to be proposed for deletion.
        foreach (var profile in UserProfileRoots(ctx.Options.ScanAllUserProfiles))
        {
            yield return Path.Combine(profile, "AppData", "Local");
            yield return Path.Combine(profile, "AppData", "Local", "Programs");
            yield return Path.Combine(profile, "AppData", "Roaming");
            yield return Path.Combine(profile, "AppData", "LocalLow");
        }
    }

    private static void ScanFileSystemRoots(ScanContext ctx)
    {
        var enumOpts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var root in FileSystemRoots(ctx).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ctx.Ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> level1;
            try { level1 = Directory.EnumerateDirectories(root, "*", enumOpts).ToList(); } catch { continue; }

            foreach (var dir in level1)
            {
                ctx.Ct.ThrowIfCancellationRequested();
                var leaf = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(ctx.Fp.InstallLocation) && PathUtil.IsUnder(dir, ctx.Fp.InstallLocation)) continue;

                var match = NameNormalizer.Match(leaf, ctx.Keys);
                if (match is { } m)
                {
                    ctx.AddFolder(dir, m, $"Folder name matches \"{ctx.Fp.DisplayName}\"");
                    continue;
                }

                // Publisher folder → look one level deeper for the product.
                bool isPublisherFolder = ctx.HasPublisher && NameNormalizer.ToKey(leaf) == ctx.PublisherKey;
                bool isGenericContainer = leaf.Equals("Programs", StringComparison.OrdinalIgnoreCase) || leaf.Equals("Packages", StringComparison.OrdinalIgnoreCase);
                if (isPublisherFolder || isGenericContainer)
                {
                    IEnumerable<string> level2;
                    try { level2 = Directory.EnumerateDirectories(dir, "*", enumOpts).ToList(); } catch { continue; }
                    foreach (var sub in level2)
                    {
                        var subLeaf = Path.GetFileName(sub);
                        var sm = NameNormalizer.Match(subLeaf, ctx.Keys, allowFuzzy: isPublisherFolder);
                        if (sm is { } smv)
                        {
                            var conf = isPublisherFolder && smv == LeftoverConfidence.Low ? LeftoverConfidence.Medium : smv;
                            ctx.AddFolder(sub, conf, isPublisherFolder ? $"Under publisher folder \"{leaf}\"" : $"Folder name matches \"{ctx.Fp.DisplayName}\"");
                        }
                    }
                }
            }
        }
    }

    // ───────────────────────────── shortcuts ─────────────────────────────

    private static IEnumerable<string> ShortcutRoots(ScanContext ctx)
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        foreach (var profile in UserProfileRoots(ctx.Options.ScanAllUserProfiles))
        {
            yield return Path.Combine(profile, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu");
            yield return Path.Combine(profile, "Desktop");
            yield return Path.Combine(profile, "AppData", "Roaming", "Microsoft", "Internet Explorer", "Quick Launch");
        }
    }

    private static void ScanShortcuts(ScanContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.Fp.InstallLocation) && ctx.Fp.ExecutableNames.All(ShortcutInspector.IsGenericExeName)) return;

        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 6 };
        var startMenuFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in ShortcutRoots(ctx).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ctx.Ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> links;
            try { links = Directory.EnumerateFiles(root, "*.lnk", opts).ToList(); } catch { continue; }

            foreach (var lnk in links)
            {
                ctx.Ct.ThrowIfCancellationRequested();
                if (ShortcutInspector.References(lnk, ctx.Fp.InstallLocation, ctx.Fp.ExecutableNames))
                {
                    ctx.AddFile(lnk, LeftoverKind.Shortcut, LeftoverConfidence.High, "Shortcut points into the program folder");
                    var parent = Path.GetDirectoryName(lnk);
                    if (parent != null && root.Contains("Start Menu", StringComparison.OrdinalIgnoreCase) && !parent.Equals(root, StringComparison.OrdinalIgnoreCase))
                        startMenuFolders.Add(parent);
                }
            }

            // Start-menu folders named like the program (may contain a website link, a help file…)
            if (root.Contains("Start Menu", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(Path.Combine(root, "Programs"), "*", new EnumerationOptions { IgnoreInaccessible = true }))
                    {
                        var m = NameNormalizer.Match(Path.GetFileName(dir), ctx.Keys, allowFuzzy: false);
                        if (m is { } mv) ctx.AddFolder(dir, mv, "Start menu folder");
                    }
                }
                catch { /* ignore */ }
            }
        }

        // Folders that only held shortcuts we flagged → propose the folder too when it would become empty.
        foreach (var folder in startMenuFolders)
        {
            try
            {
                var remaining = Directory.EnumerateFileSystemEntries(folder)
                    .Count(e => !ctx.SeenPaths.Contains(PathUtil.NormalizeForCompare(e)));
                if (remaining == 0 && PathUtil.Depth(folder) >= 6)
                    ctx.AddFolder(folder, LeftoverConfidence.High, "Start menu folder would be empty");
            }
            catch { /* ignore */ }
        }
    }

    // ───────────────────────────── registry ─────────────────────────────

    private static IEnumerable<(RegistryHive Hive, RegistryView View)> SoftwareRoots()
    {
        yield return (RegistryHive.LocalMachine, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, RegistryView.Registry32);
        yield return (RegistryHive.CurrentUser, RegistryView.Registry64);
    }

    private static readonly HashSet<string> SkipTopLevelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Classes", "Microsoft", "Wow6432Node", "Policies", "Clients", "RegisteredApplications", "ODBC", "WOW6432Node",
        "Intel", "Realtek", "NVIDIA Corporation", "AMD", "Dell", "HP", "Lenovo", "Synaptics", "Khronos", "Partner",
    };

    private static void ScanRegistry(ScanContext ctx)
    {
        // 1. The program's own uninstall entry, if the uninstaller left it behind.
        if (!string.IsNullOrEmpty(ctx.Fp.KeyName) && ctx.Fp.Scope is { } scope)
        {
            var hive = scope == RegistryScope.User ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
            var view = scope == RegistryScope.Machine32 ? RegistryView.Registry32 : RegistryView.Registry64;
            if (KeyExists(hive, view, RegistryPaths.Join(UninstallSubKey, ctx.Fp.KeyName)))
                ctx.AddRegistryKey(hive, view, RegistryPaths.Join(UninstallSubKey, ctx.Fp.KeyName), LeftoverConfidence.High, "Orphaned Programs & Features entry");
        }

        // 2. Vendor / product keys under HKLM\SOFTWARE (64 + 32) and HKCU\Software.
        foreach (var (hive, view) in SoftwareRoots())
        {
            ctx.Ct.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var software = baseKey.OpenSubKey("SOFTWARE");
                if (software is null) continue;

                foreach (var name in software.GetSubKeyNames())
                {
                    if (SkipTopLevelKeys.Contains(name)) continue;
                    var m = NameNormalizer.Match(name, ctx.Keys);
                    if (m is { } mv)
                    {
                        ctx.AddRegistryKey(hive, view, "SOFTWARE\\" + name, mv, "Key name matches the program");
                        continue;
                    }
                    if (ctx.HasPublisher && NameNormalizer.ToKey(name) == ctx.PublisherKey)
                    {
                        using var pub = software.OpenSubKey(name);
                        if (pub is null) continue;
                        var subNames = pub.GetSubKeyNames();
                        foreach (var sub in subNames)
                        {
                            var sm = NameNormalizer.Match(sub, ctx.Keys);
                            if (sm is { } smv)
                            {
                                var conf = smv == LeftoverConfidence.Low ? LeftoverConfidence.Medium : smv;
                                ctx.AddRegistryKey(hive, view, $"SOFTWARE\\{name}\\{sub}", conf, $"Under publisher key \"{name}\"");
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { ctx.Result.Warnings.Add($"Registry {hive}/{view}: {ex.Message}"); }
        }

        var loc = ctx.Fp.InstallLocation;
        var exeNames = ctx.Fp.ExecutableNames.Where(e => !ShortcutInspector.IsGenericExeName(e)).ToList();

        // 3. App Paths, Run / RunOnce, Applications, AppCompat stores, MuiCache, Installer\Folders.
        foreach (var (hive, view) in SoftwareRoots())
        {
            ctx.Ct.ThrowIfCancellationRequested();
            ScanAppPaths(ctx, hive, view, loc, exeNames);
            ScanRunKeys(ctx, hive, view, loc);
            ScanApplicationsKeys(ctx, hive, view, exeNames);
        }
        ScanValuesNamedByPath(ctx, RegistryHive.CurrentUser, RegistryView.Registry64,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store", loc, LeftoverConfidence.Medium, "Compatibility Assistant record");
        ScanValuesNamedByPath(ctx, RegistryHive.CurrentUser, RegistryView.Registry64,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", loc, LeftoverConfidence.Medium, "Compatibility setting");
        ScanValuesNamedByPath(ctx, RegistryHive.LocalMachine, RegistryView.Registry64,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", loc, LeftoverConfidence.Medium, "Compatibility setting");
        ScanValuesNamedByPath(ctx, RegistryHive.CurrentUser, RegistryView.Registry64,
            @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", loc, LeftoverConfidence.Low, "MUI cache entry");
        ScanValuesNamedByPath(ctx, RegistryHive.LocalMachine, RegistryView.Registry64,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders", loc, LeftoverConfidence.Low, "Windows Installer folder record");
        ScanValuesNamedByPath(ctx, RegistryHive.CurrentUser, RegistryView.Registry64,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched", loc, LeftoverConfidence.Low, "Taskbar usage record");
    }

    private static bool KeyExists(RegistryHive hive, RegistryView view, string subKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var k = baseKey.OpenSubKey(subKey);
            return k is not null;
        }
        catch { return false; }
    }

    private static void ScanAppPaths(ScanContext ctx, RegistryHive hive, RegistryView view, string? loc, List<string> exeNames)
    {
        const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(appPaths);
            if (root is null) return;
            foreach (var name in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(name);
                var target = k?.GetValue("") as string;
                var path = k?.GetValue("Path") as string;
                bool byLoc = !string.IsNullOrEmpty(loc) && (PathUtil.IsUnder(PathUtil.Clean(target) ?? "", loc) || PathUtil.IsUnder(PathUtil.Clean(path) ?? "", loc));
                bool byExe = exeNames.Any(e => e.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (byLoc || byExe)
                    ctx.AddRegistryKey(hive, view, RegistryPaths.Join(appPaths, name), byLoc ? LeftoverConfidence.High : LeftoverConfidence.Medium, "App Paths registration");
            }
        }
        catch { /* ignore */ }
    }

    private static void ScanRunKeys(ScanContext ctx, RegistryHive hive, RegistryView view, string? loc)
    {
        if (string.IsNullOrEmpty(loc)) return;
        foreach (var runKey in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce" })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var k = baseKey.OpenSubKey(runKey);
                if (k is null) continue;
                foreach (var valueName in k.GetValueNames())
                {
                    var cmd = k.GetValue(valueName) as string;
                    if (string.IsNullOrEmpty(cmd)) continue;
                    var parsed = UninstallCommandParser.Parse(cmd);
                    if (parsed != null && PathUtil.IsUnder(parsed.FileName, loc))
                    {
                        ctx.AddRegistryValue(hive, view, runKey, valueName, LeftoverKind.StartupEntry, LeftoverConfidence.High, $"Startup entry: {cmd}");
                        // Matching StartupApproved record
                        var approved = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + (runKey.EndsWith("RunOnce") ? "RunOnce" : (view == RegistryView.Registry32 ? "Run32" : "Run"));
                        if (ValueExists(hive, RegistryView.Registry64, approved, valueName))
                            ctx.AddRegistryValue(hive, RegistryView.Registry64, approved, valueName, LeftoverKind.RegistryValue, LeftoverConfidence.High, "Startup approval record");
                    }
                }
            }
            catch { /* ignore */ }
        }
    }

    private static bool ValueExists(RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var k = baseKey.OpenSubKey(subKey);
            return k?.GetValue(valueName) is not null;
        }
        catch { return false; }
    }

    private static void ScanApplicationsKeys(ScanContext ctx, RegistryHive hive, RegistryView view, List<string> exeNames)
    {
        if (exeNames.Count == 0) return;
        const string apps = @"SOFTWARE\Classes\Applications";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(apps);
            if (root is null) return;
            var names = root.GetSubKeyNames();
            foreach (var exe in exeNames)
            {
                if (names.Contains(exe, StringComparer.OrdinalIgnoreCase))
                    ctx.AddRegistryKey(hive, view, RegistryPaths.Join(apps, exe), LeftoverConfidence.Medium, "File association / Open-with record");
            }
        }
        catch { /* ignore */ }
    }

    private static void ScanValuesNamedByPath(ScanContext ctx, RegistryHive hive, RegistryView view, string subKey, string? loc, LeftoverConfidence confidence, string detail)
    {
        if (string.IsNullOrEmpty(loc)) return;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var k = baseKey.OpenSubKey(subKey);
            if (k is null) return;
            foreach (var valueName in k.GetValueNames())
            {
                if (valueName.Length < 4) continue;
                var candidate = valueName;
                // MuiCache names look like "C:\path\app.exe.FriendlyAppName"
                int idx = candidate.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (idx > 0) candidate = candidate[..(idx + 4)];
                if (PathUtil.IsUnder(candidate, loc))
                    ctx.AddRegistryValue(hive, view, subKey, valueName, LeftoverKind.RegistryValue, confidence, detail);
            }
        }
        catch { /* ignore */ }
    }

    // ───────────────────────────── services ─────────────────────────────

    private static void ScanServices(ScanContext ctx)
    {
        var loc = ctx.Fp.InstallLocation;
        if (string.IsNullOrEmpty(loc)) return;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var services = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null) return;
            foreach (var name in services.GetSubKeyNames())
            {
                ctx.Ct.ThrowIfCancellationRequested();
                string? imagePath;
                try
                {
                    using var k = services.OpenSubKey(name);
                    imagePath = k?.GetValue("ImagePath") as string;
                }
                catch { continue; }
                if (string.IsNullOrEmpty(imagePath)) continue;
                var parsed = UninstallCommandParser.Parse(imagePath);
                var exe = parsed?.FileName ?? imagePath;
                if (PathUtil.IsUnder(exe, loc) || (parsed != null && parsed.Arguments.Contains(loc, StringComparison.OrdinalIgnoreCase) && exe.Contains("svchost", StringComparison.OrdinalIgnoreCase) == false))
                {
                    var display = RegistryPaths.Display(RegistryHive.LocalMachine, RegistryView.Registry64, @"SYSTEM\CurrentControlSet\Services\" + name);
                    if (!ctx.SeenPaths.Add("SVC:" + name)) continue;
                    ctx.Result.Items.Add(new LeftoverItem
                    {
                        Kind = LeftoverKind.Service,
                        Path = display,
                        Detail = $"Service \"{name}\" → {imagePath}",
                        Confidence = LeftoverConfidence.High,
                        ServiceName = name,
                        Hive = RegistryHive.LocalMachine,
                        RegView = RegistryView.Registry64,
                        SubKey = @"SYSTEM\CurrentControlSet\Services\" + name,
                        ProgramName = ctx.Fp.DisplayName,
                    });
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { ctx.Result.Warnings.Add("Services: " + ex.Message); }
    }

    // ───────────────────────────── scheduled tasks ─────────────────────────────

    private static void ScanScheduledTasks(ScanContext ctx)
    {
        var loc = ctx.Fp.InstallLocation;
        if (string.IsNullOrEmpty(loc)) return;
        var tasksRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
        if (!Directory.Exists(tasksRoot)) return;
        try
        {
            var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = 8 };
            foreach (var file in Directory.EnumerateFiles(tasksRoot, "*", opts))
            {
                ctx.Ct.ThrowIfCancellationRequested();
                string xml;
                try
                {
                    if (new FileInfo(file).Length > 1024 * 1024) continue;
                    xml = File.ReadAllText(file);
                }
                catch { continue; }
                if (!xml.Contains(loc, StringComparison.OrdinalIgnoreCase)) continue;

                var taskName = "\\" + Path.GetRelativePath(tasksRoot, file).Replace('/', '\\');
                if (!ctx.SeenPaths.Add("TASK:" + taskName)) continue;
                ctx.Result.Items.Add(new LeftoverItem
                {
                    Kind = LeftoverKind.ScheduledTask,
                    Path = "Task Scheduler" + taskName,
                    Detail = "Scheduled task runs a file inside the program folder",
                    Confidence = LeftoverConfidence.High,
                    TaskName = taskName,
                    ProgramName = ctx.Fp.DisplayName,
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { ctx.Result.Warnings.Add("Scheduled tasks: " + ex.Message); }
    }
}
