using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TopuLauncher
{
    // Runtime-only visual polish. Launcher logic, profile logic and controls stay intact.
    // This layer upgrades the existing WPF shell without adding a UI framework or dependency.
    public partial class MainWindow
    {
        private static readonly object ProfessionalUiRegistration = RegisterProfessionalUi();
        private bool _professionalUiApplied;

        private static object RegisterProfessionalUi()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(ProfessionalUiLoaded));
            return new object();
        }

        private static void ProfessionalUiLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is MainWindow window)
                window.Dispatcher.BeginInvoke(new Action(window.ApplyProfessionalUi));
        }

        private void ApplyProfessionalUi()
        {
            if (_professionalUiApplied)
                return;

            _professionalUiApplied = true;
            MinWidth = Math.Max(MinWidth, 1020);
            MinHeight = Math.Max(MinHeight, 700);
            Width = Math.Max(Width, 1240);
            Height = Math.Max(Height, 800);
            FontFamily = new FontFamily("Segoe UI");

            foreach (Border border in FindVisualChildren<Border>(this))
            {
                if (border.CornerRadius.TopLeft >= 10)
                {
                    border.SnapsToDevicePixels = true;
                    border.UseLayoutRounding = true;
                }

                if (border.Background is SolidColorBrush brush)
                {
                    Color c = brush.Color;
                    if (c == Color.FromRgb(21, 25, 30) || c == Color.FromRgb(25, 27, 32))
                    {
                        border.Background = new LinearGradientBrush(Color.FromRgb(24, 29, 35), Color.FromRgb(17, 21, 26), new Point(0, 0), new Point(1, 1));
                        border.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 53, 62));
                        border.Effect = SoftShadow(12, 0.18);
                    }
                    else if (c == Color.FromRgb(15, 18, 22))
                    {
                        border.Background = new LinearGradientBrush(Color.FromRgb(14, 18, 23), Color.FromRgb(9, 12, 16), new Point(0, 0), new Point(1, 1));
                    }
                    else if (c == Color.FromRgb(17, 20, 25))
                    {
                        border.Background = new LinearGradientBrush(Color.FromRgb(19, 24, 30), Color.FromRgb(12, 15, 19), new Point(0, 0), new Point(1, 0));
                    }
                }
            }

            StyleNavigationButton(TabLaunchBtn);
            StyleNavigationButton(TabProfilesBtn);
            StyleNavigationButton(TabAccountsBtn);

            if (LaunchBtn != null)
            {
                LaunchBtn.Effect = SoftShadow(22, 0.42);
                LaunchBtn.MouseEnter += ProfessionalLaunchMouseEnter;
                LaunchBtn.MouseLeave += ProfessionalLaunchMouseLeave;
            }

            foreach (Button button in FindVisualChildren<Button>(this))
            {
                if (button == LaunchBtn || button == TabLaunchBtn || button == TabProfilesBtn || button == TabAccountsBtn)
                    continue;
                AddButtonMotion(button);
            }

            foreach (TextBlock text in FindVisualChildren<TextBlock>(this))
            {
                if (text.FontSize >= 28)
                    text.FontWeight = FontWeights.SemiBold;
                else if (text.FontSize >= 18)
                    text.FontWeight = FontWeights.SemiBold;
            }
        }

        private static DropShadowEffect SoftShadow(double blur, double opacity)
        {
            return new DropShadowEffect { BlurRadius = blur, ShadowDepth = 0, Opacity = opacity, Color = Color.FromRgb(0, 0, 0) };
        }

        private static void StyleNavigationButton(Button button)
        {
            if (button == null) return;
            button.FontSize = 13;
            button.FontWeight = FontWeights.SemiBold;
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            AddButtonMotion(button);
        }

        private static void AddButtonMotion(Button button)
        {
            button.MouseEnter -= ProfessionalButtonMouseEnter;
            button.MouseLeave -= ProfessionalButtonMouseLeave;
            button.MouseEnter += ProfessionalButtonMouseEnter;
            button.MouseLeave += ProfessionalButtonMouseLeave;
        }

        private static void ProfessionalButtonMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Button button || !button.IsEnabled) return;
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            button.RenderTransform = new ScaleTransform(1.018, 1.018);
        }

        private static void ProfessionalButtonMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Button button)
                button.RenderTransform = new ScaleTransform(1, 1);
        }

        private void ProfessionalLaunchMouseEnter(object sender, MouseEventArgs e)
        {
            if (LaunchBtn == null || !LaunchBtn.IsEnabled) return;
            LaunchBtn.Effect = SoftShadow(32, 0.62);
            LaunchBtn.RenderTransformOrigin = new Point(0.5, 0.5);
            LaunchBtn.RenderTransform = new ScaleTransform(1.012, 1.012);
        }

        private void ProfessionalLaunchMouseLeave(object sender, MouseEventArgs e)
        {
            if (LaunchBtn == null) return;
            LaunchBtn.Effect = SoftShadow(22, 0.42);
            LaunchBtn.RenderTransform = new ScaleTransform(1, 1);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (T nested in FindVisualChildren<T>(child)) yield return nested;
            }
        }
    }
}
