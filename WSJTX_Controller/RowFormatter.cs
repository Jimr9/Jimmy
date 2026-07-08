using System;
using System.Collections.Generic;
using System.Text;

namespace WSJTX_Controller
{
    // Pure "build one display row from named field fragments, in a user-configurable
    // order" logic, shared by the Stations Available row (BuildCallWaitingRow) and the
    // Raw Decodes row (ShowRawDecodes) -- extracted so both share one implementation
    // instead of two copies, and so it's directly unit-testable (JimmyTests references
    // Jimmy.exe as a compiled binary with no InternalsVisibleTo, so only public members
    // of plain classes are ever reachable from tests).
    public static class RowFormatter
    {
        // Walks `order`, appending each named field's fragment from fieldMap that's
        // actually present, skipping unknown names and duplicates. Fragments are expected
        // to already carry their own leading ", " (or leading space) separator -- the
        // leading separator is stripped from whichever fragment ends up first in the row,
        // and a separator is inserted before any later fragment that doesn't already have
        // one. Returns fallback if order is null, or if nothing in it produced any output
        // (e.g. every checked field happened to be empty for this particular decode).
        public static string BuildOrderedRow(Dictionary<string, string> fieldMap, List<string> order, string fallback)
        {
            if (order == null) return fallback;

            var sb = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in order)
            {
                if (string.IsNullOrEmpty(f) || seen.Contains(f)) continue;
                string frag;
                if (fieldMap == null || !fieldMap.TryGetValue(f, out frag)) continue;
                if (sb.Length == 0)
                {
                    if (frag.StartsWith(", ")) frag = frag.Substring(2);
                    else if (frag.Length > 0 && frag[0] == ' ') frag = frag.Substring(1);
                }
                else if (frag.Length > 0 && !frag.StartsWith(", ") && frag[0] != ' ')
                {
                    frag = ", " + frag;
                }
                sb.Append(frag);
                seen.Add(f);
            }
            return sb.Length == 0 ? fallback : sb.ToString();
        }
    }
}
