using System;
using System.Windows;
using System.Windows.Threading;

namespace TopuLauncher;

/// <summary>
/// Final launch-summary correction. Keeps the version and RAM labels sourced
/// from the active runtime profile instead of accidentally assigning RAM to the
/// version label.
/// </summary>
public partial class MainWindow
{
    private static readonly object LaunchSummaryFixRegistration = RegisterLaunchSummaryFix();

    private static object RegisterLaunchSummaryFix()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(LaunchSummaryFixLoaded));
        return new object();
    }

    private static void LaunchSummaryFixLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
        {
            window.Dispatcher.BeginInvoke(
                new Action(window.RefreshCorrectLaunchSummary),
                DispatcherPriority.ApplicationIdle);
        }
    }

    private void RefreshCorrectLaunchSummary()
    {
        try
        {
            RuntimeProfileSettings profile = GetRuntimeProfile();
            string loader = string.IsNullOrWhiteSpace(profile.Loader)
                ? "Vanilla"
                : profile.Loader;
            string version = string.IsNullOrWhiteSpace(profile.Version)
                ? GetSelectedVersion()
                : profile.Version;
            int ram = Math.Clamp(profile.RamGb, 2, 12);

            if (LaunchProfileLabel != null)
                LaunchProfileLabel.Text = GetActiveProfileName();
            if (LaunchLoaderLabel != null)
                LaunchLoaderLabel.Text = loader;
            if (LaunchVersionLabel != null)
                LaunchVersionLabel.Text = version;
            if (LaunchRamLabel != null)
                LaunchRamLabel.Text = $"{ram}GB RAM";
            if (SelectedProfileLabel != null)
            {
                SelectedProfileLabel.Text =
                    $"● {GetActiveProfileName()}   •   {loader} {version}   •   {ram}GB RAM";
            }
        }
        catch (Exception ex)
        {
            WriteException("LAUNCH SUMMARY FIX ERROR", ex);
        }
    }
}
