using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TopuLauncher
{
    // Keeps the launch-tab summary and version catalog synchronized with the
    // active profile. This intentionally does not change the profile UI.
    public partial class MainWindow
    {
        private static readonly object TopuProfileCatalogBridgeRegistration =
            RegisterTopuProfileCatalogBridge();

        private bool _topuProfileCatalogBridgeReady;

        private static object RegisterTopuProfileCatalogBridge()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(TopuProfileCatalogBridgeLoaded));
            return new object();
        }

        private static void TopuProfileCatalogBridgeLoaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is MainWindow window)
                window.InitializeTopuProfileCatalogBridge();
        }

        private void InitializeTopuProfileCatalogBridge()
        {
            if (_topuProfileCatalogBridgeReady)
                return;

            _topuProfileCatalogBridgeReady = true;

            // Replace the XAML fallback list with the complete dynamic catalog.
            // The dynamic catalog queries the loader services and validates
            // stable loader support for Fabric/Quilt.
            _ = RefreshDynamicVersionListAsync(GetSelectedVersion());

            if (_loaderBox != null)
                _loaderBox.SelectionChanged += TopuProfileCatalogLoaderChanged;

            VersionBox.SelectionChanged += TopuProfileCatalogVersionChanged;
            RamSlider.ValueChanged += TopuProfileCatalogRamChanged;
            ProfileSelector.SelectionChanged += TopuProfileCatalogProfileChanged;

            Dispatcher.BeginInvoke(
                new Action(UpdateTopuLaunchCardFromProfile),
                DispatcherPriority.ApplicationIdle);
        }

        private void TopuProfileCatalogLoaderChanged(
            object? sender,
            SelectionChangedEventArgs e)
        {
            if (!_topuProfileCatalogBridgeReady || _loaderBox?.SelectedItem == null)
                return;

            _ = RefreshDynamicVersionListAsync(GetSelectedVersion());
            Dispatcher.BeginInvoke(
                new Action(UpdateTopuLaunchCardFromProfile),
                DispatcherPriority.DataBind);
        }

        private void TopuProfileCatalogVersionChanged(
            object? sender,
            SelectionChangedEventArgs e)
        {
            if (!_topuProfileCatalogBridgeReady)
                return;

            Dispatcher.BeginInvoke(
                new Action(UpdateTopuLaunchCardFromProfile),
                DispatcherPriority.DataBind);
        }

        private void TopuProfileCatalogRamChanged(
            object? sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_topuProfileCatalogBridgeReady)
                return;

            Dispatcher.BeginInvoke(
                new Action(UpdateTopuLaunchCardFromProfile),
                DispatcherPriority.DataBind);
        }

        private void TopuProfileCatalogProfileChanged(
            object? sender,
            SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != ProfileSelector)
                return;

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    RefreshLoaderUiFromProfile();
                    UpdateTopuLaunchCardFromProfile();
                    _ = RefreshDynamicVersionListAsync(GetRuntimeProfile().Version);
                }),
                DispatcherPriority.ApplicationIdle);
        }

        private void UpdateTopuLaunchCardFromProfile()
        {
            try
            {
                RuntimeProfileSettings profile = GetRuntimeProfile();

                string loader = string.IsNullOrWhiteSpace(profile.Loader)
                    ? (_loaderBox?.SelectedItem?.ToString() ?? "Vanilla")
                    : profile.Loader;

                string version = string.IsNullOrWhiteSpace(profile.Version)
                    ? GetSelectedVersion()
                    : profile.Version;

                int ram = profile.RamGb > 0
                    ? Math.Clamp(profile.RamGb, 2, 12)
                    : Math.Clamp((int)RamSlider.Value, 2, 12);

                if (LaunchProfileLabel != null)
                    LaunchProfileLabel.Text = GetActiveProfileName();

                if (LaunchLoaderLabel != null)
                    LaunchLoaderLabel.Text = loader;

                if (LaunchVersionLabel != null)
                    LaunchVersionLabel.Text = version;

                if (LaunchRamLabel != null)
                    LaunchRamLabel.Text = $"{ram}GB RAM";
            }
            catch (Exception ex)
            {
                WriteException("TOPU LAUNCH CARD SYNC ERROR", ex);
            }
        }
    }
}
