using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Evict.App.Services;
using Evict.Core.Models;
using Evict.Core.Services;

namespace Evict.App.ViewModels;

public sealed partial class UpgradeItemViewModel : ObservableObject
{
    public UpgradeItemViewModel(UpgradablePackage pkg) => Package = pkg;
    public UpgradablePackage Package { get; }
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isUpdating;
    public string Name => Package.Name;
    public string Id => Package.Id;
    public string Installed => Package.InstalledVersion ?? "—";
    public string Available => Package.AvailableVersion ?? "—";
    public string Source => Package.Source ?? "";
}

public sealed partial class SoftwareUpdaterViewModel : ObservableObject, IActivatable
{
    private readonly AppServices _services;
    private bool _loaded;
    private CancellationTokenSource? _cts;

    public SoftwareUpdaterViewModel(AppServices services)
    {
        _services = services;
        WingetAvailable = WingetService.IsAvailable;
    }

    public ObservableCollection<UpgradeItemViewModel> Items { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _wingetAvailable;
    [ObservableProperty] private bool _includeUnknown;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private DateTime? _lastChecked;

    public string Summary => Items.Count == 0 ? (LastChecked is null ? "Not checked yet." : "Everything is up to date.") : $"{Items.Count} update(s) available";
    public string LastCheckedText => LastChecked is { } d ? "Last checked " + ProgramItemViewModel.Relative(d) : "";

    public void OnActivated()
    {
        WingetAvailable = WingetService.IsAvailable;
        if (!_loaded && WingetAvailable) _ = CheckAsync();
    }

    partial void OnIsBusyChanged(bool value) => UpdateSelectedCommand.NotifyCanExecuteChanged();
    partial void OnIsUpdatingChanged(bool value) => UpdateSelectedCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    public async Task CheckAsync()
    {
        if (!WingetAvailable) return;
        IsBusy = true;
        Error = null;
        StatusText = "Checking for updates with winget… (this can take a minute)";
        try
        {
            var (pkgs, err) = await _services.Winget.GetUpgradesAsync(IncludeUnknown, CancellationToken.None);
            foreach (var i in Items) i.PropertyChanged -= ItemChanged;
            Items.Clear();
            foreach (var p in pkgs)
            {
                var vm = new UpgradeItemViewModel(p);
                vm.PropertyChanged += ItemChanged;
                Items.Add(vm);
            }
            Error = err;
            LastChecked = DateTime.Now;
            _loaded = true;
            UpdateSelection();
            StatusText = "";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(LastCheckedText));
        }
    }

    private void ItemChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpgradeItemViewModel.IsSelected)) UpdateSelection();
    }

    private void UpdateSelection()
    {
        SelectedCount = Items.Count(i => i.IsSelected);
        UpdateSelectedCommand.NotifyCanExecuteChanged();
    }

    private bool CanUpdate() => SelectedCount > 0 && !IsBusy && !IsUpdating;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateSelectedAsync()
    {
        var targets = Items.Where(i => i.IsSelected).ToList();
        if (targets.Count == 0) return;
        _cts = new CancellationTokenSource();
        IsUpdating = true;
        LogLines.Clear();
        int ok = 0, fail = 0;
        try
        {
            foreach (var t in targets)
            {
                if (_cts.IsCancellationRequested) break;
                StatusText = $"Updating {t.Name} ({ok + fail + 1}/{targets.Count})…";
                t.IsUpdating = true;
                t.Status = "Updating…";
                var (success, msg) = await _services.Winget.UpgradeAsync(t.Package, _cts.Token, line =>
                {
                    if (string.IsNullOrWhiteSpace(line)) return;
                    var clean = line.Trim();
                    if (clean.All(c => c is '-' or '\\' or '|' or '/' or ' ' or '█' or '▒')) return;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { LogLines.Add($"[{t.Name}] {clean}"); if (LogLines.Count > 400) LogLines.RemoveAt(0); });
                });
                t.IsUpdating = false;
                t.Status = msg;
                if (success) { ok++; t.IsSelected = false; } else fail++;
            }
            StatusText = $"Updated {ok} package(s)" + (fail > 0 ? $", {fail} failed." : ".");
        }
        catch (OperationCanceledException) { StatusText = "Cancelled."; }
        finally { IsUpdating = false; }
        if (ok > 0) await CheckAsync();
    }

    [RelayCommand]
    private void CancelUpdate() => _cts?.Cancel();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var i in Items) i.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var i in Items) i.IsSelected = false;
    }

    [RelayCommand]
    private void InstallWinget() => Dialogs.OpenUrl("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1");

    [RelayCommand]
    private void OpenPackagePage(UpgradeItemViewModel? item)
    {
        if (item is null) return;
        Dialogs.OpenUrl("https://winget.run/pkg/" + item.Id.Replace('.', '/'));
    }
}
