using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;

namespace TopuLauncher
{
    // Keeps the visual launcher synchronized with the real per-profile
    // topu-profile.json manifest. This deliberately lives outside the large
    // MainWindow.xaml.cs so the existing launcher logic is not replaced.
    public partial class MainWindow
    {
        private static readonly object ProfileManifestBridgeRegistration = RegisterProfileManifestBridge();
        private bool _profileManifestBridgeReady;
        private bool _profileManifestSyncing;
        private bool _profileManifestLoaderHandlerAttached;

        private static object RegisterProfileManifestBridge()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(ProfileManifestBridgeLoaded));
            return new object();
        }

        private static void ProfileManifestBridgeLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is MainWindow window)
            {
                window.Dispatcher.BeginInvoke(
                    new Action(window.InitializeProfileManifestBridge),
                    DispatcherPriority.ContextIdle);
            }
        }

        private void InitializeProfileManifestBridge()
        {
            if (_profileManifestBridgeReady)
            {
                QueueProfileManifestSync();
                return;
            }

            try
            {
                EnsureLoaderSelector();

                if (TabProfiles != null)
                    TabProfiles.AddHandler(Button.ClickEvent, ProfileManifestButtonClicked, true);

                if (ProfileSelector != null)
                    ProfileSelector.SelectionChanged += ProfileManifestProfileChanged;

                EnsureProfileManifestLoaderHandler();
                _profileManifestBridgeReady = true;
                QueueProfileManifestSync();
            }
            catch (Exception ex)
            {
                WriteException("PROFILE MANIFEST BRIDGE INITIALIZATION ERROR", ex);
            }
        }

        private void EnsureLoaderSelector()
        {
            if (TabProfiles == null)
                return;

            if (_loaderBox == null)
            {
                _loaderBox = new ComboBox
                {
                    Height = 36,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Style = FindResource("Combo") as Style
                };
            }

            string[] loaders = { "Vanilla", "Fabric", "Forge", "Quilt", "NeoForge" };
            foreach (string loader in loaders)
            {
                bool exists = false;
                foreach (object item in _loaderBox.Items)
                {
                    if (string.Equals(item?.ToString(), loader, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    _loaderBox.Items.Add(loader);
            }

            if (_loaderBox.Style == null)
                _loaderBox.Style = FindResource("Combo") as Style;

            EnsureProfileManifestLoaderHandler();

            if (!IsDescendantOf(_loaderBox, TabProfiles))
            {
                Border card = new Border
                {
                    Background = FindResource("CardBackground") as Brush ?? new SolidColorBrush(Color.FromRgb(25, 27, 32)),
                    BorderBrush = FindResource("CardBorder") as Brush ?? new SolidColorBrush(Color.FromRgb(45, 48, 55)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(16),
                    Margin = new Thickness(0, 0, 0, 14)
                };

                StackPanel stack = new StackPanel();
                stack.Children.Add(new TextBlock
                {
                    Text = "MOD LOADER",
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 156, 166)),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 8)
                });
                stack.Children.Add(_loaderBox);
                stack.Children.Add(new TextBlock
                {
                    Text = "Choose the loader for this profile. The Minecraft version list changes automatically.",
                    Foreground = FindResource("MutedText") as Brush ?? Brushes.Gray,
                    FontSize = 10,
                    Margin = new Thickness(0, 8, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                card.Child = stack;
                TabProfiles.Children.Insert(Math.Min(2, TabProfiles.Children.Count), card);
            }
        }

        private void EnsureProfileManifestLoaderHandler()
        {
            if (_loaderBox == null || _profileManifestLoaderHandlerAttached)
                return;

            _loaderBox.SelectionChanged += ProfileManifestLoaderChanged;
            _profileManifestLoaderHandlerAttached = true;
        }

        private void ProfileManifestLoaderChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_profileManifestBridgeReady || _profileManifestSyncing || _loaderBox?.SelectedItem == null)
                return;

            string loader = NormalizeLoader(_loaderBox.SelectedItem.ToString());
            string currentVersion = GetSelectedVersion();
            SetRuntimeVersionChoicesWithoutLosingManifest(loader, currentVersion);
            UpdateRuntimeLabels(loader, GetSelectedVersion(), Math.Clamp((int)RamSlider.Value, 2, 12));
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
        {
            DependencyObject? current = child;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void ProfileManifestProfileChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_profileManifestBridgeReady || e.OriginalSource != ProfileSelector)
                return;
            QueueProfileManifestSync();
        }

        private void ProfileManifestButtonClicked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not Button button ||
                !string.Equals(button.Content?.ToString(), "Save Profile Settings", StringComparison.OrdinalIgnoreCase))
                return;

            // Run after the existing SaveProfile_Click and runtime handler so
            // neither legacy ProfileSettings nor the runtime bridge can erase
            // Loader from the unified manifest.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    SaveUnifiedProfileManifest();
                    QueueProfileManifestSync();
                }
                catch (Exception ex)
                {
                    WriteException("PROFILE MANIFEST SAVE ERROR", ex);
                }
            }), DispatcherPriority.ContextIdle);
        }

        private void QueueProfileManifestSync()
        {
            Dispatcher.BeginInvoke(new Action(SyncProfileManifestToUi), DispatcherPriority.ContextIdle);
        }

        private void SyncProfileManifestToUi()
        {
            if (_profileManifestSyncing || string.IsNullOrWhiteSpace(_gamePath))
                return;

            _profileManifestSyncing = true;
            try
            {
                EnsureLoaderSelector();
                RuntimeManifestValues values = ReadUnifiedProfileManifest(_gamePath);

                string loader = NormalizeLoader(values.Loader);
                _loaderBox!.SelectedItem = loader;

                SetRuntimeVersionChoicesWithoutLosingManifest(loader, values.Version);

                int ram = Math.Clamp(values.RamGb, 2, 12);
                RamSlider.Value = ram;
                RamLabel.Text = $"{ram}GB";

                UpdateRuntimeLabels(loader, values.Version, ram);
            }
            catch (Exception ex)
            {
                WriteException("PROFILE MANIFEST UI SYNC ERROR", ex);
            }
            finally
            {
                _profileManifestSyncing = false;
            }
        }

        private void SetRuntimeVersionChoicesWithoutLosingManifest(string loader, string version)
        {
            string[] versions = loader switch
            {
                "Vanilla" => RuntimeVanillaVersions,
                "Forge" => RuntimeForgeVersions,
                "Quilt" => RuntimeQuiltVersions,
                "NeoForge" => RuntimeNeoForgeVersions,
                _ => RuntimeFabricVersions
            };

            VersionBox.Items.Clear();
            foreach (string item in versions)
                VersionBox.Items.Add(new ComboBoxItem { Content = item });

            int index = Array.FindIndex(versions, x => string.Equals(x, version, StringComparison.OrdinalIgnoreCase));
            VersionBox.SelectedIndex = index >= 0 ? index : 0;
        }

        private void UpdateRuntimeLabels(string loader, string version, int ram)
        {
            string profile = GetActiveProfileName();
            string summary = $"● {profile}   •   {loader} {version}   •   {ram}GB RAM";

            if (SelectedProfileLabel != null)
                SelectedProfileLabel.Text = summary;
            if (LaunchVersionLabel != null)
                LaunchVersionLabel.Text = version;
            if (LaunchRamLabel != null)
                LaunchRamLabel.Text = $"{ram}GB RAM";
            if (ManagementVersionLabel != null)
                ManagementVersionLabel.Text = version;
            if (LaunchBtn != null)
                LaunchBtn.Content = $"⚡   LAUNCH {loader.ToUpperInvariant()}";
        }

        private void SaveUnifiedProfileManifest()
        {
            if (string.IsNullOrWhiteSpace(_gamePath))
                return;

            RuntimeManifestValues current = ReadUnifiedProfileManifest(_gamePath);
            current.Loader = NormalizeLoader(_loaderBox?.SelectedItem?.ToString() ?? current.Loader);
            current.Version = GetSelectedVersion();
            current.RamGb = Math.Clamp((int)RamSlider.Value, 2, 12);

            Directory.CreateDirectory(_gamePath);
            File.WriteAllText(
                GetProfileSettingsPath(_gamePath),
                JsonSerializer.Serialize(
                    new
                    {
                        Loader = current.Loader,
                        Version = current.Version,
                        RamGb = current.RamGb,
                        ForgeVersion = current.ForgeVersion
                    },
                    new JsonSerializerOptions { WriteIndented = true }));

            WriteLog($"Unified profile manifest saved: Loader={current.Loader}, Version={current.Version}, RAM={current.RamGb}GB, Path={_gamePath}");
        }

        private RuntimeManifestValues ReadUnifiedProfileManifest(string gamePath)
        {
            RuntimeManifestValues values = new RuntimeManifestValues();
            string path = GetProfileSettingsPath(gamePath);

            try
            {
                if (!File.Exists(path))
                {
                    SaveInitialUnifiedManifest(path, values);
                    return values;
                }

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = document.RootElement;

                values.Loader = ReadString(root, "Loader") ?? ReadString(root, "loader") ?? "Vanilla";
                values.Version = ReadString(root, "Version") ?? ReadString(root, "version") ?? DefaultVersion;
                values.RamGb = ReadInt(root, "RamGb", ReadInt(root, "ramGb", 4));
                values.ForgeVersion = ReadString(root, "ForgeVersion") ?? ReadString(root, "forgeVersion") ?? "";
            }
            catch (Exception ex)
            {
                WriteException("PROFILE MANIFEST READ ERROR", ex);
            }

            values.Loader = NormalizeLoader(values.Loader);
            if (string.IsNullOrWhiteSpace(values.Version))
                values.Version = DefaultVersion;
            values.RamGb = Math.Clamp(values.RamGb, 2, 12);
            return values;
        }

        private void SaveInitialUnifiedManifest(string path, RuntimeManifestValues values)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(
                    path,
                    JsonSerializer.Serialize(
                        new { Loader = values.Loader, Version = values.Version, RamGb = values.RamGb, ForgeVersion = "" },
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                WriteException("INITIAL PROFILE MANIFEST SAVE ERROR", ex);
            }
        }

        private static string? ReadString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static int ReadInt(JsonElement root, string name, int fallback)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
                return number;
            return fallback;
        }

        private static string NormalizeLoader(string? loader)
        {
            if (string.Equals(loader, "Fabric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
            if (string.Equals(loader, "Forge", StringComparison.OrdinalIgnoreCase)) return "Forge";
            if (string.Equals(loader, "Quilt", StringComparison.OrdinalIgnoreCase)) return "Quilt";
            if (string.Equals(loader, "NeoForge", StringComparison.OrdinalIgnoreCase)) return "NeoForge";
            return "Vanilla";
        }

        private sealed class RuntimeManifestValues
        {
            public string Loader { get; set; } = "Vanilla";
            public string Version { get; set; } = "1.21.1";
            public int RamGb { get; set; } = 4;
            public string ForgeVersion { get; set; } = "";
        }
    }
}
