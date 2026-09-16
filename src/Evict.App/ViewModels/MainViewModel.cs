using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evict.App.Services;
using Evict.App.Views;
using Evict.Core.Models;
using Evict.Core.Services;
using Evict.Core.Util;

namespace Evict.App.ViewModels;

public enum PageKey { Health, Programs, WindowsApps, BrowserExtensions, SoftwareUpdater, InstallMonitor, Tools, History, Settings }

public sealed partial class NavItemViewModel : ObservableObject
{
    public required PageKey Key { get; init; }
    public required string Title { get; init; }
    public required string Glyph { get; init; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _badge;
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Dictionary<PageKey, ObservableObject> _pages = new();
    private EasyUninstallWindow? _widget;

    public MainViewModel(AppServices services)
    {
        _services = services;
        NavItems = new ObservableCollection<NavItemViewModel>
        {
            new() { Key = PageKey.Health, Title = "Software Health", Glyph = "" },
            new() { Key = PageKey.Programs, Title = "Programs", Glyph = "" },
            new() { Key = PageKey.WindowsApps, Title = "Windows Apps", Glyph = "" },
            new() { Key = PageKey.BrowserExtensions, Title = "Browser Extensions", Glyph = "" },
            new() { Key = PageKey.SoftwareUpdater, Title = "Software Updater", Glyph = "" },
            new() { Key = PageKey.InstallMonitor, Title = "Install Monitor", Glyph = "" },
            new() { Key = PageKey.Tools, Title = "Tools", Glyph = "" },
            new() { Key = PageKey.History, Title = "History", Glyph = "" },
            new() { Key = PageKey.Settings, Title = "Settings", Glyph = "" },
        };
        foreach (var item in NavItems)
        {
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NavItemViewModel.IsSelected) && s is NavItemViewModel { IsSelected: true } n) Navigate(n.Key);
            };
        }
        Navigate(PageKey.Health);
    }

    public ObservableCollection<NavItemViewModel> NavItems { get; }

    [ObservableProperty] private ObservableObject? _currentPage;
    [ObservableProperty] private string _currentTitle = "Software Health";
    [ObservableProperty] private bool _isWidgetVisible;

    public bool IsElevated => ElevationHelper.IsElevated;
    public bool ShowAdminBanner => !ElevationHelper.IsElevated && !_adminBannerDismissed;
    private bool _adminBannerDismissed;

    public string WindowTitle => AppPaths.ProductName + (IsElevated ? "  (Administrator)" : "");
    public string VersionText => "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    public void Navigate(PageKey key)
    {
        foreach (var n in NavItems) if (n.Key == key && !n.IsSelected) n.IsSelected = true;
        CurrentTitle = NavItems.First(n => n.Key == key).Title;
        CurrentPage = GetOrCreate(key);
        if (CurrentPage is IActivatable a) a.OnActivated();
    }

    private ObservableObject GetOrCreate(PageKey key)
    {
        if (_pages.TryGetValue(key, out var vm)) return vm;
        vm = key switch
        {
            PageKey.Health => new HealthViewModel(_services, this),
            PageKey.Programs => new ProgramsViewModel(_services, this),
            PageKey.WindowsApps => new WindowsAppsViewModel(_services),
            PageKey.BrowserExtensions => new BrowserExtensionsViewModel(_services),
            PageKey.SoftwareUpdater => new SoftwareUpdaterViewModel(_services),
            PageKey.InstallMonitor => new InstallMonitorViewModel(_services, this),
            PageKey.Tools => new ToolsViewModel(_services, this),
            PageKey.History => new HistoryViewModel(_services, this),
            PageKey.Settings => new SettingsViewModel(_services),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        _pages[key] = vm;
        return vm;
    }

    public T GetPage<T>(PageKey key) where T : ObservableObject => (T)GetOrCreate(key);

    public void SetBadge(PageKey key, int count)
    {
        var item = NavItems.FirstOrDefault(n => n.Key == key);
        if (item != null) item.Badge = count > 0 ? count.ToString() : null;
    }

    // ───────────────────────────── command line / IPC ─────────────────────────────

    public async Task HandleCommandLineAsync(CommandLineOptions o)
    {
        if (o.Page is { } page)
        {
            var key = page switch
            {
                "programs" => PageKey.Programs, "apps" or "windowsapps" => PageKey.WindowsApps, "extensions" => PageKey.BrowserExtensions,
                "updater" or "updates" => PageKey.SoftwareUpdater, "monitor" => PageKey.InstallMonitor, "tools" => PageKey.Tools,
                "history" => PageKey.History, "settings" => PageKey.Settings, _ => PageKey.Health,
            };
            Navigate(key);
        }
        if (o.Widget) ShowWidget();
        if (o.Scan)
        {
            Navigate(PageKey.Health);
            await GetPage<HealthViewModel>(PageKey.Health).ScanAsync();
        }
        if (o.UninstallFile != null) await UninstallByPathAsync(o.UninstallFile);
        else if (o.UninstallName != null)
        {
            var programs = GetPage<ProgramsViewModel>(PageKey.Programs);
            Navigate(PageKey.Programs);
            await programs.EnsureLoadedAsync();
            var match = ProgramMatcher.FindByName(programs.Items.Select(i => i.Program), o.UninstallName);
            if (match != null) await programs.LaunchWizardForAsync(match);
            else Dialogs.Info($"No installed program matches \"{o.UninstallName}\".");
        }
    }

    /// <summary>Used by the Explorer context menu, drag & drop and the Easy Uninstall widget.</summary>
    public async Task UninstallByPathAsync(string path)
    {
        var target = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutResolver.ResolveTarget(path) ?? path : path;
        var programs = GetPage<ProgramsViewModel>(PageKey.Programs);
        Navigate(PageKey.Programs);
        await programs.EnsureLoadedAsync();
        var match = ProgramMatcher.FindByPath(programs.Items.Select(i => i.Program), target);
        if (match != null)
        {
            programs.SearchText = "";
            await programs.LaunchWizardForAsync(match);
            return;
        }

        if (Dialogs.Confirm($"\"{PathUtil.LeafName(target)}\" does not belong to any program in Programs & Features.\n\nOpen Force Uninstall for its folder instead?"))
        {
            var vm = new ForceUninstallViewModel(_services, null, programs.Items.Select(i => i.Program).ToList()) { UseProgram = false, TargetPath = target };
            new ForceUninstallWindow { DataContext = vm, Owner = Application.Current.MainWindow }.ShowDialog();
            if (vm.AnythingChanged) { _ = programs.RefreshAsync(); GetPage<HistoryViewModel>(PageKey.History).Reload(); }
        }
    }

    // ───────────────────────────── Easy Uninstall widget ─────────────────────────────

    [RelayCommand]
    private void ShowWidget()
    {
        if (_widget is { IsLoaded: true }) { _widget.Activate(); return; }
        _widget = new EasyUninstallWindow(this);
        _widget.Closed += (_, _) => { _widget = null; IsWidgetVisible = false; _services.Settings.Current.EasyUninstallWidgetVisible = false; _services.Settings.Save(); };
        _widget.Show();
        IsWidgetVisible = true;
        _services.Settings.Current.EasyUninstallWidgetVisible = true;
        _services.Settings.Save();
    }

    [RelayCommand]
    private void ToggleWidget()
    {
        if (_widget != null) _widget.Close();
        else ShowWidget();
    }

    [RelayCommand]
    private void RestartAsAdministrator()
    {
        if (IsElevated) return;
        Program.ReleaseSingleInstance();
        if (ElevationHelper.RestartElevated())
        {
            Application.Current.Shutdown();
        }
        else
        {
            Dialogs.Info("Administrator rights were not granted. You can keep using Evict, but some operations will be limited.");
        }
    }

    [RelayCommand]
    private void DismissAdminBanner()
    {
        _adminBannerDismissed = true;
        OnPropertyChanged(nameof(ShowAdminBanner));
    }

    [RelayCommand]
    private void OpenLogFolder() => Dialogs.OpenFolder(AppPaths.DataRoot);
}

/// <summary>Pages that want a hook each time they are shown.</summary>
public interface IActivatable
{
    void OnActivated();
}
