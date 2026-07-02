using System;
using System.Collections.Generic;
using System.IO;

namespace WSJTX_Controller
{
    // Lightweight line-based INI reader for Rule Definition files.
    // IniFile.cs wraps the Win32 profile API, which truncates values at 255
    // characters and has no way to enumerate the keys within a section. Rule
    // Definition files need both (long companion-file paths/descriptions, and
    // open-ended [Levels] tier names), so this is a separate, simple parser.
    public class RuleFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public static RuleFile Load(string path)
        {
            var file = new RuleFile();
            string currentSection = "";

            foreach (var rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    if (!file._sections.ContainsKey(currentSection))
                        file._sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Length == 0) continue;

                Dictionary<string, string> section;
                if (!file._sections.TryGetValue(currentSection, out section))
                {
                    section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    file._sections[currentSection] = section;
                }
                section[key] = value;
            }

            return file;
        }

        public bool HasSection(string section) => _sections.ContainsKey(section);

        public string Get(string section, string key, string defaultValue = null)
        {
            Dictionary<string, string> s;
            string v;
            if (_sections.TryGetValue(section, out s) && s.TryGetValue(key, out v))
                return v;
            return defaultValue;
        }

        // Every key/value pair in a section -- used for [Levels], where tier names
        // are arbitrary and not known in advance.
        public IEnumerable<KeyValuePair<string, string>> GetSection(string section)
        {
            Dictionary<string, string> s;
            if (_sections.TryGetValue(section, out s)) return s;
            return Array.Empty<KeyValuePair<string, string>>();
        }
    }
}
