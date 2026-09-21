using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installers;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using XboxAuthNet.Game;
using XboxAuthNet.Game.Accounts;

namespace TopuLauncher
{
public partial class MainWindow : Window
{
private static readonly HttpClient Http = CreateHttpClient();

    private MSession? _session;
    private Process? _minecraftProcess;

    private JELoginHandler? _loginHandler;

    // ============================================================
    // TOPU CLIENT / PROFILE PATHS
    // ============================================================

    private readonly string _topuClientPath;
    private readonly string _profilesPath;

    private string _gamePath;

    private readonly string _configPath;
    private readonly string _logPath;

    private readonly string _accountsPath;
    private readonly string _selectedProfilePath;
    private readonly string _selectedAccountPath;

    private readonly object _logLock = new object();

    private const string DefaultVersion = "1.21.1";
    private const string DefaultProfileName = "default";

    private static readonly string[] SupportedVersions =
    {
        "1.21.1",
        "1.21.4",
        "1.21.8",
        "1.21.11",
        "26.1.2",
        "26.2"
    };

    private static readonly (string Slug, string Name)[] PerformanceMods =
    {
        ("fabric-api", "Fabric API"),
        ("sodium", "Sodium"),
        ("lithium", "Lithium"),
        ("dynamic-fps", "Dynamic FPS"),
        ("sodium-extra", "Sodium Extra"),
        ("krypton", "Krypton")
    };

    // ============================================================
    // PROFILE CONFIG
    // ============================================================

    private sealed class ProfileSettings
    {
        public string Version { get; set; } = DefaultVersion;
        public int RamGb { get; set; } = 4;
    }

    private sealed class LauncherAccountState
    {
        public string Type { get; set; } = "offline";
        public string Identifier { get; set; } = "";
        public string Username { get; set; } = "";
    }

    public MainWindow()
    {
        InitializeComponent();

        // ========================================================
        // TOPU CLIENT ROOT
        // ========================================================

        _topuClientPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "TopuClient");

        // ========================================================
        // PROFILES ROOT
        // ========================================================

        _profilesPath = Path.Combine(
            _topuClientPath,
            "profiles");

        Directory.CreateDirectory(_topuClientPath);
        Directory.CreateDirectory(_profilesPath);

        // ========================================================
        // LAUNCHER CONFIG / LOG
        // ========================================================

        _configPath = Path.Combine(
            _topuClientPath,
            "username.txt");

        _logPath = Path.Combine(
            _topuClientPath,
            "topu-minecraft.log");

        _accountsPath = Path.Combine(
            _topuClientPath,
            "accounts.json");

        _selectedProfilePath = Path.Combine(
            _topuClientPath,
            "selected-profile.txt");

        _selectedAccountPath = Path.Combine(
            _topuClientPath,
            "selected-account.json");

        // ========================================================
        // DEFAULT ACTIVE PROFILE
        // ========================================================

        string rememberedProfile =
            LoadRememberedProfile();

        _gamePath = GetProfileGamePath(
            rememberedProfile);

        Directory.CreateDirectory(_gamePath);

        // ========================================================
        // AUTH HANDLER
        // ========================================================

        try
        {
            _loginHandler =
                new JELoginHandlerBuilder()
                    .WithHttpClient(Http)
                    .WithAccountManager(_accountsPath)
                    .Build();

            WriteLog(
                $"Microsoft account manager initialized: {_accountsPath}");
        }
        catch (Exception ex)
        {
            WriteException(
                "MICROSOFT AUTH INITIALIZATION ERROR",
                ex);
        }

        // ========================================================
        // INITIAL UI
        // ========================================================

        LoadProfilesIntoSelector();
        LoadProfileSettings(_gamePath);

        RefreshAccountList();

        LoadRememberedAccountIntoUI();

        WriteLog("Topu Client initialized.");
        WriteLog($"Topu Client directory: {_topuClientPath}");
        WriteLog($"Profiles directory: {_profilesPath}");
        WriteLog($"Active profile directory: {_gamePath}");
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new HttpClient(
            new HttpClientHandler
            {
                AllowAutoRedirect = true
            });

        client.Timeout = TimeSpan.FromMinutes(30);

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "TopuClient/1.0");

