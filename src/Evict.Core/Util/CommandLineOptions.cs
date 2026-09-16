namespace Evict.Core.Util;

/// <summary>
/// Parsed command line. Supported:
///   --uninstall-file "C:\path\app.exe|shortcut.lnk"   uninstall the program that owns this file
///   --uninstall "Program Name"                          uninstall by (partial) display name
///   --scan                                              open Software Health and run a scan
///   --widget                                            show the Easy Uninstall widget
///   --page programs|apps|extensions|updater|monitor|tools|history|settings|health
///   --updated                                           (internal) first start after a self-update – show the "updated" notice
/// Pure logic – unit tested.
/// </summary>
public sealed class CommandLineOptions
{
    public string? UninstallFile { get; init; }
    public string? UninstallName { get; init; }
    public bool Scan { get; init; }
    public bool Widget { get; init; }
    public string? Page { get; init; }
    public bool Updated { get; init; }
    public List<string> Unknown { get; } = new();

    public bool IsEmpty => UninstallFile is null && UninstallName is null && !Scan && !Widget && Page is null && !Updated;

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        string? file = null, name = null, page = null;
        bool scan = false, widget = false, updated = false;
        var unknown = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i].Trim();
            if (a.Length == 0) continue;
            var key = a.TrimStart('-', '/').ToLowerInvariant();
            string? Next() => i + 1 < args.Count ? args[++i].Trim().Trim('"') : null;

            switch (key)
            {
                case "uninstall-file" or "uninstallfile" or "file": file = Next(); break;
                case "uninstall" or "name": name = Next(); break;
                case "scan" or "health": scan = true; break;
                case "widget" or "easy": widget = true; break;
                case "page": page = Next()?.ToLowerInvariant(); break;
                case "updated": updated = true; break;
                default:
                    // A bare path (drag & drop onto the exe, or "Open with") means --uninstall-file.
                    if (!a.StartsWith('-') && !a.StartsWith('/') && (a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
                        file = a.Trim('"');
                    else unknown.Add(a);
                    break;
            }
        }
        var opts = new CommandLineOptions { UninstallFile = file, UninstallName = name, Scan = scan, Widget = widget, Page = page, Updated = updated };
        opts.Unknown.AddRange(unknown);
        return opts;
    }

    /// <summary>Serialises for the single-instance pipe: one argument per line.</summary>
    public static string Pack(IReadOnlyList<string> args) => string.Join("\n", args.Select(a => a.Replace("\n", " ")));
    public static string[] Unpack(string payload) => payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
