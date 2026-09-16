using System.Windows;
using System.Windows.Threading;
using Evict.App.Services;
using Evict.App.ViewModels;
using Evict.Core.Services;
using Evict.Core.Util;

namespace Evict.App;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;
    public static UiState UiState { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error("Unobserved task exception", args.Exception); args.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);

        Services = new AppServices();
        Services.Settings.Load();
        Services.History.Load();
        ApplyTheme(Services.Settings.Current.Theme);
        UiState.Scale = UiState.Clamp(Services.Settings.Current.UiScale);
        UiState.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UiState.Scale))
            {
                Services.Settings.Current.UiScale = UiState.Scale;
                Services.Settings.Save();
            }
        };

        Log.Info($"{AppPaths.ProductName} starting. Elevated={ElevationHelper.IsElevated}, OS={Environment.OSVersion}, .NET={Environment.Version}, args=[{string.Join(" ", Program.StartupArgs)}]");

        // Keep the Explorer context-menu command pointing at this exe if it moved.
        if (Services.Settings.Current.ExplorerContextMenu && ShellIntegration.NeedsRefresh()) ShellIntegration.Register();

        var mainVm = new MainViewModel(Services);
        var main = new MainWindow { DataContext = mainVm };
        MainWindow = main;
        main.Show();

        SingleInstance.StartServer(args => HandleArgs(mainVm, args));
        if (Program.StartupArgs.Length > 0) HandleArgs(mainVm, Program.StartupArgs);
        else if (Services.Settings.Current.EasyUninstallWidgetVisible) mainVm.ShowWidgetCommand.Execute(null);

        // Housekeeping after a self-update, then the (optional) update check in the background.
        Services.Updater.CleanupAfterUpdate();
        _ = mainVm.CheckForUpdatesOnStartupAsync();
    }

    private static void HandleArgs(MainViewModel vm, string[] args)
    {
        try
        {
            var w = Current.MainWindow;
            if (w != null)
            {
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Show();
                w.Activate();
                w.Topmost = true; w.Topmost = false; // bring to front without staying on top
            }
            var options = CommandLineOptions.Parse(args);
            if (!options.IsEmpty) _ = vm.HandleCommandLineAsync(options);
        }
        catch (Exception ex) { Log.Error("Handling command line failed", ex); }
    }

    public static void ApplyTheme(string theme)
    {
        var name = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{name}.xaml", UriKind.Absolute) };
        var merged = Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict; else merged.Add(dict);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Dispatcher exception", e.Exception);
        try
        {
            MessageBox.Show(MainWindow, "Something went wrong:\n\n" + e.Exception.Message + "\n\nDetails were written to the log file:\n" + AppPaths.LogFile,
                AppPaths.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* ignore */ }
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Services?.Settings.Save(); } catch { /* ignore */ }
        Log.Info("Exit.");
        base.OnExit(e);
    }
}
