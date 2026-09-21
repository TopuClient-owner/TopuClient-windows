using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TopuLauncher
{
    public partial class MainWindow
    {
        private static readonly object ProfileDisplayFixRegistration = RegisterProfileDisplayFix();

        private static object RegisterProfileDisplayFix()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(ProfileDisplayFixLoaded));
            EventManager.RegisterClassHandler(typeof(ComboBox), ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(ProfileDisplayLoaderChanged), true);
            return new object();
        }

        private static void ProfileDisplayFixLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is MainWindow window)
                window.Dispatcher.BeginInvoke(new Action(window.RefreshProfileDisplayFix), DispatcherPriority.ApplicationIdle);
        }

        private static void ProfileDisplayLoaderChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox combo || Window.GetWindow(combo) is not MainWindow window || combo != window._loaderBox)
                return;
            window.Dispatcher.BeginInvoke(new Action(window.RefreshProfileDisplayFix), DispatcherPriority.ContextIdle);
        }

        private void RefreshProfileDisplayFix()
        {
            try
            {
                RuntimeDisplayManifest manifest = ReadDisplayManifest(_gamePath);
                string loader = NormalizeDisplayLoader(manifest.Loader);
                string version = string.IsNullOrWhiteSpace(manifest.Version) ? DefaultVersion : manifest.Version;
                int ram = Math.Clamp(manifest.RamGb, 2, 12);
                string profile = GetActiveProfileName();

                if (LaunchLoaderLabel != null) LaunchLoaderLabel.Text = loader;
                if (LaunchVersionLabel != null) LaunchVersionLabel.Text = version;
                if (LaunchRamLabel != null) LaunchRamLabel.Text = $"{ram}GB RAM";
                if (LaunchProfileLabel != null) LaunchProfileLabel.Text = profile;
                if (SidebarProfileLabel != null) SidebarProfileLabel.Text = profile;
                if (SidebarRuntimeLabel != null) SidebarRuntimeLabel.Text = $"{loader} {version} • {ram}GB RAM";
                if (ManagementVersionLabel != null) ManagementVersionLabel.Text = version;
                if (SelectedProfileLabel != null) SelectedProfileLabel.Text = $"● {profile}   •   {loader} {version}   •   {ram}GB RAM";
                if (LaunchBtn != null) LaunchBtn.Content = $"⚡   LAUNCH {loader.ToUpperInvariant()}";
            }
            catch (Exception ex)
            {
                WriteException("PROFILE DISPLAY FIX ERROR", ex);
            }
        }

        private RuntimeDisplayManifest ReadDisplayManifest(string gamePath)
        {
            RuntimeDisplayManifest result = new RuntimeDisplayManifest();
            try
            {
                HardManifestDisplayRead(gamePath, result);
            }
            catch (Exception ex)
            {
                WriteException("PROFILE DISPLAY JSON ERROR", ex);
            }
            return result;
        }

        private void HardManifestDisplayRead(string gamePath, RuntimeDisplayManifest result)
        {
            string path = GetProfileSettingsPath(gamePath);
            if (!System.IO.File.Exists(path)) return;
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            System.Text.Json.JsonElement root = doc.RootElement;
            if (root.TryGetProperty("Loader", out var l) && l.ValueKind == System.Text.Json.JsonValueKind.String) result.Loader = l.GetString() ?? result.Loader;
            else if (root.TryGetProperty("loader", out l) && l.ValueKind == System.Text.Json.JsonValueKind.String) result.Loader = l.GetString() ?? result.Loader;
            if (root.TryGetProperty("Version", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String) result.Version = v.GetString() ?? result.Version;
            else if (root.TryGetProperty("version", out v) && v.ValueKind == System.Text.Json.JsonValueKind.String) result.Version = v.GetString() ?? result.Version;
            if (root.TryGetProperty("RamGb", out var r) && r.TryGetInt32(out int n)) result.RamGb = n;
            else if (root.TryGetProperty("ramGb", out r) && r.TryGetInt32(out n)) result.RamGb = n;
        }

        private static string NormalizeDisplayLoader(string? loader)
        {
            if (string.Equals(loader, "Fabric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
            if (string.Equals(loader, "Forge", StringComparison.OrdinalIgnoreCase)) return "Forge";
            if (string.Equals(loader, "Quilt", StringComparison.OrdinalIgnoreCase)) return "Quilt";
            if (string.Equals(loader, "NeoForge", StringComparison.OrdinalIgnoreCase)) return "NeoForge";
            return "Vanilla";
        }

        private sealed class RuntimeDisplayManifest
        {
            public string Loader { get; set; } = "Vanilla";
            public string Version { get; set; } = "1.21.1";
            public int RamGb { get; set; } = 4;
        }
    }
}
