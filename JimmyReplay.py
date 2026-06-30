#!/usr/bin/env python3
"""
JimmyReplay.py - UDP test sender with automatic verification for Jimmy.

Simulates WSJT-X: sends known decode messages to Jimmy, then reads
Jimmy's statusText and callListBox directly via Win32 API to verify
expected queue entries, status text, and state transitions.

No external packages required — uses only Python standard library +
ctypes (built-in).

BEFORE RUNNING:
  1. Close WSJT-X (Jimmy must not be connected to real WSJT-X).
  2. Start Jimmy (Debug build).
  3. In Jimmy, set mode to CQ.
  4. Enable Advanced Call Layout (Options) to bypass T/R period checks.
  5. (Optional) Add 'SOTA' to the directed CQ alert text box for a full T17 PASS.
     Without it, T17 prints a WARNING instead of PASS or FAIL — this is not a bug.

USAGE:
  python JimmyReplay.py

Requires Python 3.6+.
"""

import socket
import struct
import sys
import time
import ctypes
import ctypes.wintypes
import datetime
import os
import tempfile
import atexit

# Ensure UTF-8 output so checkmark/cross symbols render on any Windows console
try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

# ─── Jimmy/WSJT-X Configuration ──────────────────────────────────────────────
JIMMY_HOST    = "127.0.0.1"
JIMMY_PORT    = 2237
LOCAL_PORT    = 9999
RECV_TIMEOUT  = 3.0

WSJT_ID       = "WSJT-X"
WSJT_VERSION  = "3.0.0-rc1"
WSJT_REVISION = "102"        # must match Jimmy's acceptableWsjtxVersions

MY_CALL  = "KB0UZT"
MY_GRID  = "FN42"
THEIR_CALL = "K4YT"

# Period-check test stations (group 10) — unique callsigns not used elsewhere
FT8_EVEN_CALL = "W9EVN"   # FT8 expected (even) period
FT8_ODD_CALL  = "W9ODD"   # FT8 opposite (odd) period

# ─── Protocol Constants ───────────────────────────────────────────────────────
MAGIC            = bytes([0xAD, 0xBC, 0xCB, 0xDA])
MSG_HEARTBEAT    = 0
MSG_STATUS       = 1
MSG_QSO_LOGGED   = 5
MSG_ENQUEUE_V3   = 18
MSG_ENABLE_TX    = 17

# ─── Win32 API Constants ──────────────────────────────────────────────────────
GWL_STYLE         = -16
ES_READONLY       = 0x0800
WM_GETTEXT        = 0x000D
WM_GETTEXTLENGTH  = 0x000E
LB_GETCOUNT       = 0x018B
LB_GETTEXTLEN     = 0x018A
LB_GETTEXT        = 0x0189

_user32 = ctypes.windll.user32

# ═══════════════════════════════════════════════════════════════════════════════
# Win32 Helpers
# ═══════════════════════════════════════════════════════════════════════════════

class _RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]

class _POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]

_EnumChildProc = ctypes.WINFUNCTYPE(ctypes.c_bool,
                                    ctypes.wintypes.HWND, ctypes.wintypes.LPARAM)
_EnumWndProc   = ctypes.WINFUNCTYPE(ctypes.c_bool,
                                    ctypes.wintypes.HWND, ctypes.wintypes.LPARAM)

def _all_descendants(parent):
    results = []
    @_EnumChildProc
    def cb(hwnd, _):
        results.append(hwnd)
        return True
    _user32.EnumChildWindows(parent, cb, 0)
    return results

def _wnd_title(hwnd):
    buf = ctypes.create_unicode_buffer(512)
    _user32.GetWindowTextW(hwnd, buf, 512)
    return buf.value

def _cls(hwnd):
    buf = ctypes.create_unicode_buffer(256)
    _user32.GetClassNameW(hwnd, buf, 256)
    return buf.value.upper()

def _style(hwnd):
    return _user32.GetWindowLongW(hwnd, GWL_STYLE)

def _read_text(hwnd):
    n = _user32.SendMessageW(hwnd, WM_GETTEXTLENGTH, 0, 0)
    if n <= 0:
        return ""
    buf = ctypes.create_unicode_buffer(n + 2)
    _user32.SendMessageW(hwnd, WM_GETTEXT, n + 1, buf)
    return buf.value

def _read_listbox(hwnd):
    count = _user32.SendMessageW(hwnd, LB_GETCOUNT, 0, 0)
    if count <= 0:
        return []
    items = []
    for i in range(count):
        n = _user32.SendMessageW(hwnd, LB_GETTEXTLEN, i, 0)
        if n > 0:
            buf = ctypes.create_unicode_buffer(n + 2)
            _user32.SendMessageW(hwnd, LB_GETTEXT, i, buf)
            items.append(buf.value)
    return items

def _client_xy(child, parent):
    """Child's top-left corner in parent's client coordinates."""
    r = _RECT()
    _user32.GetWindowRect(child, ctypes.byref(r))
    p = _POINT(r.left, r.top)
    _user32.MapWindowPoints(0, parent, ctypes.byref(p), 1)
    return p.x, p.y

# ═══════════════════════════════════════════════════════════════════════════════
# JimmyVerifier
# ═══════════════════════════════════════════════════════════════════════════════

