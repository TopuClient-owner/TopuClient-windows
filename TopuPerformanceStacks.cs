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
using System.Windows.Threading;

namespace TopuLauncher
{
    public partial class MainWindow
    {
        private static readonly string[] FabricMods =
        {
            "fabric-api", "sodium", "sodium-extra", "lithium",
            "dynamic-fps", "ferrite-core", "immediatelyfast", "krypton"
        };

        private static readonly string[] QuiltMods =
        {
            "qsl", "sodium", "sodium-extra", "lithium",
            "dynamic-fps", "ferrite-core", "immediatelyfast"
        };

        private static readonly string[] ForgeMods =
        {
            "embeddium", "ferrite-core", "modernfix",
            "immediatelyfast", "dynamic-fps", "entityculling"
        };

        private static readonly string[] NeoForgeMods =
        {
            "sodium", "sodium-extra", "lithium", "ferrite-core",
            "modernfix", "immediatelyfast", "dynamic-fps", "entityculling"
        };

        private static readonly string[] Forge189Mods = { "foamfix" };

        private CancellationTokenSource? _performanceCts;
        private bool _performanceHooksInstalled;
        private static readonly object PerformanceRegistration = RegisterPerformanceHandlers();

        private static object RegisterPerformanceHandlers()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(PerformanceLoaded));
            return new object();
        }

        private static void PerformanceLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is MainWindow window)
            {
                window.Dispatcher.BeginInvoke(
                    new Action(window.InstallPerformanceHooks),
                    DispatcherPriority.ApplicationIdle);
            }
        }

        private void InstallPerformanceHooks()
        {
            if (_performanceHooksInstalled || _loaderBox == null)
                return;

            _performanceHooksInstalled = true;
            _loaderBox.SelectionChanged += PerformanceLoaderChanged;
            VersionBox.SelectionChanged += PerformanceVersionChanged;
        }

        private async void PerformanceLoaderChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_performanceHooksInstalled || _loaderBox?.SelectedItem == null)
                return;

            string loader = _loaderBox.SelectedItem.ToString() ?? "Vanilla";
            if (!loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
                await PreparePerformanceAsync(loader, GetSelectedVersion());
        }

        private async void PerformanceVersionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_performanceHooksInstalled || VersionBox.SelectedItem == null || _loaderBox?.SelectedItem == null)
                return;

            string loader = _loaderBox.SelectedItem.ToString() ?? "Vanilla";
            if (!loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
                await PreparePerformanceAsync(loader, GetSelectedVersion());
        }

        private async Task PreparePerformanceAsync(string loader, string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return;

            _performanceCts?.Cancel();
            _performanceCts?.Dispose();
            _performanceCts = new CancellationTokenSource();
            CancellationToken token = _performanceCts.Token;

            try
            {
                string modsPath = Path.Combine(_gamePath, "mods");
                Directory.CreateDirectory(modsPath);
                StatusText.Text = $"Preparing {loader} performance stack...";

                foreach (string project in GetPerformanceMods(loader, version))
                {
                    token.ThrowIfCancellationRequested();
                    await InstallPerformanceModAsync(project, loader, version, modsPath, token);
                }

                token.ThrowIfCancellationRequested();
                StatusText.Text = $"{loader} performance stack ready: {version}";
                WriteLog($"TOPU PERFORMANCE STACK READY: {loader} / {version}");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteException("TOPU PERFORMANCE STACK ERROR", ex);
                StatusText.Text = "Performance stack preparation failed.";
            }
        }

        private static string[] GetPerformanceMods(string loader, string version)
        {
            if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                return version == "1.8.9" ? Array.Empty<string>() : FabricMods;

            if (loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
                return QuiltMods;

            if (loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
                return version == "1.8.9" ? Forge189Mods : ForgeMods;

            if (loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
                return NeoForgeMods;

            return Array.Empty<string>();
        }

        private async Task InstallPerformanceModAsync(
            string project,
            string loader,
            string version,
            string modsPath,
            CancellationToken token)
        {
            string query =
                "?loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader.ToLowerInvariant() })) +
                "&game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { version })) +
                "&version_type=release";

            using HttpResponseMessage response = await Http.GetAsync(
                "https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(project) + "/version" + query,
                token);

            if (!response.IsSuccessStatusCode)
            {
                WriteLog($"No compatible {loader} build of {project} for {version}.");
                return;
            }

            using JsonDocument doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            if (doc.RootElement.ValueKind != JsonValueKind.Array ||
                doc.RootElement.GetArrayLength() == 0)
                return;

            foreach (JsonElement release in doc.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("files", out JsonElement files) ||
                    files.ValueKind != JsonValueKind.Array)
                    continue;

                JsonElement selected = default;
                foreach (JsonElement file in files.EnumerateArray())
                {
                    if (selected.ValueKind == JsonValueKind.Undefined)
                        selected = file;

                    if (file.TryGetProperty("primary", out JsonElement primary) &&
                        primary.GetBoolean())
                    {
                        selected = file;
                        break;
                    }
                }

                if (selected.ValueKind == JsonValueKind.Undefined)
                    continue;

                string url = selected.GetProperty("url").GetString() ?? "";
                string filename = selected.GetProperty("filename").GetString() ?? "";
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
