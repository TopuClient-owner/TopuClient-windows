using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace TopuLauncher
{
    // Bridges the existing dynamic catalog into the existing profile UI
    // without creating a second profile/loader implementation.
    internal static class TopuDynamicCatalogUiHook
    {
        static TopuDynamicCatalogUiHook()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnMainWindowLoaded));

            EventManager.RegisterClassHandler(
                typeof(ComboBox),
                Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler(OnComboBoxSelectionChanged));
        }

        private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window)
                return;

            // MainWindow.LoaderRuntime creates the loader selector and restores
            // the active profile first. Refresh the catalog afterwards.
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                InvokePrivate(window, "InitializeDynamicVersionCatalog");
            }));
        }

        private static void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox combo ||
                Window.GetWindow(combo) is not MainWindow window)
                return;

            FieldInfo? loaderField = typeof(MainWindow).GetField(
                "_loaderBox",
                BindingFlags.Instance | BindingFlags.NonPublic);

            FieldInfo? profileField = typeof(MainWindow).GetField(
                "ProfileSelector",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            object? loaderBox = loaderField?.GetValue(window);
            object? profileSelector = profileField?.GetValue(window);

            if (!ReferenceEquals(combo, loaderBox) &&
                !ReferenceEquals(combo, profileSelector))
                return;

            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    MethodInfo? getSelectedVersion = typeof(MainWindow).GetMethod(
                        "GetSelectedVersion",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    MethodInfo? refresh = typeof(MainWindow).GetMethod(
                        "RefreshDynamicVersionListAsync",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    string? preferred = getSelectedVersion?.Invoke(window, null) as string;
                    _ = refresh?.Invoke(window, new object?[] { preferred }) as Task;
                }
                catch
                {
                    // Optional metadata refresh must never break the launcher.
                }
            }));
        }

        private static void InvokePrivate(MainWindow window, string methodName)
        {
            try
            {
                MethodInfo? method = typeof(MainWindow).GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic);

                method?.Invoke(window, null);
            }
            catch
            {
                // Keep the existing launcher usable if metadata is unavailable.
            }
        }
    }
}
