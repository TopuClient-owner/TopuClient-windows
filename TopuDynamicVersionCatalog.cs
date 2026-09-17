using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace TopuLauncher
{
    // Restores the original dynamic version catalog behavior without touching
    // launch routing or profile persistence. Static fallback lists remain in
    // MainWindow.LoaderRuntime.cs if a version service is unavailable.
    public partial class MainWindow
    {
        private static readonly HttpClient DynamicVersionHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private CancellationTokenSource? _dynamicVersionCts;
        private bool _dynamicVersionHooksReady;

        static MainWindowDynamicVersionBootstrap()
        {
        }

        private void InitializeDynamicVersionCatalog()
        {
            if (_dynamicVersionHooksReady || _loaderBox == null || VersionBox == null)
                return;

            _dynamicVersionHooksReady = true;
            _loaderBox.SelectionChanged += DynamicLoaderChanged;
            Dispatcher.BeginInvoke(new Action(() => _ = RefreshDynamicVersionListAsync()), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private async void DynamicLoaderChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_dynamicVersionHooksReady || _loaderBox?.SelectedItem == null)
                return;
            await RefreshDynamicVersionListAsync();
        }

        private async Task RefreshDynamicVersionListAsync()
        {
            if (_loaderBox == null || VersionBox == null)
                return;

            string loader = _loaderBox.SelectedItem?.ToString() ?? "Vanilla";
            string preferred = GetSelectedVersion();
            try
            {
                _dynamicVersionCts?.Cancel();
                _dynamicVersionCts = new CancellationTokenSource();
                string[] versions = await GetDynamicVersionsAsync(loader, _dynamicVersionCts.Token);
                if (versions.Length == 0)
                    return;

                string saved = GetRuntimeProfile().Version;
                string target = string.IsNullOrWhiteSpace(saved) ? preferred : saved;

                VersionBox.Items.Clear();
                foreach (string version in versions)
                    VersionBox.Items.Add(new ComboBoxItem { Content = version });

                int index = Array.FindIndex(versions, v => string.Equals(v, target, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    index = Array.FindIndex(versions, v => string.Equals(v, preferred, StringComparison.OrdinalIgnoreCase));
                VersionBox.SelectedIndex = index >= 0 ? index : 0;
                VersionBox.IsEnabled = true;
                UpdateProfileCard();
                UpdateLaunchSummary();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteException("DYNAMIC VERSION CATALOG ERROR", ex);
                // Keep the static fallback list already supplied by LoaderRuntime.
            }
        }

        private async Task<string[]> GetDynamicVersionsAsync(string loader, CancellationToken token)
        {
            if (loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
            {
                using HttpResponseMessage response = await DynamicVersionHttp.GetAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", token);
                response.EnsureSuccessStatusCode();
                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                return doc.RootElement.GetProperty("versions").EnumerateArray()
                    .Where(x => string.Equals(x.GetProperty("type").GetString(), "release", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.GetProperty("id").GetString() ?? "")
                    .Where(x => x.Length > 0)
                    .ToArray();
            }

            if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                return await GetJsonGameVersionsAsync("https://meta.fabricmc.net/v2/versions/game", token);

            if (loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
                return await GetJsonGameVersionsAsync("https://meta.quiltmc.org/v3/versions/game", token);

            if (loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
                return await GetForgeVersionsAsync(token);

            if (loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
                return await GetNeoForgeVersionsAsync(token);

            return Array.Empty<string>();
        }

        private async Task<string[]> GetJsonGameVersionsAsync(string url, CancellationToken token)
        {
            using HttpResponseMessage response = await DynamicVersionHttp.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return doc.RootElement.EnumerateArray()
                .Where(x => !x.TryGetProperty("stable", out JsonElement stable) || stable.GetBoolean())
                .Select(x => x.GetProperty("version").GetString() ?? "")
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task<string[]> GetForgeVersionsAsync(CancellationToken token)
        {
            using HttpResponseMessage response = await DynamicVersionHttp.GetAsync("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", token);
            response.EnsureSuccessStatusCode();
            XDocument doc = XDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return doc.Descendants("version")
                .Select(x => x.Value.Split('-')[0])
                .Where(IsMinecraftVersion)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(VersionSortKey)
                .ToArray();
        }

        private async Task<string[]> GetNeoForgeVersionsAsync(CancellationToken token)
        {
            using HttpResponseMessage response = await DynamicVersionHttp.GetAsync("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", token);
            response.EnsureSuccessStatusCode();
            XDocument doc = XDocument.Parse(await response.Content.ReadAsStringAsync(token));
            HashSet<string> versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in doc.Descendants("version").Select(x => x.Value))
            {
                string[] parts = value.Split('.');
                if (parts.Length < 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
                    continue;
                if (major >= 26) versions.Add($"{major}.{minor}");
                else if (major >= 20) versions.Add($"1.{major}.{minor}");
            }
            return versions.OrderByDescending(VersionSortKey).ToArray();
        }

        private static bool IsMinecraftVersion(string value)
        {
            string[] p = value.Split('.');
            return p.Length >= 2 && int.TryParse(p[0], out _);
        }

        private static string VersionSortKey(string value)
        {
            return string.Join(".", value.Split('.').Select(part =>
            {
                string digits = new string(part.TakeWhile(char.IsDigit).ToArray());
                return int.TryParse(digits, out int n) ? n.ToString("D8") : "00000000";
            }));
        }
    }
}
