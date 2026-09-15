using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TopuLauncher
{
    public partial class MainWindow
    {
        private static readonly object PerformanceRepairRegistration = RegisterPerformanceRepair();
        private bool _performanceRepairReady;

        private static object RegisterPerformanceRepair()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(PerformanceRepairLoaded));
            return new object();
        }

        private static void PerformanceRepairLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window)
                return;

            window.Dispatcher.BeginInvoke(new Action(window.ApplyPerformanceRepair));
        }

        private void ApplyPerformanceRepair()
        {
            if (_performanceRepairReady)
                return;

            _performanceRepairReady = true;

            // Replace the old Krypton slot with FerriteCore.
            // Fabric API stays because Dynamic FPS and Sodium Extra need it on
            // the older profiles this launcher supports.
            if (PerformanceMods.Length >= 6)
            {
                PerformanceMods[0] = ("fabric-api", "Fabric API");
                PerformanceMods[1] = ("sodium", "Sodium");
                PerformanceMods[2] = ("lithium", "Lithium");
                PerformanceMods[3] = ("dynamic-fps", "Dynamic FPS");
                PerformanceMods[4] = ("sodium-extra", "Sodium Extra");
                PerformanceMods[5] = ("ferrite-core", "FerriteCore");
            }

            if (_loaderBox != null)
                _loaderBox.SelectionChanged += PerformanceRepairLoaderChanged;

            if (ProfileSelector != null)
                ProfileSelector.SelectionChanged += PerformanceRepairProfileChanged;

            // The previous UI code locked the version selector as soon as any
            // versions folder existed. That makes an installed profile
            // permanently stuck on 1.21.1. Version is a profile setting, not a
            // one-time installation lock, so keep it editable.
            VersionBox.IsEnabled = true;

            // Replace the old non-Fabric preview handler. It read the saved
            // profile before the current UI selection could be committed.
            LaunchBtn.PreviewMouseLeftButtonDown -= LaunchPreview;
            LaunchBtn.AddHandler(
                Button.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(PerformanceRepairLaunchPreview),
                true);

            AppendExtraRuntimeVersions();
            ForceVersionEditorEnabled();
        }

        private void PerformanceRepairLoaderChanged(object? sender, SelectionChangedEventArgs e)
        {
            AppendExtraRuntimeVersions();
        }

        private void PerformanceRepairProfileChanged(object? sender, SelectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(ForceVersionEditorEnabled));
        }

        private void ForceVersionEditorEnabled()
        {
            if (VersionBox != null)
                VersionBox.IsEnabled = true;
        }

        private void AppendExtraRuntimeVersions()
        {
            if (_loaderBox == null || VersionBox == null)
                return;

            string loader = _loaderBox.SelectedItem?.ToString() ?? "";
            string current = GetSelectedVersion();

            if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
            {
                AddVersionIfMissing("1.20.6");
            }
            else if (loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
            {
                AddVersionIfMissing("1.17.1");
            }

            // Restore the saved/current version after adding the compatibility
            // versions instead of silently jumping back to the first item.
            int index = -1;
            for (int i = 0; i < VersionBox.Items.Count; i++)
            {
                if (string.Equals(
                        (VersionBox.Items[i] as ComboBoxItem)?.Content?.ToString(),
                        current,
                        StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
                VersionBox.SelectedIndex = index;

            VersionBox.IsEnabled = true;
        }

        private void AddVersionIfMissing(string version)
        {
            if (VersionBox == null)
                return;

            bool exists = VersionBox.Items
                .OfType<ComboBoxItem>()
                .Any(x => string.Equals(
                    x.Content?.ToString(),
                    version,
                    StringComparison.OrdinalIgnoreCase));

            if (!exists)
                VersionBox.Items.Add(new ComboBoxItem { Content = version });
        }

        private void SaveCurrentRuntimeBeforeLaunch()
        {
            string version = GetSelectedVersion();
            int ram = Math.Clamp((int)RamSlider.Value, 2, 12);
            string loader = _loaderBox?.SelectedItem?.ToString() ?? "Fabric";

            RuntimeProfileSettings runtime = GetRuntimeProfile();
            runtime.Loader = loader;
            runtime.Version = version;
            runtime.RamGb = ram;
            WriteRuntimeProfile(runtime);

            // MainWindow.LaunchBtn_Click calls SetActiveProfile(), which reloads
            // the older ProfileSettings file. Keep that file synchronized too,
            // otherwise it can put the UI straight back on 1.21.1.
            SaveProfileSettings(
                _gamePath,
                new ProfileSettings
                {
                    Version = version,
                    RamGb = ram
                });

            WriteLog($"PRE-LAUNCH PROFILE SYNC: Loader={loader}, Version={version}, RAM={ram}GB");
        }

        private async void PerformanceRepairLaunchPreview(object sender, MouseButtonEventArgs e)
        {
            string loader = _loaderBox?.SelectedItem?.ToString() ?? "Fabric";
            string version = GetSelectedVersion();

            // Java8RuntimeFix owns Forge 1.8.9. Do not start a second launch.
            if (loader.Equals("Forge", StringComparison.OrdinalIgnoreCase) &&
                version.Equals("1.8.9", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            e.Handled = true;

            try
            {
                SaveCurrentRuntimeBeforeLaunch();
                CleanupDuplicatePerformanceMods();

                if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                {
                    await EnsureExtraPerformanceModAsync("immediatelyfast", "ImmediatelyFast", version);

                    // Let the existing, already-tested Fabric installer perform
                    // the normal Minecraft/Fabric installation and its six-mod
                    // performance stack.
                    await LaunchBtn_ClickInternalForRepairAsync();
                    return;
                }

                if (loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
                {
                    await InstallQuiltPerformanceStackAsync(version);
                    await LaunchNonFabricProfileAsync();
                    return;
                }

                if (loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
                {
                    await LaunchNonFabricProfileAsync();
                    return;
                }

                // NeoForge is intentionally left on the existing runtime path
                // until its installer is implemented independently of Fabric.
                await LaunchNonFabricProfileAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Launch failed.";
                WriteException("PERFORMANCE REPAIR LAUNCH ERROR", ex);
                MessageBox.Show(
                    "Minecraft failed to launch.\n\n" + ex.Message + "\n\nLog:\n" + _logPath,
                    "Topu Client",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task LaunchBtn_ClickInternalForRepairAsync()
        {
            await Task.Yield();
            LaunchBtn_Click(LaunchBtn, new RoutedEventArgs(Button.ClickEvent));
        }

        private async Task EnsureExtraPerformanceModAsync(
            string slug,
            string name,
            string minecraftVersion)
        {
            RemoveModFamily(slug);

            try
            {
                StatusText.Text = $"Installing {name}...";
                bool installed = await DownloadPerformanceModAsync(
                    slug,
                    name,
                    minecraftVersion);

                if (!installed)
                    WriteLog($"Optional performance mod unavailable: {name} for {minecraftVersion}");
            }
            catch (Exception ex)
            {
                WriteLog($"Optional performance mod failed: {name} - {ex.Message}");
            }
        }

        private async Task InstallQuiltPerformanceStackAsync(string minecraftVersion)
        {
            WriteLog("===== QUILT PERFORMANCE STACK =====");

            foreach ((string slug, string name) in new[]
            {
                ("fabric-api", "Fabric API"),
                ("sodium", "Sodium"),
                ("lithium", "Lithium"),
                ("dynamic-fps", "Dynamic FPS"),
                ("sodium-extra", "Sodium Extra"),
                ("ferrite-core", "FerriteCore"),
                ("immediatelyfast", "ImmediatelyFast")
            })
            {
                RemoveModFamily(slug);

                try
                {
                    bool installed = await DownloadPerformanceModAsync(
                        slug,
                        name,
                        minecraftVersion);

                    WriteLog(
                        installed
                            ? $"Quilt performance mod ready: {name}"
                            : $"Quilt performance mod unavailable: {name}");
                }
                catch (Exception ex)
                {
                    WriteLog($"Quilt performance mod failed: {name} - {ex.Message}");
                }
            }
        }

        private void CleanupDuplicatePerformanceMods()
        {
            string modsFolder = Path.Combine(_gamePath, "mods");
            if (!Directory.Exists(modsFolder))
                return;

            string[] families =
            {
                "fabric-api",
                "sodium",
                "lithium",
                "dynamic-fps",
                "sodium-extra",
                "ferritecore",
                "immediatelyfast",
                "krypton"
            };

            foreach (string family in families)
                RemoveModFamily(family);
        }

        private void RemoveModFamily(string slug)
        {
            string modsFolder = Path.Combine(_gamePath, "mods");
            if (!Directory.Exists(modsFolder))
                return;

            string normalized = slug
                .Replace("-", "", StringComparison.OrdinalIgnoreCase)
                .ToLowerInvariant();

            foreach (string file in Directory.GetFiles(modsFolder, "*.jar"))
            {
                string name = Path.GetFileName(file)
                    .Replace("-", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("_", "", StringComparison.OrdinalIgnoreCase)
                    .ToLowerInvariant();

                if (!name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    File.Delete(file);
                    WriteLog($"Removed duplicate/old performance mod: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    WriteLog($"Could not remove duplicate mod {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
    }
}
