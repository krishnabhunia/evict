using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evict.App.Services;
using Evict.Core.Services;

namespace Evict.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private AppSettings S => _services.Settings.Current;

    public SettingsViewModel(AppServices services) => _services = services;

    public bool IsDarkTheme
    {
        get => S.Theme == "Dark";
        set { S.Theme = value ? "Dark" : "Light"; App.ApplyTheme(S.Theme); Save(); OnPropertyChanged(); }
    }

    public IReadOnlyList<KeyValuePair<double, string>> TextSizeOptions => UiState.TextSizeOptions;
    public double TextSize
    {
        get => UiState.TextSizeOptions.Select(o => o.Key).OrderBy(k => Math.Abs(k - App.UiState.Scale)).First();
        set { App.UiState.Scale = UiState.Clamp(value); OnPropertyChanged(); }
    }

    public bool ExplorerContextMenu
    {
        get => S.ExplorerContextMenu;
        set
        {
            var (ok, error) = value ? ShellIntegration.Register() : ShellIntegration.Unregister();
            if (ok) { S.ExplorerContextMenu = value; Save(); }
            else Dialogs.Error("Could not update the Explorer context menu: " + error);
            OnPropertyChanged();
        }
    }

    public bool HealthAutoScan { get => S.HealthAutoScan; set { S.HealthAutoScan = value; Save(); OnPropertyChanged(); } }
    public bool CreateRestorePoint { get => S.CreateRestorePoint; set { S.CreateRestorePoint = value; Save(); OnPropertyChanged(); } }
    public bool QuietUninstall { get => S.QuietUninstall; set { S.QuietUninstall = value; Save(); OnPropertyChanged(); } }
    public bool AutoCleanLeftovers { get => S.AutoCleanLeftovers; set { S.AutoCleanLeftovers = value; Save(); OnPropertyChanged(); } }
    public bool SendToRecycleBin { get => S.SendToRecycleBin; set { S.SendToRecycleBin = value; Save(); OnPropertyChanged(); } }
    public bool ShowSystemComponents { get => S.ShowSystemComponents; set { S.ShowSystemComponents = value; Save(); OnPropertyChanged(); ProgramsChanged = true; } }
    public bool ShowWindowsUpdatesInPrograms { get => S.ShowWindowsUpdatesInPrograms; set { S.ShowWindowsUpdatesInPrograms = value; Save(); OnPropertyChanged(); ProgramsChanged = true; } }
    public bool ScanAllUserProfiles { get => S.ScanAllUserProfiles; set { S.ScanAllUserProfiles = value; Save(); OnPropertyChanged(); } }
    public bool MeasureFolderSizes { get => S.MeasureFolderSizes; set { S.MeasureFolderSizes = value; Save(); OnPropertyChanged(); ProgramsChanged = true; } }
    public int LargeProgramThresholdMb { get => S.LargeProgramThresholdMb; set { S.LargeProgramThresholdMb = Math.Clamp(value, 10, 100000); Save(); OnPropertyChanged(); ProgramsChanged = true; } }
    public int RecentlyInstalledDays { get => S.RecentlyInstalledDays; set { S.RecentlyInstalledDays = Math.Clamp(value, 1, 3650); Save(); OnPropertyChanged(); ProgramsChanged = true; } }
    public int InfrequentlyUsedDays { get => S.InfrequentlyUsedDays; set { S.InfrequentlyUsedDays = Math.Clamp(value, 1, 3650); Save(); OnPropertyChanged(); ProgramsChanged = true; } }

    /// <summary>Set when a setting that changes the Programs list was modified; the Programs page refreshes on next activation.</summary>
    public bool ProgramsChanged { get; set; }

    public bool IsElevated => ElevationHelper.IsElevated;
    public string VersionText => "Version " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    public string DataFolder => AppPaths.DataRoot;
    public string RuntimeText => $".NET {Environment.Version} · {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} · {Environment.OSVersion.VersionString}";
    public string ElevationText => IsElevated ? "Running as administrator" : "Running as a standard user – some operations will prompt or be limited";

    private void Save() => _services.Settings.Save();

    [RelayCommand] private void OpenDataFolder() => Dialogs.OpenFolder(AppPaths.DataRoot);
    [RelayCommand] private void OpenLogFile() => Dialogs.OpenFolder(AppPaths.LogFile);

    [RelayCommand]
    private void ResetDefaults()
    {
        if (!Dialogs.Confirm("Reset all settings to their defaults?")) return;
        var theme = S.Theme;
        var fresh = new AppSettings();
        foreach (var p in typeof(AppSettings).GetProperties()) p.SetValue(S, p.GetValue(fresh));
        if (theme != S.Theme) App.ApplyTheme(S.Theme);
        Save();
        ProgramsChanged = true;
        OnPropertyChanged(string.Empty);
    }
}
