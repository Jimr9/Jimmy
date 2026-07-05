using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WSJTX_Controller
{
    // Snapshot of the settings ImportLiveLoggedQso reads. Read at task-execution time (via the
    // Func<LiveUploadCredentials> passed to the orchestrator), not captured at call time --
    // preserves the original code's behavior of using whatever the user has configured by the
    // time the background task actually runs, not whatever was configured when the QSO was logged.
    public class LiveUploadCredentials
    {
        public bool QrzUploadEnabled;
        public bool QrzUploadRealtime;
        public string QrzLogbookApiKey;
        public bool ClubLogUploadEnabled;
        public bool ClubLogUploadRealtime;
        public string ClubLogUploadEmail;
        public string ClubLogUploadPassword;
        public string ClubLogUploadCallsign;
    }

    // Extracted from WsjtxClient.ImportLiveLoggedQso (Phase 2.6 of the modernization plan) --
    // the one genuinely async/cross-thread code path in the app (a background Task.Run that
    // imports a freshly-logged QSO, then optionally uploads it to QRZ/Club Log in real time).
    // No ctrl/Controller/WinForms reference: settings are read live via `credentials`, and the
    // UI-thread refresh (RefreshStillNeedCache/RefreshLogbookWindowIfOpen) is done by the
    // `notifyImported` callback WsjtxClient supplies -- it already owns the ctrl.BeginInvoke
    // marshaling, which stays exactly where it was, just handed in rather than hardcoded here.
    // Automated tests cannot exercise the QRZ/Club Log network calls themselves (no test
    // credentials); this extraction was verified by careful manual review against the original
    // method body, preserving control flow, error handling, and dedup-marking exactly.
    public class LiveQsoUploadOrchestrator
    {
        private readonly Func<LiveUploadCredentials> _credentials;
        private readonly Action _notifyImported;
        private readonly Action<string> _debugLog;

        public LiveQsoUploadOrchestrator(Func<LiveUploadCredentials> credentials, Action notifyImported, Action<string> debugLog)
        {
            _credentials = credentials;
            _notifyImported = notifyImported;
            _debugLog = debugLog;
        }

        public void ImportLiveLoggedQso(string dxCall, Dictionary<string, string> fields, string adifRecord, string dedupKey)
        {
            Task.Run(async () =>
            {
                try
                {
                    using (var db = new LogbookDb())
                    {
                        AdifImporter.Import(db, new[] { fields }, "WSJTX", null);

                        // Keep award tracking current for this new QSO without requiring a
                        // restart: refresh the live-tag "still needed" cache used during
                        // operation, and the Logbook window's Awards/Still Need page if open.
                        _notifyImported();

                        var creds = _credentials();
                        bool needQrz = creds.QrzUploadEnabled && creds.QrzUploadRealtime &&
                                       !string.IsNullOrWhiteSpace(creds.QrzLogbookApiKey);
                        bool needClubLog = creds.ClubLogUploadEnabled && creds.ClubLogUploadRealtime &&
                                       !string.IsNullOrWhiteSpace(creds.ClubLogUploadEmail) &&
                                       !string.IsNullOrWhiteSpace(creds.ClubLogUploadPassword) &&
                                       !string.IsNullOrWhiteSpace(creds.ClubLogUploadCallsign);

                        if (!needQrz && !needClubLog) return;

                        if (needQrz)
                        {
                            var qrzClient = new QrzLogbookClient();
                            bool ok = await qrzClient.InsertAsync(creds.QrzLogbookApiKey, adifRecord).ConfigureAwait(false);
                            if (ok) db.MarkUploaded(dedupKey, "QRZ", DateTime.UtcNow);
                            else _debugLog($"QRZ real-time upload failed for {dxCall}: {qrzClient.LastError}");
                        }

                        if (needClubLog)
                        {
                            var clClient = new ClubLogUploadClient();
                            bool ok = await clClient.RealtimeUploadAsync(
                                creds.ClubLogUploadEmail, creds.ClubLogUploadPassword, creds.ClubLogUploadCallsign, adifRecord).ConfigureAwait(false);
                            if (ok) db.MarkUploaded(dedupKey, "CLUBLOG", DateTime.UtcNow);
                            else _debugLog($"Club Log real-time upload failed for {dxCall}: {clClient.LastError}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _debugLog($"ImportLiveLoggedQso error: {ex.Message}");
                }
            });
        }
    }
}
