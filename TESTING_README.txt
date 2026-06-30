JIMMY TESTING GUIDE
===================
Plain text. Screen-reader compatible.
Last updated: 2026-06-28


CONTENTS
--------
1.  Overview
2.  Prerequisites
3.  How to test safely (no transmissions, no real QSOs affected)
4.  Suite 1: Parser Unit Tests
5.  Suite 2: Replay Integration Tests
6.  What PASS and FAIL mean
7.  How to add a new parser unit test
8.  How to add a new replay test
9.  Best practices for regression testing


1. OVERVIEW
-----------
Jimmy has two test suites:

Suite 1: Parser Unit Tests (JimmyTests)
  Fast, offline, no process needed.
  Tests the message classification logic that Jimmy uses to decide
  what to do with every decode received from WSJT-X.

Suite 2: Replay Integration Tests (JimmyReplay)
  Requires Jimmy to be running (but NOT WSJT-X).
  Simulates WSJT-X over UDP and verifies Jimmy's queue and status
  text respond correctly to known message sequences.
  No radio. No transmissions. No real QSOs are affected.


2. PREREQUISITES
----------------
Both suites:
  - Visual Studio Community (any recent edition) or Build Tools for
    Visual Studio, with MSBuild and the C# compiler installed.
  - Python 3.6 or later (for Suite 2 only).

Suite 2 only:
  - Jimmy must be running (Debug build).
  - WSJT-X must be closed.
  - In Jimmy: set mode to CQ.
  - In Jimmy: enable Advanced Call Layout (Options dialog).
    This bypasses T/R period gating so the test messages arrive
    in both even and odd periods without being filtered out.


3. HOW TO TEST SAFELY
---------------------
Suite 1 (parser tests) is entirely offline. It links against the
Jimmy assembly and calls static classifier methods directly.
No network traffic. No radio. Nothing external is involved.

Suite 2 (replay tests) sends UDP packets to Jimmy on localhost
(127.0.0.1 port 2237). WSJT-X is not running, so Jimmy has no
connection to a real radio. No CAT commands are sent. No PTT is
keyed. No frequencies change. The test traffic is synthetic and
self-contained on your computer.

To be completely safe:
  - Close WSJT-X before running Suite 2.
  - Do not connect a radio while running Suite 2.
  - Run the Debug build of Jimmy, not the installed release.


4. SUITE 1: PARSER UNIT TESTS
------------------------------
File: JimmyTests\JimmyTests.cs
Runner script: run_parser_tests.bat

What it tests:
  - AP suffix stripping (WSJT-X 3.0 "a35" and old-format "?").
  - WsjtxMessage static classifiers: IsReport, IsRogerReport,
    Is73, IsRR73, Is73orRR73, IsRogers, IsCQ, IsPota, IsSota,
    DirectedTo, IsContest, IsReply, IsShortReply, IsInvalidType.
  - ToCall and DeCall extraction.
  - End-to-end: AP-suffixed messages are stripped then classified
    correctly.
  - Contest messages route via the contest branch (IsInvalidType
    is intentionally true for FD exchanges in the normal path).

How to run:
  Double-click run_parser_tests.bat
  or type at a command prompt:
    run_parser_tests

The script builds Jimmy, builds JimmyTests, then runs the tests.
All output appears in the console window.


5. SUITE 2: REPLAY INTEGRATION TESTS
--------------------------------------
File: JimmyReplay.py
Runner script: run_replay_tests.bat

What it tests (current groups):

  Group 1 (T01-T06): Full QSO exchange directed at me.
    K4YT sends grid, report, roger-report, RRR, RR73, then 73.
    Verifies K4YT is queued at each step and the signoff path fires.

  Group 2 (T07-T08): CQ messages.
    Plain CQ and directed POTA CQ from K4YT.
    Verifies K4YT is added to the call queue.

  Group 3 (T09-T10): WSJT-X 3.0 AP suffix.
    Messages with "a35" and "a1" suffixes.
    Verifies Jimmy strips the suffix and classifies correctly.

  Group 4 (T11-T12): Contest and Field Day exchanges.
    FD exchange directed to me: accepted and queued.
    Contest exchange between other stations: rejected.

  Group 5 (T13-T14): Fix #3 - final 73 after a logged QSO.
    Logs K4YT via QsoLoggedMessage, then sends a final 73.
    Verifies status says "final 73" and K4YT is NOT re-queued.

  Group 6 (T15): Fix #4 - re-call after a prior logged QSO.
    K4YT (already in logListBox from Group 5) calls again.
    Verifies K4YT IS re-queued (old guard that blocked this is gone).

How to run:
  1. Close WSJT-X.
  2. Start Jimmy (Debug build).
  3. Set Jimmy mode to CQ.
  4. Enable Advanced Call Layout in Options.
  5. Double-click run_replay_tests.bat
     or type: run_replay_tests

Jimmy does not need to be connected to WSJT-X. The replay script
acts as a simulated WSJT-X and completes the handshake itself.

