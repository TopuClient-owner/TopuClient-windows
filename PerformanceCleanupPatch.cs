using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace TopuLauncher
{
    public partial class MainWindow
    {
        private static readonly bool PerformanceCleanupHook = RegisterPerformanceCleanupHook();

        private static bool RegisterPerformanceCleanupHook()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(CleanupLegacyPerformanceMods));
            return true;
        }

        private static void CleanupLegacyPerformanceMods(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window)
                return;

            try
            {
                string modsPath = Path.Combine(window._gamePath, "mods");
                if (!Directory.Exists(modsPath))
                    return;

                foreach (string file in Directory.EnumerateFiles(modsPath, "*.jar"))
                {
                    string name = Path.GetFileName(file);
                    if (!name.Contains("sodium-extra", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("sodium_extra", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("krypton", StringComparison.OrdinalIgnoreCase))
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
