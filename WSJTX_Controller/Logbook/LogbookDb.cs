using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Data.SQLite;

namespace WSJTX_Controller
{
    public class QsoRecord
    {
        public int    Id          { get; set; }
        public string Callsign    { get; set; }
        public string Band        { get; set; }
        public string Mode        { get; set; }
        public string QsoDate     { get; set; }
        public string TimeOn      { get; set; }
        public string Country     { get; set; }
        public string State       { get; set; }
        public int    Dxcc        { get; set; }
        public int    CqZone      { get; set; }
        public string LotwQslRcvd { get; set; }
        public string QrzQslRcvd  { get; set; }
        public string Source      { get; set; }
        public string Grid        { get; set; }
        public string Name        { get; set; }
        public string RstSent     { get; set; }
        public string RstRcvd     { get; set; }
        public bool   IsConfirmed => LotwQslRcvd == "Y" || QrzQslRcvd == "Y";
    }

    public class ImportLogEntry
    {
        public int      Id         { get; set; }
        public string   Source     { get; set; }
        public DateTime StartedAt  { get; set; }
        public int      TotalQso   { get; set; }
        public int      NewQso     { get; set; }
        public int      UpdatedQso { get; set; }
        public int      SkippedQso { get; set; }
        public string   ErrorText  { get; set; }
    }

    public class BandStat
    {
        public string Label     { get; set; }
        public int    Total     { get; set; }
        public int    Confirmed { get; set; }
        public string Pct => Total > 0 ? $"{100.0 * Confirmed / Total:0.0}%" : "—";
    }

    public class LogbookDb : IDisposable
    {
        private SQLiteConnection _conn;
        private readonly object  _lock = new object();

        // WAS = 50 states only (DC is not a state and does not count).
        private const string WasInList =
            "'AK','AL','AR','AZ','CA','CO','CT','DE','FL','GA','HI','IA','ID','IL','IN'," +
            "'KS','KY','LA','MA','MD','ME','MI','MN','MO','MS','MT','NC','ND','NE','NH','NJ'," +
            "'NM','NV','NY','OH','OK','OR','PA','RI','SC','SD','TN','TX','UT','VA','VT','WA'," +
            "'WI','WV','WY'";

        // JIMMY_TEST_DB_PATH lets the replay test suite point a real, separately-running
        // Jimmy.exe at a throwaway database instead of the user's actual logbook -- unset
        // in normal operation, so behavior is unchanged.
        public static string DbPath =>
            Environment.GetEnvironmentVariable("JIMMY_TEST_DB_PATH") ??
            Path.Combine(LookupManager.DataRoot, "Logbook", "logbook.db");

        public LogbookDb()
        {
            var dir = Path.GetDirectoryName(DbPath);
            Directory.CreateDirectory(dir);
            _conn = new SQLiteConnection($"Data Source={DbPath};");
            _conn.Open();
            InitSchema();
        }

        // Opens (or creates) a database at an explicit path.
        // Used by automated tests to avoid touching the real data directory.
        public LogbookDb(string dbPath)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _conn = new SQLiteConnection($"Data Source={dbPath};");
            _conn.Open();
            InitSchema();
        }

        // ── Schema ───────────────────────────────────────────────────────────────

