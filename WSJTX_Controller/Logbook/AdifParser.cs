using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WSJTX_Controller
{
    // Streaming ADIF parser.  Yields one record dictionary per QSO.
    // Field names are returned upper-cased; values are trimmed.
    public static class AdifParser
    {
        public static IEnumerable<Dictionary<string, string>> Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            using (var sr = new StringReader(text))
                foreach (var rec in Parse(sr))
                    yield return rec;
        }

        public static IEnumerable<Dictionary<string, string>> Parse(TextReader reader)
        {
            var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int ch;
            while ((ch = reader.Read()) != -1)
            {
                if ((char)ch != '<') continue;

                // Read tag up to the closing '>'
                var tagBuf = new StringBuilder(32);
                while ((ch = reader.Read()) != -1 && (char)ch != '>')
                    tagBuf.Append((char)ch);
                string tag = tagBuf.ToString().Trim();
                if (tag.Length == 0) continue;

                // End-of-header marker: discard whatever was accumulated so far (header
                // fields), then start the first QSO record fresh. Some sources (e.g. the
                // QRZ Logbook API's FETCH response) omit <EOH> entirely -- in that case
                // there are no header fields to discard, and the first QSO's fields
                // simply accumulate from the start.
                if (string.Equals(tag, "EOH", StringComparison.OrdinalIgnoreCase))
                {
                    record.Clear();
                    continue;
                }

                // End-of-record marker
                if (string.Equals(tag, "EOR", StringComparison.OrdinalIgnoreCase))
                {
                    if (record.Count > 0)
                    {
                        yield return record;
                        record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    continue;
                }

                // Field tag: FIELDNAME:LENGTH or FIELDNAME:LENGTH:TYPE
                var parts = tag.Split(':');
                if (parts.Length < 2) continue;

                string fieldName = parts[0].Trim().ToUpperInvariant();
                int length;
                if (!int.TryParse(parts[1].Trim(), out length) || length < 0) continue;

                // Read exactly 'length' characters
                string value = "";
                if (length > 0)
                {
                    var buf = new char[length];
                    int total = 0;
                    while (total < length)
                    {
                        int n = reader.Read(buf, total, length - total);
                        if (n == 0) break;
                        total += n;
                    }
                    value = new string(buf, 0, total).Trim();
                }

                if (fieldName.Length > 0 && value.Length > 0)
                    record[fieldName] = value;
            }

            // File ended without final <EOR>
            if (record.Count > 0)
                yield return record;
        }
    }
}
