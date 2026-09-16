using System.Text.Json;
using System.Text.Json.Serialization;

namespace Evict.Core.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Light";                 // Light | Dark
    public bool CreateRestorePoint { get; set; } = true;
    public bool QuietUninstall { get; set; } = false;
    public bool AutoCleanLeftovers { get; set; } = false;        // false → show review step
    public bool SendToRecycleBin { get; set; } = true;
    public bool ShowSystemComponents { get; set; } = false;
    public bool ShowWindowsUpdatesInPrograms { get; set; } = false;
    public bool ScanAllUserProfiles { get; set; } = true;
    public bool MeasureFolderSizes { get; set; } = true;          // compute sizes when EstimatedSize is missing
    public int LargeProgramThresholdMb { get; set; } = 500;
    public int RecentlyInstalledDays { get; set; } = 30;
    public int InfrequentlyUsedDays { get; set; } = 60;
    public bool ConfirmBeforeUninstall { get; set; } = true;
    public bool HideFrameworkAppx { get; set; } = true;
    public bool ShowSystemAppx { get; set; } = false;
    public string ShredMethod { get; set; } = "Dod3Pass";
    /// <summary>UI zoom / text size factor (0.9 – 1.4).</summary>
    public double UiScale { get; set; } = 1.0;
    public bool ExplorerContextMenu { get; set; } = false;
    public bool EasyUninstallWidgetVisible { get; set; } = false;
    public double WidgetLeft { get; set; } = -1;
    public double WidgetTop { get; set; } = -1;
    public bool HealthAutoScan { get; set; } = true;
    /// <summary>Check GitHub Releases for a newer version at start-up.</summary>
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    /// <summary>A version the user chose to skip ("v1.3.0"); the banner stays hidden for it.</summary>
    public string? SkippedUpdateVersion { get; set; }
    public double WindowWidth { get; set; } = 1240;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; } = false;
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    var json = File.ReadAllText(AppPaths.SettingsFile);
                    Current = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
                }
            }
            catch
            {
                Current = new AppSettings();
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                var json = JsonSerializer.Serialize(Current, Options);
                File.WriteAllText(AppPaths.SettingsFile, json);
            }
            catch
            {
                // Settings are a convenience; never crash for them.
            }
        }
    }
}