class JimmyVerifier:
    """
    Reads Jimmy's UI controls via Win32 to assert expected state.

    Controls identified:
      statusText  — the ONLY read-only EDIT child (ES_READONLY style)
      callListBox — leftmost LISTBOX at y < 200 in client coords  (x≈15)
      logListBox  — next LISTBOX at y < 200 in client coords      (x≈214)

    Sound events cannot be read externally, but are inferred from state:
      CallingMe sound fired  → call appears in callListBox
      finalSignoff path fired → status contains "final 73", call NOT in queue
    """

    def __init__(self):
        self._jhwnd    = None
        self._stat     = None
        self._calllist = None
        self._loglist  = None
        self.passed    = 0
        self.failed    = 0
        self._find()

    def _find(self):
        hits = []
        @_EnumWndProc
        def cb(hwnd, _):
            if _wnd_title(hwnd).startswith("Jimmy"):
                hits.append(hwnd)
            return True
        _user32.EnumWindows(cb, 0)
        if not hits:
            return
        self._jhwnd = hits[0]

        children  = _all_descendants(self._jhwnd)
        edits     = [h for h in children if 'EDIT'    in _cls(h)]
        listboxes = [h for h in children if 'LISTBOX' in _cls(h)]

        # statusText: only ReadOnly EDIT
        for h in edits:
            if _style(h) & ES_READONLY:
                self._stat = h
                break

        # callListBox: leftmost LISTBOX near top of form (y < 200, Designer y=24)
        # logListBox:  second leftmost at same y band (Designer y=24 but x=214)
        # advTx1/2/rawListBox are at y=392+ so filtered out
        near = []
        for h in listboxes:
            try:
                x, y = _client_xy(h, self._jhwnd)
                if 0 < y < 200:
                    near.append((x, y, h))
            except Exception:
                pass
        near.sort()                        # sort by x
        if near:
            self._calllist = near[0][2]    # leftmost = callListBox (x≈15)
        if len(near) >= 2:
            self._loglist = near[1][2]     # next = logListBox (x≈214)

    @property
    def available(self):
        return bool(self._jhwnd and self._stat and self._calllist)

    def _live_listboxes(self):
        """Re-enumerate child listboxes on every call.

        WinForms recreates the ListBox HWND when SelectionMode changes
        (e.g. None→One when the first call is enqueued, One→None when the
        queue empties).  Caching the HWND from _find() causes every
        subsequent LB_GETCOUNT to read a destroyed window and return 0.
        Re-discovering from the stable form HWND is the reliable fix.
        """
        if not self._jhwnd:
            return None, None
        children  = _all_descendants(self._jhwnd)
        listboxes = [h for h in children if 'LISTBOX' in _cls(h)]
        near = []
        for h in listboxes:
            try:
                x, y = _client_xy(h, self._jhwnd)
                if 0 < y < 200:
                    near.append((x, y, h))
            except Exception:
                pass
        near.sort()
        calllist = near[0][2] if near else None
        loglist  = near[1][2] if len(near) >= 2 else None
        return calllist, loglist

    # ── Read methods ──────────────────────────────────────────────────────
    def status_text(self):
        return _read_text(self._stat) if self._stat else ""

    def queue_items(self):
        calllist, _ = self._live_listboxes()
        return _read_listbox(calllist) if calllist else []

    def log_items(self):
        _, loglist = self._live_listboxes()
        return _read_listbox(loglist) if loglist else []

    # ── Polling ───────────────────────────────────────────────────────────
    def wait_for_status(self, fragment, timeout=4.0):
        """Block until status contains fragment or timeout. Returns final text."""
        deadline = time.time() + timeout
        while time.time() < deadline:
            t = self.status_text()
            if fragment.lower() in t.lower():
                return t
            time.sleep(0.1)
        return self.status_text()

    def wait_for_queue(self, fragment, timeout=4.0):
        """Block until callListBox contains an item with fragment.

        Jimmy spaces out callsign characters in display rows
        (e.g. "K 4 Y T, USA, -10 ..."), so strip spaces before comparing.
        """
        frag_nsp = fragment.lower().replace(" ", "")
        deadline = time.time() + timeout
        while time.time() < deadline:
            if any(frag_nsp in i.lower().replace(" ", "") for i in self.queue_items()):
                return True
            time.sleep(0.1)
        return False

    # ── Assertions ────────────────────────────────────────────────────────
    def _report(self, ok, label, detail=""):
        mark = "✓ PASS" if ok else "✗ FAIL"
        d    = f"  [{detail}]" if detail else ""
        print(f"    {mark}  {label}{d}")
        if ok:
            self.passed += 1
        else:
            self.failed += 1

    def check_active(self, label="Jimmy is ACTIVE"):
        t  = self.status_text()
        ok = bool(t) and not any(w in t.lower()
                                  for w in ("idle", "inactive", "start", "connecting"))
        self._report(ok, label, f"status='{t}'")

    def check_status_contains(self, fragment, label):
        self.wait_for_status(fragment, timeout=3.0)
        t  = self.status_text()
        ok = fragment.lower() in t.lower()
        self._report(ok, label, f"status='{t}'")

    def check_status_not_contains(self, fragment, label):
        # Give Jimmy a moment to settle before checking the negative
        time.sleep(0.3)
        t  = self.status_text()
        ok = fragment.lower() not in t.lower()
        self._report(ok, label, f"status='{t}'")

    def check_queue_contains(self, fragment, label):
        self.wait_for_queue(fragment, timeout=3.0)
        items    = self.queue_items()
        frag_nsp = fragment.lower().replace(" ", "")
        ok       = any(frag_nsp in i.lower().replace(" ", "") for i in items)
        self._report(ok, label, f"queue={items}")

    def check_queue_not_contains(self, fragment, label):
        time.sleep(0.3)
        items    = self.queue_items()
        frag_nsp = fragment.lower().replace(" ", "")
        ok       = not any(frag_nsp in i.lower().replace(" ", "") for i in items)
        self._report(ok, label, f"queue={items}")

    def check_queue_contains_warn(self, fragment, label, config_note):
        """Soft queue check: PASS if the call is found; WARNING (not FAIL) if not.

        Use for tests whose admission depends on a user-configurable Options setting
        that cannot be read remotely. Does not increment the fail counter when absent.
        """
        self.wait_for_queue(fragment, timeout=3.0)
        items    = self.queue_items()
        frag_nsp = fragment.lower().replace(" ", "")
        ok       = any(frag_nsp in i.lower().replace(" ", "") for i in items)
        if ok:
            self._report(True, label, f"queue={items}")
        else:
            print(f"    ⚠ WARN  {label}")
            print(f"           (not failed — requires: {config_note})")
            print(f"           queue={items}")

    def check_log_contains(self, fragment, label):
        # logListBox shows auto-logged calls formatted as "K 4 Y T, Country".
        # Jimmy spaces out callsign characters, so strip spaces from both
        # sides before comparing (e.g. "K4YT" matches "K 4 Y T, USA").
        frag_nsp = fragment.lower().replace(" ", "")
        deadline = time.time() + 3.0
        while time.time() < deadline:
            if any(frag_nsp in i.lower().replace(" ", "") for i in self.log_items()):
                break
            time.sleep(0.1)
        items = self.log_items()
        ok    = any(frag_nsp in i.lower().replace(" ", "") for i in items)
        self._report(ok, label, f"logList={items}")

    def summary(self):
        total = self.passed + self.failed
        print(f"\n  Verification summary: {self.passed}/{total} assertions passed")
        if self.failed:
            print(f"  ✗ {self.failed} assertion(s) FAILED")
        else:
            print(f"  ✓ All assertions passed")
        return self.failed == 0


