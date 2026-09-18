using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TopuLauncher
{
    // Final guard for profile state. This intentionally sits at the UI edge so
    // legacy Fabric-default handlers cannot overwrite the user's loader.
    public partial class MainWindow
    {
        private static readonly object TopuLoaderProfileHardFixRegistration = RegisterTopuLoaderProfileHardFix();
        private bool _topuLoaderProfileHardFixInstalled;

        private static object RegisterTopuLoaderProfileHardFix()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(TopuLoaderProfileHardFixLoaded));
            return new object();
        }

        private static void TopuLoaderProfileHardFixLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is MainWindow window)
            {
                window.Dispatcher.BeginInvoke(
                    new Action(window.InstallTopuLoaderProfileHardFix),
                    DispatcherPriority.ApplicationIdle);
            }
        }

        private void InstallTopuLoaderProfileHardFix()
        {
            if (_topuLoaderProfileHardFixInstalled || _loaderBox == null)
                return;

            _topuLoaderProfileHardFixInstalled = true;

            // The old runtime handler may populate its reduced fallback list
            // first. Our handler runs afterward and replaces it with the live
            // loader catalog.
            _loaderBox.SelectionChanged += TopuLoaderProfileHardFixLoaderChanged;

            // Stop legacy Save Profile Settings click handlers before Button.Click
            // is raised. We then save exactly what is visible in the UI.
            Button? save = FindButtonByContent(TabProfiles, "Save Profile Settings");
            if (save != null)
                save.PreviewMouseLeftButtonDown += TopuLoaderProfileHardFixSavePreview;

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    try
                    {
                        _ = RefreshDynamicVersionListAsync(GetSelectedVersion());
                    }
                    catch (Exception ex)
                    {
                        WriteException("PROFILE LIVE VERSION INITIALIZATION ERROR", ex);
                    }
                }),
                DispatcherPriority.ApplicationIdle);
        }

        private void TopuLoaderProfileHardFixLoaderChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_runtimeUiReady || e.OriginalSource != _loaderBox)
                return;

            string loader = _loaderBox?.SelectedItem?.ToString() ?? "Vanilla";

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    try
                    {
                        // Do not let the legacy hard-coded SetVersionChoices list
                        // become the final UI state.
                        _ = RefreshDynamicVersionListAsync(GetSelectedVersion());
                    }
                    catch (Exception ex)
                    {
                        WriteException("PROFILE LIVE VERSION REFRESH ERROR", ex);
                    }
                }),
                DispatcherPriority.ApplicationIdle);
        }

        private void TopuLoaderProfileHardFixSavePreview(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            SaveTopuProfileTruth();
        }

        private void SaveTopuProfileTruth()
        {
            try
            {
                if (_loaderBox == null)
                    return;

                string loader = _loaderBox.SelectedItem?.ToString() ?? "Vanilla";
                string version = GetSelectedVersion();
                int ram = Math.Clamp((int)Math.Round(RamSlider.Value), 2, 12);

                if (string.IsNullOrWhiteSpace(loader))
                    loader = "Vanilla";
                if (string.IsNullOrWhiteSpace(version))
                    throw new InvalidOperationException("Select a Minecraft version before saving the profile.");

                RuntimeProfileSettings profile = GetRuntimeProfile();
                profile.Loader = loader;
                profile.Version = version;
                profile.RamGb = ram;

                // Never call SetActiveProfile here: that would reload the old
                // JSON and undo the values the user just selected.
                WriteRuntimeProfile(profile);

                // Keep the existing profile/version/RAM persistence in sync too,
                // but do not write a loader default over our runtime profile.
                SaveProfileSettings(
                    _gamePath,
                    version,
                    ram);

                UpdateProfileCard();
                UpdateLaunchSummary();
                StatusText.Text = $"Saved {loader} profile: Minecraft {version}, {ram}GB RAM.";
                WriteLog($"PROFILE SAVED: loader={loader}; minecraft={version}; ram={ram}GB; path={_gamePath}");
            }
            catch (Exception ex)
            {
                WriteException("PROFILE HARD FIX SAVE ERROR", ex);
                MessageBox.Show(
                    "Could not save the profile.\n\n" + ex.Message,
                    "Topu Client",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
