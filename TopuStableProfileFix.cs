using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TopuLauncher
{
    // Small, isolated compatibility layer. It does not replace the launcher
    // runtime; it only makes sure the current UI selections are committed to
    // the active profile before the existing LaunchPreview/Click handlers run.
    public partial class MainWindow
    {
        private static readonly object StableProfileFixRegistration = RegisterStableProfileFix();

        private static object RegisterStableProfileFix()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(StableProfileFixLoaded));

            EventManager.RegisterClassHandler(
                typeof(Button),
                Button.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(StableLaunchPreview),
                true);

            return new object();
        }

        private static void StableProfileFixLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window)
                return;

            window.VersionBox.IsEnabled = true;
            window.UpdateLaunchSummary();
        }

        private static void StableLaunchPreview(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button button ||
                !string.Equals(button.Name, "LaunchBtn", StringComparison.OrdinalIgnoreCase))
                return;

            if (Window.GetWindow(button) is not MainWindow window)
                return;

            try
            {
                // Save exactly what is visible in the UI. This prevents the
                // old saved profile (for example Vanilla 1.8.9 / 4GB) from
                // replacing the user's current Quilt/Fabric/Forge selection.
                window.SaveRuntimeLoaderSetting();
                window.VersionBox.IsEnabled = true;
                window.UpdateLaunchSummary();
            }
            catch (Exception ex)
            {
                window.WriteException("STABLE PROFILE PRE-LAUNCH SAVE ERROR", ex);
            }
        }
    }
}
