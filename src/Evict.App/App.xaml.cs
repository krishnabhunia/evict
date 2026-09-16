using System.Windows;
using System.Windows.Threading;
using Evict.App.Services;
using Evict.App.ViewModels;
using Evict.Core.Services;

namespace Evict.App;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

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

        Log.Info($"{AppPaths.ProductName} starting. Elevated={ElevationHelper.IsElevated}, OS={Environment.OSVersion}, .NET={Environment.Version}");

        var main = new MainWindow { DataContext = new MainViewModel(Services) };
        MainWindow = main;
        main.Show();
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
