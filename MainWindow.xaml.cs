using System;
using System.IO;
using System.Text.Json;

namespace TopuLauncher
{
    public partial class MainWindow
    {
        private void UpdateLaunchSummary()
        {
            try
            {
                string profile = GetActiveProfileName();
                string loader = _loaderBox?.SelectedItem?.ToString() ?? "Vanilla";
                string version = GetSelectedVersion();
                int ram = Math.Clamp((int)RamSlider.Value, 2, 12);

                if (LaunchProfileLabel != null)
                    LaunchProfileLabel.Text = profile;

                if (LaunchLoaderLabel != null)
                {
                    try
                    {
                        string path = GetProfileSettingsPath(_gamePath);
                        if (File.Exists(path))
                        {
                            ProfileSettings settings =
                                JsonSerializer.Deserialize<ProfileSettings>(File.ReadAllText(path))
                                ?? new ProfileSettings();

                            if (!string.IsNullOrWhiteSpace(settings.Loader))
                                loader = settings.Loader;
                        }
                    }
                    catch
                    {
                        // Keep the UI usable when an older profile file is malformed.
                    }

                    LaunchLoaderLabel.Text = loader;
                }

                if (LaunchVersionLabel != null)
                    LaunchVersionLabel.Text = version;

                if (LaunchRamLabel != null)
                    LaunchRamLabel.Text = $"{ram}GB RAM";

                if (SelectedProfileLabel != null)
                    SelectedProfileLabel.Text =
                        $"● {profile}   •   {loader} {version}   •   {ram}GB RAM";

                UpdateAccountCard();
            }
            catch
            {
                // Summary updates must never prevent the launcher window from loading.
            }
        }
    }
}
