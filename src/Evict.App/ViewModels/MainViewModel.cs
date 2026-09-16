using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evict.App.Services;
using Evict.Core.Services;

namespace Evict.App.ViewModels;

public enum PageKey { Programs, WindowsApps, BrowserExtensions, SoftwareUpdater, InstallMonitor, Tools, History, Settings }

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

    public MainViewModel(AppServices services)
    {
        _services = services;
        NavItems = new ObservableCollection<NavItemViewModel>
        {
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
        Navigate(PageKey.Programs);
    }

    public ObservableCollection<NavItemViewModel> NavItems { get; }

    [ObservableProperty] private ObservableObject? _currentPage;
    [ObservableProperty] private string _currentTitle = "Programs";

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
