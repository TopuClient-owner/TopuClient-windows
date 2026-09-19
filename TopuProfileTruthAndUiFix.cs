using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TopuLauncher
{
    // Keeps profile state, the launch card, and the Modrinth manager on one
    // source of truth. This also repairs older profiles that were saved with
    // the launch-card defaults instead of their selected loader/version.
    public partial class MainWindow
    {
        private static readonly object ProfileTruthAndUiFixHook = RegisterProfileTruthAndUiFixHook();

        private static object RegisterProfileTruthAndUiFixHook()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(ProfileTruthAndUiFixLoaded));
            return new object();
        }

        private static void ProfileTruthAndUiFixLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window || window.Tag?.ToString() == "TopuProfileTruthAndUiFix")
                return;

            window.Tag = "TopuProfileTruthAndUiFix";
            window.Dispatcher.BeginInvoke(new Action(window.InstallProfileTruthAndUiFix));
        }

        private void InstallProfileTruthAndUiFix()
        {
            // The XAML save button has no Click attribute; the runtime loader
            // adds its handler. Listen as well so a save always updates both
            // profile formats, even when an older UI handler marks the event
            // handled.
            TabProfiles?.AddHandler(
                Button.ClickEvent,
                new RoutedEventHandler(ProfileTruthButtonClicked),
                true);

            VersionBox?.AddHandler(
                Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler(ProfileTruthSelectionChanged),
                true);

            _loaderBox?.AddHandler(
                Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler(ProfileTruthSelectionChanged),
                true);

            ProfileSelector?.AddHandler(
                Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler(ProfileTruthSelectionChanged),
                true);

            RefreshProfileTruthAndLaunchCard();
            ApplyLunarClientVisualPolish();
        }

        private void ProfileTruthButtonClicked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Button button &&
                string.Equals(button.Content?.ToString(), "Save Profile Settings", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SaveProfileTruthFromUi();
                    RefreshProfileTruthAndLaunchCard();
                }));
            }
        }

        private void ProfileTruthSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ignore the transient empty selection while the loader-specific
            // version catalog is being rebuilt.
            if (e.OriginalSource is not ComboBox box ||
                (box == VersionBox && !VersionBox.IsEnabled))
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                SaveProfileTruthFromUi();
                RefreshProfileTruthAndLaunchCard();
            }));
        }

        private void SaveProfileTruthFromUi()
        {
            try
            {
                RuntimeProfileSettings current = GetRuntimeProfile();
                string loader = _loaderBox?.SelectedItem?.ToString() ?? current.Loader;
                if (string.IsNullOrWhiteSpace(loader)) loader = "Vanilla";

                string version = GetSelectedVersion();
                if (string.IsNullOrWhiteSpace(version) || version == "unknown")
                    version = current.Version;
                if (string.IsNullOrWhiteSpace(version)) version = "1.21.1";

                int ram = Math.Clamp((int)RamSlider.Value, 2, 12);
                RuntimeProfileSettings settings = new RuntimeProfileSettings
                {
                    Loader = loader,
                    Version = version,
                    RamGb = ram,
                    ForgeVersion = current.ForgeVersion
                };

                // These two files existed in different launcher revisions.
                // Keep them identical so the launcher and Modrinth cannot use
                // different loader/version values for the same profile.
                WriteRuntimeProfile(settings);
                SaveProfileSettings(_gamePath, new ProfileSettings
                {
                    Loader = settings.Loader,
                    Version = settings.Version,
                    RamGb = settings.RamGb
                });
            }
            catch (Exception ex)
            {
                WriteException("PROFILE TRUTH SAVE ERROR", ex);
            }
        }

        private void RefreshProfileTruthAndLaunchCard()
        {
            try
            {
                RuntimeProfileSettings profile = GetRuntimeProfile();
                string loader = string.IsNullOrWhiteSpace(profile.Loader) ? "Vanilla" : profile.Loader;
                string version = string.IsNullOrWhiteSpace(profile.Version) ? "1.21.1" : profile.Version;
                int ram = Math.Clamp(profile.RamGb, 2, 12);

                if (LaunchProfileLabel != null) LaunchProfileLabel.Text = GetActiveProfileName();
                if (LaunchLoaderLabel != null) LaunchLoaderLabel.Text = loader;
                if (LaunchVersionLabel != null) LaunchVersionLabel.Text = version;
                if (LaunchRamLabel != null) LaunchRamLabel.Text = $"{ram}GB RAM";
                if (SelectedProfileLabel != null)
                    SelectedProfileLabel.Text = $"● {GetActiveProfileName()}   •   {loader} {version}   •   {ram}GB RAM";
            }
            catch (Exception ex)
            {
                WriteException("PROFILE TRUTH UI ERROR", ex);
            }
        }

        private void ApplyLunarClientVisualPolish()
        {
            // Keep the existing dark Topu identity while matching the compact,
            // high-contrast card treatment users expect from Lunar Client.
            if (TabLaunch != null) TabLaunch.Background = Brushes.Transparent;
            if (TabProfiles != null) TabProfiles.Background = Brushes.Transparent;
            if (TabAccounts != null) TabAccounts.Background = Brushes.Transparent;
            if (LaunchBtn != null)
            {
                LaunchBtn.FontWeight = FontWeights.Bold;
                LaunchBtn.Margin = new Thickness(0, 4, 0, 0);
            }
        }
    }
}
