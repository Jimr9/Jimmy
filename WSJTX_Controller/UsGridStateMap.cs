using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace WSJTX_Controller
{
    internal static class UsGridStateMap
    {
        internal static readonly Dictionary<string, string> Map;

        static UsGridStateMap()
        {
            Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = FindGridDat();
            if (path != null)
                LoadGridDat(path);
        }

        internal static bool TryGetState(string grid, out string state)
        {
            state = null;
            if (grid == null || grid.Length < 4) return false;
            return Map.TryGetValue(grid.Substring(0, 4), out state);
        }

        private static string FindGridDat()
        {
            // 1. Standard Windows install locations
            string[] stdPaths = new[]
            {
                @"C:\Program Files\WSJT-X\share\wsjtx\grid.dat",
                @"C:\Program Files (x86)\WSJT-X\share\wsjtx\grid.dat",
            };
            foreach (string p in stdPaths)
            {
                if (File.Exists(p)) return p;
            }

            // 2. Registry-derived install location
            string regPath = FindGridDatViaRegistry();
            if (regPath != null) return regPath;

            // 3. Exe-adjacent portable location
            try
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string portable = Path.Combine(exeDir, "share", "wsjtx", "grid.dat");
                if (File.Exists(portable)) return portable;
            }
            catch { }

            return null;
        }

        private static string FindGridDatViaRegistry()
        {
            string[] regKeys = new[]
            {
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            };

            foreach (string keyPath in regKeys)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
                    {
                        if (key == null) continue;
                        foreach (string subName in key.GetSubKeyNames())
                        {
                            if (!subName.StartsWith("wsjtx", StringComparison.OrdinalIgnoreCase))
                                continue;
                            using (RegistryKey sub = key.OpenSubKey(subName))
                            {
                                if (sub == null) continue;
                                string uninstall = sub.GetValue("UninstallString") as string;
                                if (string.IsNullOrEmpty(uninstall)) continue;
                                // UninstallString: C:\WSJT\wsjtx\Uninstall.exe
                                // Strip filename to get install root
                                string installDir = Path.GetDirectoryName(uninstall);
                                if (string.IsNullOrEmpty(installDir)) continue;
                                string candidate = Path.Combine(installDir, "share", "wsjtx", "grid.dat");
                                if (File.Exists(candidate)) return candidate;
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private static void LoadGridDat(string path)
        {
            try
            {
                string prefix = null;
                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw.TrimEnd();
                    if (line.Length == 0) continue;

                    // Group header: exactly 2 letters followed by <
                    // e.g. "EN<"
                    if (line.Length == 3 && line[2] == '<' &&
                        char.IsLetter(line[0]) && char.IsLetter(line[1]))
                    {
                        prefix = line.Substring(0, 2).ToUpperInvariant();
                        continue;
                    }

                    // Data line: starts with tab
                    // e.g. "\t34:MN-WI," or "\t34:MN-WI>"
                    if (prefix == null || line[0] != '\t') continue;

                    string content = line.TrimStart('\t');
                    int colon = content.IndexOf(':');
                    if (colon < 0) continue;

                    string suffix = content.Substring(0, colon).Trim();
                    string stateRaw = content.Substring(colon + 1);

                    // Remove trailing comma or >
                    if (stateRaw.Length > 0)
                    {
                        char last = stateRaw[stateRaw.Length - 1];
                        if (last == ',' || last == '>')
                            stateRaw = stateRaw.Substring(0, stateRaw.Length - 1);
                    }
                    stateRaw = stateRaw.Trim();

                    if (suffix.Length == 0 || stateRaw.Length == 0) continue;

                    string key = prefix + suffix;
                    if (key.Length == 4)
                        Map[key] = stateRaw;
                }
            }
            catch { }
        }
    }
}
