using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WSJTX_Controller
{
    public enum QrzLookupPolicy
    {
        Disabled         = 0,  // Default — never make automatic QRZ requests
        FocusedOnly      = 1,  // QRZ used only when user explicitly opens Lookup dialog
        UnidentifiedQueue = 2, // QRZ supplements offline data for queued stations it cannot identify
    }

    public class LookupInfo
    {
        public string   Callsign     { get; set; }
        public string   Name         { get; set; }
        public string   Grid         { get; set; }
        public string   State        { get; set; }
        public string   Country      { get; set; }
        public string   Continent    { get; set; }
        public string   County       { get; set; }
        public int      CqZone       { get; set; }
        public int      ItuZone      { get; set; }
        public string   QslManager   { get; set; }
        public string   Email        { get; set; }
        public int      AdifEntity   { get; set; }
        public bool     IsLoTWUser   { get; set; }
        public DateTime?LoTWActivity { get; set; }
        public string   Sources      { get; set; }  // comma list: "QRZ cache", "LoTW", "Club Log"
    }

    public class LookupManager
    {
        private bool            _useLookupData;
        private QrzLookupPolicy _policy  = QrzLookupPolicy.Disabled;
        private int             _qrzMinIntervalSeconds = 10;

        public QrzProvider     Qrz     { get; }
        public LoTWProvider    LoTW    { get; }
        public ClubLogProvider ClubLog { get; }

        public bool             Enabled => _useLookupData &&
                                          (Qrz.IsEnabled || LoTW.IsEnabled || ClubLog.IsEnabled);
        public QrzLookupPolicy  Policy  => _policy;

        public static string DataRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Jimmy", "Data");

        // Callback invoked on background thread when an auto-lookup completes.
        // WsjtxClient should BeginInvoke to marshal back to the UI thread.
        public Action OnLookupCompleted;

        private readonly ConcurrentQueue<string> _autoQueue  = new ConcurrentQueue<string>();
        private readonly HashSet<string>          _queued     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object                   _queuedLock = new object();
        private readonly SemaphoreSlim            _autoSem    = new SemaphoreSlim(1, 1);
        private System.Timers.Timer               _autoTimer;

        public LookupManager()
        {
            var root = DataRoot;
            Directory.CreateDirectory(Path.Combine(root, "Temp"));
            Qrz     = new QrzProvider(root);
            LoTW    = new LoTWProvider(root);
            ClubLog = new ClubLogProvider(root);
        }

        public void Initialize(
            bool            useLookupData,
            bool            qrzEnabled,      string qrzUser,   string qrzPass, int qrzCacheDays,
            bool            lotwEnabled,     int    lotwDays,
            // clubLogAppKey is Jimmy's registered Club Log application key (see
            // ClubLogAppKey.Resolve()), not a per-user credential.
            string          clubLogAppKey,   int    clubLogDays,
            QrzLookupPolicy policy           = QrzLookupPolicy.Disabled,
            int             qrzMinIntervalSeconds = 10)
        {
            _useLookupData         = useLookupData;
            _policy                = policy;
            _qrzMinIntervalSeconds = Math.Max(5, qrzMinIntervalSeconds);

            Qrz.Configure(qrzEnabled,     qrzUser,     qrzPass,     qrzCacheDays);
            LoTW.Configure(lotwEnabled);

            // Club Log country data is Jimmy infrastructure, not a user-facing
            // lookup feature -- it always loads/refreshes regardless of the
            // "Use Lookup Data" master switch, since Rule Definition universes
            // depend on it and have nothing to do with QRZ/LoTW live lookups.
            ClubLog.Configure(true, clubLogAppKey);
            ClubLog.Load();

            if (!useLookupData) { StopAutoTimer(); return; }

            if (lotwEnabled) LoTW.Load();
            if (qrzEnabled)  Qrz.PurgeOldEntries();

            StartAutoTimer();
        }

        public void StartBackgroundRefreshIfNeeded(int lotwDays, int clubLogDays)
        {
            if (ClubLog.NeedsRefresh(clubLogDays))
                _ = ClubLog.RefreshAsync();

            if (!_useLookupData) return;
            if (LoTW.IsEnabled && LoTW.NeedsRefresh(lotwDays))
                _ = LoTW.RefreshAsync();
        }

        // ── Synchronous lookups (no network) ────────────────────────────────────

        public CallsignLookupResult GetCachedInfo(string call) =>
            _useLookupData ? Qrz.GetCached(call) : null;

        public bool IsLoTWUser(string call) =>
            _useLookupData && LoTW.IsUser(call);

        public DateTime? LoTWLastActivity(string call) =>
            _useLookupData ? LoTW.LastActivity(call) : null;

        public ClubLogEntity GetClubLogEntity(string call) =>
            _useLookupData ? ClubLog.FindByCallsign(call) : null;

        // ── Lookup dialog aggregation ────────────────────────────────────────────

        // Build a LookupInfo for the dialog by merging all sources (no network).
        // Caller should await LookupQrzAsync first if a live lookup is desired.
        public LookupInfo GetInfoForDialog(string call)
        {
            if (string.IsNullOrEmpty(call)) return null;
            var info    = new LookupInfo { Callsign = call.ToUpperInvariant() };
            var sources = new System.Collections.Generic.List<string>();

            // QRZ cache (highest detail)
            var qrz = Qrz.GetCached(call);
            if (qrz != null)
            {
                info.Name       = qrz.Name;
                info.Grid       = qrz.Grid;
                info.State      = qrz.State;
                info.Country    = qrz.Country;
                info.Continent  = qrz.Continent;
                info.County     = qrz.County;
                info.CqZone     = qrz.CqZone;
                info.ItuZone    = qrz.ItuZone;
                info.QslManager = qrz.QslManager;
                info.Email      = qrz.Email;
                sources.Add("QRZ cache");
            }

            // Club Log (country/DXCC if QRZ didn't supply)
            var cl = ClubLog.FindByCallsign(call);
            if (cl != null)
            {
                if (string.IsNullOrEmpty(info.Country))   info.Country   = cl.Name;
                if (string.IsNullOrEmpty(info.Continent)) info.Continent = cl.Continent;
                if (info.CqZone   == 0) info.CqZone   = cl.CqZone;
                if (info.AdifEntity == 0) info.AdifEntity = cl.Adif;
                sources.Add("Club Log");
            }

            // LoTW
            if (LoTW.IsEnabled && LoTW.UserCount > 0)
            {
                info.IsLoTWUser  = LoTW.IsUser(call);
                info.LoTWActivity = LoTW.LastActivity(call);
                sources.Add("LoTW");
            }

            info.Sources = sources.Count > 0
                ? string.Join(", ", sources)
                : "No lookup data available";
            return info;
        }

        // ── QRZ async ────────────────────────────────────────────────────────────

        public Task<CallsignLookupResult> LookupQrzAsync(string call) =>
            _useLookupData && Qrz.IsEnabled
                ? Qrz.LookupAsync(call)
                : Task.FromResult<CallsignLookupResult>(null);

        public bool QrzNeedsLookup(string call) =>
            _useLookupData && Qrz.NeedsLookup(call);

        public Task<bool> TestQrzAsync() => Qrz.TestAsync();

        // ── Automatic QRZ queue (UnidentifiedQueue policy) ───────────────────────

        public bool CanAutoQueue(string call) =>
            _useLookupData &&
            _policy == QrzLookupPolicy.UnidentifiedQueue &&
            Qrz.IsEnabled &&
            Qrz.NeedsLookup(call);

        public void QueueAutoLookup(string call)
        {
            if (string.IsNullOrEmpty(call)) return;
            lock (_queuedLock)
            {
                if (_queued.Contains(call)) return;
                _queued.Add(call);
            }
            _autoQueue.Enqueue(call);
        }

        private void StartAutoTimer()
        {
            StopAutoTimer();
            if (_policy != QrzLookupPolicy.UnidentifiedQueue || !Qrz.IsEnabled) return;
            _autoTimer = new System.Timers.Timer(_qrzMinIntervalSeconds * 1000.0);
            _autoTimer.Elapsed += AutoTimerElapsed;
            _autoTimer.AutoReset = true;
            _autoTimer.Start();
        }

        private void StopAutoTimer()
        {
            try { _autoTimer?.Stop(); _autoTimer?.Dispose(); } catch { }
            _autoTimer = null;
        }

        private async void AutoTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!await _autoSem.WaitAsync(0)) return;
            try
            {
                string call;
                if (!_autoQueue.TryDequeue(out call)) return;
                lock (_queuedLock) _queued.Remove(call);
                if (!CanAutoQueue(call)) return;
                var result = await Qrz.LookupAsync(call).ConfigureAwait(false);
                if (result != null) OnLookupCompleted?.Invoke();
            }
            catch { }
            finally { _autoSem.Release(); }
        }

        public void Dispose()
        {
            StopAutoTimer();
        }
    }
}
