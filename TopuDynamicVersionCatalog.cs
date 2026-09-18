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
using System.Xml;
using System.Xml.Linq;

namespace TopuLauncher
{
    // The launcher owns the version catalog. There are NO hard-coded
    // Minecraft version lists here.
    //
    // Minecraft versions are discovered from the official loader services:
    // Fabric  -> Fabric Meta
    // Quilt   -> Quilt Meta
    // Forge   -> Forge Maven metadata
    // NeoForge-> NeoForge Maven releases metadata
    //
    // Loader versions are selected separately at launch. For Fabric/Quilt the
    // newest STABLE loader that explicitly supports the selected Minecraft
    // version is used. Forge uses its recommended build (then latest), and
    // NeoForge installs the current release for that Minecraft branch.

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
            HttpClient client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(120)
            };
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

                WriteLog($"{loader} catalog returned {versions.Length} Minecraft versions.");

                string target = string.IsNullOrWhiteSpace(uiPreferred)
                    ? GetRuntimeProfile().Version
                    : uiPreferred;

                VersionBox.Items.Clear();

                foreach (string version in versions)
                    VersionBox.Items.Add(new ComboBoxItem { Content = version });

                int index = Array.FindIndex(
                    versions,
                    v => string.Equals(v, target, StringComparison.OrdinalIgnoreCase));

                VersionBox.SelectedIndex = index >= 0 ? index : 0;
                VersionBox.IsEnabled = true;

                UpdateRuntimeProfileCard();
                UpdateLaunchSummary();

                StatusText.Text = $"Loaded {versions.Length} {loader} Minecraft versions.";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                WriteException($"DYNAMIC {loader} VERSION CATALOG ERROR", ex);
                StatusText.Text =
                    $"Could not load {loader} versions. Check your internet connection.";
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
                versions = await GetFabricGameVersionsAsync(token);
            }
            else if (loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
            {
                versions = await GetQuiltGameVersionsAsync(token);
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
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                token);

            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            return doc.RootElement.GetProperty("versions")
                .EnumerateArray()
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

        private async Task<string[]> GetFabricGameVersionsAsync(CancellationToken token)
        {
            // Fabric Meta's game endpoint is the candidate list. We then
            // validate EVERY candidate against the loader endpoint and only
            // keep versions that have at least one stable Fabric Loader.
            string[] candidates = await GetStableGameCandidatesAsync(
                "https://meta.fabricmc.net/v2/versions/game",
                token);

            return await FilterByStableLoaderAsync(
                candidates,
                game => "https://meta.fabricmc.net/v2/versions/loader/" +
                        Uri.EscapeDataString(game),
                token);
        }

        private async Task<string[]> GetQuiltGameVersionsAsync(CancellationToken token)
        {
            string[] candidates = await GetStableGameCandidatesAsync(
                "https://meta.quiltmc.org/v3/versions/game",
                token);

            return await FilterByStableLoaderAsync(
                candidates,
                game => "https://meta.quiltmc.org/v3/versions/loader/" +
                        Uri.EscapeDataString(game),
                token);
        }

        private async Task<string[]> GetStableGameCandidatesAsync(
            string url,
            CancellationToken token)
        {
            using HttpResponseMessage response =
                await DynamicVersionHttp.GetAsync(url, token);

            response.EnsureSuccessStatusCode();

            using JsonDocument doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));

            return doc.RootElement
                .EnumerateArray()
                .Where(x =>
                    !x.TryGetProperty("stable", out JsonElement stable) ||
                    stable.ValueKind != JsonValueKind.False)
                .Select(x => x.TryGetProperty("version", out JsonElement version)
                    ? version.GetString() ?? ""
                    : "")
                .Where(IsMinecraftVersion)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task<string[]> FilterByStableLoaderAsync(
            IEnumerable<string> candidates,
            Func<string, string> loaderVersionsUrl,
            CancellationToken token)
        {
            string[] candidateArray = candidates.ToArray();

            // Do not sequentially wait on 100+ HTTP requests. A small bounded
            // concurrency keeps the launcher responsive while still checking
            // the complete catalog.
            using SemaphoreSlim gate = new SemaphoreSlim(8, 8);

            List<Task<string?>> checks = new List<Task<string?>>(
                candidateArray.Length);

            foreach (string gameVersion in candidateArray)
            {
                checks.Add(CheckStableLoaderGameVersionAsync(
                    gameVersion,
                    loaderVersionsUrl,
                    gate,
                    token));
            }

            string?[] results = await Task.WhenAll(checks);

            return results
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(VersionSortKey, StringComparer.Ordinal)
                .ToArray();
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
                using HttpResponseMessage response =
                    await DynamicVersionHttp.GetAsync(
                        loaderVersionsUrl(gameVersion),
                        token);

                if (!response.IsSuccessStatusCode)
                    return null;

                using JsonDocument doc = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(token));

                foreach (JsonElement entry in doc.RootElement.EnumerateArray())
                {
                    if (!entry.TryGetProperty("loader", out JsonElement loader))
                        continue;

                    if (!loader.TryGetProperty("stable", out JsonElement stable) ||
                        stable.ValueKind != JsonValueKind.True)
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
                // Unsupported/404 loader branches are simply omitted.
                return null;
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<string[]> GetForgeGameVersionsAsync(CancellationToken token)
        {
            string[] urls =
            {
                "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml",
                "https://files.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml"
            };

            XDocument? doc = null;
            Exception? lastError = null;

            foreach (string url in urls)
            {
                try
                {
                    using HttpResponseMessage response =
                        await DynamicVersionHttp.GetAsync(url, token);

                    if (!response.IsSuccessStatusCode)
                        continue;

                    doc = XDocument.Parse(
                        await response.Content.ReadAsStringAsync(token));
                    break;
                }
                catch (Exception ex) when (ex is HttpRequestException ||
                                           ex is TaskCanceledException ||
                                           ex is InvalidOperationException ||
                                           ex is XmlException)
                {
                    lastError = ex;
                }
            }

            if (doc == null)
                throw new InvalidOperationException(
                    "Forge Maven metadata could not be loaded.",
                    lastError);

            // Forge Maven versions are normally:
            //   <minecraft-version>-<forge-build>
            // Example: 1.20.1-47.1.0
            //
            // We return UNIQUE Minecraft versions, not just two hand-picked
            // versions. This restores the full Forge catalog.
            HashSet<string> versions = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (string value in doc.Descendants("version").Select(x => x.Value))
            {
                int separator = value.LastIndexOf('-');

                string minecraftVersion = separator > 0
                    ? value[..separator]
                    : value;

                if (IsMinecraftVersion(minecraftVersion))
                    versions.Add(minecraftVersion);
            }

            return versions
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

            foreach (string loaderVersion in
                     doc.Descendants("version").Select(x => x.Value))
            {
                string minecraftVersion = NeoForgeLoaderToMinecraft(loaderVersion);

                if (!string.IsNullOrWhiteSpace(minecraftVersion))
                    versions.Add(minecraftVersion);
            }

            return versions
                .OrderByDescending(VersionSortKey, StringComparer.Ordinal)
                .ToArray();
        }

        private static string NeoForgeLoaderToMinecraft(string loaderVersion)
        {
            string[] parts = loaderVersion.Split('.');

            if (parts.Length < 2)
                return "";

            if (!int.TryParse(parts[0], out int major) ||
                !int.TryParse(parts[1], out int minor))
                return "";

            // Modern NeoForge branches:
            // 26.2.x -> Minecraft 26.2
            // 26.1.x -> Minecraft 26.1
            if (major >= 26)
                return $"{major}.{minor}";

            // Legacy NeoForge branches:
            // 21.1.x -> Minecraft 1.21.1
            // 20.6.x -> Minecraft 1.20.6
            // 20.4.x -> Minecraft 1.20.4
            // 20.2.x -> Minecraft 1.20.2
            // 20.1.x -> Minecraft 1.20.1
            if (major >= 20)
                return $"{(major >= 20 ? "1." : "")}{major}.{minor}";

            return "";
        }

        // Newest STABLE Fabric Loader that explicitly supports the selected
        // Minecraft version.
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
                if (!entry.TryGetProperty("loader", out JsonElement loader))
                    continue;

                if (!loader.TryGetProperty("stable", out JsonElement stable) ||
                    stable.ValueKind != JsonValueKind.True)
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

        // Newest STABLE Quilt Loader that explicitly supports the selected
        // Minecraft version.
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

                if (!loader.TryGetProperty("stable", out JsonElement stable) ||
                    stable.ValueKind != JsonValueKind.True)
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

        // Forge exposes a recommended build per Minecraft version. If there
        // is no recommendation, use its latest build. This is still selected
        // AFTER the user chooses the Minecraft version.
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

            if (!doc.RootElement.TryGetProperty(
                    "promos",
                    out JsonElement promos))
                return null;

            string recommendedKey = minecraftVersion + "-recommended";

            if (promos.TryGetProperty(
                    recommendedKey,
                    out JsonElement recommended))
                return recommended.GetString();

            string latestKey = minecraftVersion + "-latest";

            if (promos.TryGetProperty(
                    latestKey,
                    out JsonElement latest))
                return latest.GetString();

            return null;
        }

        private async Task PopulateVersionComboAsync(
            ComboBox combo,
            string loader)
        {
            try
            {
                string[] versions = await GetDynamicVersionsAsync(
                    loader,
                    CancellationToken.None);

                combo.Items.Clear();

                foreach (string version in versions)
                    combo.Items.Add(new ComboBoxItem { Content = version });

                if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                WriteException(
                    $"CREATE PROFILE {loader} VERSION CATALOG ERROR",
                    ex);
            }
        }

        private static bool IsMinecraftVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Split('.');

            if (parts.Length < 2)
                return false;

            if (!int.TryParse(parts[0], out _) ||
                !int.TryParse(parts[1], out _))
                return false;

            // Never put snapshots, prereleases or loader suffixes in the
            // normal Minecraft selector.
            return parts.All(part =>
                part.Length > 0 &&
                part.All(char.IsDigit));
        }

        private static string VersionSortKey(string value)
        {
            return string.Join(
                ".",
                value.Split('.').Select(part =>
                {
                    string digits = new string(
                        part.TakeWhile(char.IsDigit).ToArray());

                    return int.TryParse(digits, out int number)
                        ? number.ToString("D8")
                        : "00000000";
                }));
        }
    }
}
