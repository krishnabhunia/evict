using Microsoft.Win32;

namespace Evict.App.Services;

/// <summary>
/// Explorer right-click integration: "Uninstall with Evict" on .exe files and shortcuts.
/// Registered per user under HKCU\Software\Classes – no administrator rights needed.
/// </summary>
public static class ShellIntegration
{
    private const string Verb = "EvictUninstall";
    private static readonly string[] FileClasses = { "exefile", "lnkfile" };

    private static string ExePath => Environment.ProcessPath ?? "Evict.exe";

    public static bool IsRegistered()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\exefile\shell\{Verb}\command");
            return k?.GetValue("") is string;
        }
        catch { return false; }
    }

    /// <summary>True when the registered command points at a different (moved/renamed) Evict.exe.</summary>
    public static bool NeedsRefresh()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\exefile\shell\{Verb}\command");
            var cmd = k?.GetValue("") as string;
            return cmd != null && !cmd.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static (bool Ok, string? Error) Register()
    {
        try
        {
            foreach (var cls in FileClasses)
            {
                using var verb = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{cls}\shell\{Verb}");
                verb.SetValue("", "Uninstall with Evict");
                verb.SetValue("Icon", $"\"{ExePath}\",0");
                using var command = verb.CreateSubKey("command");
                command.SetValue("", $"\"{ExePath}\" --uninstall-file \"%1\"");
            }
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static (bool Ok, string? Error) Unregister()
    {
        try
        {
            foreach (var cls in FileClasses)
            {
                using var shell = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{cls}\shell", writable: true);
                shell?.DeleteSubKeyTree(Verb, throwOnMissingSubKey: false);
            }
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
