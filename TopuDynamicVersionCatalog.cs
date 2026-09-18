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
    // Loader-aware version catalog. The UI is populated from the loader
    // providers themselves; no reduced hard-coded Minecraft version list.
    public partial class MainWindow
    {
        private static readonly HttpClient DynamicVersionHttp = CreateDynamicVersionHttp();
        private CancellationTokenSource? _dynamicVersionCts;
        private bool _dynamicVersionHooksReady;

        private static HttpClient CreateDynamicVersionHttp()
        {
            HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TopuClient/1.0");
            return client;
        }

        private void InitializeDynamicVersionCatalog()
        {
            if (_dynamicVersionHooksReady || _loaderBox == null || VersionBox == null)
                return;

            _dynamicVersionHooksReady = true;
            _ = RefreshDynamicVersionListAsync();
        }

        private async Task RefreshDynamicVersionListAsync(string? preferred = null)
        {
            if (_loaderBox == null || VersionBox == null)
                return;

            string loader = _loaderBox.SelectedItem?.ToString() ?? "Vanilla";
            string uiPreferred = preferred ?? GetSelectedVersion();

            try
            {
                _dynamicVersionCts?.Cancel();
                _dynamicVersionCts?.Dispose();
                _dynamicVersionCts = new CancellationTokenSource();
                CancellationToken token = _dynamicVersionCts.Token;

                StatusText.Text = $"Loading {loader} Minecraft versions...";
                string[] versions = await GetDynamicVersionsAsync(loader, token);
                token.ThrowIfCancellationRequested();

                if (versions.Length == 0)
                    throw new InvalidOperationException($"The {loader} version service returned no supported Minecraft versions.");

                WriteLog($"{loader} catalog source returned {versions.Length} stable Minecraft versions.");

                string saved = GetRuntimeProfile().Version;
                string target = !string.IsNullOrWhiteSpace(saved) ? saved : uiPreferred;

                VersionBox.Items.Clear();
                foreach (string version in versions)
                    VersionBox.Items.Add(new ComboBoxItem { Content = version });

                int index = Array.FindIndex(versions, v =>
                    string.Equals(v, target, StringComparison.OrdinalIgnoreCase));

                if (index < 0)
                    index = Array.FindIndex(versions, v =>
                        string.Equals(v, uiPreferred, StringComparison.OrdinalIgnoreCase));

                VersionBox.SelectedIndex = index >= 0 ? index : 0;
                VersionBox.IsEnabled = true;
                UpdateProfileCard();
                UpdateLaunchSummary();

                WriteLog($"Dynamic {loader} catalog loaded: {versions.Length} Minecraft versions.");
                StatusText.Text = $"Loaded {versions.Length} {loader} Minecraft versions.";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteException($"DYNAMIC {loader} VERSION CATALOG ERROR", ex);
                StatusText.Text = $"Could not load {loader} versions. Check your internet connection.";
            }
        }

        private async Task<string[]> GetDynamicVersionsAsync(string loader, CancellationToken token)
        {
            if (loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
            {
                using HttpResponseMessage response = await DynamicVersionHttp.GetAsync(
                    "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", token);
                response.EnsureSuccessStatusCode();

                using JsonDocument doc = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(token));

                return doc.RootElement.GetProperty("versions").EnumerateArray()
                    .Where(x => string.Equals(
                        x.GetProperty("type").GetString(),
                        "release",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.GetProperty("id").GetString() ?? "")
                    .Where(IsMinecraftVersion)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(VersionSortKey, StringComparer.Ordinal)
                    .ToArray();
            }

            if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
                return await GetJsonGameVersionsAsync(
                    "https://meta.fabricmc.net/v2/versions/game", token);

            if (loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
                return await GetJsonGameVersionsAsync(
                    "https://meta.quiltmc.org/v3/versions/game", token);

            if (loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
                return await GetForgeGameVersionsAsync(token);

            if (loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
                return await GetNeoForgeGameVersionsAsync(token);

            return Array.Empty<string>();
        }

        private async Task<string[]> GetJsonGameVersionsAsync(
            string url,
            CancellationToken token)
        {
            using HttpResponseMessage response =
                await DynamicVersionHttp.GetAsync(url, token);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            // Only stable releases are shown. This still includes the full
            // historical stable catalog returned by Fabric/Quilt.
            return doc.RootElement.EnumerateArray()
                .Where(x =>
                    !x.TryGetProperty("stable", out JsonElement stable) ||
                    stable.GetBoolean())
                .Select(x => x.GetProperty("version").GetString() ?? "")
                .Where(IsMinecraftVersion)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(VersionSortKey, StringComparer.Ordinal)
                .ToArray();
        }

        private async Task<string[]> GetForgeGameVersionsAsync(CancellationToken token)
        {
            using HttpResponseMessage response =
                await DynamicVersionHttp.GetAsync(
                    "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml",
                    token);
            response.EnsureSuccessStatusCode();

            XDocument doc = XDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            // Forge coordinates are <minecraft-version>-<forge-build>.
            // Return every Minecraft version for which Forge has published a build.
            return doc.Descendants("version")
                .Select(x => x.Value)
                .Select(v => v.Contains('-') ? v[..v.IndexOf('-')] : v)
                .Where(IsMinecraftVersion)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(VersionSortKey, StringComparer.Ordinal)
                .ToArray();
        }

        private async Task<string[]> GetNeoForgeGameVersionsAsync(CancellationToken token)
        {
            using HttpResponseMessage response =
                await DynamicVersionHttp.GetAsync(
                    "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml",
                    token);
            response.EnsureSuccessStatusCode();

            XDocument doc = XDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            HashSet<string> versions = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (string loaderVersion in doc.Descendants("version").Select(x => x.Value))
            {
                string minecraftVersion = NeoForgeLoaderToMinecraft(loaderVersion);
                if (!string.IsNullOrWhiteSpace(minecraftVersion))
                    versions.Add(minecraftVersion);
            }

            return versions
                .OrderByDescending(VersionSortKey, StringComparer.Ordinal)
                .ToArray();
        }

        // NeoForge uses loader versions such as:
        //   21.1.x   -> Minecraft 1.21.1
        //   21.4.x   -> Minecraft 1.21.4
        //   20.6.x   -> Minecraft 1.20.6
        //   26.1.2.x -> Minecraft 26.1.2
        //   26.2.x   -> Minecraft 26.2
        private static string NeoForgeLoaderToMinecraft(string loaderVersion)
        {
            string[] parts = loaderVersion.Split('.');
            if (parts.Length < 2)
                return "";

            if (!int.TryParse(parts[0], out int major) ||
                !int.TryParse(parts[1], out int minor))
                return "";

            if (major >= 26)
            {
                if (parts.Length >= 3 &&
                    int.TryParse(parts[2], out int patch) &&
                    patch > 0)
                    return $"{major}.{minor}.{patch}";

                return $"{major}.{minor}";
            }

            if (major >= 20)
                return minor == 0 ? $"1.{major}" : $"1.{major}.{minor}";

            return "";
        }

        // Select the newest stable Fabric Loader that explicitly supports the
        // chosen Minecraft version. This prevents using a modern loader such
        // as 0.19.x against old Minecraft versions.
        private async Task<string> GetStableFabricLoaderVersionAsync(
            string minecraftVersion,
            CancellationToken token)
        {
            string url =
                "https://meta.fabricmc.net/v2/versions/loader/" +
                Uri.EscapeDataString(minecraftVersion);

            using HttpResponseMessage response =
                await DynamicVersionHttp.GetAsync(url, token);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            foreach (JsonElement entry in doc.RootElement.EnumerateArray())
            {
                bool stable = !entry.TryGetProperty("loader", out JsonElement loader) ||
                              !loader.TryGetProperty("stable", out JsonElement stableValue) ||
                              stableValue.GetBoolean();

                if (!stable)
                    continue;

                if (loader.TryGetProperty("version", out JsonElement version))
                {
                    string value = version.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            throw new InvalidOperationException(
                $"No stable Fabric Loader is available for Minecraft {minecraftVersion}.");
        }

        // Select the newest stable Quilt Loader that explicitly supports the
        // chosen Minecraft version.
        private async Task<string> GetStableQuiltLoaderVersionAsync(
            string minecraftVersion,
            CancellationToken token)
        {
            string url =
                "https://meta.quiltmc.org/v3/versions/loader/" +
                Uri.EscapeDataString(minecraftVersion);

            using HttpResponseMessage response =
                await DynamicVersionHttp.GetAsync(url, token);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            foreach (JsonElement entry in doc.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("loader", out JsonElement loader))
                    continue;

                bool stable = !loader.TryGetProperty("stable", out JsonElement stableValue) ||
                              stableValue.GetBoolean();

                if (!stable)
                    continue;

                if (loader.TryGetProperty("version", out JsonElement version))
                {
                    string value = version.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            throw new InvalidOperationException(
                $"No stable Quilt Loader is available for Minecraft {minecraftVersion}.");
        }

        private async Task PopulateVersionComboAsync(ComboBox combo, string loader)
        {
            try
            {
                string[] versions = await GetDynamicVersionsAsync(loader, CancellationToken.None);
                combo.Items.Clear();
                foreach (string version in versions)
                    combo.Items.Add(new ComboBoxItem { Content = version });
                if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                WriteException($"CREATE PROFILE {loader} VERSION CATALOG ERROR", ex);
            }
        }

        private static bool IsMinecraftVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Split('.');
            return parts.Length >= 2 &&
                   int.TryParse(parts[0], out _) &&
                   int.TryParse(parts[1], out _);
        }

        private static string VersionSortKey(string value)
        {
            return string.Join(".",
                value.Split('.').Select(part =>
                {
                    string digits = new string(
                        part.TakeWhile(char.IsDigit).ToArray());

                    return int.TryParse(digits, out int n)
                        ? n.ToString("D8")
                        : "00000000";
                }));
        }
    }
}
