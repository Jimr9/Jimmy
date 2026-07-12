using System;

namespace WSJTX_Controller
{
    // Single source of truth for "is a test harness driving this Jimmy instance right now."
    // Reuses the exact same JIMMY_TEST_DB_PATH signal LogbookDb already uses to isolate the
    // local database (see LogbookDb.DbPath) -- one environment variable now protects both.
    //
    // Added 2026-07-12 after a real incident: JIMMY_TEST_DB_PATH alone only isolates which
    // *database file* gets written to. It does nothing to stop the real QRZ/Club Log/LoTW
    // credentials -- which still come from the user's real Jimmy.ini regardless of which
    // database is active -- from being used to make genuine HTTP calls. A full session of
    // replay testing with real-time upload enabled in the real settings genuinely uploaded
    // ~100 fake QSOs to the user's live QRZ Logbook and Club Log accounts. Every method in
    // this codebase that makes an outbound network call to a third-party ham radio service
    // (QRZ, Club Log, LoTW, FCC ULS) must check this guard first and no-op instead.
    internal static class TestModeGuard
    {
        public static bool IsTestMode =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JIMMY_TEST_DB_PATH"));
    }
}