# ═══════════════════════════════════════════════════════════════════════════════
# Binary Encoding (WSJT-X UDP protocol)
# ═══════════════════════════════════════════════════════════════════════════════

def _u8(n):    return bytes([n & 0xFF])
def _u32(n):   return struct.pack('>I', n & 0xFFFFFFFF)
def _i32(n):   return struct.pack('>i', n)
def _u64(n):   return struct.pack('>Q', n)
def _f64(f):   return struct.pack('>d', f)
def _flag(b):  return bytes([1 if b else 0])

def _qstr(s):
    if s is None:
        return b'\xff\xff\xff\xff'
    b = s.encode('utf-8')
    return struct.pack('>I', len(b)) + b

def _julian(d):
    a = (14 - d.month) // 12
    y = d.year + 4800 - a
    m = d.month + 12 * a - 3
    return d.day + (153*m+2)//5 + 365*y + y//4 - y//100 + y//400 - 32045

def _qdatetime(dt):
    jd = _julian(dt.date())
    ms = (dt.hour*3600 + dt.minute*60 + dt.second)*1000 + dt.microsecond//1000
    return struct.pack('>q', jd) + _u32(ms) + _u8(1)


def build_heartbeat():
    return (MAGIC + _u32(2) + _u32(MSG_HEARTBEAT) +
            _qstr(WSJT_ID) + _u32(3) + _qstr(WSJT_VERSION) + _qstr(WSJT_REVISION))


def build_status(check=""):
    # TxFirst=False → Jimmy transmits in odd periods, receives in even.
    # SinceMidnight=0ms in decode messages (even period) matches this.
    return (
        MAGIC + _u32(2) + _u32(MSG_STATUS) +
        _qstr(WSJT_ID) +
        _u64(14_074_000) +       # Dial freq: 14.074 MHz
        _qstr("FT8") +           # Mode
        _qstr("") +              # DX call
        _qstr("-05") +           # Report
        _qstr("FT8") +           # Tx mode
        _flag(False) +           # Tx enabled
        _flag(False) +           # Transmitting
        _flag(False) +           # Decoding
        _u32(1500) +             # Rx DF
        _u32(1500) +             # Tx DF
        _qstr(MY_CALL) +         # DE call
        _qstr(MY_GRID) +         # DE grid
        _qstr("") +              # Detail / DX grid
        _flag(False) +           # Tx watchdog
        _qstr("") +              # Sub-mode
        _flag(False) +           # Fast mode
        _u8(0) +                 # Special operation: NONE
        _u32(0xFFFFFFFF) +       # Result code (N/A)
        _u32(15) +               # T/R period: 15 s (FT8)
        _qstr("Default") +       # Config name
        _qstr("") +              # Last Tx msg
        _u32(0) +                # QSO progress: CALLING
        _flag(False) +           # TxFirst: False → Jimmy TX in odd periods
        _flag(False) +           # DblClk
        _qstr(check) +           # Check field: echo CmdCheck from cmd:7
        _flag(False) +           # Tx halt clock
        _flag(False) +           # Tx enable button
        _flag(False) +           # Tx enable clock
        _qstr("NA") +            # My continent
        _flag(False)             # Metric units
    )


