using System.Collections.Generic;
using System.Text;

namespace WSJTX_Controller
{
    // Builds ADIF export text from raw field dictionaries (as produced by
    // LogbookDb.GetAdifFieldDicts) -- the export-direction counterpart to
    // AdifParser/AdifImporter, which read ADIF text into the database. Pure/testable:
    // takes plain dictionaries, no DB access of its own.
    public static class AdifExporter
    {
        public static string Header() =>
            "Jimmy ADIF export\r\n<ADIF_VER:5>3.1.4\r\n<PROGRAMID:5>Jimmy\r\n<EOH>\r\n";

        public static string BuildRecord(Dictionary<string, string> fields)
        {
            var sb = new StringBuilder();
            foreach (var kv in fields)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                sb.Append('<').Append(kv.Key).Append(':').Append(kv.Value.Length).Append('>').Append(kv.Value);
            }
            sb.Append("<eor>\r\n");
            return sb.ToString();
        }

        public static string BuildFile(IEnumerable<Dictionary<string, string>> records)
        {
            var sb = new StringBuilder();
            sb.Append(Header());
            foreach (var rec in records)
                sb.Append(BuildRecord(rec));
            return sb.ToString();
        }
    }
}
