using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TopuLauncher
{
    // Keeps the visible launch/profile UI synchronized with topu-profile.json.
    // Also removes the old Fabric-only presentation and adds NeoForge everywhere
    // the loader selector is presented.
    public partial class MainWindow
    {
        private static readonly object LoaderUiFixRegistration = RegisterLoaderUiFix();
        private bool _loaderUiFixHooked;

        private static readonly string[] NeoForgeUiVersions =
        {
            "1.21.1", "1.21.4", "1.21.8", "1.21.11", "26.1.2", "26.2"
        };

        private static object RegisterLoaderUiFix()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(LoaderUiFixLoaded));
            EventManager.RegisterClassHandler(typeof(ComboBox), FrameworkElement.LoadedEvent, new RoutedEventHandler(LoaderComboLoaded), true);
            EventManager.RegisterClassHandler(typeof(ComboBox), Selector.SelectionChangedEvent, new SelectionChangedEventHandler(LoaderComboSelectionChanged), true);
            return new object();
        }

        private static void LoaderUiFixLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is MainWindow window)
                window.Dispatcher.BeginInvoke(new Action(window.InstallLoaderUiFix));
        }

        private static bool IsLoaderCombo(ComboBox combo)
        {
            return combo.Items.Cast<object>().Select(x => x?.ToString() ?? string.Empty).Any(x => x.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
                && combo.Items.Cast<object>().Select(x => x?.ToString() ?? string.Empty).Any(x => x.Equals("Fabric", StringComparison.OrdinalIgnoreCase));
        }

        private static void LoaderComboLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox combo || !IsLoaderCombo(combo)) return;
            if (!combo.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), "NeoForge", StringComparison.OrdinalIgnoreCase)))
                combo.Items.Add("NeoForge");
        }

        private static void LoaderComboSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox combo || !IsLoaderCombo(combo)) return;
            if (!string.Equals(combo.SelectedItem?.ToString(), "NeoForge", StringComparison.OrdinalIgnoreCase)) return;

            combo.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Window? owner = Window.GetWindow(combo);
                    if (owner == null) return;
                    ComboBox? versionCombo = FindVersionCombo(owner, combo);
                    if (versionCombo == null) return;
                    versionCombo.ItemsSource = NeoForgeUiVersions;
                    versionCombo.SelectedIndex = 0;
                }
                catch { }
            }));
        }

        private static ComboBox? FindVersionCombo(DependencyObject root, ComboBox loaderCombo)
        {
            foreach (DependencyObject child in FindVisualChildren(root))
            {
                if (child is not ComboBox combo || ReferenceEquals(combo, loaderCombo)) continue;
                if (combo.ItemsSource is IEnumerable<string> source && source.Any(x => x.Equals("1.8.9", StringComparison.OrdinalIgnoreCase) || x.Equals("1.21.1", StringComparison.OrdinalIgnoreCase)))
                    return combo;
            }
            return null;
        }

        private static IEnumerable<DependencyObject> FindVisualChildren(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (DependencyObject nested in FindVisualChildren(child)) yield return nested;
            }
        }

        private void InstallLoaderUiFix()
        {
            if (_loaderUiFixHooked) return;
            _loaderUiFixHooked = true;

            if (_loaderBox != null)
            {
                if (!_loaderBox.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), "NeoForge", StringComparison.OrdinalIgnoreCase)))
                    _loaderBox.Items.Add("NeoForge");
                _loaderBox.SelectionChanged += LoaderUiFix_SelectionChanged;
            }

            if (ProfileSelector != null)
                ProfileSelector.SelectionChanged += LoaderUiFix_ProfileChanged;

            TabProfiles.AddHandler(Button.ClickEvent, new RoutedEventHandler(LoaderUiFix_ButtonClicked), true);
            ApplyLoaderUiFix();
        }

        private void LoaderUiFix_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_runtimeUiReady || e.OriginalSource != _loaderBox) return;
            Dispatcher.BeginInvoke(new Action(ApplyLoaderUiFix));
        }

        private void LoaderUiFix_ProfileChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_runtimeUiReady || e.OriginalSource != ProfileSelector) return;
            string profile = ProfileSelector.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(profile)) return;

            try
            {
                SetActiveProfile(profile);
                ApplyLoaderUiFix();
            }
            catch (Exception ex) { WriteException("LOADER PROFILE SWITCH ERROR", ex); }
        }

        private void LoaderUiFix_ButtonClicked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not Button button || !string.Equals(button.Content?.ToString(), "Save Profile Settings", StringComparison.OrdinalIgnoreCase)) return;
            Dispatcher.BeginInvoke(new Action(ApplyLoaderUiFix));
        }

        private void ApplyLoaderUiFix()
        {
            try
            {
                RuntimeProfileSettings settings = GetRuntimeProfile();
                string loader = settings.Loader;
                if (!IsKnownLoaderName(loader)) loader = "Vanilla";

                string version = string.IsNullOrWhiteSpace(settings.Version) ? "1.21.1" : settings.Version;
                int ram = Math.Clamp(settings.RamGb, 2, 12);

                if (_loaderBox != null && !string.Equals(_loaderBox.SelectedItem?.ToString(), loader, StringComparison.OrdinalIgnoreCase))
                    _loaderBox.SelectedItem = loader;

                if (loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
                {
                    VersionBox.Items.Clear();
                    foreach (string item in NeoForgeUiVersions)
                        VersionBox.Items.Add(new ComboBoxItem { Content = item });
                    int selected = Array.IndexOf(NeoForgeUiVersions, version);
                    VersionBox.SelectedIndex = selected >= 0 ? selected : 0;
                }

                if (LaunchVersionLabel != null) LaunchVersionLabel.Text = version;
                if (LaunchRamLabel != null) LaunchRamLabel.Text = $"{ram}GB RAM";
                if (LaunchBtn != null)
                {
                    LaunchBtn.Content = $"⚡   LAUNCH {loader.ToUpperInvariant()}";
                    LaunchBtn.ToolTip = $"Launch {loader} {version} with {ram}GB RAM";
                }
                if (LaunchProfileLabel != null) LaunchProfileLabel.Text = GetActiveProfileName();

                if (LaunchProfileLabel?.Parent is StackPanel profilePanel)
                {
                    foreach (UIElement child in profilePanel.Children)
                    {
                        if (child is not StackPanel details) continue;
                        foreach (UIElement detail in details.Children)
                        {
                            if (detail is TextBlock text && text != LaunchVersionLabel && text != LaunchRamLabel && text.Text != "  •  " && IsKnownLoaderName(text.Text))
                            {
                                text.Text = loader;
                                break;
                            }
                        }
                    }
                }

                if (SelectedProfileLabel != null)
                    SelectedProfileLabel.Text = $"● {GetActiveProfileName()}   •   {loader} {version}   •   {ram}GB RAM";

                SetSidebarLoaderText(loader);
            }
            catch (Exception ex) { WriteException("LOADER UI UPDATE ERROR", ex); }
        }

        private static bool IsKnownLoaderName(string? value)
        {
            return value != null && (value.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Fabric", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Forge", StringComparison.OrdinalIgnoreCase)
                || value.Equals("NeoForge", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Quilt", StringComparison.OrdinalIgnoreCase));
        }

        private void SetSidebarLoaderText(string loader)
        {
            foreach (TextBlock text in FindTextBlocks(this))
            {
                if (text.Text.Equals("Fabric PvP Launcher", StringComparison.OrdinalIgnoreCase)
                    || text.Text.Equals("Multi-Loader Launcher", StringComparison.OrdinalIgnoreCase)
                    || text.Text.Equals("Forge PvP Launcher", StringComparison.OrdinalIgnoreCase)
                    || text.Text.Equals("NeoForge PvP Launcher", StringComparison.OrdinalIgnoreCase)
                    || text.Text.Equals("Quilt PvP Launcher", StringComparison.OrdinalIgnoreCase)
                    || text.Text.Equals("Vanilla PvP Launcher", StringComparison.OrdinalIgnoreCase))
                {
                    text.Text = loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ? "Vanilla Minecraft" : $"{loader} PvP Launcher";
                    return;
                }
            }
        }

        private static IEnumerable<TextBlock> FindTextBlocks(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock text) yield return text;
                foreach (TextBlock nested in FindTextBlocks(child)) yield return nested;
            }
        }
    }
}