        private void InitSchema()
        {
            Exec("PRAGMA journal_mode=WAL;");
            Exec("PRAGMA foreign_keys=ON;");
            Exec(@"CREATE TABLE IF NOT EXISTS meta (
                key   TEXT PRIMARY KEY,
                value TEXT
            );");

            int ver;
            int.TryParse(GetMeta("db_version") ?? "0", out ver);

            if (ver < 1)
            {
                Exec(@"CREATE TABLE IF NOT EXISTS qso (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    callsign       TEXT NOT NULL,
                    band           TEXT    DEFAULT '',
                    mode           TEXT    DEFAULT '',
                    qso_date       TEXT    DEFAULT '',
                    time_on        TEXT    DEFAULT '',
                    time_off       TEXT    DEFAULT '',
                    freq_hz        INTEGER DEFAULT 0,
                    rst_sent       TEXT    DEFAULT '',
                    rst_rcvd       TEXT    DEFAULT '',
                    state          TEXT    DEFAULT '',
                    country        TEXT    DEFAULT '',
                    dxcc           INTEGER DEFAULT 0,
                    cq_zone        INTEGER DEFAULT 0,
                    grid           TEXT    DEFAULT '',
                    name           TEXT    DEFAULT '',
                    comment        TEXT    DEFAULT '',
                    tx_pwr         TEXT    DEFAULT '',
                    operator_call  TEXT    DEFAULT '',
                    station_call   TEXT    DEFAULT '',
                    my_grid        TEXT    DEFAULT '',
                    lotw_qsl_sent  TEXT    DEFAULT '',
                    lotw_qsl_rcvd  TEXT    DEFAULT '',
                    qrz_qsl_sent   TEXT    DEFAULT '',
                    qrz_qsl_rcvd   TEXT    DEFAULT '',
                    source         TEXT    NOT NULL DEFAULT 'MANUAL',
                    source_qso_id  TEXT    DEFAULT '',
                    imported_at    TEXT    NOT NULL,
                    dedup_key      TEXT    NOT NULL UNIQUE
                );");

                Exec("CREATE INDEX IF NOT EXISTS ix_call      ON qso(callsign);");
                Exec("CREATE INDEX IF NOT EXISTS ix_date      ON qso(qso_date);");
                Exec("CREATE INDEX IF NOT EXISTS ix_dxcc      ON qso(dxcc);");
                Exec("CREATE INDEX IF NOT EXISTS ix_band_mode ON qso(band, mode);");
                Exec("CREATE INDEX IF NOT EXISTS ix_state     ON qso(state);");
                Exec("CREATE INDEX IF NOT EXISTS ix_cq_zone   ON qso(cq_zone);");

                Exec(@"CREATE TABLE IF NOT EXISTS import_log (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    source      TEXT NOT NULL,
                    started_at  TEXT NOT NULL,
                    finished_at TEXT,
                    total_qso   INTEGER DEFAULT 0,
                    new_qso     INTEGER DEFAULT 0,
                    updated_qso INTEGER DEFAULT 0,
                    skipped_qso INTEGER DEFAULT 0,
                    error_text  TEXT    DEFAULT ''
                );");

                SetMeta("db_version", "1");
                ver = 1;
            }

            // v2: fields needed by the Rule Definitions (awards) engine. Standard ADIF
            // fields that were previously received (when present in the source data)
            // but discarded on import.
            if (ver < 2)
            {
                Exec("ALTER TABLE qso ADD COLUMN continent    TEXT    DEFAULT '';");
                Exec("ALTER TABLE qso ADD COLUMN itu_zone     INTEGER DEFAULT 0;");
                Exec("ALTER TABLE qso ADD COLUMN county       TEXT    DEFAULT '';");
                Exec("ALTER TABLE qso ADD COLUMN iota         TEXT    DEFAULT '';");
                Exec("ALTER TABLE qso ADD COLUMN sig          TEXT    DEFAULT '';");
                Exec("ALTER TABLE qso ADD COLUMN sig_info     TEXT    DEFAULT '';");
                Exec("ALTER TABLE qso ADD COLUMN my_sig       TEXT    DEFAULT '';");
                Exec("ALTER TABLE qso ADD COLUMN my_sig_info  TEXT    DEFAULT '';");
                Exec("ALTER TABLE qso ADD COLUMN darc_dok     TEXT    DEFAULT '';");
                Exec("ALTER TABLE qso ADD COLUMN wpx_prefix   TEXT    DEFAULT '';");

                Exec("CREATE INDEX IF NOT EXISTS ix_continent ON qso(continent);");
                Exec("CREATE INDEX IF NOT EXISTS ix_itu_zone  ON qso(itu_zone);");
                Exec("CREATE INDEX IF NOT EXISTS ix_county    ON qso(county);");
                Exec("CREATE INDEX IF NOT EXISTS ix_iota      ON qso(iota);");
                Exec("CREATE INDEX IF NOT EXISTS ix_sig_info  ON qso(sig_info);");

                SetMeta("db_version", "2");
            }

            // v3: per-QSO outbound upload tracking for QRZ/Club Log logbook upload.
            // Empty string = not yet uploaded to that service (matches this table's
            // existing convention of '' rather than NULL for "unset" TEXT columns).
            if (ver < 3)
            {
                Exec("ALTER TABLE qso ADD COLUMN qrz_uploaded_at     TEXT DEFAULT '';");
                Exec("ALTER TABLE qso ADD COLUMN clublog_uploaded_at TEXT DEFAULT '';");

                SetMeta("db_version", "3");
            }

            // v4: contest exchange fields (ADIF STX_STRING/SRX_STRING), e.g. Field
            // Day "2A MO" -- carried through to QRZ/Club Log upload so contest QSOs
            // are not missing this data there.
            if (ver < 4)
            {
                Exec("ALTER TABLE qso ADD COLUMN exchange_sent TEXT DEFAULT '';");
                Exec("ALTER TABLE qso ADD COLUMN exchange_rcvd TEXT DEFAULT '';");

                SetMeta("db_version", "4");
            }
        }

        // ── Meta ─────────────────────────────────────────────────────────────────