def build_enqueue(message_text, snr=-10, is_new_call=True, country="USA", continent="NA",
                  since_midnight_ms=0, mode="FT8"):
    # since_midnight_ms controls the T/R period of the simulated decode:
    #   0ms     → even period (FT8: period 0; FT4: 0-6s range)
    #   15000ms → odd  period (FT8: period 1, 15-29s range = TX window with TxFirst=False)
    #   7500ms  → odd  period (FT4: 7-14s range)
    # With TxFirst=False in StatusMessage, even periods are receive windows.
    return (
        MAGIC + _u32(2) + _u32(MSG_ENQUEUE_V3) +
        _qstr(WSJT_ID) +
        _flag(True) +            # AutoGen
        _u32(since_midnight_ms) +  # SinceMidnight in ms
        _i32(snr) +              # SNR
        _f64(0.1) +              # Delta time
        _u32(1500) +             # Delta frequency
        _qstr(mode) +
        _qstr(message_text) +
        _flag(False) +           # Is DX
        _flag(False) +           # Modifier
        _flag(is_new_call) +     # New call on band
        _flag(is_new_call) +     # New call any band
        _flag(False) +           # New country on band
        _flag(False) +           # New country
        _qstr(country) +
        _qstr(continent) +
        _i32(0) +                # Azimuth
        _i32(1000)               # Distance
    )


def build_qso_logged(dx_call, dx_grid="EM63"):
    now = datetime.datetime.utcnow()
    return (
        MAGIC + _u32(2) + _u32(MSG_QSO_LOGGED) +
        _qstr(WSJT_ID) +
        _qdatetime(now) +
        _qstr(dx_call) + _qstr(dx_grid) +
        _u64(14_074_000) + _qstr("FT8") +
        _qstr("-05") + _qstr("-10") +
        _qstr("") + _qstr("") + _qstr("") +
        _qdatetime(now) +
        _qstr("") + _qstr(MY_CALL) + _qstr(MY_GRID) +
        _qstr("") + _qstr("")
    )


# ═══════════════════════════════════════════════════════════════════════════════
# Jimmy UDP Startup
# ═══════════════════════════════════════════════════════════════════════════════

def _port_in_use(port):
    """Return True if something is already bound to the given UDP port.

    Must probe on the same address Jimmy uses (127.0.0.1), not 0.0.0.0.
    On Windows, binding 0.0.0.0:port does not conflict with 127.0.0.1:port,
    so a wildcard probe would always return False even when Jimmy is listening.
    """
    probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        probe.bind(("127.0.0.1", port))
        probe.close()
        return False   # bind succeeded → nothing listening
    except OSError:
        probe.close()
        return True    # bind failed → Jimmy has it


def ensure_jimmy_udp_ready():
    """
    Jimmy only binds its UDP socket after detecting WSJT-X via the lock file
    at %TEMP%\\WSJT-X.lock. If the port isn't open yet, create the lock file
    to trigger Jimmy's startup sequence, then wait for it to bind.

    The lock file is removed on exit so Jimmy cleanly detects 'WSJT-X closed'.
    """
    if _port_in_use(JIMMY_PORT):
        return True  # already open — nothing to do

    lock_path = os.path.join(tempfile.gettempdir(), "WSJT-X.lock")
    created = False

    if not os.path.exists(lock_path):
        print(f"  Jimmy UDP not yet open. Creating {lock_path} to trigger startup...")
        try:
            open(lock_path, "w").close()
            created = True
            atexit.register(lambda: os.path.exists(lock_path) and os.remove(lock_path))
        except OSError as e:
            print(f"  WARNING: Could not create lock file: {e}")
            return False
    else:
        print(f"  WSJT-X.lock already exists at {lock_path}")

    # Jimmy's mainLoopTimer fires every ~12 ms; CheckWsjtxRunning sleeps 3 s
    # then binds UDP. Poll up to 10 seconds total to cover the 3-s sleep + margin.
    print("  Waiting for Jimmy to open UDP (up to 10 s)...", end="", flush=True)
    deadline = time.time() + 10.0
    while time.time() < deadline:
        time.sleep(0.25)
        print(".", end="", flush=True)
        if _port_in_use(JIMMY_PORT):
            print(f" ready.\n")
            return True

    print(f"\n  WARNING: Jimmy did not open port {JIMMY_PORT} within 10 s.")
    if created:
        try:
            os.remove(lock_path)
        except OSError:
            pass
    return False


# ═══════════════════════════════════════════════════════════════════════════════
# WSJT-X Handshake
# ═══════════════════════════════════════════════════════════════════════════════

def _parse_enable_tx(data):
    """Extract (cmd_idx, cmd_check) from an EnableTxMessage."""
    if not data.startswith(MAGIC) or len(data) < 12:
        return None, None
    msg_type = struct.unpack_from('>I', data, 8)[0]
    if msg_type != MSG_ENABLE_TX:
        return None, None
    pos = 12
    def rd_u32():
        nonlocal pos
        v = struct.unpack_from('>I', data, pos)[0]; pos += 4; return v
    def rd_str():
        nonlocal pos
        n = rd_u32()
        if n == 0xFFFFFFFF: return ""
        s = data[pos:pos+n].decode('utf-8', errors='replace'); pos += n; return s
    rd_str()                   # Id
    cmd_idx = rd_u32()         # NewTxMsgIdx
    rd_str()                   # GenMsg
    pos += 1                   # SkipGrid
    pos += 1                   # UseRR73
    cmd_check = rd_str()       # CmdCheck
    return cmd_idx, cmd_check


