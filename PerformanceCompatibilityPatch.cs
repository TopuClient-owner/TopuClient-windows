using System;
using System.IO;
using System.Windows;

namespace TopuLauncher
{
    public partial class MainWindow
    {
        private static readonly bool PerformanceCompatibilityHook = RegisterPerformanceCompatibilityHook();

        private static bool RegisterPerformanceCompatibilityHook()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(ApplyPerformanceCompatibility));
            return true;
        }

        private static void ApplyPerformanceCompatibility(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window)
                return;

            PerformanceMods[0] = ("fabric-api", "Fabric API");
            PerformanceMods[1] = ("sodium", "Sodium");
            PerformanceMods[2] = ("lithium", "Lithium");
            PerformanceMods[3] = ("dynamic-fps", "Dynamic FPS");
            PerformanceMods[4] = ("ferrite-core", "FerriteCore");
            PerformanceMods[5] = ("immediatelyfast", "ImmediatelyFast");

            RemoveLegacyPerformanceMods(window, window._gamePath);
        }

        private static void RemoveLegacyPerformanceMods(MainWindow window, string gamePath)
        {
            try
            {
                string modsPath = Path.Combine(gamePath, "mods");
                if (!Directory.Exists(modsPath)) return;

                foreach (string file in Directory.EnumerateFiles(modsPath, "*.jar"))
                {
                    string name = Path.GetFileName(file);
                    if (!name.Contains("sodium-extra", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("sodium_extra", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        File.Delete(file);
                        window.WriteLog($"Removed obsolete performance mod: {name}");
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
