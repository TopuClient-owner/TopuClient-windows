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
    // Loader-aware version catalog.
    //
    // IMPORTANT:
    // /versions/game is NOT a loader compatibility list. It is only the
    // Minecraft release catalog. Fabric/Quilt therefore have to be checked
    // against their loader endpoint before a Minecraft version is shown.
    //
    // Forge/NeoForge use their own Maven catalogs. No reduced hard-coded
    // Minecraft version list is used here.
    public partial class MainWindow
    {
        private static readonly HttpClient DynamicVersionHttp = CreateDynamicVersionHttp();

        private static readonly object DynamicCatalogLock = new object();
        private static readonly Dictionary<string, string[]> DynamicCatalogCache =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        private CancellationTokenSource? _dynamicVersionCts;
        private bool _dynamicVersionHooksReady;

        private static HttpClient CreateDynamicVersionHttp()
        {
            HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
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
                    throw new InvalidOperationException(
                        $"The {loader} version service returned no supported Minecraft versions.");

                WriteLog($"{loader} catalog source returned {versions.Length} supported Minecraft versions.");

                string target = uiPreferred;
                if (string.IsNullOrWhiteSpace(target))
                    target = GetRuntimeProfile().Version;

                VersionBox.Items.Clear();
                foreach (string version in versions)
                    VersionBox.Items.Add(new ComboBoxItem { Content = version });

                int index = Array.FindIndex(
                    versions,
                    v => string.Equals(v, target, StringComparison.OrdinalIgnoreCase));

                VersionBox.SelectedIndex = index >= 0 ? index : 0;
                VersionBox.IsEnabled = true;

                UpdateProfileCard();
                UpdateLaunchSummary();

                WriteLog($"Dynamic {loader} catalog loaded: {versions.Length} supported Minecraft versions.");
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

        private async Task<string[]> GetDynamicVersionsAsync(
            string loader,
            CancellationToken token)
        {
            string cacheKey = loader.Trim();

            lock (DynamicCatalogLock)
            {
                if (DynamicCatalogCache.TryGetValue(cacheKey, out string[]? cached))
                    return cached;
            }

            string[] versions;

            if (loader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
            {
                versions = await GetVanillaGameVersionsAsync(token);
            }
            else if (loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
            {
                versions = await GetLoaderSupportedGameVersionsAsync(
                    "Fabric",
                    "https://meta.fabricmc.net/v2/versions/game",
                    game => $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(game)}",
                    token);
            }
            else if (loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
            {
                versions = await GetLoaderSupportedGameVersionsAsync(
                    "Quilt",
                    "https://meta.quiltmc.org/v3/versions/game",
                    game => $"https://meta.quiltmc.org/v3/versions/loader/{Uri.EscapeDataString(game)}",
                    token);
            }
            else if (loader.Equals("Forge", StringComparison.OrdinalIgnoreCase))
            {
                versions = await GetForgeGameVersionsAsync(token);
            }
            else if (loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
            {
                versions = await GetNeoForgeGameVersionsAsync(token);
            }
            else
            {
                versions = Array.Empty<string>();
            }

            lock (DynamicCatalogLock)
                DynamicCatalogCache[cacheKey] = versions;

            return versions;
        }

        private async Task<string[]> GetVanillaGameVersionsAsync(CancellationToken token)
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

        private async Task<string[]> GetLoaderSupportedGameVersionsAsync(
            string loaderName,
            string gameVersionsUrl,
            Func<string, string> loaderVersionsUrl,
            CancellationToken token)
        {
            using HttpResponseMessage response = await DynamicVersionHttp.GetAsync(
                gameVersionsUrl, token);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            string[] candidates = doc.RootElement.EnumerateArray()
                .Where(x =>
                    !x.TryGetProperty("stable", out JsonElement stable) ||
                    stable.GetBoolean())
                .Select(x => x.GetProperty("version").GetString() ?? "")
                .Where(IsMinecraftVersion)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Only advertise Minecraft versions for which this loader actually
            // publishes at least one STABLE loader build.
            using SemaphoreSlim gate = new SemaphoreSlim(4, 4);
            List<Task<string?>> checks = new List<Task<string?>>(candidates.Length);

            foreach (string gameVersion in candidates)
                checks.Add(CheckStableLoaderGameVersionAsync(
                    gameVersion, loaderVersionsUrl, gate, token));

            string?[] results = await Task.WhenAll(checks);

            string[] supported = results
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(VersionSortKey, StringComparer.Ordinal)
                .ToArray();

            WriteLog($"{loaderName}: {supported.Length} Minecraft versions have a stable loader.");
            return supported;
        }

        private async Task<string?> CheckStableLoaderGameVersionAsync(
            string gameVersion,
            Func<string, string> loaderVersionsUrl,
            SemaphoreSlim gate,
            CancellationToken token)
        {
            await gate.WaitAsync(token);
            try
            {
                using HttpResponseMessage response = await DynamicVersionHttp.GetAsync(
                    loaderVersionsUrl(gameVersion), token);

                if (!response.IsSuccessStatusCode)
                    return null;

                using JsonDocument doc = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(token));

                foreach (JsonElement entry in doc.RootElement.EnumerateArray())
                {
                    if (!entry.TryGetProperty("loader", out JsonElement loader))
                        continue;

                    if (!loader.TryGetProperty("stable", out JsonElement stable) ||
                        !stable.GetBoolean())
                        continue;

                    if (loader.TryGetProperty("version", out JsonElement version) &&
                        !string.IsNullOrWhiteSpace(version.GetString()))
                        return gameVersion;
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A missing/unsupported loader endpoint simply means that
                // Minecraft version is not supported by this loader.
                return null;
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<string[]> GetForgeGameVersionsAsync(CancellationToken token)
        {
            using HttpResponseMessage response = await DynamicVersionHttp.GetAsync(
                "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml",
                token);
            response.EnsureSuccessStatusCode();

            XDocument doc = XDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

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
            using HttpResponseMessage response = await DynamicVersionHttp.GetAsync(
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

        // NeoForge coordinates encode the Minecraft branch:
        // 21.1.x -> 1.21.1
        // 20.6.x -> 1.20.6
        // 26.1.2.x -> 26.1.2
        // 26.2.x -> 26.2
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
                return minor == 0
                    ? $"1.{major}"
                    : $"1.{major}.{minor}";

            return "";
        }

        // Returns the newest stable Fabric Loader that explicitly supports
        // this Minecraft version.
        private async Task<string> GetStableFabricLoaderVersionAsync(
            string minecraftVersion,
            CancellationToken token)
        {
            string url =
                "https://meta.fabricmc.net/v2/versions/loader/" +
                Uri.EscapeDataString(minecraftVersion);

            using HttpResponseMessage response = await DynamicVersionHttp.GetAsync(url, token);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            foreach (JsonElement entry in doc.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("loader", out JsonElement loader))
                    continue;

                if (!loader.TryGetProperty("stable", out JsonElement stable) ||
                    !stable.GetBoolean())
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

        // Returns the newest stable Quilt Loader that explicitly supports
        // this Minecraft version.
        private async Task<string> GetStableQuiltLoaderVersionAsync(
            string minecraftVersion,
            CancellationToken token)
        {
            string url =
                "https://meta.quiltmc.org/v3/versions/loader/" +
                Uri.EscapeDataString(minecraftVersion);

            using HttpResponseMessage response = await DynamicVersionHttp.GetAsync(url, token);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            foreach (JsonElement entry in doc.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("loader", out JsonElement loader))
                    continue;

                if (!loader.TryGetProperty("stable", out JsonElement stable) ||
                    !stable.GetBoolean())
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

        // Forge publishes a recommended build for many Minecraft versions.
        // Use that first; if no recommendation exists, fall back to latest.
        private async Task<string?> GetStableForgeLoaderVersionAsync(
            string minecraftVersion,
            CancellationToken token)
        {
            using HttpResponseMessage response = await DynamicVersionHttp.GetAsync(
                "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json",
                token);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            if (!doc.RootElement.TryGetProperty("promos", out JsonElement promos))
                return null;

            string recommendedKey = minecraftVersion + "-recommended";
            if (promos.TryGetProperty(recommendedKey, out JsonElement recommended))
                return recommended.GetString();

            string latestKey = minecraftVersion + "-latest";
            if (promos.TryGetProperty(latestKey, out JsonElement latest))
                return latest.GetString();

            return null;
        }

        private async Task PopulateVersionComboAsync(ComboBox combo, string loader)
        {
            try
            {
                string[] versions = await GetDynamicVersionsAsync(
                    loader, CancellationToken.None);

                combo.Items.Clear();
                foreach (string version in versions)
                    combo.Items.Add(new ComboBoxItem { Content = version });

                if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                WriteException(
                    $"CREATE PROFILE {loader} VERSION CATALOG ERROR", ex);
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