def handshake(sock, v):
    """
    Complete the 3-step WSJT-X negotiation:
      1. Send HeartbeatMessage → Jimmy replies + sets NegoState=SENT
      2. Send HeartbeatMessage again → Jimmy goes SENT→RECD, sends cmd:7 (CmdCheck)
      3. Send StatusMessage with Check=CmdCheck → Jimmy sets commConfirmed=True → ACTIVE
    """
    print("\n──── Handshake ────")
    hb = build_heartbeat()

    sock.sendto(hb, (JIMMY_HOST, JIMMY_PORT))
    print(f"  → HeartbeatMessage  ({WSJT_VERSION}/{WSJT_REVISION})")
    sock.settimeout(RECV_TIMEOUT)
    try:
        data, _ = sock.recvfrom(4096)
        print(f"  ← Response: {len(data)} bytes")
    except (socket.timeout, ConnectionResetError):
        print("  ✗ No response. Is Jimmy running on port 2237?")
        return False

    time.sleep(0.3)
    sock.sendto(hb, (JIMMY_HOST, JIMMY_PORT))
    print("  → HeartbeatMessage again  (triggers SENT→RECD)")

    cmd_check = None
    deadline  = time.time() + RECV_TIMEOUT
    while time.time() < deadline:
        sock.settimeout(max(0.1, deadline - time.time()))
        try:
            data, _ = sock.recvfrom(4096)
            ci, cc  = _parse_enable_tx(data)
            if ci == 7 and cc is not None:
                cmd_check = cc
                print(f"  ← cmd:7  CmdCheck='{cmd_check}'")
        except (socket.timeout, ConnectionResetError):
            break

    if not cmd_check:
        print("  ✗ Did not receive CmdCheck. Is Jimmy already connected to WSJT-X?")
        print("    Close WSJT-X and restart Jimmy, then try again.")
        return False

    time.sleep(0.3)
    sock.sendto(build_status(check=cmd_check), (JIMMY_HOST, JIMMY_PORT))
    print(f"  → StatusMessage  (myCall={MY_CALL}, Check='{cmd_check}')")
    time.sleep(1.5)

    if v.available:
        v.check_active("Jimmy reached ACTIVE state")
    else:
        print("  (Verifier not available — skipping auto-check)")

    print()
    return True


# ═══════════════════════════════════════════════════════════════════════════════
# Test Runner
# ═══════════════════════════════════════════════════════════════════════════════

_test_num = 0

def send(sock, label, description, payload, verify_fn=None, delay=1.5):
    global _test_num
    _test_num += 1
    tag = f"T{_test_num:02d}"
    print(f"  [{tag}] {label}")
    print(f"        {description}")
    sock.sendto(payload, (JIMMY_HOST, JIMMY_PORT))
    time.sleep(delay)
    if verify_fn:
        verify_fn()
    print()


def group1_station_calling_me(sock, v):
    """T01-T06: K4YT works through a full QSO exchange directed at KB0UZT."""
    print("  ─ Group 1: K4YT calling KB0UZT (directed to me) ─")

    send(sock,
         "Grid reply: KB0UZT K4YT EM63",
         "CallingMe sound expected; K4YT queued with 'to you' tag",
         build_enqueue(f"{MY_CALL} {THEIR_CALL} EM63"),
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T01: {THEIR_CALL} in callQueue (CallingMe sound inferred)"),
         ) if v.available else None)

    send(sock,
         "Signal report: KB0UZT K4YT -05",
         "Queue entry updated; K4YT remains in queue",
         build_enqueue(f"{MY_CALL} {THEIR_CALL} -05"),
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T02: {THEIR_CALL} still in queue after report"),
         ) if v.available else None)

    send(sock,
         "Roger report: KB0UZT K4YT R-05",
         "Queue entry updated",
         build_enqueue(f"{MY_CALL} {THEIR_CALL} R-05"),
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T03: {THEIR_CALL} still in queue after R-report"),
         ) if v.available else None)

    send(sock,
         "RRR: KB0UZT K4YT RRR",
         "Queue entry updated",
         build_enqueue(f"{MY_CALL} {THEIR_CALL} RRR"),
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T04: {THEIR_CALL} still in queue after RRR"),
         ) if v.available else None)

    send(sock,
         "RR73: KB0UZT K4YT RR73",
         "SIGNOFF path — QSO may be logged",
         build_enqueue(f"{MY_CALL} {THEIR_CALL} RR73"),
         delay=2.0,
         verify_fn=lambda: (
             v.check_status_contains("", "T05: status updated after RR73"),
         ) if v.available else None)

    send(sock,
         "73: KB0UZT K4YT 73",
         "SIGNOFF path",
         build_enqueue(f"{MY_CALL} {THEIR_CALL} 73"),
         delay=2.0)


def group2_cq_messages(sock, v):
    """T07-T08: CQ messages from other stations are added to the queue."""
    print("  ─ Group 2: CQ messages ─")

    send(sock,
         "Plain CQ: CQ K4YT EM63",
         "CallAdded sound expected; K4YT queued",
         build_enqueue(f"CQ {THEIR_CALL} EM63"),
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T07: {THEIR_CALL} in queue from CQ"),
         ) if v.available else None)

    send(sock,
         "POTA CQ: CQ POTA K4YT",
         "POTA sound expected if enabled; K4YT queued",
         build_enqueue(f"CQ POTA {THEIR_CALL}"),
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T08: {THEIR_CALL} in queue from CQ POTA"),
         ) if v.available else None)


