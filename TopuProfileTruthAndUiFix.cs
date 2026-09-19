using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TopuLauncher
{
    // Keeps the launch summary synchronized with the selected profile without
    // colliding with the older TopuProfileTruthFix handlers.
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
            TabProfiles?.AddHandler(
                Button.ClickEvent,
                new RoutedEventHandler(ProfileTruthAndUiButtonClicked),
                true);

            VersionBox?.AddHandler(
                Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler(ProfileTruthAndUiSelectionChanged),
                true);

            _loaderBox?.AddHandler(
                Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler(ProfileTruthAndUiSelectionChanged),
                true);

            ProfileSelector?.AddHandler(
                Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler(ProfileTruthAndUiSelectionChanged),
                true);

            RefreshProfileTruthAndLaunchCard();
            ApplyLunarClientVisualPolish();
        }

        private void ProfileTruthAndUiButtonClicked(object sender, RoutedEventArgs e)
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

        private void ProfileTruthAndUiSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