        public string GetMeta(string key)
        {
            lock (_lock)
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT value FROM meta WHERE key=@k;";
                    cmd.Parameters.AddWithValue("@k", key);
                    var r = cmd.ExecuteScalar();
                    return r == null || r == DBNull.Value ? null : r.ToString();
                }
            }
        }

        public void SetMeta(string key, string value)
        {
            lock (_lock)
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT INTO meta(key,value) VALUES(@k,@v) " +
                        "ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
                    cmd.Parameters.AddWithValue("@k", key);
                    cmd.Parameters.AddWithValue("@v", value ?? "");
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ── Upsert ───────────────────────────────────────────────────────────────

        // Returns (isNew, isUpdated).
        public (bool isNew, bool isUpdated) Upsert(
            string callsign, string band, string mode,
            string qsoDate, string timeOn, string timeOff,
            long freqHz, string rstSent, string rstRcvd,
            string state, string country, int dxcc, int cqZone,
            string grid, string name, string comment, string txPwr,
            string operatorCall, string stationCall, string myGrid,
            string lotwQslSent, string lotwQslRcvd,
            string qrzQslSent, string qrzQslRcvd,
            string source, string sourceQsoId, string dedupKey,
            string continent, int ituZone, string county, string iota,
            string sig, string sigInfo, string mySig, string mySigInfo,
            string darcDok, string wpxPrefix,
            string exchangeSent, string exchangeRcvd)
        {
            // Columns the ON CONFLICT clause below can modify. Compared before/after so
            // "updated" only counts rows whose data actually changed, instead of every
            // already-known QSO the source re-sends (which is nearly all of them).
            const string mutableCols =
                "lotw_qsl_sent, lotw_qsl_rcvd, qrz_qsl_sent, qrz_qsl_rcvd, country, state, name, grid, " +
                "dxcc, cq_zone, continent, itu_zone, county, iota, sig, sig_info, my_sig, my_sig_info, " +
                "darc_dok, wpx_prefix, exchange_sent, exchange_rcvd";

            lock (_lock)
            {
                bool existed;
                object[] before = null;
                using (var check = _conn.CreateCommand())
                {
                    check.CommandText = $"SELECT {mutableCols} FROM qso WHERE dedup_key=@k;";
                    check.Parameters.AddWithValue("@k", dedupKey);
                    using (var r = check.ExecuteReader())
                    {
                        existed = r.Read();
                        if (existed)
                        {
                            before = new object[r.FieldCount];
                            r.GetValues(before);
                        }
                    }
                }

                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = @"
INSERT INTO qso (
    callsign, band, mode, qso_date, time_on, time_off, freq_hz,
    rst_sent, rst_rcvd, state, country, dxcc, cq_zone, grid,
    name, comment, tx_pwr, operator_call, station_call, my_grid,
    lotw_qsl_sent, lotw_qsl_rcvd, qrz_qsl_sent, qrz_qsl_rcvd,
    source, source_qso_id, imported_at, dedup_key,
    continent, itu_zone, county, iota, sig, sig_info, my_sig, my_sig_info,
    darc_dok, wpx_prefix, exchange_sent, exchange_rcvd
) VALUES (
    @callsign, @band, @mode, @qso_date, @time_on, @time_off, @freq_hz,
    @rst_sent, @rst_rcvd, @state, @country, @dxcc, @cq_zone, @grid,
    @name, @comment, @tx_pwr, @operator_call, @station_call, @my_grid,
    @lotw_qsl_sent, @lotw_qsl_rcvd, @qrz_qsl_sent, @qrz_qsl_rcvd,
    @source, @source_qso_id, @imported_at, @dedup_key,
    @continent, @itu_zone, @county, @iota, @sig, @sig_info, @my_sig, @my_sig_info,
    @darc_dok, @wpx_prefix, @exchange_sent, @exchange_rcvd
)
ON CONFLICT(dedup_key) DO UPDATE SET
    lotw_qsl_sent = CASE WHEN excluded.source='LOTW' AND excluded.lotw_qsl_sent!='' THEN excluded.lotw_qsl_sent ELSE qso.lotw_qsl_sent END,
    lotw_qsl_rcvd = CASE WHEN qso.lotw_qsl_rcvd='Y' THEN 'Y' WHEN excluded.source='LOTW' AND excluded.lotw_qsl_rcvd!='' THEN excluded.lotw_qsl_rcvd ELSE qso.lotw_qsl_rcvd END,
    qrz_qsl_sent  = CASE WHEN excluded.source='QRZ'  AND excluded.qrz_qsl_sent !='' THEN excluded.qrz_qsl_sent  ELSE qso.qrz_qsl_sent  END,
    qrz_qsl_rcvd  = CASE WHEN qso.qrz_qsl_rcvd='Y'  THEN 'Y' WHEN excluded.source='QRZ'  AND excluded.qrz_qsl_rcvd !='' THEN excluded.qrz_qsl_rcvd  ELSE qso.qrz_qsl_rcvd  END,
    country  = CASE WHEN (qso.country ='' OR qso.country  IS NULL) AND excluded.country !='' THEN excluded.country  ELSE qso.country  END,
    state    = CASE WHEN (qso.state   ='' OR qso.state    IS NULL) AND excluded.state   !='' THEN excluded.state    ELSE qso.state    END,
    name     = CASE WHEN (qso.name    ='' OR qso.name     IS NULL) AND excluded.name    !='' THEN excluded.name     ELSE qso.name     END,
    grid     = CASE WHEN (qso.grid    ='' OR qso.grid     IS NULL) AND excluded.grid    !='' THEN excluded.grid     ELSE qso.grid     END,
    dxcc     = CASE WHEN (qso.dxcc    =0  OR qso.dxcc     IS NULL) AND excluded.dxcc    >0   THEN excluded.dxcc     ELSE qso.dxcc     END,
    cq_zone  = CASE WHEN (qso.cq_zone =0  OR qso.cq_zone  IS NULL) AND excluded.cq_zone >0   THEN excluded.cq_zone  ELSE qso.cq_zone  END,
    continent    = CASE WHEN (qso.continent   ='' OR qso.continent    IS NULL) AND excluded.continent   !='' THEN excluded.continent   ELSE qso.continent    END,
    itu_zone     = CASE WHEN (qso.itu_zone    =0  OR qso.itu_zone     IS NULL) AND excluded.itu_zone    >0   THEN excluded.itu_zone    ELSE qso.itu_zone     END,
    county       = CASE WHEN (qso.county      ='' OR qso.county       IS NULL) AND excluded.county      !='' THEN excluded.county      ELSE qso.county       END,
    iota         = CASE WHEN (qso.iota        ='' OR qso.iota         IS NULL) AND excluded.iota        !='' THEN excluded.iota         ELSE qso.iota         END,
    sig          = CASE WHEN (qso.sig         ='' OR qso.sig          IS NULL) AND excluded.sig         !='' THEN excluded.sig          ELSE qso.sig          END,
    sig_info     = CASE WHEN (qso.sig_info    ='' OR qso.sig_info     IS NULL) AND excluded.sig_info    !='' THEN excluded.sig_info     ELSE qso.sig_info     END,
    my_sig       = CASE WHEN (qso.my_sig      ='' OR qso.my_sig       IS NULL) AND excluded.my_sig      !='' THEN excluded.my_sig       ELSE qso.my_sig       END,
    my_sig_info  = CASE WHEN (qso.my_sig_info ='' OR qso.my_sig_info  IS NULL) AND excluded.my_sig_info !='' THEN excluded.my_sig_info  ELSE qso.my_sig_info  END,
    darc_dok     = CASE WHEN (qso.darc_dok    ='' OR qso.darc_dok     IS NULL) AND excluded.darc_dok    !='' THEN excluded.darc_dok     ELSE qso.darc_dok     END,
    wpx_prefix   = CASE WHEN (qso.wpx_prefix  ='' OR qso.wpx_prefix   IS NULL) AND excluded.wpx_prefix  !='' THEN excluded.wpx_prefix   ELSE qso.wpx_prefix   END,
    exchange_sent = CASE WHEN (qso.exchange_sent ='' OR qso.exchange_sent IS NULL) AND excluded.exchange_sent !='' THEN excluded.exchange_sent ELSE qso.exchange_sent END,
    exchange_rcvd = CASE WHEN (qso.exchange_rcvd ='' OR qso.exchange_rcvd IS NULL) AND excluded.exchange_rcvd !='' THEN excluded.exchange_rcvd ELSE qso.exchange_rcvd END;
";
                    cmd.Parameters.AddWithValue("@callsign",      callsign      ?? "");
                    cmd.Parameters.AddWithValue("@band",          band          ?? "");
                    cmd.Parameters.AddWithValue("@mode",          mode          ?? "");
                    cmd.Parameters.AddWithValue("@qso_date",      qsoDate       ?? "");
                    cmd.Parameters.AddWithValue("@time_on",       timeOn        ?? "");
                    cmd.Parameters.AddWithValue("@time_off",      timeOff       ?? "");
                    cmd.Parameters.AddWithValue("@freq_hz",       freqHz);
                    cmd.Parameters.AddWithValue("@rst_sent",      rstSent       ?? "");
                    cmd.Parameters.AddWithValue("@rst_rcvd",      rstRcvd       ?? "");
                    cmd.Parameters.AddWithValue("@state",         state         ?? "");
                    cmd.Parameters.AddWithValue("@country",       country       ?? "");
                    cmd.Parameters.AddWithValue("@dxcc",          dxcc);
                    cmd.Parameters.AddWithValue("@cq_zone",       cqZone);
                    cmd.Parameters.AddWithValue("@grid",          grid          ?? "");
                    cmd.Parameters.AddWithValue("@name",          name          ?? "");
                    cmd.Parameters.AddWithValue("@comment",       comment       ?? "");
                    cmd.Parameters.AddWithValue("@tx_pwr",        txPwr         ?? "");
                    cmd.Parameters.AddWithValue("@operator_call", operatorCall  ?? "");
                    cmd.Parameters.AddWithValue("@station_call",  stationCall   ?? "");
                    cmd.Parameters.AddWithValue("@my_grid",       myGrid        ?? "");
                    cmd.Parameters.AddWithValue("@lotw_qsl_sent", lotwQslSent   ?? "");
                    cmd.Parameters.AddWithValue("@lotw_qsl_rcvd", lotwQslRcvd   ?? "");
                    cmd.Parameters.AddWithValue("@qrz_qsl_sent",  qrzQslSent    ?? "");
                    cmd.Parameters.AddWithValue("@qrz_qsl_rcvd",  qrzQslRcvd   ?? "");
                    cmd.Parameters.AddWithValue("@source",         source        ?? "MANUAL");
                    cmd.Parameters.AddWithValue("@source_qso_id",  sourceQsoId  ?? "");
                    cmd.Parameters.AddWithValue("@imported_at",    DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@dedup_key",      dedupKey);
                    cmd.Parameters.AddWithValue("@continent",      continent     ?? "");
                    cmd.Parameters.AddWithValue("@itu_zone",       ituZone);
                    cmd.Parameters.AddWithValue("@county",         county        ?? "");
                    cmd.Parameters.AddWithValue("@iota",           iota          ?? "");
                    cmd.Parameters.AddWithValue("@sig",            sig           ?? "");
                    cmd.Parameters.AddWithValue("@sig_info",       sigInfo       ?? "");
                    cmd.Parameters.AddWithValue("@my_sig",         mySig         ?? "");
                    cmd.Parameters.AddWithValue("@my_sig_info",    mySigInfo     ?? "");
                    cmd.Parameters.AddWithValue("@darc_dok",       darcDok       ?? "");
                    cmd.Parameters.AddWithValue("@wpx_prefix",     wpxPrefix     ?? "");
                    cmd.Parameters.AddWithValue("@exchange_sent",  exchangeSent  ?? "");
                    cmd.Parameters.AddWithValue("@exchange_rcvd",  exchangeRcvd  ?? "");
                    cmd.ExecuteNonQuery();
                }

                bool isUpdated = false;
                if (existed)
                {
                    using (var check2 = _conn.CreateCommand())
                    {
                        check2.CommandText = $"SELECT {mutableCols} FROM qso WHERE dedup_key=@k;";
                        check2.Parameters.AddWithValue("@k", dedupKey);
                        using (var r = check2.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                var after = new object[r.FieldCount];
                                r.GetValues(after);
                                for (int i = 0; i < before.Length; i++)
                                {
                                    if (!Equals(before[i], after[i])) { isUpdated = true; break; }
                                }
                            }
                        }
                    }
                }

                return (!existed, isUpdated);
            }
        }

        // ── Import log ───────────────────────────────────────────────────────────

        public int LogImportStart(string source)
        {
            lock (_lock)
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT INTO import_log(source, started_at) VALUES(@s,@t);" +
                        "SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@s", source);
                    cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        public void LogImportFinish(int logId, int total, int newCount, int updated, int skipped, string errorText)
        {
            lock (_lock)
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText =
                        "UPDATE import_log SET finished_at=@f, total_qso=@t, new_qso=@n, " +
                        "updated_qso=@u, skipped_qso=@s, error_text=@e WHERE id=@id;";
                    cmd.Parameters.AddWithValue("@f",  DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@t",  total);
                    cmd.Parameters.AddWithValue("@n",  newCount);
                    cmd.Parameters.AddWithValue("@u",  updated);
                    cmd.Parameters.AddWithValue("@s",  skipped);
                    cmd.Parameters.AddWithValue("@e",  errorText ?? "");
                    cmd.Parameters.AddWithValue("@id", logId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<ImportLogEntry> GetImportHistory(int limit = 25)
        {
            lock (_lock)
            {
                var result = new List<ImportLogEntry>();
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT id, source, started_at, total_qso, new_qso, updated_qso, skipped_qso, error_text " +
                        $"FROM import_log ORDER BY id DESC LIMIT {limit};";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            DateTime dt;
                            DateTime.TryParse(r.IsDBNull(2) ? "" : r.GetString(2), out dt);
                            result.Add(new ImportLogEntry
                            {
                                Id         = r.GetInt32(0),
                                Source     = r.IsDBNull(1) ? "" : r.GetString(1),
                                StartedAt  = dt,
                                TotalQso   = r.IsDBNull(3) ? 0  : r.GetInt32(3),
                                NewQso     = r.IsDBNull(4) ? 0  : r.GetInt32(4),
                                UpdatedQso = r.IsDBNull(5) ? 0  : r.GetInt32(5),
                                SkippedQso = r.IsDBNull(6) ? 0  : r.GetInt32(6),
                                ErrorText  = r.IsDBNull(7) ? "" : r.GetString(7),
                            });
                        }
                    }
                }
                return result;
            }
        }

        // ── Outbound upload tracking (QRZ / Club Log logbook upload) ───────────────

        public class PendingUploadQso
        {
            public string Callsign, Band, Mode, QsoDate, TimeOn, TimeOff;
            public long   FreqHz;
            public string RstSent, RstRcvd, Grid, Name, Comment, TxPwr;
            public string OperatorCall, StationCall, MyGrid, DedupKey;
            public string ExchangeSent, ExchangeRcvd;
        }

        // service is "qrz" or "clublog". Returns QSOs never yet uploaded to that
        // service, oldest first, so a batch upload sends them in QSO order.
        public List<PendingUploadQso> GetPendingUploads(string service, int limit = 1000)
        {
            string col = UploadColumn(service);
            lock (_lock)
            {
                var result = new List<PendingUploadQso>();
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText =
                        $"SELECT callsign, band, mode, qso_date, time_on, time_off, freq_hz, " +
                        $"rst_sent, rst_rcvd, grid, name, comment, tx_pwr, operator_call, station_call, my_grid, dedup_key, " +
                        $"exchange_sent, exchange_rcvd " +
                        $"FROM qso WHERE {col} = '' ORDER BY qso_date, time_on LIMIT {limit};";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            result.Add(new PendingUploadQso
                            {
                                Callsign     = r.IsDBNull(0)  ? "" : r.GetString(0),
                                Band         = r.IsDBNull(1)  ? "" : r.GetString(1),
                                Mode         = r.IsDBNull(2)  ? "" : r.GetString(2),
                                QsoDate      = r.IsDBNull(3)  ? "" : r.GetString(3),
                                TimeOn       = r.IsDBNull(4)  ? "" : r.GetString(4),
                                TimeOff      = r.IsDBNull(5)  ? "" : r.GetString(5),
                                FreqHz       = r.IsDBNull(6)  ? 0  : r.GetInt64(6),
                                RstSent      = r.IsDBNull(7)  ? "" : r.GetString(7),
                                RstRcvd      = r.IsDBNull(8)  ? "" : r.GetString(8),
                                Grid         = r.IsDBNull(9)  ? "" : r.GetString(9),
                                Name         = r.IsDBNull(10) ? "" : r.GetString(10),
                                Comment      = r.IsDBNull(11) ? "" : r.GetString(11),
                                TxPwr        = r.IsDBNull(12) ? "" : r.GetString(12),
                                OperatorCall = r.IsDBNull(13) ? "" : r.GetString(13),
                                StationCall  = r.IsDBNull(14) ? "" : r.GetString(14),
                                MyGrid       = r.IsDBNull(15) ? "" : r.GetString(15),
                                DedupKey     = r.IsDBNull(16) ? "" : r.GetString(16),
                                ExchangeSent = r.IsDBNull(17) ? "" : r.GetString(17),
                                ExchangeRcvd = r.IsDBNull(18) ? "" : r.GetString(18),
                            });
                        }
                    }
                }
                return result;
            }
        }

        public void MarkUploaded(string dedupKey, string service, DateTime whenUtc)
        {
            string col = UploadColumn(service);
            lock (_lock)
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = $"UPDATE qso SET {col} = @w WHERE dedup_key = @k;";
                    cmd.Parameters.AddWithValue("@w", whenUtc.ToString("o"));
                    cmd.Parameters.AddWithValue("@k", dedupKey);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static string UploadColumn(string service)
        {
            switch ((service ?? "").ToUpperInvariant())
            {
                case "QRZ":     return "qrz_uploaded_at";
                case "CLUBLOG": return "clublog_uploaded_at";
                default: throw new ArgumentException("Unknown upload service: " + service);
            }
        }

        // ── Scalar helpers ───────────────────────────────────────────────────────

        public int QueryScalar(string sql, params SQLiteParameter[] parms)
        {
            lock (_lock)
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    foreach (var p in parms) cmd.Parameters.Add(p);
                    var r = cmd.ExecuteScalar();
                    return r == null || r == DBNull.Value ? 0 : Convert.ToInt32(r);
                }
            }
        }

        public int TotalQsos(string source = null)
        {
            string w = source != null ? $" WHERE source='{EscSql(source)}'" : "";
            return QueryScalar($"SELECT COUNT(*) FROM qso{w};");
        }

        public int ConfirmedQsos(string source = null)
        {
            string w  = source != null ? $" AND source='{EscSql(source)}'" : "";
            return QueryScalar($"SELECT COUNT(*) FROM qso WHERE (lotw_qsl_rcvd='Y' OR qrz_qsl_rcvd='Y'){w};");
        }

        public int LotwConfirmedQsos() =>
            QueryScalar("SELECT COUNT(*) FROM qso WHERE lotw_qsl_rcvd='Y';");

        public int QrzConfirmedQsos() =>
            QueryScalar("SELECT COUNT(*) FROM qso WHERE qrz_qsl_rcvd='Y';");

        // ── Band / mode / year stats ──────────────────────────────────────────────

        public List<BandStat> GetBandStats(string source = null)
        {
            string w = source != null ? $" AND source='{EscSql(source)}'" : "";
            return QueryBandStats(
                "SELECT band, COUNT(*) as total, " +
                "SUM(CASE WHEN lotw_qsl_rcvd='Y' OR qrz_qsl_rcvd='Y' THEN 1 ELSE 0 END) as confirmed " +
                $"FROM qso WHERE band!='' AND band IS NOT NULL{w} GROUP BY band ORDER BY band;");
        }

        public List<BandStat> GetModeStats(string source = null)
        {
            string w = source != null ? $" AND source='{EscSql(source)}'" : "";
            return QueryBandStats(
                "SELECT mode, COUNT(*) as total, " +
                "SUM(CASE WHEN lotw_qsl_rcvd='Y' OR qrz_qsl_rcvd='Y' THEN 1 ELSE 0 END) as confirmed " +
                $"FROM qso WHERE mode!='' AND mode IS NOT NULL{w} GROUP BY mode ORDER BY total DESC;");
        }

        public List<BandStat> GetYearStats(string source = null)
        {
            string w = source != null ? $" AND source='{EscSql(source)}'" : "";
            return QueryBandStats(
                "SELECT SUBSTR(qso_date,1,4) as yr, COUNT(*) as total, " +
                "SUM(CASE WHEN lotw_qsl_rcvd='Y' OR qrz_qsl_rcvd='Y' THEN 1 ELSE 0 END) as confirmed " +
                $"FROM qso WHERE LENGTH(qso_date)>=4{w} GROUP BY yr ORDER BY yr DESC;");
        }

        private List<BandStat> QueryBandStats(string sql)
        {
            lock (_lock)
            {
                var result = new List<BandStat>();
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            result.Add(new BandStat
                            {
                                Label     = r.IsDBNull(0) ? "?" : r.GetString(0),
                                Total     = r.GetInt32(1),
                                Confirmed = r.GetInt32(2),
                            });
                    }
                }
                return result;
            }
        }

        // ── Award progress ───────────────────────────────────────────────────────

        public (int worked, int confirmed) WasProgress(string band = null)
        {
            string bf = BandFilter(band);
            return (
                QueryScalar($"SELECT COUNT(DISTINCT state) FROM qso WHERE UPPER(TRIM(state)) IN ({WasInList}){bf};"),
                QueryScalar($"SELECT COUNT(DISTINCT state) FROM qso WHERE UPPER(TRIM(state)) IN ({WasInList}) AND (lotw_qsl_rcvd='Y' OR qrz_qsl_rcvd='Y'){bf};")
            );
        }

        public (int worked, int confirmed) DxccProgress(string band = null)
        {
            string bf = BandFilter(band);
            return (
                QueryScalar($"SELECT COUNT(DISTINCT dxcc) FROM qso WHERE dxcc>0{bf};"),
                QueryScalar($"SELECT COUNT(DISTINCT dxcc) FROM qso WHERE dxcc>0 AND (lotw_qsl_rcvd='Y' OR qrz_qsl_rcvd='Y'){bf};")
            );
        }

        public (int worked, int confirmed) WazProgress(string band = null)
        {
            string bf = BandFilter(band);
            return (
                QueryScalar($"SELECT COUNT(DISTINCT cq_zone) FROM qso WHERE cq_zone>0 AND cq_zone<=40{bf};"),
                QueryScalar($"SELECT COUNT(DISTINCT cq_zone) FROM qso WHERE cq_zone>0 AND cq_zone<=40 AND (lotw_qsl_rcvd='Y' OR qrz_qsl_rcvd='Y'){bf};")
            );
        }

        // ── Dashboard ─────────────────────────────────────────────────────────────

        public List<QsoRecord> GetRecentQsos(int limit = 10)
        {
            lock (_lock)
            {
                var result = new List<QsoRecord>();
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT qso_date, time_on, callsign, band, mode, country, lotw_qsl_rcvd, qrz_qsl_rcvd " +
                        $"FROM qso ORDER BY qso_date DESC, time_on DESC LIMIT {limit};";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            result.Add(new QsoRecord
                            {
                                QsoDate     = Str(r, 0),
                                TimeOn      = Str(r, 1),
                                Callsign    = Str(r, 2),
                                Band        = Str(r, 3),
                                Mode        = Str(r, 4),
                                Country     = Str(r, 5),
                                LotwQslRcvd = Str(r, 6),
                                QrzQslRcvd  = Str(r, 7),
                            });
                    }
                }
                return result;
            }
        }

        public Dictionary<string, int> GetSourceCounts()
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT source, COUNT(*) FROM qso GROUP BY source;";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            result[r.IsDBNull(0)?"?":r.GetString(0)] = r.GetInt32(1);
                    }
                }
            }
            return result;
        }

        // ── Search ────────────────────────────────────────────────────────────────

        public List<QsoRecord> SearchByCallsign(string pattern, int limit = 200)
        {
            if (!pattern.Contains("%")) pattern = pattern.ToUpperInvariant() + "%";
            lock (_lock)
            {
                var result = new List<QsoRecord>();
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT qso_date, time_on, callsign, band, mode, state, country, dxcc, " +
                        "lotw_qsl_rcvd, qrz_qsl_rcvd, source " +
                        $"FROM qso WHERE callsign LIKE @p ORDER BY qso_date DESC, time_on DESC LIMIT {limit};";
                    cmd.Parameters.AddWithValue("@p", pattern);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            result.Add(new QsoRecord
                            {
                                QsoDate     = Str(r,  0),
                                TimeOn      = Str(r,  1),
                                Callsign    = Str(r,  2),
                                Band        = Str(r,  3),
                                Mode        = Str(r,  4),
                                State       = Str(r,  5),
                                Country     = Str(r,  6),
                                Dxcc        = r.IsDBNull(7) ? 0 : r.GetInt32(7),
                                LotwQslRcvd = Str(r,  8),
                                QrzQslRcvd  = Str(r,  9),
                                Source      = Str(r, 10),
                            });
                    }
                }
                return result;
            }
        }

        // Returns best-known country name per DXCC entity, derived from QSO records.
        public Dictionary<int, string> GetDxccCountryNames()
        {
            var result = new Dictionary<int, string>();
            lock (_lock)
            {
                using (var cmd = _conn.CreateCommand())
                {
                    // Pick the most-common non-empty country name per DXCC number
                    cmd.CommandText =
                        "SELECT dxcc, country, COUNT(*) as c FROM qso " +
                        "WHERE dxcc>0 AND country!='' AND country IS NOT NULL " +
                        "GROUP BY dxcc, country ORDER BY dxcc, c DESC;";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            int adif = r.GetInt32(0);
                            if (!result.ContainsKey(adif))
                                result[adif] = r.IsDBNull(1) ? "" : r.GetString(1);
                        }
                    }
                }
            }
            return result;
        }

        // ── Transactions (for batch imports) ──────────────────────────────────────

        public SQLiteTransaction BeginTransaction() => _conn.BeginTransaction();

        // ── Helpers ───────────────────────────────────────────────────────────────

        // ── HRC cache (used by Jimmy's tag/filter system) ─────────────────────────

        private static readonly string[] UsStates50 =
        {
            "AK","AL","AR","AZ","CA","CO","CT","DE","FL","GA","HI","IA","ID","IL","IN",
            "KS","KY","LA","MA","MD","ME","MI","MN","MO","MS","MT","NC","ND","NE","NH","NJ",
            "NM","NV","NY","OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VA","VT","WA",
            "WI","WV","WY"
        };

        // Computes the three HRC filter sets used by Jimmy's decode processor.
        // All computation is local — no network access.
        // band: ADIF-style string (e.g. "20m"); null means all-band.
        public void LoadHrcCache(
            out HashSet<string> neededStates,
            out HashSet<int>    unconfirmedDxcc,
            out HashSet<int>    neededZones,
            string band = null)
        {
            string bf = BandFilter(band);
            var confirmedSt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var workedDx    = new HashSet<int>();
            var confirmedDx = new HashSet<int>();
            var confirmedZn = new HashSet<int>();

            lock (_lock)
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText =
                        $"SELECT DISTINCT UPPER(TRIM(state)) FROM qso " +
                        $"WHERE UPPER(TRIM(state)) IN ({WasInList}) " +
                        $"AND (lotw_qsl_rcvd='Y' OR qrz_qsl_rcvd='Y'){bf};";
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) if (!r.IsDBNull(0)) confirmedSt.Add(r.GetString(0));
                }
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT DISTINCT dxcc FROM qso WHERE dxcc>0{bf};";
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) workedDx.Add(r.GetInt32(0));
                }
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText =
                        $"SELECT DISTINCT dxcc FROM qso WHERE dxcc>0 " +
                        $"AND (lotw_qsl_rcvd='Y' OR qrz_qsl_rcvd='Y'){bf};";
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) confirmedDx.Add(r.GetInt32(0));
                }
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText =
                        $"SELECT DISTINCT cq_zone FROM qso WHERE cq_zone>0 AND cq_zone<=40 " +
                        $"AND (lotw_qsl_rcvd='Y' OR qrz_qsl_rcvd='Y'){bf};";
                    using (var r = cmd.ExecuteReader())
                        while (r.Read()) confirmedZn.Add(r.GetInt32(0));
                }
            }

            neededStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var st in UsStates50)
                if (!confirmedSt.Contains(st)) neededStates.Add(st);

            unconfirmedDxcc = new HashSet<int>(workedDx);
            unconfirmedDxcc.ExceptWith(confirmedDx);

            neededZones = new HashSet<int>();
            for (int z = 1; z <= 40; z++)
                if (!confirmedZn.Contains(z)) neededZones.Add(z);
        }

        private void Exec(string sql)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        private static string BandFilter(string band) =>
            !string.IsNullOrEmpty(band) ? $" AND band='{EscSql(band)}'" : "";

        private static string EscSql(string s) => s?.Replace("'", "''") ?? "";

        private static string Str(IDataReader r, int i) =>
            r.IsDBNull(i) ? "" : r.GetString(i);

        public void Dispose()
        {
            try { _conn?.Close(); } catch { }
            try { _conn?.Dispose(); } catch { }
            _conn = null;
        }
    }
}