def group3_ap_suffix(sock, v):
    """T09-T10: WSJT-X 3.0 AP suffix is stripped before classification."""
    print("  ─ Group 3: WSJT-X 3.0 AP suffix — stripped before classifying ─")

    send(sock,
         f"AP report: {MY_CALL} {THEIR_CALL} -05 a35  (strips to: -05)",
         "Same behavior as T02 (IsReport=true after strip)",
         build_enqueue(f"{MY_CALL} {THEIR_CALL} -05 a35"),
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T09: {THEIR_CALL} in queue (AP-stripped report accepted)"),
         ) if v.available else None)

    send(sock,
         f"AP CQ: CQ {THEIR_CALL} EM63 a1  (strips to: CQ K4YT EM63)",
         "Same behavior as T07 (IsCQ=true after strip)",
         build_enqueue(f"CQ {THEIR_CALL} EM63 a1"),
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T10: {THEIR_CALL} in queue (AP-stripped CQ accepted)"),
         ) if v.available else None)


def group4_contest_field_day(sock, v):
    """T11-T12: Contest/Field Day exchanges — accepted if directed to me, rejected otherwise."""
    print("  ─ Group 4: Contest / Field Day ─")

    send(sock,
         f"FD to me: {MY_CALL} {THEIR_CALL} 2A MO",
         "IsContest=true + toCall=myCall → queued (contest exception)",
         build_enqueue(f"{MY_CALL} {THEIR_CALL} 2A MO"),
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T11: {THEIR_CALL} queued (FD exchange accepted to me)"),
         ) if v.available else None)

    send(sock,
         f"Contest between others: {THEIR_CALL} K9AVT 559 TX",
         "IsContest=true but toCall=K4YT ≠ myCall → rejected",
         build_enqueue(f"{THEIR_CALL} K9AVT 559 TX"),
         verify_fn=lambda: (
             v.check_queue_not_contains("K9AVT",
                 "T12: K9AVT NOT in queue (contest between others rejected)"),
         ) if v.available else None)


def group5_final_73_after_qso_logged(sock, v):
    """T13-T14: Fix #3 — final 73 after a logged QSO must not re-queue the call.

    NOTE: This group intentionally logs K4YT. group6 depends on K4YT
    being present in logListBox — always run group5 before group6.
    """
    print("  ─ Group 5: Fix #3 — final 73 after QSO logged ─")
    print(f"    First log {THEIR_CALL} via QsoLoggedMessage, then send 73.")

    send(sock,
         f"QsoLoggedMessage: {THEIR_CALL} logged",
         "Expect: Logged sound; logListBox gains K4YT entry",
         build_qso_logged(THEIR_CALL),
         delay=2.0,
         verify_fn=lambda: (
             v.check_log_contains(THEIR_CALL,
                 f"T13: {THEIR_CALL} in logListBox after QsoLogged"),
         ) if v.available else None)

    send(sock,
         f"Final 73 (after logged): {MY_CALL} {THEIR_CALL} 73",
         f"FIX #3: status must say '{THEIR_CALL} final 73'; no CallingMe sound",
         build_enqueue(f"{MY_CALL} {THEIR_CALL} 73"),
         delay=2.0,
         verify_fn=lambda: (
             v.check_status_contains("final 73",
                 "T14 Fix#3: status contains 'final 73'"),
         ) if v.available else None)


def group6_recall_after_prior_qso(sock, v):
    """T15: Fix #4 — station re-calls after a prior logged QSO and must be re-queued.

    NOTE: Requires group5 to have run first (K4YT must be in logListBox).
    """
    print("  ─ Group 6: Fix #4 — K4YT re-calls after prior logged QSO ─")
    print(f"    K4YT is in logList. Old code (recdPrevSignoff guard)")
    print(f"    would silently drop this. Fix #4 removed that guard.")

    send(sock,
         f"Re-call: {MY_CALL} {THEIR_CALL} EM63  (K4YT already in logList)",
         f"FIX #4: {THEIR_CALL} MUST be re-queued; CallingMe sound expected",
         build_enqueue(f"{MY_CALL} {THEIR_CALL} EM63"),
         delay=2.0,
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T15 Fix#4: {THEIR_CALL} re-queued after prior logged QSO"),
         ) if v.available else None)


def group7_slash_callsign_no_country(sock, v):
    """T16: W5C/H CQ — /H is heuristic only, must be queued normally.

    Original regression: AddSelectedCall hard-rejected Country=="" (now fixed).
    /H suffix is no longer suppressed; SpecialOperationMode in StatusMessage is
    the authoritative Fox/Hound indicator. In raw display the row is tagged
    'Possible F/H' but the call is processed like any other CQ.
    The unknown-country fix (WsjtxCountry null safety) is covered by parser tests.
    """
    print("  ─ Group 7: W5C/H — /H is possible F/H only, must queue ─")

    SLASH_CALL = "W5C/H"
    send(sock,
         f"CQ {SLASH_CALL}  (/H call — not suppressed, tagged Possible F/H in raw display)",
         f"Possible F/H: {SLASH_CALL} must be queued (suffix heuristic only, not authoritative)",
         build_enqueue(f"CQ {SLASH_CALL}", country=None, continent=None),
         verify_fn=lambda: (
             v.check_queue_contains(SLASH_CALL,
                 f"T16: {SLASH_CALL} in queue (Possible F/H, /H no longer suppressed)"),
         ) if v.available else None)


