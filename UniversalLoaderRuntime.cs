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
using System.Xml.Linq;

namespace TopuLauncher
{
    public partial class MainWindow
    {
        private static readonly string[] UniversalPerformanceFabricFamily =
        {
            "sodium", "lithium", "dynamic-fps", "ferrite-core", "immediatelyfast"
        };

        private static readonly string[] UniversalPerformanceForge =
        {
            "embeddium", "ferrite-core", "modernfix", "immediatelyfast", "dynamic-fps"
        };

        private CancellationTokenSource? _universalVersionCts;
        private CancellationTokenSource? _universalPerformanceCts;
        private bool _universalLoaderHooksInstalled;
        private bool _universalCreateHooksInstalled;

        private static readonly bool UniversalLoaderWindowHook = RegisterUniversalLoaderWindowHook();

        private static bool RegisterUniversalLoaderWindowHook()
        {
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(UniversalLoaderWindowLoaded));
            return true;
        }

        private static void UniversalLoaderWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window)
                return;

            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (window is MainWindow main)
                    main.InstallUniversalLoaderHooks();
                else if (string.Equals(window.Title, "Create New Topu Profile", StringComparison.OrdinalIgnoreCase))
                    InstallUniversalCreateProfileHooks(window);
            }), DispatcherPriority.ApplicationIdle);
        }

        private void InstallUniversalLoaderHooks()
        {
            if (_universalLoaderHooksInstalled || _loaderBox == null)
                return;

            // NeoForge must be present in the actual selector before the
            // dynamic version loader starts reading the selected loader.
            if (!_loaderBox.Items.Cast<object>().Any(x =>
                string.Equals(x?.ToString(), "NeoForge", StringComparison.OrdinalIgnoreCase)))
            {
                _loaderBox.Items.Add("NeoForge");
            }

            _universalLoaderHooksInstalled = true;
            _loaderBox.SelectionChanged += UniversalLoaderSelectionChanged;
            VersionBox.SelectionChanged += UniversalVersionSelectionChanged;

            Dispatcher.BeginInvoke(new Action(() => _ = RefreshUniversalVersionsAsync()), DispatcherPriority.ApplicationIdle);
        }

        private async void UniversalLoaderSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_universalLoaderHooksInstalled || _loaderBox?.SelectedItem == null)
                return;

            await RefreshUniversalVersionsAsync();
        }

        private async void UniversalVersionSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_universalLoaderHooksInstalled || VersionBox.SelectedItem == null)
                return;

            string loader = _loaderBox?.SelectedItem?.ToString() ?? "Vanilla";
            string version = GetUniversalSelectedVersion();
            if (string.IsNullOrWhiteSpace(version))
                return;

            await InstallUniversalPerformancePackAsync(loader, version);
        }

        private string GetUniversalSelectedVersion()
        {
            if (VersionBox.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? "";
            return VersionBox.SelectedItem?.ToString() ?? "";
        }

        private async Task RefreshUniversalVersionsAsync()
        {
            if (_loaderBox == null)
                return;

            string loader = _loaderBox.SelectedItem?.ToString() ?? "Vanilla";
            _universalVersionCts?.Cancel();
            _universalVersionCts = new CancellationTokenSource();
            CancellationToken token = _universalVersionCts.Token;

            try
            {
                StatusText.Text = $"Loading {loader} versions...";
                string preferred = GetUniversalSelectedVersion();
                string[] versions = await GetUniversalLoaderVersionsAsync(loader, token);
                token.ThrowIfCancellationRequested();

                if (versions.Length == 0)
                    return;

                VersionBox.SelectionChanged -= UniversalVersionSelectionChanged;
                VersionBox.Items.Clear();
                foreach (string version in versions)
                    VersionBox.Items.Add(new ComboBoxItem { Content = version });

                int index = Array.IndexOf(versions, preferred);
                VersionBox.SelectedIndex = index >= 0 ? index : 0;
                VersionBox.SelectionChanged += UniversalVersionSelectionChanged;

                UpdateProfileCard();
                UpdateLaunchSummary();

                string selected = GetUniversalSelectedVersion();
                if (!string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase))
                    _ = InstallUniversalPerformancePackAsync(loader, selected);

                StatusText.Text = $"{loader}: {versions.Length} supported Minecraft versions";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                WriteException("DYNAMIC LOADER VERSION ERROR", ex);
                StatusText.Text = $"Could not load {loader} versions.";
            }
        }

        private async Task<string[]> GetUniversalLoaderVersionsAsync(string loader, CancellationToken token)
        {
            if (loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
            {
                using HttpResponseMessage response = await Http.GetAsync(
                    "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", token);
                response.EnsureSuccessStatusCode();
                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                return doc.RootElement.GetProperty("versions").EnumerateArray()
                    .Where(x => string.Equals(x.GetProperty("type").GetString(), "release", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.GetProperty("id").GetString() ?? "")
                    .Where(x => x.Length > 0)
                    .ToArray();
            }

            if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                return await GetGameVersionsFromJsonAsync("https://meta.fabricmc.net/v2/versions/game", token);

            if (loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
                return await GetGameVersionsFromJsonAsync("https://meta.quiltmc.org/v3/versions/game", token);

            if (loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
                return await GetForgeMinecraftVersionsAsync(token);

            if (loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
                return await GetNeoForgeMinecraftVersionsAsync(token);

            return Array.Empty<string>();
        }

        private async Task<string[]> GetGameVersionsFromJsonAsync(string url, CancellationToken token)
        {
            using HttpResponseMessage response = await Http.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return doc.RootElement.EnumerateArray()
                .Where(x => !x.TryGetProperty("stable", out JsonElement stable) || stable.GetBoolean())
                .Select(x => x.GetProperty("version").GetString() ?? "")
                .Where(x => x.Length > 0)
                .ToArray();
        }

        private async Task<string[]> GetForgeMinecraftVersionsAsync(CancellationToken token)
        {
            using HttpResponseMessage response = await Http.GetAsync(
                "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", token);
            response.EnsureSuccessStatusCode();
            string xml = await response.Content.ReadAsStringAsync(token);
            XDocument doc = XDocument.Parse(xml);
            return doc.Descendants("version")
                .Select(x => x.Value)
                .Select(x => x.Split('-').FirstOrDefault() ?? "")
                .Where(x => x.Count(c => c == '.') >= 1 && char.IsDigit(x[0]))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(VersionSortKey)
                .ToArray();
        }

        private async Task<string[]> GetNeoForgeMinecraftVersionsAsync(CancellationToken token)
        {
            using HttpResponseMessage response = await Http.GetAsync(
                "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", token);
            response.EnsureSuccessStatusCode();
            string xml = await response.Content.ReadAsStringAsync(token);
            XDocument doc = XDocument.Parse(xml);
            HashSet<string> versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string value in doc.Descendants("version").Select(x => x.Value))
            {
                string[] p = value.Split('.');
                if (p.Length < 2 || !int.TryParse(p[0], out int major) || !int.TryParse(p[1], out int minor))
                    continue;

                string mc;
                if (major >= 26)
                    mc = $"{major}.{minor}";
                else if (major >= 20)
                    mc = $"1.{major}.{minor}";
                else
                    continue;

                versions.Add(mc);
            }

            return versions.OrderByDescending(VersionSortKey).ToArray();
        }

        private static string VersionSortKey(string version)
        {
            return string.Join(".", version.Split('.').Select(p =>
            {
                string digits = new string(p.TakeWhile(char.IsDigit).ToArray());
                return int.TryParse(digits, out int n) ? n.ToString("D6") : "000000";
            }));
        }

        private async Task InstallUniversalPerformancePackAsync(string loader, string minecraftVersion)
        {
            if (string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(minecraftVersion))
                return;

            _universalPerformanceCts?.Cancel();
            _universalPerformanceCts = new CancellationTokenSource();
            CancellationToken token = _universalPerformanceCts.Token;

            try
            {
                string[] projects = loader.Equals("Forge", StringComparison.OrdinalIgnoreCase)
                    ? UniversalPerformanceForge
                    : UniversalPerformanceFabricFamily;

                string modsPath = Path.Combine(_gamePath, "mods");
                Directory.CreateDirectory(modsPath);

                RemoveObsoletePerformanceMods(modsPath, loader);

                foreach (string project in projects)
                {
                    token.ThrowIfCancellationRequested();
                    await InstallModrinthProjectAsync(project, loader, minecraftVersion, modsPath, token);
                }

                WriteLog($"Performance pack synchronized: {loader} / {minecraftVersion}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                WriteException("PERFORMANCE PACK ERROR", ex);
            }
        }

        private void RemoveObsoletePerformanceMods(string modsPath, string loader)
        {
            string[] obsolete =
            {
                "sodium-extra", "sodium_extra", "krypton", "fabric-api"
            };

            if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                return;

            foreach (string file in Directory.EnumerateFiles(modsPath, "*.jar"))
            {
                string name = Path.GetFileName(file).ToLowerInvariant();
                if (obsolete.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase)))
                {
                    try { File.Delete(file); WriteLog($"Removed incompatible performance mod: {Path.GetFileName(file)}"); }
                    catch { }
                }
            }
        }

        private async Task InstallModrinthProjectAsync(
            string project,
            string loader,
            string minecraftVersion,
            string modsPath,
            CancellationToken token)
        {
            string modrinthLoader = loader.ToLowerInvariant();
            string query = "?loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { modrinthLoader })) +
                           "&game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraftVersion })) +
                           "&version_type=release&include_changelog=false";

            using HttpResponseMessage response = await Http.GetAsync(
                "https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(project) + "/version" + query, token);

            if (!response.IsSuccessStatusCode)
            {
                WriteLog($"No {loader} build of {project} for Minecraft {minecraftVersion}.");
                return;
            }

            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                WriteLog($"No compatible release of {project} for {loader} {minecraftVersion}.");
                return;
            }

            HashSet<string> installedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement version in doc.RootElement.EnumerateArray())
            {
                if (!version.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array)
                    continue;

                JsonElement? primary = files.EnumerateArray().FirstOrDefault(f => f.TryGetProperty("primary", out JsonElement p) && p.GetBoolean());
                JsonElement file = primary ?? files.EnumerateArray().FirstOrDefault();
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
                    await File.WriteAllBytesAsync(destination, data, token);
                    WriteLog($"Installed {project}: {filename}");
                }
                installedProjects.Add(project);
                break;
            }
        }

        private static void InstallUniversalCreateProfileHooks(Window dialog)
        {
            if (dialog.Tag as string == "TopuUniversalCreateHooks")
                return;

            dialog.Tag = "TopuUniversalCreateHooks";
            List<ComboBox> combos = FindUniversalComboBoxes(dialog).ToList();
            ComboBox? loader = combos.FirstOrDefault(c =>
                c.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), "Fabric", StringComparison.OrdinalIgnoreCase)) &&
                c.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), "Forge", StringComparison.OrdinalIgnoreCase)));
            ComboBox? version = combos.FirstOrDefault(c => c != loader);
            if (loader == null || version == null)
                return;

            loader.ItemsSource = new[] { "Vanilla", "Fabric", "Forge", "Quilt", "NeoForge" };
            loader.SelectedIndex = 0;

            loader.SelectionChanged += async (_, _) =>
            {
                string selected = loader.SelectedItem?.ToString() ?? "Vanilla";
                try
                {
                    string[] values = await GetUniversalCreateVersionsAsync(selected);
                    version.ItemsSource = values;
                    version.SelectedIndex = 0;
                }
                catch { }
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    string[] values = await GetUniversalCreateVersionsAsync("Vanilla");
                    dialog.Dispatcher.Invoke(() => { version.ItemsSource = values; version.SelectedIndex = 0; });
                }
                catch { }
            });
        }

        private static async Task<string[]> GetUniversalCreateVersionsAsync(string loader)
        {
            using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                return await ParseCreateJsonAsync(client, "https://meta.fabricmc.net/v2/versions/game");
            if (loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
                return await ParseCreateJsonAsync(client, "https://meta.quiltmc.org/v3/versions/game");
            if (loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
                return await ParseCreateNeoForgeAsync(client);
            if (loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
                return await ParseCreateForgeAsync(client);

            string json = await client.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("versions").EnumerateArray()
                .Where(x => x.GetProperty("type").GetString() == "release")
                .Select(x => x.GetProperty("id").GetString() ?? "")
                .Where(x => x.Length > 0).ToArray();
        }

        private static async Task<string[]> ParseCreateJsonAsync(HttpClient client, string url)
        {
            using JsonDocument doc = JsonDocument.Parse(await client.GetStringAsync(url));
            return doc.RootElement.EnumerateArray()
                .Where(x => !x.TryGetProperty("stable", out JsonElement s) || s.GetBoolean())
                .Select(x => x.GetProperty("version").GetString() ?? "")
                .Where(x => x.Length > 0).ToArray();
        }

        private static async Task<string[]> ParseCreateForgeAsync(HttpClient client)
        {
            XDocument doc = XDocument.Parse(await client.GetStringAsync("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml"));
            return doc.Descendants("version").Select(x => x.Value.Split('-').FirstOrDefault() ?? "")
                .Where(x => x.Length > 0 && char.IsDigit(x[0])).Distinct().OrderByDescending(VersionSortKey).ToArray();
        }

        private static async Task<string[]> ParseCreateNeoForgeAsync(HttpClient client)
        {
            XDocument doc = XDocument.Parse(await client.GetStringAsync("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml"));
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in doc.Descendants("version").Select(x => x.Value))
            {
                string[] p = value.Split('.');
                if (p.Length < 2 || !int.TryParse(p[0], out int major) || !int.TryParse(p[1], out int minor)) continue;
                if (major >= 26) result.Add($"{major}.{minor}");
                else if (major >= 20) result.Add($"1.{major}.{minor}");
            }
            return result.OrderByDescending(VersionSortKey).ToArray();
        }

        private static IEnumerable<ComboBox> FindUniversalComboBoxes(DependencyObject root)
        {
            if (root is ComboBox combo) yield return combo;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                foreach (ComboBox child in FindUniversalComboBoxes(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
