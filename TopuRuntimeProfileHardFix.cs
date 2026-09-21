using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TopuLauncher
{
    public partial class MainWindow
    {
        private static readonly object RuntimeProfileHardFixRegistration = RegisterRuntimeProfileHardFix();
        private bool _runtimeProfileHardFixReady;
        private bool _runtimeProfileHardFixSyncing;

        private static object RegisterRuntimeProfileHardFix()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(RuntimeProfileHardFixLoaded));
            EventManager.RegisterClassHandler(typeof(Button), Button.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(RuntimeProfileHardFixPreviewMouseDown), true);
            return new object();
        }

        private static void RuntimeProfileHardFixLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is MainWindow window)
                window.Dispatcher.BeginInvoke(new Action(window.InitializeRuntimeProfileHardFix), DispatcherPriority.ApplicationIdle);
        }

        private void InitializeRuntimeProfileHardFix()
        {
            if (_runtimeProfileHardFixReady) { QueueHardManifestSync(); return; }
            try
            {
                _runtimeProfileHardFixReady = true;
                EnsureHardFixNeoForgeSupport();
                QueueHardManifestSync();
            }
            catch (Exception ex) { WriteException("RUNTIME PROFILE HARD FIX INITIALIZATION ERROR", ex); }
        }

        private static void RuntimeProfileHardFixPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button button || !string.Equals(button.Content?.ToString(), "Save Profile Settings", StringComparison.OrdinalIgnoreCase)) return;
            if (Window.GetWindow(button) is not MainWindow window || !window._runtimeProfileHardFixReady) return;
            e.Handled = true;
            window.SaveProfileManifestFromUi();
        }

        private void EnsureHardFixNeoForgeSupport()
        {
            if (_loaderBox == null) return;
            if (!_loaderBox.Items.Contains("NeoForge")) _loaderBox.Items.Add("NeoForge");
            _loaderBox.SelectionChanged -= HardFixLoaderChanged;
            _loaderBox.SelectionChanged += HardFixLoaderChanged;
        }

        private void HardFixLoaderChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_runtimeProfileHardFixSyncing || _loaderBox?.SelectedItem == null) return;
            string loader = NormalizeHardLoader(_loaderBox.SelectedItem.ToString());
            string currentVersion = GetSelectedVersion();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_runtimeProfileHardFixSyncing) return;
                SetHardVersionChoices(loader, currentVersion);
                UpdateHardFixLabels(loader, GetSelectedVersion(), Math.Clamp((int)RamSlider.Value, 2, 12));
            }), DispatcherPriority.ContextIdle);
        }

        private void QueueHardManifestSync() => Dispatcher.BeginInvoke(new Action(SyncHardManifestToUi), DispatcherPriority.ContextIdle);

        private void SyncHardManifestToUi()
        {
            if (_runtimeProfileHardFixSyncing || string.IsNullOrWhiteSpace(_gamePath) || _loaderBox == null) return;
            _runtimeProfileHardFixSyncing = true;
            try
            {
                EnsureHardFixNeoForgeSupport();
                HardManifest manifest = ReadHardManifest(_gamePath);
                string loader = NormalizeHardLoader(manifest.Loader);
                _loaderBox.SelectedItem = loader;
                SetHardVersionChoices(loader, manifest.Version);
                int ram = Math.Clamp(manifest.RamGb, 2, 12);
                RamSlider.Value = ram;
                RamLabel.Text = $"{ram}GB";
                UpdateHardFixLabels(loader, manifest.Version, ram);
            }
            catch (Exception ex) { WriteException("RUNTIME PROFILE HARD SYNC ERROR", ex); }
            finally { _runtimeProfileHardFixSyncing = false; }
        }

        private void SetHardVersionChoices(string loader, string preferred)
        {
            string[] versions = loader switch
            {
                "Vanilla" => RuntimeVanillaVersions,
                "Fabric" => RuntimeFabricVersions,
                "Forge" => RuntimeForgeVersions,
                "Quilt" => RuntimeQuiltVersions,
                "NeoForge" => RuntimeNeoForgeVersions,
                _ => RuntimeVanillaVersions
            };
            VersionBox.Items.Clear();
            foreach (string version in versions) VersionBox.Items.Add(new ComboBoxItem { Content = version });
            int index = Array.FindIndex(versions, x => string.Equals(x, preferred, StringComparison.OrdinalIgnoreCase));
            VersionBox.SelectedIndex = index >= 0 ? index : 0;
        }

        private void SaveProfileManifestFromUi()
        {
            try
            {
                string loader = NormalizeHardLoader(_loaderBox?.SelectedItem?.ToString());
                string version = GetSelectedVersion();
                if (string.IsNullOrWhiteSpace(version)) version = DefaultVersion;
                int ram = Math.Clamp((int)RamSlider.Value, 2, 12);
                HardManifest old = ReadHardManifest(_gamePath);
                old.Loader = loader;
                old.Version = version;
                old.RamGb = ram;
                Directory.CreateDirectory(_gamePath);
                string path = GetProfileSettingsPath(_gamePath);
                File.WriteAllText(path, JsonSerializer.Serialize(old, new JsonSerializerOptions { WriteIndented = true }));
                UpdateHardFixLabels(loader, version, ram);
                StatusText.Text = $"Saved {loader} {version} • {ram}GB RAM";
                WriteLog($"PROFILE SETTINGS SAVED: Loader={loader}, Version={version}, RAM={ram}GB, Path={path}");
            }
            catch (Exception ex)
            {
                WriteException("RUNTIME PROFILE SAVE ERROR", ex);
                MessageBox.Show(this, "Could not save profile settings.\n\n" + ex.Message, "Topu Client", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private HardManifest ReadHardManifest(string gamePath)
        {
            HardManifest manifest = new HardManifest();
            string path = GetProfileSettingsPath(gamePath);
            try
            {
                if (!File.Exists(path)) return manifest;
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = doc.RootElement;
                manifest.Loader = GetHardString(root, "Loader") ?? GetHardString(root, "loader") ?? "Vanilla";
                manifest.Version = GetHardString(root, "Version") ?? GetHardString(root, "version") ?? DefaultVersion;
                manifest.RamGb = GetHardInt(root, "RamGb", GetHardInt(root, "ramGb", 4));
                manifest.ForgeVersion = GetHardString(root, "ForgeVersion") ?? GetHardString(root, "forgeVersion") ?? "";
            }
            catch (Exception ex) { WriteException("RUNTIME PROFILE JSON READ ERROR", ex); }
            manifest.Loader = NormalizeHardLoader(manifest.Loader);
            manifest.RamGb = Math.Clamp(manifest.RamGb, 2, 12);
            return manifest;
        }

        private void UpdateHardFixLabels(string loader, string version, int ram)
        {
            string profile = GetActiveProfileName();
            if (SelectedProfileLabel != null) SelectedProfileLabel.Text = $"● {profile}   •   {loader} {version}   •   {ram}GB RAM";
            if (LaunchVersionLabel != null) LaunchVersionLabel.Text = version;
            if (LaunchRamLabel != null) LaunchRamLabel.Text = $"{ram}GB RAM";
            if (ManagementVersionLabel != null) ManagementVersionLabel.Text = version;
            if (LaunchBtn != null) LaunchBtn.Content = $"⚡   LAUNCH {loader.ToUpperInvariant()}";
        }

        private static string NormalizeHardLoader(string? loader)
        {
            if (string.Equals(loader, "Fabric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
            if (string.Equals(loader, "Forge", StringComparison.OrdinalIgnoreCase)) return "Forge";
            if (string.Equals(loader, "Quilt", StringComparison.OrdinalIgnoreCase)) return "Quilt";
            if (string.Equals(loader, "NeoForge", StringComparison.OrdinalIgnoreCase)) return "NeoForge";
            return "Vanilla";
        }

        private static string? GetHardString(JsonElement root, string property) =>
            root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static int GetHardInt(JsonElement root, string property, int fallback)
        {
            if (!root.TryGetProperty(property, out JsonElement value)) return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)) return number;
            return fallback;
        }

        private sealed class HardManifest
        {
            public string Loader { get; set; } = "Vanilla";
            public string Version { get; set; } = "1.21.1";
            public int RamGb { get; set; } = 4;
            public string ForgeVersion { get; set; } = "";
        }
    }
}