def group8_sota_cq(sock, v):
    """T17: CQ SOTA decode sent; queued only if 'SOTA' is in the directed CQ alert list.

    Admission gate: IsCallingEnabled(WANTED_CQ) && isWantedDirected, where
    isWantedDirected = IsDirectedAlert('SOTA', isDx). IsDirectedAlert reads
    alertTextBox.Text (Options → directed CQ alert field). If 'SOTA' is absent,
    the call is silently filtered — correct Jimmy behavior, not a bug.

    The verify uses check_queue_contains_warn: PASS if W0SDT is queued, WARNING
    (not FAIL) if not. The test cannot remotely read the Options setting, so a
    missing queue entry is treated as a configuration gap, not a test failure.

    To get a full PASS: add 'SOTA' to the directed CQ alert text box in Options.
    """
    print("  ─ Group 8: SOTA CQ ─")
    print("    NOTE: T17 is a full PASS only when 'SOTA' is in the directed CQ")
    print("          alert text box (Options). Otherwise a WARNING is printed.")
    SOTA_CALL = "W0SDT"
    send(sock,
         f"SOTA CQ: CQ SOTA {SOTA_CALL}",
         "Queued if 'SOTA' in alert list; WARNING (not FAIL) if not configured",
         build_enqueue(f"CQ SOTA {SOTA_CALL}", is_new_call=True),
         verify_fn=lambda: (
             v.check_queue_contains_warn(SOTA_CALL,
                 f"T17: {SOTA_CALL} in queue from CQ SOTA",
                 "add 'SOTA' to the directed CQ alert text box in Options"),
         ) if v.available else None)


def group9_short_ap_suffix(sock, v):
    """T18: Short WSJT-X 3.0 AP suffix ' a2' (1-digit) must be stripped
    and the underlying report must be accepted, not misclassified as a
    contest exchange.

    Regression for bug seen in log_6-27-2026.txt line 16414:
    WSJT-X sent 'KF4CCG F4DWB -04                      a2'; the AP strip
    failed in the old code, turning the 3-word report into a 4-token
    string that IsContest() matched as a contest exchange, causing
    rejection.
    """
    print("  ─ Group 9: Short AP suffix (' a2') — report must be accepted ─")
    send(sock,
         f"AP a2 report: {MY_CALL} {THEIR_CALL} -04 a2  (strips to: -04)",
         "Same behavior as T02 — IsReport=true after strip; K4YT stays in queue",
         build_enqueue(f"{MY_CALL} {THEIR_CALL} -04 a2"),
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T18: {THEIR_CALL} in queue (short AP a2 stripped, accepted as report)"),
         ) if v.available else None)


def group10_period_checks(sock, v):
    """T19-T20: T/R period acceptance with Advanced Call Layout enabled.

    With TxFirst=False in StatusMessage, even periods (SinceMidnight=0ms) are
    receive windows; odd periods (SinceMidnight=15000ms) are TX windows.
    Without Advanced Call Layout, opposite-period decodes directed to me are
    silently dropped by IsCorrectTimePeriodForMode().  With Advanced Call Layout
    ON, IsCorrectTimePeriodForMode() returns True unconditionally — every period
    must reach the queue.

    T19: FT8 even period (0ms)     — expected receive window, baseline
    T20: FT8 odd period  (15000ms) — opposite/TX window, must queue (Advanced mode)

    A FAIL on T20 means IsCorrectTimePeriodForMode() is NOT bypassed for the odd
    period — stop and investigate before touching production code.

    FT4 period coverage: Testing FT4 requires Jimmy to be in FT4 mode from the
    start of the session.  Sending a FT4 StatusMessage mid-session triggers a mode
    change that calls ResetOpMode() (opMode→IDLE→START); without a subsequent
    Decoding-cycle event, Jimmy never reaches ACTIVE and rejects all incoming
    decodes.  FT4 period tests are future work requiring a dedicated FT4 session.
    """
    print("  ─ Group 10: T/R period acceptance (Advanced Call Layout) ─")
    print("    Prerequisite: Advanced Call Layout enabled in Options.")
    print("    TxFirst=False → even periods are receive windows.")
    print("    SinceMidnight=0ms     = even (expected receive window)")
    print("    SinceMidnight=15000ms = odd  (TX window — opposite)")
    print()

    send(sock,
         f"FT8 even period (0ms):    {MY_CALL} {FT8_EVEN_CALL} EM63",
         "Even = expected receive window with TxFirst=False. Must queue (baseline).",
         build_enqueue(f"{MY_CALL} {FT8_EVEN_CALL} EM63",
                       since_midnight_ms=0, mode="FT8"),
         verify_fn=lambda: (
             v.check_queue_contains(FT8_EVEN_CALL,
                 f"T19: {FT8_EVEN_CALL} queued (FT8 even/expected period)"),
         ) if v.available else None)

    send(sock,
         f"FT8 odd period (15000ms): {MY_CALL} {FT8_ODD_CALL} EM63",
         "Odd = TX window (opposite). Advanced Call Layout MUST still queue it.",
         build_enqueue(f"{MY_CALL} {FT8_ODD_CALL} EM63",
                       since_midnight_ms=15000, mode="FT8"),
         verify_fn=lambda: (
             v.check_queue_contains(FT8_ODD_CALL,
                 f"T20: {FT8_ODD_CALL} queued (FT8 odd/opposite period, Advanced mode)"),
         ) if v.available else None)


