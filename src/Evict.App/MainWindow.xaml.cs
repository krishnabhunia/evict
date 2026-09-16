using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Evict.Core.Services;

namespace Evict.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var s = App.Services.Settings.Current;
        if (s.WindowWidth >= MinWidth && s.WindowHeight >= MinHeight)
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
        StateChanged += (_, _) => UpdateMaxRestoreGlyph();
        SourceInitialized += (_, _) => TryRoundCorners();
        Closing += (_, _) =>
        {
            var cur = App.Services.Settings.Current;
            cur.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal) { cur.WindowWidth = Width; cur.WindowHeight = Height; }
            App.Services.Settings.Save();
        };
        UpdateMaxRestoreGlyph();
    }

    private void UpdateMaxRestoreGlyph()
    {
        MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "" : "";
        MaxRestoreButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeRestore(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // Windows 11 rounded corners for a WindowChrome window (harmless no-op on Windows 10).
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void TryRoundCorners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
        }
        catch (Exception ex)
        {
            Log.Warn("Rounded corners unavailable: " + ex.Message);
        }
    }
}