        return client;
    }

    // ============================================================
    // PROFILE PATH MANAGEMENT
    // ============================================================

    private string GetProfileGamePath(
        string profileName)
    {
        string normalized =
            NormalizeProfileName(profileName);

        return Path.Combine(
            _profilesPath,
            normalized);
    }

    private static string NormalizeProfileName(
        string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            profileName =
                DefaultProfileName;
        }

        profileName =
            profileName.Trim();

        profileName =
            profileName.TrimStart('.');

        if (string.IsNullOrWhiteSpace(profileName))
        {
            profileName =
                DefaultProfileName;
        }

        profileName =
            profileName.ToLowerInvariant();

        foreach (char c in
                 Path.GetInvalidFileNameChars())
        {
            profileName =
                profileName.Replace(
                    c,
                    '_');
        }

        profileName =
            profileName.Trim();

        if (string.IsNullOrWhiteSpace(profileName))
        {
            profileName =
                DefaultProfileName;
        }

        return "." + profileName;
    }

    private string GetDisplayProfileName(
        string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return DefaultProfileName;

        return normalized.TrimStart('.');
    }

    private void SetActiveProfile(
        string profileName)
    {
        string normalized =
            NormalizeProfileName(profileName);

        string newGamePath =
            Path.Combine(
                _profilesPath,
                normalized);

        Directory.CreateDirectory(
            newGamePath);

        _gamePath =
            newGamePath;

        SaveRememberedProfile(
            GetDisplayProfileName(normalized));

        LoadProfileSettings(
            _gamePath);

        WriteLog(
            $"Active profile: {normalized}");

        WriteLog(
            $"Minecraft directory: {_gamePath}");

        UpdateLaunchSummary();
    }

    private string GetActiveProfileName()
    {
        if (ProfileSelector?.SelectedItem != null)
        {
            string selected = ProfileSelector.SelectedItem.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(selected))
                return selected.Trim();
        }

        string remembered = LoadRememberedProfile();
        string display = GetDisplayProfileName(Path.GetFileName(remembered));
        return string.IsNullOrWhiteSpace(display) ? DefaultProfileName : display;
    }

    // ============================================================
    // PROFILE SELECTOR
    // ============================================================

    private void LoadProfilesIntoSelector()
    {
        try
        {
            if (ProfileSelector == null)
                return;

            string current =
                GetDisplayProfileName(
                    Path.GetFileName(
                        _gamePath));

            ProfileSelector.Items.Clear();

            Directory.CreateDirectory(
                _profilesPath);

            string[] directories =
                Directory.GetDirectories(
                    _profilesPath);

            List<string> profiles =
                new List<string>();

            foreach (string directory in directories)
            {
                string name =
                    Path.GetFileName(directory);

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!name.StartsWith("."))
                    continue;

                profiles.Add(
                    GetDisplayProfileName(name));
            }

            if (!profiles.Contains(
                    DefaultProfileName,
                    StringComparer.OrdinalIgnoreCase))
            {
                profiles.Add(
                    DefaultProfileName);
            }

            foreach (string profile in
                     profiles
                         .Distinct(
                             StringComparer.OrdinalIgnoreCase)
                         .OrderBy(
                             x => x,
                             StringComparer.OrdinalIgnoreCase))
            {
                ProfileSelector.Items.Add(profile);
            }

            int index =
                ProfileSelector.Items.IndexOf(
                    current);

            if (index < 0)
            {
                index =
                    ProfileSelector.Items.IndexOf(
                        DefaultProfileName);
            }

            if (index >= 0)
            {
                ProfileSelector.SelectedIndex =
                    index;
            }

            UpdateProfileCard();
        }
        catch (Exception ex)
        {
            WriteException(
                "PROFILE LIST ERROR",
                ex);
        }
    }

    private void ProfileSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ProfileSelector?.SelectedItem == null)
            return;

        string profile =
            ProfileSelector.SelectedItem
                .ToString() ?? "";

        if (string.IsNullOrWhiteSpace(profile))
            return;

        if (!string.Equals(
                NormalizeProfileName(profile),
                NormalizeProfileName(
                    GetActiveProfileName()),
                StringComparison.OrdinalIgnoreCase))
        {
            SetActiveProfile(profile);
        }

        UpdateProfileCard();
    }

    private void CreateProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        string requested =
            NewProfileInput?.Text.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(requested))
        {
            MessageBox.Show(
                "Enter a profile name first.",
                "Topu Client",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        string normalized =
            NormalizeProfileName(requested);

        string path =
            Path.Combine(
                _profilesPath,
                normalized);

        if (Directory.Exists(path))
        {
            MessageBox.Show(
                "That profile already exists.",
                "Topu Client",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        try
        {
            Directory.CreateDirectory(path);

            SaveProfileSettings(
                path,
                new ProfileSettings
                {
                    Version = DefaultVersion,
                    RamGb = 4
                });

            NewProfileInput.Clear();

            LoadProfilesIntoSelector();

            ProfileSelector.SelectedItem =
                GetDisplayProfileName(normalized);

            SetActiveProfile(
                GetDisplayProfileName(normalized));

            StatusText.Text =
                $"Created profile: {GetDisplayProfileName(normalized)}";

            WriteLog(
                $"Created profile: {normalized}");
        }
        catch (Exception ex)
        {
            WriteException(
                "PROFILE CREATE ERROR",
                ex);

            MessageBox.Show(
                ex.Message,
                "Profile Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DeleteProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        string profile =
            GetActiveProfileName();

        if (string.Equals(
                NormalizeProfileName(profile),
                NormalizeProfileName(DefaultProfileName),
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "The default profile cannot be deleted.",
                "Topu Client",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        MessageBoxResult result =
            MessageBox.Show(
                $"Delete profile '{profile}'?\n\nAll Minecraft files inside this profile will be deleted.",
                "Delete Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            string path =
                GetProfileGamePath(profile);

            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    true);
            }

            SetActiveProfile(
                DefaultProfileName);

            LoadProfilesIntoSelector();

            StatusText.Text =
                "Profile deleted.";

            WriteLog(
                $"Deleted profile: {profile}");
        }
        catch (Exception ex)
        {
            WriteException(
                "PROFILE DELETE ERROR",
                ex);

            MessageBox.Show(
                ex.Message,
                "Profile Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string GetProfileSettingsPath(
        string gamePath)
    {
        return Path.Combine(
            gamePath,
            "topu-profile.json");
    }

    private void LoadProfileSettings(
        string gamePath)
    {
        try
        {
            string path =
                GetProfileSettingsPath(gamePath);

            ProfileSettings settings;

            if (File.Exists(path))
            {
                string json =
                    File.ReadAllText(path);

                settings =
                    JsonSerializer.Deserialize<ProfileSettings>(
                        json)
                    ?? new ProfileSettings();
            }
            else
            {
                settings =
                    new ProfileSettings();

                SaveProfileSettings(
                    gamePath,
                    settings);
            }

            string version =
                SupportedVersions.Contains(
                    settings.Version)
                    ? settings.Version
                    : DefaultVersion;

            int ram =
                Math.Clamp(
                    settings.RamGb,
                    2,
                    12);

            VersionBox.SelectedItem =
                VersionBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(
                        item =>
                            string.Equals(
                                item.Content?.ToString(),
                                version,
                                StringComparison.OrdinalIgnoreCase));

            if (VersionBox.SelectedItem == null)
                VersionBox.SelectedIndex = 0;

            RamSlider.Value =
                ram;

            if (RamLabel != null)
            {
                RamLabel.Text =
                    $"{ram}GB";
            }

            UpdateProfileCard();
        }
        catch (Exception ex)
        {
            WriteException(
                "PROFILE SETTINGS LOAD ERROR",
                ex);
        }
    }

    private void SaveProfileSettings(
        string gamePath,
        ProfileSettings settings)
    {
        try
        {
            Directory.CreateDirectory(
                gamePath);

            string path =
                GetProfileSettingsPath(
                    gamePath);

            JsonSerializerOptions options =
                new JsonSerializerOptions
                {
                    WriteIndented = true
                };

            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    settings,
                    options));
        }
        catch (Exception ex)
        {
            WriteException(
                "PROFILE SETTINGS SAVE ERROR",
                ex);

            throw;
        }
    }

    private void SaveRememberedProfile(
        string profileName)
    {
        try
        {
            File.WriteAllText(
                _selectedProfilePath,
                NormalizeProfileName(
                    profileName));
        }
        catch (Exception ex)
        {
            WriteException(
                "REMEMBER PROFILE ERROR",
                ex);
        }
    }

    private string LoadRememberedProfile()
    {
        try
        {
            if (!File.Exists(
                    _selectedProfilePath))
            {
                string defaultPath =
                    GetProfileGamePath(
                        DefaultProfileName);

                Directory.CreateDirectory(
                    defaultPath);

                return DefaultProfileName;
            }

            string saved =
                File.ReadAllText(
                    _selectedProfilePath)
                    .Trim();

            if (string.IsNullOrWhiteSpace(saved))
                return DefaultProfileName;

            string normalized =
                NormalizeProfileName(saved);

            Directory.CreateDirectory(
                Path.Combine(
                    _profilesPath,
                    normalized));

            return GetDisplayProfileName(
                normalized);
        }
        catch
        {
            return DefaultProfileName;
        }
    }

    private void UpdateProfileCard()
    {
        try
        {
            string profile =
                GetActiveProfileName();

            string version =
                GetSelectedVersion();

            int ram =
                (int)RamSlider.Value;

            if (SelectedProfileLabel != null)
            {
                SelectedProfileLabel.Text =
                    $"● {profile}   •   Fabric {version}   •   {ram}GB RAM";
            }

            if (LaunchProfileLabel != null)
            {
                LaunchProfileLabel.Text =
                    profile;
            }
        }
        catch
        {
        }
    }

    // ============================================================
    // LOGGING
    // ============================================================

    private void WriteLog(string message)
    {
        try
        {
            Directory.CreateDirectory(
                _topuClientPath);

            lock (_logLock)
            {
                File.AppendAllText(
                    _logPath,
                    $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    private void WriteException(
        string title,
        Exception ex)
    {
        WriteLog("");
        WriteLog($"===== {title} =====");
        WriteLog(ex.ToString());
    }

    private void StartLaunchLog()
    {
        try
        {
            Directory.CreateDirectory(
                _topuClientPath);

            lock (_logLock)
            {
                File.WriteAllText(
                    _logPath,
                    "===== TOPU CLIENT MINECRAFT LOG =====" +
                    Environment.NewLine +
                    $"Started: {DateTime.Now:O}" +
                    Environment.NewLine +
                    Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    private void AppendGameLog(string message)
    {
        WriteLog(message);
    }

    private void AppendRawGameOutput(
        string prefix,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            lock (_logLock)
            {
                Directory.CreateDirectory(
                    _topuClientPath);

                File.AppendAllText(
                    _logPath,
                    $"[{DateTime.Now:HH:mm:ss}] {prefix} {text}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    // ============================================================
    // ACCOUNTS
    // ============================================================

    private void RefreshAccountList()
    {
        try
        {
            if (AccountSelector == null)
                return;

            string previous =
                GetSelectedAccountIdentifier();

            AccountSelector.Items.Clear();

            // Offline accounts
            foreach (string username in
                     LoadOfflineAccounts())
            {
                AccountSelector.Items.Add(
                    new AccountDisplayItem
                    {
                        Type = "Offline",
                        Username = username,
                        Identifier = "offline:" + username
                    });
            }

            // Microsoft accounts
            if (_loginHandler != null)
            {
                try
                {
                    foreach (IXboxGameAccount account in
                             _loginHandler.AccountManager
                                .GetAccounts()
                                .OfType<IXboxGameAccount>())
                    {
                        string username =
                            account.Identifier
                            ?? "Microsoft Account";

                        AccountSelector.Items.Add(
                            new AccountDisplayItem
                            {
                                Type = "Microsoft",
                                Username = username,
                                Identifier =
                                    account.Identifier
                            });
                    }
                }
                catch (Exception ex)
                {
                    WriteException(
                        "MICROSOFT ACCOUNT LIST ERROR",
                        ex);
                }
            }

            if (AccountSelector.Items.Count == 0)
            {
                AccountSelector.SelectedIndex = -1;

                UpdateAccountCard();

                return;
            }

            int index = -1;

            if (!string.IsNullOrWhiteSpace(previous))
            {
                for (int i = 0;
                     i < AccountSelector.Items.Count;
                     i++)
                {
                    if (AccountSelector.Items[i]
                            is AccountDisplayItem item &&
                        string.Equals(
                            item.Identifier,
                            previous,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }

            if (index < 0)
            {
                LauncherAccountState state =
                    LoadRememberedAccount();

                if (!string.IsNullOrWhiteSpace(
                        state.Identifier))
                {
                    for (int i = 0;
                         i < AccountSelector.Items.Count;
                         i++)
                    {
                        if (AccountSelector.Items[i]
                                is AccountDisplayItem item &&
                            string.Equals(
                                item.Identifier,
                                state.Identifier,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }
            }

            if (index < 0)
                index = 0;

            AccountSelector.SelectedIndex =
                index;

            UpdateAccountCard();
        }
        catch (Exception ex)
        {
            WriteException(
                "ACCOUNT LIST REFRESH ERROR",
                ex);
        }
    }

    private sealed class AccountDisplayItem
    {
        public string Type { get; set; } = "";
        public string Username { get; set; } = "";
        public string Identifier { get; set; } = "";

        public override string ToString()
        {
            return
                Type +
                "  •  " +
                Username;
        }
    }

    private string GetSelectedAccountIdentifier()
    {
        if (AccountSelector?.SelectedItem
                is AccountDisplayItem item)
        {
            return item.Identifier;
        }

        return "";
    }

    private void AccountSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (AccountSelector?.SelectedItem
                is not AccountDisplayItem item)
        {
            return;
        }

        SaveRememberedAccount(
            new LauncherAccountState
            {
                Type =
                    item.Type.Equals(
                        "Microsoft",
                        StringComparison.OrdinalIgnoreCase)
                        ? "microsoft"
                        : "offline",

                Identifier =
                    item.Identifier,

                Username =
                    item.Username
            });

        UpdateAccountCard();

        StatusText.Text =
            $"Selected account: {item.Username}";
    }

    private void UpdateAccountCard()
    {
        try
        {
            if (AccountSelector?.SelectedItem
                    is AccountDisplayItem item)
            {
                if (SelectedAccountLabel != null)
                {
                    SelectedAccountLabel.Text =
                        item.Username;
                }

                if (SelectedAccountTypeLabel != null)
                {
                    SelectedAccountTypeLabel.Text =
                        item.Type +
                        " Account";
                }

                if (LaunchAccountLabel != null)
                {
                    LaunchAccountLabel.Text =
                        item.Username;
                }

                if (LaunchAccountTypeLabel != null)
                {
                    LaunchAccountTypeLabel.Text =
                        item.Type +
                        " Account";
                }
            }
            else
            {
                if (SelectedAccountLabel != null)
                    SelectedAccountLabel.Text =
                        "No account selected";

                if (SelectedAccountTypeLabel != null)
                    SelectedAccountTypeLabel.Text =
                        "Add an account below";

                if (LaunchAccountLabel != null)
                    LaunchAccountLabel.Text =
                        "No account";

                if (LaunchAccountTypeLabel != null)
                    LaunchAccountTypeLabel.Text =
                        "Add an account in Accounts";
            }
        }
        catch
        {
        }
    }

    private List<string> LoadOfflineAccounts()
    {
        try
        {
            string path =
                Path.Combine(
                    _topuClientPath,
                    "offline-accounts.json");

            if (!File.Exists(path))
                return new List<string>();

            string json =
                File.ReadAllText(path);

            return JsonSerializer.Deserialize<List<string>>(
                       json)
                   ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private void SaveOfflineAccounts(
        List<string> accounts)
    {
        try
        {
            string path =
                Path.Combine(
                    _topuClientPath,
                    "offline-accounts.json");

            JsonSerializerOptions options =
                new JsonSerializerOptions
                {
                    WriteIndented = true
                };

            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    accounts
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    options));
        }
        catch (Exception ex)
        {
            WriteException(
                "OFFLINE ACCOUNT SAVE ERROR",
                ex);
        }
    }

    private void AddOfflineAccount_Click(
        object sender,
        RoutedEventArgs e)
    {
        string username =
            OfflineUsernameInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(username))
        {
            MessageBox.Show(
                "Enter an offline username.",
                "Topu Client",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (username.Length > 16)
        {
            MessageBox.Show(
                "Minecraft usernames cannot be longer than 16 characters.",
                "Topu Client",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        List<string> accounts =
            LoadOfflineAccounts();

        if (!accounts.Contains(
                username,
                StringComparer.OrdinalIgnoreCase))
        {
            accounts.Add(username);
        }

        SaveOfflineAccounts(accounts);

        OfflineUsernameInput.Clear();

        RefreshAccountList();

        for (int i = 0;
             i < AccountSelector.Items.Count;
             i++)
        {
            if (AccountSelector.Items[i]
                    is AccountDisplayItem item &&
                item.Type == "Offline" &&
                string.Equals(
                    item.Username,
                    username,
                    StringComparison.OrdinalIgnoreCase))
            {
                AccountSelector.SelectedIndex =
                    i;

                break;
            }
        }

        StatusText.Text =
            $"Offline account added: {username}";

        WriteLog(
            $"Offline account added: {username}");
    }

    private async void MsLoginBtn_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_loginHandler == null)
        {
            MessageBox.Show(
                "Microsoft authentication could not be initialized.",
                "Topu Client",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        MsLoginBtn.IsEnabled =
            false;

        try
        {
            StatusText.Text =
                "Opening Microsoft login...";

            WriteLog(
                "Starting Microsoft interactive login.");

            MSession session =
                await _loginHandler
                    .AuthenticateInteractively();

            _session =
                session;

            WriteLog(
                $"Microsoft login successful: {session.Username}");

            RefreshAccountList();

            for (int i = 0;
                 i < AccountSelector.Items.Count;
                 i++)
            {
                if (AccountSelector.Items[i]
                        is AccountDisplayItem item &&
                    item.Type == "Microsoft" &&
                    string.Equals(
                        item.Username,
                        session.Username,
                        StringComparison.OrdinalIgnoreCase))
                {
                    AccountSelector.SelectedIndex =
                        i;

                    break;
                }
            }

            SaveRememberedAccount(
                new LauncherAccountState
                {
                    Type = "microsoft",
                    Identifier =
                        GetSelectedAccountIdentifier(),
                    Username =
                        session.Username
                });

            StatusText.Text =
                $"Microsoft account added: {session.Username}";

            MessageBox.Show(
                $"Microsoft account added successfully.\n\n{session.Username}",
                "Topu Client",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WriteException(
                "MICROSOFT LOGIN ERROR",
                ex);

            StatusText.Text =
                "Microsoft login failed.";

            MessageBox.Show(
                "Microsoft login failed.\n\n" +
                ex.Message,
                "Microsoft Login",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            MsLoginBtn.IsEnabled =
                true;
        }
    }

    private async void RemoveAccount_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (AccountSelector?.SelectedItem
                is not AccountDisplayItem item)
        {
            MessageBox.Show(
                "Select an account first.",
                "Topu Client",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        MessageBoxResult result =
            MessageBox.Show(
                $"Remove account '{item.Username}'?",
                "Remove Account",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            if (item.Type == "Offline")
            {
                List<string> accounts =
                    LoadOfflineAccounts();

                accounts.RemoveAll(
                    x =>
                        string.Equals(
                            x,
                            item.Username,
                            StringComparison.OrdinalIgnoreCase));

                SaveOfflineAccounts(accounts);
            }
            else if (_loginHandler != null)
            {
                IXboxGameAccount? account =
                    _loginHandler.AccountManager
                        .GetAccounts()
                        .OfType<IXboxGameAccount>()
                        .FirstOrDefault(
                            x =>
                                string.Equals(
                                    x.Identifier,
                                    item.Identifier,
                                    StringComparison.OrdinalIgnoreCase));

                if (account != null)
                {
                    await _loginHandler.Signout(
                        account);
                }
            }

            SaveRememberedAccount(
                new LauncherAccountState());

            RefreshAccountList();

            StatusText.Text =
                "Account removed.";
        }
        catch (Exception ex)
        {
            WriteException(
                "ACCOUNT REMOVE ERROR",
                ex);

            MessageBox.Show(
                ex.Message,
                "Account Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveRememberedAccount(
        LauncherAccountState state)
    {
        try
        {
            JsonSerializerOptions options =
                new JsonSerializerOptions
                {
                    WriteIndented = true
                };

            File.WriteAllText(
                _selectedAccountPath,
                JsonSerializer.Serialize(
                    state,
                    options));
        }
        catch (Exception ex)
        {
            WriteException(
                "REMEMBER ACCOUNT ERROR",
                ex);
        }
    }

    private LauncherAccountState LoadRememberedAccount()
    {
        try
        {
            if (!File.Exists(
                    _selectedAccountPath))
            {
                return new LauncherAccountState();
            }

            string json =
                File.ReadAllText(
                    _selectedAccountPath);

            return JsonSerializer.Deserialize<LauncherAccountState>(
                       json)
                   ?? new LauncherAccountState();
        }
        catch
        {
            return new LauncherAccountState();
        }
    }

    private void LoadRememberedAccountIntoUI()
    {
        try
        {
            LauncherAccountState state =
                LoadRememberedAccount();

            if (string.IsNullOrWhiteSpace(
                    state.Identifier))
            {
                UpdateAccountCard();
                return;
            }

            for (int i = 0;
                 i < AccountSelector.Items.Count;
                 i++)
            {
                if (AccountSelector.Items[i]
                        is AccountDisplayItem item &&
                    string.Equals(
                        item.Identifier,
                        state.Identifier,
                        StringComparison.OrdinalIgnoreCase))
                {
                    AccountSelector.SelectedIndex =
                        i;

                    return;
                }
            }

            UpdateAccountCard();
        }
        catch
        {
        }
    }

    private async Task<MSession?> AuthenticateSelectedAccountAsync()
    {
        if (AccountSelector?.SelectedItem
                is not AccountDisplayItem item)
        {
            throw new InvalidOperationException(
                "No account is selected. Add an account in the Accounts tab.");
        }

        if (item.Type == "Offline")
        {
            return MSession.CreateOfflineSession(
                item.Username);
        }

        if (_loginHandler == null)
        {
            throw new InvalidOperationException(
                "Microsoft authentication is not initialized.");
        }

        IXboxGameAccount? account =
            _loginHandler.AccountManager
                .GetAccounts()
                .OfType<IXboxGameAccount>()
                .FirstOrDefault(
                    x =>
                        string.Equals(
                            x.Identifier,
                            item.Identifier,
                            StringComparison.OrdinalIgnoreCase));

        if (account == null)
        {
            throw new InvalidOperationException(
                "The selected Microsoft account is no longer available.");
        }

        StatusText.Text =
            $"Authenticating {item.Username}...";

        WriteLog(
            $"Authenticating saved Microsoft account: {item.Username}");

        MSession session =
            await _loginHandler.Authenticate(
                account);

        SaveRememberedAccount(
            new LauncherAccountState
            {
                Type = "microsoft",
                Identifier = item.Identifier,
                Username = session.Username
            });

        return session;
    }

    // ============================================================
    // USERNAME
    // ============================================================

    private void LoadUsername()
    {
        // Kept for compatibility with older Topu Client installs.
        // Account management is now handled by the Accounts tab.
    }

    private void SaveUsername(
        string username)
    {
        // Kept for compatibility with older Topu Client code.
        try
        {
            File.WriteAllText(
                _configPath,
                username);
        }
        catch (Exception ex)
        {
            WriteException(
                "USERNAME SAVE ERROR",
                ex);
        }
    }

    // ============================================================
    // WINDOW
    // ============================================================

    private void TitleBar_MouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private void Minimize_Click(
        object sender,
        RoutedEventArgs e)
    {
        WindowState =
            WindowState.Minimized;
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (_minecraftProcess != null)
            {
                try
                {
                    if (!_minecraftProcess.HasExited)
                    {
                        _minecraftProcess.CloseMainWindow();
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        Close();
    }

    // ============================================================
    // TABS
    // ============================================================

    private void SwitchTab_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        string tab =
            button.Tag?.ToString() ?? "";

        TabLaunch.Visibility =
            Visibility.Collapsed;

        TabProfiles.Visibility =
            Visibility.Collapsed;

        TabAccounts.Visibility =
            Visibility.Collapsed;

        Brush inactive =
            new SolidColorBrush(
                Color.FromRgb(
                    136,
                    136,
                    136));

        Brush active =
            new SolidColorBrush(
                Color.FromRgb(
                    0,
                    255,
                    136));

        TabLaunchBtn.Foreground =
            inactive;

        TabProfilesBtn.Foreground =
            inactive;

        TabAccountsBtn.Foreground =
            inactive;

        TabLaunchBtn.BorderThickness =
            new Thickness(0);

        TabProfilesBtn.BorderThickness =
            new Thickness(0);

        TabAccountsBtn.BorderThickness =
            new Thickness(0);

        button.Foreground =
            active;

        button.BorderThickness =
            new Thickness(
                0,
                0,
                0,
                2);

        switch (tab)
        {
            case "TabLaunch":
                TabLaunch.Visibility =
                    Visibility.Visible;
                break;

            case "TabProfiles":
                TabProfiles.Visibility =
                    Visibility.Visible;
                break;

            case "TabAccounts":
                TabAccounts.Visibility =
                    Visibility.Visible;
                break;
        }
    }

    // ============================================================
    // RAM
    // ============================================================

    private void RamSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (RamLabel != null)
        {
            RamLabel.Text =
                $"{(int)e.NewValue}GB";
        }

        UpdateProfileCard();
    }

    // ============================================================
    // VERSION
    // ============================================================

    private string GetSelectedVersion()
    {
        string version =
            (VersionBox.SelectedItem as ComboBoxItem)
            ?.Content
            ?.ToString()
            ?.Trim()
            ?? "";

        if (string.IsNullOrWhiteSpace(version))
            return DefaultVersion;

        return version;
    }

    private int GetRequiredJavaMajor(
        string minecraftVersion)
    {
        if (minecraftVersion.StartsWith(
                "26.",
                StringComparison.OrdinalIgnoreCase))
        {
            return 25;
        }

        return 21;
    }

    // ============================================================
    // PROFILE
    // ============================================================

    private void SaveProfile_Click(
        object sender,
        RoutedEventArgs e)
    {
        string profileName =
            GetActiveProfileName();

        string normalizedProfile =
            NormalizeProfileName(
                profileName);

        // Do NOT call SetActiveProfile() here.
        // SetActiveProfile() reloads topu-profile.json and would overwrite
        // the version/RAM values the user just selected in the UI.
        // _gamePath already points to the active profile directory.

        string version =
            GetSelectedVersion();

        int ram =
            (int)RamSlider.Value;

        SaveProfileSettings(
            _gamePath,
            new ProfileSettings
            {
                Version = version,
                RamGb = ram
            });

        SelectedProfileLabel.Text =
            $"● {GetDisplayProfileName(normalizedProfile)}   •   Fabric {version}   •   {ram}GB RAM";

        StatusText.Text =
            $"Profile saved: {GetDisplayProfileName(normalizedProfile)}";

        WriteLog(
            $"Profile saved: {normalizedProfile}");

        WriteLog(
            $"Profile Minecraft directory: {_gamePath}");

        WriteLog(
            $"Profile version: {version}");

        WriteLog(
            $"Profile RAM: {ram}GB");
    }

    // ============================================================
    // AUTH MODE
    // ============================================================

    private void AuthTypeBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (StatusText != null)
            StatusText.Text = "Use the Accounts tab to select your account.";
    }

    // ============================================================
    // SERVER
    // ============================================================

    private void JoinServer_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        string server =
            button.Tag?.ToString() ?? "";

        StatusText.Text =
            $"Server selected: {server}";

        WriteLog(
            $"Server selected: {server}");
    }

    // ============================================================
    // MODRINTH
    // ============================================================

    private async void SearchModrinth_Click(
        object sender,
        RoutedEventArgs e)
    {
        string query =
            ModSearchInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show(
                "Enter a mod name first.",
                "Modrinth",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        try
        {
            SetActiveProfile(
                GetActiveProfileName());

            string minecraftVersion =
                GetSelectedVersion();

            ModSearchStatus.Text =
                $"Searching Modrinth for {query}...";

            string url =
                "https://api.modrinth.com/v2/search" +
                "?query=" +
                Uri.EscapeDataString(query) +
                "&facets=%5B%5B%22project_type%3Amod%22%5D%5D";

            using HttpResponseMessage response =
                await Http.GetAsync(url);

            response.EnsureSuccessStatusCode();

            string json =
                await response.Content.ReadAsStringAsync();

            using JsonDocument doc =
                JsonDocument.Parse(json);

            JsonElement hits =
                doc.RootElement.GetProperty("hits");

            if (hits.GetArrayLength() == 0)
            {
                ModSearchStatus.Text =
                    "No mod found.";
                return;
            }

            JsonElement hit =
                hits[0];

            string projectId =
                hit.GetProperty("project_id")
                    .GetString()
                ?? "";

            string title =
                hit.GetProperty("title")
                    .GetString()
                ?? query;

            await DownloadModByProjectIdAsync(
                projectId,
                title,
                minecraftVersion);

            ModSearchStatus.Text =
                $"Installed: {title}";
        }
        catch (Exception ex)
        {
            WriteException(
                "MODRINTH SEARCH ERROR",
                ex);

            ModSearchStatus.Text =
                "Modrinth download failed.";

            MessageBox.Show(
                ex.Message,
                "Modrinth Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task DownloadModByProjectIdAsync(
        string projectId,
        string title,
        string minecraftVersion)
    {
        string url =
            "https://api.modrinth.com/v2/project/" +
            Uri.EscapeDataString(projectId) +
            "/version" +
            "?loaders=%5B%22fabric%22%5D" +
            "&game_versions=%5B%22" +
            Uri.EscapeDataString(minecraftVersion) +
            "%22%5D";

        using HttpResponseMessage response =
            await Http.GetAsync(url);

        response.EnsureSuccessStatusCode();

        string json =
            await response.Content.ReadAsStringAsync();

        using JsonDocument doc =
            JsonDocument.Parse(json);

        JsonElement versions =
            doc.RootElement;

        if (versions.ValueKind !=
                JsonValueKind.Array ||
            versions.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"{title} has no compatible Fabric build for {minecraftVersion}.");
        }

        JsonElement? selectedFile = null;

        foreach (JsonElement version in
                 versions.EnumerateArray())
        {
            if (!version.TryGetProperty(
                    "files",
                    out JsonElement files))
            {
                continue;
            }

            selectedFile =
                FindPrimaryJar(files);

            if (selectedFile != null)
                break;
        }

        if (selectedFile == null)
        {
            throw new InvalidOperationException(
                $"No JAR file was returned for {title}.");
        }

        JsonElement file =
            selectedFile.Value;

        string downloadUrl =
            file.GetProperty("url")
                .GetString()
            ?? throw new InvalidOperationException(
                $"No download URL was returned for {title}.");

        string filename =
            file.GetProperty("filename")
                .GetString()
            ?? $"{SanitizeFileName(title)}.jar";

        string modsFolder =
            Path.Combine(
                _gamePath,
                "mods");

        Directory.CreateDirectory(
            modsFolder);

        string destination =
            Path.Combine(
                modsFolder,
                SanitizeFileName(filename));

        StatusText.Text =
            $"Downloading {title}...";

        await DownloadFileAsync(
            downloadUrl,
            destination);

        WriteLog(
            $"Installed Modrinth mod: {title}");

        WriteLog(
            $"Mod profile directory: {_gamePath}");
    }

    // ============================================================
    // FABRIC INSTALLATION
    // ============================================================

    private async Task<string> InstallFabricAsync(
        string minecraftVersion,
        MinecraftPath minecraftPath)
    {
        StatusText.Text =
            $"Installing Fabric for Minecraft {minecraftVersion}...";

        WriteLog(
            "===== FABRIC INSTALLATION START =====");

        WriteLog(
            $"Minecraft version: {minecraftVersion}");

        WriteLog(
            $"Fabric MinecraftPath: {_gamePath}");

        FabricInstaller fabricInstaller =
            new FabricInstaller(Http);

        string fabricVersionName =
            await fabricInstaller.Install(
                minecraftVersion,
                minecraftPath);

        if (string.IsNullOrWhiteSpace(
                fabricVersionName))
        {
            throw new InvalidOperationException(
                "Fabric installer returned an empty version name.");
        }

        WriteLog(
            $"Fabric installer returned: {fabricVersionName}");

        string fabricDirectory =
            Path.Combine(
                _gamePath,
                "versions",
                fabricVersionName);

        Directory.CreateDirectory(
            fabricDirectory);

        string fabricJson =
            Path.Combine(
                fabricDirectory,
                fabricVersionName + ".json");

        if (!File.Exists(fabricJson))
        {
            throw new FileNotFoundException(
                "Fabric installer did not create the Fabric version JSON.",
                fabricJson);
        }

        WriteLog(
            $"Fabric profile: {fabricJson}");

        await RepairFabricProfileAsync(
            fabricVersionName,
            fabricJson);

        if (!ValidateFabricInstallation(
                fabricVersionName,
                minecraftPath))
        {
            throw new InvalidOperationException(
                "Fabric installation is incomplete. Check topu-minecraft.log.");
        }

        WriteLog(
            "===== FABRIC INSTALLATION COMPLETE =====");

        return fabricVersionName;
    }

    // ============================================================
    // FABRIC PROFILE REPAIR
    // ============================================================

    private async Task RepairFabricProfileAsync(
        string fabricVersionName,
        string fabricJsonPath)
    {
        WriteLog(
            "===== FABRIC PROFILE CHECK =====");

        string json =
            await File.ReadAllTextAsync(
                fabricJsonPath);

        using JsonDocument document =
            JsonDocument.Parse(json);

        JsonElement root =
            document.RootElement;

        if (!root.TryGetProperty(
                "libraries",
                out JsonElement libraries) ||
            libraries.ValueKind !=
                JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Fabric profile has no libraries array.");
        }

        int checkedLibraries = 0;
        int downloadedLibraries = 0;

        foreach (JsonElement library in
                 libraries.EnumerateArray())
        {
            if (!library.TryGetProperty(
                    "name",
                    out JsonElement nameElement))
            {
                continue;
            }

            string coordinate =
                nameElement.GetString() ?? "";

            if (string.IsNullOrWhiteSpace(
                    coordinate))
                continue;

            string[] parts =
                coordinate.Split(':');

            if (parts.Length < 3)
                continue;

            string group = parts[0];
            string artifact = parts[1];
            string version = parts[2];

            string relativePath =
                group.Replace(
                    '.',
                    Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar +
                artifact +
                Path.DirectorySeparatorChar +
                version +
                Path.DirectorySeparatorChar +
                artifact +
                "-" +
                version +
                ".jar";

            string destination =
                Path.Combine(
                    _gamePath,
                    "libraries",
                    relativePath);

            checkedLibraries++;

            if (File.Exists(destination) &&
                new FileInfo(destination).Length > 0)
            {
                continue;
            }

            string? url =
                GetLibraryUrl(
                    library,
                    coordinate,
                    relativePath);

            if (string.IsNullOrWhiteSpace(url))
            {
                WriteLog(
                    $"WARNING: Cannot determine URL for {coordinate}");

                continue;
            }

            WriteLog(
                $"Missing Fabric library: {coordinate}");

            StatusText.Text =
                $"Downloading {artifact}-{version}.jar";

            await DownloadFileAsync(
                url,
                destination);

            if (!File.Exists(destination) ||
                new FileInfo(destination).Length == 0)
            {
                throw new IOException(
                    $"Fabric library failed verification: {destination}");
            }

            downloadedLibraries++;

            WriteLog(
                $"Installed Fabric library: {destination}");
        }

        WriteLog(
            $"Fabric libraries checked: {checkedLibraries}");

        WriteLog(
            $"Fabric libraries downloaded: {downloadedLibraries}");

        string? loaderJar =
            await EnsureFabricLoaderAsync(
                libraries);

        if (loaderJar == null)
        {
            throw new FileNotFoundException(
                "Fabric Loader JAR could not be installed.");
        }

        WriteLog(
            $"Fabric Loader JAR verified: {loaderJar}");

        string fabricDirectory =
            Path.Combine(
                _gamePath,
                "versions",
                fabricVersionName);

        string legacyFabricVersionJar =
            Path.Combine(
                fabricDirectory,
                fabricVersionName + ".jar");

        RemoveLegacyFabricVersionJar(
            legacyFabricVersionJar);

        WriteLog(
            "Fabric profile repair completed.");
    }

    // ============================================================
    // FABRIC LOADER
    // ============================================================

    private async Task<string?> EnsureFabricLoaderAsync(
        JsonElement libraries)
    {
        foreach (JsonElement library in
                 libraries.EnumerateArray())
        {
            if (!library.TryGetProperty(
                    "name",
                    out JsonElement nameElement))
            {
                continue;
            }

            string coordinate =
                nameElement.GetString() ?? "";

            if (!coordinate.StartsWith(
                    "net.fabricmc:fabric-loader:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] parts =
                coordinate.Split(':');

            if (parts.Length < 3)
                continue;

            string version =
                parts[2];

            string relativePath =
                Path.Combine(
                    "net",
                    "fabricmc",
                    "fabric-loader",
                    version,
                    $"fabric-loader-{version}.jar");

            string path =
                Path.Combine(
                    _gamePath,
                    "libraries",
                    relativePath);

            WriteLog(
                $"Expected Fabric Loader JAR: {path}");

            if (File.Exists(path) &&
                new FileInfo(path).Length > 0)
            {
                if (JarContainsEntry(
                        path,
                        "net/fabricmc/loader/impl/launch/knot/KnotClient.class"))
                {
                    WriteLog(
                        $"Fabric Loader verified: {path}");

                    return path;
                }

                WriteLog(
                    "Existing Fabric Loader JAR does not contain KnotClient.");
            }

            string? url =
                GetLibraryUrl(
                    library,
                    coordinate,
                    relativePath.Replace(
                        Path.DirectorySeparatorChar,
                        '/'));

            if (string.IsNullOrWhiteSpace(url))
            {
                WriteLog(
                    $"Could not determine Fabric Loader URL: {coordinate}");

                continue;
            }

            WriteLog(
                $"Downloading Fabric Loader: {url}");

            StatusText.Text =
                "Downloading Fabric Loader...";

            await DownloadFileAsync(
                url,
                path);

            if (!File.Exists(path) ||
                new FileInfo(path).Length <= 0)
            {
                throw new IOException(
                    $"Fabric Loader download failed: {path}");
            }

            if (!JarContainsEntry(
                    path,
                    "net/fabricmc/loader/impl/launch/knot/KnotClient.class"))
            {
                throw new InvalidDataException(
                    $"Downloaded Fabric Loader does not contain KnotClient: {path}");
            }

            WriteLog(
                $"Fabric Loader installed and verified: {path}");

            return path;
        }

        string loaderRoot =
            Path.Combine(
                _gamePath,
                "libraries",
                "net",
                "fabricmc",
                "fabric-loader");

        if (Directory.Exists(loaderRoot))
        {
            string[] jars =
                Directory.GetFiles(
                    loaderRoot,
                    "fabric-loader-*.jar",
                    SearchOption.AllDirectories);

            foreach (string jar in jars)
            {
                try
                {
                    if (File.Exists(jar) &&
                        new FileInfo(jar).Length > 0 &&
                        JarContainsEntry(
                            jar,
                            "net/fabricmc/loader/impl/launch/knot/KnotClient.class"))
                    {
                        WriteLog(
                            $"Found verified Fabric Loader JAR: {jar}");

                        return jar;
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private static bool JarContainsEntry(
        string jarPath,
        string entryName)
    {
        try
        {
            using FileStream stream =
                new FileStream(
                    jarPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

            using ZipArchive archive =
                new ZipArchive(
                    stream,
                    ZipArchiveMode.Read,
                    false);

            foreach (ZipArchiveEntry entry in
                     archive.Entries)
            {
                if (string.Equals(
                    entry.FullName,
                    entryName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    // LIBRARY URL
    // ============================================================

    private string? GetLibraryUrl(
        JsonElement library,
        string coordinate,
        string relativePath)
    {
        try
        {
            if (library.TryGetProperty(
                    "downloads",
                    out JsonElement downloads) &&
                downloads.ValueKind ==
                    JsonValueKind.Object &&
                downloads.TryGetProperty(
                    "artifact",
                    out JsonElement artifact) &&
                artifact.ValueKind ==
                    JsonValueKind.Object &&
                artifact.TryGetProperty(
                    "url",
                    out JsonElement artifactUrl))
            {
                string? url =
                    artifactUrl.GetString();

                if (!string.IsNullOrWhiteSpace(url))
                    return url;
            }

            if (library.TryGetProperty(
                    "url",
                    out JsonElement urlElement))
            {
                string? baseUrl =
                    urlElement.GetString();

                if (!string.IsNullOrWhiteSpace(baseUrl))
                {
                    return baseUrl.TrimEnd('/') +
                           "/" +
                           relativePath.Replace(
                               '\\',
                               '/');
                }
            }

            if (coordinate.StartsWith(
                    "net.fabricmc:",
                    StringComparison.OrdinalIgnoreCase) ||
                coordinate.StartsWith(
                    "org.ow2.asm:",
                    StringComparison.OrdinalIgnoreCase))
            {
                return
                    "https://maven.fabricmc.net/" +
                    relativePath.Replace(
                        '\\',
                        '/');
            }

            return
                "https://libraries.minecraft.net/" +
                relativePath.Replace(
                    '\\',
                    '/');
        }
        catch
        {
            return null;
        }
    }

    // ============================================================
    // FABRIC VALIDATION
    // ============================================================

    private bool ValidateFabricInstallation(
        string fabricVersionName,
        MinecraftPath minecraftPath)
    {
        try
        {
            WriteLog(
                "===== FABRIC VALIDATION =====");

            string fabricDirectory =
                Path.Combine(
                    _gamePath,
                    "versions",
                    fabricVersionName);

            string fabricJson =
                Path.Combine(
                    fabricDirectory,
                    fabricVersionName + ".json");

            string legacyFabricJar =
                Path.Combine(
                    fabricDirectory,
                    fabricVersionName + ".jar");

            RemoveLegacyFabricVersionJar(
                legacyFabricJar);

            if (!Directory.Exists(
                    fabricDirectory))
            {
                WriteLog(
                    "ERROR: Fabric directory does not exist.");

                return false;
            }

            if (!File.Exists(
                    fabricJson))
            {
                WriteLog(
                    "ERROR: Fabric JSON does not exist.");

                return false;
            }

            using JsonDocument document =
                JsonDocument.Parse(
                    File.ReadAllText(
                        fabricJson));

            if (document.RootElement.ValueKind !=
                JsonValueKind.Object)
            {
                WriteLog(
                    "ERROR: Fabric JSON is invalid.");

                return false;
            }

            string loaderRoot =
                Path.Combine(
                    _gamePath,
                    "libraries",
                    "net",
                    "fabricmc",
                    "fabric-loader");

            if (!Directory.Exists(loaderRoot))
            {
                WriteLog(
                    "ERROR: Fabric Loader directory missing.");

                return false;
            }

            string[] loaderJars =
                Directory.GetFiles(
                    loaderRoot,
                    "fabric-loader-*.jar",
                    SearchOption.AllDirectories);

            if (loaderJars.Length == 0)
            {
                WriteLog(
                    "ERROR: Fabric Loader JAR missing.");

                return false;
            }

            bool validLoader = false;

            foreach (string jar in loaderJars)
            {
                WriteLog(
                    $"Fabric Loader JAR found: {jar}");

                if (JarContainsEntry(
                        jar,
                        "net/fabricmc/loader/impl/launch/knot/KnotClient.class"))
                {
                    validLoader = true;
                }
            }

            if (!validLoader)
            {
                WriteLog(
                    "ERROR: No Fabric Loader JAR contains KnotClient.class.");

                return false;
            }

            WriteLog(
                $"Fabric JSON verified: {fabricJson}");

            WriteLog(
                "Fabric installation validation passed.");

            return true;
        }
        catch (Exception ex)
        {
            WriteException(
                "FABRIC VALIDATION ERROR",
                ex);

            return false;
        }
    }

    // ============================================================
    // PERFORMANCE MODS
    // ============================================================

    private async Task InstallPerformanceModsAsync(
        string minecraftVersion)
    {
        string modsFolder =
            Path.Combine(
                _gamePath,
                "mods");

        Directory.CreateDirectory(
            modsFolder);

        WriteLog(
            "===== PERFORMANCE MOD INSTALL =====");

        foreach ((string slug, string name)
                 in PerformanceMods)
        {
            try
            {
                StatusText.Text =
                    $"Installing {name}...";

                bool installed =
                    await DownloadPerformanceModAsync(
                        slug,
                        name,
                        minecraftVersion);

                if (installed)
                {
                    WriteLog(
                        $"Installed performance mod: {name}");
                }
            }
            catch (Exception ex)
            {
                WriteLog(
                    $"Optional mod failed: {name}");

                WriteLog(
                    ex.Message);
            }
        }

        WriteLog(
            "===== PERFORMANCE MOD INSTALL COMPLETE =====");
    }

    private async Task<bool> DownloadPerformanceModAsync(
        string slug,
        string name,
        string minecraftVersion)
    {
        string url =
            "https://api.modrinth.com/v2/project/" +
            Uri.EscapeDataString(slug) +
            "/version" +
            "?loaders=%5B%22fabric%22%5D" +
            "&game_versions=%5B%22" +
            Uri.EscapeDataString(minecraftVersion) +
            "%22%5D";

        using HttpResponseMessage response =
            await Http.GetAsync(url);

        response.EnsureSuccessStatusCode();

        string json =
            await response.Content.ReadAsStringAsync();

        using JsonDocument doc =
            JsonDocument.Parse(json);

        JsonElement versions =
            doc.RootElement;

        if (versions.ValueKind !=
                JsonValueKind.Array ||
            versions.GetArrayLength() == 0)
        {
            WriteLog(
                $"No compatible {name} build for {minecraftVersion}.");

            return false;
        }

        JsonElement? selectedFile = null;

        foreach (JsonElement version in
                 versions.EnumerateArray())
        {
            if (!version.TryGetProperty(
                    "files",
                    out JsonElement files))
            {
                continue;
            }

            selectedFile =
                FindPrimaryJar(files);

            if (selectedFile != null)
                break;
        }

        if (selectedFile == null)
            return false;

        JsonElement file =
            selectedFile.Value;

        string downloadUrl =
            file.GetProperty("url")
                .GetString()
            ?? throw new InvalidOperationException(
                $"No download URL for {name}.");

        string filename =
            file.GetProperty("filename")
                .GetString()
            ?? $"{slug}.jar";

        string destination =
            Path.Combine(
                _gamePath,
                "mods",
                SanitizeFileName(filename));

        if (File.Exists(destination))
        {
            if (new FileInfo(destination).Length > 0)
                return true;

            TryDeleteFile(destination);
        }

        await DownloadFileAsync(
            downloadUrl,
            destination);

        if (!File.Exists(destination))
        {
            throw new IOException(
                $"Mod file was not created: {destination}");
        }

        if (new FileInfo(destination).Length <= 0)
        {
            throw new IOException(
                $"Mod file is empty: {destination}");
        }

        return true;
    }

    private static JsonElement? FindPrimaryJar(
        JsonElement files)
    {
        if (files.ValueKind !=
            JsonValueKind.Array)
            return null;

        JsonElement? fallback = null;

        foreach (JsonElement file in
                 files.EnumerateArray())
        {
            if (!file.TryGetProperty(
                    "filename",
                    out JsonElement filenameElement))
            {
                continue;
            }

            string filename =
                filenameElement.GetString() ?? "";

            if (!filename.EndsWith(
                    ".jar",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool primary =
                file.TryGetProperty(
                    "primary",
                    out JsonElement primaryElement) &&
                primaryElement.ValueKind ==
                    JsonValueKind.True;

            if (primary)
                return file;

            fallback ??= file;
        }

        return fallback;
    }

    // ============================================================
    // DOWNLOADS
    // ============================================================

    private async Task DownloadFileAsync(
        string url,
        string destination)
    {
        string? directory =
            Path.GetDirectoryName(
                destination);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        string temporary =
            Path.Combine(
                directory ?? _gamePath,
                "." +
                Path.GetFileName(destination) +
                "." +
                Guid.NewGuid().ToString("N") +
                ".download");

        try
        {
            WriteLog(
                $"Downloading: {url}");

            using (HttpResponseMessage response =
                   await Http.GetAsync(
                       url,
                       HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                using Stream input =
                    await response.Content.ReadAsStreamAsync();

                using FileStream output =
                    new FileStream(
                        temporary,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read,
                        81920,
                        FileOptions.SequentialScan);

                await input.CopyToAsync(
                    output,
                    81920,
                    CancellationToken.None);

                await output.FlushAsync(
                    CancellationToken.None);
            }

            if (!File.Exists(temporary))
                throw new IOException(
                    "Temporary download file was not created.");

            long size =
                new FileInfo(
                    temporary).Length;

            if (size <= 0)
                throw new IOException(
                    "Downloaded file is empty.");

            WriteLog(
                $"Downloaded temporary file: {size:N0} bytes");

            await Task.Delay(150);

            await MoveFileWithRetryAsync(
                temporary,
                destination);

            if (!File.Exists(destination))
                throw new IOException(
                    $"Final download file does not exist: {destination}");

            long finalSize =
                new FileInfo(
                    destination).Length;

            if (finalSize <= 0)
                throw new IOException(
                    $"Final download file is empty: {destination}");

            WriteLog(
                $"Download complete: {destination} ({finalSize:N0} bytes)");
        }
        catch
        {
            TryDeleteFileWithRetry(
                temporary);

            throw;
        }
        finally
        {
            TryDeleteFileWithRetry(
                temporary);
        }
    }

    private async Task MoveFileWithRetryAsync(
        string source,
        string destination)
    {
        const int attempts = 20;

        Exception? lastException = null;

        for (int attempt = 1;
             attempt <= attempts;
             attempt++)
        {
            try
            {
                if (File.Exists(destination))
                {
                    try
                    {
                        if (new FileInfo(
                                destination).Length > 0)
                        {
                            TryDeleteFileWithRetry(
                                source);

                            return;
                        }
                    }
                    catch
                    {
                    }

                    TryDeleteFile(
                        destination);
                }

                File.Move(
                    source,
                    destination);

                return;
            }
            catch (IOException ex)
            {
                lastException = ex;

                if (attempt == attempts)
                    break;

                await Task.Delay(
                    300 +
                    attempt * 150);
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;

                if (attempt == attempts)
                    break;

                await Task.Delay(
                    300 +
                    attempt * 150);
            }
        }

        throw new IOException(
            $"Could not move downloaded file to '{destination}'.",
            lastException);
    }

    private static void TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteFileWithRetry(
        string path)
    {
        if (!File.Exists(path))
            return;

        for (int i = 0; i < 8; i++)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                File.Delete(path);
                return;
            }
            catch
            {
                Thread.Sleep(150);
            }
        }
    }

    // ============================================================
    // LEGACY FABRIC DUPLICATE
    // ============================================================

    private void RemoveLegacyFabricVersionJar(
        string legacyJarPath)
    {
        try
        {
            if (!File.Exists(
                    legacyJarPath))
                return;

            WriteLog(
                $"Removing legacy duplicate Fabric Loader JAR: {legacyJarPath}");

            TryDeleteFileWithRetry(
                legacyJarPath);

            if (File.Exists(
                    legacyJarPath))
            {
                throw new IOException(
                    $"Could not remove legacy duplicate Fabric Loader JAR: {legacyJarPath}");
            }
        }
        catch (Exception ex)
        {
            WriteException(
                "LEGACY FABRIC DUPLICATE CLEANUP ERROR",
                ex);

            throw;
        }
    }

    // ============================================================
    // JAVA
    // ============================================================

    private async Task<string> EnsureJavaAsync(
        int requiredMajor)
    {
        string runtimeFolder =
            Path.Combine(
                _gamePath,
                "runtime",
                $"java{requiredMajor}");

        string javaExe =
            Path.Combine(
                runtimeFolder,
                "bin",
                "java.exe");

        if (File.Exists(javaExe))
        {
            WriteLog(
                $"Checking Topu Java {requiredMajor}: {javaExe}");

            if (IsRequiredJava(
                javaExe,
                requiredMajor))
            {
                WriteLog(
                    $"Using Topu Java {requiredMajor}: {javaExe}");

                return javaExe;
            }
        }

        string systemJava =
            FindSystemJava(
                requiredMajor);

        if (!string.IsNullOrWhiteSpace(
                systemJava))
        {
            WriteLog(
                $"Using system Java {requiredMajor}: {systemJava}");

            return systemJava;
        }

        StatusText.Text =
            $"Downloading Java {requiredMajor}...";

        await DownloadAndInstallJavaAsync(
            requiredMajor,
            runtimeFolder);

        if (!File.Exists(javaExe))
            throw new InvalidOperationException(
                $"Java {requiredMajor} installation failed.");

        if (!IsRequiredJava(
            javaExe,
            requiredMajor))
        {
            throw new InvalidOperationException(
                $"Installed runtime is not Java {requiredMajor}.");
        }

        return javaExe;
    }

    private string FindSystemJava(
        int requiredMajor)
    {
        string javaHome =
            Environment.GetEnvironmentVariable(
                "JAVA_HOME") ?? "";

        if (!string.IsNullOrWhiteSpace(
            javaHome))
        {
            string candidate =
                Path.Combine(
                    javaHome,
                    "bin",
                    "java.exe");

            if (File.Exists(candidate) &&
                IsRequiredJava(
                    candidate,
                    requiredMajor))
            {
                return candidate;
            }
        }

        string path =
            Environment.GetEnvironmentVariable(
                "PATH") ?? "";

        foreach (string folder in
                 path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate =
                Path.Combine(
                    folder.Trim(),
                    "java.exe");

            if (!File.Exists(candidate))
                continue;

            if (IsRequiredJava(
                candidate,
                requiredMajor))
            {
                return candidate;
            }
        }

        return "";
    }

    private bool IsRequiredJava(
        string javaPath,
        int requiredMajor)
    {
        try
        {
            ProcessStartInfo info =
                new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

            using Process process =
                Process.Start(info)
                ?? throw new InvalidOperationException(
                    "Could not start Java.");

            string stdout =
                process.StandardOutput.ReadToEnd();

            string stderr =
                process.StandardError.ReadToEnd();

            process.WaitForExit();

            string combined =
                stdout +
                Environment.NewLine +
                stderr;

            WriteLog(
                $"Java version check [{javaPath}]: {combined.Trim()}");

            return combined.Contains(
                $"version \"{requiredMajor}.",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            WriteLog(
                $"Java check failed [{javaPath}]: {ex.Message}");

            return false;
        }
    }

    private async Task DownloadAndInstallJavaAsync(
        int major,
        string destination)
    {
        string apiUrl =
            "https://api.adoptium.net/v3/assets/latest/" +
            major +
            "/hotspot" +
            "?architecture=x64" +
            "&image_type=jre" +
            "&os=windows" +
            "&vendor=eclipse";

        using HttpResponseMessage response =
            await Http.GetAsync(apiUrl);

        response.EnsureSuccessStatusCode();

        string json =
            await response.Content.ReadAsStringAsync();

        using JsonDocument doc =
            JsonDocument.Parse(json);

        JsonElement assets =
            doc.RootElement;

        if (assets.ValueKind !=
                JsonValueKind.Array ||
            assets.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"No Java {major} Windows x64 runtime found.");
        }

        JsonElement package =
            assets[0]
                .GetProperty("binary")
                .GetProperty("package");

        string downloadUrl =
            package.GetProperty("link")
                .GetString()
            ?? throw new InvalidOperationException(
                "Java download URL missing.");

        string archiveName =
            package.TryGetProperty(
                "name",
                out JsonElement nameElement)
                ? nameElement.GetString()
                    ?? $"java{major}.zip"
                : $"java{major}.zip";

        string tempArchive =
            Path.Combine(
                Path.GetTempPath(),
                "topu-java-" +
                Guid.NewGuid().ToString("N") +
                "-" +
                SanitizeFileName(
                    archiveName));

        try
        {
            using (HttpResponseMessage javaResponse =
                   await Http.GetAsync(
                       downloadUrl,
                       HttpCompletionOption.ResponseHeadersRead))
            {
                javaResponse.EnsureSuccessStatusCode();

                using Stream input =
                    await javaResponse.Content.ReadAsStreamAsync();

                using FileStream output =
                    new FileStream(
                        tempArchive,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read);

                await input.CopyToAsync(
                    output,
                    81920,
                    CancellationToken.None);

                await output.FlushAsync(
                    CancellationToken.None);
            }

            if (!File.Exists(tempArchive) ||
                new FileInfo(tempArchive).Length <= 0)
            {
                throw new IOException(
                    "Java archive download failed.");
            }

            string extractionDirectory =
                destination +
                ".extracting-" +
                Guid.NewGuid().ToString("N");

            try
            {
                TryDeleteDirectory(
                    extractionDirectory);

                Directory.CreateDirectory(
                    extractionDirectory);

                ZipFile.ExtractToDirectory(
                    tempArchive,
                    extractionDirectory,
                    true);

                string? javaRoot =
                    FindJavaRoot(
                        extractionDirectory);

                if (javaRoot != null &&
                    !File.Exists(
                        Path.Combine(
                            extractionDirectory,
                            "bin",
                            "java.exe")))
                {
                    MoveJavaRootContents(
                        javaRoot,
                        extractionDirectory);
                }

                string extractedJava =
                    Path.Combine(
                        extractionDirectory,
                        "bin",
                        "java.exe");

                if (!File.Exists(
                    extractedJava))
                {
                    throw new InvalidOperationException(
                        "java.exe was not found after extraction.");
                }

                if (Directory.Exists(
                    destination))
                {
                    TryDeleteDirectory(
                        destination);
                }

                MoveDirectoryWithRetry(
                    extractionDirectory,
                    destination);
            }
            catch
            {
                TryDeleteDirectory(
                    extractionDirectory);

                throw;
            }
        }
        finally
        {
            TryDeleteFileWithRetry(
                tempArchive);
        }
    }

    private static string? FindJavaRoot(
        string destination)
    {
        foreach (string directory in
                 Directory.GetDirectories(
                     destination))
        {
            if (File.Exists(
                Path.Combine(
                    directory,
                    "bin",
                    "java.exe")))
            {
                return directory;
            }
        }

        return null;
    }

    private static void MoveJavaRootContents(
        string source,
        string destination)
    {
        foreach (string directory in
                 Directory.GetDirectories(
                     source))
        {
            string target =
                Path.Combine(
                    destination,
                    Path.GetFileName(
                        directory));

            if (Directory.Exists(target))
                Directory.Delete(
                    target,
                    true);

            Directory.Move(
                directory,
                target);
        }

        foreach (string file in
                 Directory.GetFiles(
                     source))
        {
            string target =
                Path.Combine(
                    destination,
                    Path.GetFileName(
                        file));

            File.Move(
                file,
                target,
                true);
        }

        TryDeleteDirectory(
            source);
    }

    private static void MoveDirectoryWithRetry(
        string source,
        string destination)
    {
        Exception? lastException = null;

        for (int i = 0; i < 20; i++)
        {
            try
            {
                Directory.Move(
                    source,
                    destination);

                return;
            }
            catch (IOException ex)
            {
                lastException = ex;
                Thread.Sleep(
                    250 +
                    i * 100);
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
                Thread.Sleep(
                    250 +
                    i * 100);
            }
        }

        throw new IOException(
            $"Could not move Java runtime into {destination}.",
            lastException);
    }

    private static void TryDeleteDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(
                    path,
                    true);
        }
        catch
        {
        }
    }

    // ============================================================
    // INSTALLATION VALIDATION
    // ============================================================

    private bool ValidateMinecraftInstallation(
        string minecraftVersion,
        MinecraftPath minecraftPath,
        string fabricVersion)
    {
        try
        {
            WriteLog(
                "===== INSTALLATION VALIDATION =====");

            string vanillaDirectory =
                Path.Combine(
                    _gamePath,
                    "versions",
                    minecraftVersion);

            string fabricDirectory =
                Path.Combine(
                    _gamePath,
                    "versions",
                    fabricVersion);

            string vanillaJson =
                Path.Combine(
                    vanillaDirectory,
                    minecraftVersion + ".json");

            string vanillaJar =
                Path.Combine(
                    vanillaDirectory,
                    minecraftVersion + ".jar");

            string fabricJson =
                Path.Combine(
                    fabricDirectory,
                    fabricVersion + ".json");

            string legacyFabricJar =
                Path.Combine(
                    fabricDirectory,
                    fabricVersion + ".jar");

            RemoveLegacyFabricVersionJar(
                legacyFabricJar);

            if (!File.Exists(vanillaJson))
                return false;

            if (!File.Exists(vanillaJar))
                return false;

            if (new FileInfo(
                vanillaJar).Length <= 0)
                return false;

            if (!File.Exists(fabricJson))
                return false;

            using JsonDocument vanillaDocument =
                JsonDocument.Parse(
                    File.ReadAllText(
                        vanillaJson));

            using JsonDocument fabricDocument =
                JsonDocument.Parse(
                    File.ReadAllText(
                        fabricJson));

            if (vanillaDocument.RootElement.ValueKind !=
                JsonValueKind.Object)
                return false;

            if (fabricDocument.RootElement.ValueKind !=
                JsonValueKind.Object)
                return false;

            string loaderRoot =
                Path.Combine(
                    _gamePath,
                    "libraries",
                    "net",
                    "fabricmc",
                    "fabric-loader");

            if (!Directory.Exists(loaderRoot))
                return false;

            string[] loaderJars =
                Directory.GetFiles(
                    loaderRoot,
                    "fabric-loader-*.jar",
                    SearchOption.AllDirectories);

            if (loaderJars.Length == 0)
                return false;

            bool validLoader = false;

            foreach (string jar in loaderJars)
            {
                WriteLog(
                    $"Fabric Loader available: {jar}");

                if (JarContainsEntry(
                    jar,
                    "net/fabricmc/loader/impl/launch/knot/KnotClient.class"))
                {
                    validLoader = true;
                }
            }

            if (!validLoader)
                return false;

            WriteLog(
                $"Vanilla JSON: {vanillaJson}");

            WriteLog(
                $"Vanilla JAR: {vanillaJar}");

            WriteLog(
                $"Fabric JSON: {fabricJson}");

            WriteLog(
                $"Minecraft profile root: {_gamePath}");

            WriteLog(
                "===== INSTALLATION VALIDATION PASSED =====");

            return true;
        }
        catch (Exception ex)
        {
            WriteException(
                "INSTALLATION VALIDATION ERROR",
                ex);

            return false;
        }
    }

    // ============================================================
    // MAIN LAUNCH
    // ============================================================

    private async void LaunchBtn_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_minecraftProcess != null)
        {
            try
            {
                if (!_minecraftProcess.HasExited)
                {
                    MessageBox.Show(
                        "Minecraft is already running.",
                        "Topu Client",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }
            }
            catch
            {
            }

            _minecraftProcess = null;
        }

        LaunchBtn.IsEnabled =
            false;

        try
        {
            // ====================================================
            // SELECT PROFILE FIRST
            // ====================================================

            string profileName =
                GetActiveProfileName();

            SetActiveProfile(
                profileName);

            string normalizedProfile =
                NormalizeProfileName(
                    profileName);

            StartLaunchLog();

            WriteLog(
                "===== TOPU FABRIC LAUNCH 2026 =====");

            WriteLog(
                $"Profile name: {profileName}");

            WriteLog(
                $"Profile folder: {normalizedProfile}");

            WriteLog(
                $"Minecraft game directory: {_gamePath}");

            string minecraftVersion =
                GetSelectedVersion();

            int ram =
                Math.Max(
                    2048,
                    (int)RamSlider.Value *
                    1024);

            WriteLog(
                $"Minecraft version: {minecraftVersion}");

            WriteLog(
                $"RAM: {ram} MB");

            // ====================================================
            // AUTH
            // ====================================================

            _session =
                await AuthenticateSelectedAccountAsync();

            if (_session == null)
            {
                throw new InvalidOperationException(
                    "Could not create a Minecraft session.");
            }

            WriteLog(
                $"Selected account username: {_session.Username}");

            WriteLog(
                $"Session UUID: {_session.UUID}");

            // ====================================================
            // JAVA
            // ====================================================

            int javaMajor =
                GetRequiredJavaMajor(
                    minecraftVersion);

            WriteLog(
                $"Required Java major: {javaMajor}");

            string javaPath =
                await EnsureJavaAsync(
                    javaMajor);

            WriteLog(
                $"Java path: {javaPath}");

            // ====================================================
            // CMLLIB
            // ====================================================

            MinecraftPath minecraftPath =
                new MinecraftPath(
                    _gamePath);

            WriteLog(
                $"CmlLib MinecraftPath: {minecraftPath.BasePath}");

            MinecraftLauncher launcher =
                new MinecraftLauncher(
                    minecraftPath);

            // ====================================================
            // VANILLA
            // ====================================================

            StatusText.Text =
                $"Installing Minecraft {minecraftVersion}...";

            WriteLog(
                "Installing/checking vanilla Minecraft files.");

            WriteLog(
                $"Vanilla installation root: {_gamePath}");

            Progress<InstallerProgressChangedEventArgs>
                fileProgress =
                    new Progress<InstallerProgressChangedEventArgs>(
                        args =>
                        {
                            try
                            {
                                StatusText.Text =
                                    $"Downloading {args.Name} " +
                                    $"({args.ProgressedTasks}/{args.TotalTasks})";
                            }
                            catch
                            {
                            }
                        });

            Progress<ByteProgress>
                byteProgress =
                    new Progress<ByteProgress>(
                        args =>
                        {
                            try
                            {
                                if (args.TotalBytes > 0)
                                {
                                    double percent =
                                        args.ProgressedBytes *
                                        100.0 /
                                        args.TotalBytes;

                                    StatusText.Text =
                                        $"Downloading: {percent:0}%";
                                }
                            }
                            catch
                            {
                            }
                        });

            await launcher.InstallAsync(
                minecraftVersion,
                fileProgress,
                byteProgress,
                CancellationToken.None);

            WriteLog(
                "Vanilla Minecraft installation complete.");

            // ====================================================
            // FABRIC
            // ====================================================

            string fabricVersion =
                await InstallFabricAsync(
                    minecraftVersion,
                    minecraftPath);

            WriteLog(
                $"Fabric version: {fabricVersion}");

            // ====================================================
            // VALIDATION
            // ====================================================

            if (!ValidateMinecraftInstallation(
                minecraftVersion,
                minecraftPath,
                fabricVersion))
            {
                throw new InvalidOperationException(
                    "Minecraft/Fabric installation validation failed.");
            }

            // ====================================================
            // MODS
            // ====================================================

            StatusText.Text =
                "Installing performance mods...";

            await InstallPerformanceModsAsync(
                minecraftVersion);

            if (_session == null)
            {
                throw new InvalidOperationException(
                    "Minecraft session was not created.");
            }

            // ====================================================
            // OPTIONS
            // ====================================================

            MLaunchOption options =
                new MLaunchOption
                {
                    Session = _session,
                    MaximumRamMb = ram,
                    MinimumRamMb =
                        Math.Min(
                            1024,
                            ram),
                    JavaPath = javaPath,
                    GameLauncherName =
                        "Topu Client",
                    GameLauncherVersion =
                        "1.0.0"
                };

            // ====================================================
            // REMOVE ONLY LEGACY DUPLICATE
            // ====================================================

            RemoveLegacyFabricVersionJar(
                Path.Combine(
                    _gamePath,
                    "versions",
                    fabricVersion,
                    fabricVersion + ".jar"));

            // ====================================================
            // BUILD
            // ====================================================

            StatusText.Text =
                "Building Minecraft process...";

            WriteLog(
                "Calling CmlLib BuildProcessAsync.");

            WriteLog(
                $"Fabric profile selected: {fabricVersion}");

            Process process =
                await launcher.BuildProcessAsync(
                    fabricVersion,
                    options,
                    CancellationToken.None);

            if (process == null)
            {
                throw new InvalidOperationException(
                    "CmlLib returned a null Minecraft process.");
            }

            // ====================================================
            // FINAL FIX
            // ====================================================

            NormalizeFabricProcessArguments(
                process,
                minecraftVersion,
                fabricVersion);

            ValidateFinalFabricCommand(
                process,
                minecraftVersion,
                fabricVersion);

            process.StartInfo.RedirectStandardOutput =
                true;

            process.StartInfo.RedirectStandardError =
                true;

            process.StartInfo.UseShellExecute =
                false;

            process.StartInfo.CreateNoWindow =
                true;

            process.OutputDataReceived +=
                Minecraft_OutputDataReceived;

            process.ErrorDataReceived +=
                Minecraft_ErrorDataReceived;

            WriteLog(
                $"Executable: {process.StartInfo.FileName}");

            WriteLog(
                $"Arguments: {process.StartInfo.Arguments}");

            WriteLog(
                $"Working directory: {process.StartInfo.WorkingDirectory}");

            WriteDebugFile(
                process,
                javaPath,
                minecraftVersion,
                fabricVersion,
                ram);

            StatusText.Text =
                $"Starting Fabric {minecraftVersion}...";

            WriteLog(
                "Starting Minecraft.");

            bool started =
                process.Start();

            if (!started)
            {
                throw new InvalidOperationException(
                    "Windows failed to start Minecraft.");
            }

            _minecraftProcess =
                process;

            WriteLog(
                $"Minecraft started successfully. PID={process.Id}");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            StatusText.Text =
                $"Topu Client running as {_session.Username}";

            _ = MonitorMinecraftAsync(
                process);
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Launch failed.";

            WriteException(
                "TOPU LAUNCH ERROR",
                ex);

            MessageBox.Show(
                "Minecraft failed to launch.\n\n" +
                ex.Message +
                "\n\nLog:\n" +
                _logPath,
                "Topu Client",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            LaunchBtn.IsEnabled =
                true;
        }
    }

    // ============================================================
    // FABRIC PROCESS NORMALIZATION
    // ============================================================

    private void NormalizeFabricProcessArguments(
        Process process,
        string minecraftVersion,
        string fabricVersion)
    {
        WriteLog(
            "===== FABRIC PROCESS NORMALIZATION =====");

        string original =
            process.StartInfo.Arguments;

        WriteLog(
            "Parsing CmlLib command line.");

        List<string> tokens =
            TokenizeWindowsCommandLine(
                original);

        if (tokens.Count == 0)
        {
            throw new InvalidOperationException(
                "CmlLib generated an empty Java command line.");
        }

        int cpIndex =
            FindArgumentIndex(
                tokens,
                "-cp",
                "--class-path",
                "-classpath");

        if (cpIndex < 0)
        {
            throw new InvalidOperationException(
                "Could not find -cp in CmlLib generated arguments.");
        }

        if (cpIndex + 1 >= tokens.Count)
        {
            throw new InvalidOperationException(
                "CmlLib generated -cp without a classpath value.");
        }

        string classpathToken =
            tokens[cpIndex + 1];

        List<string> classpathEntries =
            classpathToken
                .Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(
                    p => p.Trim().Trim('"'))
                .Where(
                    p => !string.IsNullOrWhiteSpace(p))
                .ToList();

        string vanillaJar =
            Path.GetFullPath(
                Path.Combine(
                    _gamePath,
                    "versions",
                    minecraftVersion,
                    minecraftVersion + ".jar"));

        string fabricProfileJar =
            Path.GetFullPath(
                Path.Combine(
                    _gamePath,
                    "versions",
                    fabricVersion,
                    fabricVersion + ".jar"));

        if (!File.Exists(vanillaJar))
        {
            throw new FileNotFoundException(
                "Vanilla Minecraft JAR is missing.",
                vanillaJar);
        }

        if (new FileInfo(
                vanillaJar).Length <= 0)
        {
            throw new InvalidDataException(
                "Vanilla Minecraft JAR is empty.");
        }

        string vanillaNormalized =
            NormalizePath(
                vanillaJar);

        string fabricProfileNormalized =
            NormalizePath(
                fabricProfileJar);

        bool removedDuplicate =
            false;

        bool vanillaPresent =
            false;

        List<string> rebuiltClasspath =
            new List<string>();

        foreach (string entry in
                 classpathEntries)
        {
            string normalized =
                NormalizePath(entry);

            if (string.Equals(
                normalized,
                fabricProfileNormalized,
                StringComparison.OrdinalIgnoreCase))
            {
                WriteLog(
                    $"REMOVED Fabric profile duplicate: {entry}");

                removedDuplicate =
                    true;

                continue;
            }

            if (string.Equals(
                normalized,
                vanillaNormalized,
                StringComparison.OrdinalIgnoreCase))
            {
                vanillaPresent =
                    true;
            }

            rebuiltClasspath.Add(
                entry);
        }

        if (!vanillaPresent)
        {
            rebuiltClasspath.Add(
                vanillaNormalized);

            WriteLog(
                $"ADDED Minecraft game JAR: {vanillaNormalized}");
        }
        else
        {
            WriteLog(
                "Minecraft game JAR was already present.");
        }

        if (rebuiltClasspath.Count == 0)
        {
            throw new InvalidOperationException(
                "Final Fabric classpath became empty.");
        }

        tokens[cpIndex + 1] =
            string.Join(
                ";",
                rebuiltClasspath);

        for (int i = tokens.Count - 1;
             i >= 0;
             i--)
        {
            if (tokens[i].StartsWith(
                "-DFabricMcEmu=",
                StringComparison.OrdinalIgnoreCase))
            {
                WriteLog(
                    $"REMOVED malformed FabricMcEmu argument: {tokens[i]}");

                tokens.RemoveAt(i);
            }
        }

        bool knotFound =
            tokens.Any(
                t => string.Equals(
                    t,
                    "net.fabricmc.loader.impl.launch.knot.KnotClient",
                    StringComparison.Ordinal));

        if (!knotFound)
        {
            throw new InvalidOperationException(
                "KnotClient main class disappeared from generated arguments.");
        }

        process.StartInfo.Arguments =
            BuildWindowsCommandLine(
                tokens);

        WriteLog(
            $"Fabric profile duplicate removed: {removedDuplicate}");

        WriteLog(
            "Fabric process arguments rebuilt.");

        WriteLog(
            "===== FABRIC PROCESS NORMALIZATION COMPLETE =====");
    }

    private void ValidateFinalFabricCommand(
        Process process,
        string minecraftVersion,
        string fabricVersion)
    {
        WriteLog(
            "===== FINAL FABRIC COMMAND CHECK =====");

        List<string> tokens =
            TokenizeWindowsCommandLine(
                process.StartInfo.Arguments);

        int cpIndex =
            FindArgumentIndex(
                tokens,
                "-cp",
                "--class-path",
                "-classpath");

        if (cpIndex < 0 ||
            cpIndex + 1 >= tokens.Count)
        {
            throw new InvalidOperationException(
                "Final Minecraft command has no valid classpath.");
        }

        List<string> classpath =
            tokens[cpIndex + 1]
                .Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(
                    NormalizePath)
                .ToList();

        string vanillaJar =
            NormalizePath(
                Path.Combine(
                    _gamePath,
                    "versions",
                    minecraftVersion,
                    minecraftVersion + ".jar"));

        string fabricJar =
            NormalizePath(
                Path.Combine(
                    _gamePath,
                    "versions",
                    fabricVersion,
                    fabricVersion + ".jar"));

        bool vanillaPresent =
            classpath.Any(
                p => string.Equals(
                    p,
                    vanillaJar,
                    StringComparison.OrdinalIgnoreCase));

        bool fabricProfilePresent =
            classpath.Any(
                p => string.Equals(
                    p,
                    fabricJar,
                    StringComparison.OrdinalIgnoreCase));

        int loaderCopies =
            classpath.Count(
                p => p.Contains(
                    "\\libraries\\net\\fabricmc\\fabric-loader\\",
                    StringComparison.OrdinalIgnoreCase) &&
                     p.EndsWith(
                         ".jar",
                         StringComparison.OrdinalIgnoreCase));

        bool knotPresent =
            tokens.Any(
                t => string.Equals(
                    t,
                    "net.fabricmc.loader.impl.launch.knot.KnotClient",
                    StringComparison.Ordinal));

        WriteLog(
            $"Minecraft game JAR present: {vanillaPresent}");

        WriteLog(
            $"Fabric profile JAR present: {fabricProfilePresent}");

        WriteLog(
            $"Fabric Loader library copies: {loaderCopies}");

        WriteLog(
            $"KnotClient present: {knotPresent}");

        if (!vanillaPresent)
        {
            throw new InvalidOperationException(
                "FINAL COMMAND INVALID: Minecraft game JAR is absent.");
        }

        if (fabricProfilePresent)
        {
            throw new InvalidOperationException(
                "FINAL COMMAND INVALID: duplicate Fabric profile JAR remains.");
        }

        if (loaderCopies != 1)
        {
            throw new InvalidOperationException(
                $"FINAL COMMAND INVALID: expected exactly one Fabric Loader library, found {loaderCopies}.");
        }

        if (!knotPresent)
        {
            throw new InvalidOperationException(
                "FINAL COMMAND INVALID: KnotClient is missing.");
        }

        WriteLog(
            "FINAL FABRIC COMMAND CHECK PASSED.");

        WriteLog(
            "Fabric Loader = exactly one library copy");

        WriteLog(
            "Minecraft game JAR = present");

        WriteLog(
            "Fabric profile duplicate = absent");

        WriteLog(
            "KnotClient = present");

        WriteLog(
            "===== FINAL FABRIC COMMAND CHECK COMPLETE =====");
    }

    // ============================================================
    // WINDOWS COMMAND LINE PARSING
    // ============================================================

    private static int FindArgumentIndex(
        List<string> arguments,
        params string[] names)
    {
        for (int i = 0;
             i < arguments.Count;
             i++)
        {
            foreach (string name in names)
            {
                if (string.Equals(
                    arguments[i],
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static List<string>
        TokenizeWindowsCommandLine(
            string commandLine)
    {
        List<string> result =
            new List<string>();

        StringBuilder current =
            new StringBuilder();

        bool inQuotes = false;
        int backslashes = 0;

        for (int i = 0;
             i < commandLine.Length;
             i++)
        {
            char c =
                commandLine[i];

            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                if (backslashes > 0)
                {
                    int pairs =
                        backslashes / 2;

                    current.Append(
                        new string(
                            '\\',
                            pairs));

                    if ((backslashes % 2) == 1)
                    {
                        current.Append('"');
                    }
                    else
                    {
                        inQuotes =
                            !inQuotes;
                    }

                    backslashes = 0;
                    continue;
                }

                inQuotes =
                    !inQuotes;

                continue;
            }

            if (backslashes > 0)
            {
                current.Append(
                    new string(
                        '\\',
                        backslashes));

                backslashes = 0;
            }

            if (char.IsWhiteSpace(c) &&
                !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(
                        current.ToString());

                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (backslashes > 0)
        {
            current.Append(
                new string(
                    '\\',
                    backslashes));
        }

        if (current.Length > 0)
        {
            result.Add(
                current.ToString());
        }

        return result;
    }

    private static string BuildWindowsCommandLine(
        IEnumerable<string> arguments)
    {
        return string.Join(
            " ",
            arguments.Select(
                QuoteWindowsArgument));
    }

    private static string QuoteWindowsArgument(
        string argument)
    {
        if (argument.Length == 0)
            return "\"\"";

        if (!argument.Any(
                char.IsWhiteSpace) &&
            !argument.Contains('"'))
        {
            return argument;
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append('"');

        int backslashes = 0;

        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append(
                    new string(
                        '\\',
                        backslashes * 2 + 1));

                builder.Append('"');

                backslashes = 0;

                continue;
            }

            if (backslashes > 0)
            {
                builder.Append(
                    new string(
                        '\\',
                        backslashes));

                backslashes = 0;
            }

            builder.Append(c);
        }

        if (backslashes > 0)
        {
            builder.Append(
                new string(
                    '\\',
                    backslashes * 2));
        }

        builder.Append('"');

        return builder.ToString();
    }

    private static string NormalizePath(
        string path)
    {
        string cleaned =
            path.Trim().Trim('"');

        try
        {
            return Path.GetFullPath(
                cleaned)
                .Replace(
                    '/',
                    '\\')
                .TrimEnd('\\');
        }
        catch
        {
            return cleaned
                .Replace(
                    '/',
                    '\\')
                .TrimEnd('\\');
        }
    }

    // ============================================================
    // MINECRAFT OUTPUT
    // ============================================================

    private void Minecraft_OutputDataReceived(
        object sender,
        DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                e.Data))
            return;

        AppendRawGameOutput(
            "[MC]",
            e.Data);
    }

    private void Minecraft_ErrorDataReceived(
        object sender,
        DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                e.Data))
            return;

        AppendRawGameOutput(
            "[MC-ERR]",
            e.Data);
    }

    // ============================================================
    // PROCESS MONITOR
    // ============================================================

    private async Task MonitorMinecraftAsync(
        Process process)
    {
        try
        {
            await Task.Run(
                () =>
                {
                    try
                    {
                        process.WaitForExit();
                    }
                    catch
                    {
                    }
                });

            await Task.Delay(500);

            int exitCode = 0;

            try
            {
                exitCode =
                    process.ExitCode;
            }
            catch
            {
            }

            AppendGameLog(
                $"===== MINECRAFT EXITED: {exitCode} =====");

            if (exitCode == 0)
            {
                AppendGameLog(
                    "Minecraft exited normally.");
            }
            else
            {
                AppendGameLog(
                    "Minecraft did not exit normally.");

                AppendGameLog(
                    "Check [MC] and [MC-ERR] lines above.");
            }

            await Dispatcher.InvokeAsync(
                () =>
                {
                    StatusText.Text =
                        exitCode == 0
                            ? "Minecraft closed normally."
                            : $"Minecraft crashed (exit code {exitCode}). Check the log.";
                });
        }
        catch (Exception ex)
        {
            WriteException(
                "PROCESS MONITOR ERROR",
                ex);
        }
        finally
        {
            try
            {
                process.OutputDataReceived -=
                    Minecraft_OutputDataReceived;

                process.ErrorDataReceived -=
                    Minecraft_ErrorDataReceived;
            }
            catch
            {
            }

            _minecraftProcess =
                null;
        }
    }

    // ============================================================
    // DEBUG FILE
    // ============================================================

    private void WriteDebugFile(
        Process process,
        string javaPath,
        string minecraftVersion,
        string fabricVersion,
        int ram)
    {
        try
        {
            string path =
                Path.Combine(
                    _gamePath,
                    "topu-launch-debug.txt");

            StringBuilder text =
                new StringBuilder();

            text.AppendLine(
                "===== TOPU CLIENT DEBUG =====");

            text.AppendLine(
                $"Time: {DateTime.Now:O}");

            text.AppendLine();

            text.AppendLine(
                $"Profile directory: {_gamePath}");

            text.AppendLine();

            text.AppendLine(
                $"Minecraft: {minecraftVersion}");

            text.AppendLine(
                $"Fabric: {fabricVersion}");

            text.AppendLine(
                $"Java: {javaPath}");

            text.AppendLine(
                $"RAM: {ram} MB");

            text.AppendLine();

            text.AppendLine(
                "Account:");

            text.AppendLine(
                _session?.Username ?? "None");

            text.AppendLine();

            text.AppendLine(
                "Executable:");

            text.AppendLine(
                process.StartInfo.FileName);

            text.AppendLine();

            text.AppendLine(
                "Arguments:");

            text.AppendLine(
                process.StartInfo.Arguments);

            text.AppendLine();

            text.AppendLine(
                "Working directory:");

            text.AppendLine(
                process.StartInfo.WorkingDirectory);

            File.WriteAllText(
                path,
                text.ToString());
        }
        catch (Exception ex)
        {
            WriteException(
                "DEBUG FILE ERROR",
                ex);
        }
    }

    // ============================================================
    // UTILITIES
    // ============================================================

    private static string SanitizeFileName(
        string filename)
    {
        foreach (char c in
                 Path.GetInvalidFileNameChars())
        {
            filename =
                filename.Replace(
                    c,
                    '_');
        }

        return filename;
    }

    private void UpdateLaunchSummary()
    {
        try
        {
            if (LaunchProfileLabel != null)
            {
                LaunchProfileLabel.Text =
                    GetActiveProfileName();
            }

            if (LaunchVersionLabel != null)
            {
                LaunchVersionLabel.Text =
                    GetSelectedVersion();
            }

            if (LaunchRamLabel != null)
            {
                LaunchRamLabel.Text =
                    $"{(int)RamSlider.Value}GB RAM";
            }

            UpdateAccountCard();
        }
        catch
        {
        }
    }
}


}