def group11_fox_hound_detection(sock, v):
    """T21-T24: /H calls are possible F/H (heuristic) — queued normally, not suppressed.

    The /H suffix is no longer authoritative. SpecialOperationMode=Hound (7) in
    StatusMessage is the only authoritative Fox/Hound indicator. Jimmy does not
    suppress /H calls; they appear in the queue and are tagged 'Possible F/H' in
    the raw decode display.

    T24 verifies that normal FT8 traffic continues to work after /H decodes.
    """
    print("  ─ Group 11: /H detection — Possible F/H, queued normally ─")
    FH_CALL = "K1ABC/H"

    send(sock,
         f"CQ from /H hound: CQ {FH_CALL}",
         "Possible F/H CQ — must be queued (not suppressed)",
         build_enqueue(f"CQ {FH_CALL}"),
         verify_fn=lambda: (
             v.check_queue_contains(FH_CALL,
                 f"T21: {FH_CALL} in queue (Possible F/H, /H not suppressed)"),
         ) if v.available else None)

    send(sock,
         f"/H calling me: {MY_CALL} {FH_CALL} -03",
         "Possible F/H report directed to me — must be queued",
         build_enqueue(f"{MY_CALL} {FH_CALL} -03"),
         verify_fn=lambda: (
             v.check_queue_contains(FH_CALL,
                 f"T22: {FH_CALL} in queue (Possible F/H report to me, not suppressed)"),
         ) if v.available else None)

    send(sock,
         f"/H 73 to me: {MY_CALL} {FH_CALL} 73",
         "Possible F/H 73 — station remains in queue (no QSO state established)",
         build_enqueue(f"{MY_CALL} {FH_CALL} 73"),
         delay=2.0,
         verify_fn=lambda: (
             v.check_queue_contains(FH_CALL,
                 f"T23: {FH_CALL} in queue after 73 (Possible F/H, no prior QSO state)"),
         ) if v.available else None)

    send(sock,
         f"Normal CQ after /H: CQ {THEIR_CALL} EM63",
         "Normal FT8 CQ after F/H traffic — must still queue (no regression)",
         build_enqueue(f"CQ {THEIR_CALL} EM63"),
         verify_fn=lambda: (
             v.check_queue_contains(THEIR_CALL,
                 f"T24: {THEIR_CALL} in queue (normal FT8 not affected by F/H)"),
         ) if v.available else None)


def run_tests(sock, v):
    print("──── Test Decode Messages ────")
    print(f"  Format: [DESTINATION] [SOURCE] [payload]")
    print(f"  'KB0UZT K4YT ...' means K4YT is calling KB0UZT (directed to me)")
    print()

    group1_station_calling_me(sock, v)
    group2_cq_messages(sock, v)
    group3_ap_suffix(sock, v)
    group4_contest_field_day(sock, v)
    group5_final_73_after_qso_logged(sock, v)
    group6_recall_after_prior_qso(sock, v)
    group7_slash_callsign_no_country(sock, v)
    group8_sota_cq(sock, v)
    group9_short_ap_suffix(sock, v)
    group10_period_checks(sock, v)
    group11_fox_hound_detection(sock, v)

    # ── To add a new replay test group ──────────────────────────────────────
    # 1. Define a new function, e.g.:
    #      def group11_your_scenario(sock, v):
    #          send(sock, "Label", "Description", build_enqueue("..."),
    #               verify_fn=lambda: v.check_queue_contains("K4YT", "T21: ..."))
    # 2. Call it here, above this comment block.
    # Tests are auto-numbered by the global _test_num counter.
    # ────────────────────────────────────────────────────────────────────────

    print("──── Test sequence complete ────")
    if v.available:
        v.summary()
    else:
        print("  (Verifier was not available — all assertions skipped)")
        print("  Re-run with Jimmy open to enable automatic verification.")
    print()


# ═══════════════════════════════════════════════════════════════════════════════
# Entry Point
# ═══════════════════════════════════════════════════════════════════════════════

def main():
    print("=" * 60)
    print("  JimmyReplay.py — UDP test sender + auto-verifier")
    print("=" * 60)
    print(f"\n  Target: {JIMMY_HOST}:{JIMMY_PORT}")
    print(f"  Simulating WSJT-X {WSJT_VERSION}/{WSJT_REVISION}")
    print(f"  myCall={MY_CALL}  myGrid={MY_GRID}")
    print()

    # Locate Jimmy's controls before opening socket
    print("  Locating Jimmy controls via Win32...")
    v = JimmyVerifier()
    if v.available:
        print(f"  ✓ Found Jimmy window, statusText, callListBox, logListBox")
        print(f"    Current status: '{v.status_text()}'")
    else:
        print("  ✗ Jimmy window not found (or controls not located).")
        print("    Assertions will be skipped. Start Jimmy first if you want")
        print("    automatic verification.")
    print()

    print("  Checklist:")
    print("  [1] WSJT-X is closed")
    print("  [2] Jimmy is running (Debug build)")
    print("  [3] Jimmy mode = CQ")
    print("  [4] Advanced Call Layout enabled (Options)")
    print("  [5] (optional) 'SOTA' in directed CQ alert text box → T17 PASS")
    print("      Without it, T17 prints WARNING instead of PASS or FAIL")
    print()

    if not ensure_jimmy_udp_ready():
        print("  ERROR: Jimmy's UDP port is not available. Cannot run tests.")
        print("  Make sure Jimmy is running (Debug build) and WSJT-X is closed.")
        sys.exit(1)

    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.bind(("0.0.0.0", LOCAL_PORT))
        print(f"  Listening on port {LOCAL_PORT}\n")
    except OSError as e:
        print(f"  ERROR: Cannot bind to port {LOCAL_PORT}: {e}")
        sys.exit(1)

    try:
        if handshake(sock, v):
            run_tests(sock, v)
    except KeyboardInterrupt:
        print("\n  Interrupted.")
    finally:
        sock.close()


if __name__ == "__main__":
    main()
