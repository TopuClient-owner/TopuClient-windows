using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TopuLauncher
{
    public partial class MainWindow
    {
        private static readonly object ProfileTruthFixRegistration = RegisterProfileTruthFix();
        private bool _profileTruthFixInstalled;

        private static object RegisterProfileTruthFix()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(ProfileTruthFixLoaded));
            return new object();
        }

        private static void ProfileTruthFixLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is MainWindow window)
                window.InstallProfileTruthFix();
        }

        private void InstallProfileTruthFix()
        {
            if (_profileTruthFixInstalled || _loaderBox == null)
                return;

            _profileTruthFixInstalled = true;
            _loaderBox.SelectionChanged += ProfileTruthSelectionChanged;
            VersionBox.SelectionChanged += ProfileTruthSelectionChanged;
            RamSlider.ValueChanged += ProfileTruthRamChanged;
            ProfileSelector.SelectionChanged += ProfileTruthProfileChanged;

            TabProfiles.AddHandler(
                Button.ClickEvent,
                new RoutedEventHandler(ProfileTruthButtonClicked),
                true);

            Dispatcher.BeginInvoke(
                new Action(RefreshProfileTruthUi),
                DispatcherPriority.ApplicationIdle);
        }

        private void ProfileTruthSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(
                new Action(RefreshProfileTruthUi),
                DispatcherPriority.DataBind);
        }

        private void ProfileTruthRamChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Dispatcher.BeginInvoke(
                new Action(RefreshProfileTruthUi),
                DispatcherPriority.DataBind);
        }

        private void ProfileTruthProfileChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != ProfileSelector)
                return;

            Dispatcher.BeginInvoke(
                new Action(RefreshProfileTruthUi),
                DispatcherPriority.ApplicationIdle);
        }

        private void ProfileTruthButtonClicked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not Button button ||
                !string.Equals(
                    button.Content?.ToString(),
                    "Save Profile Settings",
                    StringComparison.OrdinalIgnoreCase))
                return;

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    try
                    {
                        // The legacy save writes Version/RAM. Immediately after
                        // it finishes, write the complete runtime profile so the
                        // selected Loader is never lost.
                        SaveRuntimeLoaderSetting();
                        RefreshProfileTruthUi();
                        UpdateLaunchSummary();
                    }
                    catch (Exception ex)
                    {
                        WriteException("PROFILE TRUTH SAVE ERROR", ex);
                    }
                }),
                DispatcherPriority.ApplicationIdle);
        }

        private void RefreshProfileTruthUi()
        {
            try
            {
                RuntimeProfileSettings profile = GetRuntimeProfile();
                string loader = profile.Loader;
                if (string.IsNullOrWhiteSpace(loader))
                    loader = _loaderBox?.SelectedItem?.ToString() ?? "Vanilla";

                string version = GetSelectedVersion();
                if (!string.IsNullOrWhiteSpace(profile.Version))
                    version = profile.Version;

                int ram = Math.Clamp((int)RamSlider.Value, 2, 12);
                if (profile.RamGb >= 2)
                    ram = Math.Clamp(profile.RamGb, 2, 12);

                string profileName = GetActiveProfileName();

                if (SelectedProfileLabel != null)
                    SelectedProfileLabel.Text = $"● {profileName}   •   {loader} {version}   •   {ram}GB RAM";

                if (LaunchProfileLabel != null)
                    LaunchProfileLabel.Text = profileName;
            }
            catch (Exception ex)
            {
                WriteException("PROFILE TRUTH UI ERROR", ex);
            }
        }
    }
}
