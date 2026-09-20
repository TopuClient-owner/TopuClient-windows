using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TopuLauncher;

/// <summary>
/// Applies the refreshed Topu visual language without replacing the existing
/// launcher controls or feature wiring. Keeping this in a partial class makes
/// the UI pass safe for existing profiles and installations.
/// </summary>
public partial class MainWindow
{
    private static readonly object ModernUiRegistration = RegisterModernUi();
    private bool _modernUiApplied;

    private static object RegisterModernUi()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ModernUiLoaded));
        return new object();
    }

    private static void ModernUiLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
        {
            window.Dispatcher.BeginInvoke(
                new Action(window.ApplyModernUi),
                DispatcherPriority.ApplicationIdle);
        }
    }

    private void ApplyModernUi()
    {
        if (_modernUiApplied)
            return;

        _modernUiApplied = true;

        // Keep the existing XAML event contracts and feature controls intact,
        // but give the launcher a consistent modern surface at runtime.
        Background = Brushes.Transparent;
        Title = "Topu Client";

        ApplyCardSurface(TabLaunch);
        ApplyCardSurface(TabProfiles);
        ApplyCardSurface(TabAccounts);

        if (LaunchBtn != null)
        {
            LaunchBtn.Height = 56;
            LaunchBtn.FontSize = 15;
            LaunchBtn.FontWeight = FontWeights.Bold;
            LaunchBtn.Margin = new Thickness(0, 8, 0, 0);
        }

        ApplyNavigationState(TabLaunchBtn);
        UpdateModernProfileSummary();
    }

    private static void ApplyCardSurface(Panel? panel)
    {
        if (panel == null)
            return;

        panel.Margin = new Thickness(2);
        panel.SnapsToDevicePixels = true;
    }

    private void ApplyNavigationState(Button? active)
    {
        Button[] navigation = { TabLaunchBtn, TabProfilesBtn, TabAccountsBtn };
        foreach (Button button in navigation)
        {
            if (button == null)
                continue;

            button.MinHeight = 44;
            button.Padding = new Thickness(18, 0, 18, 0);
            button.FontSize = 12;
            button.BorderThickness = button == active
                ? new Thickness(3, 0, 0, 0)
                : new Thickness(0);
            button.Foreground = button == active
                ? (Brush)FindResource("TopuGreen")
                : new SolidColorBrush(Color.FromRgb(154, 161, 173));
        }
    }

    private void UpdateModernProfileSummary()
    {
        try
        {
            RuntimeProfileSettings profile = GetRuntimeProfile();
            string loader = string.IsNullOrWhiteSpace(profile.Loader)
                ? "Vanilla"
                : profile.Loader;
            string version = string.IsNullOrWhiteSpace(profile.Version)
                ? GetSelectedVersion()
                : profile.Version;
            int ram = Math.Clamp(profile.RamGb, 2, 12);

            if (LaunchProfileLabel != null)
                LaunchProfileLabel.Text = GetActiveProfileName();
            if (LaunchLoaderLabel != null)
                LaunchLoaderLabel.Text = loader;
            if (LaunchVersionLabel != null)
                LaunchVersionLabel.Text = version;
            if (LaunchRamLabel != null)
                LaunchRamLabel.Text = $"{ram}GB RAM";
            if (SelectedProfileLabel != null)
                SelectedProfileLabel.Text =
                    $"● {GetActiveProfileName()}   •   {loader} {version}   •   {ram}GB RAM";
        }
        catch (Exception ex)
        {
            WriteException("MODERN UI SUMMARY ERROR", ex);
        }
    }
}
