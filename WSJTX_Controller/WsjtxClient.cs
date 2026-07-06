//to-do
//- 
//
//NOTE CAREFULLY: Several message classes require the use of a slightly modified WSJT-X program.
//Further information is in the README file.

using System;
//using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WsjtxUdpLib.Messages;
using WsjtxUdpLib.Messages.Out;

namespace WSJTX_Controller
{
    public class WsjtxClient : IDisposable
    {
        public Controller ctrl;
        // View seams (Phase 2.3/2.4 first wave) -- currently just point at ctrl (Controller
        // implements all three), but let ShowStatus/ShowQueue/ShowLogged stop touching ctrl's
        // controls directly. Kept alongside `ctrl` rather than replacing it outright: most of
        // WsjtxClient's ~450 other ctrl. touches are not migrated yet (see modernization plan).
        public IJimmyStatusView StatusView;
        public IJimmyQueueView QueueView;
        public IJimmyLogView LogView;
        public bool altListPaused = false;
        public UdpClient udpClient;
        public int port;
        public IPAddress ipAddress;
        public bool multicast;
        public bool overrideUdpDetect;
        public bool debug;
        public string pgmName;
        public string pgmVer;
        public bool diagLog = false;
        public bool cqPaused = true;
        public int offsetLoLimit = 300;
        public int offsetHiLimit = 2800;
        public bool useRR73 = false;                //applies to non-FT4 modes

        private string nl = Environment.NewLine;

        private List<string> acceptableWsjtxVersions = new List<string> { "2.7.0/204", "3.0.0-rc1/102", "3.0.0-rc1/103" };
        private List<string> supportedModes = new List<string>() { "FT8", "FT4" };

        public int maxPrevTo = 2;
        public int maxPrevPotaTo = 4;
        public int maxAutoGenEnqueue = 4;
        public int holdMaxTxRepeat = 50;
        public bool suspendComm = false;
        public string myCall = null, myGrid = null, myContinent = null;
        public bool cmdPrompts = true;
        public bool tuning = false;
        // new: optional INI-controlled order for call-waiting row fields.
        public List<string> callWaitingRowOrderFields = new List<string> { "tag", "pri", "country", "callp", "snr", "distAz" };

        private StreamWriter logSw = null;
        private StreamWriter potaSw = null;
        private bool settingChanged = false;
        private string cmdCheck = "";
        private bool commConfirmed = false;
        private Dictionary<string, EnqueueDecodeMessage> callDict = new Dictionary<string, EnqueueDecodeMessage>();
        private Queue<string> callQueue = new Queue<string>();
        private List<string> sentReportList = new List<string>();
        private List<string> sentCallList = new List<string>();
        private Dictionary<string, List<EnqueueDecodeMessage>> allCallDict = new Dictionary<string, List<EnqueueDecodeMessage>>();            //all calls to this station plus CQs (and replies: grids) processed
        private Dictionary<string, EnqueueDecodeMessage> lastCallActivity = new Dictionary<string, EnqueueDecodeMessage>();   //most recent decode seen from each call, unfiltered (unlike allCallDict) -- used by IsStationBusyElsewhere()
        private Dictionary<string, int> timeoutCallDict = new Dictionary<string, int>();    //calls sent to myCall immed after timeout
        private List<string> blockList = new List<string>();
        private List<string> unwantedCqList = new List<string>();      //caller is unwanted directed CQ
        private List<EnqueueDecodeMessage> _rawDecodeHistory = new List<EnqueueDecodeMessage>();
        // Retained display snapshots for advanced TX1/TX2 lists.
        // Only rebuilt by AddCall (for that call's side) and global clears.
        // Never rebuilt by RemoveCall/TrimCallQueue so the display persists across
        // opposite-side decode periods.
        private List<string> _tx1SnapshotRows  = new List<string>();
        private List<string> _tx1SnapshotCalls = new List<string>();
        private List<string> _tx2SnapshotRows  = new List<string>();
        private List<string> _tx2SnapshotCalls = new List<string>();
        // Maps each normal-list display row index to its true callQueue position.
        // Rebuilt by ShowQueue whenever callInProg is filtered from the visible rows.
        private List<int> _callListBoxQueueIndices = new List<int>();
        private bool _lastAddCallCategoryPlayed;
        private Dictionary<string, DateTime> _wantedAnywhereAlertTimes   = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, DateTime> _oppositePeriodAlertTimes    = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, DateTime> _awardAlertTimes             = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private const int WantedAnywhereAlertCooldownSecs  = 30;
        private const int OppositePeriodAlertCooldownSecs  = 45;
        private const int AwardAlertCooldownSecs           = 30;
        private bool txEnabled = false;
        private bool txEnabledConf = false;
        private bool wsjtxTxEnableButton = false;
        private bool transmitting = false;
        private bool decoding = false;
        private WsjtxMessage.QsoStates qsoState = WsjtxMessage.QsoStates.CALLING;
        private WsjtxMessage.QsoStates qsoStateConf = WsjtxMessage.QsoStates.CALLING;
        private string mode = "";
        private bool modeSupported = true;
        private bool txFirst = false;
        private int? trPeriod = null;       //msec
        private ulong dialFrequency = 0;
        private int? bandIdx = null;
        private List<int> bands = new List<int>() { 160, 80, 60, 40, 30, 20, 17, 15, 12, 10, 6 };
        private Dictionary<string, List<int>> freqsDict = new Dictionary<string, List<int>>(){
            {"FT8", new List<int>(){ 1840, 3573, 5357, 7074, 10136, 14074, 18100, 21074, 24915, 28074, 50313 }},
            {"FT4", new List<int>(){ 1840, 3575, 5357, 7047, 10140, 14080, 18104, 21140, 24919, 28180, 50318 }}};
        private UInt32 txOffset = 0;
        private string replyCmd = null;     //no "reply to" cmd sent to WSJT-X yet, will not be a CQ
        private string curCmd = null;       //cmd last issed, can be CQ
        private EnqueueDecodeMessage replyDecode = null;
        private string configuration = null;
        public string callInProg = null;
        private bool restartQueue = false;

        private WsjtxMessage.QsoStates lastQsoState = WsjtxMessage.QsoStates.INVALID;
        private UdpClient udpClient2;
        private IPEndPoint endPoint;
        private bool? lastXmitting = null;
        private bool? lastTxWatchdog = null;
        private string dxCall = null;
        private string lastMode = null;
        private ulong? lastDialFrequency = null;
        private bool? lastTxFirst = null;
        private bool? lastDecoding = null;
        private int? lastSpecOp = null;
        private string lastTxMsg = null;
        private bool? lastTxEnabled = null;
        private string lastCallInProgDebug = null;
        private bool? lastTxTimeoutDebug = null;
        private string lastReplyCmdDebug = null;
        private WsjtxMessage.QsoStates lastQsoStateDebug = WsjtxMessage.QsoStates.INVALID;
        private string lastDxCallDebug = null;
        private string lastTxMsgDebug = null;
        private string lastLastTxMsgDebug = null;
        private bool lastTransmittingDebug = false;
        private bool lastRestartQueueDebug = false;
        private bool lastTxFirstDebug = false;

        private string lastDxCall = null;
        private int xmitCycleCount = 0;
        private bool txTimeout = false;
        private bool newDirCq = false;
        private int specOp = 0;
        private string lCall = null;            //last call sign logged
        private string tCall = null;            //call sign being processed at timeout or completed
        private string txMsg = null;            //msg for the most-recent Tx
        private List<string> logList = new List<string>();      //calls logged for current mode/band for this session

        // WSJT-X sends both QsoLoggedMessage and LoggedAdifMessage for every logged QSO.
        // Either one alone is enough to record the QSO and refresh awards (see
        // HandleLiveQsoLogged/HandleLiveAdifLogged) -- this set makes sure that when both
        // normally arrive, the second one is a no-op instead of double-processing. Keyed by
        // the same callsign/band/mode/date/time dedup key LogbookDb already uses (NOT the
        // protocol's "Id" field -- that's WSJT-X's fixed per-instance identifier, the same
        // value on every message, not a per-QSO key).
        private readonly HashSet<string> _liveLoggedQsoKeys = new HashSet<string>();
        private bool ClaimLiveLoggedQso(string dedupKey) =>
            string.IsNullOrEmpty(dedupKey) || _liveLoggedQsoKeys.Add(dedupKey);
        private Dictionary<string, List<string>> potaLogDict = new Dictionary<string, List<string>>();      //calls logged for any mode/band for this day: "call: date,band,mode"

        private AsyncCallback asyncCallback;
        private UdpState udpSt;
        private static bool messageRecd;
        private static byte[] datagram;
        private static IPEndPoint fromEp = new IPEndPoint(IPAddress.Any, 0);
        private static bool recvStarted;
        private static uint defaultAudioOffset = 1500;
        private string failReason = "Failure reason: Unknown";
        public static int wsjtxRevision;
        public static int wsjtxTestVer;
        public static int lastWsjtx270RcRevision = 185;

        public const int maxQueueLines = 8, maxQueueWidth = 19, maxLogWidth = 9;
        private byte[] ba;
        private EnableTxMessage emsg;
        private WsjtxMessage msg = new UnknownMessage();
        private Random rnd = new Random();
        DateTime firstDecodeTime;
        private const string spacer = "           *";
        private const int freqChangeThreshold = 200;
        private bool skipFirstDecodeSeries = true;
        private System.Windows.Forms.Timer postDecodeTimer = new System.Windows.Forms.Timer();
        private System.Windows.Forms.Timer processDecodeTimer = new System.Windows.Forms.Timer();
        private System.Windows.Forms.Timer processDecodeTimer2 = new System.Windows.Forms.Timer();
        private System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();
        private System.Windows.Forms.Timer statusTimer2 = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer cmdCheckTimer = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer dialogTimer2 = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer dialogTimer3 = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer heartbeatRecdTimer = new System.Windows.Forms.Timer();
        private bool _requireOffsetForActive = false;   // true after a band change; keeps CheckActive from firing before WSJT-X settles
        private string path = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\{Assembly.GetExecutingAssembly().GetName().Name.ToString()}";
        private List<int> audioOffsets = new List<int>();
        private int oddOffset = 0;
        private int lastOddOffsetDebug = 0;
        private int evenOffset = 0;
        private int lastEvenOffsetDebug = 0;
        public int cachedOddOffset = 0;
        public int cachedEvenOffset = 0;
        private bool analysisCompleted = false;
        private bool pendingCqAfterAnalysis = false;
        private string cancelledCall = null;
        private int maxTxRepeat = 4;
        private bool _manualCallInProg = false;
        // holdTxRepeat removed — manual Hold now always uses holdMaxTxRepeat.
        private string curVerBld = null;
        private int consecCqCount = 0;
        private int lastConsecCqCountDebug = 0;
        private const int maxConsecCqCount = 8;
        private int consecTimeoutCount = 0;
        private int lastConsecTimeoutCount = 0;
        private const int maxConsecTimeoutCount = 10;
        private int consecTxCount = 0;
        private int lastConsecTxCountDebug = 0;
        private bool lastPausedDebug = true;
        private const int maxConsecTxCount = 12;
        private const uint tuningAudioOffset = 3200;
        private uint prevOffset = defaultAudioOffset;

        public NotificationSounds Sounds;
        public LiveQsoUploadOrchestrator LiveQsoUploader;
        bool wsjtxClosing = false;
        const int heartbeatInterval = 15;           //expected recv interval, sec
        string toCallTxStart = null;
        DateTime txBeginTime = DateTime.MaxValue;
        bool shortTx = false;
        bool txInterrupted = false;
        bool metricUnits = false;
        private int decodeNum = 0;
        private const int maxCheckTxRepeat = 2;
        private int decodeCycle = 0;
        private bool decodesProcessed = false;
        private bool debugDetail = false;
        private int maxDecodeAgeMinutes = 15;
        public TxModes txMode;
        public bool usePskReporter = true;
        public bool rawPriorityTags = false;
        public LookupManager lookupManager;
        public bool lotwBoostEnabled = false;
        private TxModes lastTxModeDebug;
        private string discardCall = null;
        private string expiredCall = null;
        private int maxCallQueueAgePeriods = 16;
        private int discardCallCycleCount = 0;

        //for status display only
        private string curTxPayload = null;
        private string loggedCall = null;
        private string finalSignoffCall = null;
        private bool modePrompt = true;
        private bool replyFromInProg = false;
        private bool deletedAllCalls = false;
        private bool newTxFirst = false;
        private bool? lastCallListTxFirst = null;
        private bool newBand = false;
        private bool newMode = false;
        private string uploadResult = null;
        private string tuneResult = null;
        private string lastStatusTxMsg = null;
        private string timedOutCall = null;
        private string curTxMsg = null;
        private bool newSelection = false;
        private int wsjtxResultCode = 0;
        private string statusDetail = null;
        private int lastWsjtxResultCode = 0;
        private int decodeCount = 0;
        private int consecNoDecodes = 0;
        private int maxNoDecodes = 4;
        private List<double> timeOffsets = new List<double>();
        private double timeOffset = 0;
        private double maxTimeOffset = 1.20;
        private bool txEnableChanged = false;
        private bool promptsChanged = false;
        private string toCallStatus = null;
        private string callInProgLastActivity = null;
        private bool newPskReporter = false;

        public static bool IsWsjtx270Rc()
        {
            return WSJTX_Controller.WsjtxClient.wsjtxRevision == WSJTX_Controller.WsjtxClient.lastWsjtx270RcRevision;
        }

        private struct UdpState
        {
            public UdpClient u;
            public IPEndPoint e;
        }

        public enum TxModes
        {
            LISTEN,
            CALL_CQ
        }

        private enum OpModes
        {
            IDLE,
            START,
            ACTIVE
        }
        private OpModes opMode;

        public enum CallPriority
        {
            RESERVED,
            NEW_COUNTRY,            //1
            NEW_COUNTRY_ON_BAND,    //2
            TO_MYCALL,              //3
            MANUAL_SEL,             //4
            WANTED_CQ,              //5
            DEFAULT                 //6
        }

        // Ranking category: what type of call this is for queue-ordering purposes.
        // Separate from CallPriority so behavioral checks (aging, hold, logging, RR73,
        // admission gates) remain tied to CallPriority and are not affected by
        // user-configurable ranking weights.
        //
        // DEFAULT = 0 is intentional: an uninitialized Category field on a new
        // EnqueueDecodeMessage is safe — it ranks as DEFAULT (lowest tier) rather
        // than accidentally ranking as NEW_COUNTRY (highest tier).
        public enum CallCategory
        {
            DEFAULT = 0,         // all other calls — safe uninitialized value
            NEW_COUNTRY,         // 1 — maps from Priority NEW_COUNTRY (1)
            NEW_COUNTRY_ON_BAND, // 2 — maps from Priority NEW_COUNTRY_ON_BAND (2)
            TO_MYCALL,           // 3 — maps from Priority TO_MYCALL (3)
            MANUAL_SEL,          // 4 — maps from Priority MANUAL_SEL (4)
            WANTED_CQ,           // 5 — maps from Priority WANTED_CQ (5)
            POTA,                // 6 — DEFAULT priority + IsPotaCall
            SOTA,                // 7 — DEFAULT priority + IsSotaCall
            ALWAYS_WANTED,       // 8 — matches user-defined wanted callsign list
            WAS_NEEDED,          // 9 — US state needed for WAS award (HRC database)
            DXCC_UNCONFIRMED,    // 10 — DXCC entity worked but unconfirmed (HRC database)
            ZONE_NEEDED,         // 11 — CQ zone needed for WAZ award (HRC database)
            STILL_NEEDED,        // 12 — matches the Rule Definition selected in the Still Need tab
        }

        public enum RankMethods
        {
            //"sort order"
            CALL_ORDER,
            MOST_RECENT,
            DIST_INCR,
            DIST_DECR,
            SNR_INCR,
            SNR_DECR,
            AZ_NQUAD,   //AZ order important
            AZ_NEQUAD,
            AZ_EQUAD,
            AZ_SEQUAD,
            AZ_SQUAD,
            AZ_SWQUAD,
            AZ_WQUAD,
            AZ_NWQUAD   //AZ order important
        }
        // Ranking config (category weights, calling priorities, sort/rank-method state) now
        // lives in Ranker (CallQueueRanker.cs) so it's unit-testable outside a live Form.
        public CallQueueRanker Ranker = new CallQueueRanker();

        // User-defined always-wanted callsigns. Calls matching this set get ALWAYS_WANTED category.
        public HashSet<string> wantedCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // HRC database caches — populated from the local Ham Radio Center database at startup,
        // after each import, and after each band change.  All lookups are in-memory.
        public HashSet<string> hrcNeededStates    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<int>    hrcUnconfirmedDxcc = new HashSet<int>();
        public HashSet<int>    hrcNeededZones     = new HashSet<int>();

        // One entry per Rule Definition currently checked for live tagging in the
        // Logbook window's Still Need tab (see Controller.RefreshStillNeedCache()).
        // Independent of the fixed HRC sets above: this supports any enabled Rule
        // Definition, not just WAS/DXCC/WAZ, and several can be active at once.
        // Only GroupBy kinds with a fast decode-time field are usable (see
        // RuleEngine.SupportsLiveTag). A rule is simply absent from this dictionary
        // whenever it isn't checked, its GroupBy isn't one of those kinds, or it has
        // no fixed still-needed checklist (e.g. Target=COUNT/LEVELS awards never
        // produce one).
        public class ActiveAwardTag
        {
            public string          RuleId;
            public string          RuleName;
            public RuleGroupBy     GroupBy;
            public HashSet<string> Set;
        }
        public Dictionary<string, ActiveAwardTag> activeAwardTags = new Dictionary<string, ActiveAwardTag>();

        // Returns the ADIF-style band string for the current dial frequency (e.g. "20m"),
        // or null when the frequency is unknown or off-band.
        public string CurrentBandStr => FreqToBandStr(dialFrequency / 1e6);

        // Replace the entire categoryWeight table (loaded from INI or set by Phase 4 UI).
        // Validates that all entries are present and DEFAULT is 0 (delegated to Ranker).
        public void ApplyCategoryWeights(Dictionary<CallCategory, int> weights)
        {
            if (!Ranker.ApplyCategoryWeights(weights)) return;
            if (debug) DebugOutput($"{Time()} ApplyCategoryWeights: {string.Join(", ", Ranker.categoryWeight.Select(kv => $"{kv.Key}={kv.Value}"))}");
            SortCalls();
        }

        // Apply calling priorities (loaded from INI or set by dialog).
        // Order determines Alt+N category preference; membership gates admission of DEFAULT and Alt+N.
        public void ApplyCallingPriorities(List<CallCategory> enabled)
        {
            Ranker.ApplyCallingPriorities(enabled);
        }

        // POTA, SOTA, and MANUAL_SEL are hidden from the Call Filters UI; they follow the Directed CQ entry.
        private bool IsCallingEnabled(CallCategory cat) => Ranker.IsCallingEnabled(cat);

        // Apply wanted callsign list (loaded from INI or set by Phase 5 UI).
        // Re-derives categories for any existing queue entries affected.
        public void ApplyWantedCalls(HashSet<string> wanted)
        {
            wantedCalls = wanted ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Re-derive category for all queued calls so existing entries pick up changes.
            foreach (var kv in callDict)
            {
                var d = kv.Value;
                if (d.Category == CallCategory.ALWAYS_WANTED || d.Priority == (int)CallPriority.DEFAULT)
                {
                    d.Category = DeriveCategory(d);
                    Ranker.SetRank(d, debug ? (Action<string>)(s => DebugOutput($"{spacer}{s}")) : null);
                }
            }
            SortCalls();
        }

        private enum Periods
        {
            UNK,
            ODD,
            EVEN
        }
        private Periods period;

        private enum autoFreqPauseModes
        {
            DISABLED,
            ENABLED,
            ACTIVE
        }
        private autoFreqPauseModes autoFreqPauseMode;
        private autoFreqPauseModes lastAutoFreqPauseModeDebug = autoFreqPauseModes.DISABLED;

        public enum ListenModeTxPeriods
        {
            EVEN,
            ODD,
            ANY
        }

        public enum NewCallBands
        {
            ANY,
            CURRENT
        }

        public enum WsjtxResultCodes
        {
            NONE,
            LOTW_UPL,
            PWR_SWR_RPT,
            PWR_SWR_END,
            PWR_SWR_SINGLE_RPT
        }

        public WsjtxClient(Controller c, IPAddress reqIpAddress, int reqPort, bool reqMulticast, bool reqOverrideUdpDetect, bool reqDebug, bool reqLog, WsjtxClient.TxModes tMode)
        {
            ctrl = c;           //used for accessing/updating UI
            StatusView = c;
            QueueView = c;
            LogView = c;
            Sounds = new NotificationSounds(() => ctrl.soundsEnabled);
            LiveQsoUploader = new LiveQsoUploadOrchestrator(
                credentials: () => new LiveUploadCredentials
                {
                    QrzUploadEnabled = ctrl.qrzUploadEnabled,
                    QrzUploadRealtime = ctrl.qrzUploadRealtime,
                    QrzLogbookApiKey = ctrl.qrzLogbookApiKey,
                    ClubLogUploadEnabled = ctrl.clubLogUploadEnabled,
                    ClubLogUploadRealtime = ctrl.clubLogUploadRealtime,
                    ClubLogUploadEmail = ctrl.clubLogUploadEmail,
                    ClubLogUploadPassword = ctrl.clubLogUploadPassword,
                    ClubLogUploadCallsign = ctrl.clubLogUploadCallsign,
                },
                notifyImported: () => ctrl.BeginInvoke(new Action(() =>
                {
                    ctrl.RefreshStillNeedCache();
                    ctrl.RefreshLogbookWindowIfOpen();
                })),
                debugLog: msg => DebugOutput($"{Time()} {msg}"));
            ipAddress = reqIpAddress;
            port = reqPort;
            multicast = reqMulticast;
            overrideUdpDetect = reqOverrideUdpDetect;
            txMode = tMode;
            //major.minor.build.private
            string allVer = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
            Version v;
            Version.TryParse(allVer, out v);
            string fileVer = $"{v.Major}.{v.Minor}";
            pgmVer = WsjtxMessage.PgmVersion = fileVer;
            debug = reqDebug;
            opMode = OpModes.IDLE;              //wait for WSJT-X running to read its .INI
            WsjtxMessage.NegoState = WsjtxMessage.NegoStates.INITIAL;
            ctrl.Text = pgmName = Assembly.GetExecutingAssembly().GetName().Name.ToString();

            if (reqLog)            //request log file open
            {
                diagLog = SetLogFileState(true);
                if (diagLog)
                {
                    DebugOutput($"{nl}{nl}{nl}{DateTime.UtcNow.ToString("yyyy-MM-dd HHmmss")} UTC ###################### {pgmName} v{pgmVer} starting....");
                }
            }

            ClearAudioOffsets();
            if (ctrl.freqCheckBox.Checked) WsjtxSettingChanged();

            ResetNego();
            UpdateDebug();

            DebugOutput($"{Time()} NegoState:{WsjtxMessage.NegoState}");
            DebugOutput($"{Time()} opMode:{opMode}");
            DebugOutput($"{Time()} Waiting for WSJT-X to run...");

            ShowStatus();
            ShowQueue();
            if (ctrl.advancedCallLayout) ShowAdvancedQueue(null);
            ShowLogged();
            messageRecd = false;
            recvStarted = false;

            ctrl.verLabel.Text = $"by WM8Q v{fileVer}";
            ctrl.verLabel2.Text = "Check for update";

            UpdateModeSelection();
            UpdateModeVisible();

            emsg = new EnableTxMessage();
            emsg.Id = WsjtxMessage.UniqueId;

            firstDecodeTime = DateTime.MinValue;

            postDecodeTimer.Interval = 4000;
            postDecodeTimer.Tick += new System.EventHandler(ProcessPostDecodeTimerTick);

            processDecodeTimer.Tick += new System.EventHandler(ProcessDecodeTimerTick);

            processDecodeTimer2.Tick += new System.EventHandler(ProcessDecodeTimer2Tick);

            statusTimer.Tick += new System.EventHandler(StatusTimerTick);

            statusTimer2.Tick += new System.EventHandler(StatusTimer2Tick);       //restore previous status
            statusTimer2.Interval = 5000;

            cmdCheckTimer.Tick += new System.EventHandler(cmdCheckTimer_Tick);

            dialogTimer2.Tick += new System.EventHandler(dialogTimer2_Tick);
            dialogTimer2.Interval = 20;

            dialogTimer3.Tick += new System.EventHandler(dialogTimer3_Tick);
            dialogTimer3.Interval = 20;

            ReadPotaLogDict();

            heartbeatRecdTimer.Interval = 4 * heartbeatInterval * 1000;            //heartbeats every 15 sec
            heartbeatRecdTimer.Tick += new System.EventHandler(HeartbeatNotRecd);

            UpdateMaxTxRepeat();

            var dtNow = DateTime.UtcNow;

            UpdateBandComboBox();

            ShowLogged();

            UpdateBlockList(ctrl.exceptTextBox.Text);

            modePrompt = (txMode == TxModes.CALL_CQ);

            UpdateDebug();          //last before starting loop
        }

        public void CancelQso()
        {
            SetCallInProg(null);
            xmitCycleCount = 0;
            txTimeout = false;
            timedOutCall = null;
            ctrl.holdCheckBox.Checked = false;
        }

        // Must be called BEFORE CancelQso() so callInProg/replyDecode are still valid.
        // In advanced layout, the TX1/TX2 snapshot is not cleared by RemoveCall, so the row
        // remains visible after ReplyTo dequeues it.  Re-adding the call keeps the row backed
        // by the real queue so Enter works again.  No-op if not in advanced layout, call
        // already gone, or call is already in the queue.
        public void RequeueAbortedCall()
        {
            if (!ctrl.advancedCallLayout) return;
            if (callInProg == null || replyDecode == null) return;
            if (FindCallIndexInQueue(callInProg) >= 0) return;
            SetRank(replyDecode);
            DebugOutput($"{Time()} RequeueAbortedCall: re-enqueuing '{callInProg}'");
            AddCall(callInProg, replyDecode);
        }

        public bool EnableMode()              //cq/listen mode selected
        {
            HaltTuning();
            if (txMode == TxModes.CALL_CQ && !txEnabled)
            {
                DisableAutoFreqPause();
                consecNoDecodes = 0;

                if (callInProg != null)
                {
                    txTimeout = false;
                    xmitCycleCount = 0;
                    timedOutCall = null;
                    ClearCallTimeout(callInProg);
                    EnableTx();
                }
                else
                {
                    return false;
                }

                CancelDiscardCall();
                cqPaused = false;
                txEnableChanged = true;
                StartStatusTimer();
                Sounds.PlaySoundEvent(ctrl.soundEnabled_TxEnabled, ctrl.soundFile_TxEnabled);
                DebugOutput($"{Time()} EnableMode cqPaused:{cqPaused} txMode:{txMode} txTimeout:{txTimeout} xmitCycleCount:{xmitCycleCount}");
                UpdateDebug();
                return true;
            }

            if ((txMode == TxModes.LISTEN) && !txEnabled && callInProg != null) //listen mode, disabled or timed out
            {
                DisableAutoFreqPause();
                consecNoDecodes = 0;
                newSelection = true;
                txTimeout = false;
                xmitCycleCount = 0;
                timedOutCall = null;
                ClearCallTimeout(callInProg);
                EnableTx();
                cqPaused = false;
                modePrompt = true;
                txEnableChanged = true;
                StartStatusTimer();
                Sounds.PlaySoundEvent(ctrl.soundEnabled_TxEnabled, ctrl.soundFile_TxEnabled);
                StartDiscardCall(callInProg);
                DebugOutput($"{Time()} EnableMode txMode:{txMode} txTimeout:{txTimeout} xmitCycleCount:{xmitCycleCount}");
                UpdateDebug();
                return true;
            }

            return false;
        }

        // WSJT-X re-enabled its own "Enable Tx" button without Jimmy having asked for it --
        // most likely WSJT-X's own "Wait and Reply" feature resuming a stalled QSO after the
        // other station finally replied (see StatusMessage.TxEnableClk handling above). Mirrors
        // EnableMode()'s resume bookkeeping for the stalled callInProg, but skips re-sending
        // EnableTx() since WSJT-X already enabled itself -- and announces a distinct status
        // message so the operator can tell this was automatic, not their own action.
        private void HandleUnsolicitedTxResume()
        {
            if (callInProg == null) return;      //nothing Jimmy was waiting on to resume

            txTimeout = false;
            xmitCycleCount = 0;
            timedOutCall = null;
            ClearCallTimeout(callInProg);
            txEnabled = true;         //sync belief to match WSJT-X's actual state (no EnableTx() resend needed)
            cqPaused = false;
            UpdateMaxTxRepeat();
            StartStatusTimer();
            Sounds.PlaySoundEvent(ctrl.soundEnabled_TxEnabled, ctrl.soundFile_TxEnabled);
            StatusView.ShowMessage($"WSJT-X resumed calling {callInProg} automatically", false);
            DebugOutput($"{Time()} HandleUnsolicitedTxResume, callInProg:'{callInProg}' cqPaused:{cqPaused} txMode:{txMode}");
        }

        public void RankMethodIdxChanged(int idx)
        {
            Ranker.RankMethodIdxChanged(idx);
            DebugOutput($"{nl}{Time()} ApplySortOrder, order:{string.Join(",", Ranker.rankOrderList)} beam:{Ranker.rankBeamMethod?.ToString() ?? "none"}");
            SortCalls();
        }

        public void ApplySortOrder(List<RankMethods> orderList, RankMethods? beamMethod)
        {
            Ranker.ApplySortOrder(orderList, beamMethod);
            DebugOutput($"{nl}{Time()} ApplySortOrder, order:{string.Join(",", Ranker.rankOrderList)} beam:{Ranker.rankBeamMethod?.ToString() ?? "none"}");
            SortCalls();
        }

        private bool IsPrimarySort(RankMethods method) => Ranker.IsPrimarySort(method);

        private int CompareRank(EnqueueDecodeMessage existing, EnqueueDecodeMessage incoming) =>
            Ranker.CompareRank(existing, incoming, lookupManager != null ? (Func<string, bool>)lookupManager.IsLoTWUser : null, lotwBoostEnabled);

        public void ReplyRR73Changed(bool state)
        {
            var calls = new List<string>() { };
            if (!state)     //'reply to RR73' unchecked
            {
                //remove any RR73 messages in call queue
                foreach (var entry in callDict)
                {
                    if (entry.Value.IsRR73()) calls.Add(entry.Key);
                }
            }

            foreach (var call in calls)
            {
                RemoveCall(call);
            }
        }

        public void TxPeriodIdxChanged(int idx)
        {
            if (callQueue.Count == 0 || (txMode == TxModes.LISTEN && idx == (int)ListenModeTxPeriods.ANY)) return;
            if (ctrl.advancedCallLayout) return;

            CheckCallQueuePeriod((ListenModeTxPeriods)idx == ListenModeTxPeriods.EVEN);
        }

        //override auto IP addr, port, and/or mode with new values
        public void UpdateAddrPortMulti(IPAddress reqIpAddress, int reqPort, bool reqMulticast, bool reqOverrideUdpDetect)
        {
            ipAddress = reqIpAddress;
            port = reqPort;
            multicast = reqMulticast;
            overrideUdpDetect = reqOverrideUdpDetect;
            ResetNego();
            CloseAllUdp();
        }

        public void ReceiveCallback(IAsyncResult ar)
        {
            datagram = null;
            messageRecd = true;

            try
            {
                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT) return;
                UdpClient u = ((UdpState)(ar.AsyncState)).u;
                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT) return;
                fromEp = ((UdpState)(ar.AsyncState)).e;
                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT) return;
                datagram = u.EndReceive(ar, ref fromEp);
            }
            catch (Exception err)
            {
#if DEBUG
                Console.WriteLine($"Exception: ReceiveCallback() {err}");
#endif
                return;
            }

            //DebugOutput($"Received: {receiveString}");
        }

        public void UdpLoop()
        {
            if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT)
            {
                if (!suspendComm) CheckWsjtxRunning();            //re-init if so
                return;
            }
            else
            {
                bool notRunning = !IsWsjtxRunning();
                if (notRunning || wsjtxClosing)
                {
                    DebugOutput($"{nl}{Time()} WSJT-X notRunning:{notRunning} wsjtxClosing:{wsjtxClosing}");
                    ResetNego();
                    CloseAllUdp();
                    wsjtxClosing = false;
                    StatusView.ShowMessage("WSJT-X closed", true);
                }
            }

            //timer expires at 11-12 msec minimum (due to OS limitations)
            if (messageRecd)
            {
                if (datagram != null) Update();
                messageRecd = false;
                recvStarted = false;
            }
            // Receive a UDP datagram
            if (!recvStarted)
            {
                if (udpClient == null || WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT) return;
                udpClient.BeginReceive(asyncCallback, udpSt);
                recvStarted = true;
            }
        }

        private void CheckWsjtxRunning()
        {
            if (IsWsjtxRunning())
            {
                DebugOutput($"{nl}{Time()} WSJT-X running");
                StatusView.ShowMessage("WSJT-X detected", false);
                Thread.Sleep(3000);     //wait for WSJT-X to open UDP

                bool retry = true;
                while (retry)
                {
                    if (!overrideUdpDetect)
                    {
                        if (!DetectUdpSettings(out ipAddress, out port, out multicast))
                        {
                            DebugOutput($"{spacer}using default IP address from WSJT-X");
                            heartbeatRecdTimer.Stop();
                            suspendComm = true;
                            ctrl.BringToFront();
                            MessageBox.Show($"{pgmName} is using the default UDP IP address and port.", pgmName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            suspendComm = false;
                        }
                    }


                    DebugOutput($"{spacer}ipAddress:{ipAddress} port:{port} multicast:{multicast}");
                    string modeStr = multicast ? "multicast" : "unicast";
                    try
                    {
                        if (multicast)
                        {
                            udpClient = new UdpClient();
                            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                            udpClient.Client.Bind(endPoint = new IPEndPoint(IPAddress.Any, port));
                            udpClient.JoinMulticastGroup(ipAddress);
                        }
                        else
                        {
                            udpClient = new UdpClient(endPoint = new IPEndPoint(ipAddress, port));
                        }
                        DebugOutput($"{spacer}opened udpClient:{udpClient}");
                        retry = false;
                    }
                    catch (Exception e)
                    {
                        e.ToString();
                        DebugOutput($"{spacer}unable to open udpClient:{udpClient}{nl}{e}");
                        heartbeatRecdTimer.Stop();
                        suspendComm = true;
                        ctrl.BringToFront();
                        if (MessageBox.Show($"Unable to open WSJT-X's specified UDP port,{nl}address: {ipAddress}{nl}port: {port}{nl}mode: {modeStr}.{nl}{nl}In WSJT-X, select File | Settings | Reporting.{nl}At 'UDP Server':{nl}- Enter '239.255.0.0' for 'UDP Server{nl}- Enter '2237' for 'UDP Server port number'{nl}- Select all checkboxes at 'Outgoing interfaces'{nl}- Click 'Retry' below to try opening the UDP port again.{nl}{nl}Alternatively:{nl}- Click 'Cancel' below for {ctrl.friendlyName}'s 'Config'{nl}- Enter the UDP address and port as shown in WSJT-X, or{nl}- Select 'Override' to use auto-detected UDP settings.", pgmName, MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) == DialogResult.Cancel)
                        {
                            ctrl.OpenUdpConfig();
                            return;                 //suspendComm set to false at Options dialog close
                        }
                    }
                }
                suspendComm = false;

                udpSt = new UdpState();
                udpSt.e = endPoint;
                udpSt.u = udpClient;
                asyncCallback = new AsyncCallback(ReceiveCallback);

                WsjtxMessage.NegoState = WsjtxMessage.NegoStates.INITIAL;
                DebugOutput($"{spacer}NegoState:{WsjtxMessage.NegoState}");

                if (!suspendComm)
                {
                    ctrl.initialConnFaultTimer.Interval = 3 * heartbeatInterval * 1000;           //pop up dialog showing UDP corrective info at tick
                    ctrl.initialConnFaultTimer.Start();
                }
            }
        }

        public bool ConnectedToWsjtx()
        {
            return opMode == OpModes.ACTIVE;
        }

        public bool WsjtxConnecting()
        {
            return opMode  >= OpModes.START;
        }

        public bool ClearCallQueue()
        {
            HaltTuning();
            bool hadCallInProg = callInProg != null;
            CancelQso();                        //Ctrl+W abandons any active contact
            if (hadCallInProg) ResetTxToCq();   //prevent stale mid-QSO TX message on next enable
            if (callQueue.Count == 0) return hadCallInProg;

            callQueue.Clear();
            callDict.Clear();
            ShowQueue();
            if (ctrl.advancedCallLayout) ShowAdvancedQueue(null);
            UpdateMaxTxRepeat();
            deletedAllCalls = true;
            ShowStatus();
            return true;
        }

        public void DebugChanged()
        {
            ShowQueue();
            if (ctrl.advancedCallLayout) ShowAdvancedQueue(null);
            UpdateCallInProg();
        }

        public void AutoFreqChanged(bool autoFreqEnabled, bool bandOrModeChanged)
        {
            DisableAutoFreqPause();
            if (autoFreqEnabled)
            {
                //if (commConfirmed) EnableMonitoring();       may crash WSJT-X
                if (opMode != OpModes.ACTIVE)
                {
                    ctrl.freqCheckBox.Text = "Use best freq (pending)";
                    ctrl.freqCheckBox.ForeColor = Color.DarkGreen;
                    return;
                }
                if (oddOffset > 0 && evenOffset > 0)
                {
                    ctrl.freqCheckBox.Text = "Use best Tx frequency";
                    return;
                }

                ctrl.freqCheckBox.Text = "Use best freq (pending)";
                ctrl.freqCheckBox.ForeColor = Color.DarkGreen;

                cqPaused = true;
                StopDecodeTimers();
                DisableTx(false);
                opMode = OpModes.START;
                UpdateBandComboBox();
                if (bandOrModeChanged)
                {
                    txTimeout = false;
                    tCall = null;
                    replyCmd = null;
                    curCmd = null;
                    replyDecode = null;
                    newDirCq = false;
                    dxCall = null;
                    xmitCycleCount = 0;
                    timedOutCall = null;
                    SetCallInProg(null);
                    UpdateCallInProg();
                }
                UpdateModeVisible();
                UpdateModeSelection();
                DebugOutput($"{Time()} [BAND-AUDIT] AutoFreqChanged enabled:true, bandIdx:{bandIdx} bandOrModeChanged:{bandOrModeChanged} evenOffset:{evenOffset} oddOffset:{oddOffset} opMode:{opMode} NegoState:{WsjtxMessage.NegoState} cqPaused:{cqPaused}");
            }
            else
            {
                ctrl.freqCheckBox.Text = "Use best Tx frequency";
                ctrl.freqCheckBox.ForeColor = Color.Black;
                DebugOutput($"{Time()} [BAND-AUDIT] AutoFreqChanged enabled:false, bandIdx:{bandIdx}");
                CheckActive();
            }
            UpdateDebug();
        }

        public bool ToggleHoldCheckBox()
        {
            HaltTuning();
            if (callInProg == null) return false;

            ctrl.holdCheckBox.Checked = !ctrl.holdCheckBox.Checked;
            if (ctrl.holdCheckBox.Checked) xmitCycleCount = 0;
            return HoldCheckBoxChanged();
        }

        public bool ToggleOperatingMode()
        {
            string newMode = mode != "FT8" ? "FT8" : "FT4";
            SetOperatingMode(newMode);
            return true;
        }

        public bool TogglePskReporter()
        {
            usePskReporter = !usePskReporter;

            emsg.NewTxMsgIdx = 17;
            emsg.Param0 = usePskReporter;
            emsg.Param1 = false;        //ignored
            emsg.Offset = 0;            //ignored
            emsg.GenMsg = $"(mod by WM8Q, w/{pgmName} v{pgmVer} [FT8 for blind hams], qrz.com/db/WM8Q)";
            emsg.CmdCheck = "";         //ignored
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Set PSKReporter' cmd:17{nl}{emsg}");

            newPskReporter = true;
            ShowStatus();
            return true;
        }

        public bool SetOperatingMode(string newMode)
        {
            if (transmitting || txEnabled) HaltTx();
            if (transmitting) Thread.Sleep(250);        //radio must return to original rx freq first

            emsg.NewTxMsgIdx = 21;
            emsg.GenMsg = newMode;
            emsg.Param0 = false;
            emsg.Param1 = false;
            emsg.Param2 = 0;         //ignored
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Operating Mode' cmd:21{nl}{emsg}");

            return true;
        }

        public bool TogglePrompts()
        {
            cmdPrompts = !cmdPrompts;
            promptsChanged = true;
            ShowStatus();
            return true;
        }

        public bool HoldCheckBoxChanged()
        {
            if (callInProg == null) return false;

            DebugOutput($"{Time()} HoldCheckBoxChanged holdCheckBox.Checked:{ctrl.holdCheckBox.Checked} holdCheckBox.Enabled:{ctrl.holdCheckBox.Enabled}");
            if (ctrl.holdCheckBox.Checked /*|| (mode == "MSK144" && modeSupported)*/)
            {
                ctrl.limitLabel.Enabled = false;
                ctrl.repeatLabel.Enabled = false;
                ctrl.timeoutNumUpDown.Enabled = false;
                ctrl.optimizeCheckBox.Enabled = false;
            }
            else
            {
                ctrl.limitLabel.Enabled = true;
                ctrl.repeatLabel.Enabled = true;
                ctrl.timeoutNumUpDown.Enabled = true;
                ctrl.optimizeCheckBox.Enabled = true;
            }
            DebugOutput($"{nl}{Time()} HoldCheckBoxChanged");
            ShowStatus();
            return true;
        }

        //log file mode requested to be (possibly) changed
        public void LogModeChanged(bool enable)
        {
            if (enable == diagLog) return;       //no change requested

            diagLog = SetLogFileState(enable);
        }

        public void TxModeChanged(TxModes tMode)          //tx mode selected
        {
            HaltTuning();
            pendingCqAfterAnalysis = false;
            TxModes prevTxMode = txMode;
            txMode = tMode;
            DebugOutput($"{nl}{Time()} TxModeChanged, txMode:{txMode} cqPaused:{cqPaused} txEnabled:{txEnabled}");
            UpdateModeSelection();
            UpdateListenModeTxPeriod();

            cqPaused = txMode == TxModes.CALL_CQ;

            if (!cqPaused)
            {
                if (txMode == TxModes.CALL_CQ && prevTxMode == TxModes.LISTEN)        //WSJT-X "Enable Tx" button is checked
                {
                    EnableTx();       //set WSJT-X tx to enabled and set "Enable Tx" button state to checked
                    DebugOutput($"{spacer}value:{ctrl.timeoutNumUpDown.Value} callQueue.Count:{callQueue.Count}");
                    if (ctrl.timeoutNumUpDown.Value <= maxCheckTxRepeat && callQueue.Count > 0)
                    {
                        DebugOutput($"{CallQueueString()}");
                        EnqueueDecodeMessage dmsg;
                        PeekCall(0, out dmsg);
                        bool evenCall = IsEvenCall(dmsg);
                        DebugOutput($"{spacer}evenCall:{evenCall}");
                        if (!ctrl.advancedCallLayout)
                            CheckCallQueuePeriod(!evenCall);        //remove queued calls from wrong time period
                    }
                }

                if (txMode == TxModes.LISTEN && prevTxMode == TxModes.CALL_CQ)        //WSJT-X "Enable Tx" button is checked
                {

                    HaltTx();           //stop CQing immediately
                    DisableTx(true);    //set WSJT-X tx to disable
                    txEnableChanged = true;
                    modePrompt = true;
                }

                CheckNextXmit();
            }

            if (txMode == TxModes.CALL_CQ && opMode == OpModes.ACTIVE && callInProg == null)
            {
                newDirCq = true;
                cqPaused = false;
                SetupCq(true);
            }

            StartStatusTimer();
            UpdateDebug();
        }

        public void TxRepeatChanged()
        {
            UpdateMaxTxRepeat();

            bool evenCall;
            DebugOutput($"{Time()} TxRepeatChanged optimize:{ctrl.optimizeCheckBox.Checked} selected:{(int)ctrl.timeoutNumUpDown.Value} maxTxRepeat:{maxTxRepeat} maxPrevTo:{maxPrevTo} maxAutoGenEnqueue:{maxAutoGenEnqueue}");
            if (ctrl.timeoutNumUpDown.Value <= maxCheckTxRepeat)
            {
                if (callQueue.Count > 0)
                {
                    DebugOutput($"{spacer}check next call");
                    DebugOutput($"{CallQueueString()}");
                    EnqueueDecodeMessage dmsg;
                    PeekCall(0, out dmsg);
                    evenCall = IsEvenCall(dmsg);
                    DebugOutput($"{spacer}evenCall:{evenCall}");
                    if (!ctrl.advancedCallLayout)
                        CheckCallQueuePeriod(!evenCall);        //remove queued calls from wrong time period
                }
                else
                {
                    DebugOutput($"{spacer}check replyDecode");
                    if (callInProg != null && replyDecode != null)
                    {
                        evenCall = IsEvenCall(replyDecode);
                        DebugOutput($"{spacer}evenCall:{evenCall}");
                        if (!ctrl.advancedCallLayout)
                            CheckCallQueuePeriod(!evenCall);        //remove queued calls from wrong time period
                    }
                }
            }
            UpdateDebug();
        }

        public void UpdateCallInProg()
        {
            //placeholder
        }

        public void UpdateCallListAccessibleName(bool force = false)
        {
            if (!force && lastCallListTxFirst == txFirst) return;
            lastCallListTxFirst = txFirst;
            string rxLabel = txFirst ? "RX2" : "RX1";
            ctrl.callListBox.AccessibleName = $"{rxLabel} Stations Available List Box";

            // Update advanced list labels to reflect the active transmit side.
            // txFirst=true  → user transmits on TX1 (even); TX2 is the receive side → label TX2 as RX2.
            // txFirst=false → user transmits on TX2 (odd);  TX1 is the receive side → label TX1 as RX1.
            if (txFirst)
            {
                ctrl.advTx1Label.Text             = "TX1 available stations:";
                ctrl.advTx1ListBox.AccessibleName = $"TX1 available stations, {_tx1SnapshotRows.Count} calls";
                ctrl.advTx2Label.Text             = "RX2 available stations:";
                ctrl.advTx2ListBox.AccessibleName = $"RX2 available stations, {_tx2SnapshotRows.Count} calls";
            }
            else
            {
                ctrl.advTx1Label.Text             = "RX1 available stations:";
                ctrl.advTx1ListBox.AccessibleName = $"RX1 available stations, {_tx1SnapshotRows.Count} calls";
                ctrl.advTx2Label.Text             = "TX2 available stations:";
                ctrl.advTx2ListBox.AccessibleName = $"TX2 available stations, {_tx2SnapshotRows.Count} calls";
            }
        }

        public void WsjtxSettingChanged()
        {
            settingChanged = true;
            newDirCq = true;
        }

        public string SpacifyMyCall()
        {
            return Spacify(myCall);
        }

        public void Pause(bool haltTx, bool showStatus)         //go to pause mode, optionally halt Tx
        {
            consecNoDecodes = 0;
            if (haltTx || tuning) HaltTx();

            StopDecodeTimers();
            DisableAutoFreqPause();
            ctrl.holdCheckBox.Checked = false;

            if (txMode == TxModes.CALL_CQ) cqPaused = true;
            DebugOutput($"{spacer}cqPaused:{cqPaused} txEnabled:{txEnabled}");

            txEnableChanged = showStatus;
            if (showStatus && !transmitting) StartStatusTimer();
            modePrompt = true;
            UpdateMaxTxRepeat();
            UpdateDebug();
        }

        public void UpdateModeVisible()
        {
            if (opMode == OpModes.ACTIVE)
            {
                ctrl.listenModeButton.Visible = true;
                ctrl.cqModeButton.Visible = true;
                ctrl.modeGroupBox.Visible = true;
            }
            else
            {
                ctrl.listenModeButton.Visible = false;
                ctrl.cqModeButton.Visible = false;
                ctrl.modeGroupBox.Visible = false;
                ctrl.modeGroupBox.Visible = false;
            }

            UpdateListenModeTxPeriod();
            DebugOutput($"{spacer}UpdateModeVisible, txMode:{txMode}");
        }

        private void Update()
        {
            if (suspendComm) return;

            try
            {
                msg = WsjtxMessage.Parse(datagram);
                //DebugOutput($"{Time()} msg:{msg} datagram[{datagram.Length}]:{nl}{DatagramString(datagram)}");
            }
            catch (ParseFailureException ex)
            {
                //File.WriteAllBytes($"{ex.MessageType}.couldnotparse.bin", ex.Datagram);
                DebugOutput($"{Time()} ERROR: Parse failure {ex.InnerException.Message}");
                DebugOutput($"datagram[{datagram.Length}]: {DatagramString(datagram)}");
                return;
            }

            if (msg == null)
            {
                DebugOutput($"{Time()} ERROR: null message, datagram[{datagram.Length}]: {DatagramString(datagram)}");
                return;
            }

            //rec'd first HeartbeatMessage
            //check version, send requested schema version
            //request a StatusMessage
            //go from INIT to SENT state
            if (msg.GetType().Name == "HeartbeatMessage" && (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.INITIAL || WsjtxMessage.NegoState == WsjtxMessage.NegoStates.FAIL))
            {
                ctrl.initialConnFaultTimer.Stop();             //stop connection fault dialog
                HeartbeatMessage imsg = (HeartbeatMessage)msg;
                DebugOutput($"{Time()}{nl}{imsg}");

                string[] sa = imsg.Revision.Split(' '); //may contain other info, including URL

                string rev = sa[0];
                int.TryParse(rev, out wsjtxRevision);

                string testVer = sa.Length >= 2 ? sa[1] : "42";
                int.TryParse(testVer, out wsjtxTestVer);

                curVerBld = $"{imsg.Version}/{rev}";

                if (!acceptableWsjtxVersions.Contains(curVerBld))
                {
                    heartbeatRecdTimer.Stop();
                    suspendComm = true;
                    ctrl.BringToFront();
                    MessageBox.Show($"WSJT-X v{imsg.Version}/{imsg.Revision} is not supported.{nl}{nl}Supported WSJT-X version(s):{nl}{AcceptableVersionsString()}{nl}{nl}You can check the WSJT-X version/build by selecting 'Help | About' in WSJT-X.{nl}{nl}{pgmName} will try again when you close this dialog.", pgmName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    ResetOpMode();
                    ShowStatus();
                    suspendComm = false;
                    UpdateDebug();
                    return;
                }
                else
                {
                    if (udpClient2 != null)
                    {
                        udpClient2.Close();
                        udpClient2 = null;
                        DebugOutput($"{spacer}closed udpClient2:{udpClient2}");
                    }

                    var tmsg = new HeartbeatMessage();
                    tmsg.SchemaVersion = WsjtxMessage.PgmSchemaVersion;
                    tmsg.MaxSchemaNumber = (uint)WsjtxMessage.PgmSchemaVersion;
                    tmsg.Id = WsjtxMessage.UniqueId;
                    tmsg.Version = WsjtxMessage.PgmVersion;
                    tmsg.Revision = WsjtxMessage.PgmRevision;

                    ba = tmsg.GetBytes();
                    udpClient2 = new UdpClient();
                    udpClient2.Connect(fromEp);
                    udpClient2.Send(ba, ba.Length);
                    WsjtxMessage.NegoState = WsjtxMessage.NegoStates.SENT;
                    UpdateDebug();
                    DebugOutput($"{spacer}NegoState:{WsjtxMessage.NegoState}");
                    DebugOutput($"{Time()} >>>>>Sent'Heartbeat' msg:{nl}{tmsg}");
                    ShowStatus();
                    StatusView.ShowMessage("WSJT-X responding", false);

                    if (wsjtxRevision == 102 && wsjtxTestVer < 72) DeleteLotwCsv();        //fixed, reason for WSJT-X crashing at startup because of NVDA determined
                }
                UpdateDebug();
                return;
            }

            //rec'd negotiation HeartbeatMessage
            //send another request for a StatusMessage
            //go from SENT to RECD state
            if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.SENT && msg.GetType().Name == "HeartbeatMessage")
            {
                HeartbeatMessage hmsg = (HeartbeatMessage)msg;
                DebugOutput($"{Time()}{nl}{hmsg}");
                WsjtxMessage.NegotiatedSchemaVersion = hmsg.SchemaVersion;
                WsjtxMessage.NegoState = WsjtxMessage.NegoStates.RECD;
                UpdateDebug();
                DebugOutput($"{spacer}NegoState:{WsjtxMessage.NegoState}");
                DebugOutput($"{spacer}negotiated schema version:{WsjtxMessage.NegotiatedSchemaVersion}");
                UpdateDebug();

                //send ACK request to WSJT-X, to get 
                //a StatusMessage reply to start normal operation
                Thread.Sleep(250);
                emsg.NewTxMsgIdx = 7;
                emsg.GenMsg = $"";          //no effect
                emsg.ReplyReqd = true;
                emsg.EnableTimeout = !debug;
                emsg.CmdCheck = cmdCheck;
                ba = emsg.GetBytes();
                udpClient2.Send(ba, ba.Length);
                DebugOutput($"{Time()} >>>>>Sent 'Ack Req' cmd:7 cmdCheck:{cmdCheck}{nl}{emsg}");

                emsg.NewTxMsgIdx = 17;
                emsg.Param0 = usePskReporter;
                emsg.Param1 = false;        //ignored
                emsg.Offset = 0;            //ignored
                emsg.GenMsg = $"(mod by WM8Q, w/{pgmName} v{pgmVer} [FT8 for blind hams], qrz.com/db/WM8Q)";
                emsg.CmdCheck = "";         //ignored
                ba = emsg.GetBytes();
                udpClient2.Send(ba, ba.Length);
                DebugOutput($"{Time()} >>>>>Sent 'Set PSKReporter' cmd:17{nl}{emsg}");

                HaltTx();       //sync up WSJT-X button state

                if (bandIdx == null)
                {
                    SetOperatingMode("FT8");            //after halt
                    Thread.Sleep(250);
                    mode = "FT8";
                    bandIdx = FreqToBandIdx(dialFrequency / 1e6);       //can be null if unknown
                    if (bandIdx == null) bandIdx = 5;
                    SetBandTxFirst((uint)(bandToFreq(bandIdx) * 1000), txFirst, "InitialConnect");
                    Thread.Sleep(250);
                }

                cmdCheckTimer.Interval = 10000;           //set up cmd check timeout
                cmdCheckTimer.Start();
                DebugOutput($"{spacer}check cmd timer started");
                return;
            }

            //while in INIT or SENT state:
            //get minimal info from StatusMessage needed for faster startup
            //and for special case of ack msg returned by WSJT-X after req for StatusMessage
            //check for no call sign or grid, exit if so;
            //calculate best offset frequency;
            //also get decode offset frequencies for best offest calculation
            if (WsjtxMessage.NegoState != WsjtxMessage.NegoStates.RECD)
            {
                if (msg.GetType().Name == "StatusMessage")
                {
                    StatusMessage smsg = (StatusMessage)msg;
                    DebugOutput($"{nl}{Time()}{nl}{smsg}{nl}{spacer}NegoState:{WsjtxMessage.NegoState} opMode:{opMode} smsg.TRPeriod:'{smsg.TRPeriod}'");

                    txFirst = smsg.TxFirst;
                    UpdateCallListAccessibleName();     // update RX1/TX1 labels as soon as txFirst is known

                    //if seconds units, need msec
                    if (smsg.TRPeriod != null)
                    {
                        if ((int)smsg.TRPeriod < 1000)
                        {
                            trPeriod = 1000 * (int)smsg.TRPeriod;
                        }
                        else
                        {
                            trPeriod = (int)smsg.TRPeriod;
                        }
                    }

                    if (trPeriod != null)
                    {
                        decoding = smsg.Decoding;
                        DebugOutput($"{spacer}decoding:{decoding} lastDecoding:{lastDecoding} decodeCycle:{decodeCycle} trPeriod:{trPeriod}");
                        if (decoding != lastDecoding)
                        {
                            if (decoding)
                            {
                                if (decodeCycle == 0)
                                {
                                    SetPeriodState();
                                }
                                if (ctrl.advancedCallLayout)
                                {
                                    _rawDecodeHistory.Clear();
                                    if (ctrl.advShowRaw) ShowRawDecodes();
                                }
                            }
                            else
                            {
                                postDecodeTimer.Stop();
                                postDecodeTimer.Start();                    //restart timer at every decode, will time out after last decode
                                DebugOutput($"{spacer}postDecodeTimer start, decodeNum:{decodeNum} decodeCycle:{decodeCycle}");

                                if (lastDecoding != null)           //need to start with decoding = true
                                {
                                    if (decodeCycle == 0)
                                    {
                                        //first calcluation of best offset
                                        if (!skipFirstDecodeSeries)
                                        {
                                            DebugOutput($"{spacer}audioOffsets.Count:{audioOffsets.Count}");
                                            CalcBestOffset(audioOffsets, period, false);
                                            CalcAvgTimeOffset(false);
                                        }
                                    }
                                    decodeCycle++;
                                    DebugOutput($"{spacer}next decodeCycle:{decodeCycle}");
                                }
                            }
                        }
                        lastDecoding = decoding;
                    }

                    txEnabledConf = smsg.TxEnabled;
                    if (txEnabledConf != lastTxEnabled)         //lastTxEnabled can be null
                    {
                        if (txEnabledConf)
                        {
                            StatusView.ShowMessage("Not ready yet... please wait", true);
                        }
                    }
                    lastTxEnabled = txEnabledConf;

                    wsjtxTxEnableButton = smsg.TxEnableButton;          //keep WSJT-X "Enable Tx" button state current
                    UpdateDblClkTip();

                    //marker2
                    string mode = smsg.Mode;
                    if (mode != lastMode)
                    {
                        DebugOutput($"{spacer}mode changed, decodeCycle:{CurrentDecodeCycleString()} lastDecoding:{lastDecoding}");
                        ClearAudioOffsets();
                        decodeCycle = 0;
                        consecNoDecodes = 0;
                    }
                    lastMode = mode;

                    dialFrequency = smsg.DialFrequency;
                    if (lastDialFrequency == null) lastDialFrequency = dialFrequency;
                    if (lastDialFrequency != null && (Math.Abs((float)lastDialFrequency - (float)dialFrequency) > freqChangeThreshold))
                    {
                        DebugOutput($"{spacer}frequency changed, decodeCycle:{CurrentDecodeCycleString()} lastDecoding:{lastDecoding}");
                        ClearAudioOffsets();
                    }
                    lastDialFrequency = dialFrequency;

                    if (myContinent != smsg.MyContinent)
                    {
                        myContinent = smsg.MyContinent;
                        ctrl.replyLocalCheckBox.Text = (myContinent == null ? "loc" : myContinent);
                        DebugOutput($"{spacer}myContinent changed:{myContinent}");
                    }

                    UpdateRR73();
                    specOp = (int)smsg.SpecialOperationMode;

                    configuration = smsg.ConfigurationName;
                    if (!CheckMyCall(smsg)) return;
                    DebugOutput($"{spacer}myCall:'{myCall}' myGrid:'{myGrid}' mode:'{mode}' specOp:'{specOp}' configuration:{configuration} check:{smsg.Check}");
                    UpdateDebug();
                }

                if (msg.GetType().Name == "EnqueueDecodeMessage")
                {
                    EnqueueDecodeMessage qmsg = (EnqueueDecodeMessage)msg;
                    if (qmsg.DeltaFrequency > offsetLoLimit && qmsg.DeltaFrequency < offsetHiLimit) audioOffsets.Add(qmsg.DeltaFrequency);
                    timeOffsets.Add(qmsg.DeltaTime);

                    if (!qmsg.AutoGen)
                        StatusView.ShowMessage("Not ready yet... please wait", true);
                }
            }

            //************
            //CloseMessage
            //************
            if (msg.GetType().Name == "CloseMessage")
            {
                DebugOutput($"{nl}{Time()} CloseMessage rec'd{nl}{Time()}{nl}{msg}");
                if (WsjtxMessage.NegoState != WsjtxMessage.NegoStates.WAIT) wsjtxClosing = true;
                DebugOutput($"{spacer}NegoState:{WsjtxMessage.NegoState} wsjtxClosing:{wsjtxClosing}");
                return;
            }

            //****************
            //HeartbeatMessage
            //****************
            //in case 'Monitor' disabled, get StatusMessages
            if (msg.GetType().Name == "HeartbeatMessage")
            {
                if (opMode != OpModes.ACTIVE) DebugOutput($"{nl}{Time()} WSJT-X event, heartbeat rec'd:{nl}{msg}");
                emsg.NewTxMsgIdx = 7;
                emsg.GenMsg = $"";          //no effect
                emsg.ReplyReqd = (opMode != OpModes.ACTIVE);
                emsg.EnableTimeout = !debug;
                if (emsg.ReplyReqd) cmdCheck = RandomCheckString();
                emsg.CmdCheck = cmdCheck;
                ba = emsg.GetBytes();
                udpClient2.Send(ba, ba.Length);
                if (opMode != OpModes.ACTIVE) DebugOutput($"{Time()} >>>>>Sent 'Ack Req' cmd:7 cmdCheck:{cmdCheck}{nl}{emsg}");

                heartbeatRecdTimer.Stop();
                if (!debug)
                {
                    heartbeatRecdTimer.Start();
                    if (opMode != OpModes.ACTIVE) DebugOutput($"{spacer}heartbeatRecdTimer restarted");
                }

                emsg.NewTxMsgIdx = 13;      //important! reset watchdog timer
                emsg.GenMsg = $"";          //no effect
                emsg.ReplyReqd = false;     //no effect
                emsg.EnableTimeout = true;  //no effect
                emsg.CmdCheck = "";         //no effect
                ba = emsg.GetBytes();
                udpClient2.Send(ba, ba.Length);
                if (opMode != OpModes.ACTIVE) DebugOutput($"{Time()} >>>>>Sent 'Reset Tx watchdog' cmd:13");

            }

            if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.RECD)
            {
                if (modeSupported)
                {
                    //********************
                    //EnqueueDecodeMessage
                    //********************
                    //only resulting action is to add call to callQueue, optionally restart queue
                    if (msg.GetType().Name == "EnqueueDecodeMessage" && myCall != null)
                    {
                        EnqueueDecodeMessage dmsg = (EnqueueDecodeMessage)msg;
                        if (dmsg.AutoGen && ctrl.advancedCallLayout)
                        {
                            while (_rawDecodeHistory.Count >= ctrl.rawMaxRows)
                                _rawDecodeHistory.RemoveAt(0);
                            _rawDecodeHistory.Add(dmsg);
                            if (ctrl.advShowRaw) ShowRawDecodes();
                        }
                        if (!dmsg.Message.Contains(";"))
                        {
                            //normal (not "special operating activity") message
                            ProcessDecodeMsg(dmsg, false);
                        }
                        else
                        {
                            //fox/hound-style (multi-target) message: process as two separate decodes (note: full f/h mode not supported)
                            // 0    1     2    3   4
                            //W1AW RR73; WM8Q T2C -02
                            string msg = dmsg.Message;
                            DebugOutput($"{nl}{Time()} F/H msg detected: {msg}");
                            string[] words = msg.Replace(";", "").Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (words.Length != 5) return;

                            EnqueueDecodeMessage dmsg2 = dmsg.DeepCopy();       //prevent aliasing

                            dmsg.Message = $"{words[0]} {words[3]} {words[1]}";
                            DebugOutput($"{spacer}processing first msg: {dmsg.Message}");
                            ProcessDecodeMsg(dmsg, true);

                            dmsg2.Message = $"{words[2]} {words[3]} {words[4]}";
                            DebugOutput($"{spacer}processing second msg: {dmsg2.Message}");
                            ProcessDecodeMsg(dmsg2, true);
                        }
                        return;
                    }
                }


                //*************
                //StatusMessage
                //*************
                if (msg.GetType().Name == "StatusMessage")
                {
                    StatusMessage smsg = (StatusMessage)msg;
                    DateTime dtNow = DateTime.UtcNow;
                    bool modeChanged = false;
                    if (opMode < OpModes.ACTIVE) DebugOutput($"{Time()}{nl}{msg}{nl}{spacer}opMode:{opMode} cqPaused:{cqPaused} myCall:'{myCall}'");
                    qsoStateConf = smsg.CurQsoState();
                    txEnabledConf = smsg.TxEnabled;
                    dxCall = smsg.DxCall;                               //unreliable info, can be edited manually
                    if (dxCall == "") dxCall = null;
                    mode = smsg.Mode;
                    specOp = (int)smsg.SpecialOperationMode;
                    txMsg = WsjtxMessage.RemoveAngleBrackets(smsg.LastTxMsg);        //msg from last Tx
                    txFirst = smsg.TxFirst;
                    UpdateCallListAccessibleName();     // update RX1/TX1 labels as soon as txFirst is known
                    decoding = smsg.Decoding;
                    transmitting = smsg.Transmitting;
                    int? prevBandIdx = bandIdx;
                    dialFrequency = smsg.DialFrequency;
                    bandIdx = FreqToBandIdx(dialFrequency / 1e6);       //can be null if unknown
                    txOffset = smsg.TxDF;
                    wsjtxTxEnableButton = smsg.TxEnableButton;
                    UpdateDblClkTip();
                    metricUnits = smsg.MetricUnits;
                    wsjtxResultCode = smsg.ResultCode != null ? (int)smsg.ResultCode : 0;
                    statusDetail = smsg.Detail;     //can be null

                    if (lastXmitting == null) lastXmitting = transmitting;     //initialize
                    if (lastQsoState == WsjtxMessage.QsoStates.INVALID) lastQsoState = qsoStateConf;    //initialize WSJT-X user QSO state change detection
                    if (lastDecoding == null) lastDecoding = decoding;     //initialize
                    if (lastTxWatchdog == null) lastTxWatchdog = smsg.TxWatchdog;   //initialize
                    if (lastTxFirst == null) lastTxFirst = txFirst;                     //initialize

                    if (txMsg != lastStatusTxMsg)
                    {
                        if (transmitting)
                        {
                            curTxMsg = txMsg;       //tx interrupted with a different call
                            curTxPayload = null;
                            DebugOutput($"{nl}{Time()} WSJT-X event, txMsg changed, curTxMsg:{curTxMsg} curTxPayload:'{curTxPayload}'");
                            if (!tuning) ShowStatus();
                        }
                        lastStatusTxMsg = txMsg;
                    }


                    //need msec unit
                    if (smsg.TRPeriod != null)
                    {
                        if ((int)smsg.TRPeriod < 1000)
                        {
                            trPeriod = 1000 * (int)smsg.TRPeriod;
                        }
                        else
                        {
                            trPeriod = (int)smsg.TRPeriod;
                        }
                    }

                    if (cmdCheckTimer.Enabled && smsg.Check == cmdCheck)             //found the random cmd check string, cmd receive ack'd
                    {
                        cmdCheckTimer.Stop();
                        commConfirmed = true;
                        DebugOutput($"{nl}{Time()} WSJT-X event, Check cmd rec'd, match");
                    }

                    //*********************************
                    //detect WSJT-X xmit start/end ASAP
                    //*********************************
                    if (trPeriod != null && transmitting != lastXmitting)
                    {
                        if (transmitting)
                        {
                            StartProcessDecodeTimer();
                            ProcessTxStart();
                            if (firstDecodeTime == DateTime.MinValue) firstDecodeTime = DateTime.UtcNow;       //start counting until WSJT-X watchdog timer set
                        }
                        else                //end of transmit
                        {
                            ProcessTxEnd();
                        }
                        lastXmitting = transmitting;
                    }

                    //***********************
                    //check myCall and myGrid
                    //***********************
                    if (myCall == null || myGrid == null)
                    {
                        CheckMyCall(smsg);
                    }
                    else
                    {
                        if (myCall != smsg.DeCall || myGrid != smsg.DeGrid)
                        {
                            DebugOutput($"{nl}{Time()} WSJT-X event, Call or grid changed, myCall:{smsg.DeCall} (was {myCall}) myGrid:{smsg.DeGrid} (was {myGrid})");
                            myCall = smsg.DeCall;
                            myGrid = smsg.DeGrid;

                            ResetOpMode();
                            Pause(true, true);
                            SetCallInProg(null);    //not calling anyone
                        }
                    }

                    //*****************
                    //check myContinent
                    //*****************
                    if (myContinent != smsg.MyContinent)
                    {
                        myContinent = smsg.MyContinent;
                        ctrl.replyLocalCheckBox.Text = (myContinent == null ? "loc" : myContinent);
                        DebugOutput($"{nl}{Time()} WSJT-X event, myContinent changed:{myContinent}");
                    }

                    //*******************************
                    //check for WSJT-X dxCall changed
                    //*******************************
                    if (dxCall != lastDxCall)       //occurs after dbl-click reported
                    {
                        DebugOutput($"{nl}{Time()} WSJT-X event, dxCall changed, dxCall:{dxCall} (was {lastDxCall})");
                        lastDxCall = dxCall;
                    }

                    //****************************
                    //detect WSJT-X Tx mode change
                    //****************************
                    if (mode != lastMode)
                    {
                        DebugOutput($"{nl}{Time()} WSJT-X event, mode changed, mode:'{mode}' (was '{lastMode}')");
                        UpdateRR73();

                        if (opMode > OpModes.IDLE)
                        {
                            decodeCycle = 0;
                            consecNoDecodes = 0;
                            ClearAudioOffsets();
                        }

                        if (opMode >= OpModes.START)
                        {
                            ctrl.holdCheckBox.Checked = false;
                            DisableAutoFreqPause();
                            ResetOpMode();
                            SetCallInProg(null);      //not calling anyone
                            StatusView.ShowMessage("Mode changed", false);
                            modeChanged = true;
                            newMode = true;
                        }
                        CheckModeSupported();
                    }
                    lastMode = mode;

                    //**********************************
                    //check for WSJT-X frequency changed
                    //**********************************
                    if (lastDialFrequency != null && (Math.Abs((float)lastDialFrequency - (float)dialFrequency) > freqChangeThreshold))
                    {
                        DebugOutput($"{nl}{Time()} [BAND-AUDIT] StatusMsg FreqChanged: newFreq:{dialFrequency / 1e6:F6} oldFreq:{lastDialFrequency / 1e6:F6} oldBandIdx:{prevBandIdx} newBandIdx:{FreqToBandIdx(dialFrequency / 1e6)} opMode:{opMode}");
                        bandIdx = FreqToBandIdx(dialFrequency / 1e6);       //can be null if unknown

                        if (FreqToBandIdx(dialFrequency / 1e6) == FreqToBandIdx(lastDialFrequency / 1e6))      //same band
                        {
                            DisableAutoFreqPause();

                            if (opMode == OpModes.ACTIVE)
                            {
                                ClearAudioOffsets();
                                if (ctrl.freqCheckBox.Checked) AutoFreqChanged(true, false);
                                Pause(true, false);
                                //if transmitting, let tx end trigger show status
                                if (!transmitting) ShowStatus();
                                if (!modeChanged) StatusView.ShowMessage("Frequency changed", false);
                                decodeCount = 0;
                                consecNoDecodes = 0;
                            }
                        }
                        else        //new band
                        {
                            DisableAutoFreqPause();
                            ClearAudioOffsets();
                            if (ctrl.freqCheckBox.Checked) _requireOffsetForActive = true;
                            newBand = true;
                            decodeCount = 0;
                            consecNoDecodes = 0;
                            AutoFreqChanged(ctrl.freqCheckBox.Checked, true);
                            DebugOutput($"{spacer}band changed:'{FreqToBandStr(dialFrequency / 1e6)}' (was:'{FreqToBandStr(lastDialFrequency / 1e6)}')");

                            _rawDecodeHistory.Clear();
                            if (ctrl.advShowRaw) ShowRawDecodes();

                            // Always clear calls and log on any confirmed band change, regardless of
                            // opMode. BandUp/Down set opMode=START via AutoFreqChanged before the
                            // command is sent, so opMode is never ACTIVE when this confirmation
                            // arrives — gating ClearCalls on ACTIVE caused the old list to persist.
                            DebugOutput($"{spacer}[BAND-AUDIT] StatusMsg FreqChanged: new band confirmed → ClearCalls+logList.Clear");
                            ClearCalls(true);
                            logList.Clear();        //can re-log on new mode/band or in new session
                            ShowLogged();
                            ctrl.LoadHrcCache();    //refresh HRC sets (band-independent; picks up any new imports)
                            ctrl.RefreshStillNeedCache();    //reload Still Need live-tag cache for the new band

                            if (opMode == OpModes.ACTIVE)
                            {
                                CancelQso();            //band change abandons any active contact
                                //won't get notification of Halt and Enable Tx buttons changing
                                if (txEnabled) Pause(true, false);
                            }

                            //if transmitting, let tx end trigger show status
                            if (!transmitting) ShowStatus();

                            if (!modeChanged) StatusView.ShowMessage("Band changed", false);
                            DebugOutput($"{spacer}cleared queued calls:DialFrequency, txTimeout:{txTimeout} callInProg:'{CallPriorityString(callInProg)}'");
                        }
                    }
                    lastDialFrequency = smsg.DialFrequency;

                    //*******************************************
                    //detect WSJT-X special operating mode change
                    //*******************************************
                    if (specOp != lastSpecOp)
                    {
                        DebugOutput($"{nl}{Time()} WSJT-X event, Special operating mode changed, specOp:{specOp} (was {lastSpecOp})");

                        if (opMode > OpModes.IDLE) ClearAudioOffsets();

                        if (opMode >= OpModes.START)
                        {
                            ctrl.holdCheckBox.Checked = false;
                            DisableAutoFreqPause();
                            ResetOpMode();
                            ShowStatus();
                            SetCallInProg(null);      //not calling anyone
                            modeChanged = true;
                            newMode = true;
                        }
                        CheckModeSupported();
                    }
                    lastSpecOp = specOp;

                    //***************************************
                    //check for transition from IDLE to START
                    //***************************************
                    if (commConfirmed && supportedModes.Contains(mode) && specOp == 0 && opMode == OpModes.IDLE)
                    {
                        EnableMonitoring();              //must do only after DisableTx and HaltTx
                        //if (debug) EnableDebugLog();

                        opMode = OpModes.START;
                        DebugOutput($"{Time()} opMode:{opMode}");
                        if (ctrl.freqCheckBox.Checked) ShowStatus();
                        UpdateModeVisible();
                    }

                    //*************************
                    //detect decoding start/end
                    //*************************
                    if (decoding != lastDecoding)
                    {
                        if (smsg.Decoding)
                        {
                            string newLn = (decodeCycle == 0 ? nl : "");
                            DebugOutput($"{newLn}{Time()} WSJT-X event, Decode start, trPeriod:'{trPeriod}' decodeCycle:{decodeCycle}, processDecodeTimer.Enabled:{processDecodeTimer.Enabled}");
                            if (decodeCycle == 0 && trPeriod != null)
                            {
                                SetPeriodState();
                                decodesProcessed = false;
                                if (!processDecodeTimer.Enabled)           //was not started at end of last xmit, use first decode instead
                                {
                                    int msec = (dtNow.Second * 1000) + dtNow.Millisecond;
                                    int diffMsec = msec % (int)trPeriod;
                                    int cycleTimerAdj = CalcTimerAdj();
                                    int interval = Math.Max(((int)trPeriod) - diffMsec - cycleTimerAdj, 1);
                                    DebugOutput($"{spacer}msec:{msec} diffMsec:{diffMsec} interval:{interval} cycleTimerAdj:{cycleTimerAdj}");
                                    if (interval > 0)
                                    {
                                        processDecodeTimer.Interval = interval;
                                        processDecodeTimer.Start();
                                        DebugOutput($"{spacer}processDecodeTimer start");
                                    }
                                }
                            }
                        }
                        else  //not decoding
                        {
                            postDecodeTimer.Stop();
                            postDecodeTimer.Start();                    //restart timer at every decode, will time out after last decode
                            DebugOutput($"{Time()} WSJT-X event, Decode end, postDecodeTimer start, decodeNum:{decodeNum} decodeCycle:{decodeCycle}");
                            if (decodeCycle == 0)
                            {
                                //first calculation of best offset
                                if (!skipFirstDecodeSeries)
                                {
                                    if (CalcBestOffset(audioOffsets, period, false))       //calc for period when decodes started
                                    {
                                        ctrl.freqCheckBox.Text = "Use best Tx frequency";
                                        ctrl.freqCheckBox.ForeColor = Color.Black;
                                    }
                                    CalcAvgTimeOffset(false);
                                }
                            }
                            decodeCycle++;
                            DebugOutput($"{spacer}next decodeCycle:{decodeCycle}");
                        }
                        lastDecoding = smsg.Decoding;
                    }

                    //*************************************
                    //check for changed QSO state in WSJT-X
                    //*************************************
                    if (lastQsoState != qsoStateConf)
                    {
                        qsoState = qsoStateConf;            //qsoState confirmed
                        DebugOutput($"{nl}{Time()} WSJT-X event, qsoState changed, qsoState:{qsoState} (was {lastQsoState})");
                        lastQsoState = qsoState;
                        UpdateCallInProg();
                        DebugOutputStatus();
                    }

                    //**********************
                    //WSJT-X Tx halt clicked
                    //**********************
                    if (smsg.TxHaltClk)
                    {
                        if (opMode >= OpModes.START)
                        {
                            DebugOutput($"{nl}{Time()} WSJT-X event, TxHaltClk, cqPaused:{cqPaused} txMode:{txMode} processDecodeTimer.Enabled:{processDecodeTimer.Enabled}");
                            txEnabled = false;        //sync belief -- WSJT-X halted Tx on its own, not via Jimmy's own EnableTx()/DisableTx()
                            Pause(false, true);       //WSJT-X already halted Tx
                        }
                    }
                    //***********************************************
                    //check for WSJT-X Tx enable button state changed
                    //***********************************************
                    if (smsg.TxEnableClk)           //WSJT-X "Tx Enable" button clicked, and button state updated by WSJT-X
                    {
                        if (opMode >= OpModes.START)
                        {
                            DebugOutput($"{nl}{Time()} WSJT-X event, wsjtxTxEnableButton:{wsjtxTxEnableButton}, txEnabled:{txEnabled} cqPaused:{cqPaused} txMode:{txMode} processDecodeTimer.Enabled:{processDecodeTimer.Enabled}");
                            if (!txEnabled)    //Jimmy didn't ask for this -- WSJT-X changed its own Enable Tx button
                            {
                                if (wsjtxTxEnableButton)    //button just became enabled on WSJT-X's own initiative (e.g. Wait and Reply)
                                {
                                    HandleUnsolicitedTxResume();
                                }
                                else                        //button just became disabled
                                {
                                    //HaltTx();
                                    Console.Beep();
                                }
                            }
                        }
                    }

                    //***********************************
                    //check for changed WSJT-X Tx enabled
                    //***********************************
                    if (txEnabledConf != lastTxEnabled)
                    {
                        DebugOutput($"{nl}{Time()} WSJT-X event, Tx enable change confirmed, txEnabled:{txEnabled} (was {lastTxEnabled}) cqPaused:{cqPaused} txMode:{txMode}");
                        lastTxEnabled = txEnabledConf;
                    }

                    //**********************************************
                    //check for WSJT-X watchdog timer status changed
                    //**********************************************
                    if (smsg.TxWatchdog != lastTxWatchdog)
                    {
                        DebugOutput($"{nl}{Time()} WSJT-X event, smsg.TxWatchdog:{smsg.TxWatchdog} (was {lastTxWatchdog})");
                        /*if (opMode == OpModes.ACTIVE)
                        {
                            ctrl.holdCheckBox.Checked = false;
                        }

                        if (smsg.TxWatchdog && opMode == OpModes.ACTIVE)        //only need this event if in valid mode
                        {
                            if (firstDecodeTime != DateTime.MinValue)
                            {
                                string txt;
                                if ((DateTime.UtcNow - firstDecodeTime).TotalMinutes < 15)
                                {
                                    txt = $"Set the 'Tx watchdog' in WSJT-X to 15 minutes or longer.{nl}{nl}This will be the timeout in case {ctrl.friendlyName} sends the same message repeatedly (for example, calling CQ when the band is closed).{nl}{nl}The WSJT-X 'Tx watchdog' setting is under File | Settings, in the 'General' tab.";
                                }
                                else
                                {
                                    txt = $"The 'Tx watchdog' in WSJT-X has timed out.{nl}{nl}(The WSJT-X 'Tx watchdog' setting is under File | Settings, in the 'General' tab).{nl}{nl}Select an 'Operatng Mode' to continue.";
                                }

                                firstDecodeTime = DateTime.MinValue;        //allow timing to restart
                            }
                        }*/

                        lastTxWatchdog = smsg.TxWatchdog;
                    }

                    //*****************************
                    //detect WSJT-X Tx First change
                    //*****************************
                    if (txFirst != lastTxFirst)
                    {
                        DebugOutput($"{nl}{Time()} WSJT-X event, Tx first changed, txFirst:{txFirst} txMode:{txMode}");
                        settingChanged = true;
                        DisableAutoFreqPause();
                        if (opMode > OpModes.IDLE) ClearAudioOffsets();

                        if (opMode == OpModes.ACTIVE)
                        {
                            newTxFirst = true;
                            if (!ctrl.advancedCallLayout)
                            {
                                // Normal mode: a txFirst change means the user manually
                                // switched TX period — clear the queue and pause so the
                                // next decode cycle fills the list for the new period.
                                SetCallInProg(null);
                                ClearCalls(true);
                                Pause(true, true);
                                ctrl.holdCheckBox.Checked = false;
                            }
                            else
                            {
                                // Advanced mode: both TX periods coexist in the queue.
                                // A txFirst change here is either an Alt+F manual toggle
                                // or a cross-period ReplyMessage side-effect — keep the
                                // queue and show the confirmed status promptly.
                                StartStatusTimer();
                            }
                        }
                        lastTxFirst = txFirst;
                        UpdateCallListAccessibleName();
                    }

                    //**********************************
                    //detect WSJT-X log upload log state
                    //**********************************
                    if (wsjtxResultCode != lastWsjtxResultCode)
                    {
                        if (wsjtxResultCode == (int)WsjtxResultCodes.LOTW_UPL)
                        {
                            DebugOutput($"{nl}{Time()} WSJT-X event, upload to LOTW, wsjtxResultCode:{wsjtxResultCode} statusDetail:'{statusDetail}' isNull:{statusDetail == null}");
                            uploadResult = (statusDetail != null && statusDetail != "" ) ? statusDetail : "QSO upload status unknown";
                            ShowStatus();
                        }

                        if (wsjtxResultCode == (int)WsjtxResultCodes.PWR_SWR_SINGLE_RPT)        //no reason to lose decode syncing 
                        {
                            DisableAutoFreqPause();
                            DebugOutput($"{nl}{Time()} WSJT-X event, power/swr single result, wsjtxResultCode:{wsjtxResultCode} statusDetail:'{statusDetail}' isNull:{statusDetail == null}");
                            tuneResult = (statusDetail != null && statusDetail != "") ? statusDetail : "Power/SWR unknown";
                            ShowStatus();
                        }

                        if (wsjtxResultCode == (int)WsjtxResultCodes.PWR_SWR_RPT)
                        {
                            consecNoDecodes = 0;
                            StopDecodeTimers();
                            DisableAutoFreqPause();
                            DebugOutput($"{nl}{Time()} WSJT-X event, power/swr result, wsjtxResultCode:{wsjtxResultCode} statusDetail:'{statusDetail}' isNull:{statusDetail == null}");
                            tuneResult = (statusDetail != null && statusDetail != "") ? statusDetail : "Power/SWR unknown";
                            ShowStatus();
                        }

                        if (wsjtxResultCode == (int)WsjtxResultCodes.PWR_SWR_END)
                        {
                            decodeCycle = 0;        //restart decode syncing
                            DebugOutput($"{nl}{Time()} WSJT-X event, power/swr result, wsjtxResultCode:{wsjtxResultCode}");
                            tuneResult = "Tune stopped";
                            tuning = false;             //normal status msgs
                            statusTimer.Interval = 750;     //will be receiving mode soon
                            statusTimer.Start();
                        }
                        lastWsjtxResultCode = wsjtxResultCode;
                    }



                    if (CheckActive())
                    {
                        _requireOffsetForActive = false;
                        UInt32 activeOffset = AudioOffsetFromTxPeriod();
                        //send cmd:10 when offset is known, or when freqCheckBox is off (offset=0 is safe in that case)
                        if (activeOffset > 0 || !ctrl.freqCheckBox.Checked)
                        {
                        emsg.NewTxMsgIdx = 10;
                        emsg.GenMsg = $"";          //no effect
                        emsg.SkipGrid = ctrl.skipGridCheckBox.Checked;
                        emsg.UseRR73 = ctrl.useRR73CheckBox.Checked;
                        emsg.CmdCheck = "";         //ignored
                        emsg.Offset = activeOffset;
                        ba = emsg.GetBytes();
                        udpClient2.Send(ba, ba.Length);
                        DebugOutput($"{Time()} [BAND-AUDIT] CheckActive→cmd:10 sent: bandIdx:{bandIdx} offset:{activeOffset}");
                        DebugOutput($"{Time()} >>>>>Sent 'Opt Req' cmd:10{nl}{emsg}");
                        }
                        if (settingChanged)
                        {
                            ctrl.WsjtxSettingConfirmed();
                            settingChanged = false;
                        }

                        newBand = true;
                        newMode = true;
                        decodeCount = 0;
                        consecNoDecodes = 0;
                        ShowStatus();
                    }

                    //*****end of status *****
                    UpdateDebug();
                    return;
                }

                //*****************
                //QsoLoggedMessage
                //*****************
                if (msg.GetType().Name == "QsoLoggedMessage")
                {
                    var qMsg = (QsoLoggedMessage)msg;
                    DebugOutput($"{nl}{Time()} QsoLoggedMessage rec'd: DxCall:'{qMsg.DxCall}'");
                    HandleLiveQsoLogged(qMsg);
                }

                //*****************
                //LoggedAdifMessage -- WSJT-X sends this alongside QsoLoggedMessage for every
                //logged QSO. Jimmy normally acts on QsoLoggedMessage; this is a fallback so
                //one dropped UDP packet doesn't silently keep a QSO out of the log/awards.
                //(Note: this message's own "Id" field, like QsoLoggedMessage's, is WSJT-X's
                //fixed per-instance identifier, not a per-QSO key -- ClaimLiveLoggedQso()
                //dedupes on callsign/band/mode/date/time instead, so the normal case where
                //both messages arrive for the same QSO only processes it once.)
                //*****************
                else if (msg.GetType().Name == "LoggedAdifMessage")
                {
                    var aMsg = (LoggedAdifMessage)msg;
                    DebugOutput($"{nl}{Time()} LoggedAdifMessage rec'd, Id:'{aMsg.Id}'");
                    HandleLiveAdifLogged(aMsg);
                }
            }
        }

        // Shared "a QSO was just logged" UI feedback and CQ-resume, called from whichever of
        // QsoLoggedMessage/LoggedAdifMessage claims the Id first (see _liveLoggedQsoIds).
        private void OnQsoLogged(string dxCall)
        {
            if (dxCall != null && !logList.Contains(dxCall))
            {
                logList.Add(dxCall);
                loggedCall = dxCall;
                lCall = dxCall;
                ShowLogged();
                Sounds.PlaySoundEvent(ctrl.loggedCheckBox.Checked, ctrl.soundFile_Logged);
                StatusView.ShowMessage($"Logged QSO with {dxCall}", false);
                DebugOutput($"{spacer}OnQsoLogged: added '{dxCall}' to logList");
            }
            if (txMode == TxModes.CALL_CQ &&
                dxCall != null &&
                string.Equals(dxCall, callInProg, StringComparison.OrdinalIgnoreCase))
            {
                DebugOutput($"{spacer}OnQsoLogged: CQ mode QSO complete, resuming CQ");
                RemoveCall(dxCall);
                CancelQso();
                cqPaused = false;
                SetupCq(true);
            }
        }

        // Builds the ADIF-style field dictionary and record text from a QsoLoggedMessage,
        // claims the QSO by its dedup key (see ClaimLiveLoggedQso), and if not already
        // handled via the LoggedAdifMessage fallback, fires the UI feedback and hands off
        // to the shared import/upload tail (see ImportLiveLoggedQso).
        private void HandleLiveQsoLogged(QsoLoggedMessage qMsg)
        {
            double freqMhz = qMsg.TxFrequency / 1_000_000.0;
            string freqMhzStr = freqMhz.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
            string band = AdifImporter.NormalizeBand("", freqMhzStr);
            string qsoDate = qMsg.DateTimeOn.ToString("yyyyMMdd");
            string timeOn  = qMsg.DateTimeOn.ToString("HHmmss");
            string timeOff = qMsg.DateTimeOff.ToString("HHmmss");

            string dedupKey = AdifImporter.BuildDedupKey(qMsg.DxCall ?? "", band, qMsg.Mode ?? "", qsoDate, timeOn);
            if (!ClaimLiveLoggedQso(dedupKey)) return;

            OnQsoLogged(qMsg.DxCall);

            var fields = new Dictionary<string, string>
            {
                ["CALL"]             = qMsg.DxCall ?? "",
                ["BAND"]             = band,
                ["FREQ"]             = freqMhzStr,
                ["MODE"]             = qMsg.Mode ?? "",
                ["QSO_DATE"]         = qsoDate,
                ["TIME_ON"]          = timeOn,
                ["TIME_OFF"]         = timeOff,
                ["RST_SENT"]         = qMsg.ReportSent ?? "",
                ["RST_RCVD"]         = qMsg.ReportReceived ?? "",
                ["GRIDSQUARE"]       = qMsg.DxGrid ?? "",
                ["NAME"]             = qMsg.Name ?? "",
                ["COMMENT"]          = qMsg.Comments ?? "",
                ["TX_PWR"]           = qMsg.TxPower ?? "",
                ["OPERATOR"]         = qMsg.OperatorCall ?? "",
                ["STATION_CALLSIGN"] = qMsg.MyCall ?? "",
                ["MY_GRIDSQUARE"]    = qMsg.MyGrid ?? "",
                ["STX_STRING"]       = qMsg.ExchangeSent ?? "",
                ["SRX_STRING"]       = qMsg.ExchangeReceived ?? "",
            };
            EnrichWithClubLogGeoData(fields, qMsg.DxCall);

            string adifRecord = AdifRecordBuilder.Build(
                qMsg.DxCall ?? "", band, (long)qMsg.TxFrequency, qMsg.Mode ?? "",
                qsoDate, timeOn, timeOff, qMsg.ReportSent ?? "", qMsg.ReportReceived ?? "",
                qMsg.DxGrid ?? "", qMsg.Name ?? "", qMsg.Comments ?? "", qMsg.TxPower ?? "",
                qMsg.OperatorCall ?? "", qMsg.MyCall ?? "", qMsg.MyGrid ?? "",
                qMsg.ExchangeSent ?? "", qMsg.ExchangeReceived ?? "");

            ImportLiveLoggedQso(qMsg.DxCall, fields, adifRecord, dedupKey);
        }

        // Fallback trigger for the same event QsoLoggedMessage normally handles -- WSJT-X
        // sends both messages for every logged QSO, so if one is ever dropped in transit
        // the other still gets the QSO into Jimmy's log/awards (see _liveLoggedQsoKeys).
        private void HandleLiveAdifLogged(LoggedAdifMessage aMsg)
        {
            Dictionary<string, string> fields;
            try
            {
                fields = AdifParser.Parse(aMsg.AdifText).FirstOrDefault();
            }
            catch (Exception ex)
            {
                DebugOutput($"{Time()} HandleLiveAdifLogged: could not parse ADIF text: {ex.Message}");
                return;
            }
            if (fields == null) return;

            string dxCall  = fields.TryGetValue("CALL", out var callVal) ? callVal : null;
            string band    = fields.TryGetValue("BAND", out var bandVal) ? bandVal : "";
            string mode    = fields.TryGetValue("MODE", out var modeVal) ? modeVal : "";
            string qsoDate = fields.TryGetValue("QSO_DATE", out var dateVal) ? dateVal : "";
            string timeOn  = fields.TryGetValue("TIME_ON", out var timeVal) ? timeVal : "";
            EnrichWithClubLogGeoData(fields, dxCall);

            string dedupKey = AdifImporter.BuildDedupKey(dxCall ?? "", band, mode, qsoDate, timeOn);
            if (!ClaimLiveLoggedQso(dedupKey)) return;

            OnQsoLogged(dxCall);
            ImportLiveLoggedQso(dxCall, fields, aMsg.AdifText, dedupKey);
        }

        // Fills COUNTRY/DXCC/CONT/CQZ from the callsign's DXCC prefix (Club Log's country
        // database, downloaded automatically at startup and available offline) when the
        // source message didn't supply them -- WSJT-X's own QsoLoggedMessage/LoggedAdifMessage
        // protocol never includes these, so without this a live-logged QSO's award status
        // (DXCC/WAZ/Continents awards) wouldn't reflect it until a later QRZ/LoTW/Club Log
        // sync backfilled the row. Only fills gaps; never overwrites a value already present.
        private void EnrichWithClubLogGeoData(Dictionary<string, string> fields, string call)
        {
            if (string.IsNullOrEmpty(call)) return;
            var entity = ctrl.lookupManager?.ClubLog?.FindByCallsign(call);
            if (entity == null) return;

            if (!fields.TryGetValue("COUNTRY", out var country) || string.IsNullOrEmpty(country))
                fields["COUNTRY"] = entity.Name ?? "";
            if ((!fields.TryGetValue("DXCC", out var dxcc) || string.IsNullOrEmpty(dxcc)) && entity.Adif > 0)
                fields["DXCC"] = entity.Adif.ToString();
            if (!fields.TryGetValue("CONT", out var cont) || string.IsNullOrEmpty(cont))
                fields["CONT"] = entity.Continent ?? "";
            if ((!fields.TryGetValue("CQZ", out var cqz) || string.IsNullOrEmpty(cqz)) && entity.CqZone > 0)
                fields["CQZ"] = entity.CqZone.ToString();
        }

        // Feeds a just-logged QSO into Jimmy's local logbook database (via the same
        // dedup-safe AdifImporter pipeline already used for QRZ/LoTW/manual imports,
        // so My Log/Awards/Still Need reflect it immediately) and, if the user has
        // opted into real-time upload for QRZ and/or Club Log, sends it there too.
        // Runs on a background task so a slow/failed network call never blocks
        // WSJT-X message processing; all exceptions are caught and logged, never
        // allowed to propagate. Shared by both the QsoLoggedMessage and LoggedAdifMessage
        // code paths -- adifRecord is either built from QsoLoggedMessage's typed fields, or
        // (for the LoggedAdifMessage path) is the exact ADIF text WSJT-X itself logged.
        private void ImportLiveLoggedQso(string dxCall, Dictionary<string, string> fields, string adifRecord, string dedupKey)
        {
            LiveQsoUploader.ImportLiveLoggedQso(dxCall, fields, adifRecord, dedupKey);
        }

        private void ProcessDecodeMsg(EnqueueDecodeMessage dmsg, bool isSpecOp)
        {
            if (dmsg.AutoGen && (dmsg.DeltaFrequency > offsetLoLimit && dmsg.DeltaFrequency < offsetHiLimit)) audioOffsets.Add(dmsg.DeltaFrequency);
            timeOffsets.Add(dmsg.DeltaTime);

            decodeCount++;
            consecNoDecodes = 0;

            if (opMode != OpModes.ACTIVE)
            {
                DebugOutput($"{spacer}ProcessDecodeMsg: skipped opMode:{opMode} msg:'{dmsg.Message}'");
                return;
            }

            if (dmsg.Message.Contains("..."))
            {
                DebugOutput($"{nl}{Time()}");
                DebugOutput($"{dmsg}{nl}{spacer}msg:'{dmsg.Message}'");
                DebugOutput($"{nl}{spacer}ProcessDecodeMsg(4), rejected: contains ...");
                return;
            }


            string deCall = dmsg.DeCall();
            string toCall = dmsg.ToCall();
            bool isContest = false;
            bool isInvalidType = false;

            if ((isContest = dmsg.IsContest()) || (isInvalidType = dmsg.IsInvalidType()) || deCall == null || toCall == null)
            {
                // Contest/FD-format message directed to my callsign — the caller must reach the waiting list
                if (isContest && toCall == myCall && deCall != null) { }
                else
                {
                    DebugOutput($"{nl}{Time()}");
                    DebugOutput($"{dmsg}{nl}{spacer}msg:'{dmsg.Message}'");
                    DebugOutput($"{spacer}ProcessDecodeMsg(3), rejected: isContest:{isContest} isInvalidType:{isInvalidType} deCall:'{deCall}' toCall:'{toCall}'");
                    return;
                }
            }

            bool toMyCall = dmsg.IsCallTo(myCall);
            lastCallActivity[deCall] = dmsg;    //most recent decode from deCall, regardless of who it's directed to -- see IsStationBusyElsewhere()
            dmsg.OffAir = true;     //default: play sound

            //do some processing not directly related to replying immediately
            //set initial priority
            dmsg.SequenceNumber = NextMsgSeqNum();
            dmsg.Priority = (int)CallPriority.DEFAULT;
            if (toMyCall) dmsg.Priority = (int)CallPriority.TO_MYCALL;       //as opposed to a decode from anyone else
            if (dmsg.IsNewCountryOnBand) dmsg.Priority = (int)CallPriority.NEW_COUNTRY_ON_BAND;
            if (dmsg.IsNewCountry) dmsg.Priority = (int)CallPriority.NEW_COUNTRY;

            // Upgrade directed-alert CQ calls to WANTED_CQ unconditionally — whether a CQ is
            // directed (e.g. CQ POTA) is a property of the message, not of the operator's filter
            // settings.  The Call Filters decide whether the classified call is admitted; the
            // classification itself must not depend on filter state.
            if (dmsg.Priority == (int)CallPriority.DEFAULT && dmsg.IsCQ())
            {
                string directedTo = WsjtxMessage.DirectedTo(dmsg.Message);
                if (IsDirectedAlert(directedTo, dmsg.IsDx))
                    dmsg.Priority = (int)CallPriority.WANTED_CQ;
            }

            dmsg.Category = DeriveCategory(dmsg);   //after Priority set; before SetRank
            CheckAwardAlert(dmsg);   // independent of Category/Call Filters admission -- see method comment
            SetRank(dmsg);      //only after priority set

            UpdateCallQueue(deCall, dmsg);      //newest call from station replaces older in call queue, so quality is kept current for sorting


            //detect previous signoff before adding call to allCallDict
            bool recdPrevSignoff = RecdSignoff(deCall);

            //if caller has not already signed off (with 73 or RR73) or already logged:
            //current msg (not to myCall) might be replaceable by a previous msg (to myCall)
            //rec'd later in the QSO cycle than this msg;
            //this is the case when a caller stops calling myCall
            /*//and starts CQing again or is new country replying to another caller
            if (!toMyCall && dmsg.AutoGen && !logList.Contains(deCall) && !recdPrevSignoff && (dmsg.IsCQ() || dmsg.IsNewCountryOnBand))
            {
                List<EnqueueDecodeMessage> msgList;
                if (allCallDict.TryGetValue(deCall, out msgList))
                {
                    //find latest msg from deCall to myCall
                    EnqueueDecodeMessage rmsg;
                    if ((rmsg = msgList.FindLast(RogerReport)) != null || (rmsg = msgList.FindLast(Report)) != null || (rmsg = msgList.FindLast(Reply)) != null)
                    {
                        if (rmsg.DeCall() != null && rmsg.ToCall() != null)     //sanity check
                        {
                            DebugOutput($"{Time()}");
                            DebugOutput($"{dmsg}{nl}{spacer}msg:'{dmsg.Message}'");
                            DebugOutput($"{spacer}ProcessDecodeMsg(2), substitute msg found:");
                            DebugOutput($"{rmsg}{nl}{spacer}msg:'{rmsg.Message}'");
                            dmsg.Message = rmsg.Message;
                            dmsg.OffAir = false;        //flag no sound, no save
                            toMyCall = true;
                        }
                    }
                }
            }*/

            if (toMyCall && dmsg.AutoGen)
            {
                DebugOutput($"{nl}{Time()}");
                DebugOutput($"{dmsg}{nl}{spacer}msg:'{dmsg.Message}' decodeCycle:{CurrentDecodeCycleString()} decodesProcessed:{decodesProcessed} cqPaused:{cqPaused}");
                DebugOutput($"{spacer}ProcessDecodeMsg(1), deCall:'{deCall}' callInProg:'{CallPriorityString(callInProg)}' recdPrevSignoff:{recdPrevSignoff}");
                DebugOutput($"{spacer}txEnabled:{txEnabled} transmitting:{transmitting} restartQueue:{restartQueue} RecdAnyMsg:{RecdAnyMsg(deCall)}");

                consecTimeoutCount = 0;

                if (deCall == discardCall)      //reset count of periods with no call from discardCall
                {
                    discardCallCycleCount = 0;
                    DebugOutput($"{spacer}discardCall:'{discardCall}' discardCallCycleCount:{discardCallCycleCount}");
                }

                int prevTo = 0;
                bool tmpBlock = false;
                bool isPota = IsPotaCall(dmsg);
                int maxTo = MaxTimeoutsForMsg(isPota);
                timeoutCallDict.TryGetValue(deCall, out prevTo);
                DebugOutput($"{spacer}prevTo:{prevTo} maxTo:{maxTo}");
                if (!dmsg.Is73orRR73() && !dmsg.IsRogers() && prevTo >= maxTo)        //trouble finishing signal report(s)
                {
                    StatusView.ShowMessage($"Blocking {deCall} temporarily...", false);
                    DebugOutput($"{spacer}ignoring call, prevTo:{prevTo} restartQueue:{restartQueue}");
                    tmpBlock = true;
                }

                CheckLateLog(deCall, dmsg);
                UpdateDebug();

                //if call not already logged: save Report (...+03) and RogerReport (...R-02) decodes for out-of-order call processing
                //except save RR73 and 73 for later recdPrevSignoff check, also needed for status display
                //don't save if a substituted message
                if ((!logList.Contains(deCall) || dmsg.Is73orRR73()) && dmsg.OffAir)
                {
                    AddAllCallDict(deCall, dmsg);
                }

                if (IsBlocked(deCall) || tmpBlock)
                {
                    StatusView.ShowMessage($"{deCall} is blocked)", false);
                    if (debugDetail) DebugOutput($"{spacer}{deCall} ignored, blocked");
                    return;
                }

                // Weak-signal floor: never suppress the station we're actively working —
                // SNR can dip on a final RR73/73 and we must not drop a QSO in progress.
                if (ctrl.ignoreWeakSnrCheckBox.Checked && dmsg.Snr <= (int)ctrl.minSnrNumUpDown.Value && deCall != callInProg)
                {
                    if (debugDetail) DebugOutput($"{spacer}{deCall} ignored, weak signal snr:{dmsg.Snr} floor:{(int)ctrl.minSnrNumUpDown.Value}");
                    return;
                }

                DebugOutput($"{spacer}deCall:{deCall} dmsg.Priority:{dmsg.Priority} callQueue.Contains:{callQueue.Contains(deCall)} SentAnyMsg:{SentAnyMsg(deCall)}");
                //if calling CQ DX and ignore non-DX replies
                if (!dmsg.IsDx
                    && dmsg.Priority > (int)CallPriority.NEW_COUNTRY_ON_BAND
                    && txMode == TxModes.CALL_CQ
                    && ctrl.callCqDxCheckBox.Checked
                    && ctrl.ignoreNonDxCheckBox.Checked
                    && !callQueue.Contains(deCall)
                    && !SentAnyMsg(deCall)
                    && !dmsg.Is73orRR73()
                    )
                {
                    StatusView.ShowMessage($"{deCall} ignored (not DX)", false);
                    DebugOutput($"{spacer}{deCall} ignored, DX only");
                    return;
                }

                consecTxCount = 0;          //reset Tx hold count since we're being heard
                DebugOutput($"{spacer}consecTxCount:0");

                bool isCorrectTimePeriod = IsCorrectTimePeriodForMode(dmsg);
                DebugOutput($"{spacer}isCorrectTimePeriod:{isCorrectTimePeriod}");

                bool ignore = (dmsg.Is73() || (dmsg.IsRR73() && !ctrl.replyRR73CheckBox.Checked)) && logList.Contains(deCall);
                if (ignore)
                {
                    finalSignoffCall = deCall;
                    ShowStatus();
                }

                if (!txEnabled && deCall != null && !dmsg.Is73orRR73())
                {
                    if (!callQueue.Contains(deCall))
                    {
                        if (isCorrectTimePeriod)
                        {
                            DebugOutput($"{spacer}'{deCall}' not in queue");
                            AddCall(deCall, dmsg);

                            //check for call after decodes "done"
                            if (decodesProcessed && !cqPaused)
                            {
                                DebugOutput($"{spacer}late decode(1), restartQueue:{restartQueue}");
                                StartProcessDecodeTimer2();
                            }
                            if (!_lastAddCallCategoryPlayed) Sounds.PlaySoundEvent(ctrl.callAddedCheckBox.Checked, ctrl.soundFile_CallAdded);
                        }
                    }
                    else
                    {
                        DebugOutput($"{spacer}'{deCall}' already in queue");
                        UpdateCall(deCall, dmsg);
                    }
                    UpdateDebug();
                }

                //decode processing of calls to myCall requires txEnabled
                if (txEnabled && deCall != null)
                {
                    DebugOutput($"{spacer}'{deCall}' is to {myCall}");
                    if ((deCall == callInProg || (txTimeout && deCall == tCall)) && recdPrevSignoff)        //cancel call in progress
                    {
                        restartQueue = true;
                        DebugOutput($"{spacer}already rec'd signoff, restartQueue:{restartQueue} qsoState:{qsoState}");
                    }
                    else
                    {
                        if (!dmsg.Is73orRR73())       //not a 73 or RR73
                        {
                            DebugOutput($"{spacer}not a 73 or RR73");
                            if (deCall != callInProg)
                            {
                                DebugOutput($"{spacer}{deCall} is not callInProg:{CallPriorityString(callInProg)}");
                                if (!callQueue.Contains(deCall))        //call not in queue, enqueue the call data
                                {
                                    if (isCorrectTimePeriod)
                                    {
                                        DebugOutput($"{spacer}'{deCall}' not already in queue");
                                        AddCall(deCall, dmsg);

                                        //check for high-priority call after decodes "done"
                                        DebugOutput($"{spacer}transmitting:{transmitting} qsoState:{qsoState}");
                                        if (decodesProcessed && !cqPaused)
                                        {
                                            DebugOutput($"{spacer}late decode(2), restartQueue:{restartQueue}");
                                            StartProcessDecodeTimer2();
                                        }
                                        if (!_lastAddCallCategoryPlayed) Sounds.PlaySoundEvent(ctrl.callAddedCheckBox.Checked, ctrl.soundFile_CallAdded);
                                    }

                                }
                                else       //call is already in queue, update the call data
                                {
                                    DebugOutput($"{spacer}'{deCall}' already in queue");
                                    UpdateCall(deCall, dmsg);
                                }
                            }
                            else        //call is in progress
                            {
                                DebugOutput($"{spacer}{CallPriorityString(deCall)} is callInProg, txTimeout:{txTimeout} cancelledCall:'{cancelledCall}' isSpecOp:{isSpecOp}");

                                if (isCorrectTimePeriod)
                                {
                                    AddCall(deCall, dmsg);

                                    if ((txEnabled && txMode == TxModes.LISTEN) || (!cqPaused && txMode == TxModes.CALL_CQ))
                                    {
                                        replyFromInProg = true;       //special status shown when callInProg will be replied to
                                        DebugOutput($"{spacer}replyFromInProg:{replyFromInProg}");
                                        ShowStatus();
                                    }

                                    //check for call after decodes "done"
                                    if (decodesProcessed && !cqPaused)
                                    {
                                        DebugOutput($"{spacer}late decode(3), restartQueue:{restartQueue}");
                                        StartProcessDecodeTimer2();
                                    }
                                    if (!_lastAddCallCategoryPlayed && deCall != callInProg) Sounds.PlaySoundEvent(ctrl.callAddedCheckBox.Checked, ctrl.soundFile_CallAdded);
                                }
                            }
                        }
                        else        //decode is 73 or RR73 msg
                        {
                            DebugOutput($"{spacer}decode is 73 or RR73, IsRR73:{dmsg.IsRR73()} isSpecOp:{isSpecOp} checked:{ctrl.replyRR73CheckBox.Checked} priority:{Priority(deCall)} contains:{logList.Contains(deCall)}");
                            if (deCall == callInProg)       ///check for ignore 73 or RR73
                            {
                                //                 WSJT-X will not reply automatically to RR73 for F/H msg
                                if (dmsg.Is73() || (dmsg.IsRR73() && isSpecOp) || !logList.Contains(deCall) || (!ctrl.replyRR73CheckBox.Checked && Priority(deCall) > (int)CallPriority.NEW_COUNTRY_ON_BAND))     //if new country, RR73 gets a 73 reply
                                {
                                    if (dmsg.IsRR73()) DebugOutput($"{spacer}WSJT-X not replying to RR73");
                                    restartQueue = true;
                                    DebugOutput($"{spacer}call is in progress, restartQueue:{restartQueue}");
                                    if ((decodesProcessed && !cqPaused) || transmitting)
                                    {
                                        //prevent calling CQ when not wanted
                                        DebugOutput($"{spacer}late decode (RR)73, restartQueue:{restartQueue}");
                                        //StartProcessDecodeTimer2();
                                    }
                                }
                                else        //allow WSJT-X to reply automatically to RR73
                                {
                                    DebugOutput($"{spacer}WSJT-X is replying to RR73");
                                    AddTimeoutCall(deCall);
                                }
                            }
                            else        //deCall is not call in progress
                            {
                                //check for required reply to RR73
                                if (isCorrectTimePeriod && logList.Contains(deCall) && dmsg.IsRR73() && (ctrl.replyRR73CheckBox.Checked || Priority(deCall) <= (int)CallPriority.NEW_COUNTRY_ON_BAND))        //call not in queue, enqueue the call data
                                {
                                    AddTimeoutCall(deCall);
                                    //allow RR73 to be processed
                                    if (!callQueue.Contains(deCall))
                                    {
                                        DebugOutput($"{spacer}'{deCall}' not already in queue");
                                        AddCall(deCall, dmsg);
                                        if (!_lastAddCallCategoryPlayed) Sounds.PlaySoundEvent(ctrl.callAddedCheckBox.Checked, ctrl.soundFile_CallAdded);
                                    }
                                    else
                                    {
                                        UpdateCall(deCall, dmsg);
                                    }
                                }
                                else //don't process the 73 or RR73
                                {
                                    RemoveCall(deCall);     //may have been added manually
                                }
                            }
                        }
                    }
                    UpdateDebug();
                }
            }
            else    //not toMyCall or is not auto-generated
            {
                //only resulting action is to add call to callQueue, optionally restart queue
                AddSelectedCall(dmsg);              //known to be "new" and not "replay
                UpdateDebug();
            }

            return;
        }

        private bool CheckActive()
        {
            //*****************************************
            //check for transition from START to ACTIVE
            //****************************************
            if (commConfirmed && myCall != null && supportedModes.Contains(mode) && specOp == 0 && opMode == OpModes.START && (!ctrl.freqCheckBox.Checked || !_requireOffsetForActive || (oddOffset > 0 || evenOffset > 0)))
            {
                opMode = OpModes.ACTIVE;
                if (txMode == TxModes.LISTEN)
                {
                    Pause(true, false);
                }

                UpdateModeVisible();
                UpdateBandComboBox();
                UpdateCallListAccessibleName();
                ctrl.LoadHrcCache();    //refresh HRC sets (band-independent; harmless to re-run here)
                ctrl.RefreshStillNeedCache();    //reload Still Need live-tag cache now that the current band is known
                DebugOutput($"{spacer}CheckActive, opMode:{opMode}");
                UpdateDebug();
                return true;
            }
            return false;
        }

        private void StartProcessDecodeTimer()
        {
            DateTime dtNow = DateTime.UtcNow;
            int diffMsec = ((dtNow.Second * 1000) + dtNow.Millisecond) % (int)trPeriod;
            int cycleTimerAdj = CalcTimerAdj();
            processDecodeTimer.Interval = (2 * (int)trPeriod) - diffMsec - cycleTimerAdj;
            processDecodeTimer.Start();
            DebugOutput($"{Time()} processDecodeTimer start: interval:{processDecodeTimer.Interval} msec");
        }

        private bool CheckMyCall(StatusMessage smsg)
        {
            if (smsg.DeCall == null || smsg.DeGrid == null || smsg.DeGrid.Length < 4)
            {
                heartbeatRecdTimer.Stop();
                suspendComm = true;
                ctrl.BringToFront();
                MessageBox.Show($"Call sign and Grid are not entered in WSJT-X.{nl}{nl}Enter these in WSJT-X:{nl}- Select 'File | Settings' then the 'General' tab.{nl}{nl}(Grid must be at least 4 characters){nl}{nl}{pgmName} will try again when you close this dialog.", pgmName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ResetOpMode();
                ShowStatus();
                suspendComm = false;
                return false;
            }

            if (myCall == null)
            {
                myCall = smsg.DeCall;
                myGrid = smsg.DeGrid;
                DebugOutput($"{spacer}CheckMyCall myCall:{myCall} myGrid:{myGrid}");
            }

            UpdateDebug();
            return true;
        }

        private void CheckNextXmit()
        {
            //can be called anytime, but will be called at least once per decode period shortly before the tx period begins;
            //can result in tx enabled (or disabled)
            DebugOutput($"{Time()} CheckNextXmit, txTimeout:{txTimeout} callQueue.Count:{callQueue.Count} qsoState:{qsoState}");
            DateTime dtNow = DateTime.UtcNow;      //helps with debugging to do this here

            //*******************
            //Best Tx freq update
            //*******************
            if (autoFreqPauseMode == autoFreqPauseModes.ENABLED)        //auto freq update started
            {
                DebugOutput($"{spacer}CheckNextXmit(4) start");
                autoFreqPauseMode = autoFreqPauseModes.ACTIVE;
                UpdateCallInProg();
                DebugOutput($"{spacer}auto freq update continue");
                DebugOutput($"{spacer}CheckNextXmit(4) end, autoFreqPauseMode:{autoFreqPauseMode}");
                return;
            }
            else if (autoFreqPauseMode == autoFreqPauseModes.ACTIVE)        //end auto freq update
            {
                DebugOutput($"{spacer}CheckNextXmit(5) start");
                DebugOutput($"{spacer}auto freq update end");
                DisableAutoFreqPause();
                if (txMode == TxModes.CALL_CQ || callInProg != null) EnableTx();
                DebugOutput($"{spacer}CheckNextXmit(5) end, autoFreqPauseMode:{autoFreqPauseMode}");
            }

            //********************
            //Tx state processing
            //********************
            //check for time to resume CQing mode,
            //or disable tx for listen mode
            if (txTimeout)        //important to sync qso logged to end of xmit, and manually-added call(s) to status msgs
            {
                DebugOutput($"{spacer}CheckNextXmit(1) start");
                DebugOutput($"{spacer}callQueue.Count:{callQueue.Count} ctrl.freqCheckBox.Checked:{ctrl.freqCheckBox.Checked} mode:'{mode}'");
                DebugOutput($"{CallQueueString()}");

                //start CQing (or if Listening: prepare for replying) 
                if (txMode == TxModes.LISTEN)
                {
                    Pause(true, false);
                    consecTxCount = 0;
                    DebugOutput($"{spacer}consecTxCount:0");
                }
                else        //CQ mode
                {
                    DebugOutput($"{spacer}no entries in queue, start CQing");
                    SetupCq(true);      //also sets WSJT-X "Tx Enable" button state
                }
                restartQueue = false;           //get ready for next decode phase
                txTimeout = false;              //ready for next timeout
                tCall = null;
                xmitCycleCount = 0;

                DebugOutputStatus();
                DebugOutput($"{spacer}CheckNextXmit(1) end, restartQueue:{restartQueue} txTimeout:{txTimeout}");
                UpdateDebug();      //unconditional
                return;             //don't process newDirCq
            }

            //*************************************
            //Directed CQ / new setting / best freq
            //*************************************
            if (txMode == TxModes.CALL_CQ && qsoState == WsjtxMessage.QsoStates.CALLING)
            {
                DebugOutput($"{spacer}CheckNextXmit(4) start");
                if (callInProg == null)
                {

                    DebugOutput($"{spacer}CheckNextXmit(2) start");
                    if (ctrl.freqCheckBox.Checked && oddOffset > 0 && evenOffset > 0)
                    {
                        //set/show frequency offset for period after decodes started
                        emsg.NewTxMsgIdx = 10;
                        emsg.GenMsg = $"";          //no effect
                        emsg.SkipGrid = ctrl.skipGridCheckBox.Checked;
                        emsg.UseRR73 = ctrl.useRR73CheckBox.Checked;
                        emsg.CmdCheck = "";         //ignored
                        emsg.Offset = AudioOffsetFromTxPeriod();
                        ba = emsg.GetBytes();
                        udpClient2.Send(ba, ba.Length);
                        DebugOutput($"{Time()} >>>>>Sent 'Opt Req' cmd:10{nl}{emsg}");
                        if (settingChanged)
                        {
                            ctrl.WsjtxSettingConfirmed();
                            settingChanged = false;
                        }
                    }

                    if (newDirCq)
                    {
                        emsg.NewTxMsgIdx = 6;
                        emsg.GenMsg = $"CQ{NextDirCq()} {myCall} {myGrid}";
                        emsg.SkipGrid = ctrl.skipGridCheckBox.Checked;
                        emsg.UseRR73 = ctrl.useRR73CheckBox.Checked;
                        emsg.CmdCheck = "";         //ignored
                        ba = emsg.GetBytes();           //set up for CQ, auto, call 1st
                        udpClient2.Send(ba, ba.Length);
                        DebugOutput($"{Time()} >>>>>Sent 'Setup CQ' cmd:6{nl}{emsg}");
                        qsoState = WsjtxMessage.QsoStates.CALLING;      //in case enqueueing call manually right now
                        replyCmd = null;        //invalidate last reply cmd since not replying
                        replyDecode = null;
                        curCmd = emsg.GenMsg;
                        newDirCq = false;
                        DebugOutput($"{spacer}newDirCq:{newDirCq}");
                        if (settingChanged)
                        {
                            ctrl.WsjtxSettingConfirmed();
                            settingChanged = false;
                        }
                    }

                    UpdateWsjtxOptions();
                    DebugOutputStatus();
                    DebugOutput($"{spacer}CheckNextXmit(4) end");
                    UpdateDebug();      //unconditional
                    return;
                }
                else
                {
                    //LogBeep();
                    DebugOutput($"{spacer}CheckNextXmit(4) end, callInProg:{callInProg}");
                }
            }
        }

        private void ProcessDecodes()
        {
            //always called shortly before the tx period begins
            cancelledCall = null;
            UpdateMaxTxRepeat();
            int maxDiscardCount = maxTxRepeat + 2;     //count number of rx periods since msg from discardCall last rec'd
            DebugOutput($"{nl}{Time()} ProcessDecodes: restartQueue:{restartQueue} txTimeout:{txTimeout} txEnabled:{txEnabled}{nl}{spacer}txMode:{txMode} cqPaused:{cqPaused} txEnabled:{txEnabled}");
            DebugOutput($"{spacer}cancelledCall:{cancelledCall} autoFreqPauseMode:{autoFreqPauseMode} callInProg:'{callInProg}' discardCall:'{discardCall}' discardCallCycleCount:{discardCallCycleCount}");
            DebugOutput($"{spacer}maxDiscardCount:{maxDiscardCount} maxTxRepeat:{maxTxRepeat}");
            DebugOutputStatus();

            if (TrimCallQueue())
            {
                DebugOutput(CallQueueString());
            }

            if (debug)
            {
                DebugOutput(AllCallDictString());
                DebugOutput(SentCallListString());
                DebugOutput(LogListString());
                DebugOutput(PotaLogDictString());
                DebugOutput(TimeoutCallDictString());
                //DebugOutput(ReportListString());
                DebugOutput(UnwantedCqListString());
            }

            if (discardCall != null && discardCall == callInProg && ++discardCallCycleCount >= maxDiscardCount) DiscardCall();

            if (restartQueue) 
            {
                txTimeout = true;       //important to only set this now, not during decode phase, since decodes can happen after Tx-starts
                SetCallInProg(null);    //not calling anyone (set this as late as possible to pick up possible reply to last Tx)
                DebugOutput($"{spacer}qsoState:{qsoState} txTimeout:{txTimeout} callInProg:'{CallPriorityString(callInProg)}'");
                UpdateDebug();
            }

            //check for call in progress with tx disabled
            if (!cqPaused && !txEnabled && callInProg != null && autoFreqPauseMode == autoFreqPauseModes.DISABLED)
            {
                DebugOutput($"{spacer}call in progress with tx disabled");
                //LogBeep();
                //EnableTx();
            }

            //check for auto freq update disabled while CQ mode previously in progress
            if (!cqPaused && !txEnabled && txMode == TxModes.CALL_CQ && autoFreqPauseMode == autoFreqPauseModes.DISABLED)
            {
                DebugOutput($"{spacer}auto freq update disabled while CQ mode previously in progress");
            }

            if ((((txTimeout || !txEnabled) && txMode == TxModes.LISTEN) || (!cqPaused && txMode == TxModes.CALL_CQ)) && callInProg != null && callQueue.Contains(callInProg))
            {
                DebugOutput($"{spacer}resume '{discardCall}' after timeout");
                DisableAutoFreqPause();
                CancelDiscardCall();            //no more retries
                timedOutCall = null;
                EnqueueDecodeMessage dummy;
                int idx = FindCall(callInProg, out dummy);
                if (idx >= 0)
                {
                    ReplyTo(idx);
                    replyFromInProg = true;
                    DebugOutput($"{spacer}resumed {callInProg}, replyFromInProg:{replyFromInProg} xmitCycleCount:{xmitCycleCount} timedOutCall:'{timedOutCall}'");
                    ShowStatus();
                }
            }
            //check for disable Tx in listen mode
            //or call timed out
            //or inhibit reply to 73/RR73
            //or process auto freq update enabled / in progress
            else if (!cqPaused && (autoFreqPauseMode != autoFreqPauseModes.DISABLED || txTimeout))
            {
                DebugOutput($"{spacer}check auto freq/disable tx");
                CheckNextXmit();        //can result in tx disabled)
            }
            else if (newDirCq)
            {
                CheckNextXmit();
            }
            else
            {
                UpdateWsjtxOptions();
            }
            var dtNow = DateTime.UtcNow;
            bool even = IsEvenPeriod((dtNow.Minute * 60) + dtNow.Second - 3);       //might be in start of transmit period when all decodes done
            EnqueueDecodeMessage dmsg;
            if (ctrl.soundsEnabled && even != txFirst && ctrl.callAddedCheckBox.Checked && callQueue.Count > 0 && PeekCall(0, out dmsg) != null)
            {
                if (dmsg.Quality >= (int)EnqueueDecodeMessage.Qualities.MEDIUM)
                {
                    Sounds.Play("chime.wav");
                }
                else
                {
                    DebugOutput($"{spacer}low quality msg:{dmsg.Message}");
                }
            }
            decodesProcessed = true;
            DebugOutput($"{Time()} ProcessDecodes done, decodesProcessed:{decodesProcessed}");
            UpdateDebug();
        }

        //check for time to log (best done at Tx-start to avoid any logging/dequeueing timing problem if done at Tx end)
        private void ProcessTxStart()
        {
            if (!ctrl.keepTransmitListDuringTx)
            {
                if (txFirst)   // TX1 transmitting → clear TX1
                {
                    _tx1SnapshotRows.Clear();
                    _tx1SnapshotCalls.Clear();
                    ctrl.advTx1ListBox.AccessibleName = "TX1 available stations, 0 calls";
                    UpdateListIfChanged(ctrl.advTx1ListBox, new List<string> { "No available stations" });
                }
                else           // TX2 transmitting → clear TX2
                {
                    _tx2SnapshotRows.Clear();
                    _tx2SnapshotCalls.Clear();
                    ctrl.advTx2ListBox.AccessibleName = "TX2 available stations, 0 calls";
                    UpdateListIfChanged(ctrl.advTx2ListBox, new List<string> { "No available stations" });
                }
            }

            string toCall = WsjtxMessage.ToCall(txMsg);
            curTxMsg = txMsg;       //the message displayed
            if (txMsg == "TUNE") tuning = true;
            lastStatusTxMsg = txMsg;     //status update for interrupted Tx not required
            string lastToCall = WsjtxMessage.ToCall(lastTxMsg);
            DebugOutput($"{nl}{Time()} WSJT-X event, Tx start: toCall:'{toCall}' lastToCall:'{lastToCall}' decodesProcessed:{decodesProcessed} processDecodeTimer interval:{processDecodeTimer.Interval} msec tuning:{tuning}");
            var dtNow = DateTime.UtcNow;
            SetTxStartInfo(dtNow, toCall);

            curTxPayload = null;

            if (toCall == null)
            {
                //WSJT-X replied to invalid message, process next msg
                txTimeout = true;
                SetCallInProg(null);       //call is expired now
                return;
            }

            DebugOutput($"{Time()} Tx start done: txMsg:'{txMsg}' lastTxMsg:'{lastTxMsg}' toCall:'{toCall}' lastToCall:'{lastToCall}'");
            UpdateCallInProg();
            RemoveCall(toCall);
            UpdateDebug();      //unconditional
        }

        //check for QSO end or timeout (and possibly logging (if txMsg changed between Tx start and Tx end)
        private void ProcessTxEnd()
        {
            string toCall = WsjtxMessage.ToCall(txMsg);
            string lastToCall = WsjtxMessage.ToCall(lastTxMsg);
            string deCall = WsjtxMessage.DeCall(replyCmd);
            string cmdToCall = WsjtxMessage.ToCall(curCmd);
            bool isCq = WsjtxMessage.IsCQ(txMsg);
            DateTime txEndTime = DateTime.UtcNow;
            shortTx = false;
            double? txTime = null;

            if (txMsg == "TUNE") tuning = false;
            DebugOutput($"{nl}{Time()} WSJT-X event, Tx end: toCall:'{toCall}' lastToCall:'{lastToCall}' deCall:'{deCall}' cmdToCall:'{cmdToCall}' tuning:{tuning}");
            DebugOutput($"{spacer}toCallTxStart:{toCallTxStart} decodesProcessed:{decodesProcessed} txEndTime:{txEndTime.ToString("HHmmss.fff")} maxTxRepeat:{maxTxRepeat}");

            if (toCall == null)
            {
                if (txMsg == "TUNE")
                {
                    DisableTx(false);
                    HaltTx();          //****this syncs txEnable state with WSJT-X****
                    return;
                }

                //WSJT-X replied to invalid message, process next msg
                txTimeout = true;
                SetCallInProg(null);
                return;
            }

            if (toCall == discardCall)
            {
                discardCallCycleCount = 0;
                DebugOutput($"{spacer}discardCall:'{discardCall}' discardCallCycleCount:{discardCallCycleCount}");
            }

            UpdateCallInProg();
            DebugOutputStatus();

            //toCallTxStart = null is a special case: intentional interruption
            //allow interruption if tx time was long enough
            txInterrupted = (toCallTxStart != null && toCall != toCallTxStart);
            if ((mode == "FT8" || mode == "FT4") && txBeginTime != DateTime.MaxValue)
            {
                //FT8: 12.64 sec, FT4: 4.48 sec tx time normally
                int shortTxMsec = (mode == "FT8" ? 11000 : 3500);       //how short tx can be and still be assumed a valid tx
                txTime = (txEndTime - txBeginTime).TotalMilliseconds;
                shortTx = (txTime < shortTxMsec);
            }

            DebugOutput($"{spacer}txTime:{txTime}");

            if (shortTx || txInterrupted)           //tx was invalid
            {
                DebugOutput($"{spacer}shortTx:{shortTx} txInterrupted:{txInterrupted} tx originally to '{toCallTxStart}'");
            }
            else
            {
                //check for max Tx count during Tx hold
                if (ctrl.freqCheckBox.Checked && autoFreqPauseMode == autoFreqPauseModes.DISABLED && (ctrl.holdCheckBox.Checked || txMode == TxModes.LISTEN))
                {
                    consecTxCount++;
                    if (consecTxCount >= maxConsecTxCount)
                    {
                        if (autoFreqPauseMode == autoFreqPauseModes.DISABLED)
                        {
                            DisableTx(true);
                            autoFreqPauseMode = autoFreqPauseModes.ENABLED;
                            UpdateCallInProg();
                            DebugOutput($"{spacer}auto freq update started (consec Tx), autoFreqPauseMode:{autoFreqPauseMode}");
                        }
                        else
                        {
                            consecTxCount = 0;
                        }
                    }
                }
                else
                {
                    consecTxCount = 0;
                }
                DebugOutput($"{spacer}autoFreqPauseMode:{autoFreqPauseMode} consecTxCount:{consecTxCount}");

                //could have clicked on "CQ" button in WSJT-X
                if (isCq)
                {
                    DebugOutput($"{spacer}possible CQ button, callInProg:'{CallPriorityString(callInProg)}'");

                    if (txMode == TxModes.LISTEN)
                    {
                        txTimeout = true;
                        if (discardCall == null) SetCallInProg(null);       //call expires now
                        DebugOutput($"{spacer}txTimeout:{txTimeout} txMode:{txMode}");
                    }

                    //check for consecutive CQs sent
                    if (ctrl.freqCheckBox.Checked && autoFreqPauseMode == autoFreqPauseModes.DISABLED && txMode == TxModes.CALL_CQ)
                    {
                        if (++consecCqCount >= maxConsecCqCount)
                        {
                            if (autoFreqPauseMode == autoFreqPauseModes.DISABLED)
                            {
                                DisableTx(true);
                                autoFreqPauseMode = autoFreqPauseModes.ENABLED;
                                UpdateCallInProg();
                                DebugOutput($"{spacer}auto freq update started (CQs)");
                            }
                            else
                            {
                                consecCqCount = 0;
                            }
                        }
                    }
                    else
                    {
                        consecCqCount = 0;
                    }
                    DebugOutput($"{spacer}toCall:{toCall} autoFreqPauseMode:{autoFreqPauseMode} consecCqCount:{consecCqCount} consecTimeoutCount:{consecTimeoutCount}");
                }
                else    //toCall not CQ
                {
                    consecCqCount = 0;
                    DebugOutput($"{spacer}consecCqCount:{consecCqCount} consecTimeoutCount:{consecTimeoutCount}");
                }

                if (debug)
                {
                    DebugOutput($"{spacer}logEarlyCheckBox:{ctrl.logEarlyCheckBox.Checked} IsRogers:{WsjtxMessage.IsRogers(txMsg)} RecdReport:{RecdReport(toCall)} RecdRogerReport:{RecdRogerReport(toCall)}{nl}{spacer}sentReportList.Contains:{sentReportList.Contains(toCall)} logList.Contains:{logList.Contains(toCall)} sentCallList.Contains:{sentCallList.Contains(toCall)}");
                }

                if (!logList.Contains(toCall))          //toCall not logged yet this mode/band for this session
                {
                    //check for time to log early; NOTE: doing this at Tx end because WSJT-X may have changed Tx msgs (between Tx start and Tx end) due to late-decode for the current call
                    //  option enabled                   just sent RRR                and prev. recd Report  or prev. recd RogerReport   and prev. sent any report
                    if (IsLogEarly(toCall) && WsjtxMessage.IsRogers(txMsg) && (RecdReport(toCall) || RecdRogerReport(toCall)) && sentReportList.Contains(toCall))
                    {
                        DebugOutput($"{spacer}early logging: toCall:'{toCall}'");
                        LogQso(toCall);
                    }
                    //check for QSO completed, trigger next call in the queue
                    if (WsjtxMessage.Is73orRR73(txMsg))
                    {
                        txTimeout = true;
                        tCall = toCall;
                        xmitCycleCount = 0;
                        SetCallInProg(null);
                        DebugOutput($"{spacer}reset(2): (is 73 or RR73) xmitCycleCount:{xmitCycleCount} txTimeout:{txTimeout}{nl}           callInProg:'{CallPriorityString(callInProg)}' tCall:'{tCall}'");

                        //NOTE: doing this at Tx end because WSJT-X may have changed Tx msgs (between Tx start and Tx end) due to late-decode for the current call
                        // prev. recd Report    or prev. recd RogerReport   and prev. sent any report
                        if ((RecdReport(toCall) || RecdRogerReport(toCall)) && sentReportList.Contains(toCall))
                        {
                            DebugOutput($"{spacer}normal logging: toCall:'{toCall}'");
                            LogQso(toCall);
                        }
                    }
                }
                else    //logList contains toCall
                {
                    if (WsjtxMessage.Is73orRR73(txMsg))
                    {
                        txTimeout = true;      //timeout to Tx the next call in the queue
                        tCall = toCall;
                        xmitCycleCount = 0;
                        SetCallInProg(null);
                        DebugOutput($"{spacer}reset(6): (is 73 or RR73) xmitCycleCount:{xmitCycleCount} txTimeout:{txTimeout}{nl}           callInProg:'{CallPriorityString(callInProg)}' tCall:'{tCall}'");
                    }
                }

                //count tx cycles: check for changed Tx call in WSJT-X
                UpdateMaxTxRepeat();
                if (maxTxRepeat > 1 && !IsSameMessage(lastTxMsg, txMsg))
                {
                    if (xmitCycleCount >= 0)
                    {
                        //check  for "to" call changed since last xmit end
                        // !restartQueue = didn't just add this call to queue during late-decode that overlapped Tx start
                        if (!restartQueue && toCall != lastToCall && callQueue.Contains(toCall))
                        {
                            RemoveCall(toCall);         //manually switched to Txing a call that was also in the queue
                        }

                        if (ctrl.holdCheckBox.Checked && toCall == lastToCall)      //overall xmit limit during hold
                        {
                            xmitCycleCount++;
                        }
                        else
                        {
                            xmitCycleCount = 0;
                        }
                        DebugOutput($"{spacer}reset(1) (different msg) xmitCycleCount:{xmitCycleCount} txMsg:'{txMsg}' lastTxMsg:'{lastTxMsg}' holdCheckBox.Checked:{ctrl.holdCheckBox.Checked}");
                    }
                    lastTxMsg = txMsg;
                }
                else        //same "to" call as last xmit or maxTxRepeat = 1, count xmit cycles
                {
                    if (!isCq)        //don't count CQ (or non-std) calls
                    {
                        xmitCycleCount++;           //count xmits to same call sign at end of xmit cycle
                        DebugOutput($"{spacer}(same msg, or maxTxRepeat = 1) xmitCycleCount:{xmitCycleCount} txMsg:'{txMsg}' lastTxMsg:'{lastTxMsg}'");
                        DebugOutput($"{spacer}holdCheckBox.Checked:{ctrl.holdCheckBox.Checked} holdMaxTxRepeat:{holdMaxTxRepeat}");

                        if ((!ctrl.holdCheckBox.Checked && xmitCycleCount >= maxTxRepeat - 1) || (ctrl.holdCheckBox.Checked && xmitCycleCount >= holdMaxTxRepeat - 1))  //n msgs = n-1 diffs
                        {
                            xmitCycleCount = 0;
                            txTimeout = true;
                            if (discardCall == null) SetCallInProg(null);       //call expires now
                            timedOutCall = toCall;
                            tCall = toCall;        //call to remove from queue, will be null if non-std msg
                            lastTxMsg = null;
                            ctrl.holdCheckBox.Checked = false;

                            //this caller might call indefinitely, so count call attempts
                            AddTimeoutCall(toCall);
                            DebugOutput($"{spacer}reset(3) (timeout) xmitCycleCount:{xmitCycleCount} txTimeout:{txTimeout} tCall:'{tCall}' callInProg:'{CallPriorityString(callInProg)}' holdCheckBox.Checked:{ctrl.holdCheckBox.Checked}");
                        }
                    }
                    else
                    {
                        //same CQ or non-std call
                        xmitCycleCount = 0;
                        DebugOutput($"{spacer}reset(4) (no action, CQ or non-std) xmitCycleCount:{xmitCycleCount}");
                    }
                }

                if (txTimeout)      //CQ or reply timed out
                {
                    DebugOutput($"{spacer}'{tCall}' timed out or completed");
                    RemoveCall(tCall);

                    if (!isCq) consecCqCount = 0;
                    //auto freq update when too many timed out replies
                    DebugOutput($"{spacer}ctrl.freqCheckBox.Checked:{ctrl.freqCheckBox.Checked} autoFreqPauseMode:{autoFreqPauseMode} txMode:{txMode} toCall:{toCall} mode:'{mode}'");
                    if (ctrl.freqCheckBox.Checked && autoFreqPauseMode == autoFreqPauseModes.DISABLED && txMode == TxModes.CALL_CQ && toCall != "CQ")
                    {
                        consecTimeoutCount += maxTxRepeat;
                        if (consecTimeoutCount >= maxConsecTimeoutCount)
                        {
                            if (autoFreqPauseMode == autoFreqPauseModes.DISABLED)
                            {
                                DisableTx(true);
                                autoFreqPauseMode = autoFreqPauseModes.ENABLED;
                                UpdateCallInProg();
                                DebugOutput($"{spacer}auto freq update started (no QSOs)");
                            }
                            else
                            {
                                consecTimeoutCount = 0;
                            }
                        }
                    }
                    else
                    {
                        consecTimeoutCount = 0;
                    }
                    DebugOutput($"{spacer}txTimeout:{txTimeout} autoFreqPauseMode:{autoFreqPauseMode} consecTimeoutCount:{consecTimeoutCount} callQueue.Count:{callQueue.Count} consecCqCount:{consecCqCount}");
                }

                //check for time to process new directed CQ
                if (txMode == TxModes.CALL_CQ && (toCall == "CQ" || qsoState == WsjtxMessage.QsoStates.CALLING) && (ctrl.callCqDxCheckBox.Checked || (ctrl.callDirCqCheckBox.Checked && ctrl.directedTextBox.Text.Trim().Length > 0)))
                {
                    xmitCycleCount = 0;
                    newDirCq = true;
                    DebugOutput($"{spacer}reset(5) (new directed CQ) xmitCycleCount:{xmitCycleCount} newDirCq:{newDirCq}");
                }
            }

            ShowStatus();           //**before** adding to sentReportList and sentCallList

            //save all call signs a report msg was (completely/partially) sent to
            if (!isCq && !sentCallList.Contains(toCall)) sentCallList.Add(toCall);
            if ((WsjtxMessage.IsReport(txMsg) || WsjtxMessage.IsRogerReport(txMsg)) && !sentReportList.Contains(toCall)) sentReportList.Add(toCall);

            txBeginTime = DateTime.MaxValue;
            DebugOutput($"{Time()} Tx end done, lastTxMsg:'{lastTxMsg}' txEnabled:{txEnabled} cqPaused:{cqPaused}");
            UpdateDebug();      //unconditional
        }

        private void UpdateWsjtxOptions()
        {
            if (settingChanged)
            {
                emsg.NewTxMsgIdx = 10;
                emsg.GenMsg = $"";          //no effect
                emsg.SkipGrid = ctrl.skipGridCheckBox.Checked;
                emsg.UseRR73 = ctrl.useRR73CheckBox.Checked;
                emsg.CmdCheck = "";         //ignored
                emsg.Offset = 0;            //ignored
                ba = emsg.GetBytes();
                udpClient2.Send(ba, ba.Length);
                DebugOutput($"{Time()} >>>>>Sent 'Opt Req' cmd:10{nl}{emsg}");

                ctrl.WsjtxSettingConfirmed();
                settingChanged = false;
            }
        }

        //log a QSO (early or normal timing in QSO progress)
        private void LogQso(string call)
        {
            List<EnqueueDecodeMessage> msgList;
            if (!allCallDict.TryGetValue(call, out msgList)) return;          //no previous call(s) from DX station
            EnqueueDecodeMessage rMsg;
            if ((rMsg = msgList.FindLast(RogerReport)) == null && (rMsg = msgList.FindLast(Report)) == null) return;        //the DX station never reported a signal
            if (!sentReportList.Contains(call)) return;         //never reported SNR to the DX station
            RequestLog(call, rMsg, null);
        }

        private bool IsEvenPeriod(int secPastHour)          //or seconds since midnight
        {
            if (mode == "FT4")          //irregular
            {
                int sec = secPastHour % 60;     //seconds past the minute
                return (sec >= 0 && sec < 7) || (sec >= 15 && sec < 22) || (sec >= 30 && sec < 37) || (sec >= 45 && sec < 52);
            }

            return (secPastHour / (trPeriod / 1000)) % 2 == 0;
        }

        private bool IsEvenCall(EnqueueDecodeMessage d)
        {
            return IsEvenPeriod((d.SinceMidnight.Minutes * 60) + d.SinceMidnight.Seconds);
        }

        private string NextDirCq()
        {
            string dirCq = "";

            List<string> dirList = new List<string>();
            if (ctrl.callNonDirCqCheckBox.Checked)
            {
                dirList.Add("");            //note zero length
            }

            if (ctrl.callCqDxCheckBox.Checked)
            {
                dirList.Add("DX");
            }

            if ((ctrl.callDirCqCheckBox.Checked && ctrl.directedTextBox.Text.Trim().Length > 0))
            {
                dirList.AddRange(ctrl.CallDirCqEntries());
            }

            if (dirList.Count > 0)
            {
                string s = dirList[rnd.Next(dirList.Count)];
                if (s.Length <= 4 && s.Length > 0) dirCq = " " + s;          //is directed else non-directed
                DebugOutput($"{spacer}dirCq:'{dirCq}'");
            }

            return dirCq;
        }

        private void ResetNego()
        {
            WsjtxMessage.Reinit();                      //NegoState = WAIT;
            heartbeatRecdTimer.Stop();
            cmdCheckTimer.Stop();
            DebugOutput($"{nl}{Time()} ResetNego, NegoState:{WsjtxMessage.NegoState}");
            ResetOpMode();
            DebugOutput($"{Time()} Waiting for WSJT-X to run...");
            cmdCheck = RandomCheckString();
            commConfirmed = false;
            UpdateRR73();
            ShowStatus();
            UpdateDebug();
        }

        private void ResetOpMode()
        {
            StopDecodeTimers();
            postDecodeTimer.Stop();
            decodeCycle = 0;
            decodeCount = 0;
            consecNoDecodes = 0;
            DebugOutput($"{Time()} ResetOpMode, postDecodeTimer stop, decodeCycle:{decodeCycle}");
            ClearCalls(true);
            cqPaused = true;
            if (WsjtxMessage.NegoState != WsjtxMessage.NegoStates.WAIT) HaltTx();
            opMode = OpModes.IDLE;
            lastMode = null;
            lastSpecOp = null;
            lastDecoding = null;
            lastXmitting = null;
            bandIdx = null;
            decodesProcessed = false;
            myCall = null;
            myGrid = null;
            SetCallInProg(null);
            txTimeout = false;
            restartQueue = false;
            replyCmd = null;
            curCmd = null;
            replyDecode = null;
            tCall = null;
            newDirCq = false;
            dxCall = null;
            xmitCycleCount = 0;
            logList.Clear();        //can re-log on new mode, band, or session
            ShowLogged();
            UpdateModeVisible();
            UpdateModeSelection();
            UpdateDebug();
            UpdateBandComboBox();
            UpdateDblClkTip();
            AutoFreqChanged(ctrl.freqCheckBox.Checked, true);
            ctrl.holdCheckBox.Checked = false;
            DebugOutput($"{nl}{Time()} ResetOpMode, opMode:{opMode} NegoState:{WsjtxMessage.NegoState} cqPaused:{cqPaused}");
        }

        private void ClearCalls(bool clearBandSpecific)             //if only changing Tx period, keep info for the current band, since may return to original Tx period
        {
            callQueue.Clear();
            UpdateMaxTxRepeat();
            callDict.Clear();
            if (clearBandSpecific)
            {
                timeoutCallDict.Clear();
                allCallDict.Clear();
                sentCallList.Clear();
                sentReportList.Clear();
                unwantedCqList.Clear();
            }
            decodeNum = 0;
            ShowQueue();
            if (ctrl.advancedCallLayout) ShowAdvancedQueue(null);
            xmitCycleCount = 0;
            timedOutCall = null;
            CancelDiscardCall();
            ctrl.holdCheckBox.Checked = false;  //reset hold
            DebugOutput($"{Time()} ClearCalls, clearBandSpecific:{clearBandSpecific} decodeNum:{decodeNum} xmitCycleCount:{xmitCycleCount}");
            StopDecodeTimers();
        }

        private void ClearAudioOffsets()
        {
            oddOffset = 0;
            evenOffset = 0;
            cachedOddOffset = 0;
            cachedEvenOffset = 0;
            period = Periods.UNK;
            DisableAutoFreqPause();
            skipFirstDecodeSeries = true;
            timeOffset = 0;
            analysisCompleted = false;
            pendingCqAfterAnalysis = false;
            DebugOutput($"{Time()} [BAND-AUDIT] ClearAudioOffsets: bandIdx:{bandIdx} skipFirstDecodeSeries:{skipFirstDecodeSeries} mode:'{mode}'");
        }

        //update call in call queue
        //if to myCall and has progressed in the FT8 QSO protocol, or
        //if priority increased or if grid now available;
        //return true if call added
        private bool UpdateCall(string call, EnqueueDecodeMessage msg)
        {
            DebugOutput($"{Time()} UpdateCall");
            EnqueueDecodeMessage dmsg;
            if (callDict.TryGetValue(call, out dmsg))
            {
                if (WsjtxMessage.ToCall(msg.Message) == myCall && WsjtxMessage.ToCall(dmsg.Message) == myCall && WsjtxMessage.Progress(msg.Message) > WsjtxMessage.Progress(dmsg.Message))
                {
                    DebugOutput($"{spacer}update stage/sequence '{msg.Message}' (was '{dmsg.Message}')");
                    RemoveCall(call);
                    return AddCall(call, msg);      //re-ranked
                }

                if (call != null && callDict.ContainsKey(call))
                {
                    //check for call saved as a low-priority CQ but now high-priority call to myCall
                    if ((msg.Priority < dmsg.Priority)
                        || (WsjtxMessage.Grid(dmsg.Message) == null && WsjtxMessage.Grid(msg.Message) != null))
                    {
                        if (IsCorrectTimePeriodForMode(msg))
                        {
                            DebugOutput($"{spacer}update priority/grid  '{msg.Message}' (was '{dmsg.Message}')");
                            RemoveCall(call);
                            return AddCall(call, msg);      //re-ranked
                        }
                    }
                }
            }
            return false;
        }

        //remove call from queue/dictionary;
        //call not required to be present
        //return false if failure
        private bool RemoveCall(string call, bool updateSnapshots = true)
        {
            EnqueueDecodeMessage msg;
            if (call != null && callDict.TryGetValue(call, out msg))     //dictionary contains call data for this call sign
            {
                callDict.Remove(call);

                string[] qArray = new string[callQueue.Count];
                callQueue.CopyTo(qArray, 0);
                callQueue.Clear();
                for (int i = 0; i < qArray.Length; i++)
                {
                    if (qArray[i] != call) callQueue.Enqueue(qArray[i]);
                }

                if (callDict.Count != callQueue.Count)
                {
                    DebugOutput("ERROR: queueDict and callDict out of sync");
                    UpdateDebug();
                    return false;
                }

                ShowQueue();
                if (updateSnapshots && ctrl.advancedCallLayout) ShowAdvancedQueue(null);
                if (debugDetail) DebugOutput($"{spacer}removed {call}{nl}{CallQueueString()}");
                UpdateMaxTxRepeat();
                return true;
            }
            if (debugDetail) DebugOutput($"{spacer}not removed, not in callQueue '{call}'{nl}{CallQueueString()}");
            return false;
        }

        //add call/decode to queue/dict; call rank already set;
        //place in queue according to priority then rank using current rankMethod;
        //set sequence number if not already set
        //return false if already added
        private bool AddCall(string call, EnqueueDecodeMessage msg)
        {
            _lastAddCallCategoryPlayed = false;
            var callArray = callQueue.ToArray();        //make queue accessible by index

            if (debugDetail) DebugOutput($"{Time()} AddCall, call:{call} priority:{msg.Priority} cat:{msg.Category} rank:{msg.Rank}");
            if (!callDict.ContainsKey(call))     //dictionary does not contain call data for this call sign
            {
                var tmpQueue = new Queue<string>();         //will be the updated queue

                //go thru calls in reverse time order
                int i;
                for (i = 0; i < callArray.Length; i++)
                {
                    EnqueueDecodeMessage decode;
                    if (!callDict.TryGetValue(callArray[i], out decode))     //get the decode for an existing call in the queue
                    {
                        DebugOutput("ERROR: queueDict and callDict out of sync");
                        UpdateDebug();
                        return false;
                    }
                    if (CompareRank(decode, msg) <= 0)         //reached insertion point for new call
                    {
                        break;
                    }
                    else
                    {
                        tmpQueue.Enqueue(callArray[i]); //add the existing priority call 
                    }
                }
                tmpQueue.Enqueue(call);         //add the new priority call (before oldest non-priority call, or at end of all-priority-call queue)

                //fill in the remaining non-priority callls
                for (int j = i; j < callArray.Length; j++)
                {
                    tmpQueue.Enqueue(callArray[j]);
                }
                callQueue = tmpQueue;

                callDict.Add(call, msg);
                _lastAddCallCategoryPlayed = PlayCategorySound(msg);

                // Feature 2: opposite-period alert — fires when an interesting call is queued
                // on the period opposite to the operator's current TX/RX focus.
                if (ctrl.soundEnabled_OppositePeriod
                    && msg.Category != CallCategory.DEFAULT
                    && IsEvenCall(msg) == txFirst   // call is on our TX period, not our listen period
                    && IsAlertCooledDown(_oppositePeriodAlertTimes, call, OppositePeriodAlertCooldownSecs))
                {
                    Sounds.PlaySoundEvent(ctrl.soundEnabled_OppositePeriod, ctrl.soundFile_OppositePeriod);
                    _oppositePeriodAlertTimes[call] = DateTime.UtcNow;
                    DebugOutput($"{spacer}OppositePeriod alert: '{call}' cat:{msg.Category} txFirst:{txFirst}");
                }

                ShowQueue();
                if (ctrl.advancedCallLayout) ShowAdvancedQueue(IsEvenCall(msg));
                if (lookupManager != null && msg.Country.Length == 0 && lookupManager.CanAutoQueue(call))
                    lookupManager.QueueAutoLookup(call);
                else if (ctrl.showUsStateCheckBox.Checked &&
                         msg.Country == "USA" &&
                         lookupManager != null &&
                         lookupManager.CanAutoQueue(call) &&
                         GridToUsState(WsjtxMessage.Grid(msg.Message)) == null)
                    lookupManager.QueueAutoLookup(call);
                if (debugDetail) DebugOutput($"{spacer}enqueued {call}{nl}{CallQueueString()}");
                UpdateMaxTxRepeat();
                UpdateCallInProg();
                return true;
            }
            if (debugDetail) DebugOutput($"{spacer}already enqueued {call}{nl}{CallQueueString()}");
            return false;
        }

        //return index/msg of specified call in queue
        //queue not assumed to have any entries;
        //return -1 if failure
        private int FindCall(string call, out EnqueueDecodeMessage dmsg)
        {
            dmsg = null;
            if (call == null) return -1;
            int idx = Array.IndexOf(callQueue.ToArray(), call);
            if (idx < 0) return -1;

            if (PeekCall(idx, out dmsg) == null) return -1;
            return idx;
        }

        //return call/msg at specified index in queue;
        //queue not assume to have any entries;
        //return null if failure
        private string PeekCall(int idx, out EnqueueDecodeMessage dmsg)
        {
            dmsg = null;
            if (callQueue.Count == 0)
            {
                DebugOutput($"{spacer}no peek");
                return null;
            }

            var callArray = callQueue.ToArray();
            if (idx < 0 || idx >= callArray.Length)
            {
                DebugOutput($"{spacer}out of range, idx:{idx}");
                return null;
            }
            string call = callArray[idx];

            if (!callDict.TryGetValue(call, out dmsg))
            {
                DebugOutput("ERROR: '{call}' not found");
                UpdateDebug();
                return null;
            }

            DebugOutput($"{spacer}peek {call}: msg:'{dmsg.Message}'");
            return call;
        }

        private string RemoveCallLast()
        {
            if (callQueue.Count == 0) return null;
            var callArray = callQueue.ToArray();
            string call = callArray[callArray.Length - 1];
            RemoveCall(call);
            return call;
        }

        private string RemoveCallLastForPeriod(bool isEven)
        {
            if (callQueue.Count == 0) return null;
            var callArray = callQueue.ToArray();
            EnqueueDecodeMessage d;
            for (int i = callArray.Length - 1; i >= 0; i--)
            {
                if (callDict.TryGetValue(callArray[i], out d) && IsEvenCall(d) == isEven)
                {
                    RemoveCall(callArray[i]);
                    return callArray[i];
                }
            }
            return null;
        }

        private int PeriodCallCount(bool isEven)
        {
            EnqueueDecodeMessage d;
            int count = 0;
            foreach (var call in callQueue)
                if (callDict.TryGetValue(call, out d) && IsEvenCall(d) == isEven)
                    count++;
            return count;
        }

        private string CallQueueString()
        {
            string delim = "";
            int count = 0;
            StringBuilder sb = new StringBuilder();
            sb.Append($"{spacer}callQueue [");
            foreach (string call in callQueue)
            {
                int pri = 0;
                int rank = 0;
                int qual = 0;
                string msg = "";
                TimeSpan sm = TimeSpan.MinValue;
                EnqueueDecodeMessage d;
                if (callDict.TryGetValue(call, out d))
                {
                    pri = d.Priority;
                    rank = d.Rank;
                    qual = d.Quality;
                    sm = d.SinceMidnight;
                    msg = d.Message;
                }
                //int prevTo;
                //timeoutCallDict.TryGetValue(call, out prevTo);

                if (++count % (debugDetail ? 2 : 5) == 0)
                {
                    sb.Append($"{nl}{spacer}");
                    delim = "";
                }

                if (debugDetail)
                {
                    sb.Append($"{delim}{call}:'{msg}'/{sm.Minutes.ToString().PadLeft(2, '0')}{sm.Seconds.ToString().PadLeft(2, '0')}/{pri}/{qual}/{rank}");
                }
                else
                {
                    sb.Append($"{delim}{call}:{sm.Minutes.ToString().PadLeft(2, '0')}{sm.Seconds.ToString().PadLeft(2, '0')}/{pri}/{qual}");
                }
                delim = ", ";
            }
            sb.Append("]");
            return sb.ToString();
        }

        private string ReportListString()
        {
            string delim = "";
            int count = 0;
            StringBuilder sb = new StringBuilder();
            sb.Append($"{spacer}sentReportList [");
            foreach (string call in sentReportList)
            {
                if (++count % 12 == 0)
                {
                    sb.Append($"{nl}{spacer}");
                    delim = "";
                }

                sb.Append(delim + call);
                delim = " ";
            }
            sb.Append("]");
            return sb.ToString();
        }

        private string SentCallListString()
        {
            string delim = "";
            int count = 0;
            StringBuilder sb = new StringBuilder();
            sb.Append($"{spacer}sentCallList [");
            foreach (string call in sentCallList)
            {
                if (++count % 12 == 0)
                {
                    sb.Append($"{nl}{spacer}");
                    delim = "";
                }

                sb.Append(delim + call);
                delim = " ";
            }
            sb.Append("]");
            return sb.ToString();
        }

        private string UnwantedCqListString()
        {
            string delim = "";
            int count = 0;
            StringBuilder sb = new StringBuilder();
            sb.Append($"{spacer}unwantedCqList [");
            foreach (string call in unwantedCqList)
            {
                if (++count % 12 == 0)
                {
                    sb.Append($"{nl}{spacer}");
                    delim = "";
                }

                sb.Append(delim + call);
                delim = " ";
            }
            sb.Append("]");
            return sb.ToString();
        }

        private string LogListString()
        {
            string delim = "";
            int count = 0;
            StringBuilder sb = new StringBuilder();
            sb.Append($"{spacer}logList [");
            foreach (string call in logList)
            {
                if (++count % 12 == 0)
                {
                    sb.Append($"{nl}{spacer}");
                    delim = "";
                }
                sb.Append(delim + call);
                delim = " ";
            }
            sb.Append("]");
            return sb.ToString();
        }

        private string CallDictString()
        {
            string delim = "";
            StringBuilder sb = new StringBuilder();
            sb.Append("callDict [");
            foreach (var entry in callDict)
            {
                sb.Append(delim + entry.Key);
                delim = " ";
            }
            sb.Append("]");
            return sb.ToString();
        }

        private string TimeoutCallDictString()
        {
            int count = 0;
            string delim = "";
            StringBuilder sb = new StringBuilder();
            sb.Append($"{spacer}timeoutCallDict [");
            foreach (var entry in timeoutCallDict)
            {
                sb.Append($"{delim}{entry.Key} {entry.Value}");
                delim = ", ";
                if (++count % 8 == 0)
                {
                    sb.Append($"{nl}{spacer}");
                    delim = "";
                }
            }
            sb.Append("]");
            return sb.ToString();
        }

        private string AllCallDictString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"{spacer}allCallDict");
            if (allCallDict.Count == 0)
            {
                sb.Append(" []");
            }
            else
            {
                sb.Append(":");
            }

            foreach (var entry in allCallDict)
            {
                sb.Append($"{nl}{spacer}{entry.Key} ");
                string delim = "";
                sb.Append("[");
                foreach (EnqueueDecodeMessage msg in entry.Value)
                {
                    sb.Append($"{delim}{msg.Message}:{msg.Priority} @{msg.SinceMidnight}");
                    delim = ", ";
                }
                sb.Append("]");
            }

            return sb.ToString();
        }

        private string PotaLogDictString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"{spacer}potaLogDict");
            if (potaLogDict.Count == 0)
            {
                sb.Append(" []");
            }
            else
            {
                sb.Append(":");
            }
            foreach (var entry in potaLogDict)
            {
                string delim = "";
                sb.Append($"{nl}{spacer}{entry.Key} [");
                foreach (var info in entry.Value)
                {
                    sb.Append($"{delim}{info}");
                    delim = "  ";
                }
                sb.Append("]");
            }

            return sb.ToString();
        }

        private string Time()
        {
            var dt = DateTime.UtcNow;
            return dt.ToString("HHmmss.fff");
        }

        public void Closing()
        {
            DebugOutput($"{nl}{nl}{DateTime.UtcNow.ToString("yyyy-MM-dd HHmmss")} UTC ###################### Program closing...");
            if (opMode > OpModes.IDLE) HaltTx();
            ResetOpMode();
            ShowStatus();
            heartbeatRecdTimer.Stop();
            cmdCheckTimer.Stop();
            DebugOutput($"{spacer}heartbeatRecdTimer stop");

            try
            {
                if (emsg != null && udpClient2 != null)
                {
                    //notify WSJT-X
                    emsg.NewTxMsgIdx = 0;           //de-init WSJT-X
                    emsg.GenMsg = $"";         //ignored
                    emsg.SkipGrid = ctrl.skipGridCheckBox.Checked;
                    emsg.UseRR73 = ctrl.useRR73CheckBox.Checked;
                    emsg.CmdCheck = "";         //ignored
                    ba = emsg.GetBytes();
                    udpClient2.Send(ba, ba.Length);
                    DebugOutput($"{Time()} >>>>>Sent 'De-init Req' cmd:0{nl}{emsg}");
                    Thread.Sleep(500);
                    udpClient2.Close();
                    udpClient2 = null;
                    DebugOutput($"{spacer}closed udpClient2:{udpClient2}");
                }
            }
            catch (Exception e)         //udpClient might be disposed already
            {
                DebugOutput($"{spacer}error at Closing, error:{e.ToString()}");
            }

            CloseAllUdp();

            if (potaSw != null)
            {
                potaSw.Flush();
                potaSw.Close();
                potaSw = null;
            }

            SetLogFileState(false);         //close log file
        }

        public void Dispose()
        {
        }


        private bool PlayCategorySound(EnqueueDecodeMessage msg)
        {
            string call = msg.DeCall();
            switch (msg.Category)
            {
                case CallCategory.TO_MYCALL:
                    return Sounds.PlaySoundEvent(ctrl.mycallCheckBox.Checked, ctrl.soundFile_CallingMe, call, "CALLING_ME");
                case CallCategory.NEW_COUNTRY:
                    return Sounds.PlaySoundEvent(ctrl.soundEnabled_NewDxcc, ctrl.soundFile_NewDxcc, call, "NEW_COUNTRY");
                case CallCategory.NEW_COUNTRY_ON_BAND:
                    return Sounds.PlaySoundEvent(ctrl.soundEnabled_NewDxccOnBand, ctrl.soundFile_NewDxccOnBand, call, "NEW_COUNTRY_ON_BAND");
                case CallCategory.ALWAYS_WANTED:
                    return Sounds.PlaySoundEvent(ctrl.soundEnabled_AlwaysWanted, ctrl.soundFile_AlwaysWanted, call, "ALWAYS_WANTED");
                case CallCategory.WANTED_CQ:
                    if (IsPotaCall(msg) && ctrl.soundEnabled_Pota && !string.IsNullOrEmpty(ctrl.soundFile_Pota))
                        return Sounds.PlaySoundEvent(ctrl.soundEnabled_Pota, ctrl.soundFile_Pota, call, "POTA");
                    if (IsSotaCall(msg) && ctrl.soundEnabled_Sota && !string.IsNullOrEmpty(ctrl.soundFile_Sota))
                        return Sounds.PlaySoundEvent(ctrl.soundEnabled_Sota, ctrl.soundFile_Sota, call, "SOTA");
                    return Sounds.PlaySoundEvent(ctrl.soundEnabled_DirectedCq, ctrl.soundFile_DirectedCq, call, "DIRECTED_CQ");
                case CallCategory.POTA:
                    return Sounds.PlaySoundEvent(ctrl.soundEnabled_Pota, ctrl.soundFile_Pota, call, "POTA");
                case CallCategory.SOTA:
                    return Sounds.PlaySoundEvent(ctrl.soundEnabled_Sota, ctrl.soundFile_Sota, call, "SOTA");
                case CallCategory.STILL_NEEDED:
                    // The award-match sound is handled uniformly by CheckAwardAlert, which
                    // runs independently of Category/admission for every decode -- returning
                    // true here just prevents the generic "Call added" fallback from also
                    // playing for the same, already-alerted station.
                    return true;
                default:
                    return false;
            }
        }

        private bool IsAlertCooledDown(Dictionary<string, DateTime> dict, string call, int cooldownSecs)
        {
            DateTime last;
            if (!dict.TryGetValue(call, out last)) return true;
            return (DateTime.UtcNow - last).TotalSeconds >= cooldownSecs;
        }

        private string CategoryTag(EnqueueDecodeMessage d)
        {
            switch (d.Category)
            {
                case CallCategory.NEW_COUNTRY:         return "New DXCC";
                case CallCategory.NEW_COUNTRY_ON_BAND: return "New DXCC on band";
                case CallCategory.ALWAYS_WANTED:       return "Wanted";
                case CallCategory.WANTED_CQ:
                    return "";  // pri field already shows the directed-to target
                case CallCategory.POTA:                return "POTA";
                case CallCategory.SOTA:                return "SOTA";
                case CallCategory.WAS_NEEDED:          return "WAS Needed";
                case CallCategory.DXCC_UNCONFIRMED:    return "DXCC Unconf";
                case CallCategory.ZONE_NEEDED:         return "Zone Needed";
                case CallCategory.STILL_NEEDED:        return AwardDisplayName(d) + " Needed";
                default:                               return "";
            }
        }

        // Looks up the display name of whichever active award this message matched
        // (stashed on the message by DeriveCategory/CheckAwardAlert), falling back to a
        // generic label if the rule can't be found (e.g. unchecked between match and display).
        private string AwardDisplayName(EnqueueDecodeMessage d)
        {
            ActiveAwardTag tag;
            if (!string.IsNullOrEmpty(d.MatchedAwardRuleId) && activeAwardTags.TryGetValue(d.MatchedAwardRuleId, out tag))
                return tag.RuleName;
            return "Still";
        }

        private void ShowQueue()
        {
            int q = callQueue.Count;
            bool callInProgInQueue = callInProg != null && callQueue.Contains(callInProg);
            int displayQ = callInProgInQueue ? q - 1 : q;

            // Build the new row list completely in memory before touching the UI.
            // callInProg is excluded from the display rows; _callListBoxQueueIndices maps
            // each remaining display row back to its true queue position so that
            // Enter/double-click/right-click still address the correct queue entry.
            var newItems = new List<string>();
            var newKeys = new List<string>();
            var newQueueIndices = new List<int>();
            SelectionMode newMode;

            if (displayQ == 0)
            {
                newMode = SelectionMode.None;
                newItems.Add(callInProg == null
                    ? "[No stations calling or in progress]"
                    : "[No stations calling]");
                newKeys.Add(null);      // keep keys parallel to items even for the placeholder row
            }
            else
            {
                newMode = SelectionMode.One;
                int queuePos = 0;
                foreach (string call in callQueue)
                {
                    if (callInProgInQueue && StringComparer.OrdinalIgnoreCase.Equals(call, callInProg))
                    { queuePos++; continue; }
                    EnqueueDecodeMessage d;
                    if (callDict.TryGetValue(call, out d))
                    {
                        newItems.Add(BuildCallWaitingRow(call, d));
                        newKeys.Add(call);
                        newQueueIndices.Add(queuePos);
                    }
                    queuePos++;
                }
            }
            _callListBoxQueueIndices = newQueueIndices;

            // Advanced TX1/TX2 lists are driven by retained snapshots updated only by
            // AddCall (and global clears). ShowQueue never touches them so that
            // RemoveCall and TrimCallQueue cannot erase the opposite side's display.

            QueueView.RenderCallQueue($"Stations calling: {displayQ}", newItems, newKeys, newMode);
        }

        public void RefreshCallWaitingRows()
        {
            ShowQueue();
            if (ctrl.advancedCallLayout) ShowAdvancedQueue(null);
        }

        public void RefreshAdvancedLists()
        {
            if (!ctrl.advancedCallLayout) return;
            ShowAdvancedQueue();
            if (ctrl.advShowRaw) ShowRawDecodes();
        }

        private void ShowAdvancedQueue(bool? evenSide = null)
        {
            // evenSide==true  → only TX1 (even) snapshot is rebuilt (AddCall for TX1).
            // evenSide==false → only TX2 (odd)  snapshot is rebuilt (AddCall for TX2).
            // evenSide==null  → both snapshots rebuilt (ClearCalls, sort, debug, startup).
            //
            // RemoveCall and TrimCallQueue never call this method, so the snapshot for
            // each side is frozen between its own AddCall events — the opposite side's
            // retained display is never touched.
            bool rebuildTx1 = evenSide == null || evenSide == true;
            bool rebuildTx2 = evenSide == null || evenSide == false;

            // While a side is our active Tx slot and the user has "keep transmit list
            // during Tx" unchecked, keep that side's snapshot forcibly empty here instead
            // of repopulating it -- otherwise any decode/queue change that happens mid-
            // transmission (very common) silently refills it before the Tx cycle even
            // ends, undoing ProcessTxStart()'s clear. Resumes populating normally the
            // moment transmitting goes false (Tx end) for that side.
            bool suppressTx1 = !ctrl.keepTransmitListDuringTx && transmitting && txFirst;
            bool suppressTx2 = !ctrl.keepTransmitListDuringTx && transmitting && !txFirst;

            if (rebuildTx1)
            {
                _tx1SnapshotRows  = new List<string>();
                _tx1SnapshotCalls = new List<string>();
                if (!suppressTx1)
                {
                    foreach (string call in callQueue)
                    {
                        if (StringComparer.OrdinalIgnoreCase.Equals(call, callInProg)) continue;
                        EnqueueDecodeMessage d;
                        if (!callDict.TryGetValue(call, out d)) continue;
                        if (!IsEvenCall(d)) continue;
                        _tx1SnapshotCalls.Add(call);
                        _tx1SnapshotRows.Add(BuildCallWaitingRow(call, d));
                    }
                }
            }

            if (rebuildTx2)
            {
                _tx2SnapshotRows  = new List<string>();
                _tx2SnapshotCalls = new List<string>();
                if (!suppressTx2)
                {
                    foreach (string call in callQueue)
                    {
                        if (StringComparer.OrdinalIgnoreCase.Equals(call, callInProg)) continue;
                        EnqueueDecodeMessage d;
                        if (!callDict.TryGetValue(call, out d)) continue;
                        if (IsEvenCall(d)) continue;
                        _tx2SnapshotCalls.Add(call);
                        _tx2SnapshotRows.Add(BuildCallWaitingRow(call, d));
                    }
                }
            }

            if (ctrl.advShowTx1 && rebuildTx1)
            {
                bool tx1HasItems = _tx1SnapshotRows.Count > 0;
                string tx1Prefix = txFirst ? "TX1" : "RX1";
                string tx1Name = $"{tx1Prefix} available stations, {_tx1SnapshotRows.Count} calls";
                var display = tx1HasItems
                    ? _tx1SnapshotRows
                    : new List<string> { "No available stations" };
                var keys = tx1HasItems
                    ? _tx1SnapshotCalls
                    : new List<string> { null };
                QueueView.RenderAdvancedList(true, tx1Name, display, keys);
            }

            if (ctrl.advShowTx2 && rebuildTx2)
            {
                bool tx2HasItems = _tx2SnapshotRows.Count > 0;
                string tx2Prefix = txFirst ? "RX2" : "TX2";
                string tx2Name = $"{tx2Prefix} available stations, {_tx2SnapshotRows.Count} calls";
                var display = tx2HasItems
                    ? _tx2SnapshotRows
                    : new List<string> { "No available stations" };
                var keys = tx2HasItems
                    ? _tx2SnapshotCalls
                    : new List<string> { null };
                QueueView.RenderAdvancedList(false, tx2Name, display, keys);
            }
        }

        private string BuildCallWaitingRow(string call, EnqueueDecodeMessage d)
        {
            string snr = $", {d.Snr.ToString("+#;-#;0")}";
            string countryName = d.Country;
            if (countryName.Length == 0 && lookupManager != null)
            {
                var clEntity = lookupManager.GetClubLogEntity(call);
                if (clEntity != null) countryName = clEntity.Name;
            }
            string country = countryName.Length > 0 ? $", {countryName}" : "";

            string g = WsjtxMessage.Grid(d.Message);
            string grid = g == null ? "" : $", {SpacifyPayload(g)}";

            if (ctrl.showUsStateCheckBox.Checked &&
                d.Country == "USA" &&
                d.Priority != (int)CallPriority.NEW_COUNTRY_ON_BAND &&
                d.Priority != (int)CallPriority.NEW_COUNTRY)
            {
                string state = GridToUsState(g);
                if (state == null && lookupManager != null)
                {
                    var cached = lookupManager.GetCachedInfo(call);
                    if (!string.IsNullOrEmpty(cached?.State)) state = cached.State;
                }
                if (state != null) country = $", {state}";
            }

            int dist = metricUnits || d.Distance < 0 ? d.Distance : (int)((0.6213 * d.Distance) + 0.5);
            string unitsStr = metricUnits ? "km" : "mi";
            string distAz = (d.Distance >= 0 && d.Azimuth >= 0) ? $", {dist}{unitsStr}, {d.Azimuth}°" : "";

            string oe = debug ? $", {d.SinceMidnight.Minutes.ToString().PadLeft(2, '0')}:{d.SinceMidnight.Seconds.ToString().PadLeft(2, '0')}" : "";

            string to = WsjtxMessage.DirectedTo(d.Message);
            string dirTo = (to == null ? "" : $" {to}");
            string callp = $"{Spacify(call)}";
            string pri = (d.Priority == (int)CallPriority.TO_MYCALL) ? " replying" : (d.Priority == (int)CallPriority.WANTED_CQ ? dirTo : "");

            string rankStr = debug ? $", {d.Rank}" : "";
            string descr = debug ? $", {Reason(d)}" : "";
            string tagRaw = CategoryTag(d);
            string tagStr = tagRaw.Length > 0 ? $", {tagRaw}" : "";

            if (callWaitingRowOrderFields == null)
                return $"{callp}{pri}{tagStr}{grid}{snr}{country}{distAz}{oe}{descr}{rankStr}";

            var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "callp", callp }, { "pri", pri }, { "tag", tagStr }, { "grid", grid }, { "snr", snr },
                { "country", country }, { "distAz", distAz }, { "oe", oe },
                { "descr", descr }, { "rankStr", rankStr }
            };
            var sb = new System.Text.StringBuilder();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in callWaitingRowOrderFields)
            {
                if (string.IsNullOrEmpty(f) || seen.Contains(f)) continue;
                string frag;
                if (!fieldMap.TryGetValue(f, out frag)) continue;
                if (sb.Length == 0)
                {
                    if (frag.StartsWith(", ")) frag = frag.Substring(2);
                    else if (frag.Length > 0 && frag[0] == ' ') frag = frag.Substring(1);
                }
                else if (frag.Length > 0 && !frag.StartsWith(", ") && frag[0] != ' ')
                {
                    frag = ", " + frag;
                }
                sb.Append(frag);
                seen.Add(f);
            }
            return sb.Length == 0
                ? $"{callp}{pri}{tagStr}{grid}{snr}{country}{distAz}{oe}{descr}{rankStr}"
                : sb.ToString();
        }

        private void UpdateListIfChanged(ListBox lb, List<string> newItems)
        {
            bool changed = lb.Items.Count != newItems.Count;
            if (!changed)
            {
                for (int i = 0; i < newItems.Count; i++)
                {
                    if ((string)lb.Items[i] != newItems[i]) { changed = true; break; }
                }
            }
            if (!changed) return;

            lb.BeginUpdate();
            try
            {
                lb.Items.Clear();
                lb.Items.AddRange(newItems.ToArray());
            }
            finally { lb.EndUpdate(); }
        }

        private static readonly Dictionary<CallCategory, string> RawTagLabels =
            new Dictionary<CallCategory, string>
        {
            { CallCategory.NEW_COUNTRY,         "New DXCC" },
            { CallCategory.NEW_COUNTRY_ON_BAND, "New DXCC band" },
            { CallCategory.ALWAYS_WANTED,       "Wanted" },
            { CallCategory.TO_MYCALL,           "Calling me" },
            { CallCategory.MANUAL_SEL,          "Manual" },
            { CallCategory.WANTED_CQ,           "Dir CQ" },
            { CallCategory.POTA,                "POTA" },
            { CallCategory.SOTA,                "SOTA" },
            { CallCategory.WAS_NEEDED,          "WAS Needed" },
            { CallCategory.DXCC_UNCONFIRMED,    "DXCC Unconf" },
            { CallCategory.ZONE_NEEDED,         "Zone Needed" },
        };

        private void ShowRawDecodes()
        {
            var items = new List<string>();
            // Parallel to items; a decode's callsign alone isn't a unique-enough identity here
            // (the same station can appear in several rows -- CQ, reply, report, ...), so the
            // key includes enough of the decode to disambiguate the specific row.
            var keys = new List<string>();
            foreach (var d in _rawDecodeHistory)
            {
                if (!PassesRawDecodeFilter(d)) continue;

                string side = IsEvenCall(d) ? "TX1" : "TX2";
                var sb = new System.Text.StringBuilder();

                if (rawPriorityTags && d.Category != CallCategory.DEFAULT)
                {
                    string tag;
                    if (d.Category == CallCategory.WANTED_CQ)
                        tag = WsjtxMessage.DirectedTo(d.Message) ?? "Dir CQ";
                    else if (d.Category == CallCategory.STILL_NEEDED)
                        tag = AwardDisplayName(d) + " Needed";
                    else
                        RawTagLabels.TryGetValue(d.Category, out tag);
                    if (!string.IsNullOrEmpty(tag))
                        sb.Append($"{tag} ");
                }

                if (WsjtxMessage.IsFoxHound(d.Message))
                    sb.Append("Possible F/H ");

                sb.Append(side);
                sb.Append(": ");
                sb.Append(d.Message);

                if (ctrl.rawShowSnr)
                    sb.Append($", {d.Snr.ToString("+#;-#;0")}dB");

                string g = WsjtxMessage.Grid(d.Message);
                if (ctrl.rawShowGrid && g != null)
                    sb.Append($", {g}");

                if (ctrl.rawShowCountry && d.Country.Length > 0)
                    sb.Append($", {d.Country}");

                if (ctrl.rawShowState && d.Country == "USA" && g != null)
                {
                    string state = GridToUsState(g);
                    if (state != null) sb.Append($", {state}");
                }

                if (ctrl.rawShowDistAz && d.Distance >= 0 && d.Azimuth >= 0)
                {
                    int dist = metricUnits || d.Distance < 0 ? d.Distance : (int)((0.6213 * d.Distance) + 0.5);
                    string unitsStr = metricUnits ? "km" : "mi";
                    sb.Append($", {dist}{unitsStr} {d.Azimuth}°");
                }

                items.Add(sb.ToString());
                keys.Add($"{d.DeCall()}|{d.Message}|{d.SinceMidnight.Ticks}");
            }
            if (ctrl.rawNewestFirst) { items.Reverse(); keys.Reverse(); }
            if (items.Count == 0) { items.Add("[No decodes this period]"); keys.Add(null); }

            QueueView.RenderRawDecodes(items, keys);
        }

        private bool PassesRawDecodeFilter(EnqueueDecodeMessage d)
        {
            // Advanced filter: only decodes with a callsign
            if (ctrl.rawOnlyCallsigns && string.IsNullOrEmpty(d.DeCall())) return false;

            // rawOnlyUnworked: station must be new on the current band (not in WSJT-X log)
            if (ctrl.rawOnlyUnworked)
            {
                if (string.IsNullOrEmpty(d.DeCall())) return false;
                if (!d.IsNewCallOnBand) return false;
            }

            // rawOnlyRanked: station must pass Tilly's basic call-wanted criteria,
            // mirroring the gates in AddSelectedCall (new-on-band, origin, band scope,
            // OR new-country-on-band with checkbox, OR directed alert with checkbox).
            if (ctrl.rawOnlyRanked)
            {
                if (string.IsNullOrEmpty(d.DeCall())) return false;

                bool isNewCtyOnBand    = d.IsNewCountryOnBand;
                bool isDirAlert        = d.IsCQ() && IsDirectedAlert(WsjtxMessage.DirectedTo(d.Message), d.IsDx);
                bool isWantedDirected  = ctrl.replyDirCqCheckBox.Checked && isDirAlert;

                if (!isNewCtyOnBand && !isWantedDirected)
                {
                    // Primary gate: must be new on current band
                    if (!d.IsNewCallOnBand) return false;

                    // Origin filter: DX and/or local
                    bool wantedOrigin = (ctrl.replyDxCheckBox.Checked && d.IsDx)
                                     || (ctrl.replyLocalCheckBox.Checked && !d.IsDx);
                    if (!wantedOrigin) return false;

                    // Band scope: when set to "Any band", station must also be new on any band
                    if (ctrl.bandComboBox.SelectedIndex == (int)NewCallBands.ANY && !d.IsNewCallAnyBand)
                        return false;
                }
            }

            // Classify message type
            bool isPota   = d.Message.Contains("POTA");
            bool isSota   = d.Message.Contains("SOTA");
            bool isDxCq   = d.IsCQ() && d.Message.Contains(" DX ");
            bool isCq     = d.IsCQ() && !isPota && !isSota && !isDxCq;
            bool isRR73   = d.IsRR73();
            bool is73     = d.Is73();

            // For non-CQ, non-terminal messages determine report vs directed.
            // WsjtxMessage.DirectedTo() returns null for non-CQ messages, so use
            // the specific message-type predicates instead.
            bool isReport   = false;
            bool isDirected = false;
            if (!isCq && !isDxCq && !isPota && !isSota && !isRR73 && !is73)
            {
                isReport   = WsjtxMessage.IsReport(d.Message) || WsjtxMessage.IsRogerReport(d.Message);
                isDirected = !isReport;
            }

            // Apply message type filters
            if (isPota     && !ctrl.rawShowPota)      return false;
            if (isSota     && !ctrl.rawShowSota)      return false;
            if (isDxCq     && !ctrl.rawShowDx)        return false;
            if (isCq       && !ctrl.rawShowCq)        return false;
            if (isRR73     && !ctrl.rawShowRR73)      return false;
            if (is73       && !ctrl.rawShow73)        return false;
            if (isReport   && !ctrl.rawShowReports)   return false;
            if (isDirected && !ctrl.rawShowDirected)  return false;

            return true;
        }

        // ===== Advanced list index helpers =====

        private string GetFilteredCall(bool evenSide, int listIdx, out int queueIdx)
        {
            queueIdx = -1;
            var arr = callQueue.ToArray();
            int count = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                EnqueueDecodeMessage d;
                if (!callDict.TryGetValue(arr[i], out d)) continue;
                if (IsEvenCall(d) == evenSide)
                {
                    if (count == listIdx) { queueIdx = i; return arr[i]; }
                    count++;
                }
            }
            return null;
        }

        // Return call sign from the retained TX1 display snapshot at the given list index.
        // The call may or may not still be in the live callQueue (snapshot persists across removes).
        public string GetCallAtTx1Index(int listIdx)
        {
            if (listIdx < 0 || listIdx >= _tx1SnapshotCalls.Count) return null;
            return _tx1SnapshotCalls[listIdx];
        }

        public string GetCallAtTx2Index(int listIdx)
        {
            if (listIdx < 0 || listIdx >= _tx2SnapshotCalls.Count) return null;
            return _tx2SnapshotCalls[listIdx];
        }

        // Return the current callQueue array index for the call shown at listIdx in the
        // TX1 snapshot.  Returns -1 when the call is no longer in the live queue.
        public int GetQueueIndexForTx1(int listIdx)
        {
            string call = GetCallAtTx1Index(listIdx);
            return call != null ? FindCallIndexInQueue(call) : -1;
        }

        public int GetQueueIndexForTx2(int listIdx)
        {
            string call = GetCallAtTx2Index(listIdx);
            return call != null ? FindCallIndexInQueue(call) : -1;
        }

        // Find the call's position in the current callQueue array; -1 if absent.
        private int FindCallIndexInQueue(string call)
        {
            var arr = callQueue.ToArray();
            for (int i = 0; i < arr.Length; i++)
                if (string.Equals(arr[i], call, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public void NextCallFromTx1(int listIdx)
        {
            string call = GetCallAtTx1Index(listIdx);
            if (call == null) return;
            int qi = FindCallIndexInQueue(call);
            if (qi >= 0) NextCall(false, qi, operatorSelected: true);
        }

        public void NextCallFromTx2(int listIdx)
        {
            string call = GetCallAtTx2Index(listIdx);
            if (call == null) return;
            int qi = FindCallIndexInQueue(call);
            if (qi >= 0) NextCall(false, qi, operatorSelected: true);
        }

        // Maps a filtered display index (advRawListBox.SelectedIndex) to the
        // corresponding entry in _rawDecodeHistory, skipping items that do not
        // pass the current filter.  Returns null when out of range.
        private EnqueueDecodeMessage GetFilteredRawDecode(int listIdx)
        {
            int count = 0;
            foreach (var d in _rawDecodeHistory)
            {
                if (!PassesRawDecodeFilter(d)) continue;
                if (count == listIdx) return d;
                count++;
            }
            return null;
        }

        public void NextCallFromRawDecode(int listIdx)
        {
            // Use the filter-aware index so the correct decode is retrieved even
            // when some message types are hidden.
            var d = GetFilteredRawDecode(listIdx);
            if (d == null) return;
            string deCall = d.DeCall();
            if (string.IsNullOrEmpty(deCall)) return;
            if (!ConnectedToWsjtx()) return;

            // If the call is already in the queue use the standard NextCall path,
            // which handles listen-mode period checks, discard tracking, etc.
            var arr = callQueue.ToArray();
            for (int i = 0; i < arr.Length; i++)
            {
                if (string.Equals(arr[i], deCall, StringComparison.OrdinalIgnoreCase))
                {
                    NextCall(false, i, operatorSelected: true);
                    return;
                }
            }

            // Not in queue — do not transmit.  The call was deliberately excluded
            // by queue filters (already logged, blocked, origin filter, wrong period,
            // etc.).  Bypassing those filters via ReplyTo would be unsafe.
            StatusView.ShowMessage($"{deCall} not in call queue", false);
        }

        public string GetRawDecodeCallOrText(int listIdx)
        {
            // Use filter-aware lookup so Ctrl+C copies the call the user actually sees.
            var d = GetFilteredRawDecode(listIdx);
            if (d == null) return null;
            string deCall = d.DeCall();
            return string.IsNullOrEmpty(deCall) ? d.Message : deCall;
        }

        private void ShowStatus()
        {
            string status = "";
            Color foreColor = Color.Black;
            Color backColor = Color.Yellow;     //caution

            string k = cmdPrompts ? $", use Alt, K, for command key list" : "";

            try
            {
                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT)
                {
                    status = $"{pgmName} {pgmVer}. Waiting for WSJT-X{k}.";
                    foreColor = Color.Black;
                    backColor = Color.Orange;
                    return;
                }

                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.FAIL)
                {
                    status = failReason;
                    backColor = Color.Red;
                    return;
                }

                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.INITIAL)
                {
                    status = $"{pgmName} {pgmVer}. Connecting to WSJT-X{k}.";
                    foreColor = Color.Black;
                    backColor = Color.Orange;
                }
                else  //includes NegoState = SENT or RECD
                {
                    switch ((int)opMode)
                    {
                        case (int)OpModes.START:
                            string newSel = "";
                            if (newMode)
                            {
                                newSel = $"{mode} mode selected.";
                            }

                            if (newBand)
                            {
                                string b = bandIdx != null ? $"{bands[(int)bandIdx]} meter" : "Unknown";
                                newSel = $"{b} band selected.";
                            }

                            if (ctrl.freqCheckBox.Checked)
                            {
                                status = $"{newSel} Analyzing audio, calls not queued yet{k}.";
                            }
                            else
                            {
                                status = $"{newSel}Connecting to WSJT-X, wait until ready{k}.";
                            }
                            foreColor = Color.Black;
                            backColor = Color.Orange;
                            newBand = false;
                            return;
                        case (int)OpModes.IDLE:
                            status = modeSupported ? $"Connecting to WSJT-X, wait until ready{k}." : "WSJT-X operating mode not supported";
                            foreColor = Color.Black;
                            backColor = Color.Orange;
                            return;
                        case (int)OpModes.ACTIVE:
                            int qcw = callQueue.Count;
                            if ((cqPaused && txMode == TxModes.CALL_CQ) || (!transmitting && txMode == TxModes.LISTEN && qcw > 0)) modePrompt = true;
                            DateTime dt = DateTime.Now.ToUniversalTime();
                            TimeSpan sinceMidnight = dt - new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0);
                            DebugOutput($"{nl}{Time()} ShowStatus, txEnabled:{txEnabled} cqPaused:{cqPaused} txTimeout:{txTimeout}");
                            DebugOutput($"{spacer}loggedCall:'{loggedCall}' timedOutCall:'{timedOutCall}' replyFromInProg:{replyFromInProg}");
                            DebugOutput($"{spacer}callInProg:'{callInProg}' txMode:{txMode} qcw:{qcw} transmitting:{transmitting} qsoState:{qsoState}");
                            DebugOutput($"{spacer}curTxMsg:{curTxMsg} curTxPayload:'{curTxPayload}' autoFreqPauseMode:{autoFreqPauseMode}");
                            DebugOutput($"{spacer}newSelection:{newSelection} uploadResult:'{uploadResult}' newBand:{newBand} newTxFirst:{newTxFirst} holdCheckBox:{ctrl.holdCheckBox.Checked}");
                            DebugOutput($"{spacer}modePrompt:{modePrompt} txEnableChanged:{txEnableChanged} tuneResult:{tuneResult} toCallStatus:'{toCallStatus}'");

                            string prevRxStr = "";
                            string curRxStr = "";
                            string txStr = "";
                            string curTxMode = "";
                            string prevRxPayload;
                            string curRxPayload;
                            string hold = ctrl.holdCheckBox.Checked ? ", timeout extended" : "";
                            string tMode = txMode == TxModes.LISTEN ? "Listen" : "CQ";
                            string tmStr = mode == "FT8" ? "" : $", {mode}";
                            string desc = $", {tMode} mode{tmStr}";

                            int displayedCount = ctrl.advancedCallLayout
                                ? (ctrl.advShowTx1 ? _tx1SnapshotRows.Count : 0)
                                  + (ctrl.advShowTx2 ? _tx2SnapshotRows.Count : 0)
                                : (callInProg != null && callQueue.Contains(callInProg) ? qcw - 1 : qcw);
                            string callsStr = displayedCount == 1 ? "available station" : "available stations";
                            string count = displayedCount == 0 ? "no" : $"{displayedCount}";

                            HashSet<string> visibleCalls = null;
                            if (ctrl.advancedCallLayout)
                            {
                                visibleCalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                if (ctrl.advShowTx1) foreach (var vc in _tx1SnapshotCalls) visibleCalls.Add(vc);
                                if (ctrl.advShowTx2) foreach (var vc in _tx2SnapshotCalls) visibleCalls.Add(vc);
                            }

                            int n = SnapshotPriorityCount(CallPriority.TO_MYCALL, visibleCalls);
                            EnqueueDecodeMessage dmsg = new EnqueueDecodeMessage();
                            string c = PeekVisibleCall(out dmsg, visibleCalls);
                            string pc = (c != null && (callInProg == null || timedOutCall != null || loggedCall != null)) ? $", {Spacify(c)} first" : "";
                            string pri = n > 0 ? $", {n} to you{pc}" : "";

                            n = SnapshotPriorityCount(CallPriority.NEW_COUNTRY, visibleCalls) + SnapshotPriorityCount(CallPriority.NEW_COUNTRY_ON_BAND, visibleCalls);
                            string cty = n > 0 ? $", {n} new DXCC" : "";

                            n = SnapshotPriorityCount(CallPriority.WANTED_CQ, visibleCalls);
                            string want = n > 0 ? $", {n} wanted" : "";

                            string callsWaiting = (!transmitting || qsoState == WsjtxMessage.QsoStates.CALLING)
                                ? $", {count} {callsStr}{pri}{cty}{want}"
                                : "";
                            string prompt = (cmdPrompts && modePrompt) ? ((txMode == TxModes.CALL_CQ) ? $", Alt E to enable transmit" : (!transmitting && qcw > 0 ? $", Control W for list or Alt N for next" : "")) : "";

                            string curCall = callInProg;
                            //string txToCall = WsjtxMessage.ToCall(curTxMsg);
                            //if (transmitting && curTxMsg != null) curCall = curTxToCall;
 
                            string sel = newSelection ? " selected" : "";
                            string inProg = curCall != null ? $", {Spacify(curCall)}{sel}" : "";
                            curTxMode = transmitting ? "Transmitting" : "Receiving";
                            string cond = (!transmitting && txMode == TxModes.CALL_CQ) ? (!cqPaused ? ((uploadResult != null || txEnableChanged) ? ", transmit enabled" : "") : ", transmit disabled") : "";

                            if (newTxFirst) curTxMode = (txFirst ? "Tx first selected, " : "Tx second selected, ") + curTxMode;

                            if (newPskReporter)
                            {
                                string u = usePskReporter ? "Enabled" : "Disabled";
                                curTxMode = $"{u} PSKReporter spots, " + curTxMode;
                            }

                            if (newMode)
                            {
                                curTxMode = $"{mode} mode, " + curTxMode;
                            }

                            if (newBand)
                            {
                                string b = bandIdx != null ? $"{bands[(int)bandIdx]} meter" : "Unknown";
                                curTxMode = $"{b} band selected, " + curTxMode;
                            }

                            if (uploadResult != null)
                            {
                                curTxMode = $"{uploadResult}, " + curTxMode;
                            }

                            if (deletedAllCalls)
                            {
                                curTxMode = $"Deleted all waiting calls, " + curTxMode;
                            }

                            if (loggedCall != null)
                            {
                                curTxMode = $"{Spacify(loggedCall)} logged, " + curTxMode;
                            }

                            if (finalSignoffCall != null)
                            {
                                curTxMode = $"{Spacify(finalSignoffCall)} final 73, " + curTxMode;
                            }

                            if (consecNoDecodes >= maxNoDecodes)
                            {
                                curTxMode += $", no decodes, check time, frequency, audio in";
                                consecNoDecodes = 0;
                            }

                            if (Math.Abs(timeOffset) > maxTimeOffset)
                            {
                                curTxMode += $", time offset {timeOffset:F1} seconds, check clock time ";
                            }

                            if (promptsChanged)
                            {
                                string p = cmdPrompts ? "enabled" : "disabled";
                                curTxMode = $"Command prompts {p}, " + curTxMode;
                                if (!cmdPrompts) prompt = "";
                            }

                            if (tuneResult != null)     //for 'tune stopped'
                            {
                                curTxMode = $"{tuneResult}, " + curTxMode;
                            }

                            //marker1
                            if (cqPaused)
                            {
                                if (tuning)
                                {
                                    status = tuneResult;
                                }
                                else
                                {
                                    status = $"{curTxMode}{cond}{inProg}{callsWaiting}{desc}{hold}{prompt}.";
                                    foreColor = Color.White;
                                    backColor = Color.Green;
                                }
                            }
                            else    //not paused
                            {
                                if (!transmitting)
                                {
                                    foreColor = Color.White;
                                    backColor = Color.Green;
                                }

                                if (curTxMsg != null && transmitting)
                                {
                                    if (curTxPayload == null) curTxPayload = WsjtxMessage.Payload(curTxMsg);
                                    string p = SpacifyPayload(curTxPayload);
                                    txStr = p != null ? $", sending {p}" : "";
                                }

                                prevRxPayload = null;
                                curRxPayload = null;
                                if (curCall != null)
                                {
                                    //get latest msg from deCall to myCall
                                    List<EnqueueDecodeMessage> msgList;
                                    if (allCallDict.TryGetValue(curCall, out msgList))
                                    {
                                        EnqueueDecodeMessage rmsg = msgList[msgList.Count - 1];
                                        if (!rmsg.IsCQ())
                                        {
                                            var sec = (sinceMidnight - rmsg.SinceMidnight).TotalSeconds;
                                            //DebugOutput($"{spacer}rmsg:'{rmsg.Message}' rmsg.SinceMidnight:{rmsg.SinceMidnight} TotalSeconds:{sec}");
                                            if (sec < 3.5 * (trPeriod / 1000))  //Rx period that just ended
                                            {
                                                curRxPayload = SpacifyPayload(WsjtxMessage.Payload(rmsg.Message));
                                                //DebugOutput($"{spacer}found current:{curRxPayload}");
                                                if (!rmsg.Is73orRR73() && msgList.Count >= 2)
                                                {   //Rx period previous to the one that just ended
                                                    rmsg = msgList[msgList.Count - 2];
                                                    if (!rmsg.IsCQ())
                                                    {
                                                        prevRxPayload = SpacifyPayload(WsjtxMessage.Payload(rmsg.Message));
                                                        //DebugOutput($"{spacer}found prev:{prevRxPayload}");
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                //Rx period previous to the one that just ended
                                                prevRxPayload = SpacifyPayload(WsjtxMessage.Payload(rmsg.Message));
                                                //DebugOutput($"{spacer}no current, found prev:{prevRxPayload}");
                                            }
                                            if (prevRxPayload != null && prevRxPayload == curRxPayload) prevRxPayload = null;  //no need to repeat the same results
                                        }
                                    }

                                    curRxStr = (curRxPayload == null && callInProg != null && curCall == callInProg && sentCallList.Contains(curCall)) ? (callInProgLastActivity != null ? $" {callInProgLastActivity}" : " no response") : "";
                                    if (curRxPayload != null) curRxStr = $", received {curRxPayload}";     //otherwise, no response
                                    prevRxStr = prevRxPayload != null ? $", previous {prevRxPayload}" : "";
                                    if (transmitting && (curTxPayload == "73" || curTxPayload == "RR73")) prevRxStr = "";    //don't neeed that detail any more
                                }

                                if (expiredCall != null && ((txMode == TxModes.LISTEN && !txEnabled) || txMode == TxModes.CALL_CQ))
                                {
                                    inProg = $", {Spacify(expiredCall)}";
                                    cond = " expired";
                                    curRxStr = "";
                                    prevRxStr = "";
                                    expiredCall = null;
                                }
                                else if (timedOutCall != null && ((txMode == TxModes.CALL_CQ && transmitting) || (txMode == TxModes.LISTEN && !txEnabled)))
                                {
                                    inProg = $", {Spacify(timedOutCall)}";
                                    cond = " timed out,";
                                    timedOutCall = null;
                                    if (cmdPrompts && txMode == TxModes.LISTEN) prompt = $", use Alt E to resume QSO";
                                }
                                else if (modePrompt && callInProg != null && txMode == TxModes.LISTEN && !txEnabled)
                                {
                                    if (cmdPrompts)
                                    {
                                        prompt = $", use Alt E to resume QSO";
                                    }
                                    /*else
                                    {
                                        cond = ", transmit disabled";
                                    }*/
                                }

                                if (loggedCall != null && callInProg == loggedCall) inProg = "";  //no need to say it twice

                                if (transmitting || (curRxPayload != null && curRxPayload != "")) desc = "";

                                if (tuning)
                                {
                                    status = tuneResult;
                                    foreColor = Color.Black;
                                    backColor = Color.Yellow;     //caution
                                }
                                else if (autoFreqPauseMode > autoFreqPauseModes.DISABLED)
                                {
                                    status = "Updating best transmit frequency.";
                                }
                                else if (replyFromInProg)
                                {
                                    status = $"Replying to {Spacify(callInProg)}.";          //must be short
                                }
                                else  //not a special case
                                {
                                    status = $"{curTxMode}{inProg}{cond}{curRxStr}{prevRxStr}{txStr}{callsWaiting}{desc}{hold}{prompt}.";
                                }
                            }
                            DebugOutput($"{spacer}curCall:'{curCall}' sinceMidnight:{sinceMidnight}");
                            DebugOutput($"{spacer}curTxMode:'{curTxMode}' desc:'{desc}' inProg:'{inProg}'");
                            DebugOutput($"{spacer}cond:'{cond}' curRxStr:'{curRxStr}' prevRxStr:'{prevRxStr}'");
                            DebugOutput($"{spacer}txStr:'{txStr}' callsWaiting:'{callsWaiting}' prompt:'{prompt}'");
                            DebugOutput($"{spacer}status:'{status}'");

                            loggedCall = null;
                            finalSignoffCall = null;
                            modePrompt = false;
                            newTxFirst = false;
                            newBand = false;
                            newMode = false;
                            uploadResult = null;
                            newSelection = false;
                            replyFromInProg = false;
                            deletedAllCalls = false;
                            txEnableChanged = false;
                            promptsChanged = false;
                            tuneResult = null;
                            toCallStatus = null;
                            callInProgLastActivity = null;
                            newPskReporter = false;

                            break;
                    }
                }
            }
            finally
            {
                string bandMode = (bandIdx != null && !string.IsNullOrEmpty(mode))
                    ? $"{bands[(int)bandIdx]}m {mode}" : "Status:";
                StatusView.RenderStatus(bandMode, status, foreColor, backColor);
            }
        }

        private void ShowLogged()
        {
            var logItems = new List<string>();
            var logKeys = new List<string>();
            if (logList.Count == 0)
            {
                logItems.Add("[No calls auto-logged]");
                logKeys.Add(null);
            }
            else
            {
                var rList = logList.GetRange(0, logList.Count);
                rList.Reverse();
                foreach (string call in rList)
                {
                    logItems.Add($"{Spacify(call)}, {Country(call)}");
                    logKeys.Add(call);
                }
            }

            LogView.RenderLoggedList($"Auto-logged calls: {logList.Count}", logItems, logKeys);
        }

        //process an automatically-generated request to add a decode to call reply queue
        public void AddSelectedCall(EnqueueDecodeMessage emsg)
        {
            string msg = emsg.Message;

            string deCall = WsjtxMessage.DeCall(msg);       //known to not be null
            string toCall = WsjtxMessage.ToCall(msg);       //known to not be null
            string directedTo = WsjtxMessage.DirectedTo(msg);
            bool isCq = emsg.IsCQ();                //CQ format check
            bool isPota = emsg.IsPota();
            bool isDirectedAlert = isCq && IsDirectedAlert(directedTo, emsg.IsDx);
            bool isGridReply = WsjtxMessage.IsReply(emsg.Message);
            bool isAcceptableCq = isCq && (directedTo == null /*|| directedTo == "QRP"*/ || (directedTo == "DX" && emsg.IsDx) || directedTo == myContinent);
            bool isWantedNewCallOnBand = ctrl.bandComboBox.SelectedIndex == (int)WsjtxClient.NewCallBands.CURRENT && emsg.IsNewCallOnBand;
            bool isWantedAzimuth = Ranker.rankMethod < RankMethods.AZ_NQUAD || Ranker.rankMethod > RankMethods.AZ_NWQUAD || emsg.Rank != CallQueueRanker.OffBeamRank;         //within desired azimuth
            bool isWantedMsgType =
                (ctrl.cqOnlyRadioButton.Checked && (isAcceptableCq || WsjtxMessage.Is73orRR73(emsg.Message)))               //CQ, with or without grid info, or (RR)73
                || (ctrl.cqGridRadioButton.Checked && ((isAcceptableCq && WsjtxMessage.Grid(emsg.Message) != null) || isGridReply))             //CQ or reply, with grid info
                || ctrl.anyMsgRadioButton.Checked;                                                 //don't care about grid info
            bool isWantedOrigin = ((ctrl.replyDxCheckBox.Checked && emsg.IsDx) || (ctrl.replyLocalCheckBox.Checked && !emsg.IsDx)) && (!isCq || isAcceptableCq);
            bool isWantedCall = isWantedMsgType && isWantedOrigin && isWantedAzimuth && (emsg.IsNewCallAnyBand || isWantedNewCallOnBand);
            // isWantedDirected: pure classification — whether this decode IS a directed CQ that
            // matches the alert list.  Admission is gated by IsCallingEnabled(WANTED_CQ) in the
            // category switch below; the replyDirCqCheckBox no longer controls admission.
            bool isWantedDirected = isDirectedAlert;

            //refine the call priority
            if (isDirectedAlert && ctrl.replyDirCqCheckBox.Checked && emsg.Priority == (int)CallPriority.DEFAULT)
                emsg.Priority = (int)CallPriority.WANTED_CQ;
            if (!emsg.AutoGen) emsg.Priority = (int)CallPriority.MANUAL_SEL;     //generated by click
            emsg.Category = DeriveCategory(emsg);   //after Priority set; before SetRank
            SetRank(emsg);           //only after set priority, need this before AddCall()

            if (emsg.Country == "")
            {
                DebugOutput($"{spacer}Country unknown for '{deCall}', queuing without country");
            }

            int replyDecodePriority = (int)CallPriority.NEW_COUNTRY;      //simulate highest priority
            if (replyDecode != null)
            {
                //DebugOutput($"{spacer}replyDecode.Message: {replyDecode.Message} replyDecode.Priority:{replyDecode.Priority}");
                if (!RecdAnyMsg(replyDecode.DeCall())) replyDecodePriority = replyDecode.Priority;
            }

            UpdateMaxTxRepeat();

            //*******************
            //Auto-generated call
            //*******************
            //auto-generated notification of a call rec'd by WSJT-X;
            if (emsg.AutoGen)       //automatically-generated queue request, not clicked
            {
                if (debugDetail) DebugOutput($"{Time()}");
                if (debugDetail) DebugOutput($"{emsg}{nl}{spacer}msg:'{emsg.Message}' decodeCycle:{CurrentDecodeCycleString()} decodesProcessed:{decodesProcessed} cqPaused:{cqPaused}");

                if (myCall == null || opMode != OpModes.ACTIVE)
                {
                    if (debugDetail) DebugOutput($"{spacer}AddSelectedCall rejected, myCall or opMode");
                    return;
                }

                if (deCall == null)
                {
                    if (debugDetail) DebugOutput($"{spacer}AddSelectedCall rejected, deCall null");
                    return;
                }

                if (deCall == callInProg)
                {
                    if (isCq || emsg.Is73orRR73() || emsg.IsRogers())
                    {
                        toCallStatus = "ready";
                        callInProgLastActivity = isCq ? "calling CQ" : "sent 73";
                    }
                    else
                    {
                        toCallStatus = "busy";
                        callInProgLastActivity = $"working {toCall}";
                    }

                    DebugOutput($"{spacer}AddSelectedCall toCallStatus:{toCallStatus} activity:{callInProgLastActivity}");
                    return;
                }

                // Feature 1: wanted-anywhere alert — fires for any decode of a wanted callsign,
                // regardless of whether the call will be admitted to the transmit queue.
                if (ctrl.wantedCallAnywhereEnabled && wantedCalls.Count > 0
                    && wantedCalls.Contains(deCall)
                    && IsAlertCooledDown(_wantedAnywhereAlertTimes, deCall, WantedAnywhereAlertCooldownSecs))
                {
                    Sounds.PlaySoundEvent(ctrl.soundEnabled_WantedAnywhere, ctrl.soundFile_WantedAnywhere);
                    _wantedAnywhereAlertTimes[deCall] = DateTime.UtcNow;
                    DebugOutput($"{spacer}WantedAnywhere alert: '{deCall}'");
                }

                if (isCq)    //check for unwanted directed CQ
                {
                    if (isDirectedAlert || isAcceptableCq)      //acceptable CQ
                    {
                        if (unwantedCqList.Contains(deCall))
                        {
                            DebugOutput($"{spacer}AddSelectedCall unwanted directed CQ now wanted"); 
                            unwantedCqList.Remove(deCall);
                        }
                    }
                    else                                        //unwanted CQ
                    {
                        if (!unwantedCqList.Contains(deCall))
                        {
                            DebugOutput($"{spacer}AddSelectedCall rejected, unwanted directed CQ"); 
                            unwantedCqList.Add(deCall);
                            if (callQueue.Contains(deCall)) RemoveCall(deCall);
                        }
                        return;
                    }
                }
                else        //other than a CQ
                {
                    if (unwantedCqList.Contains(deCall))
                    {
                        if (debugDetail) DebugOutput($"{spacer}AddSelectedCall rejected, call following unwanted directed CQ");
                        return;
                    }
                }

                if (callQueue.Contains(deCall))
                {
                    UpdateCall(deCall, emsg);
                    if (debugDetail) DebugOutput($"{spacer}AddSelectedCall rejected, already in callQueue");
                    return;
                }

                // Already-worked check: reject calls logged on this band, except POTA (can repeat)
                // or new-DXCC categories (the entity being new on band is the relevant criterion).
                bool isNewDxccCategory = emsg.Category == CallCategory.NEW_COUNTRY
                                        || emsg.Category == CallCategory.NEW_COUNTRY_ON_BAND;
                if (!emsg.IsNewCallOnBand && !isPota && !isNewDxccCategory)
                {
                    DebugOutput($"{spacer}AddSelectedCall: already worked '{deCall}'");
                    return;
                }

                if (IsBlocked(deCall))
                {
                    if (debugDetail) DebugOutput($"{spacer}AddSelectedCall rejected, call blocked");
                    return;
                }

                // ── Call Filters admission gate (Phase 1) ────────────────────────────
                // Classification is already complete (DeriveCategory + SetRank ran above).
                // Each category is admitted iff its Call Filter is enabled in callingEnabled.
                // Manual clicks (!AutoGen) bypass all filters.
                // Note: TO_MYCALL decodes normally take the toMyCall branch (line 2348) and
                // never reach this method.  The case below is defensive insurance only — Jimmy
                // should never hide a station that is calling us.
                bool isAdmitted;
                switch (emsg.Category)
                {
                    case CallCategory.TO_MYCALL:
                        // Always admit — queue admission is never gated by the "Calling Me"
                        // checkbox.  That checkbox controls only Alt+N (next-call) selection.
                        isAdmitted = true;
                        break;
                    case CallCategory.NEW_COUNTRY:
                    case CallCategory.NEW_COUNTRY_ON_BAND:
                    case CallCategory.ALWAYS_WANTED:
                        isAdmitted = IsCallingEnabled(emsg.Category);
                        break;
                    case CallCategory.WANTED_CQ:
                    case CallCategory.POTA:
                    case CallCategory.SOTA:
                        // Directed CQ: filter must be enabled AND the CQ must match the alert list.
                        isAdmitted = IsCallingEnabled(CallCategory.WANTED_CQ) && isWantedDirected;
                        break;
                    case CallCategory.WAS_NEEDED:
                    case CallCategory.DXCC_UNCONFIRMED:
                    case CallCategory.ZONE_NEEDED:
                    case CallCategory.STILL_NEEDED:
                        isAdmitted = IsCallingEnabled(emsg.Category);
                        break;
                    case CallCategory.DEFAULT:
                        // Ordinary CQ: filter must be enabled AND Receive-tab sub-filters must pass.
                        isAdmitted = IsCallingEnabled(CallCategory.DEFAULT) && isWantedCall;
                        break;
                    default:
                        isAdmitted = false;
                        break;
                }
                if (!emsg.AutoGen) isAdmitted = true;

                if (isAdmitted)
                {
                    if (!debugDetail) DebugOutput($"{Time()}");
                    if (!debugDetail) DebugOutput($"{emsg}{nl}{spacer}msg:'{emsg.Message}' decodeCycle:{CurrentDecodeCycleString()} decodesProcessed:{decodesProcessed} cqPaused:{cqPaused}");
                    DebugOutput($"{spacer}AddSelectedCall, isCq:{isCq} deCall:'{deCall}' Priority:{emsg.Priority} Category:{emsg.Category} Rank:{emsg.Rank} IsDx:{emsg.IsDx} isWantedCall:{isWantedCall} isWantedDir:{isWantedDirected}");
                    DebugOutput($"{spacer}filterAdmit:{IsCallingEnabled(emsg.Category)} isWantedOrigin:{isWantedOrigin} isWantedMsgType:{isWantedMsgType} isWantedAz:{isWantedAzimuth}");
                    DebugOutput($"{spacer}maxAutoGenEnqueue:{maxAutoGenEnqueue} maxPrevTo:{maxPrevTo} isNewCallAnyBand:{emsg.IsNewCallAnyBand} isNewCallOnBand:{emsg.IsNewCallOnBand} isWantedNewCallOnBand:{isWantedNewCallOnBand}");
                    DebugOutput($"{spacer}isNewCountry:{emsg.IsNewCountry} isNewCountryOnBand:{emsg.IsNewCountryOnBand} isPota:{isPota} directedTo:'{directedTo}'");
                    DebugOutput($"{spacer}opMode:{opMode} toCall: '{toCall}' callInProg:'{CallPriorityString(callInProg)}' callQueue.Count:{callQueue.Count} callQueue.Contains:{callQueue.Contains(deCall)} logList.Contains:{logList.Contains(deCall)}");

                    if (!IsCorrectTimePeriodForMode(emsg))
                    {
                        DebugOutput($"{spacer}rejected, wrong time period");
                        return;
                    }

                    if (isPota) DebugOutput($"{PotaLogDictString()}");
                    List<string> list;
                    if (isPota && potaLogDict.TryGetValue(deCall, out list))
                    {
                        string band = FreqToBandStr(dialFrequency / 1e6);
                        if (band == null) band = "???";
                        string date = DateTime.Now.ToShortDateString();     //local date/time
                        string potaInfo = $"{date},{band},{mode}";
                        DebugOutput($"{spacer}potaInfo:{potaInfo}");
                        if (list.Contains(potaInfo))
                        {
                            DebugOutput($"{spacer}rejected, already logged POTA");
                            return;
                        }
                    }

                    bool addedWantedCall = false;
                    bool emsgIsEven = IsEvenCall(emsg);
                    int periodCount = PeriodCallCount(emsgIsEven);
                    // Priority calls and admitted HRC-filter calls bypass the per-period queue limit.
                    if (periodCount < maxAutoGenEnqueue || isWantedDirected ||
                        emsg.Category == CallCategory.NEW_COUNTRY ||
                        emsg.Category == CallCategory.NEW_COUNTRY_ON_BAND ||
                        emsg.Category == CallCategory.WAS_NEEDED ||
                        emsg.Category == CallCategory.DXCC_UNCONFIRMED ||
                        emsg.Category == CallCategory.ZONE_NEEDED ||
                        emsg.Category == CallCategory.STILL_NEEDED ||
                        (addedWantedCall = CanAddWantedCall(deCall, emsg, isWantedCall, emsgIsEven)))
                    {
                        int prevTo = 0;
                        int maxTo = MaxTimeoutsForMsg(isPota);
                        DebugOutput($"{spacer}replyDecodePriority:{replyDecodePriority} prevTo:{prevTo} maxPrevPotaTo:{maxPrevPotaTo} maxTo:{maxTo}");
                        if (!timeoutCallDict.TryGetValue(deCall, out prevTo) || prevTo < maxTo)
                        {
                            bool addedCall = AddCall(deCall, emsg);
                            if (addedCall && PeriodCallCount(emsgIsEven) > ctrl.maxQueuedCallsBase)
                            {
                                if (RemoveCallLastForPeriod(emsgIsEven) == deCall) addedCall = false;
                            }

                            DebugOutput($"{spacer}addedCall:{addedCall} decodesProcessed:{decodesProcessed}");
                            if (addedCall && decodesProcessed && !cqPaused)
                            {
                                DebugOutput($"{spacer}late decode(4), restartQueue:{restartQueue}");
                                StartProcessDecodeTimer2();
                            }

                            if (addedCall && toCall != myCall && !_lastAddCallCategoryPlayed)
                                Sounds.PlaySoundEvent(ctrl.callAddedCheckBox.Checked, ctrl.soundFile_CallAdded);
                        }
                        else
                        {
                            DebugOutput($"{spacer}rejected, prevTo:{prevTo}");
                        }
                    }
                    else
                    {
                        DebugOutput($"{spacer}AddSelectedCall: queue full '{deCall}' period:{(emsgIsEven ? "even" : "odd")} periodCount:{periodCount}/{maxAutoGenEnqueue}");
                    }
                    UpdateDebug();
                }
                else
                {
                    // Determine why this call was not admitted for diagnostic logging.
                    string notWantedReason;
                    var filterCat = (emsg.Category == CallCategory.POTA || emsg.Category == CallCategory.SOTA)
                                    ? CallCategory.WANTED_CQ : emsg.Category;
                    if (!IsCallingEnabled(filterCat))
                        notWantedReason = $"filter:{emsg.Category}";
                    else
                        notWantedReason = !isWantedMsgType ? "msgType" : !isWantedOrigin ? "origin" : !isWantedAzimuth ? "azimuth" : "newBand";
                    DebugOutput($"{spacer}AddSelectedCall: not wanted '{deCall}' cat:{emsg.Category} reason:{notWantedReason} msgType:{isWantedMsgType} origin:{isWantedOrigin} az:{isWantedAzimuth} newBand:{emsg.IsNewCallAnyBand || isWantedNewCallOnBand}");
                }
                return;
            }
        }

        public void UpdateDebug()
        {
            if (!debug) return;
            string s;
            bool chg = false;

            try
            {
                ctrl.label5.ForeColor = wsjtxTxEnableButton ? Color.White : Color.Black;
                ctrl.label5.BackColor = wsjtxTxEnableButton ? Color.Red : Color.LightGray;
                ctrl.label5.Text = $"En but: {wsjtxTxEnableButton.ToString().Substring(0, 1)}";

                ctrl.label6.Text = $"dec: {period.ToString().Substring(0, 1)}";
                ctrl.label32.Text = $"pdt: {postDecodeTimer.Enabled.ToString().Substring(0, 1)}";

                ctrl.label7.ForeColor = txEnabled ? Color.White : Color.Black;
                ctrl.label7.BackColor = txEnabled ? Color.Red : Color.LightGray;
                ctrl.label7.Text = $"txEn: {txEnabled.ToString().Substring(0, 1)}";

                ctrl.label23.Text = $"t/c/p/e: {maxTxRepeat}/{maxPrevTo}/{maxPrevPotaTo}/{maxAutoGenEnqueue}";

                if (replyCmd != lastReplyCmdDebug)
                {
                    ctrl.label8.ForeColor = Color.Red;
                    ctrl.label21.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label8.Text = $"cmd from: {WsjtxMessage.DeCall(replyCmd)}";
                lastReplyCmdDebug = replyCmd;

                ctrl.label9.Text = $"opMode: {opMode}-{WsjtxMessage.NegoState}";

                ctrl.label34.Text = $"decPr: {decodesProcessed.ToString().Substring(0, 1)}";

                string txTo = (curTxMsg == null ? "" : WsjtxMessage.ToCall(curTxMsg));
                s = (txTo == "CQ" ? null : txTo);
                ctrl.label12.Text = $"tx to: {s}";

                if (callInProg != lastCallInProgDebug)
                {
                    ctrl.label13.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label13.Text = $"in-prog: {CallPriorityString(callInProg)}";
                lastCallInProgDebug = callInProg;

                if (qsoState != lastQsoStateDebug)
                {
                    ctrl.label14.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label14.Text = $"qso: {qsoState}";
                lastQsoStateDebug = qsoState;

                if (evenOffset != lastEvenOffsetDebug)
                {
                    ctrl.label15.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label15.Text = $"evn: {evenOffset}";
                lastEvenOffsetDebug = evenOffset;

                if (oddOffset != lastOddOffsetDebug)
                {
                    ctrl.label16.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label16.Text = $"odd: {oddOffset}";
                lastOddOffsetDebug = oddOffset;

                if (txTimeout != lastTxTimeoutDebug)
                {
                    ctrl.label10.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label10.Text = $"t/o: {txTimeout.ToString().Substring(0, 1)}";
                lastTxTimeoutDebug = txTimeout;

                if (txFirst != lastTxFirstDebug)
                {
                    ctrl.label11.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label11.Text = $"txFirst: {txFirst.ToString().Substring(0, 1)}";
                lastTxFirstDebug = txFirst;

                if (restartQueue != lastRestartQueueDebug)
                {
                    ctrl.label24.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label24.Text = $"rstQ: {restartQueue.ToString().Substring(0, 1)}";
                lastRestartQueueDebug = restartQueue;

                if (transmitting != lastTransmittingDebug)
                {
                    ctrl.label25.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label25.Text = $"tx: {transmitting.ToString().Substring(0, 1)}";
                lastTransmittingDebug = transmitting;

                if (curTxMsg != lastTxMsgDebug)
                {
                    ctrl.label19.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label19.Text = $"tx:  {curTxMsg}";
                lastTxMsgDebug = curTxMsg;

                if (lastTxMsg != lastLastTxMsgDebug)
                {
                    ctrl.label18.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label18.Text = $"last: {lastTxMsg}";
                lastLastTxMsgDebug = lastTxMsg;

                if (lastDxCallDebug != dxCall)
                {
                    ctrl.label4.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label4.Text = $"dxCall: {dxCall}";
                lastDxCallDebug = dxCall;

                ctrl.label21.Text = $"replyCmd: {replyCmd}";

                if (autoFreqPauseMode != lastAutoFreqPauseModeDebug)
                {
                    ctrl.label17.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label17.Text = $"aFP: {autoFreqPauseMode}";
                lastAutoFreqPauseModeDebug = autoFreqPauseMode;

                if (consecCqCount != lastConsecCqCountDebug)
                {
                    ctrl.label26.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label26.Text = $"cCQ: {consecCqCount}/{maxConsecCqCount}";
                lastConsecCqCountDebug = consecCqCount;

                if (consecTimeoutCount != lastConsecTimeoutCount)
                {
                    ctrl.label27.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label27.Text = $"cTo: {consecTimeoutCount}/{maxConsecTimeoutCount}";
                lastConsecTimeoutCount = consecTimeoutCount;

                ctrl.label20.Text = $"xmitCyc : {xmitCycleCount}";

                if (consecTxCount != lastConsecTxCountDebug)
                {
                    ctrl.label1.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label1.Text = $"cTx: {consecTxCount}/{maxConsecTxCount}";
                lastConsecTxCountDebug = consecTxCount;

                if (cqPaused != lastPausedDebug)
                {
                    ctrl.label2.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label2.Text = $"cqPaused: {cqPaused.ToString().Substring(0, 1)}";
                lastPausedDebug = cqPaused;

                if (txMode != lastTxModeDebug)
                {
                    ctrl.label28.ForeColor = Color.Red;
                    chg = true;
                }
                string m = txMode == TxModes.LISTEN ? "Lis" : "CQ";
                ctrl.label28.Text = $"TxMode: {m}";
                lastTxModeDebug = txMode;

                ctrl.label22.Text = $"disCall: '{discardCall}'/{discardCallCycleCount}";
                ctrl.label29.Text = $"shTx: {shortTx.ToString().Substring(0, 1)}";
                ctrl.label30.Text = $"t/o call: {timedOutCall}";

                if (replyDecode == null)
                {
                    ctrl.label31.Text = $"replyDec: ---          ";
                }
                else
                {
                    ctrl.label31.Text = $"replyDec: {replyDecode.DeCall()}: {replyDecode.Priority}";
                }

                ctrl.label33.Text = (decoding ? $"decCyc: {decodeCycle}" : "decCyc:");

                if (chg)
                {
                    ctrl.debugHighlightTimer.Stop();
                    ctrl.debugHighlightTimer.Interval = 1000;
                    ctrl.debugHighlightTimer.Start();
                }
            }
            catch (Exception err)
            {
                DebugOutput($"ERROR: UpdateDebug: err:{err}");
            }
        }

        public void ConnectionDialog()
        {
            ctrl.initialConnFaultTimer.Stop();
            heartbeatRecdTimer.Stop();
            if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.INITIAL)
            {
                heartbeatRecdTimer.Stop();
                suspendComm = true;         //in case udpClient msgs start 
                string s = multicast ? $"{nl}{nl}In WSJT-X:{nl}- Select File | Settings then the 'Reporting' tab.{nl}{nl}'- Try different 'Outgoing interface' selection(s), including selecting all of them." : "";
                ctrl.BringToFront();
                MessageBox.Show($"No response from WSJT-X.{s}{nl}{nl}{pgmName} will continue waiting for WSJT-X to respond when you close this dialog.{nl}{nl}Alternatively, select 'Config' and override the auto-detected UDP settings.", pgmName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                suspendComm = false;
                ctrl.initialConnFaultTimer.Start();
            }
        }

        public void CmdCheckDialog()
        {
            cmdCheckTimer.Stop();
            if (commConfirmed) return;

            heartbeatRecdTimer.Stop();
            suspendComm = true;
            ctrl.BringToFront();
            MessageBox.Show($"Unable to make a two-way connection with WSJT-X.{nl}{nl}{pgmName} will try again when you close this dialog.", pgmName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ResetOpMode();
            ShowStatus();

            if (udpClient2 != null)
            {
                emsg.NewTxMsgIdx = 7;
                //emsg.SchemaVersion = (uint)WsjtxMessage.NegotiatedSchemaVersion;
                emsg.GenMsg = $"";          //no effect
                emsg.ReplyReqd = true;
                emsg.EnableTimeout = !debug;
                cmdCheck = RandomCheckString();
                emsg.CmdCheck = cmdCheck;
                ba = emsg.GetBytes();
                udpClient2.Send(ba, ba.Length);
                DebugOutput($"{Time()} >>>>>Sent 'Ack Req' cmd:7 cmdCheck:{cmdCheck}{nl}{emsg}");

                cmdCheckTimer.Interval = 10000;           //set up cmd check timeout
                cmdCheckTimer.Start();
                DebugOutput($"{Time()} Check cmd timer restarted");
            }

            suspendComm = false;
        }

        private string AcceptableVersionsString()
        {
            string delim = "";
            StringBuilder sb = new StringBuilder();

            foreach (string s in acceptableWsjtxVersions)
            {
                sb.Append(delim);
                sb.Append(s);
                delim = $"{nl}";
            }

            return sb.ToString();
        }

        private string RandomCheckString()
        {
            string s = rnd.Next().ToString();
            if (s.Length > 8) s = s.Substring(0, 8);
            return s;
        }

        private void DebugOutput(string s)
        {
            if (diagLog)
            {
                try
                {
                    if (logSw != null) logSw.WriteLine(s);
                }
                catch (Exception e)
                {
#if DEBUG
                    Console.WriteLine(e);
#endif
                }
            }

#if DEBUG
            if (debug)
            {
                Console.WriteLine(s);
            }
#endif
        }

        //during decoding, check for late signoff (73 or RR73) 
        //from a call sign that isn't (or won't be) the call in progress;
        //if reports have been exchanged, log the QSO;
        //logging is done directly via log file, not via WSJT-X
        private void CheckLateLog(string call, EnqueueDecodeMessage msg)
        {
            DebugOutput($"{spacer}CheckLateLog: call:'{call}' callInProg:'{CallPriorityString(callInProg)}' txTimeout:{txTimeout} msg:{msg.Message} Is73orRR73:{WsjtxMessage.Is73orRR73(msg.Message)} logList:{logList.Contains(call)} allCallDict:{allCallDict.ContainsKey(call)} sentReport:{sentReportList.Contains(call)}");
            if (call == null || !WsjtxMessage.Is73orRR73(msg.Message))
            {
                DebugOutput($"{spacer}no late log: msg is null or not 73/RR73");
                return;
            }

            if (logList.Contains(call))         //call already logged for thos mode or band for this session
            {
                DebugOutput($"{spacer}no late log: call is already logged");
                return;
            }

            List<EnqueueDecodeMessage> msgList;
            if (!allCallDict.TryGetValue(call, out msgList))
            {
                DebugOutput($"{spacer}no late log: no previous call(s) rec'd");
                return;          //no previous call(s) from DX station
            }

            EnqueueDecodeMessage rMsg;
            if ((rMsg = msgList.FindLast(RogerReport)) == null && (rMsg = msgList.FindLast(Report)) == null)
            {
                DebugOutput($"{spacer}no late log: no report rec'd from '{call}'; allCallDict has {msgList.Count} msg(s): {string.Join(", ", msgList.Select(m => $"'{m.Message}'"))}");
                return;        //the DX station never reported a signal
            }

            if (!sentReportList.Contains(call))
            {
                DebugOutput($"{spacer}no late log: no previous report(s) sent");
                return;         //never reported SNR to the DX station
            }

            RequestLog(call, rMsg, msg);              //process a "late" QSO completion
        }

        private bool Rogers(EnqueueDecodeMessage msg)
        {
            return WsjtxMessage.IsRogers(msg.Message);
        }

        private bool RogerReport(EnqueueDecodeMessage msg)
        {
            return WsjtxMessage.IsRogerReport(msg.Message);
        }

        private bool Report(EnqueueDecodeMessage msg)
        {
            return WsjtxMessage.IsReport(msg.Message);
        }

        private bool Reply(EnqueueDecodeMessage msg)
        {
            return WsjtxMessage.IsReply(msg.Message);
        }

        private bool Signoff(EnqueueDecodeMessage msg)
        {
            return WsjtxMessage.Is73orRR73(msg.Message);
        }

        private bool CQ(EnqueueDecodeMessage msg)
        {
            return WsjtxMessage.IsCQ(msg.Message);
        }

        //request WSJT-X log a QSO to the WSJT-X .ADI log file and re-broadcast to UDP listeners;
        //logging done only via WSJT-X because WSJT-X keeps track of 'logged-before' status, 
        //which is important to processing CQ notification msgs received from WSJT-X
        //recdMsg null if logging because of a sent msg
        private void RequestLog(string call, EnqueueDecodeMessage reptMsg, EnqueueDecodeMessage recdMsg)
        {
            string qsoDateOff, qsoTimeOff;

            //<call:4>W1AW  <gridsquare:4>EM77 <mode:3>FT8 <rst_sent:3>-10 <rst_rcvd:3>+01 <qso_date:8>20201226 
            //<time_on:6>042215 <qso_date_off:8>20201226 <time_off:6>042300 <band:3>40m <freq:8>7.076439 
            //<station_callsign:4>WM8Q <my_gridsquare:6>DN61OK <eor>

            string rstSent = reptMsg.Snr == 0 ? "+00" : (reptMsg.Snr > 0 ? "+" + reptMsg.Snr.ToString("D2") : reptMsg.Snr.ToString("D2"));
            string rstRecd = WsjtxMessage.RstRecd(reptMsg.Message);
            string qsoDateOn = reptMsg.RxDate.ToString("yyyyMMdd");
            string qsoTimeOn = reptMsg.SinceMidnight.ToString("hhmmss");      //one of the report decodes
            EnqueueDecodeMessage cqMsg = CqMsg(call);
            bool isPota = cqMsg != null && cqMsg.IsPota();
            var dtNow = DateTime.UtcNow;

            if (recdMsg == null)            //logging because of xmitted RRR, 73, or RR73 (not because of a rec'd msg)
            {
                qsoDateOff = dtNow.ToString("yyyyMMdd");
                qsoTimeOff = dtNow.TimeOfDay.ToString("hhmmss");
            }
            else
            {
                qsoDateOff = recdMsg.RxDate.ToString("yyyyMMdd");
                qsoTimeOff = recdMsg.SinceMidnight.ToString("hhmmss");
            }
            string qsoMode = mode;
            string grid = "";
            EnqueueDecodeMessage gridMsg = ReplyMsg(call);
            if (gridMsg == null) gridMsg = cqMsg;
            if (gridMsg != null)
            {
                string g = WsjtxMessage.Grid(gridMsg.Message);
                if (g != null) grid = g;                //CQ does have a grid
            }
            string freq = ((dialFrequency + txOffset) / 1e6).ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            string band = FreqToBandStr(dialFrequency / 1e6);
            if (band == null) band = "Unknown";

            // Reuses the same shared builder as HandleLiveQsoLogged/HandleLiveAdifLogged (see
            // AdifRecordBuilder.cs) instead of maintaining a separate hand-rolled field list here --
            // this is the record actually sent to WSJT-X (cmd:255) and, via ImportLiveLoggedQso,
            // uploaded verbatim to QRZ/Club Log in real time, so any field the shared builder
            // supports now flows through for Jimmy-initiated logging too. name/comment/tx_pwr/
            // operator/exchange are passed empty -- WSJT-X's own QsoLoggedMessage carries those
            // (typed into WSJT-X's own logging dialog); Jimmy's self-initiated log here has no
            // equivalent source for them today.
            string adifRecord = AdifRecordBuilder.Build(
                call, band, (long)(dialFrequency + txOffset), mode,
                qsoDateOn, qsoTimeOn, qsoTimeOff, rstSent, rstRecd, grid,
                name: "", comment: "", txPwr: "", operatorCall: "",
                stationCall: myCall, myGrid: myGrid, qsoDateOff: qsoDateOff);

            //request add record to log / worked before (using explicit parameters, unlike typical WSJT-X logging)
            //send ADIF record to WSJT-X for re-broadcast to logging pgms
            emsg.NewTxMsgIdx = 255;     //function code
            emsg.GenMsg = $"{call}${grid}${band}${mode}";
            emsg.Param0 = false;      //no effect
            emsg.Param1 = false;      //no effect
            emsg.CmdCheck = adifRecord;
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Broadcast' cmd:255");
            emsg.CmdCheck = "";

            // Root-cause fix: the cmd:255 broadcast above asks WSJT-X to log the QSO, but
            // WSJT-X's own confirmation (QsoLoggedMessage/LoggedAdifMessage) -- the only
            // thing that normally drives ImportLiveLoggedQso (local logbook.db, Awards/Still
            // Need tracking, and QRZ/Club Log real-time upload) -- is not guaranteed to come
            // back. Observed in practice: WSJT-X wrote the QSO to its own ADIF log but never
            // sent the confirmation, so Jimmy's own database silently never learned about it.
            // Jimmy already has every field needed to record this QSO itself, so it does so
            // directly here instead of depending solely on that round trip. ClaimLiveLoggedQso
            // still dedupes against WSJT-X's confirmation if it arrives afterward.
            string liveDedupKey = AdifImporter.BuildDedupKey(call, band, mode, qsoDateOn, qsoTimeOn);
            if (ClaimLiveLoggedQso(liveDedupKey))
            {
                var liveFields = new Dictionary<string, string>
                {
                    ["CALL"]             = call,
                    ["BAND"]             = band,
                    ["FREQ"]             = freq,
                    ["MODE"]             = mode,
                    ["QSO_DATE"]         = qsoDateOn,
                    ["TIME_ON"]          = qsoTimeOn,
                    ["TIME_OFF"]         = qsoTimeOff,
                    ["RST_SENT"]         = rstSent,
                    ["RST_RCVD"]         = rstRecd,
                    ["GRIDSQUARE"]       = grid,
                    ["STATION_CALLSIGN"] = myCall,
                    ["MY_GRIDSQUARE"]    = myGrid,
                };
                EnrichWithClubLogGeoData(liveFields, call);
                ImportLiveLoggedQso(call, liveFields, adifRecord, liveDedupKey);
            }

            Sounds.PlaySoundEvent(ctrl.loggedCheckBox.Checked, ctrl.soundFile_Logged);
            StatusView.ShowMessage($"Logged QSO with {call}", false);
            if (isPota) AddPotaLogDict(call, DateTime.Now, band, mode);         //local date/time
            consecCqCount = 0;
            consecTimeoutCount = 0;
            consecTxCount = 0;
            DebugOutput($"{spacer}QSO logged: call'{call}' consecCqCount:0 consecTimeoutCount:0 consecTxCount:0");
            UpdateCallInProg();
            logList.Add(call);
            ShowLogged();
            loggedCall = call;
            lCall = call;
            CancelDiscardCall();
            UpdateDebug();
        }

        private int? FreqToBandIdx(double? freq)            //null if unknown
        {
            if (freq == null) return null;
            if (freq >= 1.8 && freq <= 2.0) return 0;
            if (freq >= 3.5 && freq <= 4.0) return 1;
            if (freq >= 5.35 && freq <= 5.37) return 2;
            if (freq >= 7.0 && freq <= 7.3) return 3;
            if (freq >= 10.1 && freq <= 10.15) return 4;
            if (freq >= 14.0 && freq <= 14.35) return 5;
            if (freq >= 18.068 && freq <= 18.168) return 6;
            if (freq >= 21.0 && freq <= 21.45) return 7;
            if (freq >= 24.89 && freq <= 24.99) return 8;
            if (freq >= 28.0 && freq <= 29.7) return 9;
            if (freq >= 50.0 && freq <= 54.0) return 10;
            return null;
        }

        private string FreqToBandStr(double? freq)           //null if unknown
        {
            if (freq == null) return null;
            int? idx = FreqToBandIdx(freq);
            if (idx == null || (int)idx < 0 || !freqsDict.Keys.Contains(mode) || (int)idx >= freqsDict[mode].Count) return null;
            return $"{bands[(int)idx]}m";
        }

        private int? bandToFreq(int? idx)
        {
            if (idx == null || (int)idx < 0 || !freqsDict.Keys.Contains(mode) || (int)idx >= freqsDict[mode].Count) return null;
            return freqsDict[mode][(int)idx];
        }

        private void RemoveAllCall(string call)
        {
            if (call == null) return;
            if (allCallDict.Remove(call)) DebugOutput($"{spacer}removed '{call}' from allCallDict");
            if (sentReportList.Remove(call)) DebugOutput($"{spacer}removed '{call}' from sentReportList");
            if (sentCallList.Remove(call)) DebugOutput($"{spacer}removed '{call}' from sentCallList");
        }

        private string CurrentStatus()
        {
            string repDec = (replyDecode == null ? "''" : $"{nl}           {replyDecode}");
            return $"myCall:'{myCall}' callInProg:'{CallPriorityString(callInProg)}' qsoState:{qsoState} lastQsoState:{lastQsoState} txMsg:'{txMsg}' decodeCycle:{CurrentDecodeCycleString()}{nl}           lastTxMsg:'{lastTxMsg}' curCmd:'{curCmd}' replyCmd:'{replyCmd}' opMode:{opMode} replyDecode:{repDec}{nl}           txTimeout:{txTimeout} restartQueue:{restartQueue} xmitCycleCount:{xmitCycleCount} transmitting:{transmitting} mode:'{mode}' txEnabled:{txEnabled}{nl}           txFirst:{txFirst} dxCall:'{dxCall}' trPeriod:'{trPeriod}' settingChanged:{settingChanged} wsjtxTxEnableButton:{wsjtxTxEnableButton}{nl}           newDirCq:{newDirCq} tCall:'{tCall}' decoding:{decoding} cqPaused:{cqPaused} txMode:{txMode}{nl}           autoFreqPauseMode:{autoFreqPauseMode} consecCqCount:{consecCqCount} consecTimeoutCount:{consecTimeoutCount} holdCheckBox.Checked:{ctrl.holdCheckBox.Checked}{nl}{CallQueueString()}";
        }

        private void DebugOutputStatus()
        {
            DebugOutput($"(update)   {CurrentStatus()}");
        }

        //detect supported mode
        //opMode = IDLE, NegoState can be in SENT or RECD
        private void CheckModeSupported()
        {
            string s = "";
            modeSupported = supportedModes.Contains(mode) && specOp == 0;
            DebugOutput($"{Time()} CheckModeSupported, mode:'{mode}' curVerBld:{curVerBld} modeSupported:{modeSupported}");

            if (!modeSupported)
            {
                ShowStatus();
                if (specOp != 0) s = "Special ";
                DebugOutput($"{spacer}{s}mode:'{mode}' specOp:'{specOp}'");
                failReason = $"{s}{mode} mode not supported";
                if (txMode == TxModes.LISTEN)
                {
                    if (opMode == OpModes.ACTIVE) ctrl.cqModeButton_Click(null, null);       //re-enable WSJT-X "Tx even/1st" control
                }
            }

            if (mode == "MSK144" && modeSupported)
            {
                ctrl.freqCheckBox.Enabled = false;
                ctrl.freqCheckBox.Checked = false;
                ctrl.optimizeCheckBox.Enabled = false;
                ctrl.optimizeCheckBox.Checked = false;
                ctrl.holdCheckBox.Checked = false;
            }
            else
            {
                ctrl.freqCheckBox.Enabled = true;
                ctrl.optimizeCheckBox.Enabled = !ctrl.holdCheckBox.Checked;
            }
        }

        private string DatagramString(byte[] datagram)
        {
            var sb = new StringBuilder();
            string delim = "";
            for (int i = 0; i < datagram.Length; i++)
            {
                sb.Append(delim);
                sb.Append(datagram[i].ToString("X2"));
                delim = " ";
            }
            return sb.ToString();
        }

        public void BlockCall(int idx)
        {
            DebugOutput($"{Time()} BlockCall {idx}");
            if (idx < 0) idx = 0;
            if (idx >= callQueue.Count) return;

            EnqueueDecodeMessage dmsg = new EnqueueDecodeMessage();
            string call = PeekCall(idx, out dmsg);

            if (call == null) return;

            if (!IsBlocked(call))
            {
                ctrl.ExceptTextBoxAdd(call);       //callqueue updated by BlockedTextChanged()
                DebugOutput($"{spacer}added  {call} to blocked call list");
                StatusView.ShowMessage($"{call} is now blocked", ctrl.callAddedCheckBox.Checked);
            }
            else
            {
                StatusView.ShowMessage($"{call} already blocked", ctrl.callAddedCheckBox.Checked);
            }
        }

        public string GetCallAtIndex(int idx)
        {
            if (idx < 0 || idx >= callQueue.Count) return null;
            return callQueue.ToArray()[idx];
        }

        // Translates a display row index from the normal callListBox (which filters
        // callInProg) to the corresponding true callQueue position.
        // Returns displayIdx unchanged when no mapping exists (e.g., no active QSO).
        public int MapNormalListIndex(int displayIdx)
        {
            if (displayIdx >= 0 && displayIdx < _callListBoxQueueIndices.Count)
                return _callListBoxQueueIndices[displayIdx];
            return displayIdx;
        }

        public bool IsBlockedCall(string call)
        {
            return IsBlocked(call);
        }

        // Called from Controller after lookup or settings change to re-rank and refresh.
        public void SortCallsPublic()  { SortCalls(); ShowQueue(); if (ctrl.advancedCallLayout) ShowAdvancedQueue(null); }
        public void RefreshQueueDisplay() { ShowQueue(); if (ctrl.advancedCallLayout) ShowAdvancedQueue(null); }

        public bool ManualEnqueueCall(string callsign)
        {
            if (string.IsNullOrEmpty(callsign)) return false;
            if (udpClient2 == null) return false;

            try
            {
                var cmsg = new ConfigureMessage
                {
                    SchemaVersion    = WsjtxMessage.NegotiatedSchemaVersion,
                    Id               = WsjtxMessage.UniqueId,
                    DXCall           = callsign,
                    DXGrid           = "",
                    GenerateMessages = true,
                };
                ba = cmsg.GetBytes();
                udpClient2.Send(ba, ba.Length);
                DebugOutput($"{Time()} >>>>>Sent 'Configure' for manual call to '{callsign}'");
            }
            catch (Exception ex)
            {
                DebugOutput($"{Time()} ManualEnqueueCall failed: {ex.Message}");
                return false;
            }

            EnableTx();

            // Set active-call state so status shows the target callsign, matching normal queue-reply behavior
            SetCallInProg(callsign);
            _manualCallInProg = true;
            cqPaused = false;
            restartQueue = false;
            txTimeout = false;
            tCall = null;
            xmitCycleCount = 0;
            timedOutCall = null;

            return true;
        }

        public void BlockedTextChanged(string text)
        {
            UpdateBlockList(text);

            //remove blocked call(s) from call queue
            foreach (string call in blockList)
            {
                RemoveCall(call);
            }
        }

        public bool BandUp()
        {
            if (!freqsDict.Keys.Contains(mode)) return false;
            if (bandIdx == null || (int)bandIdx >= freqsDict[mode].Count - 1) return false;
            int targetIdx = (int)bandIdx + 1;
            if (bandToFreq(targetIdx) == null) return false;

            ClearAudioOffsets();
            if (ctrl.freqCheckBox.Checked) _requireOffsetForActive = true;
            AutoFreqChanged(ctrl.freqCheckBox.Checked, true);
            Pause(true, false);
            CancelQso();

            DebugOutput($"{Time()} [BAND-AUDIT] BandUp: currentBandIdx:{bandIdx} targetIdx:{targetIdx} newFreq:{(uint)(bandToFreq(targetIdx) * 1000)} txFirst:{txFirst}");
            SetBandTxFirst((uint)(bandToFreq(targetIdx) * 1000), txFirst, "BandUp");
            ShowBandChangePending(targetIdx);
            return true;
        }

        public bool BandDown()
        {
            if (!freqsDict.Keys.Contains(mode)) return false;
            if (bandIdx == null || (int)bandIdx <= 0) return false;
            int targetIdx = (int)bandIdx - 1;
            if (bandToFreq(targetIdx) == null) return false;

            ClearAudioOffsets();
            if (ctrl.freqCheckBox.Checked) _requireOffsetForActive = true;
            AutoFreqChanged(ctrl.freqCheckBox.Checked, true);
            Pause(true, false);
            CancelQso();

            DebugOutput($"{Time()} [BAND-AUDIT] BandDown: currentBandIdx:{bandIdx} targetIdx:{targetIdx} newFreq:{(uint)(bandToFreq(targetIdx) * 1000)} txFirst:{txFirst}");
            SetBandTxFirst((uint)(bandToFreq(targetIdx) * 1000), txFirst, "BandDown");
            ShowBandChangePending(targetIdx);
            return true;
        }

        public bool SelectBand(int targetIdx)
        {
            if (!freqsDict.Keys.Contains(mode)) return false;
            if (targetIdx < 0 || targetIdx >= freqsDict[mode].Count) return false;
            if (bandToFreq(targetIdx) == null) return false;
            if (bandIdx != null && (int)bandIdx == targetIdx) return false;

            ClearAudioOffsets();
            if (ctrl.freqCheckBox.Checked) _requireOffsetForActive = true;
            AutoFreqChanged(ctrl.freqCheckBox.Checked, true);
            Pause(true, false);
            CancelQso();

            DebugOutput($"{Time()} [BAND-AUDIT] SelectBand: currentBandIdx:{bandIdx} targetIdx:{targetIdx} newFreq:{(uint)(bandToFreq(targetIdx) * 1000)} txFirst:{txFirst}");
            SetBandTxFirst((uint)(bandToFreq(targetIdx) * 1000), txFirst, "SelectBand");
            ShowBandChangePending(targetIdx);
            return true;
        }

        private void ShowBandChangePending(int targetIdx)
        {
            ctrl.statusText.ForeColor = Color.Black;
            ctrl.statusText.BackColor = Color.Yellow;
            ctrl.statusText.Text = $"Changing to {bands[targetIdx]} meter band...";
            ctrl.statusText.SelectionStart = 0;
        }

        public bool ReportPowerSwr()
        {
            GetPowerSwr();
            StartStatusTimer2(false);
            return true;
        }

        public bool ToggleTuningProcess()
        {
            if (!tuning && transmitting)
            {
                HaltTx();
                Thread.Sleep(500);
            }

            ToggleTuning();
            tuning = !tuning;

            if (!tuning) StartStatusTimer2(false);

            return true;
        }

        public bool AudioLevel(bool up)
        {
            if (!transmitting) return false;

            if (!tuning) StartStatusTimer2(false);

            AdjAudioLevel(up);
            return true;
        }

        public bool AnalysisNeeded => ctrl.freqCheckBox.Checked && !analysisCompleted;

        public void StartSlotAnalysis(bool pendingCq = false)
        {
            DebugOutput($"{Time()} [BAND-AUDIT] StartSlotAnalysis: bandIdx:{bandIdx} pendingCq:{pendingCq}");
            ClearAudioOffsets();
            pendingCqAfterAnalysis = pendingCq;
            StatusView.ShowMessage("Analyzing transmit slot...", false);
        }

        public bool ToggleTxFirst()
        {
            HaltTuning();
            HaltTx();           // stop current TX before switching period so WSJT-X honors the new setting
            DebugOutput($"{Time()} ToggleTxFirst, newFreq:{0} newTxFirst:{!txFirst}");
            SetBandTxFirst(0, !txFirst, "ToggleTxFirst");
            // Write an immediate status before WSJT-X confirms via StatusMessage
            // (~100 ms round-trip).  ShowStatus() will overwrite this once newTxFirst
            // is set in the txFirst-change handler.
            string pendingSide = txFirst ? "second" : "first";
            ctrl.statusText.ForeColor = Color.Black;
            ctrl.statusText.BackColor = Color.Yellow;
            ctrl.statusText.Text = $"Tx {pendingSide} selected, halted";
            ctrl.statusText.SelectionStart  = 0;
            ctrl.statusText.SelectionLength = 0;
            // Force NVDA/JAWS to announce this pending status immediately, same guard and
            // reasoning as Controller.RenderStatus (only send to the foreground window).
            if (ctrl.statusText.Focused && Form.ActiveForm == ctrl)
                SendKeys.Send("{UP}");
            return true;
        }

        // Alt+U. Originally LoTW-only; now also triggers the QRZ/Club Log upload
        // catch-up when those services are configured+enabled, so pressing this one
        // key sends everything pending to every enabled service. Each part is
        // independently gated -- an unconfigured/disabled service is silently
        // skipped, never attempted.
        public bool UploadLotw()
        {
            HaltTuning();
            if (ctrl.lotwUploadEnabled)
                StartUploadLotw();
            RunUploadCatchUp();
            return true;
        }

        // Sends every QSO not yet uploaded to QRZ/Club Log. This is the batch
        // safety net: it runs regardless of whether real-time upload is on for a
        // service (normally finding nothing to do in that case), and it is the
        // only path at all when real-time is off. Runs in the background --
        // Alt+U returns immediately, matching the existing LoTW behavior where
        // WSJT-X performs its upload asynchronously on its own too.
        private void RunUploadCatchUp()
        {
            Task.Run(async () =>
            {
                try
                {
                    using (var db = new LogbookDb())
                    {
                        if (ctrl.qrzUploadEnabled && !string.IsNullOrWhiteSpace(ctrl.qrzLogbookApiKey))
                            await CatchUpQrz(db).ConfigureAwait(false);

                        if (ctrl.clubLogUploadEnabled &&
                            !string.IsNullOrWhiteSpace(ctrl.clubLogUploadEmail) &&
                            !string.IsNullOrWhiteSpace(ctrl.clubLogUploadPassword) &&
                            !string.IsNullOrWhiteSpace(ctrl.clubLogUploadCallsign))
                            await CatchUpClubLog(db).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    DebugOutput($"{Time()} RunUploadCatchUp error: {ex.Message}");
                }
            });
        }

        // QRZ's INSERT is single-QSO-per-call (no batch parameter), so a backlog is
        // sent as a loop with a small courtesy delay between calls -- QRZ documents
        // no hard rate limit, but other logging software follows this same pattern.
        private async Task CatchUpQrz(LogbookDb db)
        {
            var pending = db.GetPendingUploads("QRZ");
            if (pending.Count == 0) return;
            DebugOutput($"{Time()} QRZ upload catch-up: {pending.Count} pending QSO(s).");
            var client = new QrzLogbookClient();
            foreach (var q in pending)
            {
                string adifRecord = AdifRecordBuilder.Build(
                    q.Callsign, q.Band, q.FreqHz, q.Mode, q.QsoDate, q.TimeOn, q.TimeOff,
                    q.RstSent, q.RstRcvd, q.Grid, q.Name, q.Comment, q.TxPwr,
                    q.OperatorCall, q.StationCall, q.MyGrid, q.ExchangeSent, q.ExchangeRcvd);
                bool ok = await client.InsertAsync(ctrl.qrzLogbookApiKey, adifRecord).ConfigureAwait(false);
                if (ok) db.MarkUploaded(q.DedupKey, "QRZ", DateTime.UtcNow);
                else DebugOutput($"{Time()} QRZ upload catch-up failed for {q.Callsign}: {client.LastError}");
                await Task.Delay(300).ConfigureAwait(false);
            }
        }

        // Club Log's own guidance is that a backlog must go through putlogs.php
        // (one file, one request) rather than looping realtime.php -- so the whole
        // pending set is sent as a single batch upload here.
        private async Task CatchUpClubLog(LogbookDb db)
        {
            var pending = db.GetPendingUploads("CLUBLOG");
            if (pending.Count == 0) return;
            DebugOutput($"{Time()} Club Log upload catch-up: {pending.Count} pending QSO(s).");

            var sb = new StringBuilder();
            foreach (var q in pending)
            {
                sb.Append(AdifRecordBuilder.Build(
                    q.Callsign, q.Band, q.FreqHz, q.Mode, q.QsoDate, q.TimeOn, q.TimeOff,
                    q.RstSent, q.RstRcvd, q.Grid, q.Name, q.Comment, q.TxPwr,
                    q.OperatorCall, q.StationCall, q.MyGrid, q.ExchangeSent, q.ExchangeRcvd));
            }

            var client = new ClubLogUploadClient();
            bool ok = await client.BatchUploadAsync(
                ctrl.clubLogUploadEmail, ctrl.clubLogUploadPassword, ctrl.clubLogUploadCallsign,
                ClubLogAppKey.Resolve(), sb.ToString()).ConfigureAwait(false);

            if (ok)
                foreach (var q in pending) db.MarkUploaded(q.DedupKey, "CLUBLOG", DateTime.UtcNow);
            else
                DebugOutput($"{Time()} Club Log upload catch-up failed ({pending.Count} QSOs): {client.LastError}");
        }

        // Alt+N: Walk Call Filter order (outer), then rank order within each category (inner).
        // The first category in callingEnabled that has a queued call wins; within that category,
        // the highest-ranked call (lowest callQueue index) is selected.
        // POTA/SOTA/MANUAL_SEL are treated as WANTED_CQ for filter matching.
        // Ordinary CQ (DEFAULT) is selectable when it is checked in Call Filters.
        public void NextBestPriorityCall()
        {
            NextBestPriorityCallFiltered(_ => true);
        }

        // Alt+N when TX1 list is focused: same category-first ordering, limited to calls
        // visible in the TX1 snapshot.
        public void NextBestPriorityCallFromTx1()
        {
            var snap = new HashSet<string>(_tx1SnapshotCalls, StringComparer.OrdinalIgnoreCase);
            NextBestPriorityCallFiltered(call => snap.Contains(call));
        }

        // Alt+N when TX2 list is focused: same category-first ordering, limited to calls
        // visible in the TX2 snapshot.
        public void NextBestPriorityCallFromTx2()
        {
            var snap = new HashSet<string>(_tx2SnapshotCalls, StringComparer.OrdinalIgnoreCase);
            NextBestPriorityCallFiltered(call => snap.Contains(call));
        }

        // Alt+N when Raw Decodes list is focused: same category-first ordering, limited to calls
        // that pass the current raw-decode filter.
        public void NextBestPriorityCallFromRaw()
        {
            var rawCallSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _rawDecodeHistory)
            {
                if (!PassesRawDecodeFilter(d)) continue;
                string deCall = d.DeCall();
                if (!string.IsNullOrEmpty(deCall)) rawCallSet.Add(deCall);
            }
            NextBestPriorityCallFiltered(call => rawCallSet.Contains(call));
        }

        // Shared implementation for all Alt+N variants.
        // Outer loop: Call Filter order (callingEnabled list).
        // Inner loop: callQueue rank order — highest-ranked call in the winning category is selected.
        // inView: returns true if the call should be considered (used to restrict to a visible list).
        private void NextBestPriorityCallFiltered(Func<string, bool> inView)
        {
            var arr = callQueue.ToArray();
            foreach (CallCategory filterCat in Ranker.callingEnabled)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (!inView(arr[i])) continue;
                    EnqueueDecodeMessage dmsg;
                    if (!callDict.TryGetValue(arr[i], out dmsg)) continue;
                    // Map POTA/SOTA/MANUAL_SEL to WANTED_CQ for filter-category matching.
                    CallCategory effCat = dmsg.Category;
                    if (effCat == CallCategory.POTA || effCat == CallCategory.SOTA || effCat == CallCategory.MANUAL_SEL)
                        effCat = CallCategory.WANTED_CQ;
                    if (effCat != filterCat) continue;
                    NextCall(false, i);
                    return;
                }
            }
            ctrl.statusText.Text = "No priority calls available";
        }

        public void NextCall(bool confirm, int idx, bool operatorSelected = false)
        {
            HaltTuning();
            DebugOutput($"{Time()} NextCall {idx} operatorSelected:{operatorSelected}");
            dialogTimer2.Tag = $"{confirm} {idx} {operatorSelected}";
            dialogTimer2.Start();
        }

        private void dialogTimer2_Tick(object sender, EventArgs e)
        {
            dialogTimer2.Stop();
            //if (cqPaused) return;
            var a = ((string)dialogTimer2.Tag).Split(' ');
            bool confirm = a[0] == "True";
            int idx = Convert.ToInt32(a[1]);
            bool operatorSelected = a.Length > 2 && a[2] == "True";
            if (idx < 0) idx = 0;

            DebugOutput($"{Time()} dialogTimer2_Tick, idx:{idx}");
            if (callQueue.Count > 0)
            {
                if (idx >= callQueue.Count) return;
                DebugOutput($"{CallQueueString()}");
                EnqueueDecodeMessage dmsg = new EnqueueDecodeMessage();
                string call = PeekCall(idx, out dmsg);

                if (!confirm || Confirm($"Reply to {call}?") == DialogResult.Yes)
                {
                    if (!callQueue.Contains(call)) return;          //call has already been removed or processed

                    DateTime dtNow = DateTime.Now;
                    bool evenCall = IsEvenCall(dmsg);
                    bool evenPeriod = IsEvenPeriod((dtNow.Minute * 60) + dtNow.Second);       //listen mode can xmit on either period depending on current time

                    if (txMode == TxModes.LISTEN)
                    {
                        if (transmitting && evenCall == evenPeriod)     //reply is in same time period from msg
                        {
                            HaltTx();
                        }

                        DebugOutput($"{spacer}value:{ctrl.timeoutNumUpDown.Value}");
                        if (ctrl.timeoutNumUpDown.Value <= maxCheckTxRepeat)
                        {
                            DebugOutput($"{spacer}call:{call} evenCall:{evenCall}");
                            if (!ctrl.advancedCallLayout)
                                CheckCallQueuePeriod(!evenCall);        //remove queued calls from wrong time period
                            // Re-find call: period removal may have removed lower-indexed entries,
                            // shifting this call's position — the pre-removal idx is now stale.
                            idx = FindCallIndexInQueue(call);
                            if (idx < 0) return;                            //call was also removed
                        }
                    }

                    if (txMode == TxModes.CALL_CQ && !cqPaused && transmitting)
                    {
                        HaltTx();
                    }

                    xmitCycleCount = 0;
                    timedOutCall = null;
                    txTimeout = false;
                    newSelection = true;
                    ctrl.holdCheckBox.Checked = false;
                    EnqueueDecodeMessage prevReplyDecode = replyDecode;

                    if (operatorSelected) _manualCallInProg = true;
                    DebugOutput($"{spacer}reply to {call}, txTimeout:{txTimeout} holdCheckBox.Checked{ctrl.holdCheckBox.Checked} operatorSelected:{operatorSelected} _manualCallInProg:{_manualCallInProg}");
                    ReplyTo(idx);
                    StartDiscardCall(call);
                    if (!transmitting)                  //if transmtting, 
                    {
                        if (evenCall == evenPeriod)
                        {
                            //txEnableChanged = true;
                            ShowStatus();
                        }
                        else
                        {
                            StartStatusTimer();            //will actually be transmitting
                        }
                    }
                    else
                    {
                        lastStatusTxMsg = null;         //will be updated when interrupted Tx detected
                    }

                    DisableAutoFreqPause();
                    ClearCallTimeout(call);

                    UpdateDebug();
                    string n = cqPaused ? " next" : "";
                    if (!confirm) StatusView.ShowMessage($"Replying{n} to {call}", ctrl.callAddedCheckBox.Checked);
                    return;
                }
                return;
            }
        }

        public void EditCallQueue(int idx)
        {
            if (idx < 0) return;
            DebugOutput($"{Time()} EditCallQueue {idx}");
            dialogTimer3.Tag = idx;
            dialogTimer3.Start();
        }

        private void dialogTimer3_Tick(object sender, EventArgs e)
        {
            dialogTimer3.Stop();
            int idx = (int)dialogTimer3.Tag;
            if (idx > callQueue.Count - 1) return;
            var callArray = callQueue.ToArray();
            string call = callArray[idx];

            if (callQueue.Contains(call))
            {
                DebugOutput($"{Time()} dialogTimer3_Tick");
                RemoveCall(call);
                ShowStatus();
                DebugOutputStatus();
                UpdateDebug();
            }
        }

        //must be actual DX (relative to current continent) to match "DX"
        private bool IsDirectedAlert(string dirTo, bool isDx)
        {
            if (dirTo == null) return false;

            string[] a = ctrl.ReplyDirCqEntries();
            foreach (string elem in a)
            {
                if (elem == dirTo && (elem != "DX" || isDx)) return true;
            }
            return false;
        }

        //return true if received a R-XX or R+XX from the specified call
        private bool RecdRogerReport(string call)
        {
            if (call == null) return false;
            List<EnqueueDecodeMessage> msgList;
            if (!allCallDict.TryGetValue(call, out msgList)) return false;          //no previous call(s) from DX station
            //DebugOutput($"{spacer}recd previous call(s)");
            return msgList.FindLast(RogerReport) != null;        //the DX station never sent R-XX or R+XX
        }

        //return true if received a -XX or +XX from the specified call
        private bool RecdReport(string call)
        {
            if (call == null) return false;
            List<EnqueueDecodeMessage> msgList;
            if (!allCallDict.TryGetValue(call, out msgList)) return false;          //no previous call(s) from DX station
            //DebugOutput($"{spacer}recd previous call(s)");
            return msgList.FindLast(Report) != null;        //the DX station never sent -XX or +XX
        }

        //return true if received a grid from specified call
        private bool RecdReply(string call)
        {
            if (call == null) return false;
            List<EnqueueDecodeMessage> msgList;
            if (!allCallDict.TryGetValue(call, out msgList)) return false;          //no previous call(s) from DX station
            //DebugOutput($"{spacer}recd previous call(s)");
            return msgList.FindLast(Reply) != null;        //the DX station never sent grid
        }

        private bool RecdSignoff(string call)
        {
            if (call == null) return false;
            List<EnqueueDecodeMessage> msgList;
            if (!allCallDict.TryGetValue(call, out msgList)) return false;          //no previous call(s) from DX station
            //DebugOutput($"{spacer}recd previous call(s)");
            return msgList.FindLast(Signoff) != null;        //the DX station never sent 73 or RR73
        }

        private EnqueueDecodeMessage ReplyMsg(string call)
        {
            if (call == null) return null;
            List<EnqueueDecodeMessage> msgList;
            if (!allCallDict.TryGetValue(call, out msgList)) return null;          //no previous call(s) from DX station
            //DebugOutput($"{spacer}recd previous call(s)");
            return msgList.FindLast(Reply);
        }

        private EnqueueDecodeMessage CqMsg(string call)
        {
            if (call == null) return null;
            List<EnqueueDecodeMessage> msgList;
            if (!allCallDict.TryGetValue(call, out msgList)) return null;          //no previous call(s) from DX station
            //DebugOutput($"{spacer}recd previous call(s)");
            return msgList.FindLast(CQ);
        }

        private bool RecdAnyMsg(string call)
        {
            if (call == null) return false;
            return RecdReply(call) || RecdReport(call) || RecdRogerReport(call);
        }

        private bool SentAnyMsg(string call)
        {
            if (call == null) return false;
            return sentCallList.Contains(call);
        }

        //add CQ (for grid info), 
        //or report (only those to myCall), or rogerReport (only those to myCall)
        //keep only one CQ and reply for a call, but update grid/dist/az info if necessary
        private void AddAllCallDict(string call, EnqueueDecodeMessage emsg)
        {
            if ((!emsg.IsCallTo(myCall) && !emsg.IsCQ())) return;
            //  is CQ          already added a CQ from call
            if (emsg.IsCQ() && CqMsg(call) != null) return;         //don't duplicate CQs

            List<EnqueueDecodeMessage> vlist;
            //create new List for call if nothing entered yet into the Dictionary
            if (!allCallDict.TryGetValue(call, out vlist)) allCallDict.Add(call, vlist = new List<EnqueueDecodeMessage>());
            vlist.Add(emsg);        //messages from call are in order rec'd, will be duplicate msg types
            DebugOutput($"{Time()} AddAllCallDict, call:{call} msg.Message:{emsg.Message}");
        }

        // True if call's most recently seen decode shows them actively engaged with a
        // different station (not myCall, not a bare CQ) within the last couple of TX
        // periods -- i.e. they aren't listening for us right now. CheckNextXmit() already
        // re-evaluates the whole Tx decision every cycle, so skipping a transmit on this
        // basis can't lose the opportunity, just defers it to the next cycle's re-check.
        private bool IsStationBusyElsewhere(string call)
        {
            if (call == null) return false;
            if (!lastCallActivity.TryGetValue(call, out EnqueueDecodeMessage dmsg)) return false;

            string toCall = dmsg.ToCall();
            // A 73/RR73/Rogers means the OTHER exchange is ending -- the station is about to
            // be free, not busy. Mirrors AddSelectedCall's existing "ready" vs "busy"
            // distinction (toCallStatus) for a station already in callInProg.
            if (toCall == null || toCall == "CQ" || toCall == myCall
                || dmsg.Is73orRR73() || dmsg.IsRogers()) return false;

            if (trPeriod == null) return false;
            var ts = new TimeSpan(0, 0, (2 * (int)trPeriod) / 1000);      //allow up to ~2 periods before considered stale
            var age = DateTime.UtcNow - (dmsg.RxDate + dmsg.SinceMidnight);
            DebugOutput($"{spacer}IsStationBusyElsewhere: call:{call} toCall:{toCall} msg:'{dmsg.Message}' age:{age.TotalSeconds:F1}s ts:{ts.TotalSeconds:F1}s");
            if (age > ts) return false;

            return true;
        }

        //remove old rec'd calls
        private bool TrimAllCallDict()
        {
            bool removed = false;
            var keys = new List<string>();
            var dtNow = DateTime.UtcNow;
            var ts = new TimeSpan(0, maxDecodeAgeMinutes, 0);

            foreach (var entry in allCallDict)
            {
                var list = entry.Value;
                if (entry.Key != callInProg && list.Count > 0)
                {
                    var decode = list[0];           //just check the oldest entry
                    //DebugOutput($"{spacer}entry.Key:{entry.Key} dtNow:{dtNow.ToString("HHmmss.fff")} decode.RxDate:{decode.RxDate} decode.SinceMidnight:{decode.SinceMidnight} sum:{decode.RxDate + decode.SinceMidnight}");
                    if ((dtNow - (decode.RxDate + decode.SinceMidnight)) > ts)  //entry is older than wanted
                    {
                        keys.Add(entry.Key);        //collect keys to delete
                        //DebugOutput($"{spacer}{entry.Key} is expired");
                    }
                }
            }

            //delete keys to old decodes and sent reports
            foreach (string key in keys)
            {
                if (!callQueue.Contains(key))
                {
                    RemoveAllCall(key);
                    removed = true;
                }
            }

            if (removed) DebugOutput($"{spacer}TrimAllCallDict: expired calls removed from allCallDict and/or sentReportList");
            return removed;
        }

        private bool TrimCallQueue()
        {
            bool removed = false;
            var keys = new List<string>();
            var dtNow = DateTime.UtcNow;
            var ts = new TimeSpan(0, 0, ((int)trPeriod * maxCallQueueAgePeriods) / 1000);    //total periods

            foreach (var entry in callDict)
            {   //                              old call                                                          not a high priority                                             not manually selected
                if (entry.Key != callInProg && (dtNow - (entry.Value.RxDate + entry.Value.SinceMidnight)) > ts && entry.Value.Priority > (int)CallPriority.NEW_COUNTRY_ON_BAND && entry.Value.AutoGen)  //entry is older than wanted
                {
                    keys.Add(entry.Key);        //collect keys to delete
                }
            }

            //delete keys to old decodes
            foreach (string key in keys)
            {
                RemoveCall(key, updateSnapshots: false);
                removed = true;
            }

            if (removed) DebugOutput($"{spacer}TrimCallQueue: expired calls removed from callQueue and callDict");
            if (removed && ctrl.advancedCallLayout) ShowAdvancedQueue(null);
            return removed;
        }


        private void SetCallInProg(string call)
        {
            ctrl.holdCheckBox.Enabled = (call != null);
            DebugOutput($"{spacer}SetCallInProg: callInProg:'{CallPriorityString(call)}' (was '{CallPriorityString(callInProg)}') holdCheckBox.Enabled:{ctrl.holdCheckBox.Enabled}");

            if (call != null) lCall = null;     //last logged call is not relevant now

            if (call == null) { CancelDiscardCall(); _manualCallInProg = false; }

            callInProg = call;
            UpdateDblClkTip();
            UpdateCallInProg();
        }

        public void EnableTx()
        {
            try
            {
                if (emsg == null || udpClient2 == null)
                {
                    DebugOutput($"{Time()} EnableTx skipped, udpClient2:{udpClient2} emsg:{emsg}");
                    return;
                }

                DebugOutput($"{Time()} EnableTx, txEnabled:{txEnabled} processDecodeTimer.Enabled:{processDecodeTimer.Enabled}");
                emsg.NewTxMsgIdx = 9;
                emsg.Param0 = true;       //WSJT-X Enable Tx button state 
                emsg.GenMsg = $"";         //ignored
                emsg.CmdCheck = "";         //ignored
                ba = emsg.GetBytes();
                udpClient2.Send(ba, ba.Length);
                DebugOutput($"{Time()} >>>>>Sent 'Enable Tx' cmd:9{nl}{emsg}");
                txEnabled = true;
                wsjtxTxEnableButton = true;
                UpdateDblClkTip();
                DebugOutput($"{spacer}txEnabled:{txEnabled}");
            }
            catch
            {
                DebugOutput($"{Time()} 'EnableTx' failed, txEnabled:{txEnabled}");        //only happens during closing
            }

            UpdateDebug();
        }

        private void DisableTx(bool buttonState)
        {
            DebugOutput($"{Time()} DisableTx, txEnabled:{txEnabled} processDecodeTimer.Enabled:{processDecodeTimer.Enabled}");
            StopDecodeTimers();

            try
            {
                if (emsg == null || udpClient2 == null)
                {
                    DebugOutput($"{Time()} DisableTx skipped, udpClient2:{udpClient2} emsg:{emsg}");
                    return;
                }

                emsg.NewTxMsgIdx = 8;
                emsg.Param0 = buttonState;    //set WSJT-X Enable Tx button state
                emsg.GenMsg = $"";         //ignored
                emsg.CmdCheck = "";         //ignored
                ba = emsg.GetBytes();
                udpClient2.Send(ba, ba.Length);
                DebugOutput($"{Time()} >>>>>Sent 'Disable Tx' cmd:8{nl}{emsg}");
                txEnabled = false;
                wsjtxTxEnableButton = buttonState;
                UpdateDblClkTip();
                DebugOutput($"{spacer}txEnabled:{txEnabled}");
            }
            catch
            {
                DebugOutput($"{Time()} 'DisableTx' failed, txEnabled:{txEnabled}");        //only happens during closing
            }

            UpdateDebug();
        }

        private void EnableMonitoring()
        {
            emsg.NewTxMsgIdx = 11;
            emsg.GenMsg = $"";         //ignored
            emsg.CmdCheck = "";         //ignored
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Enable Monitoring' cmd:11{nl}{emsg}");
        }

        private void SetListenMode()
        {
            if (udpClient2 == null)
            {
                DebugOutput($"{Time()} SetListenMode skipped, udpClient2:{udpClient2}");
                return;
            }

            emsg.NewTxMsgIdx = 14;
            emsg.Param0 = (txMode == TxModes.LISTEN);
            emsg.GenMsg = $"";          //ignored
            emsg.CmdCheck = "";         //ignored
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Set listen mode' cmd:14{nl}{emsg}");
        }

        private void SetBandTxFirst(uint freq, bool state, string caller = "")       //requires bestWsjtxVersions
        {
            if (udpClient2 == null)
            {
                DebugOutput($"{Time()} SetBandTxFirst skipped, udpClient2:{udpClient2}");
                return;
            }

            string bandLabel = freq > 0 ? (FreqToBandStr(freq / 1000.0 / 1e6) ?? $"{freq / 1000}kHz") : "none";
            DebugOutput($"{Time()} [BAND-AUDIT] SetBandTxFirst: caller:{caller} freq:{freq} band:{bandLabel} txFirst:{state} bandIdx:{bandIdx}");

            emsg.NewTxMsgIdx = 15;
            emsg.Param0 = state;
            emsg.Offset = freq;
            emsg.GenMsg = $"";          //ignored
            emsg.CmdCheck = "";         //ignored
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Set band / Tx first' cmd:15{nl}{emsg}");
        }

        private void GetPowerSwr()       //requires bestWsjtxVersions
        {
            if (udpClient2 == null)
            {
                DebugOutput($"{Time()} GetPowerSwr skipped, udpClient2:{udpClient2}");
                return;
            }

            emsg.NewTxMsgIdx = 18;
            emsg.Param0 = false;        //ignored
            emsg.Offset = 0;            //ignored
            emsg.GenMsg = $"";          //ignored
            emsg.CmdCheck = "";         //ignored
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Get Power/SWR' cmd:18{nl}{emsg}");
        }

        private void AdjAudioLevel(bool up)       //requires bestWsjtxVersions
        {
            if (udpClient2 == null)
            {
                DebugOutput($"{Time()} SetAudioLevel skipped, udpClient2:{udpClient2}");
                return;
            }

            emsg.NewTxMsgIdx = 20;
            emsg.Param0 = up;
            emsg.Offset = 0;            //ignored
            emsg.GenMsg = $"";          //ignored
            emsg.CmdCheck = "";         //ignored
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Set Audio Level' cmd:20{nl}{emsg}");
        }

        private void ToggleTuning()       //requires bestWsjtxVersions
        {
            if (udpClient2 == null)
            {
                DebugOutput($"{Time()} ToggleTuning skipped, udpClient2:{udpClient2}");
                return;
            }

            if (txEnabled) HaltTx();

            emsg.NewTxMsgIdx = 19;
            emsg.Param0 = cmdPrompts;  //detail level
            emsg.Offset = 0;            //ignored
            emsg.GenMsg = $"";          //ignored
            emsg.CmdCheck = "";         //ignored
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'ToggleTuning' cmd:19{nl}{emsg}");
        }

        private void StartUploadLotw()       //requires bestWsjtxVersions
        {
            if (udpClient2 == null)
            {
                DebugOutput($"{Time()} StartUploadLotw skipped, udpClient2:{udpClient2}");
                return;
            }

            emsg.NewTxMsgIdx = 16;
            emsg.Param0 = false;         //ignored
            emsg.Param1 = false;        //ignored
            emsg.Offset = 0;            //ignored
            emsg.GenMsg = $"";          //ignored
            emsg.CmdCheck = "";         //ignored
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Start upload to LOTW' cmd:16{nl}{emsg}");
        }

        private void EnableDebugLog()
        {
            if (!debug) return;

            emsg.NewTxMsgIdx = 5;
            emsg.GenMsg = $"";         //ignored
            emsg.CmdCheck = "";         //ignored
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Enable Debug' cmd:5{nl}{emsg}");
        }

        public void HaltTuning()
        {
            if (tuning) HaltTx();
        }

        public void HaltTx()
        {
            StopDecodeTimers();
            tuning = false;
            if (udpClient2 != null)
            {
                emsg.NewTxMsgIdx = 12;
                emsg.GenMsg = $"";         //ignored
                emsg.CmdCheck = "";         //ignored
                ba = emsg.GetBytes();
                udpClient2.Send(ba, ba.Length);
                DebugOutput($"{Time()} >>>>>Sent 'HaltTx' cmd:12{nl}{emsg}");
                txEnabled = false;
                wsjtxTxEnableButton = false;
                UpdateDblClkTip();
            }
            else
            {
                DebugOutput($"{Time()} HaltTx skipped, udpClient2:{udpClient2}");
                return;
            }
        }

        // Stop current TX (cmd:12) and uncheck WSJT-X TX Enable button (cmd:8).
        // Use for Escape so TX halts regardless of which mode Jimmy is in.
        public void HaltAndDisableTx()
        {
            HaltTx();
            DisableTx(true);
        }

        // After abandoning a QSO, reset WSJT-X's pending TX message to CQ (cmd:10 + cmd:6).
        // This prevents a stale mid-QSO message (e.g. RRR) from firing if TX is re-enabled
        // before a fresh Reply can reset the selection.
        public void ResetTxToCq()
        {
            if (!ConnectedToWsjtx()) return;
            SetupCq(false);
        }

        public void UpdateModeSelection()
        {
            ctrl.cqModeButton.Checked = txMode == TxModes.CALL_CQ;
            ctrl.listenModeButton.Checked = txMode == TxModes.LISTEN;
        }

        private void UpdateListenModeTxPeriod()
        {
            ctrl.periodLabel.Visible = ctrl.periodComboBox.Visible = ctrl.PeriodHelpLabel.Visible = true;
            ctrl.periodLabel.Enabled = ctrl.periodComboBox.Enabled = txMode == TxModes.LISTEN;
        }

        private void UpdateRR73()
        {
            if (mode == "FT4")
            {
                ctrl.useRR73CheckBox.Checked = true;
                ctrl.useRR73CheckBox.Enabled = false;
            }
            else
            {
                ctrl.useRR73CheckBox.Checked = useRR73;
                ctrl.useRR73CheckBox.Enabled = true;

                if (mode == "") ctrl.WsjtxSettingConfirmed();
            }
        }

        private void StatusTimerTick(object sender, EventArgs e)
        {
            statusTimer.Stop();
            if (decodeCount == 0)
            {
                consecNoDecodes++;
            }
            else
            {
                consecNoDecodes = 0;
            }
            ShowStatus();
        }

        private void StatusTimer2Tick(object sender, EventArgs e)
        {
            statusTimer2.Stop();
            ShowStatus();
        }

        private void ProcessPostDecodeTimerTick(object sender, EventArgs e)
        {
            DecodesCompleted();
        }

        private void ProcessDecodeTimerTick(object sender, EventArgs e)
        {
            processDecodeTimer.Stop();
            //DebugOutput($"{nl}{Time()} processDecodeTimer stop");
            statusTimer.Interval = 2000;        //allow enough time so transmit (if needed) has started
            if (!tuning) statusTimer.Start();
            ProcessDecodes();
        }
        private void ProcessDecodeTimer2Tick(object sender, EventArgs e)
        {
            processDecodeTimer2.Stop();
            string toCall = WsjtxMessage.ToCall(curTxMsg);
            DebugOutput($"{nl}{Time()} ProcessDecodeTimer2Tick, processDecodeTimer2 stop, toCall:{toCall} curTxMsg:'{curTxMsg}' cqPaused:{cqPaused} transmitting:{transmitting} restartQueue:{restartQueue}");
            if (toCall == callInProg) RemoveCall(toCall);       //late decode caused WSJT-X to transmit a new response after the original transmit started

            //           "call CQ" mode and a late 73 may have caused WSJT-X to start calling "CQ" when it should be "CQ DX" or other directed CQ
            //TO-DO
            //bool unwantedCq = (txMode == TxModes.CALL_CQ && (qsoState == WsjtxMessage.QsoStates.CALLING || qsoState == WsjtxMessage.QsoStates.SIGNOFF));
            DebugOutput($"{spacer}txTimeout:{txTimeout} txMode:{txMode} qsoState:{qsoState}");
        }

        //the last decode pass has completed, ready to detect first decode pass
        private void DecodesCompleted()
        {
            postDecodeTimer.Stop();
            DebugOutput($"{nl}{Time()} DecodesCompleted, postDecodeTimer stop, decodeNum:{decodeNum} skipFirstDecodeSeries:{skipFirstDecodeSeries} NegoState:{WsjtxMessage.NegoState}");
            decodeNum++;
            decodeCycle = 0;
            DebugOutput($"{spacer}decodeCycle:{decodeCycle}");

            if (skipFirstDecodeSeries)
            {
                skipFirstDecodeSeries = false;
                oddOffset = 0;
                evenOffset = 0;
                audioOffsets.Clear();
                if (cachedOddOffset > 0) oddOffset = cachedOddOffset;
                if (cachedEvenOffset > 0) evenOffset = cachedEvenOffset;
            }
            else
            {
                //final calculation of best offset
                bool wasCompleted = analysisCompleted;
                if (CalcBestOffset(audioOffsets, period, true))       //calc for period when decodes started
                {
                    ctrl.freqCheckBox.Text = "Use best Tx frequency";
                    ctrl.freqCheckBox.ForeColor = Color.Black;
                    if (!wasCompleted)      // show status and trigger pending CQ only on first completion
                    {
                        StatusView.ShowMessage("Transmit slot analysis complete.", false);
                        if (pendingCqAfterAnalysis)
                        {
                            pendingCqAfterAnalysis = false;
                            ctrl.cqModeButton_Click(null, null);
                        }
                    }
                }
                CalcAvgTimeOffset(true);
            }

            if (WsjtxMessage.NegoState != WsjtxMessage.NegoStates.RECD)
                return;

            if (ctrl.freqCheckBox.Checked)
            {
                if (!transmitting)
                {
                    if (!CheckActive())
                    {
                        //set/show frequency offset for Tx period
                        emsg.NewTxMsgIdx = 10;
                        emsg.GenMsg = $"";          //no effect
                        emsg.SkipGrid = ctrl.skipGridCheckBox.Checked;
                        emsg.UseRR73 = ctrl.useRR73CheckBox.Checked;
                        emsg.CmdCheck = "";         //ignored
                        emsg.Offset = AudioOffsetFromTxPeriod();
                        ba = emsg.GetBytes();
                        udpClient2.Send(ba, ba.Length);
                        DebugOutput($"{Time()} [BAND-AUDIT] DecodesCompleted→cmd:10 sent: bandIdx:{bandIdx} offset:{emsg.Offset}");
                        DebugOutput($"{Time()} >>>>>Sent 'Opt Req' cmd:10{nl}{emsg}");
                        if (settingChanged)
                        {
                            ctrl.WsjtxSettingConfirmed();
                            settingChanged = false;
                        }
                    }
                }
            }
            UpdateDebug();

            if (TrimAllCallDict())
            {
                DebugOutput(AllCallDictString());
            }
        }

        private void HeartbeatNotRecd(object sender, EventArgs e)
        {
            //no heartbeat from WSJT-X, re-init communication
            heartbeatRecdTimer.Stop();
            DebugOutput($"{Time()} heartbeatRecdTimer timed out");
            if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.RECD)
            {
                StatusView.ShowMessage("WSJT-X disconnected", false);
                Sounds.PlaySoundEvent(ctrl.soundEnabled_Disconnected, ctrl.soundFile_Disconnected);
            }
            else
            {
                StatusView.ShowMessage("WSJT-X not responding", true);
            }
            ResetNego();
            CloseAllUdp();          //usually not needed
        }

        private void cmdCheckTimer_Tick(object sender, EventArgs e)
        {
            CmdCheckDialog();
        }

        private void CheckCallQueuePeriod(bool tmpTxFirst)
        {
            bool removed = false;
            var calls = new List<string>();

            foreach (var entry in callDict)
            {
                var decode = entry.Value;
                if (IsEvenCall(decode) == tmpTxFirst)  //entry is wrong time period for new txFirst
                {
                    calls.Add(entry.Key);        //collect keys to delete
                }
            }

            //delete from callQueue
            foreach (string call in calls)
            {
                RemoveCall(call);
                removed = true;
            }

            if (removed) DebugOutput($"{spacer}CheckCallQueuePeriod: calls removed{nl}{CallQueueString()}");
        }

        private bool IsSameMessage(string tx, string lastTx)
        {
            if (tx == lastTx) return true;
            if (WsjtxMessage.ToCall(tx) != WsjtxMessage.ToCall(lastTx)) return false;
            if (WsjtxMessage.IsReport(tx) && WsjtxMessage.IsReport(lastTx)) return true;
            if (WsjtxMessage.IsRogerReport(tx) && WsjtxMessage.IsRogerReport(lastTx)) return true;
            return false;
        }

        //set log file open/closed state
        //return new diagnostic log file state (true = open)
        private bool SetLogFileState(bool enable)
        {
            if (enable)         //want log file opened for write
            {
                if (logSw == null)     //log not already open
                {
                    try
                    {
                        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                        logSw = File.AppendText($"{path}\\log_{DateTime.Now.Date.ToShortDateString().Replace('/', '-')}.txt");      //local time
                        logSw.AutoFlush = true;
                        logSw.WriteLine($"{nl}{nl}{Time()} Opened log");
                    }
                    catch (Exception err)
                    {
                        err.ToString();
                        logSw = null;
                        return false;       //log file state = closed
                    }
                }
                return true;       //log file state = open
            }
            else    //want log file flushed and closed
            {
                if (logSw != null)
                {
                    logSw.WriteLine($"{Time()} Closing log...");
                    logSw.Flush();
                    logSw.Close();
                    logSw = null;
                }
                return false;       //log file state = closed
            }
        }

        private void ReadPotaLogDict()
        {
            List<string> updList = new List<string>();
            string pathFileNameExt = $"{path}\\pota.txt";
            StreamReader potaSr = null;
            potaSw = null;
            potaLogDict.Clear();

            try
            {
                if (File.Exists(pathFileNameExt))
                {
                    string line = null;
                    string today = DateTime.Now.ToShortDateString();        //local time
                    potaSr = File.OpenText(pathFileNameExt);
                    DebugOutput($"{spacer}POTA log opened for read");

                    while ((line = potaSr.ReadLine()) != null)
                    {
                        string[] parts = line.Split(new char[] { ',' });   //call,date,band,mode
                        if (parts.Length == 4 && parts[1] == today)
                        {                       //date     band       mode
                            string potaInfo = $"{parts[1]},{parts[2]},{parts[3]}";
                            List<string> curList;
                            //                          call
                            if (potaLogDict.TryGetValue(parts[0], out curList))
                            {
                                if (!curList.Contains(potaInfo)) curList.Add(potaInfo);
                            }
                            else
                            {
                                List<string> newList = new List<string>();
                                newList.Add(potaInfo);
                                //              call
                                potaLogDict.Add(parts[0], newList);
                            }

                            updList.Add(line);
                        }
                    }
                    potaSr.Close();
                }
            }
            catch (Exception err)
            {
                DebugOutput($"{spacer}POTA log open/read failed: {err.ToString()}");
                if (potaSr != null) potaSr.Close();
                return;
            }

            //open, re-write updated file; leave file open if no error
            try
            {
                if (File.Exists(pathFileNameExt)) File.Delete(pathFileNameExt);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                potaSw = File.AppendText(pathFileNameExt);
                potaSw.AutoFlush = true;
                DebugOutput($"{spacer}POTA log opened for write");

                foreach (string line in updList)
                {
                    potaSw.WriteLine(line);
                }
            }
            catch (Exception err)
            {
                DebugOutput($"{spacer}POTA log open/rewrite failed: {err.ToString()}");
                potaSw = null;
            }
            DebugOutput($"{PotaLogDictString()}");
        }

        private void AddPotaLogDict(string potaCall, DateTime potaDtLocal, string potaBand, string potaMode)     //UTC
        {
            bool updateLog = false;

            string potaInfo = $"{potaDtLocal.Date.ToShortDateString()},{potaBand},{potaMode}";
            DebugOutput($"{spacer}AddPotaLogDict, potaInfo:{potaInfo}");
            DebugOutput($"{PotaLogDictString()}");
            List<string> curList;
            if (potaLogDict.TryGetValue(potaCall, out curList))
            {
                if (!curList.Contains(potaInfo))
                {
                    curList.Add(potaInfo);
                    updateLog = true;
                }
            }
            else
            {
                List<string> newList = new List<string>();
                newList.Add(potaInfo);
                potaLogDict.Add(potaCall, newList);
                updateLog = true;
            }

            if (potaSw != null && updateLog)
            {
                potaSw.WriteLine($"{potaCall},{potaInfo}");
                DebugOutput($"{PotaLogDictString()}");
            }
        }

        private bool CalcBestOffset(List<int> offsetList, Periods decodePeriod, bool clearList)
        {
            DebugOutput($"{Time()} CalcBestOffset, decodePeriod:{decodePeriod} clearList:{clearList} offsetList.Count:{offsetList.Count()} skipFirstDecodeSeries:{skipFirstDecodeSeries}");

            if (period == Periods.UNK)
            {
                oddOffset = 0;
                evenOffset = 0;
                offsetList.Clear();
                timeOffset = 0;
                return false;
            }

            int bestOffset = 0;
            int maxInterval = 0;

            //set limits
            offsetList.Add(offsetLoLimit);
            offsetList.Add(offsetHiLimit);

            offsetList.Sort();
            int[] offsets = offsetList.ToArray();

            for (int i = 0; i < offsets.Length - 1; i++)
            {
                if (offsets[i + 1] - offsets[i] > maxInterval)
                {
                    maxInterval = offsets[i + 1] - offsets[i];
                    bestOffset = (offsets[i + 1] + offsets[i]) / 2;
                }
            }

            if (decodePeriod == Periods.EVEN)
            {
                evenOffset = bestOffset;
                if (bestOffset > 0) cachedEvenOffset = bestOffset;
            }
            else
            {
                oddOffset = bestOffset;
                if (bestOffset > 0) cachedOddOffset = bestOffset;
            }

            if (clearList) offsetList.Clear();

            DebugOutput($"{spacer}evenOffset:{evenOffset} oddOffset:{oddOffset}");

            bool bothKnown = oddOffset > 0 && evenOffset > 0;
            if (bothKnown) analysisCompleted = true;
            return bothKnown;
        }

        private UInt32 AudioOffsetFromMsg(EnqueueDecodeMessage msg)        //msg is a reply msg, so tx msg will be opposite time period
        {
            if (msg == null || !ctrl.freqCheckBox.Checked) return 0;

            if (IsEvenCall(msg))
            {
                return (UInt32)oddOffset;
            }
            else
            {
                return (UInt32)evenOffset;
            }
        }

        private UInt32 AudioOffsetFromTxPeriod()
        {
            if ((period == Periods.UNK || !ctrl.freqCheckBox.Checked))
                return 0;

            if (txFirst)
            {
                return (UInt32)evenOffset;
            }
            else
            {
                return (UInt32)oddOffset;
            }
        }

        private int CalcTimerAdj()
        {
            return (mode == "FT8" ? 150 /*300*/ : (mode == "FT4" ? 150 /*300*/ : (mode == "FST4" ? 750 : 300)));      //msec
        }

        private void UpdateMaxTxRepeat()
        {
            int limit = (int)ctrl.timeoutNumUpDown.Value;

            if (!ctrl.optimizeCheckBox.Checked || _manualCallInProg)
            {
                maxTxRepeat = limit;
            }
            else
            {
                // Proportional reduction based on waiting-call depth.
                // Factor:  0–1 waiting → 100%,  2 → 75%,  3 → 50%,  4+ → 33%.
                // Integer truncation (floor) is used so the result is always ≤ limit
                // and never overshoots the target percentage.
                double factor;
                if      (callQueue.Count <= 1) factor = 1.0;
                else if (callQueue.Count == 2) factor = 0.75;
                else if (callQueue.Count == 3) factor = 0.5;
                else                           factor = 1.0 / 3.0;

                int reduced = Math.Max(1, (int)(limit * factor));
                maxTxRepeat = reduced;

                if (debug && reduced < limit)
                    DebugOutput($"{spacer}Optimize: maxTxRepeat {limit}→{reduced} ({callQueue.Count} calls waiting)");
            }

            UpdateMaxPrevTo();
            UpdateMaxAutoGenEnqueue();
        }

        //if low number of repeated msgs before timeout
        //allow calls to be re-queued more often     
        private void UpdateMaxPrevTo()
        {
            maxPrevTo = 2;
            if (maxTxRepeat == 3) maxPrevTo = 3;
            if (maxTxRepeat == 2) maxPrevTo = 4;
            if (maxTxRepeat == 1) maxPrevTo = 5;
            maxPrevPotaTo = Math.Min((int)(maxPrevTo * 1.5), 8);
        }

        public void UpdateMaxAutoGenEnqueue()
        {
            int baseVal = ctrl.maxQueuedCallsBase;
            maxAutoGenEnqueue = baseVal;
            if (maxTxRepeat == 3) maxAutoGenEnqueue = baseVal + 1;
            if (maxTxRepeat == 2) maxAutoGenEnqueue = baseVal + 2;
            if (maxTxRepeat == 1) maxAutoGenEnqueue = baseVal + 3;

            if (IsPrimarySort(RankMethods.CALL_ORDER) || IsPrimarySort(RankMethods.MOST_RECENT)) return;

            float cqFactor = ctrl.cqOnlyRadioButton.Enabled && ctrl.cqOnlyRadioButton.Checked ? 1.25f : 1.0f;
            float gridFactor = ctrl.cqGridRadioButton.Enabled && ctrl.cqGridRadioButton.Checked ? 1.35f : 1.0f;
            float anyFactor = ctrl.anyMsgRadioButton.Enabled && ctrl.anyMsgRadioButton.Checked ? 1.5f : 1.0f;
            float obFactor = ctrl.bandComboBox.Enabled && ctrl.bandComboBox.SelectedIndex == (int)WsjtxClient.NewCallBands.CURRENT ? 1.75f : 1.0f;
            float factor = cqFactor * gridFactor * anyFactor * obFactor;
            maxAutoGenEnqueue = (int)(maxAutoGenEnqueue * factor);
            maxAutoGenEnqueue = Math.Min(maxAutoGenEnqueue, ctrl.maxQueuedCallsBase);
        }

        private void DisableAutoFreqPause()
        {
            DebugOutput($"{Time()} DisableAutoFreqPause autoFreqPauseMode:{autoFreqPauseMode} consecCqCount:{consecCqCount} consecTimeoutCount:{consecTimeoutCount}");
            autoFreqPauseMode = autoFreqPauseModes.DISABLED;
            consecCqCount = 0;
            consecTimeoutCount = 0;
            consecTxCount = 0;
            UpdateCallInProg();
            UpdateDebug();
            DebugOutput($"{spacer}autoFreqPauseMode:{autoFreqPauseMode} consecCqCount:0 consecTimeoutCount:0 consecTxCount:0");
        }

        private string CallPriorityString(string call)
        {
            if (call == null) return "";

            return $"{call}:{Priority(call)}";
        }

        //for the specified call, return the priority, or default if not found
        //check allCallDict and replyDecode
        private int Priority(string call)
        {
            int priority = (int)CallPriority.DEFAULT;
            if (call == null || call == "CQ") return priority;

            EnqueueDecodeMessage msg = null;
            List<EnqueueDecodeMessage> msgList;
            if (allCallDict.TryGetValue(call, out msgList))
            {
                if (msgList.Count > 0)
                {
                    msg = msgList.Last();       //list is in chronological order, latest at end
                    priority = msg.Priority;
                }
            }
            else
            {
                if (replyDecode != null && callInProg != null && replyDecode.DeCall() == call) priority = replyDecode.Priority;
            }
            return priority;
        }

        //for the specified call, return the country, or empty string if not found
        //check allCallDict and replyDecode
        private string Country(string call)
        {
            string country = "";
            if (call == null || call == "CQ") return country;

            EnqueueDecodeMessage msg = null;
            List<EnqueueDecodeMessage> msgList;
            if (allCallDict.TryGetValue(call, out msgList))
            {
                if (msgList.Count > 0)
                {
                    msg = msgList.Last();       //list is in chronological order, latest at end
                    country = msg.Country;
                }
            }
            else
            {
                if (replyDecode != null && callInProg != null && replyDecode.DeCall() == call) country = replyDecode.Country;
            }
            return country;
        }

        private void StartProcessDecodeTimer2()
        {
            if (processDecodeTimer2.Enabled || (mode != "FT8" && mode != "FT4")) return;
            processDecodeTimer2.Interval = (mode == "FT8" ? 1500 : 750);
            processDecodeTimer2.Start();
            DebugOutput($"{Time()} processDecodeTimer2 start");
        }

        private void StopDecodeTimers()
        {
            if (processDecodeTimer.Enabled)
            {
                processDecodeTimer.Stop();       //no xmit cycle now
                DebugOutput($"{Time()} processDecodeTimer stop");
            }
            if (processDecodeTimer2.Enabled)
            {
                processDecodeTimer2.Stop();
                DebugOutput($"{Time()} processDecodeTimer2 stop");
            }
        }

        //high priority call should log late (needs definite confirmation, not presumed/implied)
        private bool IsLogEarly(string deCall)
        {
            if (!ctrl.logEarlyCheckBox.Checked) return false;
            return Priority(deCall) > (int)CallPriority.NEW_COUNTRY_ON_BAND;
        }

        /*private bool SendUdp(byte[] ba)
        {
            if (udpClient2 == null) return false;

            try
            {
                udpClient2.Send(ba, ba.Length);
            }
            catch (Exception e)
            {
                DebugOutput($"{Time()} sendUdp error:{e.ToString()}");
                return false;
            }
            return true;
        }*/

        //return success or failure
        private bool DetectUdpSettings(out IPAddress ipa, out int prt, out bool mul)
        {
            //use WSJT-X.ini file for settings
            string pgmNameWsjtx = "WSJT-X";
            string pathWsjtx = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\{pgmNameWsjtx}";
            string pathFileNameExtWsjtx = pathWsjtx + "\\" + pgmNameWsjtx + ".ini";

            //set defaults
            ipa = IPAddress.Parse("127.0.0.1");
            prt = 2237;
            mul = false;

            //temp
            IPAddress ipaAddr;
            int prtInt;
            string ipaString;

            if (!Directory.Exists(pathWsjtx)) return false;

            try
            {
                IniFile iniFile = new IniFile(pathFileNameExtWsjtx);
                ipaString = iniFile.Read("UDPServer", "Configuration");
                ipaAddr = IPAddress.Parse(ipaString);
                prtInt = Convert.ToInt32(iniFile.Read("UDPServerPort", "Configuration"));
            }
            catch
            {
                //ctrl.BringToFront();
                //MessageBox.Show($"Unable to open settings file: " + pathFileNameExt + "{nl}{nl}Continuing with default settings...", pgmName, MessageBoxButtons.OK);
                return false;
            }

            if (ipaString == "" || prtInt == 0)
            {
                return false;
            }

            prt = prtInt;
            ipa = ipaAddr;
            mul = ipaString.Substring(0, 4) != "127.";
            return true;
        }

        private bool IsWsjtxRunning()
        {
            string file = "WSJT-X.lock";
            string pathFileNameExt = $"{Path.GetTempPath()}{file}";
            //string linuxPathFileNameExt = "Z:\\tmp\\WSJT-X.lock";     //wine/linux testing
            return File.Exists(pathFileNameExt) /*|| File.Exists(linuxPathFileNameExt)*/;     //wine/linux testing
        }

        //must call only when in WAIT state
        //to avoid async cakkback using disposed udpClient
        private void CloseAllUdp()
        {
            DebugOutput($"{Time()} CloseAllUdp");

            try
            {
                if (udpClient != null)
                {
                    udpClient.Close();
                    udpClient = null;
                    DebugOutput($"{spacer}closed udpClient");
                }
                if (udpClient2 != null)
                {
                    udpClient2.Close();
                    udpClient2 = null;
                    DebugOutput($"{spacer}closed udpClient2");
                }
            }
            catch (Exception e)         //udpClient might be disposed already
            {
                DebugOutput($"{spacer}error:{e.ToString()}");
            }
        }

        private DialogResult Confirm(string s)
        {
            var confDlg = new ConfirmDlg();
            confDlg.text = s;
            confDlg.Owner = ctrl;
            confDlg.ShowDialog();
            return confDlg.DialogResult;
        }

        private void SetupCq(bool enableTx)
        {
            //set/show frequency offset for period after decodes started
            emsg.NewTxMsgIdx = 10;
            emsg.GenMsg = $"";          //no effect
            emsg.SkipGrid = ctrl.skipGridCheckBox.Checked;
            emsg.UseRR73 = ctrl.useRR73CheckBox.Checked;
            emsg.CmdCheck = "";         //ignored
            emsg.Offset = AudioOffsetFromTxPeriod();
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Opt Req' cmd:10{nl}{emsg}");
            if (settingChanged)
            {
                ctrl.WsjtxSettingConfirmed();
                settingChanged = false;
            }

            emsg.NewTxMsgIdx = 6;
            emsg.GenMsg = $"CQ{NextDirCq()} {myCall} {myGrid}";
            emsg.SkipGrid = ctrl.skipGridCheckBox.Checked;
            emsg.UseRR73 = ctrl.useRR73CheckBox.Checked;
            emsg.CmdCheck = "";         //ignored
            ba = emsg.GetBytes();           //set up for CQ, auto, call 1st
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Setup CQ' cmd:6{nl}{emsg}");
            qsoState = WsjtxMessage.QsoStates.CALLING;      //in case enqueueing call manually right now
            replyCmd = null;        //invalidate last reply cmd since not replying
            replyDecode = null;
            curCmd = emsg.GenMsg;
            newDirCq = false;           //if set, was processed here
            DebugOutput($"{spacer}qsoState:{qsoState} (was {lastQsoState} replyCmd:'{replyCmd}') newDirCq:{newDirCq}");

            if (enableTx) EnableTx();             //sets WSJT-X "Enable Tx" button state
        }

        private void SetPeriodState()
        {
            DateTime dtNow = DateTime.UtcNow;
            DebugOutput($"{Time()} SetPeriodState, dtNow:{dtNow.ToString("HHmmss.fff")} trPeriod:{trPeriod}");
            period = IsEvenPeriod((dtNow.Minute * 60) + dtNow.Second) ? Periods.EVEN : Periods.ODD;       //determine this period
            DebugOutput($"{spacer}period:{period}");
        }

        private void LogBeep()
        {
            if (!debug) return;
            Console.Beep();
            DebugOutput($"{spacer}BEEP");
        }

        //get (and remove) call/msg at specified index in queue;
        //queue not assumed to have any entries;
        //return null if failure
        private string GetCall(int idx, out EnqueueDecodeMessage dmsg)
        {
            dmsg = null;
            if (callQueue.Count == 0)
            {
                DebugOutput($"{spacer}not exists idx:{idx}");
                return null;
            }

            var callArray = callQueue.ToArray();
            string call = callArray[idx];

            if (!callDict.TryGetValue(call, out dmsg))
            {
                DebugOutput("ERROR: '{call}' not found");
                UpdateDebug();
                return null;
            }

            RemoveCall(call);

            DebugOutput($"{spacer}call:{call}: msg:'{dmsg.Message}'");
            return call;
        }

        private void ReplyTo(int queueIdx)
        {
            var dmsg = new EnqueueDecodeMessage();
            string nCall = GetCall(queueIdx, out dmsg);
            DebugOutput($"{Time()} ReplyTo, queueIdx:{queueIdx} nCall:'{nCall}'");
            ReplyTo(dmsg);
        }

        private void ReplyTo(EnqueueDecodeMessage dmsg)
        {
            if (dmsg == null)
            {
                DebugOutput($"{Time()} ReplyTo, error: msg not is null");
                return;
            }

            string nCall = dmsg.DeCall();
            string toCall = WsjtxMessage.ToCall(dmsg.Message);
            DebugOutput($"{Time()} ReplyTo, nCall:'{nCall}' toCall:{toCall}");

            if (ctrl.dontTransmitToBusyStation && IsStationBusyElsewhere(nCall))
            {
                DebugOutput($"{Time()} ReplyTo, skipped: {nCall} is busy with another station, will retry next cycle");
                StatusView.ShowMessage($"Waiting -- {nCall} is busy, will retry next cycle", false);
                return;
            }

            if (WsjtxMessage.IsCQ(dmsg.Message))                  //save the grid for logging
            {
                AddAllCallDict(nCall, dmsg);
            }

            //set call options
            emsg.NewTxMsgIdx = 10;
            emsg.GenMsg = $"";          //no effect
            emsg.SkipGrid = (dmsg.UseStdReply ? false : ctrl.skipGridCheckBox.Checked);
            emsg.UseRR73 = ctrl.useRR73CheckBox.Checked;
            emsg.CmdCheck = "";         //ignored
            emsg.Offset = AudioOffsetFromMsg(dmsg);
            ba = emsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Opt Req' cmd:10{nl}{emsg}");
            if (settingChanged)
            {
                ctrl.WsjtxSettingConfirmed();
                settingChanged = false;
            }

            //send Reply message
            var rmsg = new ReplyMessage();
            rmsg.SchemaVersion = WsjtxMessage.NegotiatedSchemaVersion;
            rmsg.Id = WsjtxMessage.UniqueId;
            rmsg.SinceMidnight = dmsg.SinceMidnight;
            rmsg.Snr = dmsg.Snr;
            rmsg.DeltaTime = dmsg.DeltaTime;
            rmsg.DeltaFrequency = dmsg.DeltaFrequency;
            rmsg.Mode = dmsg.Mode;
            rmsg.Message = dmsg.Message;
            rmsg.UseStdReply = dmsg.UseStdReply;
            ba = rmsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            replyCmd = dmsg.Message;            //save the last reply cmd to determine which call is in progress
            replyDecode = dmsg.DeepCopy();      //save the decode the reply cmd derived from
            curCmd = dmsg.Message;
            SetCallInProg(nCall);
            DebugOutput($"{Time()} >>>>>Sent 'Reply To Msg' cmd:{nl}{rmsg} lastTxMsg:'{lastTxMsg}'{nl}{spacer}replyCmd:'{replyCmd}'");
            //toCallTxStart = null to flag intentional interruption
            if (transmitting) SetTxStartInfo(DateTime.UtcNow, null);  //because tx-start event already happened

            EnableTx();             //also sets WSJT-X "Tx Enable" button state

            cqPaused = false;
            restartQueue = false;           //get ready for next decode phase
            txTimeout = false;              //ready for next timeout
            tCall = null;
            xmitCycleCount = 0;
            timedOutCall = null;
            UpdateDebug();
        }

        // Derive the ranking category from classification fields.
        // Category is separate from Priority so behavioral checks remain tied to Priority.
        // Called after Priority is set; must be called before SetRank().
        private CallCategory DeriveCategory(EnqueueDecodeMessage d)
        {
            CallCategory cat;
            switch (d.Priority)
            {
                case (int)CallPriority.NEW_COUNTRY:         cat = CallCategory.NEW_COUNTRY; break;
                case (int)CallPriority.NEW_COUNTRY_ON_BAND: cat = CallCategory.NEW_COUNTRY_ON_BAND; break;
                case (int)CallPriority.TO_MYCALL:           cat = CallCategory.TO_MYCALL; break;
                case (int)CallPriority.MANUAL_SEL:          cat = CallCategory.MANUAL_SEL; break;
                case (int)CallPriority.WANTED_CQ:           cat = CallCategory.WANTED_CQ; break;
                default:
                    string deCall = d.DeCall();
                    if (wantedCalls.Count > 0 && !string.IsNullOrEmpty(deCall) && wantedCalls.Contains(deCall))
                        cat = CallCategory.ALWAYS_WANTED;
                    else if (IsPotaCall(d)) cat = CallCategory.POTA;
                    else if (IsSotaCall(d)) cat = CallCategory.SOTA;
                    else if (IsHrcWasNeeded(d))       cat = CallCategory.WAS_NEEDED;
                    else if (IsHrcDxccUnconfirmed(d)) cat = CallCategory.DXCC_UNCONFIRMED;
                    else if (IsHrcZoneNeeded(d))      cat = CallCategory.ZONE_NEEDED;
                    else
                    {
                        string matchedRuleId = MatchedAwardRuleId(d);
                        if (matchedRuleId != null) { cat = CallCategory.STILL_NEEDED; d.MatchedAwardRuleId = matchedRuleId; }
                        else cat = CallCategory.DEFAULT;
                    }
                    break;
            }
            if (debug) DebugOutput($"{spacer}DeriveCategory: '{d.DeCall()}' pri:{d.Priority} → {cat}");
            return cat;
        }

        // Independent of Category/Call Filters admission by design: a station can be
        // classified NEW_COUNTRY (or anything else) for ranking/queueing purposes and
        // still separately match one of the actively-checked awards -- e.g. turning off
        // "New DXCC" during a DXCC contest must not also silence award alerts for the
        // same stations. This runs for every decode, admitted or not, and plays the
        // "Award Needed" sound (with its own cooldown) when a match is found. It does
        // not affect ranking, Priority, Category, or Call Filters admission in any way.
        private void CheckAwardAlert(EnqueueDecodeMessage d)
        {
            if (activeAwardTags.Count == 0) return;
            string call = d.DeCall();
            if (string.IsNullOrEmpty(call)) return;

            string matchedRuleId = MatchedAwardRuleId(d);
            if (matchedRuleId == null) return;
            if (d.MatchedAwardRuleId == null) d.MatchedAwardRuleId = matchedRuleId;

            if (!IsAlertCooledDown(_awardAlertTimes, call, AwardAlertCooldownSecs)) return;
            _awardAlertTimes[call] = DateTime.UtcNow;
            Sounds.PlaySoundEvent(ctrl.soundEnabled_AwardNeeded, ctrl.soundFile_AwardNeeded, call, matchedRuleId);
        }

        // Returns true if dmsg is associated with a "CQ SOTA" transmission.
        private bool IsSotaCall(EnqueueDecodeMessage emsg)
        {
            if (emsg.IsSota()) return true;
            EnqueueDecodeMessage dmsg = CqMsg(emsg.DeCall());
            if (dmsg == null) return false;
            return dmsg.IsSota();
        }

        // ── HRC (Ham Radio Center) category helpers ─────────────────────────────────
        // All three read only in-memory HashSets populated at startup / after import.
        // If the HRC database is unavailable, the sets remain empty and these return false.

        // Each guarded by activeAwardTags (the realized, actually-live-tagging cache) rather
        // than activeAwardRuleIds (the raw checkbox state), so it auto-retires only once the
        // equivalent generic award (WAS/DXCC/WAZ) is BOTH checked in the new Still Need list
        // AND actually producing usable live tags -- e.g. checking "DXCC" alone does not
        // suppress this, since the shipped DXCC.ini is Target=COUNT and so never enters
        // activeAwardTags (RuleResult.StillNeeded is only ever populated for Target=All; see
        // Controller.RefreshStillNeedCache()). Previously checked activeAwardRuleIds directly,
        // which silently disabled DXCC-needed alerts the moment the box was checked even though
        // the new system was never actually going to tag anything in its place.
        private bool IsHrcWasNeeded(EnqueueDecodeMessage d)
        {
            if (hrcNeededStates.Count == 0 || activeAwardTags.ContainsKey("WAS")) return false;
            string grid = WsjtxMessage.Grid(d.Message);
            if (string.IsNullOrEmpty(grid)) return false;
            string state = GridToUsState(grid);
            return !string.IsNullOrEmpty(state) && hrcNeededStates.Contains(state);
        }

        private bool IsHrcDxccUnconfirmed(EnqueueDecodeMessage d)
        {
            if (hrcUnconfirmedDxcc.Count == 0 || activeAwardTags.ContainsKey("DXCC")) return false;
            string call = d.DeCall();
            if (string.IsNullOrEmpty(call)) return false;
            var entity = lookupManager.GetClubLogEntity(call);
            return entity != null && entity.Adif > 0 && hrcUnconfirmedDxcc.Contains(entity.Adif);
        }

        private bool IsHrcZoneNeeded(EnqueueDecodeMessage d)
        {
            if (hrcNeededZones.Count == 0 || activeAwardTags.ContainsKey("WAZ")) return false;
            string call = d.DeCall();
            if (string.IsNullOrEmpty(call)) return false;
            var entity = lookupManager.GetClubLogEntity(call);
            return entity != null && entity.CqZone > 0 && hrcNeededZones.Contains(entity.CqZone);
        }

        // Matches a decode against every actively-checked award (activeAwardTags), built by
        // Controller.RefreshStillNeedCache() from whichever Rule Definitions are checked in
        // the Still Need tab. Only a fast in-memory lookup happens here -- the RuleEngine
        // evaluation itself already ran once per rule, at selection/refresh time, not per
        // decode. Returns the matched rule's Id, or null if none matched. The field used to
        // derive the match key depends on each rule's GroupBy; kinds not listed here are
        // never included in activeAwardTags (see RuleEngine.SupportsLiveTag).
        private string MatchedAwardRuleId(EnqueueDecodeMessage d)
        {
            if (activeAwardTags.Count == 0) return null;
            string call = d.DeCall();
            if (string.IsNullOrEmpty(call)) return null;

            foreach (var tag in activeAwardTags.Values)
            {
                if (tag.Set.Count == 0) continue;
                bool match;
                switch (tag.GroupBy)
                {
                    case RuleGroupBy.Callsign:
                        match = tag.Set.Contains(call);
                        break;

                    case RuleGroupBy.State:
                        string grid = WsjtxMessage.Grid(d.Message);
                        string state = string.IsNullOrEmpty(grid) ? null : GridToUsState(grid);
                        match = !string.IsNullOrEmpty(state) && tag.Set.Contains(state);
                        break;

                    case RuleGroupBy.CqZone:
                    {
                        var entity = lookupManager.GetClubLogEntity(call);
                        match = entity != null && entity.CqZone > 0 && tag.Set.Contains(entity.CqZone.ToString());
                        break;
                    }

                    case RuleGroupBy.Continent:
                        // d.Continent comes straight from WSJT-X's own decode message -- always
                        // available, no Club Log dependency (unlike CqZone/Dxcc above, WSJT-X
                        // doesn't supply those as decode fields, only country/continent).
                        match = !string.IsNullOrEmpty(d.Continent) && tag.Set.Contains(d.Continent);
                        break;

                    case RuleGroupBy.Dxcc:
                    {
                        var entity = lookupManager.GetClubLogEntity(call);
                        match = entity != null && entity.Adif > 0 && tag.Set.Contains(entity.Adif.ToString());
                        break;
                    }

                    default:
                        match = false;
                        break;
                }
                if (match) return tag.RuleId;
            }
            return null;
        }

        private void SetRank(EnqueueDecodeMessage d) =>
            Ranker.SetRank(d, debug ? (Action<string>)(s => DebugOutput($"{spacer}{s}")) : null);

        private string Reason(EnqueueDecodeMessage d)
        {
            switch (d.Priority)
            {
                case (int)CallPriority.NEW_COUNTRY:
                    return "New country";

                case (int)CallPriority.NEW_COUNTRY_ON_BAND:
                    return "New country on band";

                case (int)CallPriority.TO_MYCALL:
                    return $"Call to {myCall}";

                case (int)CallPriority.MANUAL_SEL:
                    return "Manually selected";

                case (int)CallPriority.WANTED_CQ:
                    return "Directed CQ";

                default:
                    return "Auto-selected";
            }

        }
        private void SortCalls()
        {
            var list = new List<EnqueueDecodeMessage>();
            foreach (EnqueueDecodeMessage d in callDict.Values)
            {
                SetRank(d);
                list.Add(d);
            }

            Func<string, bool> isLoTWUser = lookupManager != null ? (Func<string, bool>)lookupManager.IsLoTWUser : null;
            list.Sort((p, q) => Ranker.Compare(p, q, isLoTWUser, lotwBoostEnabled));

            callQueue.Clear();
            foreach (EnqueueDecodeMessage d in list)
            {
                callQueue.Enqueue(d.DeCall());
            }

            ShowQueue();
            if (ctrl.advancedCallLayout) ShowAdvancedQueue(null);
            DebugOutput($"{spacer}callQueue re-sorted{nl}{CallQueueString()}");
        }

        //decode rank already set
        private bool CanAddWantedCall(string call, EnqueueDecodeMessage decode, bool isWantedCall, bool isEvenPeriod)
        {
            int periodCount = PeriodCallCount(isEvenPeriod);
            if (periodCount < maxAutoGenEnqueue || decode.IsNewCallOnBand || decode.IsNewCallAnyBand || IsPrimarySort(RankMethods.MOST_RECENT)) return true;
            if (IsPrimarySort(RankMethods.CALL_ORDER) || !isWantedCall) return false;

            var callArray = callQueue.ToArray();
            EnqueueDecodeMessage d;

            for (int i = 0; i < callQueue.Count; i++)
            {
                if (!callDict.TryGetValue(callArray[i], out d)) continue;
                if (IsEvenCall(d) != isEvenPeriod) continue;
                if (d.Rank < decode.Rank)
                {
                    DebugOutput($"{spacer}CanAddWantedCall, add call:{call}");
                    return true;
                }
            }
            return false;
        }

        private bool IsCorrectTimePeriod(EnqueueDecodeMessage dmsg, DateTime dtNow)
        {
            bool res = true;
            bool evenCall = IsEvenCall(dmsg);
            bool evenPeriod = txFirst;          //CQ mode can only xmit on txFirst setting
                                                //"dtNow" will be shortly before the tx period following the decode period, at least once per cycle;
                                                //add 2 seconds to dtNow to assure that decision to reply is based on the tx period's time
            if (txMode == TxModes.LISTEN) evenPeriod = IsEvenPeriod((dtNow.Minute * 60) + dtNow.Second + 2);       //listen mode can xmit on either period depending on current time
            if (!dmsg.UseStdReply) res = evenCall != evenPeriod;      //reply is in opposite time period from msg
            DebugOutput($"{spacer}IsCorrectTimePeriod:{res} evenCall:{evenCall} evenPeriod:{evenPeriod} UseStdReply:{dmsg.UseStdReply}");
            return res;
        }

        private void AddTimeoutCall(string call)
        {
            int callTimeouts;
            if (timeoutCallDict.TryGetValue(call, out callTimeouts))
            {
                timeoutCallDict.Remove(call);
            }
            timeoutCallDict.Add(call, ++callTimeouts);
            DebugOutput($"{Time()} AddTimeoutCall, call:{call} callTimeouts:{callTimeouts}");
        }

        private void UpdateBandComboBox()
        {
            int idx = ctrl.bandComboBox.SelectedIndex;
            ctrl.bandComboBox.Items.Clear();
            if (opMode == OpModes.ACTIVE)
            {
                string b = FreqToBandStr(dialFrequency / 1e6);
                if (b == null) b = "this band";
                ctrl.bandComboBox.Items.AddRange(new string[] { "for 1 band", $"for {b}" });
            }
            else
            {
                ctrl.bandComboBox.Items.AddRange(new string[] { "for 1 band", "this band" });
            }
            ctrl.bandComboBox.SelectedIndex = idx;
        }

        private void ClearCallTimeout(string call)
        {
            if (call == null) return;

            int prevTo;
            if (timeoutCallDict.TryGetValue(call, out prevTo))
            {
                timeoutCallDict.Remove(call);
            }
            timeoutCallDict.Add(call, 0);
        }

        private void UpdateDblClkTip()
        {
            if (callQueue.Count == 0) ShowQueue();      //update dbl-click tip
        }

        private bool IsCorrectTimePeriodForMode(EnqueueDecodeMessage emsg)
        {
            // Advanced layout shows TX1 and TX2 lists simultaneously, so both periods
            // must be allowed into the queue regardless of current txFirst setting.
            if (ctrl.advancedCallLayout)
            {
                DebugOutput($"{Time()} IsCorrectTimePeriodForMode, advanced mode: accepting all periods");
                return true;
            }

            bool evenCall = IsEvenCall(emsg);
            bool res = false;
            DebugOutput($"{Time()} IsCorrectTimePeriodForMode, txMode:{txMode} txFirst:{txFirst} evenCall:{evenCall}");

            if (txMode == TxModes.CALL_CQ || ctrl.periodComboBox.SelectedIndex == (int)ListenModeTxPeriods.ANY)
            {
                res = (evenCall != txFirst);
            }
            else        //listen mode
            {
                res = (evenCall != (ctrl.periodComboBox.SelectedIndex == (int)ListenModeTxPeriods.EVEN));
            }
            DebugOutput($"{spacer}IsCorrectTimePeriodForMode:{res}");
            return res;
        }

        //return non-unique random message sequence number for ranking
        //numbers for previous decode always increase for next decode
        //int range -2,147,483,648 to 2,147,483,647, allows for 8388608 decode cycles before underflow
        private int NextMsgSeqNum()
        {
            int m = decodeNum * 256;            //assume max of 256 decodes per period
            return m + rnd.Next(0, 256);
        }

        private void SetTxStartInfo(DateTime dtNow, string toCall)
        {
            txBeginTime = dtNow;
            toCallTxStart = toCall;
            DebugOutput($"{spacer}txBeginTime:{txBeginTime.ToString("HHmmss.fff")} toCallTxStart:'{toCallTxStart}'");
        }

        private string CurrentDecodeCycleString()
        {
            if (!decoding) return "";
            return $"{decodeCycle}";
        }

        private bool IsBlocked(string call)
        {
            return blockList.Contains(call);
        }

        private int MaxTimeoutsForMsg(bool isPota)
        {
            DebugOutput($"{spacer}isPota:{isPota} maxPrevTo:{maxPrevTo} maxPrevPotaTo:{maxPrevPotaTo}");
            return (isPota || IsPrimarySort(RankMethods.MOST_RECENT)) ? maxPrevPotaTo : maxPrevTo;
        }

        //return true if call is (or associated with a previous) "CQ POTA"
        private bool IsPotaCall(EnqueueDecodeMessage emsg)
        {
            if (emsg.IsCQ()) return emsg.IsPota();

            EnqueueDecodeMessage dmsg = CqMsg(emsg.DeCall());
            if (dmsg == null) return false;         //never a CQ POTA from deCall
            return dmsg.IsPota();
        }

        private void UpdateBlockList(string text)
        {
            blockList = text.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList<string>();
        }

        private string Spacify(string s)
        {
            if (s == null) return "";

            var a = s.ToArray();
            var sb = new StringBuilder();

            foreach (char c in a)
            {
                if (c != ' ')
                {
                    sb.Append(c);
                    sb.Append(' ');
                }

            }
            return sb.ToString().Trim();
        }

        private string SpacifyMsg(string msg)
        {
            var sa = msg.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (sa.Length < 2) return "";

            string pl = sa.Length >= 3 ? $", {Spacify(sa[2])}" : "";
            return $"{Spacify(sa[0])}, {Spacify(sa[1])}{pl}";
        }

        private string SpacifyPayload(string s)
        {
            if (s == null) return "";
            if (s == "" || s == "CQ" || s == "RRR") return s;
            if (s.Contains("-"))        //neg roger report
            {
                return s.Replace("-", " -");
            }
            if (s.Contains("+"))        //pos roger report
            {
                return s.Replace("+", " +");
            }
            if (s.Contains("73"))
            {
                return Spacify(s);
            }
            //grid
            if (s.Length == 4)
            {
                return s.Substring(0, 1) + " " + s.Substring(1, 1) + " " + s.Substring(2, 2);
            }
            else
            {
                return Spacify(s);
            }
        }

        private static string GridToUsState(string grid)
        {
            string state;
            return UsGridStateMap.TryGetState(grid, out state) ? state : null;
        }

        private int CallQueuePriorityCount(CallPriority p)
        {
            int count = 0;
            foreach (var entry in callDict)
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(entry.Key, callInProg)) continue;
                if (entry.Value.Priority == (int)p) count++;
            }
            return count;
        }

        private int SnapshotPriorityCount(CallPriority p, HashSet<string> visibleCalls)
        {
            if (visibleCalls == null) return CallQueuePriorityCount(p);
            int count = 0;
            foreach (var call in visibleCalls)
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(call, callInProg)) continue;
                EnqueueDecodeMessage d;
                if (callDict.TryGetValue(call, out d) && d.Priority == (int)p) count++;
            }
            return count;
        }

        private string PeekVisibleCall(out EnqueueDecodeMessage dmsg, HashSet<string> visibleCalls)
        {
            dmsg = null;
            foreach (string call in callQueue)
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(call, callInProg)) continue;
                if (visibleCalls != null && !visibleCalls.Contains(call)) continue;
                callDict.TryGetValue(call, out dmsg);
                return call;
            }
            return null;
        }

        private void StartStatusTimer()
        {
            statusTimer.Stop();
            statusTimer.Interval = 250;
            statusTimer.Start();
        }

        private void DeleteLotwCsv()
        {
            string pgmNameWsjtx = "WSJT-X";
            string pathWsjtx = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\{pgmNameWsjtx}";
            string pathFileNameExt = pathWsjtx + "\\" + "lotw-user-activity.csv";

            try
            {
                if (File.Exists(pathFileNameExt))
                {
                    File.Delete(pathFileNameExt);
                    DebugOutput($"{Time()} DeleteLotwCsv, deleted {pathFileNameExt}");
                }
            }
            catch (Exception)
            {
                DebugOutput($"{Time()} DeleteLotwCsv, unable to delete {pathFileNameExt}");
            }
        }

        private void CalcAvgTimeOffset(bool clear)
        {
            timeOffset = 0;

            if (timeOffsets.Count == 0) return;

            foreach (double offset in timeOffsets)
            {
                timeOffset += offset;
            }
            timeOffset /= timeOffsets.Count;

            DebugOutput($"{Time()} CalcAvgTimeOffset, timeOffset:{timeOffset:F2} clear:{clear}");
            if (clear) timeOffsets.Clear();
        }

        private void DiscardCall()
        {
            if (callInProg == discardCall && ((txMode == TxModes.LISTEN && !txEnabled) || txMode == TxModes.CALL_CQ))
            {
                DebugOutput($"{Time()} DiscardCall: in effect, discardCall:'{discardCall}' discardCallCycleCount:{discardCallCycleCount}");
                if (txMode == TxModes.LISTEN) Pause(true, false);
                if (!transmitting) expiredCall = discardCall;
                SetCallInProg(null);
                DebugOutput($"{spacer}callInProg:'{callInProg}' expiredCall:'{expiredCall}' txTimeout:{txTimeout} xmitCycleCount:{xmitCycleCount} timedOutCall:'{timedOutCall}'");
            }
            else
            {
                DebugOutput($"{Time()} DiscardCall: not in effect (was discardCall:'{discardCall}' discardCallCycleCount:{discardCallCycleCount})");
            }

            discardCall = null;
            discardCallCycleCount = 0;
            DebugOutput($"{spacer} now discardCall:'{discardCall}' discardCallCycleCount:{discardCallCycleCount}");
        }

        private void CancelDiscardCall()
        {
            DebugOutput($"{Time()} CancelDiscardCall: (was '{discardCall}' discardCallCycleCount:{discardCallCycleCount})");
            discardCall = null;
            discardCallCycleCount = 0;
            DebugOutput($"{spacer} now discardCall:'{discardCall}' discardCallCycleCount:{discardCallCycleCount}");
        }

        private void StartDiscardCall(string call)      //or reset discard call
        {
            if (txMode == TxModes.CALL_CQ) return;

            discardCall = call;
            discardCallCycleCount = 0;
            DebugOutput($"{Time()} StartDiscardCall: discardCall:'{discardCall}' discardCallCycleCount:{discardCallCycleCount}");
        }

        private void StartStatusTimer2(bool uncond)
        {
            if (uncond)
            {
                statusTimer2.Stop();
                statusTimer2.Start();       //long tx/rx period, restore previous status
            }
        }

        private void UpdateCallQueue(string call, EnqueueDecodeMessage dmsg)
        {
            if (!ctrl.cqOnlyRadioButton.Checked || dmsg.ToCall() == myCall) return;

            if (dmsg.ToCall() != myCall && dmsg.Quality < (int)EnqueueDecodeMessage.Qualities.MEDIUM)
            {
                RemoveCall(call);
                if (debugDetail) DebugOutput($"{Time()} UpdateCallQueue: removed call:'{call}' msg:{dmsg.Message} quality:{dmsg.Quality}");
            }
        }

        public WsjtxDiagData GetDiagnosticData()
        {
            try
            {
                var queueEntries = new List<CallQueueDiagEntry>();
                int pos = 0;
                foreach (string callsign in callQueue)
                {
                    pos++;
                    if (callDict.TryGetValue(callsign, out var dm))
                    {
                        queueEntries.Add(new CallQueueDiagEntry
                        {
                            Callsign            = callsign,
                            QueuePosition       = pos,
                            Country             = dm.Country ?? "",
                            Message             = dm.Message ?? "",
                            Snr                 = dm.Snr,
                            Category            = dm.Category.ToString(),
                            IsNewCountry        = dm.IsNewCountry,
                            IsNewCountryOnBand  = dm.IsNewCountryOnBand,
                            Distance            = dm.Distance,
                            Azimuth             = dm.Azimuth,
                        });
                    }
                }

                var decodeHistory = new List<DecodeHistoryDiagEntry>();
                foreach (var dm in _rawDecodeHistory)
                {
                    try
                    {
                        decodeHistory.Add(new DecodeHistoryDiagEntry
                        {
                            TimeUtc            = (dm.RxDate + dm.SinceMidnight).ToString("HH:mm:ss"),
                            Message            = dm.Message ?? "",
                            Mode               = dm.Mode ?? "",
                            Snr                = dm.Snr,
                            DeltaTime          = dm.DeltaTime,
                            DeltaFrequency     = dm.DeltaFrequency,
                            Country            = dm.Country ?? "",
                            Category           = dm.Category.ToString(),
                            IsNewCountry       = dm.IsNewCountry,
                            IsNewCountryOnBand = dm.IsNewCountryOnBand,
                            IsDx               = dm.IsDx,
                        });
                    }
                    catch { /* skip individual entry on error */ }
                }

                return new WsjtxDiagData
                {
                    MyCall          = myCall,
                    MyGrid          = myGrid,
                    Mode            = mode,
                    TxFirst         = txFirst,
                    Connected       = ConnectedToWsjtx(),
                    Connecting      = WsjtxConnecting(),
                    PgmName         = pgmName,
                    PgmVer          = pgmVer,
                    IpAddress       = ipAddress,
                    Port            = port,
                    Multicast       = multicast,
                    DiagLog         = diagLog,
                    UsePskReporter  = usePskReporter,
                    TxMode          = txMode,
                    CallInProg      = callInProg,
                    DialFrequency   = dialFrequency,
                    BandIdx         = bandIdx,
                    Bands           = bands.ToArray(),
                    CallQueueCount  = callQueue.Count,
                    LoggedCount     = logList.Count,
                    Tx1Count        = _tx1SnapshotRows.Count,
                    Tx2Count        = _tx2SnapshotRows.Count,
                    RawDecodeCount  = _rawDecodeHistory.Count,
                    CallQueueDetails = queueEntries,
                    DecodeHistory   = decodeHistory,
                };
            }
            catch
            {
                return new WsjtxDiagData();
            }
        }
    }

    public class WsjtxDiagData
    {
        public string MyCall;
        public string MyGrid;
        public string Mode   = "";
        public bool TxFirst;
        public bool Connected;
        public bool Connecting;
        public string PgmName;
        public string PgmVer;
        public System.Net.IPAddress IpAddress;
        public int Port;
        public bool Multicast;
        public bool DiagLog;
        public bool UsePskReporter;
        public WsjtxClient.TxModes TxMode;
        public string CallInProg;
        public ulong DialFrequency;
        public int? BandIdx;
        public int[] Bands = new int[0];
        public int CallQueueCount;
        public int LoggedCount;
        public int Tx1Count;
        public int Tx2Count;
        public int RawDecodeCount;
        public List<CallQueueDiagEntry>     CallQueueDetails = new List<CallQueueDiagEntry>();
        public List<DecodeHistoryDiagEntry> DecodeHistory    = new List<DecodeHistoryDiagEntry>();
    }

    public class CallQueueDiagEntry
    {
        public string Callsign;
        public int    QueuePosition;
        public string Country;
        public string Message;
        public int    Snr;
        public string Category;
        public bool   IsNewCountry;
        public bool   IsNewCountryOnBand;
        public int    Distance;
        public int    Azimuth;
    }

    public class DecodeHistoryDiagEntry
    {
        public string TimeUtc;
        public string Message;
        public string Mode;
        public int    Snr;
        public double DeltaTime;
        public int    DeltaFrequency;
        public string Country;
        public string Category;
        public bool   IsNewCountry;
        public bool   IsNewCountryOnBand;
        public bool   IsDx;
    }
}

