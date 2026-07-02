using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Text.RegularExpressions;

namespace WSJTX_Controller
{
    public class ImportResult
    {
        public int    Processed { get; set; }
        public int    NewQsos   { get; set; }
        public int    Updated   { get; set; }
        public int    Skipped   { get; set; }
        public string Errors    { get; set; } = "";

        public override string ToString() =>
            $"Processed {Processed}: {NewQsos} new, {Updated} updated, {Skipped} skipped" +
            (string.IsNullOrEmpty(Errors) ? "" : $"; {Errors.Split('\n').Length} errors");
    }

    public static class AdifImporter
    {
        // Band frequency boundaries (MHz).  Used when BAND is absent but FREQ present.
        private static readonly (double lo, double hi, string band)[] FreqBands =
        {
            (0.1357, 0.1378, "2200m"),
            (0.472,  0.479,  "630m"),
            (1.8,    2.0,    "160m"),
            (3.5,    4.0,    "80m"),
            (5.06,   5.45,   "60m"),
            (7.0,    7.3,    "40m"),
            (10.1,   10.15,  "30m"),
            (14.0,   14.35,  "20m"),
            (18.068, 18.168, "17m"),
            (21.0,   21.45,  "15m"),
            (24.89,  24.99,  "12m"),
            (28.0,   29.7,   "10m"),
            (50.0,   54.0,   "6m"),
            (70.0,   70.5,   "4m"),
            (144.0,  148.0,  "2m"),
            (222.0,  225.0,  "1.25m"),
            (420.0,  450.0,  "70cm"),
            (902.0,  928.0,  "33cm"),
            (1240.0, 1300.0, "23cm"),
        };

