using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TopuLauncher
{
    public partial class MainWindow
    {
        private static readonly string[] FabricMods = { "fabric-api", "sodium", "sodium-extra", "lithium", "dynamic-fps", "ferrite-core", "immediatelyfast", "krypton" };
        private static readonly string[] QuiltMods = { "qsl", "sodium", "sodium-extra", "lithium", "dynamic-fps", "ferrite-core", "immediatelyfast" };
        private static readonly string[] ForgeMods = { "embeddium", "ferrite-core", "modernfix", "immediatelyfast", "dynamic-fps", "entityculling" };
        private static readonly string[] NeoForgeMods = { "sodium", "sodium-extra", "lithium", "ferrite-core", "modernfix", "immediatelyfast", "dynamic-fps", "entityculling" };
        private static readonly string[] Forge189Mods = { "foamfix" };
        private static readonly string[] KnownMods = { "fabric-api", "qsl", "sodium", "sodium-extra", "lithium", "dynamic-fps", "ferrite-core", "immediatelyfast", "krypton", "embeddium", "modernfix", "entityculling", "foamfix" };

        private CancellationTokenSource? _performanceCts;
        private string _performanceReadyKey = "";
        private bool _performanceHooksInstalled;
        private bool _performanceReplayingLaunch;
        private static readonly object PerformanceRegistration = RegisterPerformanceHandlers();

        private static object RegisterPerformanceHandlers()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(PerformanceLoaded));
            EventManager.RegisterClassHandler(typeof(Button), Button.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(PerformanceLaunchPreview), true);
            return new object();
        }

        private static void PerformanceLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is MainWindow window)
                window.Dispatcher.BeginInvoke(new Action(window.InstallPerformanceHooks), DispatcherPriority.ApplicationIdle);
        }

        private void InstallPerformanceHooks()
        {
            if (_performanceHooksInstalled || _loaderBox == null) return;
            _universalVersionCts?.Cancel();
            _universalPerformanceCts?.Cancel();
            _loaderBox.SelectionChanged -= UniversalLoaderSelectionChanged;
            VersionBox.SelectionChanged -= UniversalVersionSelectionChanged;
            _performanceHooksInstalled = true;
            _loaderBox.SelectionChanged += PerformanceLoaderChanged;
            VersionBox.SelectionChanged += PerformanceVersionChanged;
        }

        private async void PerformanceLoaderChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_performanceHooksInstalled || _loaderBox?.SelectedItem == null) return;
            _performanceReadyKey = "";
            string loader = _loaderBox.SelectedItem.ToString() ?? "Vanilla";
            if (!loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)) await PreparePerformanceAsync(loader, GetSelectedVersion());
        }

        private async void PerformanceVersionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_performanceHooksInstalled || VersionBox.SelectedItem == null || _loaderBox?.SelectedItem == null) return;
            _performanceReadyKey = "";
            string loader = _loaderBox.SelectedItem.ToString() ?? "Vanilla";
            if (!loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)) await PreparePerformanceAsync(loader, GetSelectedVersion());
        }

        private static void PerformanceLaunchPreview(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button button || !string.Equals(button.Name, "LaunchBtn", StringComparison.OrdinalIgnoreCase) || Window.GetWindow(button) is not MainWindow window || window._performanceReplayingLaunch) return;
            try { window.SaveRuntimeLoaderSetting(); window.VersionBox.IsEnabled = true; } catch (Exception ex) { window.WriteException("PERFORMANCE PROFILE SAVE ERROR", ex); }
            string loader = window._loaderBox?.SelectedItem?.ToString() ?? "Vanilla";
            string version = window.GetSelectedVersion();
            if (loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)) return;
            string key = loader + "|" + version;
            if (window._performanceReadyKey.Equals(key, StringComparison.OrdinalIgnoreCase)) return;
            e.Handled = true;
            _ = window.PrepareThenReplayLaunchAsync(loader, version);
        }

        private async Task PrepareThenReplayLaunchAsync(string loader, string version)
        {
            try
            {
                await PreparePerformanceAsync(loader, version);
                if (!_performanceReadyKey.Equals(loader + "|" + version, StringComparison.OrdinalIgnoreCase)) { StatusText.Text = "Performance stack could not be prepared; launch cancelled."; return; }
                _performanceReplayingLaunch = true;
                LaunchBtn.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Button.PreviewMouseLeftButtonDownEvent, Source = LaunchBtn });
            }
            catch (Exception ex) { WriteException("PERFORMANCE PRE-LAUNCH ERROR", ex); StatusText.Text = "Performance stack preparation failed."; }
            finally { _performanceReplayingLaunch = false; }
        }

        private async Task PreparePerformanceAsync(string loader, string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return;
            string key = loader + "|" + version;
            _performanceCts?.Cancel();
            _performanceCts?.Dispose();
            _performanceCts = new CancellationTokenSource();
            CancellationToken token = _performanceCts.Token;
            try
            {
                StatusText.Text = $"Preparing {loader} performance stack...";
                string modsPath = Path.Combine(_gamePath, "mods");
                Directory.CreateDirectory(modsPath);
                RemoveIncompatiblePerformanceMods(modsPath, loader, version);
                foreach (string project in GetPerformanceMods(loader, version)) { token.ThrowIfCancellationRequested(); await InstallPerformanceModAsync(project, loader, version, modsPath, token); }
                token.ThrowIfCancellationRequested();
                _performanceReadyKey = key;
                StatusText.Text = $"{loader} performance stack ready: {version}";
                WriteLog($"TOPU PERFORMANCE STACK READY: {loader} / {version}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _performanceReadyKey = ""; WriteException("TOPU PERFORMANCE STACK ERROR", ex); StatusText.Text = "Performance stack preparation failed."; }
        }

        private static string[] GetPerformanceMods(string loader, string version)
        {
            if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase)) return version == "1.8.9" ? Array.Empty<string>() : FabricMods;
            if (loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase)) return QuiltMods;
            if (loader.Equals("Forge", StringComparison.OrdinalIgnoreCase)) return version == "1.8.9" ? Forge189Mods : ForgeMods;
            if (loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase)) return NeoForgeMods;
            return Array.Empty<string>();
        }

        private void RemoveIncompatiblePerformanceMods(string modsPath, string loader, string version)
        {
            HashSet<string> allowed = new HashSet<string>(GetPerformanceMods(loader, version), StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(modsPath, "*.jar"))
            {
                string name = Path.GetFileName(file);
                string? matched = KnownMods.FirstOrDefault(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
                if (matched == null || allowed.Contains(matched)) continue;
                try { File.Delete(file); WriteLog($"Removed incompatible {loader} performance mod: {name}"); }
                catch (Exception ex) { WriteException("PERFORMANCE MOD CLEANUP ERROR", ex); }
            }
        }

        private async Task InstallPerformanceModAsync(string project, string loader, string version, string modsPath, CancellationToken token)
        {
            string query = "?loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader.ToLowerInvariant() })) + "&game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { version })) + "&version_type=release";
            using HttpResponseMessage response = await Http.GetAsync("https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(project) + "/version" + query, token);
            if (!response.IsSuccessStatusCode) { WriteLog($"No compatible {loader} build of {project} for {version}."); return; }
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return;
            foreach (JsonElement release in doc.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array) continue;
                JsonElement selected = default;
                foreach (JsonElement file in files.EnumerateArray()) { if (selected.ValueKind == JsonValueKind.Undefined) selected = file; if (file.TryGetProperty("primary", out JsonElement primary) && primary.GetBoolean()) { selected = file; break; } }
                if (selected.ValueKind == JsonValueKind.Undefined) continue;
                string url = selected.GetProperty("url").GetString() ?? "";
                string filename = selected.GetProperty("filename").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(filename)) continue;
                string destination = Path.Combine(modsPath, filename);
                if (!File.Exists(destination) || new FileInfo(destination).Length == 0) { byte[] data = await Http.GetByteArrayAsync(url, token); token.ThrowIfCancellationRequested(); await File.WriteAllBytesAsync(destination, data, token); WriteLog($"Installed {loader} performance mod: {filename}"); }
                return;
            }
        }
    }
}
