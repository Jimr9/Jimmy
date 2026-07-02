using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;

namespace WSJTX_Controller
{
    public class ClubLogEntity
    {
        public string Name      { get; set; }
        public string Prefix    { get; set; }
        public string Continent { get; set; }
        public int    CqZone    { get; set; }
        public int    Adif      { get; set; }
        public bool   Deleted   { get; set; }
    }

    public class ClubLogProvider
    {
        private readonly string _dir;
        private readonly string _dataFile;
        private readonly string _metaFile;
        private List<ClubLogEntity> _entities = new List<ClubLogEntity>();
        private static readonly HttpClient _http =
            new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // Must be the cdn subdomain -- clublog.org/cty.php now rejects direct
        // requests ("Unsupported address... requests for this file are made to
        // https://cdn.clublog.org/").
        private const string BaseUrl = "https://cdn.clublog.org/cty.php";

        public bool     IsEnabled   { get; private set; }
        public string   LastError   { get; private set; }
        public DateTime LastUpdate  { get; private set; }
        public int      EntityCount => _entities.Count;
        private string _apiKey = "";

        public ClubLogProvider(string dataRoot)
        {
            _dir      = Path.Combine(dataRoot, "ClubLog");
            _dataFile = Path.Combine(_dir, "clublog_cty.xml");
            _metaFile = Path.Combine(_dir, "metadata.txt");
            Directory.CreateDirectory(_dir);
        }

        public void Configure(bool enabled, string apiKey)
        {
            IsEnabled = enabled;
            _apiKey   = apiKey ?? "";
        }

        public void Load()
        {
            LastUpdate = ReadMeta();
            if (File.Exists(_dataFile)) ParseFile(_dataFile);
        }

        public bool NeedsRefresh(int days) =>
            !File.Exists(_dataFile) || (DateTime.UtcNow - LastUpdate).TotalDays >= days;

        public async Task<bool> RefreshAsync()
        {
            LastError = null;
            var url = string.IsNullOrWhiteSpace(_apiKey)
                ? BaseUrl
                : $"{BaseUrl}?api={Uri.EscapeDataString(_apiKey)}";
            var tmp = Path.Combine(_dir, "clublog_cty.tmp");
            try
            {
                var data = await _http.GetStringAsync(url).ConfigureAwait(false);
                if (data.TrimStart().StartsWith("Error") || data.Length < 200)
                {
                    LastError = Redact(data.Length < 500 ? data.Trim() : "Club Log returned unexpected response.");
                    return false;
                }
                File.WriteAllText(tmp, data, System.Text.Encoding.UTF8);
                File.Copy(tmp, _dataFile, overwrite: true);
                try { File.Delete(tmp); } catch { }
                LastUpdate = DateTime.UtcNow;
                WriteMeta(LastUpdate);
                ParseFile(_dataFile);
                return _entities.Count > 0;
            }
            catch (Exception ex)
            {
                LastError = Redact(ex.Message);
                try { File.Delete(tmp); } catch { }
                return false;
            }
        }

        // The API key is a Jimmy application secret, not a user credential -- it
        // must never reach the UI or a log file, including inside an error message
        // (e.g. an HttpRequestException that happens to echo the request URI).
        private string Redact(string text) =>
            string.IsNullOrEmpty(text) || string.IsNullOrEmpty(_apiKey)
                ? text
                : text.Replace(_apiKey, "[REDACTED]");

