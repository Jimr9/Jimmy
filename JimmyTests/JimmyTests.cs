using System;
using WsjtxUdpLib.Messages.Out;

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
}
