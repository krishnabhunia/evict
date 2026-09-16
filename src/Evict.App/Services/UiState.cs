using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Evict.App.Services;

/// <summary>Application-wide UI state that is not a user setting per se: current zoom (text size) factor.</summary>
public sealed partial class UiState : ObservableObject
{
    public const double MinScale = 0.9;
    public const double MaxScale = 1.4;

    [ObservableProperty] private double _scale = 1.0;

    public static readonly IReadOnlyList<KeyValuePair<double, string>> TextSizeOptions = new[]
    {
        new KeyValuePair<double, string>(0.9, "Small (90 %)"),
        new KeyValuePair<double, string>(1.0, "Normal (100 %)"),
        new KeyValuePair<double, string>(1.1, "Large (110 %)"),
        new KeyValuePair<double, string>(1.2, "Larger (120 %)"),
        new KeyValuePair<double, string>(1.3, "Extra large (130 %)"),
        new KeyValuePair<double, string>(1.4, "Huge (140 %)"),
    };

    public static double Clamp(double v) => Math.Round(Math.Clamp(v, MinScale, MaxScale), 2);

    public void Step(double delta) => Scale = Clamp(Scale + delta);

    /// <summary>
    /// Scales a dialog window once at creation: its content gets a LayoutTransform and its size grows to match,
    /// so the window still shows the same amount of UI at every text size.
    /// </summary>
    public void ApplyToDialog(Window window)
    {
        var s = Scale;
        if (window.Content is FrameworkElement root) root.LayoutTransform = new ScaleTransform(s, s);
        if (Math.Abs(s - 1.0) < 0.001) return;
        var work = SystemParameters.WorkArea;
        window.MinWidth = Math.Min(window.MinWidth * s, work.Width);
        window.MinHeight = Math.Min(window.MinHeight * s, work.Height);
        window.Width = Math.Min(window.Width * s, work.Width - 40);
        window.Height = Math.Min(window.Height * s, work.Height - 40);
    }
}
