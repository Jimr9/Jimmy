using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace WSJTX_Controller
{
    public class LoTWProvider : ILookupProvider
    {
        private readonly string _dir;
        private readonly string _csvFile;
        private readonly string _metaFile;
        private Dictionary<string, DateTime> _users =
            new Dictionary<string, DateTime>(0, StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient _http =
            new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        private const string Url = "https://lotw.arrl.org/lotw-user-activity.csv";

        public string   SourceName => "LoTW";
        public bool     IsEnabled  { get; private set; }
        public string   LastError  { get; private set; }
        public DateTime LastUpdate { get; private set; }
        public int      UserCount  => _users.Count;

        public LoTWProvider(string dataRoot)
        {
            _dir      = Path.Combine(dataRoot, "LoTW");
            _csvFile  = Path.Combine(_dir, "lotw-user-activity.csv");
            _metaFile = Path.Combine(_dir, "metadata.txt");
            Directory.CreateDirectory(_dir);
        }

        public void Configure(bool enabled)
        {
            IsEnabled = enabled;
        }

        public void Load()
        {
            LastUpdate = ReadMeta();
            if (File.Exists(_csvFile)) ParseFile(_csvFile);
        }

        public bool IsUser(string call) =>
            IsEnabled && !string.IsNullOrEmpty(call) && _users.ContainsKey(call);

        public DateTime? LastActivity(string call)
        {
            if (!IsEnabled || string.IsNullOrEmpty(call)) return null;
            DateTime dt;
            return _users.TryGetValue(call, out dt) ? (DateTime?)dt : null;
        }

        // In-memory dictionary lookup only -- safe for the per-decode hot path.
        public void Contribute(LookupRecord record, string call)
        {
            if (!IsEnabled || string.IsNullOrEmpty(call) || UserCount == 0) return;
            DateTime activity;
            if (!_users.TryGetValue(call, out activity)) return;

            record.IsLoTWUser       = true;
            record.LoTWLastActivity = activity;
            record.Sources.Add(SourceName);
        }

        public bool NeedsRefresh(int days) =>
            !File.Exists(_csvFile) || (DateTime.UtcNow - LastUpdate).TotalDays >= days;

        public async Task<bool> RefreshAsync()
        {
            LastError = null;
            var tmp = Path.Combine(_dir, "lotw-user-activity.tmp");
            try
            {
                var data = await _http.GetStringAsync(Url).ConfigureAwait(false);
                File.WriteAllText(tmp, data, System.Text.Encoding.UTF8);
                File.Copy(tmp, _csvFile, overwrite: true);
                try { File.Delete(tmp); } catch { }
                LastUpdate = DateTime.UtcNow;
                WriteMeta(LastUpdate);
                ParseFile(_csvFile);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                try { File.Delete(tmp); } catch { }
                return false;
            }
        }

        private void ParseFile(string path)
        {
            try
            {
                var users = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadLines(path, System.Text.Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    var comma = line.IndexOf(',');
                    var call  = (comma > 0 ? line.Substring(0, comma) : line).Trim();
                    if (string.IsNullOrEmpty(call)) continue;
                    DateTime dt = DateTime.MinValue;
                    if (comma > 0)
                        DateTime.TryParse(line.Substring(comma + 1).Trim(), out dt);
                    users[call.ToUpperInvariant()] = dt;
                }
                _users = users;
            }
            catch (Exception ex)
            {
                LastError = "Parse error: " + ex.Message;
            }
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