If Jimmy is not running, the script still executes but skips all
UI assertions and prints "(Verifier was not available)".


6. WHAT PASS AND FAIL MEAN
---------------------------
Parser tests (Suite 1):
  PASS  - The classifier returned the expected value.
  FAIL  - The classifier returned the wrong value. The label and
          both expected and actual values are printed.
  Final line shows "ALL TESTS PASSED" or "SOME TESTS FAILED".
  A failing parser test means a regression in message classification.

Replay tests (Suite 2):
  checkmark PASS - Jimmy's UI showed the expected state within the
                   timeout window (usually 3-4 seconds).
  cross FAIL     - Jimmy's UI did not reach the expected state.
                   The actual queue contents or status text are
                   printed so you can diagnose the difference.
  Final summary: "N/M assertions passed".
  A failing replay test means a regression in Jimmy's behavior.


7. HOW TO ADD A NEW PARSER UNIT TEST
--------------------------------------
Parser tests live in JimmyTests\JimmyTests.cs.

Step 1: Find or create a test group function.
  Each group is a static void method, for example ApStripTests(),
  ReportTests(), FinalAckTests(). If your test fits an existing
  group, add it there. If it covers a new category, add a new
  method at the bottom of the class.

Step 2: Write the test using Check() or CheckStr().
  Check(label, actual_bool, expected_bool)
  CheckStr(label, actual_string, expected_string)

  Example:
    Check("IsReport: zero dB",
          WsjtxMessage.IsReport($"{MY_CALL} {THEIR_CALL} +00"),
          true);

Step 3: If you created a new group method, call it from Main().
  Add the call after the last existing group call:
    YourNewGroupTests();

Step 4: Run run_parser_tests.bat to confirm PASS.

Best practice: Every new classifier method in WsjtxMessage.cs
should have at least one true case and one false case in the
parser tests.


8. HOW TO ADD A NEW REPLAY TEST
---------------------------------
Replay tests live in JimmyReplay.py.
Tests are organized as group functions (group1, group2, ...).
The test number tag (T01, T02, ...) is assigned automatically
by a global counter, so tests always number sequentially.

Step 1: Define a new group function at the bottom of the group
  functions section (just before run_tests()).

  Example:
    def group7_my_new_scenario(sock, v):
        """T16: My new regression test."""
        print("  - Group 7: My new scenario -")

        send(sock,
             "Label shown in output",
             "What this message tests",
             build_enqueue(f"{MY_CALL} {THEIR_CALL} -05"),
             verify_fn=lambda: (
                 v.check_queue_contains(THEIR_CALL,
                     f"T16: {THEIR_CALL} in queue after new message"),
             ) if v.available else None)

Step 2: Call your group from run_tests(), just before the
  "To add a new replay test group" comment block:
    group7_my_new_scenario(sock, v)

Step 3: Run run_replay_tests.bat with Jimmy running to confirm PASS.

Available send() parameters:
  sock        - the UDP socket (always pass the sock parameter)
  label       - short label printed in the test header line
  description - longer description printed below the label
  payload     - UDP bytes (use build_enqueue, build_qso_logged, etc.)
  verify_fn   - optional lambda that calls v.check_* methods
  delay       - seconds to wait after sending (default 1.5)

Available verify assertions:
  v.check_queue_contains(fragment, label)
  v.check_queue_not_contains(fragment, label)
  v.check_status_contains(fragment, label)
  v.check_status_not_contains(fragment, label)
  v.check_log_contains(fragment, label)
  v.check_active(label)

Note on group dependencies:
  Groups 5 and 6 are intentionally sequential (Group 5 logs K4YT;
  Group 6 tests what happens when K4YT calls again). New groups
  should be independent when possible. Document any dependency in
  the group function's docstring.


9. BEST PRACTICES FOR REGRESSION TESTING
------------------------------------------
Every bug fix should get a test before the fix and a passing test
after.

For parser bugs (wrong classification of a message):
  1. Reproduce the bad input as a string in JimmyTests.cs.
  2. Add a Check() call that fails with the old code.
  3. Fix WsjtxMessage.cs.
  4. Confirm the test now passes.
  5. Leave the test in place permanently.

For behavior bugs (Jimmy queues/drops/sounds wrong):
  1. Identify the message sequence that triggered the bug.
  2. Add a new replay group in JimmyReplay.py that sends those
     messages and asserts the correct outcome.
  3. Fix Jimmy.
  4. Run the replay test with Jimmy running and confirm PASS.
  5. Leave the group in place as a regression guard.

Keep tests small and focused:
  One assertion per observable behavior.
  If a test needs setup state (e.g., a prior QSO logged), document
  the dependency in the group function's docstring.

Naming conventions:
  Parser groups:  FooBarTests() in JimmyTests.cs
  Replay groups:  group7_short_description(sock, v) in JimmyReplay.py
  Test labels:    Start with the tag "T16: " for traceability.


END OF TESTING GUIDE
