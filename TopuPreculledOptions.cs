using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace TopuLauncher
{
    public partial class MainWindow
    {
        private static readonly object PreculledOptionsRegistration = RegisterPreculledOptions();
        private bool _preculledOptionsAppliedForLaunch;

        private static object RegisterPreculledOptions()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(PreculledOptionsLoaded));
            return new object();
        }

        private static void PreculledOptionsLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window || window.LaunchBtn == null)
                return;

            window.LaunchBtn.PreviewMouseLeftButtonDown -= window.PreparePreculledOptionsForLaunch;
            window.LaunchBtn.PreviewMouseLeftButtonDown += window.PreparePreculledOptionsForLaunch;
        }

        private void PreparePreculledOptionsForLaunch(object? sender, MouseButtonEventArgs e)
        {
            _preculledOptionsAppliedForLaunch = ApplyPreculledOptions();
        }

        private bool ApplyPreculledOptions()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_gamePath))
                    return false;

                Directory.CreateDirectory(_gamePath);

                Assembly assembly = typeof(MainWindow).Assembly;
                string? resourceName = assembly
                    .GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith("Assets.Preculled.options.txt", StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                {
                    WriteLog("PRE-CULLED OPTIONS: Embedded options.txt resource was not found.");
                    return false;
                }

                using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    WriteLog("PRE-CULLED OPTIONS: Could not open embedded options.txt resource.");
                    return false;
                }

                using StreamReader reader = new StreamReader(stream);
                string template = reader.ReadToEnd();
                string optionsPath = Path.Combine(_gamePath, "options.txt");

                List<string> existingLines = File.Exists(optionsPath)
                    ? File.ReadAllLines(optionsPath).ToList()
                    : new List<string>();

                Dictionary<string, string> enforced = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string rawLine in template.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int separator = rawLine.IndexOf(':');
                    if (separator <= 0)
                        continue;

                    string key = rawLine.Substring(0, separator).Trim();
                    string value = rawLine.Substring(separator + 1).Trim();
                    if (key.Length > 0)
                        enforced[key] = value;
                }

                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<string> merged = new List<string>(existingLines.Count + enforced.Count);

                foreach (string line in existingLines)
                {
                    int separator = line.IndexOf(':');
                    if (separator <= 0)
                    {
                        merged.Add(line);
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    if (enforced.TryGetValue(key, out string? enforcedValue))
                    {
                        merged.Add(key + ":" + enforcedValue);
                        seen.Add(key);
                    }
                    else
                    {
                        merged.Add(line);
                    }
                }

                foreach (KeyValuePair<string, string> setting in enforced)
                {
                    if (!seen.Contains(setting.Key))
                        merged.Add(setting.Key + ":" + setting.Value);
                }

                File.WriteAllLines(optionsPath, merged);
                WriteLog("PRE-CULLED OPTIONS: Applied to " + optionsPath);
                return true;
            }
            catch (Exception ex)
            {
                WriteException("PRE-CULLED OPTIONS ERROR", ex);
                return false;
            }
        }
    }
}