        public ClubLogEntity FindByPrefix(string prefix)
        {
            if (!IsEnabled || string.IsNullOrEmpty(prefix)) return null;
            foreach (var e in _entities)
            {
                if (!e.Deleted &&
                    string.Equals(e.Prefix, prefix, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return null;
        }

        // Longest-prefix match: tries the full callsign, then progressively shorter
        // prefixes (e.g. "W1AW" → "W1A" → "W1" → "W") until a match is found.
        public ClubLogEntity FindByCallsign(string call)
        {
            if (!IsEnabled || _entities.Count == 0 || string.IsNullOrEmpty(call)) return null;
            call = call.ToUpperInvariant();
            for (int len = call.Length; len >= 1; len--)
            {
                string candidate = call.Substring(0, len);
                foreach (var e in _entities)
                {
                    if (!e.Deleted &&
                        string.Equals(e.Prefix, candidate, StringComparison.OrdinalIgnoreCase))
                        return e;
                }
            }
            return null;
        }

        private void ParseFile(string path)
        {
            try
            {
                var content = File.ReadAllText(path, System.Text.Encoding.UTF8).TrimStart();
                if (content.StartsWith("<"))
                    ParseXml(content);
                else
                    ParseCtyText(content);
            }
            catch (Exception ex)
            {
                LastError = "Parse error: " + ex.Message;
                _entities = new List<ClubLogEntity>();
            }
        }

        private void ParseXml(string xml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var result = new List<ClubLogEntity>();

            var entityNodes = doc.SelectNodes("//*[local-name()='ENTITY']")
                           ?? doc.SelectNodes("//*[local-name()='entity']");
            if (entityNodes != null)
            {
                foreach (XmlNode n in entityNodes)
                {
                    var name = Child(n, "NAME") ?? Child(n, "name");
                    if (string.IsNullOrEmpty(name)) continue;
                    var e = new ClubLogEntity
                    {
                        Name      = name,
                        Prefix    = Child(n, "PREFIX")  ?? Child(n, "prefix"),
                        Continent = Child(n, "CONT")    ?? Child(n, "cont"),
                        Deleted   = string.Equals(Child(n, "DELETED") ?? Child(n, "deleted"),
                                                  "TRUE", StringComparison.OrdinalIgnoreCase),
                    };
                    int v;
                    int.TryParse(Child(n, "ADIF") ?? Child(n, "adif"), out v); e.Adif   = v;
                    int.TryParse(Child(n, "CQ")   ?? Child(n, "cq"),   out v); e.CqZone = v;
                    result.Add(e);
                }
            }

            if (result.Count > 0)
                _entities = result;
            else
                LastError = "Club Log XML parsed but no entities found; format may have changed.";
        }

        private void ParseCtyText(string text)
        {
            // Standard Big CTY format
            var result = new List<ClubLogEntity>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith(" ")) continue;
                // Entity header: "Name:  cqz, ituz, cont, cap, prefix, adif, *flag:"
                var colon = line.IndexOf(':');
                if (colon < 1) continue;
                var name   = line.Substring(0, colon).Trim();
                var fields = line.Substring(colon + 1).TrimEnd(':').Split(',');
                var e = new ClubLogEntity { Name = name };
                if (fields.Length >= 3) e.Continent = fields[2].Trim();
                if (fields.Length >= 5) e.Prefix    = fields[4].Trim().TrimStart('*');
                int v;
                if (fields.Length >= 1 && int.TryParse(fields[0].Trim(), out v)) e.CqZone = v;
                if (fields.Length >= 6 && int.TryParse(fields[5].Trim().TrimStart('*'), out v)) e.Adif = v;
                if (!string.IsNullOrEmpty(name)) result.Add(e);
            }

            if (result.Count > 0)
                _entities = result;
            else
                LastError = "Club Log CTY text parsed but no entities found.";
        }

        private static string Child(XmlNode parent, string tag)
        {
            var n = parent.SelectSingleNode($"*[local-name()='{tag}']");
            var t = n?.InnerText?.Trim();
            return string.IsNullOrEmpty(t) ? null : t;
        }

        private DateTime ReadMeta()
        {
            try
            {
                if (!File.Exists(_metaFile)) return DateTime.MinValue;
                DateTime dt;
                return DateTime.TryParse(File.ReadAllText(_metaFile).Trim(), out dt)
                    ? dt : DateTime.MinValue;
            }
            catch { return DateTime.MinValue; }
        }

        private void WriteMeta(DateTime dt)
        {
            try { File.WriteAllText(_metaFile, dt.ToString("o")); } catch { }
        }
    }
}
