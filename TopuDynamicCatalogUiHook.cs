using System;
using System.Windows;
using System.Windows.Controls;

namespace TopuLauncher
{
    // Wires the already-implemented dynamic catalog into the existing
    // profile UI. This deliberately does not replace the profile system or
    // add another loader implementation.
    public partial class MainWindow
    {
        private bool _topuDynamicCatalogHooked;

        private void InstallDynamicCatalogUiHook()
        {
            if (_topuDynamicCatalogHooked || _loaderBox == null || VersionBox == null)
                return;

            _topuDynamicCatalogHooked = true;

            _loaderBox.SelectionChanged += TopuDynamicLoaderChanged;
            ProfileSelector.SelectionChanged += TopuDynamicProfileChanged;

            // Let the original profile/runtime initialization finish first.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                InitializeDynamicVersionCatalog();
                _ = RefreshDynamicVersionListAsync(GetSelectedVersion());
            }));
        }

        private void TopuDynamicLoaderChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_runtimeUiReady || _loaderBox == null)
                return;

            _ = RefreshDynamicVersionListAsync(GetSelectedVersion());
        }

        private void TopuDynamicProfileChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_runtimeUiReady || e.OriginalSource != ProfileSelector)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _ = RefreshDynamicVersionListAsync(GetSelectedVersion());
            }));
        }

        static MainWindow()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(InstallDynamicCatalogHookLoaded));
        }

        private static void InstallDynamicCatalogHookLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window)
                return;

            window.Dispatcher.BeginInvoke(new Action(window.InstallDynamicCatalogUiHook));
        }
    }
}
