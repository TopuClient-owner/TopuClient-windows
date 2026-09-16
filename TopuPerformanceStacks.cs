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
        private static readonly string[] TopuFabricPerformance =
        {
            "fabric-api", "sodium", "sodium-extra", "lithium", "dynamic-fps",
            "ferrite-core", "immediatelyfast", "krypton"
        };

        private static readonly string[] TopuQuiltPerformance =
        {
            "qsl", "sodium", "sodium-extra", "lithium", "dynamic-fps",
            "ferrite-core", "immediatelyfast"
        };

        private static readonly string[] TopuForgePerformance =
        {
            "embeddium", "ferrite-core", "modernfix", "immediatelyfast",
            "dynamic-fps", "entityculling"
        };

        private static readonly string[] TopuNeoForgePerformance =
        {
            "sodium", "sodium-extra", "lithium", "ferrite-core",
            "modernfix", "immediatelyfast", "dynamic-fps", "entityculling"
        };

        private static readonly string[] TopuForge189Performance =
        {
            "foamfix"
        };

        private static readonly string[] TopuKnownPerformanceProjects =
        {
            "fabric-api", "qsl", "sodium", "sodium-extra", "lithium", "dynamic-fps",
            "ferrite-core", "immediatelyfast", "krypton", "embeddium", "modernfix",
            "entityculling", "foamfix"
        };

        private CancellationTokenSource? _topuPerformanceCts;
        private string _topuPerformanceReadyKey = "";
        private bool _topuPerformanceHooksInstalled;
        private bool _topuPerformanceReplayingLaunch;

        private static readonly object TopuPerformanceRegistration = RegisterTopuPerformanceHandlers();

        private static object RegisterTopuPerformanceHandlers()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(TopuPerformanceLoaded));

            EventManager.RegisterClassHandler(
                typeof(Button),
                Button.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(TopuPerformanceLaunchPreview),
                true);

            return new object();
        }

        private static void TopuPerformanceLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window)
                return;

            window.Dispatcher.BeginInvoke(new Action(window.InstallTopuPerformanceHooks), DispatcherPriority.ApplicationIdle);
        }

        private void InstallTopuPerformanceHooks()
        {
            if (_topuPerformanceHooksInstalled || _loaderBox == null)
                return;

            // The old universal runtime must not own version/performance events.
            // Core launcher code remains the single owner of profile/version UI.
            _universalVersionCts?.Cancel();
            _universalPerformanceCts?.Cancel();
            _loaderBox.SelectionChanged -= UniversalLoaderSelectionChanged;
            VersionBox.SelectionChanged -= UniversalVersionSelectionChanged;
            _universalLoaderHooksInstalled = true;

            _topuPerformanceHooksInstalled = true;
            _loaderBox.SelectionChanged += TopuPerformanceLoaderChanged;
            VersionBox.SelectionChanged += TopuPerformanceVersionChanged;
        }

        private async void TopuPerformanceLoaderChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_topuPerformanceHooksInstalled || _loaderBox?.SelectedItem == null)
                return;

            _topuPerformanceReadyKey = "";
            string loader = _loaderBox.SelectedItem.ToString() ?? "Vanilla";
            string version = GetSelectedVersion();
            if (!string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase))
                await PrepareTopuPerformanceAsync(loader, version);
        }

        private async void TopuPerformanceVersionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_topuPerformanceHooksInstalled || VersionBox.SelectedItem == null || _loaderBox?.SelectedItem == null)
                return;

            _topuPerformanceReadyKey = "";
            string loader = _loaderBox.SelectedItem.ToString() ?? "Vanilla";
            string version = GetSelectedVersion();
            if (!string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase))
                await PrepareTopuPerformanceAsync(loader, version);
        }

        private static void TopuPerformanceLaunchPreview(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button button ||
                !string.Equals(button.Name, "LaunchBtn", StringComparison.OrdinalIgnoreCase) ||
                Window.GetWindow(button) is not MainWindow window ||
                window._topuPerformanceReplayingLaunch)
                return;

            if (window._loaderBox == null)
                return;

            string loader = window._loaderBox.SelectedItem?.ToString() ?? "Vanilla";
            string version = window.GetSelectedVersion();
            string key = loader + "|" + version;

            if (loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ||
                window._topuPerformanceReadyKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                return;

            e.Handled = true;
            _ = window.PrepareThenReplayLaunchAsync(loader, version);
        }

        private async Task PrepareThenReplayLaunchAsync(string loader, string version)
        {
            try
            {
                await PrepareTopuPerformanceAsync(loader, version);

                if (!_topuPerformanceReadyKey.Equals(loader + "|" + version, StringComparison.OrdinalIgnoreCase))
                {
                    StatusText.Text = "Performance stack could not be prepared; launch cancelled.";
                    return;
                }

                _topuPerformanceReplayingLaunch = true;
                MouseButtonEventArgs replay = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Button.PreviewMouseLeftButtonDownEvent,
                    Source = LaunchBtn
                };
                LaunchBtn.RaiseEvent(replay);
            }
            catch (Exception ex)
            {
                WriteException("PERFORMANCE PRE-LAUNCH ERROR", ex);
            }
            finally
            {
                _topuPerformanceReplayingLaunch = false;
            }
        }

        private async Task PrepareTopuPerformanceAsync(string loader, string minecraftVersion)
        {
            if (string.IsNullOrWhiteSpace(minecraftVersion))
                return;

            if (loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
            {
                _topuPerformanceReadyKey = loader + "|" + minecraftVersion;
                return;
            }

            string key = loader + "|" + minecraftVersion;
            _topuPerformanceCts?.Cancel();
            _topuPerformanceCts = new CancellationTokenSource();
            CancellationToken token = _topuPerformanceCts.Token;

            try
            {
                StatusText.Text = $"Preparing {loader} performance stack...";
                string modsPath = Path.Combine(_gamePath, "mods");
                Directory.CreateDirectory(modsPath);

                RemoveIncompatibleTopuPerformanceMods(modsPath, loader, minecraftVersion);

                foreach (string project in GetTopuPerformanceProjects(loader, minecraftVersion))
                {
                    token.ThrowIfCancellationRequested();
                    await InstallTopuModrinthProjectAsync(project, loader, minecraftVersion, modsPath, token);
                }

                token.ThrowIfCancellationRequested();
                _topuPerformanceReadyKey = key;
                StatusText.Text = $"{loader} performance stack ready: {minecraftVersion}";
                WriteLog($"TOPU PERFORMANCE STACK READY: {loader} / {minecraftVersion}");
            }
            catch (OperationCanceledException)
            {
                if (!_topuPerformanceReadyKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    StatusText.Text = "Performance stack update cancelled.";
            }
            catch (Exception ex)
            {
                _topuPerformanceReadyKey = "";
                WriteException("TOPU PERFORMANCE STACK ERROR", ex);
                StatusText.Text = "Performance stack preparation failed.";
            }
        }

        private static IEnumerable<string> GetTopuPerformanceProjects(string loader, string minecraftVersion)
        {
            if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                return TopuFabricPerformance;

            if (loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
                return TopuQuiltPerformance;

            if (loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
                return TopuNeoForgePerformance;

            if (loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
                return minecraftVersion == "1.8.9" ? TopuForge189Performance : TopuForgePerformance;

            return Array.Empty<string>();
        }

        private static void RemoveIncompatibleTopuPerformanceMods(string modsPath, string loader, string minecraftVersion)
        {
            HashSet<string> allowed = new HashSet<string>(
                GetTopuPerformanceProjects(loader, minecraftVersion),
                StringComparer.OrdinalIgnoreCase);

            foreach (string file in Directory.EnumerateFiles(modsPath, "*.jar"))
            {
                string name = Path.GetFileName(file);
                string? matched = TopuKnownPerformanceProjects.FirstOrDefault(project =>
                    name.Contains(project, StringComparison.OrdinalIgnoreCase));

                if (matched == null || allowed.Contains(matched))
                    continue;

                try
                {
                    File.Delete(file);
                    WriteLog($"Removed incompatible {loader} performance mod: {name}");
                }
                catch (Exception ex)
                {
                    WriteException("PERFORMANCE MOD CLEANUP ERROR", ex);
                }
            }
        }

        private async Task InstallTopuModrinthProjectAsync(
            string project,
            string loader,
            string minecraftVersion,
            string modsPath,
            CancellationToken token)
        {
            string query = "?loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader.ToLowerInvariant() })) +
                           "&game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraftVersion })) +
                           "&version_type=release&include_changelog=false";

            using HttpResponseMessage response = await Http.GetAsync(
                "https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(project) + "/version" + query,
                token);

            if (!response.IsSuccessStatusCode)
            {
                WriteLog($"No compatible {loader} release of {project} for Minecraft {minecraftVersion}.");
                return;
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                WriteLog($"No compatible release of {project} for {loader} {minecraftVersion}.");
                return;
            }

            foreach (JsonElement version in doc.RootElement.EnumerateArray())
            {
                if (!version.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array)
                    continue;

                JsonElement file = default;
                foreach (JsonElement candidate in files.EnumerateArray())
                {
                    if (candidate.TryGetProperty("primary", out JsonElement primary) && primary.GetBoolean())
                    {
                        file = candidate;
                        break;
                    }

                    if (file.ValueKind == JsonValueKind.Undefined)
                        file = candidate;
                }

                if (file.ValueKind == JsonValueKind.Undefined)
                    continue;

                string url = file.GetProperty("url").GetString() ?? "";
                string filename = file.GetProperty("filename").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(filename))
                    continue;

                string destination = Path.Combine(modsPath, filename);
                if (!File.Exists(destination) || new FileInfo(destination).Length == 0)
                {
                    byte[] data = await Http.GetByteArrayAsync(url, token);
                    token.ThrowIfCancellationRequested();
                    await File.WriteAllBytesAsync(destination, data, token);
                    WriteLog($"Installed {loader} performance mod: {filename}");
                }
                return;
            }
        }
    }
}
