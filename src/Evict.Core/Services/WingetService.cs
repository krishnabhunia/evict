using System.Text;
using Evict.Core.Models;

namespace Evict.Core.Services;

/// <summary>
/// Software Updater backed by the Windows Package Manager (winget). Lists upgradable packages by
/// parsing the fixed-width table "winget upgrade" prints, and upgrades them one at a time.
/// </summary>
public sealed class WingetService
{
    public static string? FindWinget()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var alias = Path.Combine(local, "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(alias)) return alias;
            // Elevated processes sometimes lack the per-user alias on PATH – look inside WindowsApps.
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var winApps = Path.Combine(pf, "WindowsApps");
            if (Directory.Exists(winApps))
            {
                var dir = Directory.EnumerateDirectories(winApps, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe").OrderByDescending(d => d).FirstOrDefault();
                if (dir != null && File.Exists(Path.Combine(dir, "winget.exe"))) return Path.Combine(dir, "winget.exe");
            }
        }
        catch { /* ignore */ }
        return null;
    }

    public static bool IsAvailable => FindWinget() != null;

    public async Task<(List<UpgradablePackage> Packages, string? Error)> GetUpgradesAsync(bool includeUnknown, CancellationToken ct, Action<string>? onLine = null)
    {
        var winget = FindWinget();
        if (winget is null) return (new(), "winget (App Installer) was not found. Install it from the Microsoft Store to use Software Updater.");
        var args = "upgrade --accept-source-agreements --disable-interactivity" + (includeUnknown ? " --include-unknown" : "");
        var res = await ProcessRunner.RunCapturedAsync(winget, args, ct, TimeSpan.FromMinutes(4), onLine, Encoding.UTF8).ConfigureAwait(false);
        if (res.TimedOut) return (new(), "winget did not respond in time.");
        var list = ParseUpgradeTable(res.StdOut);
        string? err = null;
        if (list.Count == 0 && res.ExitCode != 0 && !res.StdOut.Contains("No installed package", StringComparison.OrdinalIgnoreCase))
            err = $"winget exited with code {res.ExitCode}: {FirstUseful(res.StdOut + res.StdErr)}";
        return (list, err);
    }

    private static string FirstUseful(string s) =>
        s.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.All(c => c is '-' or '\\' or '|' or '/' or ' ')) ?? "";

    /// <summary>
    /// Parses winget's table. Columns are located from the header line so localisation and
    /// column width changes do not break us: "Name  Id  Version  Available  Source".
    /// </summary>
    public static List<UpgradablePackage> ParseUpgradeTable(string output)
    {
        var result = new List<UpgradablePackage>();
        if (string.IsNullOrWhiteSpace(output)) return result;
        var lines = output.Replace("\r", "").Split('\n');

        int headerIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Contains(" Id ") && l.Contains("Version") && (l.Contains("Available") || l.Contains("Verfügbar") || l.Contains("Disponible")))
            {
                headerIdx = i; break;
            }
        }
        if (headerIdx < 0 || headerIdx + 1 >= lines.Length) return result;

        var header = lines[headerIdx];
        int idCol = header.IndexOf(" Id ", StringComparison.Ordinal) + 1;
        int verCol = header.IndexOf("Version", idCol, StringComparison.Ordinal);
        int availCol = IndexOfAny(header, verCol + 7, "Available", "Verfügbar", "Disponible");
        int srcCol = IndexOfAny(header, availCol + 5, "Source", "Quelle", "Fuente", "Origine");
        if (idCol <= 0 || verCol <= idCol || availCol <= verCol) return result;

        for (int i = headerIdx + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;
            if (line.TrimStart().StartsWith("---") || line.Trim().All(c => c == '-')) continue;   // separator
            if (line.Contains("upgrades available", StringComparison.OrdinalIgnoreCase) || line.Contains("upgrade available", StringComparison.OrdinalIgnoreCase)) break;
            if (line.StartsWith("The following packages", StringComparison.OrdinalIgnoreCase)) break; // pinned / unknown section
            if (line.Length < verCol) continue;

            string name = Slice(line, 0, idCol);
            string id = Slice(line, idCol, verCol);
            string version = Slice(line, verCol, availCol);
            string available = srcCol > availCol ? Slice(line, availCol, srcCol) : Slice(line, availCol, line.Length);
            string source = srcCol > availCol ? Slice(line, srcCol, line.Length) : "";

            if (id.Length == 0 || name.Length == 0) continue;
            if (id.Contains(' ') && !id.Contains('.')) continue; // misaligned wide-character row; skip rather than mis-parse
            result.Add(new UpgradablePackage { Name = name, Id = id, InstalledVersion = version, AvailableVersion = available, Source = source });
        }
        return result;
    }

    private static int IndexOfAny(string s, int start, params string[] needles)
    {
        if (start < 0 || start >= s.Length) return -1;
        foreach (var n in needles)
        {
            int i = s.IndexOf(n, start, StringComparison.Ordinal);
            if (i >= 0) return i;
        }
        return -1;
    }

    private static string Slice(string line, int start, int end)
    {
        if (start >= line.Length || start < 0) return "";
        end = Math.Min(end, line.Length);
        if (end <= start) return "";
        return line[start..end].Trim().TrimEnd('…');
    }

    public async Task<(bool Ok, string Message)> UpgradeAsync(UpgradablePackage pkg, CancellationToken ct, Action<string>? onLine = null)
    {
        var winget = FindWinget();
        if (winget is null) return (false, "winget not found.");
        var args = $"upgrade --id \"{pkg.Id}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
        if (!string.IsNullOrEmpty(pkg.Source)) args += $" --source {pkg.Source}";
        var res = await ProcessRunner.RunCapturedAsync(winget, args, ct, TimeSpan.FromMinutes(30), onLine, Encoding.UTF8).ConfigureAwait(false);
        var text = (res.StdOut + "\n" + res.StdErr);
        bool ok = res.ExitCode == 0 || text.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase);
        return (ok, ok ? "Updated." : $"winget exited with code {res.ExitCode}: {FirstUseful(text)}");
    }
}
