using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TopuLauncher
{
    // Modern runtime layout layer. Keeps the existing launcher logic and named controls intact,
    // but replaces the old sidebar presentation with a compact top navigation and modern profile cards.
    public partial class MainWindow
    {
        private static readonly object ProfessionalUiRegistration = RegisterProfessionalUi();
        private bool _professionalUiApplied;
        private WrapPanel? _profileCards;
        private Button? _editProfileButton;
        private Border? _profileTools;
        private Border? _profileSettingsCard;

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
            if (_professionalUiApplied) return;
            _professionalUiApplied = true;

            MinWidth = Math.Max(MinWidth, 1050);
            MinHeight = Math.Max(MinHeight, 700);
            Width = Math.Max(Width, 1240);
            Height = Math.Max(Height, 800);
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            StyleExistingShell();
            BuildTopNavigation();
            BuildModernProfileCards();
            BuildProfileTools();
            LockRuntimeSelectorsForInstalledProfile();
            if (LaunchBtn != null) LaunchBtn.Click += RefreshRuntimeLockAfterLaunch;
        }

        private void StyleExistingShell()
        {
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
                        border.Background = new LinearGradientBrush(Color.FromRgb(25, 30, 36), Color.FromRgb(15, 19, 24), new Point(0, 0), new Point(1, 1));
                        border.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 53, 62));
                        border.Effect = SoftShadow(14, 0.2);
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
                if (button == LaunchBtn || button == TabLaunchBtn || button == TabProfilesBtn || button == TabAccountsBtn) continue;
                AddButtonMotion(button);
            }
        }

        private void BuildTopNavigation()
        {
            if (TabLaunchBtn == null || TabProfilesBtn == null || TabAccountsBtn == null) return;

            DependencyObject? scroll = VisualTreeHelper.GetParent(TabLaunch);
            if (scroll is not ScrollViewer scrollViewer) return;
            if (VisualTreeHelper.GetParent(scrollViewer) is not Grid bodyGrid) return;
            if (VisualTreeHelper.GetParent(bodyGrid) is not Grid rootGrid) return;

            if (bodyGrid.RowDefinitions.Count > 0) return;

            bodyGrid.ColumnDefinitions.Clear();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border? oldSidebar = bodyGrid.Children.OfType<Border>().FirstOrDefault(x => x != scrollViewer && x.Child is Grid);
            if (oldSidebar != null) oldSidebar.Visibility = Visibility.Collapsed;

            Grid.SetColumn(scrollViewer, 0);
            Grid.SetRow(scrollViewer, 1);
            Grid.SetColumnSpan(scrollViewer, 1);
            scrollViewer.Margin = new Thickness(0);

            StackPanel nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(28, 0, 0, 0) };
            Border navBorder = new Border
            {
                Background = new LinearGradientBrush(Color.FromRgb(17, 21, 27), Color.FromRgb(11, 14, 18), new Point(0, 0), new Point(1, 0)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(36, 42, 49)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = nav
            };
            Grid.SetRow(navBorder, 0);
            Grid.SetColumn(navBorder, 0);
            bodyGrid.Children.Add(navBorder);

            MoveButtonToNav(TabLaunchBtn, nav);
            MoveButtonToNav(TabProfilesBtn, nav);
            MoveButtonToNav(TabAccountsBtn, nav);

            foreach (Button b in new[] { TabLaunchBtn, TabProfilesBtn, TabAccountsBtn })
            {
                b.Width = 150;
                b.Height = 40;
                b.Margin = new Thickness(0, 0, 8, 0);
                b.HorizontalContentAlignment = HorizontalAlignment.Center;
                b.Padding = new Thickness(12, 0, 12, 0);
                b.BorderThickness = new Thickness(0);
            }

            rootGrid.UpdateLayout();
        }

        private static void MoveButtonToNav(Button button, Panel nav)
        {
            if (button.Parent is Panel oldParent)
                oldParent.Children.Remove(button);
            nav.Children.Add(button);
        }

        private void BuildModernProfileCards()
        {
            if (ProfileSelector == null) return;
            if (VisualTreeHelper.GetParent(ProfileSelector) is not Panel parent) return;
            if (_profileCards != null) return;

            int index = parent.Children.IndexOf(ProfileSelector);
            if (index < 0) return;

            ProfileSelector.Visibility = Visibility.Collapsed;
            _profileCards = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 2)
            };
            parent.Children.Insert(index, _profileCards);

            ProfileSelector.SelectionChanged += ModernProfileSelectionChanged;
            RebuildProfileCards();
        }

        private void RebuildProfileCards()
        {
            if (_profileCards == null || ProfileSelector == null) return;
            _profileCards.Children.Clear();

            foreach (object item in ProfileSelector.Items)
            {
                string name = item?.ToString() ?? "default";
                Button card = new Button
                {
                    Content = BuildProfileCardContent(name),
                    Width = 150,
                    Height = 94,
                    Margin = new Thickness(0, 0, 10, 10),
                    Padding = new Thickness(14),
                    Background = new SolidColorBrush(Color.FromRgb(20, 24, 29)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(43, 49, 57)),
                    BorderThickness = new Thickness(1),
                    Tag = name,
                    Cursor = Cursors.Hand,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                card.Template = CreateCardButtonTemplate();
                card.Click += ProfileCard_Click;
                _profileCards.Children.Add(card);
            }

            UpdateProfileCardSelection();
        }

        private static FrameworkElement BuildProfileCardContent(string name)
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "PROFILE", Foreground = new SolidColorBrush(Color.FromRgb(92, 101, 112)), FontSize = 8, FontWeight = FontWeights.Bold });
            panel.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 7, 0, 0) });
            panel.Children.Add(new TextBlock { Text = "Minecraft instance", Foreground = new SolidColorBrush(Color.FromRgb(116, 125, 137)), FontSize = 9, Margin = new Thickness(0, 5, 0, 0) });
            return panel;
        }

        private static ControlTemplate CreateCardButtonTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentSourceProperty, "Content");
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            return new ControlTemplate(typeof(Button)) { VisualTree = border };
        }

        private void ProfileCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string profile && ProfileSelector != null)
                ProfileSelector.SelectedItem = profile;
        }

        private void ModernProfileSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            RebuildProfileCards();
            LockRuntimeSelectorsForInstalledProfile();
        }

        private void UpdateProfileCardSelection()
        {
            if (_profileCards == null || ProfileSelector == null) return;
            string selected = ProfileSelector.SelectedItem?.ToString() ?? "";
            foreach (Button card in _profileCards.Children.OfType<Button>())
            {
                bool active = string.Equals(card.Tag?.ToString(), selected, StringComparison.OrdinalIgnoreCase);
                card.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(0, 255, 136)) : new SolidColorBrush(Color.FromRgb(43, 49, 57));
                card.BorderThickness = new Thickness(active ? 2 : 1);
                card.Background = active ? new SolidColorBrush(Color.FromRgb(13, 40, 28)) : new SolidColorBrush(Color.FromRgb(20, 24, 29));
            }
        }

        private void BuildProfileTools()
        {
            if (TabProfiles == null || _profileTools != null) return;

            _profileSettingsCard = FindProfileSettingsCard();
            if (_profileSettingsCard != null) _profileSettingsCard.Visibility = Visibility.Collapsed;

            _profileTools = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 21, 26)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 48, 56)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14)
            };
            StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal };
            _editProfileButton = MakeToolButton("Edit", true);
            _editProfileButton.Click += EditProfile_Click;
            tools.Children.Add(_editProfileButton);
            tools.Children.Add(MakeToolButton("Add Mod", false, AddModTool_Click));
            tools.Children.Add(MakeToolButton("Add Modpack", false, AddModpackTool_Click));
            tools.Children.Add(MakeToolButton("Open Game Directory", false, OpenGameDirectoryTool_Click));
            _profileTools.Child = tools;

            int insert = Math.Min(4, TabProfiles.Children.Count);
            TabProfiles.Children.Insert(insert, _profileTools);
        }

        private Border? FindProfileSettingsCard()
        {
            return FindVisualChildren<Border>(TabProfiles).FirstOrDefault(b => FindVisualChildren<TextBlock>(b).Any(t => t.Text == "PROFILE SETTINGS"));
        }

        private static Button MakeToolButton(string text, bool primary, RoutedEventHandler? handler = null)
        {
            Button b = new Button
            {
                Content = text,
                Height = 36,
                Padding = new Thickness(16, 0, 16, 0),
                Margin = new Thickness(0, 0, 8, 0),
                Background = primary ? new SolidColorBrush(Color.FromRgb(0, 255, 136)) : new SolidColorBrush(Color.FromRgb(31, 37, 44)),
                Foreground = primary ? new SolidColorBrush(Color.FromRgb(4, 16, 10)) : Brushes.White,
                BorderBrush = primary ? new SolidColorBrush(Color.FromRgb(0, 255, 136)) : new SolidColorBrush(Color.FromRgb(55, 63, 72)),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            b.Template = CreateCardButtonTemplate();
            if (handler != null) b.Click += handler;
            return b;
        }

        private void EditProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_profileSettingsCard == null) return;
            _profileSettingsCard.Visibility = _profileSettingsCard.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            StatusText.Text = _profileSettingsCard.Visibility == Visibility.Visible ? "Profile settings opened." : "Profile settings closed.";
        }

        private void AddModTool_Click(object sender, RoutedEventArgs e)
        {
            if (ModSearchInput != null)
            {
                ModSearchInput.Focus();
                if (_profileSettingsCard != null) _profileSettingsCard.Visibility = Visibility.Visible;
            }
            StatusText.Text = "Enter a mod name and use Search & Add.";
        }

        private void AddModpackTool_Click(object sender, RoutedEventArgs e)
        {
            if (ModSearchInput != null)
            {
                ModSearchInput.Focus();
                if (_profileSettingsCard != null) _profileSettingsCard.Visibility = Visibility.Visible;
            }
            StatusText.Text = "Use the profile's Modrinth area to add compatible content to this instance.";
        }

        private void OpenGameDirectoryTool_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _gamePath, UseShellExecute = true });
                StatusText.Text = "Opened the active profile directory.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Could not open the profile directory.";
                WriteException("OPEN PROFILE DIRECTORY ERROR", ex);
            }
        }

        private void RefreshRuntimeLockAfterLaunch(object? sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(LockRuntimeSelectorsForInstalledProfile));
        }

        private void LockRuntimeSelectorsForInstalledProfile()
        {
            if (VersionBox == null || ProfileSelector == null) return;
            string path = _gamePath;
            string versions = System.IO.Path.Combine(path, "versions");
            bool installed = System.IO.Directory.Exists(versions) && System.IO.Directory.EnumerateFiles(versions, "*.json", System.IO.SearchOption.AllDirectories).Any();
            VersionBox.IsEnabled = !installed;
        }

        private static DropShadowEffect SoftShadow(double blur, double opacity) => new DropShadowEffect { BlurRadius = blur, ShadowDepth = 0, Opacity = opacity, Color = Colors.Black };

        private static void StyleNavigationButton(Button button)
        {
            if (button == null) return;
            button.FontSize = 13;
            button.FontWeight = FontWeights.SemiBold;
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
            button.RenderTransform = new ScaleTransform(1.015, 1.015);
        }

        private static void ProfessionalButtonMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Button button) button.RenderTransform = new ScaleTransform(1, 1);
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
