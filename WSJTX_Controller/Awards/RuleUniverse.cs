using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WSJTX_Controller
{
    // Resolves a RuleDefinition's Universe string into a concrete checklist to
    // compare worked/confirmed values against. Built-in universes are small,
    // stable lists that rarely change; "File:name.txt" universes are companion
    // files dropped in the RuleDefinitions\Lists folder.
    public static class RuleUniverse
    {
        public static readonly string[] Us50States =
        {
            "AK","AL","AR","AZ","CA","CO","CT","DE","FL","GA","HI","IA","ID","IL","IN",
            "KS","KY","LA","MA","MD","ME","MI","MN","MO","MS","MT","NC","ND","NE","NH","NJ",
            "NM","NV","NY","OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VA","VT","WA",
            "WI","WV","WY"
        };

        // Matches the ADIF CONT field's 2-letter continent codes.
        public static readonly string[] Continents = { "NA", "SA", "EU", "AF", "AS", "OC" };

        public static string[] CqZones =>
            Enumerable.Range(1, 40).Select(z => z.ToString()).ToArray();

        // ITU zones are numbered 1-90.
        public static string[] ItuZones =>
            Enumerable.Range(1, 90).Select(z => z.ToString()).ToArray();

        // Returns null (with a human-readable reason in error) if the universe can't
        // be resolved: unknown name, missing companion file, or a dynamic source
        // (e.g. DXCC_CURRENT) not yet wired up. A null return means "ALL"-type
        // completion/still-needed can't be computed for this award right now, but
        // COUNT/LEVELS-type awards never call this at all.
        public static HashSet<string> Resolve(string universe, string listsFolder, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(universe))
            {
                error = "No Universe specified.";
                return null;
            }

            var cmp = StringComparer.OrdinalIgnoreCase;

            if (universe.Equals("US_50_STATES", StringComparison.OrdinalIgnoreCase))
                return new HashSet<string>(Us50States, cmp);
            if (universe.Equals("CONTINENTS", StringComparison.OrdinalIgnoreCase))
                return new HashSet<string>(Continents, cmp);
            if (universe.Equals("CQ_ZONES", StringComparison.OrdinalIgnoreCase))
                return new HashSet<string>(CqZones, cmp);
            if (universe.Equals("ITU_ZONES", StringComparison.OrdinalIgnoreCase))
                return new HashSet<string>(ItuZones, cmp);

            if (universe.Equals("DXCC_CURRENT", StringComparison.OrdinalIgnoreCase))
            {
                error = "DXCC_CURRENT is not implemented yet (needs Club Log entity list integration). " +
                        "Use Target.Type=COUNT for DXCC-style awards until this is added.";
                return null;
            }

            if (universe.StartsWith("File:", StringComparison.OrdinalIgnoreCase))
            {
                string fileName = universe.Substring("File:".Length).Trim();
                string path = Path.Combine(listsFolder, fileName);
                if (!File.Exists(path))
                {
                    error = $"Universe file not found: {path}";
                    return null;
                }

                var set = new HashSet<string>(cmp);
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

                    // Strip a trailing inline comment (e.g. "1  ; Canada").
                    int c1 = line.IndexOf(';');
                    int c2 = line.IndexOf('#');
                    int cut = c1 >= 0 && c2 >= 0 ? Math.Min(c1, c2) : Math.Max(c1, c2);
                    if (cut >= 0) line = line.Substring(0, cut).Trim();

                    if (line.Length > 0) set.Add(line);
                }
                return set;
            }

            error = $"Unknown Universe: {universe}";
            return null;
        }
    }
}
