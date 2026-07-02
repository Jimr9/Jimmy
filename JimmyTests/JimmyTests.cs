using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WsjtxUdpLib.Messages.Out;
using WSJTX_Controller;

// Unit tests for Jimmy's WSJT-X message parser.
//
// Tests WsjtxMessage static classifier methods and the AP-suffix strip logic
// that runs inside DecodeMessage.Parse() / EnqueueDecodeMessage.Parse().
// No UDP, no network, no WSJT-X or Jimmy process needed.
//
// Run via test.bat (builds Jimmy.exe first, then builds and runs this).
// In all examples, MY_CALL = "KB0UZT".
// FT8 message format: [DESTINATION] [SOURCE] [payload]
//   e.g. "KB0UZT K4YT EM63" means K4YT is calling KB0UZT.

static class JimmyTests
{
    const string MY_CALL = "KB0UZT";
    const string THEIR_CALL = "K4YT";

    static int passed;
    static int failed;

    static void Check(string label, bool actual, bool expected)
    {
        if (actual == expected)
        {
            Console.WriteLine($"  PASS  {label}");
            passed++;
        }
        else
        {
            Console.WriteLine($"  FAIL  {label}: expected {expected}, got {actual}");
            failed++;
        }
    }

    static void CheckStr(string label, string actual, string expected)
    {
        bool ok = (actual == expected);
        if (ok)
        {
            Console.WriteLine($"  PASS  {label}");
            passed++;
        }
        else
        {
            string a = actual == null ? "<null>" : $"'{actual}'";
            string e = expected == null ? "<null>" : $"'{expected}'";
            Console.WriteLine($"  FAIL  {label}: expected {e}, got {a}");
            failed++;
        }
    }

    // Replicates the AP-suffix stripping from DecodeMessage.Parse() and
    // EnqueueDecodeMessage.Parse() — tested here independently so failures
    // are caught before checking downstream classifiers.
    static string StripApSuffix(string msg)
    {
        if (msg == null) return null;
        // Old WSJT-X 2.x AP format: " ?"
        int idx = msg.IndexOf(" ?");
        if (idx != -1)
            msg = msg.Substring(0, idx).TrimEnd();
        // WSJT-X 3.0 AP format: trailing " a<digits>" e.g. " a35"
        int i = msg.Length - 1;
        while (i >= 0 && char.IsDigit(msg[i])) i--;
        if (i < msg.Length - 1 && i >= 1 && msg[i] == 'a' && msg[i - 1] == ' ')
            msg = msg.Substring(0, i - 1).TrimEnd();
        return msg;
    }

    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--verify-clublog")
        {
            VerifyClubLogEquivalence();
            return;
        }

        Console.WriteLine("=== Jimmy Parser Unit Tests ===");
        Console.WriteLine($"  WsjtxMessage static classifiers + AP strip logic");
        Console.WriteLine($"  myCall in examples = {MY_CALL}");
        Console.WriteLine();

        ApStripTests();
        ReportTests();
        FinalAckTests();
        CqTests();
        ContestTests();
        ToFromCallTests();
        ReplyTests();
        InvalidTypeTests();
        ApChainTests();
        SlashCallNoCountryTests();
        FoxHoundTests();
        HrcEnumTests();
        HrcCacheTests();
        RuleUniverseBuiltInTests();
        RuleUniverseClubLogTests();