        // source: "QRZ", "LOTW", or "MANUAL"
        public static ImportResult Import(
            LogbookDb db,
            IEnumerable<Dictionary<string, string>> records,
            string source,
            Action<int> progressCallback = null)
        {
            var result = new ImportResult();
            var errors = new StringBuilder();

            int batchSize = 0;
            SQLiteTransaction tx = db.BeginTransaction();
            try
            {
                foreach (var raw in records)
                {
                    try
                    {
                        var q = Normalize(raw, source);
                        if (q == null) { result.Skipped++; result.Processed++; continue; }

                        var (isNew, isUpdated) = db.Upsert(
                            q.callsign, q.band, q.mode, q.qsoDate, q.timeOn, q.timeOff,
                            q.freqHz, q.rstSent, q.rstRcvd, q.state, q.country,
                            q.dxcc, q.cqZone, q.grid, q.name, q.comment, q.txPwr,
                            q.operatorCall, q.stationCall, q.myGrid,
                            q.lotwQslSent, q.lotwQslRcvd, q.qrzQslSent, q.qrzQslRcvd,
                            source, q.sourceQsoId, q.dedupKey,
                            q.continent, q.ituZone, q.county, q.iota,
                            q.sig, q.sigInfo, q.mySig, q.mySigInfo,
                            q.darcDok, q.wpxPrefix);

                        if (isNew)         result.NewQsos++;
                        else if (isUpdated) result.Updated++;
                        else                result.Skipped++;

                        result.Processed++;

                        batchSize++;
                        if (batchSize >= 500)
                        {
                            tx.Commit();
                            tx.Dispose();
                            tx = db.BeginTransaction();
                            batchSize = 0;
                        }

                        progressCallback?.Invoke(result.Processed);
                    }
                    catch (Exception ex)
                    {
                        result.Processed++;
                        result.Skipped++;
                        if (errors.Length < 2000)
                            errors.AppendLine(ex.Message);
                    }
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
            finally
            {
                tx.Dispose();
            }

            result.Errors = errors.ToString().Trim();
            return result;
        }

        // ── Normalization ─────────────────────────────────────────────────────────

        private sealed class NormalizedQso
        {
            public string callsign, band, mode, qsoDate, timeOn, timeOff;
            public long   freqHz;
            public string rstSent, rstRcvd, state, country, grid, name, comment, txPwr;
            public int    dxcc, cqZone;
            public string operatorCall, stationCall, myGrid;
            public string lotwQslSent, lotwQslRcvd, qrzQslSent, qrzQslRcvd;
            public string sourceQsoId;
            public string dedupKey;
            // Fields used by the Rule Definitions (awards) engine.
            public string continent, county, iota, sig, sigInfo, mySig, mySigInfo, darcDok, wpxPrefix;
            public int    ituZone;
        }

        private static NormalizedQso Normalize(Dictionary<string, string> f, string source)
        {
            string call = GetField(f, "CALL");
            if (string.IsNullOrWhiteSpace(call)) return null;
            call = call.ToUpperInvariant().Trim();

            string band = NormalizeBand(GetField(f, "BAND"), GetField(f, "FREQ"));
            string mode = (GetField(f, "MODE") ?? "").ToUpperInvariant().Trim();

            string qsoDate = NormalizeDate(GetField(f, "QSO_DATE") ?? GetField(f, "QSO_DATE_OFF") ?? "");
            string timeOn  = NormalizeTime(GetField(f, "TIME_ON")  ?? "");
            string timeOff = NormalizeTime(GetField(f, "TIME_OFF") ?? "");

            if (string.IsNullOrEmpty(qsoDate)) return null;

            string dedupKey = BuildDedupKey(call, band, mode, qsoDate, timeOn);

            long freqHz = 0;
            double freqMhz;
            if (double.TryParse(GetField(f, "FREQ"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out freqMhz))
                freqHz = (long)(freqMhz * 1_000_000);

            int dxcc = 0;
            int.TryParse(GetField(f, "DXCC") ?? "", out dxcc);

            int cqZone = 0;
            int.TryParse(GetField(f, "CQZ") ?? GetField(f, "CQ_ZONE") ?? "", out cqZone);

            // QSL field mapping differs by source.
            // LoTW download: QSL_RCVD:Y means confirmed (LOTW_QSL_RCVD is a logging-software field, absent in LoTW's own export).
            // QRZ download:  APP_QRZLOG_STATUS:C means confirmed on QRZ logbook; QSL_RCVD is physical paper cards only.
            string lotwSent, lotwRcvd, qrzSent, qrzRcvd;
            if (source == "LOTW")
            {
                lotwSent = QslVal(GetField(f, "QSL_SENT"));
                lotwRcvd = QslVal(GetField(f, "QSL_RCVD"));
                qrzSent  = "";
                qrzRcvd  = "";
            }
            else if (source == "QRZ")
            {
                lotwSent = QslVal(GetField(f, "LOTW_QSL_SENT"));
                lotwRcvd = QslVal(GetField(f, "LOTW_QSL_RCVD"));
                string appStatus = GetField(f, "APP_QRZLOG_STATUS");
                qrzRcvd  = string.Equals(appStatus, "C", StringComparison.OrdinalIgnoreCase) ? "Y" : "";
                qrzSent  = QslVal(GetField(f, "QSL_SENT"));
            }
            else
            {
                lotwSent = QslVal(GetField(f, "LOTW_QSL_SENT"));
                string lotwRcvdDirect = QslVal(GetField(f, "LOTW_QSL_RCVD"));
                lotwRcvd = lotwRcvdDirect.Length > 0 ? lotwRcvdDirect : QslVal(GetField(f, "QSL_RCVD"));
                string appStatus = GetField(f, "APP_QRZLOG_STATUS");
                qrzRcvd  = string.Equals(appStatus, "C", StringComparison.OrdinalIgnoreCase) ? "Y" : "";
                qrzSent  = QslVal(GetField(f, "QSL_SENT"));
            }

            string state = (GetField(f, "STATE") ?? "").ToUpperInvariant().Trim();
            if (state.Length > 2) state = state.Substring(0, 2);

            int ituZone = 0;
            int.TryParse(GetField(f, "ITUZ") ?? GetField(f, "ITU_ZONE") ?? "", out ituZone);

            return new NormalizedQso
            {
                callsign     = call,
                band         = band,
                mode         = mode,
                qsoDate      = qsoDate,
                timeOn       = timeOn,
                timeOff      = timeOff,
                freqHz       = freqHz,
                rstSent      = GetField(f, "RST_SENT") ?? "",
                rstRcvd      = GetField(f, "RST_RCVD") ?? "",
                state        = state,
                country      = GetField(f, "COUNTRY") ?? "",
                dxcc         = dxcc,
                cqZone       = cqZone,
                grid         = (GetField(f, "GRIDSQUARE") ?? GetField(f, "GRID") ?? "").ToUpperInvariant(),
                name         = GetField(f, "NAME") ?? "",
                comment      = GetField(f, "COMMENT") ?? GetField(f, "NOTES") ?? "",
                txPwr        = GetField(f, "TX_PWR") ?? "",
                operatorCall = (GetField(f, "OPERATOR") ?? "").ToUpperInvariant(),
                stationCall  = (GetField(f, "STATION_CALLSIGN") ?? GetField(f, "MY_CALL") ?? "").ToUpperInvariant(),
                myGrid       = (GetField(f, "MY_GRIDSQUARE") ?? GetField(f, "MY_GRID") ?? "").ToUpperInvariant(),
                lotwQslSent  = lotwSent,
                lotwQslRcvd  = lotwRcvd,
                qrzQslSent   = qrzSent,
                qrzQslRcvd   = qrzRcvd,
                sourceQsoId  = GetField(f, "APP_QRZLOG_QSLDATE") ?? "",
                dedupKey     = dedupKey,
                continent    = (GetField(f, "CONT") ?? "").ToUpperInvariant(),
                ituZone      = ituZone,
                county       = (GetField(f, "CNTY") ?? "").ToUpperInvariant(),
                iota         = (GetField(f, "IOTA") ?? "").ToUpperInvariant(),
                sig          = (GetField(f, "SIG") ?? "").ToUpperInvariant(),
                sigInfo      = (GetField(f, "SIG_INFO") ?? "").ToUpperInvariant(),
                mySig        = (GetField(f, "MY_SIG") ?? "").ToUpperInvariant(),
                mySigInfo    = (GetField(f, "MY_SIG_INFO") ?? "").ToUpperInvariant(),
                darcDok      = (GetField(f, "DARC_DOK") ?? "").ToUpperInvariant(),
                wpxPrefix    = (GetField(f, "PFX") ?? "").ToUpperInvariant(),
            };
        }

        public static string BuildDedupKey(string call, string band, string mode, string qsoDate, string timeOn)
        {
            string t4 = timeOn != null && timeOn.Length >= 4 ? timeOn.Substring(0, 4) : timeOn ?? "";
            return $"{call.ToUpperInvariant()}|{(band ?? "").ToLowerInvariant()}|{(mode ?? "").ToUpperInvariant()}|{qsoDate}|{t4}";
        }

        private static string NormalizeBand(string band, string freqStr)
        {
            if (!string.IsNullOrWhiteSpace(band))
                return band.ToLowerInvariant().Trim();

            if (!string.IsNullOrWhiteSpace(freqStr))
            {
                double mhz;
                if (double.TryParse(freqStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out mhz))
                {
                    foreach (var (lo, hi, b) in FreqBands)
                        if (mhz >= lo && mhz <= hi) return b;
                }
            }
            return "";
        }

        private static string NormalizeDate(string d)
        {
            if (string.IsNullOrWhiteSpace(d)) return "";
            d = d.Trim().Replace("-", "").Replace("/", "");
            return d.Length >= 8 ? d.Substring(0, 8) : "";
        }

        private static string NormalizeTime(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return "";
            t = t.Trim().Replace(":", "");
            return t.Length >= 4 ? t.Substring(0, 4) : t;
        }

        private static string QslVal(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "";
            v = v.Trim().ToUpperInvariant();
            return (v == "Y" || v == "N" || v == "R" || v == "Q" || v == "I") ? v : "";
        }

        private static string GetField(Dictionary<string, string> f, string key)
        {
            string v;
            return f.TryGetValue(key, out v) ? (v ?? "").Trim() : null;
        }
    }
}
