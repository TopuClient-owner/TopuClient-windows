using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TopuLauncher
{
    // Visual-only polish layer. It intentionally leaves the existing layout and
    // launcher logic alone while giving Topu Client a more premium desktop look.
    public partial class MainWindow
    {
        private static readonly object ProfessionalUiRegistration = RegisterProfessionalUi();
        private bool _professionalUiApplied;

        private static object RegisterProfessionalUi()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(ProfessionalUiLoaded));
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

            // Slightly roomier proportions for a desktop launcher.
            MinWidth = Math.Max(MinWidth, 940);
            MinHeight = Math.Max(MinHeight, 670);

            foreach (Border border in FindVisualChildren<Border>(this))
            {
                if (border.Background is SolidColorBrush brush &&
                    brush.Color == Color.FromRgb(25, 27, 32))
                {
                    border.Effect = new DropShadowEffect
                    {
                        BlurRadius = 18,
                        ShadowDepth = 0,
                        Opacity = 0.22
                    };
                }
            }

            if (LaunchBtn != null)
            {
                LaunchBtn.Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.38
                };
                LaunchBtn.MouseEnter += ProfessionalLaunchMouseEnter;
                LaunchBtn.MouseLeave += ProfessionalLaunchMouseLeave;
            }

            AddButtonMotion(TabLaunchBtn);
            AddButtonMotion(TabProfilesBtn);
            AddButtonMotion(TabAccountsBtn);

            foreach (Button button in FindVisualChildren<Button>(this))
            {
                if (button == LaunchBtn || button == TabLaunchBtn || button == TabProfilesBtn || button == TabAccountsBtn)
                    continue;

                if (button.Style != null)
                    AddButtonMotion(button);
            }
        }

        private static void AddButtonMotion(Button button)
        {
            button.MouseEnter += ProfessionalButtonMouseEnter;
            button.MouseLeave += ProfessionalButtonMouseLeave;
        }

        private static void ProfessionalButtonMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is not Button button || !button.IsEnabled)
                return;

            button.RenderTransformOrigin = new Point(0.5, 0.5);
            button.RenderTransform = new ScaleTransform(1.015, 1.015);
        }

        private static void ProfessionalButtonMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Button button)
                button.RenderTransform = new ScaleTransform(1, 1);
        }

        private void ProfessionalLaunchMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (LaunchBtn == null || !LaunchBtn.IsEnabled)
                return;

            LaunchBtn.Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 0,
                Opacity = 0.55
            };
        }

        private void ProfessionalLaunchMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (LaunchBtn == null)
                return;

            LaunchBtn.Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.38
            };
        }

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;

                foreach (T nested in FindVisualChildren<T>(child))
                    yield return nested;
            }
        }
    }
}