        Console.WriteLine();
        Console.WriteLine($"=== {passed} passed, {failed} failed ===");
        if (failed > 0)
        {
            Console.WriteLine("SOME TESTS FAILED");
            Environment.Exit(1);
        }
        else
        {
            Console.WriteLine("ALL TESTS PASSED");
        }
    }

    static void ApStripTests()
    {
        Console.WriteLine("── AP Suffix Stripping ──");
        CheckStr("no suffix: unchanged",
            StripApSuffix($"{MY_CALL} {THEIR_CALL} -05"),
                          $"{MY_CALL} {THEIR_CALL} -05");
        CheckStr("a35 stripped from report",
            StripApSuffix($"{MY_CALL} {THEIR_CALL} -05 a35"),
                          $"{MY_CALL} {THEIR_CALL} -05");
        CheckStr("a1 stripped from CQ+grid",
            StripApSuffix($"CQ {THEIR_CALL} EM63 a1"),
                          $"CQ {THEIR_CALL} EM63");
        CheckStr("a35 stripped from grid reply",
            StripApSuffix($"{MY_CALL} {THEIR_CALL} EM63 a35"),
                          $"{MY_CALL} {THEIR_CALL} EM63");
        CheckStr("a35 stripped from FD exchange",
            StripApSuffix($"{MY_CALL} {THEIR_CALL} 2A MO a35"),
                          $"{MY_CALL} {THEIR_CALL} 2A MO");
        CheckStr("old-format ' ?' stripped",
            StripApSuffix($"{MY_CALL} {THEIR_CALL} +00  ? a3"),
                          $"{MY_CALL} {THEIR_CALL} +00");
        // Grid EM63 ends in digits but the char before the trailing digits is 'M', not 'a'
        CheckStr("grid EM63: digit-suffix guard prevents strip",
            StripApSuffix($"CQ {THEIR_CALL} EM63"),
                          $"CQ {THEIR_CALL} EM63");
        CheckStr("73 message: unchanged",
            StripApSuffix($"{MY_CALL} {THEIR_CALL} 73"),
                          $"{MY_CALL} {THEIR_CALL} 73");
        CheckStr("empty string: unchanged",
            StripApSuffix(""), "");
        // Short AP suffix " a2" (1-digit): same stripping rule
        CheckStr("a2 stripped from report",
            StripApSuffix($"{MY_CALL} {THEIR_CALL} -04 a2"),
                          $"{MY_CALL} {THEIR_CALL} -04");
        // Old-format long-space hybrid without '?': e.g. WSJT-X 3.0-rc1 early builds
        // "KB0UZT K4YT -04                      a2" — many spaces, no '?', but ' a2' at end
        CheckStr("long-space a2 (no '?') stripped",
            StripApSuffix($"{MY_CALL} {THEIR_CALL} -04                      a2"),
                          $"{MY_CALL} {THEIR_CALL} -04");
    }

    static void ReportTests()
    {
        Console.WriteLine("\n── Signal Reports ──");
        Check("IsReport: negative dB",       WsjtxMessage.IsReport($"{MY_CALL} {THEIR_CALL} -05"), true);
        Check("IsReport: positive dB",       WsjtxMessage.IsReport($"{MY_CALL} {THEIR_CALL} +05"), true);
        Check("IsReport: -12",               WsjtxMessage.IsReport($"{MY_CALL} {THEIR_CALL} -12"), true);
        Check("IsReport: R-05 is NOT",       WsjtxMessage.IsReport($"{MY_CALL} {THEIR_CALL} R-05"), false);
        Check("IsReport: grid is NOT",       WsjtxMessage.IsReport($"{MY_CALL} {THEIR_CALL} EM63"), false);
        Check("IsReport: 73 is NOT",         WsjtxMessage.IsReport($"{MY_CALL} {THEIR_CALL} 73"), false);

        Check("IsRogerReport: R-05",         WsjtxMessage.IsRogerReport($"{MY_CALL} {THEIR_CALL} R-05"), true);
        Check("IsRogerReport: R+12",         WsjtxMessage.IsRogerReport($"{MY_CALL} {THEIR_CALL} R+12"), true);
        Check("IsRogerReport: -05 is NOT",   WsjtxMessage.IsRogerReport($"{MY_CALL} {THEIR_CALL} -05"), false);
    }

    static void FinalAckTests()
    {
        Console.WriteLine("\n── 73 / RR73 / RRR ──");
        Check("Is73: 73",                       WsjtxMessage.Is73($"{MY_CALL} {THEIR_CALL} 73"), true);
        Check("Is73: RR73 is NOT Is73",         WsjtxMessage.Is73($"{MY_CALL} {THEIR_CALL} RR73"), false);
        Check("IsRR73: RR73",                   WsjtxMessage.IsRR73($"{MY_CALL} {THEIR_CALL} RR73"), true);
        Check("IsRR73: 73 is NOT IsRR73",       WsjtxMessage.IsRR73($"{MY_CALL} {THEIR_CALL} 73"), false);
        Check("Is73orRR73: 73",                 WsjtxMessage.Is73orRR73($"{MY_CALL} {THEIR_CALL} 73"), true);
        Check("Is73orRR73: RR73",               WsjtxMessage.Is73orRR73($"{MY_CALL} {THEIR_CALL} RR73"), true);
        Check("Is73orRR73: -05 is NOT",         WsjtxMessage.Is73orRR73($"{MY_CALL} {THEIR_CALL} -05"), false);
        Check("IsRogers: RRR",                  WsjtxMessage.IsRogers($"{MY_CALL} {THEIR_CALL} RRR"), true);
        Check("IsRogers: 73 is NOT",            WsjtxMessage.IsRogers($"{MY_CALL} {THEIR_CALL} 73"), false);
        Check("IsRogers: RR73 is NOT",          WsjtxMessage.IsRogers($"{MY_CALL} {THEIR_CALL} RR73"), false);
    }

    static void CqTests()
    {
        Console.WriteLine("\n── CQ Types ──");
        Check("IsCQ: plain CQ with grid",    WsjtxMessage.IsCQ($"CQ {THEIR_CALL} EM63"), true);
        Check("IsCQ: CQ no grid",            WsjtxMessage.IsCQ($"CQ {THEIR_CALL}"), true);
        Check("IsCQ: directed POTA",         WsjtxMessage.IsCQ($"CQ POTA {THEIR_CALL}"), true);
        Check("IsCQ: directed SOTA",         WsjtxMessage.IsCQ($"CQ SOTA {THEIR_CALL}"), true);
        Check("IsCQ: directed DX with grid", WsjtxMessage.IsCQ($"CQ DX {THEIR_CALL} EM63"), true);
        Check("IsCQ: directed NA",           WsjtxMessage.IsCQ($"CQ NA {THEIR_CALL}"), true);
        Check("IsCQ: non-CQ is NOT",         WsjtxMessage.IsCQ($"{MY_CALL} {THEIR_CALL} -05"), false);

        Check("IsPota: POTA CQ",                  WsjtxMessage.IsPota($"CQ POTA {THEIR_CALL}"), true);
        Check("IsPota: plain CQ is NOT POTA",     WsjtxMessage.IsPota($"CQ {THEIR_CALL} EM63"), false);
        Check("IsSota: SOTA CQ",                  WsjtxMessage.IsSota($"CQ SOTA {THEIR_CALL}"), true);
        Check("IsSota: plain CQ is NOT SOTA",     WsjtxMessage.IsSota($"CQ {THEIR_CALL} EM63"), false);

        CheckStr("DirectedTo: POTA",   WsjtxMessage.DirectedTo($"CQ POTA {THEIR_CALL}"), "POTA");
        CheckStr("DirectedTo: SOTA",   WsjtxMessage.DirectedTo($"CQ SOTA {THEIR_CALL}"), "SOTA");
        CheckStr("DirectedTo: DX",     WsjtxMessage.DirectedTo($"CQ DX {THEIR_CALL} EM63"), "DX");
        CheckStr("DirectedTo: NA",     WsjtxMessage.DirectedTo($"CQ NA {THEIR_CALL}"), "NA");
        CheckStr("DirectedTo: plain CQ → null",
                                       WsjtxMessage.DirectedTo($"CQ {THEIR_CALL} EM63"), null);
    }

    static void ContestTests()
    {
        Console.WriteLine("\n── Contest / Field Day ──");
        Check("IsContest: FD 2A MO to me",       WsjtxMessage.IsContest($"{MY_CALL} {THEIR_CALL} 2A MO"), true);
        Check("IsContest: FD R 2A MO to me",     WsjtxMessage.IsContest($"{MY_CALL} {THEIR_CALL} R 2A MO"), true);
        Check("IsContest: FD 2A MO to other",    WsjtxMessage.IsContest($"{THEIR_CALL} K9AVT 559 TX"), true);
        Check("IsContest: 559 TX",               WsjtxMessage.IsContest($"{MY_CALL} {THEIR_CALL} 559 TX"), true);
        Check("IsContest: R 559 TX",             WsjtxMessage.IsContest($"{MY_CALL} {THEIR_CALL} R 559 TX"), true);
        Check("IsContest: 559 0021",             WsjtxMessage.IsContest($"{MY_CALL} {THEIR_CALL} 559 0021"), true);
        Check("IsContest: CQ RU",                WsjtxMessage.IsContest($"CQ RU {THEIR_CALL}"), true);
        Check("IsContest: CQ TEST",              WsjtxMessage.IsContest($"CQ TEST {THEIR_CALL}"), true);
        Check("IsContest: plain report is NOT",  WsjtxMessage.IsContest($"{MY_CALL} {THEIR_CALL} -05"), false);
        Check("IsContest: 73 is NOT",            WsjtxMessage.IsContest($"{MY_CALL} {THEIR_CALL} 73"), false);
        Check("IsContest: CQ plain is NOT",      WsjtxMessage.IsContest($"CQ {THEIR_CALL} EM63"), false);
    }

    static void ToFromCallTests()
    {
        Console.WriteLine("\n── ToCall / DeCall ──");
        // FT8 format: [DESTINATION] [SOURCE] [payload]
        // "KB0UZT K4YT EM63" = K4YT calling KB0UZT
        CheckStr("ToCall: station calling me",   WsjtxMessage.ToCall($"{MY_CALL} {THEIR_CALL} EM63"), MY_CALL);
        CheckStr("DeCall: station calling me",   WsjtxMessage.DeCall($"{MY_CALL} {THEIR_CALL} EM63"), THEIR_CALL);
        CheckStr("ToCall: CQ",                   WsjtxMessage.ToCall($"CQ {THEIR_CALL} EM63"), "CQ");
        CheckStr("DeCall: CQ",                   WsjtxMessage.DeCall($"CQ {THEIR_CALL} EM63"), THEIR_CALL);
        CheckStr("ToCall: directed CQ POTA",     WsjtxMessage.ToCall($"CQ POTA {THEIR_CALL}"), "CQ");
        CheckStr("DeCall: directed CQ POTA",     WsjtxMessage.DeCall($"CQ POTA {THEIR_CALL}"), THEIR_CALL);
        CheckStr("ToCall: contest to me",        WsjtxMessage.ToCall($"{MY_CALL} {THEIR_CALL} 2A MO"), MY_CALL);
        CheckStr("ToCall: contest to other",     WsjtxMessage.ToCall($"{THEIR_CALL} K9AVT 559 TX"), THEIR_CALL);
    }

    static void ReplyTests()
    {
        Console.WriteLine("\n── IsReply / IsShortReply ──");
        Check("IsReply: grid",                   WsjtxMessage.IsReply($"{MY_CALL} {THEIR_CALL} EM63"), true);
        Check("IsReply: report is NOT",          WsjtxMessage.IsReply($"{MY_CALL} {THEIR_CALL} -05"), false);
        Check("IsReply: CQ is NOT",              WsjtxMessage.IsReply($"CQ {THEIR_CALL} EM63"), false);
        Check("IsShortReply: 2 words",           WsjtxMessage.IsShortReply($"{MY_CALL} {THEIR_CALL}"), true);
        Check("IsShortReply: 3 words is NOT",    WsjtxMessage.IsShortReply($"{MY_CALL} {THEIR_CALL} EM63"), false);
        Check("IsShortReply: CQ is NOT",         WsjtxMessage.IsShortReply($"CQ {THEIR_CALL}"), false);
    }

    static void InvalidTypeTests()
    {
        Console.WriteLine("\n── IsInvalidType ──");
        Check("IsInvalidType: report is valid",      WsjtxMessage.IsInvalidType($"{MY_CALL} {THEIR_CALL} -05"), false);
        Check("IsInvalidType: grid is valid",        WsjtxMessage.IsInvalidType($"{MY_CALL} {THEIR_CALL} EM63"), false);
        Check("IsInvalidType: 73 is valid",          WsjtxMessage.IsInvalidType($"{MY_CALL} {THEIR_CALL} 73"), false);
        Check("IsInvalidType: CQ is valid",          WsjtxMessage.IsInvalidType($"CQ {THEIR_CALL} EM63"), false);
        Check("IsInvalidType: garbage IS invalid",   WsjtxMessage.IsInvalidType($"{MY_CALL} {THEIR_CALL} GARBAGE"), true);
        // Un-stripped AP suffix makes the type unrecognizable
        Check("IsInvalidType: a35 before strip IS invalid",
              WsjtxMessage.IsInvalidType($"{MY_CALL} {THEIR_CALL} -05 a35"), true);
        // After stripping, classifier correctly recognizes it
        Check("IsInvalidType: a35 after strip is valid",
              WsjtxMessage.IsInvalidType(StripApSuffix($"{MY_CALL} {THEIR_CALL} -05 a35")), false);
    }

    // Regression: slash callsigns with no country must parse correctly.
    // Covers the bug where AddSelectedCall hard-rejected calls with Country=="".
    static void SlashCallNoCountryTests()
    {
        Console.WriteLine("\n── Slash Callsign / Unknown Country ──");
        // Parser must recognise "CQ W5C/H" as a valid CQ from callsign W5C/H
        Check("IsCQ: CQ W5C/H",            WsjtxMessage.IsCQ("CQ W5C/H"), true);
        CheckStr("DeCall: CQ W5C/H",       WsjtxMessage.DeCall("CQ W5C/H"), "W5C/H");
        Check("IsInvalidType: CQ W5C/H",   WsjtxMessage.IsInvalidType("CQ W5C/H"), false);
        // WsjtxCountry must return "" for null or empty — never throw
        CheckStr("WsjtxCountry: null → empty",   EnqueueDecodeMessage.WsjtxCountry(null), "");
        CheckStr("WsjtxCountry: empty → empty",  EnqueueDecodeMessage.WsjtxCountry(""), "");
    }

    // IsFoxHound() is a suffix heuristic only — /H may be a Hound callsign OR a
    // legitimate portable suffix. SpecialOperationMode in StatusMessage is the
    // authoritative source. These tests verify the heuristic rule, not blocking.
    static void FoxHoundTests()
    {
        Console.WriteLine("\n── Possible F/H Detection (suffix heuristic, not authoritative) ──");
        Check("Possible F/H: CQ from /H call",          WsjtxMessage.IsFoxHound("CQ W5C/H"),                     true);
        Check("Possible F/H: CQ /H with grid",           WsjtxMessage.IsFoxHound("CQ W5C/H EM63"),                true);
        Check("Possible F/H: /H report to me",           WsjtxMessage.IsFoxHound($"{MY_CALL} W5C/H -03"),         true);
        Check("Possible F/H: /H 73 to me",               WsjtxMessage.IsFoxHound($"{MY_CALL} W5C/H 73"),          true);
        Check("Possible F/H: /H RR73 to me",             WsjtxMessage.IsFoxHound($"{MY_CALL} W5C/H RR73"),        true);
        Check("Possible F/H: to-call is /H",             WsjtxMessage.IsFoxHound($"W5C/H {MY_CALL} RR73"),        true);
        // Normal FT8 must NOT be flagged as possible F/H
        Check("Possible F/H: normal CQ is NOT",          WsjtxMessage.IsFoxHound($"CQ {THEIR_CALL} EM63"),        false);
        Check("Possible F/H: normal report is NOT",      WsjtxMessage.IsFoxHound($"{MY_CALL} {THEIR_CALL} -03"),  false);
        Check("Possible F/H: normal 73 is NOT",          WsjtxMessage.IsFoxHound($"{MY_CALL} {THEIR_CALL} 73"),   false);
        // Other slash-suffix portable calls must NOT be flagged as possible F/H
        Check("Possible F/H: /P call is NOT",            WsjtxMessage.IsFoxHound($"CQ W5C/P EM63"),               false);
        Check("Possible F/H: /M call is NOT",            WsjtxMessage.IsFoxHound($"CQ W5C/M"),                    false);
        // Safety: null and empty input
        Check("Possible F/H: null is NOT",               WsjtxMessage.IsFoxHound(null),                           false);
        Check("Possible F/H: empty is NOT",              WsjtxMessage.IsFoxHound(""),                             false);
    }

    // ── HRC filter enum values ────────────────────────────────────────────────
    // Verify the three new CallCategory values have the expected integer assignments.
    // If any of these fail, DeriveCategory / AddSelectedCall routing is broken.
    static void HrcEnumTests()
    {
        Console.WriteLine("\n── HRC CallCategory Enum Values ──");
        // Existing values must be unchanged — regression guard
        Check("DEFAULT == 0",             (int)WsjtxClient.CallCategory.DEFAULT             == 0,  true);
        Check("ALWAYS_WANTED == 8",       (int)WsjtxClient.CallCategory.ALWAYS_WANTED       == 8,  true);
        // New HRC values
        Check("WAS_NEEDED == 9",          (int)WsjtxClient.CallCategory.WAS_NEEDED          == 9,  true);
        Check("DXCC_UNCONFIRMED == 10",   (int)WsjtxClient.CallCategory.DXCC_UNCONFIRMED    == 10, true);
        Check("ZONE_NEEDED == 11",        (int)WsjtxClient.CallCategory.ZONE_NEEDED         == 11, true);
    }

    // ── HRC cache SQL logic ───────────────────────────────────────────────────
    // Creates a throwaway SQLite database, inserts known QSOs, calls LoadHrcCache(),
    // and verifies the three output HashSets are computed correctly.
    // No network access, no WSJT-X, no real HRC data path involved.
    static void HrcCacheTests()
    {
        Console.WriteLine("\n── HRC Cache (LoadHrcCache SQL logic) ──");
        string tmpDb = Path.Combine(Path.GetTempPath(),
            "JimmyTest_HRC_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var db = new LogbookDb(tmpDb))
            {
                // TX confirmed via LoTW → TX must NOT be in neededStates
                InsertQso(db, "W5TX",   "TX", dxcc: 100, zone: 4, lotwRcvd: "Y");
                // CA worked but unconfirmed → CA MUST be in neededStates
                InsertQso(db, "W6CA",   "CA", dxcc: 100, zone: 3);
                // WY: never worked at all → WY MUST be in neededStates (no QSO inserted)

                // DXCC 100 has a confirmed QSO (W5TX) → 100 must NOT be in unconfirmedDxcc
                // DXCC 200 worked, never confirmed → 200 MUST be in unconfirmedDxcc
                InsertQso(db, "OE1TST", "  ", dxcc: 200, zone: 15);
                // DXCC 300 confirmed → 300 must NOT be in unconfirmedDxcc
                InsertQso(db, "VK2TST", "  ", dxcc: 300, zone: 29, lotwRcvd: "Y");

                // Zone 3 confirmed (VE3TST) → zone 3 must NOT be in neededZones
                InsertQso(db, "VE3TST", "ON", dxcc: 400, zone: 3,  lotwRcvd: "Y");
                // Zone 5 worked but unconfirmed → zone 5 MUST be in neededZones
                InsertQso(db, "W0TST",  "CO", dxcc: 100, zone: 5);
                // Zone 20: never worked → zone 20 MUST be in neededZones

                HashSet<string> neededStates;
                HashSet<int>    unconfirmedDxcc;
                HashSet<int>    neededZones;
                db.LoadHrcCache(out neededStates, out unconfirmedDxcc, out neededZones);

                // ── States ──────────────────────────────────────────────────
                Check("neededStates: TX confirmed → NOT in set",   neededStates.Contains("TX"), false);
                Check("neededStates: CA unconfirmed → in set",     neededStates.Contains("CA"), true);
                Check("neededStates: WY (no QSO) → in set",        neededStates.Contains("WY"), true);
                Check("neededStates: count ≤ 50",                  neededStates.Count <= 50,    true);
                // Only TX was confirmed, so 49 states should be needed
                Check("neededStates: count == 49",                 neededStates.Count == 49,    true);
                // DC must never appear — it is not a state
                Check("neededStates: DC never present",            neededStates.Contains("DC"), false);

                // ── DXCC unconfirmed ─────────────────────────────────────────
                // DXCC 100 has a confirmed QSO → NOT unconfirmed
                Check("unconfirmedDxcc: DXCC 100 has confirmed → NOT in set",
                      unconfirmedDxcc.Contains(100), false);
                // DXCC 200: worked, no confirmation → IS unconfirmed
                Check("unconfirmedDxcc: DXCC 200 worked/unconfirmed → in set",
                      unconfirmedDxcc.Contains(200), true);
                // DXCC 300: confirmed → NOT unconfirmed
                Check("unconfirmedDxcc: DXCC 300 confirmed → NOT in set",
                      unconfirmedDxcc.Contains(300), false);

                // ── Zones ────────────────────────────────────────────────────
                // Zone 3: VE3TST confirmed → NOT needed
                Check("neededZones: zone 3 confirmed (VE3TST) → NOT in set", neededZones.Contains(3),  false);
                // Zone 4: W5TX confirmed → NOT needed (W5TX has zone=4, lotwRcvd='Y')
                Check("neededZones: zone 4 confirmed (W5TX) → NOT in set",   neededZones.Contains(4),  false);
                Check("neededZones: zone 5 unconfirmed → in set",             neededZones.Contains(5),  true);
                Check("neededZones: zone 20 (no QSO) → in set",              neededZones.Contains(20), true);
                // Zone 29: VK2TST confirmed → NOT needed
                Check("neededZones: zone 29 confirmed (VK2TST) → NOT in set", neededZones.Contains(29), false);
                Check("neededZones: count ≤ 40",                               neededZones.Count <= 40,  true);
                // Zones 3, 4, and 29 confirmed → 40 - 3 = 37 zones needed
                Check("neededZones: count == 37",                              neededZones.Count == 37,  true);
                // Zone 41 must never be added — only zones 1-40 are valid
                Check("neededZones: zone 41 never present",                   neededZones.Contains(41), false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL  HrcCacheTests threw: {ex.GetType().Name}: {ex.Message}");
            failed++;
        }
        finally
        {
            try { File.Delete(tmpDb); } catch { }
        }
    }

    // Insert a minimal QSO record into a test LogbookDb.
    // Each callsign produces a unique dedup key — no counter needed.
    static void InsertQso(LogbookDb db, string call, string state,
        int dxcc, int zone, string lotwRcvd = "")
    {
        string key = AdifImporter.BuildDedupKey(call, "20m", "FT8", "20241201", "1200");
        // Parameter order: ..., lotwQslSent, lotwQslRcvd, qrzQslSent, qrzQslRcvd, ...
        db.Upsert(call, "20m", "FT8", "20241201", "1200", "1215",
            14_074_000, "-10", "-05", state, "Test", dxcc, zone,
            "", "", "", "", "", "", "",
            "", lotwRcvd, "", "",
            "MANUAL", "", key,
            "", 0, "", "", "", "", "", "", "", "");
    }

    // Verify that each AP-suffixed message, once stripped, classifies correctly.
    //
    // IsInvalidType does NOT cover contest/FD exchanges — they are handled by a
    // separate isContest branch in ProcessDecodeMsg before IsInvalidType is
    // checked.  So IsInvalidType("KB0UZT K4YT 2A MO") == true is intentional.
    static void ApChainTests()
    {
        Console.WriteLine("\n── AP Suffix: strip then classify ──");

        // Cases 0-2: non-contest messages — IsInvalidType=false after strip
        string[] nonContest = {
            $"{MY_CALL} {THEIR_CALL} -05 a35",   // signal report
            $"CQ {THEIR_CALL} EM63 a1",           // plain CQ
            $"{MY_CALL} {THEIR_CALL} EM63 a35",  // grid reply
        };
        bool[] expectReport = { true,  false, false };
        bool[] expectCq     = { false, true,  false };
        bool[] expectReply  = { false, false, true  };

        for (int n = 0; n < nonContest.Length; n++)
        {
            string s = StripApSuffix(nonContest[n]);
            Check($"case {n}: IsInvalidType=false after strip",  WsjtxMessage.IsInvalidType(s), false);
            Check($"case {n}: IsContest=false after strip",      WsjtxMessage.IsContest(s),     false);
            Check($"case {n}: IsReport",   WsjtxMessage.IsReport(s), expectReport[n]);
            Check($"case {n}: IsCQ",       WsjtxMessage.IsCQ(s),     expectCq[n]);
            Check($"case {n}: IsReply",    WsjtxMessage.IsReply(s),   expectReply[n]);
        }

        // Regression: short AP suffix " a2" on a report must not survive as contest tokens.
        // Scenario: WSJT-X sends "KB0UZT K4YT -04                      a2" (old RC format).
        // After strip → "KB0UZT K4YT -04".  IsContest must be False; IsReport must be True.
        string f4dwb = StripApSuffix($"{MY_CALL} THEIR -04                      a2");
        CheckStr("F4DWB-style: stripped correctly",   f4dwb, $"{MY_CALL} THEIR -04");
        Check("F4DWB-style: IsContest=False after strip", WsjtxMessage.IsContest(f4dwb), false);
        Check("F4DWB-style: IsReport=True after strip",   WsjtxMessage.IsReport(f4dwb),  true);

        // Case 3: FD/contest exchange — IsContest=true; IsInvalidType=true by design
        // (contest messages are routed via the isContest branch, not the normal path)
        string fd = StripApSuffix($"{MY_CALL} {THEIR_CALL} 2A MO a35");
        CheckStr("case 3: stripped FD exchange",  fd, $"{MY_CALL} {THEIR_CALL} 2A MO");
        Check("case 3: IsContest=true",           WsjtxMessage.IsContest(fd), true);
        Check("case 3: IsInvalidType=true (FD routes via contest branch, not normal path)",
                                                  WsjtxMessage.IsInvalidType(fd), true);
    }

    // ── TEMPORARY one-off verification, not part of the regular suite ─────────
    // Compares the new Club Log-backed / built-in universes against the existing
    // companion-file approach using REAL downloaded Club Log data (not a
    // fixture). Requires network access, so it's gated behind --verify-clublog
    // and is not called from Main()'s normal run. Delete after use.
    // Persistent (not per-run) cache dir so repeat runs of this verification
    // reuse the downloaded country file instead of re-downloading every time.
    static readonly string ClubLogVerifyCacheDir =
        Path.Combine(Path.GetTempPath(), "JimmyVerify_ClubLog_Cache");

    static void VerifyClubLogEquivalence()
    {
        string listsFolder = @"C:\claude\Jimmy\WSJTX_Controller\bin\Debug\RuleDefinitions\Lists";
        Directory.CreateDirectory(ClubLogVerifyCacheDir);

        // CA_PROVINCES needs no Club Log data at all.
        Console.WriteLine("=== CA_PROVINCES vs rac_provinces.txt ===");
        CompareUniverses("CA_PROVINCES", "File:rac_provinces.txt", listsFolder, null);
        Console.WriteLine();

        // Real key lives in a private file outside the repo, never an env var
        // or a build artifact -- read line 9 directly (same convention as
        // Jimmy.csproj's ClubLogKeyFile/ClubLogKeyLineNumber properties).
        string keyFilePath = @"C:\Users\Jim\Dropbox\amateur radio\Keys_private\Club Log API key for Jimmy.txt";
        string key = "";
        if (File.Exists(keyFilePath))
        {
            var lines = File.ReadAllLines(keyFilePath);
            if (lines.Length >= 9) key = (lines[8] ?? "").Trim();
        }
        if (string.IsNullOrEmpty(key))
        {
            Console.WriteLine($"Could not read Club Log key from line 9 of {keyFilePath} -- DXCC_* comparisons skipped.");
            return;
        }

        var provider = new ClubLogProvider(ClubLogVerifyCacheDir);
        provider.Configure(true, key);
        provider.Load();   // reuse cached clublog_cty.xml if one already exists
        if (provider.EntityCount == 0)
        {
            Console.WriteLine("No cached Club Log data yet -- downloading once...");
            bool ok = provider.RefreshAsync().GetAwaiter().GetResult();
            Console.WriteLine($"  RefreshAsync() returned: {ok}, EntityCount={provider.EntityCount}, LastError={provider.LastError}");
            if (!ok || provider.EntityCount == 0)
            {
                Console.WriteLine("Could not download real Club Log data -- DXCC_* comparisons skipped.");
                return;
            }
        }
        else
        {
            Console.WriteLine($"Reusing cached Club Log data: EntityCount={provider.EntityCount}, LastUpdate={provider.LastUpdate}");
        }

        Console.WriteLine();
        Console.WriteLine("=== DXCC_NORTH_AMERICA vs na_dxcc_entities.txt (as LimitTo) ===");
        CompareUniverses("DXCC_NORTH_AMERICA", "File:na_dxcc_entities.txt", listsFolder, provider);

        Console.WriteLine();
        Console.WriteLine("=== Other Club Log-backed universes (no companion-file counterpart to compare) ===");
        foreach (var token in new[] { "DXCC_CURRENT", "DXCC_DELETED", "DXCC_SOUTH_AMERICA", "DXCC_EUROPE", "DXCC_AFRICA", "DXCC_ASIA", "DXCC_OCEANIA" })
        {
            string err;
            var set = RuleUniverse.Resolve(token, listsFolder, provider, out err);
            Console.WriteLine(set == null
                ? $"  {token}: ERROR {err}"
                : $"  {token}: {set.Count} entities");
        }
    }

    static void CompareUniverses(string newToken, string oldToken, string listsFolder, ClubLogProvider clubLog)
    {
        string err1, err2;
        var a = RuleUniverse.Resolve(newToken, listsFolder, clubLog, out err1);
        var b = RuleUniverse.Resolve(oldToken, listsFolder, clubLog, out err2);

        if (a == null) { Console.WriteLine($"  {newToken}: ERROR {err1}"); return; }
        if (b == null) { Console.WriteLine($"  {oldToken}: ERROR {err2}"); return; }

        var onlyInNew = a.Except(b, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var onlyInOld = b.Except(a, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        Console.WriteLine($"  {newToken}: {a.Count} entries   {oldToken}: {b.Count} entries");
        if (onlyInNew.Count == 0 && onlyInOld.Count == 0)
        {
            Console.WriteLine("  IDENTICAL");
        }
        else
        {
            Console.WriteLine("  DIFFERENT");
            if (onlyInNew.Count > 0) Console.WriteLine($"    Only in {newToken}: {string.Join(", ", onlyInNew)}");
            if (onlyInOld.Count > 0) Console.WriteLine($"    Only in {oldToken}: {string.Join(", ", onlyInOld)}");
        }
    }

    // ── RuleUniverse built-ins ────────────────────────────────────────────────
    // CA_PROVINCES and the AN (Antarctica) continent code, added to replace the
    // rac_provinces.txt companion file and fill a gap in the ADIF continent set.
    static void RuleUniverseBuiltInTests()
    {
        Console.WriteLine("\n── RuleUniverse: Built-in Universes ──");

        string err;
        var caProvinces = RuleUniverse.Resolve("CA_PROVINCES", "", null, out err);
        CheckStr("CA_PROVINCES: no error", err, null);
        Check("CA_PROVINCES: count == 13", caProvinces != null && caProvinces.Count == 13, true);
        foreach (var p in new[] { "AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT" })
            Check($"CA_PROVINCES: contains {p}", caProvinces != null && caProvinces.Contains(p), true);

        Check("Continents: includes AN (Antarctica)", RuleUniverse.Continents.Contains("AN"), true);
        Check("Continents: has 7 entries",             RuleUniverse.Continents.Length == 7,   true);
    }

    // ── RuleUniverse Club Log-backed universes ─────────────────────────────────
    // DXCC_CURRENT / DXCC_DELETED / continent-filtered DXCC universes, resolved
    // from a fixture ClubLogProvider (no network access -- Load() reads a local
    // XML file, same mechanism ClubLogProvider uses for its real cache).
    static void RuleUniverseClubLogTests()
    {
        Console.WriteLine("\n── RuleUniverse: Club Log-backed Universes ──");

        // Unavailable case: no provider at all (e.g. a caller that never wired one up).
        string unavailErr;
        var noProvider = RuleUniverse.Resolve("DXCC_CURRENT", "", null, out unavailErr);
        Check("DXCC_CURRENT: null provider -> unresolved", noProvider == null, true);
        Check("DXCC_CURRENT: null provider -> has error",  !string.IsNullOrEmpty(unavailErr), true);

        string tmpRoot = Path.Combine(Path.GetTempPath(),
            "JimmyTest_ClubLog_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(tmpRoot, "ClubLog"));
            // Entity node name must be upper-case ENTITY: ClubLogProvider.ParseXml
            // looks for "ENTITY" first and only falls back to "entity" if that
            // XPath query returns null, but SelectNodes returns an empty (non-null)
            // list rather than null when nothing matches -- so lower-case entity
            // elements would silently parse as zero entities.
            string xml =
                "<clublog><entities>" +
                "<ENTITY><adif>6</adif><name>ALASKA</name><prefix>KL</prefix><deleted>FALSE</deleted><cqz>1</cqz><cont>NA</cont></ENTITY>" +
                "<ENTITY><adif>291</adif><name>UNITED STATES OF AMERICA</name><prefix>K</prefix><deleted>FALSE</deleted><cqz>5</cqz><cont>NA</cont></ENTITY>" +
                "<ENTITY><adif>100</adif><name>ARGENTINA</name><prefix>LU</prefix><deleted>FALSE</deleted><cqz>13</cqz><cont>SA</cont></ENTITY>" +
                "<ENTITY><adif>999</adif><name>FICTIONAL DELETED ENTITY</name><prefix>ZZ</prefix><deleted>TRUE</deleted><cqz>1</cqz><cont>NA</cont></ENTITY>" +
                "</entities></clublog>";
            File.WriteAllText(Path.Combine(tmpRoot, "ClubLog", "clublog_cty.xml"), xml);

            var provider = new ClubLogProvider(tmpRoot);
            provider.Configure(true, "");
            provider.Load();
            Check("Fixture: 4 entities loaded", provider.EntityCount == 4, true);

            string err;
            var current = RuleUniverse.Resolve("DXCC_CURRENT", "", provider, out err);
            Check("DXCC_CURRENT: resolved",            current != null,                     true);
            Check("DXCC_CURRENT: count == 3",           current != null && current.Count == 3, true);
            Check("DXCC_CURRENT: includes 6 (Alaska)",   current != null && current.Contains("6"),   true);
            Check("DXCC_CURRENT: includes 291 (USA)",    current != null && current.Contains("291"), true);
            Check("DXCC_CURRENT: includes 100 (Argentina)", current != null && current.Contains("100"), true);
            Check("DXCC_CURRENT: excludes deleted 999",  current != null && !current.Contains("999"), true);

            var deleted = RuleUniverse.Resolve("DXCC_DELETED", "", provider, out err);
            Check("DXCC_DELETED: count == 1",     deleted != null && deleted.Count == 1,     true);
            Check("DXCC_DELETED: contains 999",   deleted != null && deleted.Contains("999"), true);

            var na = RuleUniverse.Resolve("DXCC_NORTH_AMERICA", "", provider, out err);
            Check("DXCC_NORTH_AMERICA: count == 2",          na != null && na.Count == 2,          true);
            Check("DXCC_NORTH_AMERICA: includes 6",          na != null && na.Contains("6"),        true);
            Check("DXCC_NORTH_AMERICA: includes 291",        na != null && na.Contains("291"),      true);
            Check("DXCC_NORTH_AMERICA: excludes deleted 999", na != null && !na.Contains("999"),     true);

            var sa = RuleUniverse.Resolve("DXCC_SOUTH_AMERICA", "", provider, out err);
            Check("DXCC_SOUTH_AMERICA: count == 1",   sa != null && sa.Count == 1,        true);
            Check("DXCC_SOUTH_AMERICA: contains 100", sa != null && sa.Contains("100"),   true);

            var eu = RuleUniverse.Resolve("DXCC_EUROPE", "", provider, out err);
            Check("DXCC_EUROPE: count == 0 (none in fixture)", eu != null && eu.Count == 0, true);
        }
        finally
        {
            try { Directory.Delete(tmpRoot, true); } catch { }
        }
    }
}
