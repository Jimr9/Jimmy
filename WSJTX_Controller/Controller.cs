using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using WsjtxUdpLib;
using System.Net;
using System.Configuration;
using System.Threading;
using System.Media;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using System.Text.RegularExpressions;


namespace WSJTX_Controller
{
    public partial class Controller : Form
    {
        public WsjtxClient wsjtxClient;
        public OptionsDlg optionsDlg;
        public bool alwaysOnTop = false;
        public bool skipLevelPrompt = false;
        public bool advancedCallLayout = true;
        public bool advShowTx1 = true;
        public bool advShowTx2 = true;
        public bool advShowRaw = true;
        public bool rawShowCq = true;
        public bool rawShowDirected = true;
        public bool rawShowReports = true;
        public bool rawShowRR73 = false;
        public bool rawShow73 = false;
        public bool rawShowPota = true;
        public bool rawShowSota = true;
        public bool rawShowDx = true;
        public bool rawShowSnr = true;
        public bool rawShowGrid = true;
        public bool rawShowCountry = true;
        public bool rawShowState = true;
        public bool rawShowDistAz = false;
        public bool rawOnlyCallsigns = false;
        public bool rawOnlyUnworked = false;
        public bool rawOnlyRanked = false;
        public bool rawPriorityTags = false;
        public bool rawNewestFirst = false;
        public int rawMaxRows = 100;
        public int maxQueuedCallsBase = 5;
        public bool keepTransmitListDuringTx = false;
        public bool keepListPositionDuringRefresh = false;

        // Sound settings: enabled flags and file paths for each sound event
        // CallAdded/CallingMe/Logged enabled state is controlled by existing checkboxes
        public bool   soundsEnabled         = true;
        public string soundFile_CallAdded   = "blip.wav";
        public string soundFile_CallingMe   = "trumpet.wav";
        public string soundFile_Logged      = "echo.wav";
        public bool   soundEnabled_TxEnabled     = true;
        public string soundFile_TxEnabled        = "beepbeep.wav";
        public bool   soundEnabled_Disconnected  = true;
        public string soundFile_Disconnected     = "dive.wav";
        public bool   soundEnabled_NewDxcc        = false;
        public string soundFile_NewDxcc           = "";
        public bool   soundEnabled_NewDxccOnBand  = false;
        public string soundFile_NewDxccOnBand     = "";
        public bool   soundEnabled_AlwaysWanted   = false;
        public string soundFile_AlwaysWanted      = "";
        public bool   soundEnabled_DirectedCq     = false;
        public string soundFile_DirectedCq        = "";
        public bool   soundEnabled_Pota           = false;
        public string soundFile_Pota              = "";
        public bool   soundEnabled_Sota           = false;
        public string soundFile_Sota              = "";
        public bool   soundEnabled_WantedAnywhere = false;
        public string soundFile_WantedAnywhere    = "";
        public bool   soundEnabled_OppositePeriod = false;
        public string soundFile_OppositePeriod    = "";
        public bool   soundEnabled_AwardNeeded    = false;
        public string soundFile_AwardNeeded       = "";

        // Feature flags
        public bool   wantedCallAnywhereEnabled   = true;

        // Logbook credentials (loaded from ini; set from Options > Lookup / Data tab)
        public string qrzLogbookApiKey = "";
        public string lotwLogbookUser  = "";
        public string lotwLogbookPass  = "";

        // Logbook upload settings (loaded from ini; set from Options > Lookup / Data tab).
        // QRZ upload reuses qrzLogbookApiKey above -- same key QRZ uses for download.
        // Club Log upload needs its own per-user credentials (Application Password, not
        // the normal Club Log website login), separate from the app-wide Club Log key
        // used for read-only country data (see ClubLogAppKey.cs).
        public bool   qrzUploadEnabled       = false;
        public bool   qrzUploadRealtime      = false;
        public bool   clubLogUploadEnabled   = false;
        public bool   clubLogUploadRealtime  = false;
        public string clubLogUploadEmail     = "";
        public string clubLogUploadPassword  = "";
        public string clubLogUploadCallsign  = "";
        public bool   lotwUploadEnabled      = true;   // matches today's Alt+U behavior by default
        private LogbookWindow _logbookWindow;

        // Ids of the Rule Definitions checked for live FT8 tagging in the Logbook window's
        // Still Need tab, persisted so tagging survives across sessions and works even before
        // the Logbook window has been opened. Empty = none actively tracked. Several awards
        // can be tracked at once (see RefreshStillNeedCache()).
        public HashSet<string> activeAwardRuleIds = new HashSet<string>();

        // Lookup / Data settings
        public LookupManager    lookupManager;
        public bool             useLookupData           = false;
        public bool             qrzEnabled              = false;
        public string           qrzUsername             = "";
        public string           qrzPassword             = "";
        public int              qrzCacheDays            = 7;
        public QrzLookupPolicy  qrzLookupPolicy         = QrzLookupPolicy.Disabled;
        public int              qrzMinIntervalSeconds   = 10;
        public bool             lotwEnabled             = false;
        public bool             lotwBoostEnabled        = false;
        public int              lotwRefreshDays         = 30;
        // No clubLogEnabled/clubLogApiKey fields: Club Log country data is
        // automatic Jimmy infrastructure, not a user-facing toggle or a
        // per-user credential -- the key is Jimmy's application key
        // (ClubLogAppKey.Resolve()) and downloads happen unconditionally,
        // subject only to the refresh interval below. See RuleUniverse.cs.
        public int              clubLogRefreshDays      = 30;

        private bool formLoaded = false;
        private bool openOptionsOnUdpTab = false;
        private HelpDlg helpDlg = null;
        private Control _helpReturnFocus = null;
        private IniFile iniFile = null;
        public HotkeyConfig hotkeyConfig;
        private int minSkipCount = 1;
        private const int maxSkipCount = 20;
        private const string separateBySpaces = "(separate by spaces)";
        public string friendlyName = "";
        private MouseEventArgs mouseEventArgs;
        private int listBoxClickCount;
        private bool ignoreDirectedChange = false;
        private string helpSuffix = " Help";
        private bool ignoreExceptChange = false;
        private bool _suppressIntentSync = false;

        private System.Windows.Forms.Timer mainLoopTimer;

        public System.Windows.Forms.Timer statusMsgTimer;
        public System.Windows.Forms.Timer initialConnFaultTimer;
        public System.Windows.Forms.Timer debugHighlightTimer;
        public System.Windows.Forms.Timer guideTimer;
        public System.Windows.Forms.Timer callListBoxClickTimer;
        public System.Windows.Forms.Timer helpTimer;

        private string nl = Environment.NewLine;
        private static string alphaOnly = "[^A-Za-z]";         //match if any numeric
        private static string numericOnly = "[^0-9]";          //match if any alpha


        public Controller()
        {
            InitializeComponent();
            KeyPreview = true;

            //timers
            mainLoopTimer = new System.Windows.Forms.Timer();
            mainLoopTimer.Tick += new System.EventHandler(mainLoopTimer_Tick);
            statusMsgTimer = new System.Windows.Forms.Timer();
            statusMsgTimer.Interval = 5000;
            statusMsgTimer.Tick += new System.EventHandler(statusMsgTimer_Tick);
            initialConnFaultTimer = new System.Windows.Forms.Timer();
            initialConnFaultTimer.Tick += new System.EventHandler(initialConnFaultTimer_Tick);
            debugHighlightTimer = new System.Windows.Forms.Timer();
            debugHighlightTimer.Tick += new System.EventHandler(debugHighlightTimer_Tick);
            guideTimer = new System.Windows.Forms.Timer();
            guideTimer.Interval = 20;
            guideTimer.Tick += new System.EventHandler(guideTimer_Tick);
            callListBoxClickTimer = new System.Windows.Forms.Timer();
            callListBoxClickTimer.Interval = 250;
            callListBoxClickTimer.Tick += new System.EventHandler(callListBoxClickTimer_Tick);
            helpTimer = new System.Windows.Forms.Timer();
            helpTimer.Interval = 20;
            helpTimer.Tick += new System.EventHandler(helpTimer_Tick);
        }

#if DEBUG
        //project type must be Console application for this to work

        [DllImport("Kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
#endif

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private bool IsJimmyForegrounded() => GetForegroundWindow() == this.Handle;
        private void Form_Load(object sender, EventArgs e)
        {
            //use .ini file for settings (avoid .Net config file mess)
            string pgmName = Assembly.GetExecutingAssembly().GetName().Name.ToString();
            string path = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\{pgmName}";
            string pathFileNameExt = path + "\\" + pgmName + ".ini";
            List<string> parsedCallWaitingRowOrder = null;
            hotkeyConfig = new HotkeyConfig();
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                iniFile = new IniFile(pathFileNameExt);
                hotkeyConfig.LoadFromIni(iniFile);
                // Parse optional call-waiting row order from INI (INI-only setting).
                // Stored in local variable and assigned to wsjtxClient after it's constructed.
                // Use ASCII comma and ignore invalid tokens/duplicates.
                try
                {
                    string orderStr = iniFile.Read("callWaitingRowOrder");
                    if (!string.IsNullOrWhiteSpace(orderStr))
                    {
                        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            "callp", "pri", "tag", "grid", "snr", "country", "distAz", "oe", "descr", "rankStr"
                        };

                        var parsed = new List<string>();
                        foreach (var tok in orderStr.Split(new char[] { (char)44 }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var f = tok.Trim();
                            if (f.Length == 0) continue;
                            if (!allowed.Contains(f)) continue; // ignore invalid names
                            if (parsed.Exists(s => string.Equals(s, f, StringComparison.OrdinalIgnoreCase))) continue; // ignore duplicates after first occurrence
                            parsed.Add(f);
                        }

                        if (parsed.Count > 0) parsedCallWaitingRowOrder = parsed;
                    }
                }
                catch
                {
                    // swallow parse errors and leave parsedCallWaitingRowOrder null
                }
            }
            catch
            {
                MessageBox.Show("Unable to create settings file: " + pathFileNameExt + $"{nl}Continuing with default settings...", friendlyName, MessageBoxButtons.OK);
            }

            string ipAddrStr = null;
            IPAddress ipAddress = null;
            int port = 0;
            bool multicast = true;
            bool overrideUdpDetect = false;
            bool debug = false;
            bool diagLog = false;
            WsjtxClient.TxModes txMode = WsjtxClient.TxModes.CALL_CQ;
            int offsetHiLimit = -1;
            int offsetLoLimit = -1;
            bool useRR73 = false;
            bool mode = true;
            string myContinent = null;
            bool newOnBand = true;
            bool cmdPrompts = true;
            bool usePskReporter = true;
            friendlyName = pgmName;

            //control defaults
            periodComboBox.SelectedIndex = 2;
            int rankMethodIdx = (int)WsjtxClient.RankMethods.MOST_RECENT;
            freqCheckBox.Checked = false;
            string rankOrderStr = null;
            string rankBeamStr = null;
            string categoryWeightsStr = null;
            string callingPrioritiesStr = null;
            string categoryDisabledStr = null;  // legacy migration only
            string wantedCallsStr = null;

            if (iniFile == null || !iniFile.KeyExists("firstRun"))     //.ini file not written yet, read properties (possibly set defaults)
            {
                debug = Properties.Settings.Default.debug;
                if (Properties.Settings.Default.windowPos != new Point(0, 0)) 
                    this.Location = Properties.Settings.Default.windowPos;
                if (Properties.Settings.Default.windowHt != 0) 
                    this.Height = Properties.Settings.Default.windowHt;
                ipAddrStr = Properties.Settings.Default.ipAddress;
                port = Properties.Settings.Default.port;
                multicast = Properties.Settings.Default.multicast;
                timeoutNumUpDown.Value = Properties.Settings.Default.timeout;
                directedTextBox.Text = Properties.Settings.Default.directeds;
                callDirCqCheckBox.Checked = Properties.Settings.Default.useDirected;
                mycallCheckBox.Checked = Properties.Settings.Default.playMyCall;
                loggedCheckBox.Checked = Properties.Settings.Default.playLogged;
                alertTextBox.Text = Properties.Settings.Default.alertDirecteds;
                replyDirCqCheckBox.Checked = Properties.Settings.Default.useAlertDirected;
                logEarlyCheckBox.Checked = Properties.Settings.Default.logEarly;
                alwaysOnTop = Properties.Settings.Default.alwaysOnTop;
                useRR73 = Properties.Settings.Default.useRR73;
                skipGridCheckBox.Checked = Properties.Settings.Default.skipGrid;
                diagLog = Properties.Settings.Default.diagLog;
                callAddedCheckBox.Checked = Properties.Settings.Default.playCallAdded;
                replyLocalCheckBox.Checked = Properties.Settings.Default.enableReplyLocal;
                replyDxCheckBox.Checked = Properties.Settings.Default.enableReplyDx;
                freqCheckBox.Checked = Properties.Settings.Default.bestOffset;
                replyRR73CheckBox.Checked = Properties.Settings.Default.replyRR73;
                newOnBand = Properties.Settings.Default.newOnBand;
                cmdPrompts = Properties.Settings.Default.cmdPrompts;
                bandComboBox.SelectedIndex = newOnBand ? 1 : 0;
                usePskReporter = Properties.Settings.Default.usePskReporter;
                optimizeCheckBox.Checked = true;
                callNonDirCqCheckBox.Checked = true;
                showUsStateCheckBox.Checked = true;

            }
            else        //read settings from .ini file (avoid .Net config file mess)
            {
                debug = iniFile.Read("debug") == "True";

                int x;
                int.TryParse(iniFile.Read("windowPosX"), out x);
                int y;
                int.TryParse(iniFile.Read("windowPosY"), out y);
                //check all screens, extended screen may not be present
                var screens = System.Windows.Forms.Screen.AllScreens;
                bool found = false;
                Rectangle matchedScreenBounds = Screen.PrimaryScreen.Bounds;
                for (int scnIdx = 0; scnIdx < screens.Length; scnIdx++)
                {
                    var screenBounds = screens[scnIdx].Bounds;
                    var centerPt = new Point(x + (this.Width / 2), y + (this.Height / 2));
                    if (screenBounds.Contains(centerPt))
                    {
                        found = true;       //found screen for window posn
                        matchedScreenBounds = screenBounds;
                        break;
                    }
                }
                if (!found)     //default window posn
                {
                    x = 0;
                    y = 0;
                    matchedScreenBounds = Screen.PrimaryScreen.Bounds;
                }
                this.Location = new Point(x, y);
                int i;
                int w;
                int.TryParse(iniFile.Read("windowWd"), out w);
                int.TryParse(iniFile.Read("windowHt"), out i);
                // Clamp to the matched screen and today's safe minimum, so a saved size
                // from a different/larger monitor can't leave the window unusable.
                if (w > 0) this.Width  = Math.Max(this.MinimumSize.Width,  Math.Min(w, matchedScreenBounds.Width));
                if (i > 0) this.Height = Math.Max(this.MinimumSize.Height, Math.Min(i, matchedScreenBounds.Height));

                if (iniFile.Read("windowState") == "Maximized")
                    this.WindowState = FormWindowState.Maximized;

                ipAddrStr = iniFile.Read("ipAddress");
                multicast = iniFile.Read("multicast") == "True";
                try
                {
                    ipAddress = IPAddress.Parse(ipAddrStr);
                    port = int.Parse(iniFile.Read("port"));
                }
                catch (Exception)
                {
                    ipAddrStr = Properties.Settings.Default.ipAddress;
                    port = Properties.Settings.Default.port;
                    multicast = Properties.Settings.Default.multicast;
                }

                int.TryParse(iniFile.Read("timeout"), out i);
                timeoutNumUpDown.Value = i;
                directedTextBox.Text = iniFile.Read("directeds");
                callDirCqCheckBox.Checked = iniFile.Read("useDirected") == "True";
                mycallCheckBox.Checked = iniFile.Read("playMyCall") != "False";
                loggedCheckBox.Checked = iniFile.Read("playLogged") != "False";
                callAddedCheckBox.Checked = iniFile.Read("playCallAdded") != "False";
                alertTextBox.Text = iniFile.Read("alertDirecteds");
                replyDirCqCheckBox.Checked = iniFile.Read("useAlertDirected") == "True";
                logEarlyCheckBox.Checked = iniFile.Read("logEarly") == "True";
                alwaysOnTop = iniFile.Read("alwaysOnTop") == "True";
                useRR73 = iniFile.Read("useRR73") == "True";
                skipGridCheckBox.Checked = iniFile.Read("skipGrid") == "True";
                replyDxCheckBox.Checked = iniFile.Read("enableReplyDx") != "False";     //default: true
                diagLog = iniFile.Read("diagLog") == "True";
                freqCheckBox.Checked = iniFile.Read("bestOffset") == "True";
                replyRR73CheckBox.Checked = iniFile.Read("replyRR73") == "True";
                cmdPrompts = iniFile.Read("cmdPrompts") != "False";     //default: true

                //start of .ini-file-only settings (not in .Net config)
                // mode (txMode startup) always defaults to LISTEN; not persisted across sessions
                if (iniFile.KeyExists("offsetHiLimit")) int.TryParse(iniFile.Read("offsetHiLimit"), out offsetHiLimit);
                if (iniFile.KeyExists("offsetLoLimit")) int.TryParse(iniFile.Read("offsetLoLimit"), out offsetLoLimit);
                replyLocalCheckBox.Checked = iniFile.Read("enableReplyLocal") != "False";     //default
                optimizeCheckBox.Checked = iniFile.Read("optimizeTx") == "True";
                exceptTextBox.Text = iniFile.Read("exceptCalls");
                callCqDxCheckBox.Checked = iniFile.Read("callCqDx") == "True";
                ignoreNonDxCheckBox.Checked = iniFile.Read("ignoreNonDx") == "True";
                callNonDirCqCheckBox.Checked = iniFile.Read("callNonDirCq") == "True";
                overrideUdpDetect = iniFile.Read("overrideUdpDetect") == "True";
                skipLevelPrompt = iniFile.Read("skipLevelPrompt") == "True";
                cqOnlyRadioButton.Checked = iniFile.Read("cqOnly") != "False";              //default: true
                newOnBand = iniFile.Read("newOnBand") != "False";      //default: true
                bandComboBox.SelectedIndex = newOnBand ? 1 : 0;
                if (iniFile.KeyExists("myContinent")) myContinent = iniFile.Read("myContinent");    //required to be null if not set
                if (iniFile.KeyExists("rankMethod")) int.TryParse(iniFile.Read("rankMethod"), out rankMethodIdx);
                if (iniFile.KeyExists("rankOrder")) rankOrderStr = iniFile.Read("rankOrder");
                if (iniFile.KeyExists("rankBeam")) rankBeamStr = iniFile.Read("rankBeam");
                if (iniFile.KeyExists("categoryWeights"))   categoryWeightsStr   = iniFile.Read("categoryWeights");
                if (iniFile.KeyExists("callingPriorities")) callingPrioritiesStr = iniFile.Read("callingPriorities");
                else if (iniFile.KeyExists("categoryDisabled")) categoryDisabledStr = iniFile.Read("categoryDisabled"); // migrate from old setting
                if (iniFile.KeyExists("wantedCalls"))       wantedCallsStr       = iniFile.Read("wantedCalls");
                if (iniFile.KeyExists("wantedCallAnywhereEnabled")) wantedCallAnywhereEnabled = iniFile.Read("wantedCallAnywhereEnabled") == "True";
                rawPriorityTags = iniFile.Read("rawPriorityTags") == "True";
                cqGridRadioButton.Checked = iniFile.Read("cqGrid") == "True";
                anyMsgRadioButton.Checked = iniFile.Read("anyMsg") == "True";
                if (iniFile.KeyExists("txPeriodIdx"))
                {
                    int.TryParse(iniFile.Read("txPeriodIdx"), out i);
                    periodComboBox.SelectedIndex = i;
                }
                usePskReporter = iniFile.Read("usePskReporter") != "False";              //default: true
                showUsStateCheckBox.Checked = iniFile.Read("showUsState") == "True";
                advancedCallLayout = iniFile.Read("advCallLayout") == "True";
                advShowTx1 = iniFile.Read("advShowTx1") != "False";
                advShowTx2 = iniFile.Read("advShowTx2") != "False";
                advShowRaw = iniFile.Read("advShowRaw") != "False";
                rawShowCq = iniFile.Read("rawShowCq") != "False";
                rawShowDirected = iniFile.Read("rawShowDirected") != "False";
                rawShowReports = iniFile.Read("rawShowReports") != "False";
                rawShowRR73 = iniFile.Read("rawShowRR73") == "True";
                rawShow73 = iniFile.Read("rawShow73") == "True";
                rawShowPota = iniFile.Read("rawShowPota") != "False";
                rawShowSota = iniFile.Read("rawShowSota") != "False";
                rawShowDx = iniFile.Read("rawShowDx") != "False";
                rawShowSnr = iniFile.Read("rawShowSnr") != "False";
                rawShowGrid = iniFile.Read("rawShowGrid") != "False";
                rawShowCountry = iniFile.Read("rawShowCountry") != "False";
                rawShowState = iniFile.Read("rawShowState") != "False";
                rawShowDistAz = iniFile.Read("rawShowDistAz") == "True";
                rawOnlyCallsigns = iniFile.Read("rawOnlyCallsigns") == "True";
                rawOnlyUnworked = iniFile.Read("rawOnlyUnworked") == "True";
                rawOnlyRanked = iniFile.Read("rawOnlyRanked") == "True";
                rawNewestFirst = iniFile.Read("rawNewestFirst") == "True";
                int rawMax;
                if (iniFile.KeyExists("rawMaxRows") && int.TryParse(iniFile.Read("rawMaxRows"), out rawMax) && rawMax >= 10 && rawMax <= 5000)
                    rawMaxRows = rawMax;
                int maxQueued;
                if (iniFile.KeyExists("maxQueuedCalls") && int.TryParse(iniFile.Read("maxQueuedCalls"), out maxQueued) && maxQueued >= 4 && maxQueued <= 100)
                    maxQueuedCallsBase = maxQueued;
                keepTransmitListDuringTx = iniFile.Read("keepTransmitListDuringTx") == "True";
                keepListPositionDuringRefresh = iniFile.Read("keepListPositionDuringRefresh") == "True";

                // Sound settings: migrate old enabled keys for backward compat
                // Enabled state for CallAdded/CallingMe/Logged already read above from playCallAdded/playMyCall/playLogged
                if (iniFile.KeyExists("soundFile_CallAdded"))  soundFile_CallAdded  = iniFile.Read("soundFile_CallAdded");
                if (iniFile.KeyExists("soundFile_CallingMe"))  soundFile_CallingMe  = iniFile.Read("soundFile_CallingMe");
                if (iniFile.KeyExists("soundFile_Logged"))     soundFile_Logged     = iniFile.Read("soundFile_Logged");
                if (iniFile.KeyExists("soundEnabled_TxEnabled"))    soundEnabled_TxEnabled    = iniFile.Read("soundEnabled_TxEnabled") != "False";
                if (iniFile.KeyExists("soundFile_TxEnabled"))       soundFile_TxEnabled       = iniFile.Read("soundFile_TxEnabled");
                if (iniFile.KeyExists("soundEnabled_Disconnected")) soundEnabled_Disconnected = iniFile.Read("soundEnabled_Disconnected") != "False";
                if (iniFile.KeyExists("soundFile_Disconnected"))    soundFile_Disconnected    = iniFile.Read("soundFile_Disconnected");
                if (iniFile.KeyExists("soundEnabled_NewDxcc"))       soundEnabled_NewDxcc       = iniFile.Read("soundEnabled_NewDxcc") == "True";
                if (iniFile.KeyExists("soundFile_NewDxcc"))          soundFile_NewDxcc          = iniFile.Read("soundFile_NewDxcc");
                if (iniFile.KeyExists("soundEnabled_NewDxccOnBand")) soundEnabled_NewDxccOnBand = iniFile.Read("soundEnabled_NewDxccOnBand") == "True";
                if (iniFile.KeyExists("soundFile_NewDxccOnBand"))    soundFile_NewDxccOnBand    = iniFile.Read("soundFile_NewDxccOnBand");
                if (iniFile.KeyExists("soundEnabled_AlwaysWanted"))  soundEnabled_AlwaysWanted  = iniFile.Read("soundEnabled_AlwaysWanted") == "True";
                if (iniFile.KeyExists("soundFile_AlwaysWanted"))     soundFile_AlwaysWanted     = iniFile.Read("soundFile_AlwaysWanted");
                if (iniFile.KeyExists("soundEnabled_DirectedCq"))    soundEnabled_DirectedCq    = iniFile.Read("soundEnabled_DirectedCq") == "True";
                if (iniFile.KeyExists("soundFile_DirectedCq"))       soundFile_DirectedCq       = iniFile.Read("soundFile_DirectedCq");
                if (iniFile.KeyExists("soundEnabled_Pota"))          soundEnabled_Pota          = iniFile.Read("soundEnabled_Pota") == "True";
                if (iniFile.KeyExists("soundFile_Pota"))             soundFile_Pota             = iniFile.Read("soundFile_Pota");
                if (iniFile.KeyExists("soundEnabled_Sota"))           soundEnabled_Sota           = iniFile.Read("soundEnabled_Sota") == "True";
                if (iniFile.KeyExists("soundFile_Sota"))              soundFile_Sota              = iniFile.Read("soundFile_Sota");
                if (iniFile.KeyExists("soundEnabled_WantedAnywhere")) soundEnabled_WantedAnywhere = iniFile.Read("soundEnabled_WantedAnywhere") == "True";
                if (iniFile.KeyExists("soundFile_WantedAnywhere"))    soundFile_WantedAnywhere    = iniFile.Read("soundFile_WantedAnywhere");
                if (iniFile.KeyExists("soundEnabled_OppositePeriod")) soundEnabled_OppositePeriod = iniFile.Read("soundEnabled_OppositePeriod") == "True";
                if (iniFile.KeyExists("soundFile_OppositePeriod"))    soundFile_OppositePeriod    = iniFile.Read("soundFile_OppositePeriod");
                if (iniFile.KeyExists("soundEnabled_AwardNeeded"))    soundEnabled_AwardNeeded    = iniFile.Read("soundEnabled_AwardNeeded") == "True";
                if (iniFile.KeyExists("soundFile_AwardNeeded"))       soundFile_AwardNeeded       = iniFile.Read("soundFile_AwardNeeded");
                if (iniFile.KeyExists("soundsEnabled"))               soundsEnabled               = iniFile.Read("soundsEnabled") != "False";

                // Lookup / Data settings
                if (iniFile.KeyExists("useLookupData"))      useLookupData      = iniFile.Read("useLookupData") == "True";
                if (iniFile.KeyExists("qrzEnabled"))         qrzEnabled         = iniFile.Read("qrzEnabled")    == "True";
                if (iniFile.KeyExists("qrzUsername"))        qrzUsername        = iniFile.Read("qrzUsername");
                if (iniFile.KeyExists("qrzPassword"))        qrzPassword        = iniFile.Read("qrzPassword");
                int qrzcd; if (iniFile.KeyExists("qrzCacheDays")    && int.TryParse(iniFile.Read("qrzCacheDays"),    out qrzcd)   && qrzcd   >= 1) qrzCacheDays    = qrzcd;
                int qrzpol; if (iniFile.KeyExists("qrzLookupPolicy") && int.TryParse(iniFile.Read("qrzLookupPolicy"), out qrzpol)) qrzLookupPolicy = (QrzLookupPolicy)qrzpol;
                int qrzint; if (iniFile.KeyExists("qrzMinIntervalSeconds") && int.TryParse(iniFile.Read("qrzMinIntervalSeconds"), out qrzint) && qrzint >= 5) qrzMinIntervalSeconds = qrzint;
                if (iniFile.KeyExists("lotwEnabled"))        lotwEnabled        = iniFile.Read("lotwEnabled")    == "True";
                if (iniFile.KeyExists("lotwBoostEnabled"))   lotwBoostEnabled   = iniFile.Read("lotwBoostEnabled") == "True";
                int lotwd; if (iniFile.KeyExists("lotwRefreshDays") && int.TryParse(iniFile.Read("lotwRefreshDays"), out lotwd)   && lotwd   >= 1) lotwRefreshDays  = lotwd;
                int clgd; if (iniFile.KeyExists("clubLogRefreshDays") && int.TryParse(iniFile.Read("clubLogRefreshDays"), out clgd) && clgd >= 1) clubLogRefreshDays = clgd;
                if (iniFile.KeyExists("qrzLogbookApiKey")) qrzLogbookApiKey = iniFile.Read("qrzLogbookApiKey") ?? "";
                if (iniFile.KeyExists("lotwLogbookUser"))  lotwLogbookUser  = iniFile.Read("lotwLogbookUser")  ?? "";
                if (iniFile.KeyExists("lotwLogbookPass"))  lotwLogbookPass  = iniFile.Read("lotwLogbookPass")  ?? "";
                if (iniFile.KeyExists("qrzUploadEnabled"))      qrzUploadEnabled      = iniFile.Read("qrzUploadEnabled")      == "True";
                if (iniFile.KeyExists("qrzUploadRealtime"))     qrzUploadRealtime     = iniFile.Read("qrzUploadRealtime")     == "True";
                if (iniFile.KeyExists("clubLogUploadEnabled"))  clubLogUploadEnabled  = iniFile.Read("clubLogUploadEnabled")  == "True";
                if (iniFile.KeyExists("clubLogUploadRealtime")) clubLogUploadRealtime = iniFile.Read("clubLogUploadRealtime") == "True";
                if (iniFile.KeyExists("clubLogUploadEmail"))    clubLogUploadEmail    = iniFile.Read("clubLogUploadEmail")    ?? "";
                if (iniFile.KeyExists("clubLogUploadPassword")) clubLogUploadPassword = iniFile.Read("clubLogUploadPassword") ?? "";
                if (iniFile.KeyExists("clubLogUploadCallsign")) clubLogUploadCallsign = iniFile.Read("clubLogUploadCallsign") ?? "";
                if (iniFile.KeyExists("lotwUploadEnabled"))     lotwUploadEnabled     = iniFile.Read("lotwUploadEnabled")     == "True";
                if (iniFile.KeyExists("activeAwardRuleIds")) activeAwardRuleIds = ParseActiveAwardRuleIds(iniFile.Read("activeAwardRuleIds"));
                else if (iniFile.KeyExists("stillNeedLiveTagRuleId"))
                {
                    // Migrate the old single-rule setting the first time this INI is loaded
                    // under the new multi-award system.
                    string oldId = iniFile.Read("stillNeedLiveTagRuleId");
                    if (!string.IsNullOrWhiteSpace(oldId)) activeAwardRuleIds = new HashSet<string> { oldId };
                }
            }

            txMode = mode ? WsjtxClient.TxModes.LISTEN : WsjtxClient.TxModes.CALL_CQ;

            if (directedTextBox.Text == "") callDirCqCheckBox.Checked = false;
            directedTextBox.Enabled = callDirCqCheckBox.Checked;
            if (!directedTextBox.Enabled && directedTextBox.Text == "")
            {
                directedTextBox.Text = separateBySpaces;
            }

            if (alertTextBox.Text == "") replyDirCqCheckBox.Checked = false;
            alertTextBox.Enabled = replyDirCqCheckBox.Checked;
            if (!alertTextBox.Enabled && alertTextBox.Text == "")
            {
                alertTextBox.Text = separateBySpaces;
            }

            if (exceptTextBox.Text == "")
            {
                exceptTextBox.Text = separateBySpaces;
                exceptTextBox.ForeColor = Color.Gray;
            }

            UpdateTxLabel();

            callCqDxCheckBox_CheckedChanged(null, null);
            callNonDirCqCheckBox_CheckedChanged(null, null);
            directedTextBox_Leave(null, null);
            if (!cqOnlyRadioButton.Checked && !cqGridRadioButton.Checked && !anyMsgRadioButton.Checked) cqOnlyRadioButton.Checked = true;
            UpdateCqNewOnBand();

#if DEBUG
            AllocConsole();

            if (!debug)
            {
                ShowWindow(GetConsoleWindow(), 0);
            }
#endif

            //start the UDP message server
            wsjtxClient = new WsjtxClient(this, IPAddress.Parse(ipAddrStr), port, multicast, overrideUdpDetect, debug, diagLog, txMode);
            if (parsedCallWaitingRowOrder != null)
            {
                wsjtxClient.callWaitingRowOrderFields = parsedCallWaitingRowOrder;
            }
            if (iniFile != null)
            {
                int.TryParse(iniFile.Read("txOddOffset"), out int cachedOdd);
                int.TryParse(iniFile.Read("txEvenOffset"), out int cachedEven);
                if (cachedOdd > 0) wsjtxClient.cachedOddOffset = cachedOdd;
                if (cachedEven > 0) wsjtxClient.cachedEvenOffset = cachedEven;
            }
            wsjtxClient.myContinent = myContinent;
            if (myContinent != null) replyLocalCheckBox.Text = myContinent;
            if (offsetLoLimit > 0) wsjtxClient.offsetLoLimit = offsetLoLimit;
            if (offsetHiLimit > 0) wsjtxClient.offsetHiLimit = offsetHiLimit;
            wsjtxClient.useRR73 = useRR73;
            wsjtxClient.ApplySortOrder(
                ParseRankOrder(rankOrderStr, rankMethodIdx),
                ParseRankBeam(rankBeamStr, rankMethodIdx));
            wsjtxClient.ApplyCategoryWeights(ParseCategoryWeights(categoryWeightsStr));
            wsjtxClient.ApplyCallingPriorities(ParseCallingPriorities(callingPrioritiesStr, categoryDisabledStr));
            // Migration (Phase 1): if this config pre-dates Call Filters and the operator had
            // replyDxCheckBox or replyLocalCheckBox enabled, ordinary CQ calls were being admitted.
            // Add DEFAULT to callingEnabled so that admission behaviour is preserved after upgrade.
            if (!wsjtxClient.callingEnabled.Contains(WsjtxClient.CallCategory.DEFAULT)
                && (replyDxCheckBox.Checked || replyLocalCheckBox.Checked))
            {
                wsjtxClient.callingEnabled.Add(WsjtxClient.CallCategory.DEFAULT);
                iniFile.Write("callingPriorities",
                    FormatCallingPriorities(wsjtxClient.callingEnabled));
            }
            // Migration: a config saved before STILL_NEEDED existed won't have it in its
            // callingPriorities list, so Still Need live tagging would be silently disabled
            // for existing installs (ParseCallingPriorities only fills in the new default for
            // configs with no saved list at all). Add it once, same tier as WAS/DXCC/ZONE.
            if (!string.IsNullOrWhiteSpace(callingPrioritiesStr)
                && !wsjtxClient.callingEnabled.Contains(WsjtxClient.CallCategory.STILL_NEEDED))
            {
                wsjtxClient.callingEnabled.Add(WsjtxClient.CallCategory.STILL_NEEDED);
                iniFile.Write("callingPriorities",
                    FormatCallingPriorities(wsjtxClient.callingEnabled));
            }
            wsjtxClient.ApplyWantedCalls(ParseWantedCalls(wantedCallsStr));
            wsjtxClient.rawPriorityTags = rawPriorityTags;
            wsjtxClient.cmdPrompts = cmdPrompts;
            wsjtxClient.usePskReporter = usePskReporter;

            lookupManager = new LookupManager();
            lookupManager.Initialize(
                useLookupData,
                qrzEnabled, qrzUsername, qrzPassword, qrzCacheDays,
                lotwEnabled, lotwRefreshDays,
                ClubLogAppKey.Resolve(), clubLogRefreshDays,
                qrzLookupPolicy, qrzMinIntervalSeconds);
            wsjtxClient.lookupManager     = lookupManager;
            wsjtxClient.lotwBoostEnabled  = lotwBoostEnabled;
            LoadHrcCache();
            lookupManager.OnLookupCompleted = () =>
                BeginInvoke(new Action(() => wsjtxClient.RefreshQueueDisplay()));
            lookupManager.StartBackgroundRefreshIfNeeded(lotwRefreshDays, clubLogRefreshDays);

            // Loads every .ini file from the RuleDefinitions folder (awards engine).
            // A bad or missing folder must never block startup.
            RuleLibrary.ClubLog = lookupManager.ClubLog;
            try { RuleLibrary.Load(); } catch { }
            RefreshStillNeedCache();   // must run after RuleLibrary.Load() so the saved selection resolves


            mainLoopTimer.Interval = 10;           //actual is 11-12 msec (due to OS limitations)
            mainLoopTimer.Start();

            wsjtxClient.UpdateModeVisible();

            TopMost = alwaysOnTop;

            UpdateDebug();

            wsjtxClient.UpdateModeSelection();
            SyncCqIntentFromMode();     // force-sync after wsjtxClient is assigned

            // Logbook button — added below sortOrderButton at y=309
            var logbookButton = new System.Windows.Forms.Button
            {
                Text           = "Logbook",
                AccessibleName = "Open Ham Radio Center Logbook",
                Location       = new System.Drawing.Point(10, 339),
                Size           = new System.Drawing.Size(130, 25),
                TabIndex       = 50,
            };
            logbookButton.Click += (s2, e2) => OpenLogbookWindow();
            this.Controls.Add(logbookButton);
            logbookButton.BringToFront();

            formLoaded = true;
            ApplyAdvancedLayout();

            if (!this.Focused)
            {
                this.Focus();
            }

            if (!statusText.Focused)
            {
                statusText.Focus();
            }
            SendKeys.Send("{UP}");
        }

        private void Controller_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (iniFile != null)
            {
                iniFile.Write("debug", wsjtxClient.debug.ToString());
                // Save the Normal-state bounds even if currently maximized/minimized, so
                // restoring later doesn't land on maximized dimensions.
                Rectangle normalBounds = this.WindowState == FormWindowState.Normal
                    ? new Rectangle(this.Location, this.Size)
                    : this.RestoreBounds;
                iniFile.Write("windowPosX", normalBounds.X.ToString());
                iniFile.Write("windowPosY", normalBounds.Y.ToString());
                iniFile.Write("windowWd", normalBounds.Width.ToString());
                iniFile.Write("windowHt", normalBounds.Height.ToString());
                iniFile.Write("windowState", this.WindowState.ToString());
                if (wsjtxClient.ipAddress != null) iniFile.Write("ipAddress", wsjtxClient.ipAddress.ToString());   //string
                if (wsjtxClient.port != 0) iniFile.Write("port", wsjtxClient.port.ToString());
                iniFile.Write("multicast", wsjtxClient.multicast.ToString());
                iniFile.Write("timeout", ((int)timeoutNumUpDown.Value).ToString());
                iniFile.Write("useDirected", callDirCqCheckBox.Checked.ToString());
                if (directedTextBox.Text == separateBySpaces) directedTextBox.Clear();
                iniFile.Write("directeds", directedTextBox.Text.Trim());
                iniFile.Write("playMyCall", mycallCheckBox.Checked.ToString());
                iniFile.Write("playLogged", loggedCheckBox.Checked.ToString());
                iniFile.Write("playCallAdded", callAddedCheckBox.Checked.ToString());
                iniFile.Write("useAlertDirected", replyDirCqCheckBox.Checked.ToString());
                if (alertTextBox.Text == separateBySpaces) alertTextBox.Clear();
                iniFile.Write("alertDirecteds", alertTextBox.Text.Trim());
                iniFile.Write("logEarly", logEarlyCheckBox.Checked.ToString());
                iniFile.Write("alwaysOnTop", alwaysOnTop.ToString());
                iniFile.Write("useRR73", wsjtxClient.useRR73.ToString());
                iniFile.Write("skipGrid", skipGridCheckBox.Checked.ToString());
                iniFile.Write("firstRun", "False");
                iniFile.Write("enableReplyDx", replyDxCheckBox.Checked.ToString());
                iniFile.Write("enableReplyLocal", replyLocalCheckBox.Checked.ToString());
                iniFile.Write("diagLog", wsjtxClient.diagLog.ToString());
                // txMode startup is always LISTEN; not persisted across sessions
                iniFile.Write("bestOffset", freqCheckBox.Checked.ToString());
                iniFile.Write("optimizeTx", optimizeCheckBox.Checked.ToString());
                if (exceptTextBox.Text == separateBySpaces) exceptTextBox.Clear();
                iniFile.Write("exceptCalls", exceptTextBox.Text.Trim());
                iniFile.Write("callCqDx", callCqDxCheckBox.Checked.ToString());
                iniFile.Write("ignoreNonDx", ignoreNonDxCheckBox.Checked.ToString());
                iniFile.Write("callNonDirCq", callNonDirCqCheckBox.Checked.ToString());
                iniFile.Write("overrideUdpDetect", wsjtxClient.overrideUdpDetect.ToString());
                iniFile.Write("skipLevelPrompt", skipLevelPrompt.ToString());
                iniFile.Write("cqOnly", cqOnlyRadioButton.Checked.ToString());
                iniFile.Write("newOnBand", (bandComboBox.SelectedIndex == 1).ToString());
                iniFile.Write("myContinent", wsjtxClient.myContinent);
                iniFile.Write("rankMethod", wsjtxClient.rankMethodIdx.ToString());
                iniFile.Write("categoryWeights",   FormatCategoryWeights(wsjtxClient.categoryWeight));
                iniFile.Write("callingPriorities", FormatCallingPriorities(wsjtxClient.callingEnabled));
                iniFile.Write("wantedCalls",              FormatWantedCalls(wsjtxClient.wantedCalls));
                iniFile.Write("wantedCallAnywhereEnabled", wantedCallAnywhereEnabled.ToString());
                iniFile.Write("rawPriorityTags",          rawPriorityTags.ToString());
                iniFile.Write("replyRR73", replyRR73CheckBox.Checked.ToString());
                iniFile.Write("cqGrid", cqGridRadioButton.Checked.ToString());
                iniFile.Write("anyMsg", anyMsgRadioButton.Checked.ToString());
                iniFile.Write("txPeriodIdx", periodComboBox.SelectedIndex.ToString());
                iniFile.Write("cmdPrompts", wsjtxClient.cmdPrompts.ToString());
                iniFile.Write("usePskReporter", wsjtxClient.usePskReporter.ToString());
                iniFile.Write("showUsState", showUsStateCheckBox.Checked.ToString());
                iniFile.Write("advCallLayout", advancedCallLayout.ToString());
                iniFile.Write("advShowTx1", advShowTx1.ToString());
                iniFile.Write("advShowTx2", advShowTx2.ToString());
                iniFile.Write("advShowRaw", advShowRaw.ToString());
                iniFile.Write("rawShowCq", rawShowCq.ToString());
                iniFile.Write("rawShowDirected", rawShowDirected.ToString());
                iniFile.Write("rawShowReports", rawShowReports.ToString());
                iniFile.Write("rawShowRR73", rawShowRR73.ToString());
                iniFile.Write("rawShow73", rawShow73.ToString());
                iniFile.Write("rawShowPota", rawShowPota.ToString());
                iniFile.Write("rawShowSota", rawShowSota.ToString());
                iniFile.Write("rawShowDx", rawShowDx.ToString());
                iniFile.Write("rawShowSnr", rawShowSnr.ToString());
                iniFile.Write("rawShowGrid", rawShowGrid.ToString());
                iniFile.Write("rawShowCountry", rawShowCountry.ToString());
                iniFile.Write("rawShowState", rawShowState.ToString());
                iniFile.Write("rawShowDistAz", rawShowDistAz.ToString());
                iniFile.Write("rawOnlyCallsigns", rawOnlyCallsigns.ToString());
                iniFile.Write("rawOnlyUnworked", rawOnlyUnworked.ToString());
                iniFile.Write("rawOnlyRanked", rawOnlyRanked.ToString());
                iniFile.Write("rawPriorityTags", rawPriorityTags.ToString());
                iniFile.Write("rawNewestFirst", rawNewestFirst.ToString());
                iniFile.Write("rawMaxRows", rawMaxRows.ToString());
                iniFile.Write("maxQueuedCalls", maxQueuedCallsBase.ToString());
                iniFile.Write("keepTransmitListDuringTx", keepTransmitListDuringTx.ToString());
                iniFile.Write("keepListPositionDuringRefresh", keepListPositionDuringRefresh.ToString());
                // Sound settings
                iniFile.Write("soundFile_CallAdded",        soundFile_CallAdded   ?? "");
                iniFile.Write("soundFile_CallingMe",        soundFile_CallingMe   ?? "");
                iniFile.Write("soundFile_Logged",           soundFile_Logged      ?? "");
                iniFile.Write("soundEnabled_TxEnabled",     soundEnabled_TxEnabled.ToString());
                iniFile.Write("soundFile_TxEnabled",        soundFile_TxEnabled   ?? "");
                iniFile.Write("soundEnabled_Disconnected",  soundEnabled_Disconnected.ToString());
                iniFile.Write("soundFile_Disconnected",     soundFile_Disconnected ?? "");
                iniFile.Write("soundEnabled_NewDxcc",       soundEnabled_NewDxcc.ToString());
                iniFile.Write("soundFile_NewDxcc",          soundFile_NewDxcc      ?? "");
                iniFile.Write("soundEnabled_NewDxccOnBand", soundEnabled_NewDxccOnBand.ToString());
                iniFile.Write("soundFile_NewDxccOnBand",    soundFile_NewDxccOnBand ?? "");
                iniFile.Write("soundEnabled_AlwaysWanted",  soundEnabled_AlwaysWanted.ToString());
                iniFile.Write("soundFile_AlwaysWanted",     soundFile_AlwaysWanted  ?? "");
                iniFile.Write("soundEnabled_DirectedCq",    soundEnabled_DirectedCq.ToString());
                iniFile.Write("soundFile_DirectedCq",       soundFile_DirectedCq    ?? "");
                iniFile.Write("soundEnabled_Pota",          soundEnabled_Pota.ToString());
                iniFile.Write("soundFile_Pota",             soundFile_Pota          ?? "");
                iniFile.Write("soundEnabled_Sota",           soundEnabled_Sota.ToString());
                iniFile.Write("soundFile_Sota",              soundFile_Sota              ?? "");
                iniFile.Write("soundEnabled_WantedAnywhere", soundEnabled_WantedAnywhere.ToString());
                iniFile.Write("soundFile_WantedAnywhere",    soundFile_WantedAnywhere    ?? "");
                iniFile.Write("soundEnabled_OppositePeriod", soundEnabled_OppositePeriod.ToString());
                iniFile.Write("soundFile_OppositePeriod",    soundFile_OppositePeriod    ?? "");
                iniFile.Write("soundEnabled_AwardNeeded",    soundEnabled_AwardNeeded.ToString());
                iniFile.Write("soundFile_AwardNeeded",       soundFile_AwardNeeded       ?? "");
                iniFile.Write("soundsEnabled",               soundsEnabled.ToString());
                iniFile.Write("txOddOffset",  wsjtxClient.cachedOddOffset.ToString());
                iniFile.Write("txEvenOffset", wsjtxClient.cachedEvenOffset.ToString());
                // Lookup / Data settings
                iniFile.Write("useLookupData",           useLookupData.ToString());
                iniFile.Write("qrzEnabled",              qrzEnabled.ToString());
                iniFile.Write("qrzUsername",             qrzUsername              ?? "");
                iniFile.Write("qrzPassword",             qrzPassword              ?? "");
                iniFile.Write("qrzCacheDays",            qrzCacheDays.ToString());
                iniFile.Write("qrzLookupPolicy",         ((int)qrzLookupPolicy).ToString());
                iniFile.Write("qrzMinIntervalSeconds",   qrzMinIntervalSeconds.ToString());
                iniFile.Write("lotwEnabled",             lotwEnabled.ToString());
                iniFile.Write("lotwBoostEnabled",        lotwBoostEnabled.ToString());
                iniFile.Write("lotwRefreshDays",         lotwRefreshDays.ToString());
                iniFile.Write("clubLogRefreshDays",      clubLogRefreshDays.ToString());
                iniFile.Write("qrzLogbookApiKey",        qrzLogbookApiKey         ?? "");
                iniFile.Write("lotwLogbookUser",         lotwLogbookUser          ?? "");
                iniFile.Write("lotwLogbookPass",         lotwLogbookPass          ?? "");
                iniFile.Write("qrzUploadEnabled",        qrzUploadEnabled.ToString());
                iniFile.Write("qrzUploadRealtime",       qrzUploadRealtime.ToString());
                iniFile.Write("clubLogUploadEnabled",    clubLogUploadEnabled.ToString());
                iniFile.Write("clubLogUploadRealtime",   clubLogUploadRealtime.ToString());
                iniFile.Write("clubLogUploadEmail",      clubLogUploadEmail       ?? "");
                iniFile.Write("clubLogUploadPassword",   clubLogUploadPassword    ?? "");
                iniFile.Write("clubLogUploadCallsign",   clubLogUploadCallsign    ?? "");
                iniFile.Write("lotwUploadEnabled",       lotwUploadEnabled.ToString());
                iniFile.Write("activeAwardRuleIds",  FormatActiveAwardRuleIds(activeAwardRuleIds));
                iniFile.DeleteKey("stillNeedLiveTagRuleId");
                // Phase 4: remove stale keys left by older versions.
                iniFile.DeleteKey("autoReplyNewCq");
                iniFile.DeleteKey("replyOnlyDxcc");
                iniFile.DeleteKey("categoryDisabled");
                // Club Log's key is an app key (ClubLogAppKey), never a per-user
                // setting -- remove any value a pre-cleanup version stored here.
                iniFile.DeleteKey("clubLogApiKey");
                // Club Log is now always-on infrastructure, not a user toggle --
                // remove the old per-user enabled flag left by earlier versions.
                iniFile.DeleteKey("clubLogEnabled");
                hotkeyConfig?.SaveToIni(iniFile);
            }

            CloseComm();
            optionsDlg?.Close();
            if (helpDlg != null) helpDlg.Close();
            _logbookWindow?.Close();
        }

        public void SaveHotkeyConfig()
        {
            if (iniFile != null) hotkeyConfig?.SaveToIni(iniFile);
        }

        public void CloseComm()
        {
            if (mainLoopTimer != null) mainLoopTimer.Stop();
            mainLoopTimer = null;
            statusMsgTimer.Stop();
            initialConnFaultTimer.Stop();
            wsjtxClient.Closing();
        }

        private void Controller_FormClosed(object sender, FormClosedEventArgs e)
        {

        }

#if DEBUG
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();
#endif
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!formLoaded) return false;

            if (keyData == hotkeyConfig[HotkeyAction.Help])
            {
                modeHelpLabel_Click(null, null);
                return true;
            }

            if (keyData == hotkeyConfig[HotkeyAction.Options])
            {
                optionsButton_Click(null, null);
                return true;
            }

            if (keyData == hotkeyConfig[HotkeyAction.UpdateCheck])
            {
                verLabel2_Click(null, null);
                return true;
            }


            if (!wsjtxClient.WsjtxConnecting()) return false;


            if (keyData == hotkeyConfig[HotkeyAction.ToggleMode])
            {
                return wsjtxClient.ToggleOperatingMode();
            }

            if (keyData == hotkeyConfig[HotkeyAction.BandUp])
            {
                return wsjtxClient.BandUp();
            }

            if (keyData == hotkeyConfig[HotkeyAction.BandDown])
            {
                return wsjtxClient.BandDown();
            }

            if (hotkeyConfig[HotkeyAction.Band160m] != Keys.None && keyData == hotkeyConfig[HotkeyAction.Band160m]) return wsjtxClient.SelectBand(0);
            if (hotkeyConfig[HotkeyAction.Band80m]  != Keys.None && keyData == hotkeyConfig[HotkeyAction.Band80m])  return wsjtxClient.SelectBand(1);
            if (hotkeyConfig[HotkeyAction.Band60m]  != Keys.None && keyData == hotkeyConfig[HotkeyAction.Band60m])  return wsjtxClient.SelectBand(2);
            if (hotkeyConfig[HotkeyAction.Band40m]  != Keys.None && keyData == hotkeyConfig[HotkeyAction.Band40m])  return wsjtxClient.SelectBand(3);
            if (hotkeyConfig[HotkeyAction.Band30m]  != Keys.None && keyData == hotkeyConfig[HotkeyAction.Band30m])  return wsjtxClient.SelectBand(4);
            if (hotkeyConfig[HotkeyAction.Band20m]  != Keys.None && keyData == hotkeyConfig[HotkeyAction.Band20m])  return wsjtxClient.SelectBand(5);
            if (hotkeyConfig[HotkeyAction.Band17m]  != Keys.None && keyData == hotkeyConfig[HotkeyAction.Band17m])  return wsjtxClient.SelectBand(6);
            if (hotkeyConfig[HotkeyAction.Band15m]  != Keys.None && keyData == hotkeyConfig[HotkeyAction.Band15m])  return wsjtxClient.SelectBand(7);
            if (hotkeyConfig[HotkeyAction.Band12m]  != Keys.None && keyData == hotkeyConfig[HotkeyAction.Band12m])  return wsjtxClient.SelectBand(8);
            if (hotkeyConfig[HotkeyAction.Band10m]  != Keys.None && keyData == hotkeyConfig[HotkeyAction.Band10m])  return wsjtxClient.SelectBand(9);
            if (hotkeyConfig[HotkeyAction.Band6m]   != Keys.None && keyData == hotkeyConfig[HotkeyAction.Band6m])   return wsjtxClient.SelectBand(10);


            if (!wsjtxClient.ConnectedToWsjtx()) return false;


            if (keyData == hotkeyConfig[HotkeyAction.PSKReporter])
            {
                return wsjtxClient.TogglePskReporter();
            }

            if (keyData == hotkeyConfig[HotkeyAction.Prompts])
            {
                return wsjtxClient.TogglePrompts();
            }

            if (keyData == hotkeyConfig[HotkeyAction.UploadLotw])
            {
                return wsjtxClient.UploadLotw();
            }

            if (keyData == hotkeyConfig[HotkeyAction.EnableTx])
            {
                return wsjtxClient.EnableMode();
            }

            if (keyData == hotkeyConfig[HotkeyAction.DeleteAllCalls])
            {
                return wsjtxClient.ClearCallQueue();
            }

            if (keyData == hotkeyConfig[HotkeyAction.HaltTx])
            {
                var focused = this.ActiveControl;
                if (wsjtxClient.ConnectedToWsjtx())
                {
                    wsjtxClient.RequeueAbortedCall();
                    wsjtxClient.CancelQso();
                    wsjtxClient.HaltAndDisableTx();
                    wsjtxClient.ResetTxToCq();
                    listenModeButton_Click(null, null);
                    ShowMsg("Tx halted", true);
                }
                BeginInvoke((Action)(() => RestoreFocus(focused)));
                return true;
            }

            if (keyData == hotkeyConfig[HotkeyAction.CallCqMode])
            {
                if (modeGroupBox.Visible && cqIntentListenButton.Checked)
                    ShowMsg("Listen mode selected; CQ not started.", true);
                else if (modeGroupBox.Visible)
                {
                    if (wsjtxClient.txMode == WsjtxClient.TxModes.LISTEN && wsjtxClient.AnalysisNeeded)
                    {
                        var confDlg = new ConfirmDlg();
                        confDlg.text = "Transmit slot has not been analyzed.\nRun recommended analysis now?";
                        confDlg.Owner = this;
                        confDlg.ShowDialog();
                        if (confDlg.DialogResult == DialogResult.Yes)
                            wsjtxClient.StartSlotAnalysis(true);
                        else
                        {
                            ShowMsg("Transmit slot analysis skipped.", true);
                            cqModeButton_Click(null, null);
                        }
                    }
                    else
                        cqModeButton_Click(null, null);
                }
                return true;
            }

            if (keyData == hotkeyConfig[HotkeyAction.AnalyzeSlot] && hotkeyConfig[HotkeyAction.AnalyzeSlot] != Keys.None)
            {
                wsjtxClient.StartSlotAnalysis(false);
                return true;
            }
            if (keyData == hotkeyConfig[HotkeyAction.LookupStation] && hotkeyConfig[HotkeyAction.LookupStation] != Keys.None)
            {
                LookupFocusedCall();
                return true;
            }

            if (keyData == hotkeyConfig[HotkeyAction.ListenMode])
            {
                listenModeButton_Click(null, null);
                return true;
            }

            if (keyData == hotkeyConfig[HotkeyAction.NextCall])
            {
                if (advTx1ListBox.Visible && advTx1ListBox.Focused)
                    wsjtxClient.NextBestPriorityCallFromTx1();
                else if (advTx2ListBox.Visible && advTx2ListBox.Focused)
                    wsjtxClient.NextBestPriorityCallFromTx2();
                else if (advRawListBox.Visible && advRawListBox.Focused)
                    wsjtxClient.NextBestPriorityCallFromRaw();
                else
                    wsjtxClient.NextBestPriorityCall();
                return true;
            }

            if (keyData == hotkeyConfig[HotkeyAction.ManualCall])
            {
                OpenManualCallDialog();
                return true;
            }

            if (keyData == hotkeyConfig[HotkeyAction.TxPeriod])
            {
                return wsjtxClient.ToggleTxFirst();
            }

            if (keyData == hotkeyConfig[HotkeyAction.HoldTimeout])
            {
                return wsjtxClient.ToggleHoldCheckBox();
            }

            if (keyData == hotkeyConfig[HotkeyAction.PowerSwr])
            {
                return wsjtxClient.ReportPowerSwr();
            }

            if (keyData == hotkeyConfig[HotkeyAction.TuneMode])
            {
                return wsjtxClient.ToggleTuningProcess();
            }

            if (keyData == hotkeyConfig[HotkeyAction.SortOrder])
            {
                OpenSortOrderEditor();
                return true;
            }

            if (keyData == hotkeyConfig[HotkeyAction.RowOrder])
            {
                OpenCallWaitingRowOrderEditor();
                return true;
            }

            if (keyData == hotkeyConfig[HotkeyAction.ResetWindowSize])
            {
                ResetWindowSize();
                return true;
            }

            if (keyData == hotkeyConfig[HotkeyAction.AudioUp])
            {
                return wsjtxClient.AudioLevel(true);
            }

            if (keyData == hotkeyConfig[HotkeyAction.AudioDown])
            {
                return wsjtxClient.AudioLevel(false);
            }

            return base.ProcessCmdKey(ref msg, keyData); // Let other keys be processed normally
        }

        private void mainLoopTimer_Tick(object sender, EventArgs e)
        {
            if (mainLoopTimer == null) return;
            wsjtxClient.UdpLoop();
        }

        private void statusMsgTimer_Tick(object sender, EventArgs e)
        {
            statusMsgTimer.Stop();
            wsjtxClient.UpdateCallInProg();
        }

        private void initialConnFaultTimer_Tick(object sender, EventArgs e)
        {
            if (IsJimmyForegrounded()) BringToFront();
            wsjtxClient.ConnectionDialog();
        }

        private void debugHighlightTimer_Tick(object sender, EventArgs e)
        {
            debugHighlightTimer.Stop();
            label17.ForeColor = Color.Black;
            label24.ForeColor = Color.Black;
            label25.ForeColor = Color.Black;
            label13.ForeColor = Color.Black;
            label10.ForeColor = Color.Black;
            label20.ForeColor = Color.Black;
            label21.ForeColor = Color.Black;
            label8.ForeColor = Color.Black;
            label19.ForeColor = Color.Black;
            label18.ForeColor = Color.Black;
            label12.ForeColor = Color.Black;
            label4.ForeColor = Color.Black;
            label14.ForeColor = Color.Black;
            label15.ForeColor = Color.Black;
            label16.ForeColor = Color.Black;
            label26.ForeColor = Color.Black;
            label27.ForeColor = Color.Black;
            label1.ForeColor = Color.Black;
            label2.ForeColor = Color.Black;
            label28.ForeColor = Color.Black;
            label11.ForeColor = Color.Black;
        }

        private void timeoutNumUpDown_ValueChanged(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            if (timeoutNumUpDown.Value < minSkipCount)
            {
                timeoutNumUpDown.Value = minSkipCount;
            }

            if (timeoutNumUpDown.Value > maxSkipCount)
            {
                timeoutNumUpDown.Value = maxSkipCount;
            }
            UpdateTxLabel();

            wsjtxClient.TxRepeatChanged();
            optionsDlg?.UpdateView();
        }

        private void UpdateTxLabel()
        {
            if (timeoutNumUpDown.Value == 1)
            {
                repeatLabel.Text = "Tx per msg";
            }
            else
            {
                repeatLabel.Text = "repeated Tx";
            }
        }

        private void replyDirCqCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            if (replyDirCqCheckBox.Checked) ignoreNonDxCheckBox.Checked = false;

            CheckManualSelection();

            alertTextBox.Enabled = replyDirCqCheckBox.Checked;
            if (replyDirCqCheckBox.Checked && alertTextBox.Text == separateBySpaces)
            {
                alertTextBox.Clear();
                alertTextBox.ForeColor = System.Drawing.Color.Black;
            }
            if (!replyDirCqCheckBox.Checked && alertTextBox.Text == "") alertTextBox.Text = separateBySpaces;

            optionsDlg?.UpdateView();
        }

        private void loggedCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (formLoaded && loggedCheckBox.Checked) wsjtxClient.PlaySoundEvent(true, soundFile_Logged);
        }

        private void mycallCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (formLoaded && mycallCheckBox.Checked) wsjtxClient.PlaySoundEvent(true, soundFile_CallingMe);
        }

        private void verLabel_DoubleClick(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            wsjtxClient.debug = !wsjtxClient.debug;
            UpdateDebug();
            if (formLoaded) wsjtxClient.DebugChanged();
        }

        private bool _inResize;
        private void Controller_Resize(object sender, EventArgs e)
        {
            if (!formLoaded || _inResize) return;
            _inResize = true;
            try { ApplyAdvancedLayout(); } finally { _inResize = false; }
        }

        private void ResetWindowSize()
        {
            this.WindowState = FormWindowState.Normal;
            this.Location = new Point(0, 0);
            this.Size = this.MinimumSize;   // natural size for the currently visible lists
            ApplyAdvancedLayout();

            statusText.Text = "Window size and position reset to default.";
            if (!statusText.Focused) statusText.Focus();
            BeginInvoke((Action)(() => SendKeys.Send("{UP}")));
        }

        // Natural (unstretched) bottom Y of the advanced lists block for however many of
        // TX1/TX2/Raw are shown, always derived from the fixed base sizes below -- never
        // from the lists' current (possibly window-stretched) positions/sizes. Sharing this
        // between ApplyAdvancedLayout and UpdateDebug keeps the per-configuration minimum
        // window height stable instead of ratcheting up every time the window grows.
        private int NaturalAdvancedListsBottom(bool showTx1, bool showTx2, bool showRaw, out int baseListH, out int baseRawH)
        {
            const int startY   = 376;   // first label Y (same as designer baseline)
            const int labelH   = 14;    // approx height of bold 8.25pt label
            const int labelGap = 2;     // gap between label bottom and list top
            const int groupGap = 6;     // gap between list bottom and next label

            int count = (showTx1 ? 1 : 0) + (showTx2 ? 1 : 0) + (showRaw ? 1 : 0);
            switch (count)
            {
                case 1:  baseListH = 200; baseRawH = 200; break;
                case 2:  baseListH = 120; baseRawH = 120; break;
                default: baseListH = 77;  baseRawH = 92;  break;   // 3 lists: original proportions
            }

            int bottom = startY;
            if (showTx1) bottom += labelH + labelGap + baseListH + groupGap;
            if (showTx2) bottom += labelH + labelGap + baseListH + groupGap;
            if (showRaw) bottom += labelH + labelGap + baseRawH + groupGap;
            return bottom;
        }

        private void UpdateDebug()
        {
            SuspendLayout();
            FormBorderStyle = FormBorderStyle.Sizable;
            label2.Visible = wsjtxClient.debug;
            label5.Visible = wsjtxClient.debug;
            label7.Visible = wsjtxClient.debug;
            label13.Visible = wsjtxClient.debug;
            label28.Visible = wsjtxClient.debug;
            if (wsjtxClient.debug)
            {
#if DEBUG
                AllocConsole();
                ShowWindow(GetConsoleWindow(), 5);
#endif
                WindowState = FormWindowState.Maximized;
                wsjtxClient.UpdateDebug();
            }
            else
            {
                bool anyAdvList = advancedCallLayout && (advShowTx1 || advShowTx2 || advShowRaw);
                int naturalHeight;
                if (anyAdvList)
                {
                    int bottom = NaturalAdvancedListsBottom(advShowTx1, advShowTx2, advShowRaw, out _, out _);
                    naturalHeight = bottom + 45;
                }
                else
                {
                    naturalHeight = sortOrderButton.Location.Y + sortOrderButton.Height + 45;
                }
                // 390 is the original Designer minimum height (today's default/safe floor);
                // never let the per-configuration natural height shrink below it.
                MinimumSize = new Size(MinimumSize.Width, Math.Max(390, naturalHeight));
#if DEBUG
                ShowWindow(GetConsoleWindow(), 0);
#endif
            }
            ResumeLayout();
        }

        private void skipGridCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            skipGridCheckBox.Text = "Skip grid (pending)";
            skipGridCheckBox.ForeColor = Color.DarkGreen;
            wsjtxClient.WsjtxSettingChanged();
        }

        private void useRR73CheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            useRR73CheckBox.Text = "Use RR73 (pending)";
            useRR73CheckBox.ForeColor = Color.DarkGreen;
            wsjtxClient.WsjtxSettingChanged();
        }

        public void WsjtxSettingConfirmed()
        {
            skipGridCheckBox.Text = "Skip grid msg";
            skipGridCheckBox.ForeColor = Color.Black;
            useRR73CheckBox.Text = "Use RR73 msg";
            useRR73CheckBox.ForeColor = Color.Black;
        }

        public void SetupDlgClosed() { }

        public void OpenUdpConfig()
        {
            initialConnFaultTimer.Stop();

            if (optionsDlg != null)
            {
                optionsDlg.SelectUdpTab();
                optionsDlg.BringToFront();
                return;
            }

            openOptionsOnUdpTab = true;
            guideTimer.Start();
        }

        // Loads the HRC database filter sets into WsjtxClient's in-memory caches.
        // Scoped to the radio's current band so tags reflect per-band award status.
        // Safe to call any time; silently skips if the DB is unavailable or empty.
        public void LoadHrcCache()
        {
            if (wsjtxClient == null) return;
            string band = wsjtxClient.CurrentBandStr;   // null when frequency is unknown
            try
            {
                using (var db = new LogbookDb())
                {
                    HashSet<string> neededStates;
                    HashSet<int>    unconfirmedDxcc;
                    HashSet<int>    neededZones;
                    db.LoadHrcCache(out neededStates, out unconfirmedDxcc, out neededZones, band);
                    wsjtxClient.hrcNeededStates    = neededStates;
                    wsjtxClient.hrcUnconfirmedDxcc = unconfirmedDxcc;
                    wsjtxClient.hrcNeededZones     = neededZones;
                }
            }
            catch { }
        }

        // Rebuilds WsjtxClient's live-tag cache from every Rule Definition currently checked
        // in activeAwardRuleIds, scoped to the radio's current band -- mirrors LoadHrcCache()'s
        // per-band semantics. Several awards can be tracked at once; each gets its own entry in
        // wsjtxClient.activeAwardTags. Only evaluates the RuleEngine here, at selection/refresh
        // time; decode-time matching is a plain HashSet lookup per active award. Safe to call any
        // time. Rules that can't be found, fail RuleEngine.SupportsLiveTag(def), or have no fixed
        // still-needed checklist (e.g. a Target=COUNT/LEVELS award) are simply left out.
        public void RefreshStillNeedCache()
        {
            if (wsjtxClient == null) return;

            var tags = new Dictionary<string, WsjtxClient.ActiveAwardTag>();
            foreach (string ruleId in activeAwardRuleIds)
            {
                var def = RuleLibrary.Definitions.FirstOrDefault(d => d.Enabled && d.Id == ruleId);
                if (!RuleEngine.SupportsLiveTag(def)) continue;

                try
                {
                    var result = RuleEngine.EvaluateBand(def, wsjtxClient.CurrentBandStr);
                    if (result.StillNeeded == null) continue;   // no fixed checklist to tag against

                    tags[ruleId] = new WsjtxClient.ActiveAwardTag
                    {
                        RuleId   = ruleId,
                        RuleName = def.Name,
                        GroupBy  = def.GroupBy,
                        Set      = new HashSet<string>(result.StillNeeded, StringComparer.OrdinalIgnoreCase),
                    };
                }
                catch { /* skip this rule, keep the others */ }
            }
            wsjtxClient.activeAwardTags = tags;
        }

        public void OpenLogbookWindow()
        {
            if (_logbookWindow != null && !_logbookWindow.IsDisposed)
            {
                _logbookWindow.Activate();
                return;
            }
            try
            {
                _logbookWindow = new LogbookWindow(iniFile, qrzLogbookApiKey, lotwLogbookUser, lotwLogbookPass,
                    clubLogUploadEmail, clubLogUploadPassword, clubLogUploadCallsign,
                    onImportComplete: () => BeginInvoke(new Action(() => { LoadHrcCache(); RefreshStillNeedCache(); })),
                    initialActiveAwardRuleIds: activeAwardRuleIds,
                    onActiveAwardRuleIdsChanged: (ruleId, isTracked) =>
                    {
                        if (isTracked) activeAwardRuleIds.Add(ruleId);
                        else activeAwardRuleIds.Remove(ruleId);
                        iniFile?.Write("activeAwardRuleIds", FormatActiveAwardRuleIds(activeAwardRuleIds));
                        RefreshStillNeedCache();
                    });
                _logbookWindow.FormClosed += (s, e) => _logbookWindow = null;
                _logbookWindow.Show();
            }
            catch (Exception ex)
            {
                _logbookWindow = null;
                MessageBox.Show(
                    ex.GetType().Name + ": " + ex.Message + "\r\n\r\n" + ex.StackTrace,
                    "Logbook Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        public void optionsButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;
            if (wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTuning();
            initialConnFaultTimer.Stop();

            if (optionsDlg != null)
            {
                optionsDlg.BringToFront();
                return;
            }

            guideTimer.Start();
        }

        private void guideTimer_Tick(object sender, EventArgs e)
        {
            guideTimer.Stop();
            optionsDlg = new OptionsDlg(wsjtxClient, this, openOptionsOnUdpTab);
            openOptionsOnUdpTab = false;
            optionsDlg.Show();
        }

        public void OptionsDlgClosed()
        {
            initialConnFaultTimer.Start();
            TopMost = alwaysOnTop;
            wsjtxClient.suspendComm   = false;
            wsjtxClient.lotwBoostEnabled = lotwBoostEnabled;
            wsjtxClient.RefreshResourceFileCache();
            optionsDlg = null;
            lookupManager?.Initialize(
                useLookupData,
                qrzEnabled, qrzUsername, qrzPassword, qrzCacheDays,
                lotwEnabled, lotwRefreshDays,
                ClubLogAppKey.Resolve(), clubLogRefreshDays,
                qrzLookupPolicy, qrzMinIntervalSeconds);
            wsjtxClient.SortCallsPublic();  // re-rank if LoTW boost changed
        }

        public void LookupFocusedCall()
        {
            string call = null;
            if (callListBox.Visible)
            {
                int idx = callListBox.SelectedIndex;
                if (idx >= 0)
                    call = wsjtxClient.GetCallAtIndex(wsjtxClient.MapNormalListIndex(idx));
            }
            else
            {
                // Advanced layout: try TX1 then TX2 selected index
                int idx = advTx1ListBox.SelectedIndex;
                if (idx >= 0) call = wsjtxClient.GetCallAtTx1Index(idx);
                if (call == null)
                {
                    idx = advTx2ListBox.SelectedIndex;
                    if (idx >= 0) call = wsjtxClient.GetCallAtTx2Index(idx);
                }
            }
            if (string.IsNullOrEmpty(call)) return;
            using (var dlg = new LookupInfoDlg(call, lookupManager))
            {
                dlg.ShowDialog(this);
                if (dlg.QrzLookupOccurred)
                    wsjtxClient?.DebugChanged();
            }
        }
        public void HelpClosed()
        {
            initialConnFaultTimer.Start();
            helpDlg = null;
            RestoreFocus(_helpReturnFocus);
            _helpReturnFocus = null;
        }

        public void ShowMsg(string text, bool sound)
        {
            //tempOnly
            /*if (sound)
            {
                SystemSounds.Beep.Play();
            }

            statusMsgTimer.Stop();
            msgTextBox.Text = text;
            statusMsgTimer.Start();*/
        }

        private void IncludeHelpLabel_Click(object sender, EventArgs e)
        {
            ShowHelp($"The 'Reply to new calls' section allows you to choose which messages from new callers you want to add to the 'Stations calling' list." +
                $"{nl}{nl}- Select 'CQ' if you want to reply only to CQ messages." +
                $"{nl}- Select 'CQ/grid' if you want to reply only to messages with grid information, allowing you to prioritize calls based on distance or azimuth." +
                $"{nl}- Select 'any' to reply to any message." +
                $"{nl}{nl}Note: The selections here don't affect replies to 'new countries' or 'new countries on band', which are enabled when 'Reply to new DXCC' is selected.");
        }

        private void IgnoreNonDxHelpLabel_Click(object sender, EventArgs e)
        {
            ShowHelp($"When calling 'CQ DX', select 'Ignore non-DX reply' to disable replying to calls to {MyCall()} from continents other than your continent." +
                $"{nl}{nl}This also disables replies to calls not directed to {MyCall()}.");
        }

        private void UseDirectedHelpLabel_Click(object sender, EventArgs e)
        {
            ShowHelp($"To send directed CQs:{nl}" +
                $"- Enter the code(s) for the directed CQs you want to transmit (2 to 4 letters each), separated by spaces." +
                $"{nl}- Don't enter 'DX' here." +
                $"{nl}{nl}The directed CQs will be used in random order." +
                $"{nl}{nl}Example: EU SA OC");
        }

        private void AlertDirectedHelpLabel_Click(object sender, EventArgs e)
        {
            string continent = wsjtxClient.myContinent == null ? "" : $" '{wsjtxClient.myContinent}'";
            ShowHelp($"Enter targets such as POTA SOTA DX." +
                $"{nl}{nl}Matching calls such as CQ POTA are added to the waiting list as Directed CQ calls." +
                $"{nl}{nl}If you enter 'DX', there will be no reply if the caller is on your continent." +
                $"{nl}{nl}There is no need to enter 'DX' or your continent{continent} if you have selected 'DX' and 'CQ/73' at 'Reply to new calls'." +
                $"{nl}{nl}(Note: 'CQ POTA' is an exception to the 'already worked' rule, these calls will allow a reply if you haven't already logged that call in the current mode/band in the current day).");
        }

        private void LogEarlyHelpLabel_Click(object sender, EventArgs e)
        {
            ShowHelp($"To maximize the chance of completed QSOs, consider 'early logging':" +
                $"{nl}{nl}" +
                $"The defining requirement for any QSO is the exchange of call signs and signal reports." +
                $"{nl}Once either party sends an 'RRR' message (and reports have been exchanged), those requirements have been met... a '73' is not necessary for logging the QSO." +
                $"{nl}{nl}Note that the QSO will continue after early logging, completing when 'RR73' or '73' is sent, or '73' is received." +
                $"{nl}{nl}New countries are an exception to early logging. In this case, logging is only after confirmation with a '73' or 'RR73'.");
        }

        private void verLabel2_Click(object sender, EventArgs e)
        {
            string command = "https://github.com/jimr9/Jimmy/releases/latest";
            System.Diagnostics.Process.Start(command);
        }

        private void ExcludeHelpLabel_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            wsjtxClient.UpdateMaxAutoGenEnqueue();
            string continent = wsjtxClient.myContinent == null ? "" : $" '{wsjtxClient.myContinent}'";
            string onBand = $"{bandComboBox.Items[1]}";
            ShowHelp($"{friendlyName} will add up to {wsjtxClient.maxAutoGenEnqueue} calls to the 'Stations calling' list that meet these conditions:" +
                $"{nl}{nl}- The call has not been worked before 'for 1 band' or '{onBand}'." +
                $"{nl}- The call is 'DX' or originated in your continent{continent}." +
                $"{nl}- The received message can be" +
                $"{nl}     * CQ, 73 or RR73 (the best time to reply), or" +
                $"{nl}     * grid information (for distance calculation), or" +
                $"{nl}     * any type (for maximum number of replies)." +
                $"{nl}- The caller is on your Rx time slot (if in 'Call CQ' mode)." +
                $"{nl}- The caller hasn't been replied to more than {wsjtxClient.maxPrevTo} times during this mode / band session." +
                $"{nl}{nl}If you select 'DX', {friendlyName} will reply to calls from continents other than yours." +
                $"{nl}{nl}For example, this is useful in case you've already worked all states/entities on your continent, and only want to reply to calls you haven't worked yet from other continents." +
                $"{nl}{nl}- If you select your continent{continent}, {friendlyName} will reply only to those calls." +
                $"{nl}{nl}For example, this is useful in case you're running QRP, and expect you can't be heard on other continents, and only want to reply to calls from your continent." +
                $"{nl}{nl}Select 'for 1 band' if you want to reply to calls you haven't worked before, but only need new calls on one band. Select '{onBand}' to also reply to calls that you haven't worked before on the current band." +
                $"{nl}{nl}Note: If you have entered 'directed CQs' to reply to, those CQs will be replied to regardless of the 'DX',{continent}, 'from messages', or new 'for 1 band' or '{onBand}' settings here.");
        }

        private void modeHelpLabel_Click(object sender, EventArgs e)
        {
            if (formLoaded && wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTuning();
            ShowHelp(BuildHelpText());
        }

        private string BuildHelpText()
        {
            string K(HotkeyAction a) => HotkeyConfig.FormatKeysForHelp(hotkeyConfig[a]);
            string ver = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? string.Empty;

            return
                $"{friendlyName} {ver}" +
                $"{nl}{nl}{friendlyName} processes 'QSO's by selecting one of two modes:" +
                $"{nl}'Call CQ' mode, and 'Listen for calls' mode." +
                $"{nl}Stations you haven't worked yet are added to the 'Stations calling' list." +
                $"{nl}Stations calling you directly have priority on this list, and are moved to the top." +
                $"{nl}{nl}You can leave this window open, for reference, as you run {friendlyName}." +
                $"{nl}Important: Minimize WSJT-X and stay on the {friendlyName} window full-time!" +

                $"{nl}{nl}Command keys:" +
                $"{nl}{K(HotkeyAction.RowOrder)}: Open stations available row order editor." +
                $"{nl}{K(HotkeyAction.Options)}: Review or set options for processing 'QSO's." +
                $"{nl}{K(HotkeyAction.CallCqMode)}: Start selected CQ mode (CQ only / CQ DX only / CQ and CQ DX). Does nothing in Listen mode." +
                $"{nl}{K(HotkeyAction.ListenMode)}: Select 'Listen for calls' mode." +
                $"{nl}{K(HotkeyAction.EnableTx)}: Enable transmit, or re-enable timed out 'QSO'." +
                $"{nl}{K(HotkeyAction.HaltTx)}: Halt transmit immediately." +
                $"{nl}{K(HotkeyAction.NextCall)}: Skip to the next available station, very useful!" +
                $"{nl}{K(HotkeyAction.ManualCall)}: Enter a callsign manually to call." +

                $"{nl}{K(HotkeyAction.AnalyzeSlot)}: Analyze transmit slot (find quietest audio frequency for CQ; requires 'Use best Tx frequency' enabled)." +
                $"{nl}{K(HotkeyAction.LookupStation)}: Look up selected station (shows callsign, country, state, LoTW status, and more)." +

                $"{nl}{nl}Radio configuration keys:" +
                $"{nl}{K(HotkeyAction.TuneMode)}: Toggle Tune mode, to determine correct audio output level to radio ({K(HotkeyAction.AudioUp)} and {K(HotkeyAction.AudioDown)} keys to adjust, {K(HotkeyAction.Prompts)} for fast or complete updates)." +
                $"{nl}{K(HotkeyAction.AudioUp)} key: Increase audio output level to radio (during tune or transmit)." +
                $"{nl}{K(HotkeyAction.AudioDown)} key: Decrease audio output level to radio (during tune or transmit)." +
                $"{nl}{K(HotkeyAction.PowerSwr)}: Quick check of output power and SWR (during transmit) or audio input (during receive)." +
                $"{nl}{K(HotkeyAction.BandUp)}: Select next higher band." +
                $"{nl}{K(HotkeyAction.BandDown)}: Select next lower band." +

                $"{nl}{nl}Optional command keys:" +
                $"{nl}{K(HotkeyAction.DeleteAllCalls)}: Delete all 'Stations calling'." +
                $"{nl}Delete key: Delete selected call in 'Stations calling'." +
                $"{nl}{K(HotkeyAction.TxPeriod)}: Toggle transmit period." +
                $"{nl}{K(HotkeyAction.HoldTimeout)}: Toggle extended timeout." +
                $"{nl}{K(HotkeyAction.UploadLotw)}: Upload to Logbook of the World." +
                $"{nl}{K(HotkeyAction.ToggleMode)}: Select operating mode (FT8 or FT4)." +
                $"{nl}{K(HotkeyAction.Prompts)}: Toggle command prompts in {friendlyName} status." +
                $"{nl}Escape key: Halt transmit, cancel current 'QSO', switch to Listen mode." +
                $"{nl}{K(HotkeyAction.UpdateCheck)}: Check for update to {friendlyName}." +
                $"{nl}{K(HotkeyAction.PSKReporter)}: Toggle sending spots to PSKReporter (leave 'Enabled' to help other hams)" +
                $"{nl}{K(HotkeyAction.SortOrder)}: Open stations available sort order editor." +
                $"{nl}{K(HotkeyAction.ResetWindowSize)}: Reset window size and position to default." +
                $"{nl}{K(HotkeyAction.Help)}: Read the list of shortcut keys." +

                $"{nl}{nl}Main navigation keys:" +
                $"{nl}{K(HotkeyAction.NavStatus)}: Read QSO and radio status (Note that {K(HotkeyAction.NavStatus)} is the 'home' location!)." +
                $"{nl}{K(HotkeyAction.NavCallList)}: Read and select from 'Stations calling' list." +

                $"{nl}{nl}Optional navigation keys:" +
                $"{nl}{K(HotkeyAction.NavLoggedList)}: Read 'Auto-logged calls' list." +
                $"{nl}{K(HotkeyAction.NavLoggedCount)}: Read total number of 'Auto-logged calls'." +
                $"{nl}{K(HotkeyAction.NavPendingCount)}: Read number of pending 'Stations calling'." +
                $"{nl}Ctrl, Y: Play the 'New call', 'Call directed to {SpacifyMyCall()}', and 'Logged' alert sounds.";
        }

        public void cqModeButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            wsjtxClient.TxModeChanged(WsjtxClient.TxModes.CALL_CQ);
            optionsDlg?.UpdateView();
        }

        public void listenModeButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            wsjtxClient.TxModeChanged(WsjtxClient.TxModes.LISTEN);
            optionsDlg?.UpdateView();
        }

        // Sets the 4-radio from the current txMode (called when mode flips between LISTEN and CALL_CQ)
        public void SyncCqIntentFromMode()
        {
            if (wsjtxClient == null) return;
            if (wsjtxClient.txMode == WsjtxClient.TxModes.LISTEN)
                cqIntentListenButton.Checked = true;
            else
                SyncCqSubtypeRadio();
        }

        // Sets the CQ subtype radio (CQ only / CQ DX only / CQ and CQ DX) from the checkboxes
        private void SyncCqSubtypeRadio()
        {
            bool callCq = callNonDirCqCheckBox.Checked;
            bool callCqDx = callCqDxCheckBox.Checked;
            if (callCq && callCqDx)
                cqIntentCqAndDxButton.Checked = true;
            else if (!callCq && callCqDx)
                cqIntentCqDxOnlyButton.Checked = true;
            else
                cqIntentCqOnlyButton.Checked = true;
        }

        // Called when CQ checkboxes change: only updates CQ subtype radio if a CQ intent is already selected
        public void SyncCqIntentFromCheckboxes()
        {
            if (_suppressIntentSync) return;
            if (!cqIntentListenButton.Checked)
                SyncCqSubtypeRadio();
        }

        // Click handlers for the 4 operating-mode intent radio buttons

        private void cqIntentListenButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;
            listenModeButton_Click(null, null);
        }

        private void cqIntentCqOnlyButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;
            _suppressIntentSync = true;
            callNonDirCqCheckBox.Checked = true;
            callCqDxCheckBox.Checked = false;
            _suppressIntentSync = false;
            optionsDlg?.UpdateView();
        }

        private void cqIntentCqDxOnlyButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;
            _suppressIntentSync = true;
            callCqDxCheckBox.Checked = true;
            callNonDirCqCheckBox.Checked = false;
            _suppressIntentSync = false;
            optionsDlg?.UpdateView();
        }

        private void cqIntentCqAndDxButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;
            _suppressIntentSync = true;
            callNonDirCqCheckBox.Checked = true;
            callCqDxCheckBox.Checked = true;
            _suppressIntentSync = false;
            optionsDlg?.UpdateView();
        }

        private void freqCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            wsjtxClient.WsjtxSettingChanged();
            wsjtxClient.AutoFreqChanged(freqCheckBox.Checked, false);
            optionsDlg?.UpdateView();
        }

        private void LimitTxHelpLabel_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            string adv = wsjtxClient != null ? $"{nl}{nl}If 'Optimize throughput' is selected, the maximum number of replies and CQs for the current call is automatically adjusted lower than the specified limit (if possible), to help process the call queue faster." +
                $"{nl}{nl}If 'Hold' is selected, the 'Repeated Tx' limit is ignored, and replies to the current call sign are transmitted a maximum of {wsjtxClient.holdMaxTxRepeat} times." : "";
            ShowHelp($"This will limit the number of times the same message is transmitted." +
                $"{nl}{nl}For example, it will limit the number of repeated transmitted replies or CQs for the current call. If there is no response to your reply messages when the limit is reached, the next call in the queue is processed (or if the call queue is empty, CQing (or listening) will resume)." +
                $"{nl}{nl}As the repeat limit is reduced, the number of times a call can be automatically re-added to the call queue is increased, to compensate.{adv}");
        }

        private void optimizeCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (formLoaded) wsjtxClient.TxRepeatChanged();
        }

        private void holdCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            wsjtxClient.HoldCheckBoxChanged();
        }

        private void directedTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.KeyChar = char.ToUpper(e.KeyChar);
            char c = e.KeyChar;
            if (c == (char)Keys.Back || c == ' ' || (c >= 'A' && c <= 'Z')) return;
            Console.Beep();
            e.Handled = true;
        }

        private void alertTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.KeyChar = char.ToUpper(e.KeyChar);
            char c = e.KeyChar;
            if (c == (char)Keys.Back || c == ' ' || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return;
            Console.Beep();
            e.Handled = true;
        }

        private void ReplyRR73HelpLabel_Click(object sender, EventArgs e)
        {
            ShowHelp($"Select 'Reply to RR73 msg' if you want to reply '73' to an RR73 message received at the end of a QSO." +
                $"{nl}{nl}'RR73' means:" +
                $"{nl}- 'Signal report received', and" +
                $"{nl}- 'Best regards', and" +
                $"{nl}- 'I'm confident you will see this', so" +
                $"{nl}- 'No further reply requested'." +
                $"{nl}{nl}You can safely skip replying to 'RR73' to speed up the QSO cycle, if conditions allow." +
                $"{nl}{nl}Exceptions:" +
                $"{nl}- If from a new country, RR73 is always replied to with a '73'." +
                $"{nl}- If a Fox/Hound-style (multi-stream) 'RR73' message, no '73' is expected by the caller, so it's not sent.");
        }

        private void PeriodHelpLabel_Click(object sender, EventArgs e)
        {
            ShowHelp($"'Tx period' allows you to select which period you want WSJT-X to use for transmit when in 'Listen for calls' mode." +
                $"{nl}{nl}If you are using multiple transmitters at your station, you may want for all of them to use the same Tx period, to avoid interference." +
                $"{nl}{nl}Otherwise, the normal selection is 'any'.");
        }

        private void AutoFreqHelpLabel_Click(object sender, EventArgs e)
        {
            ShowHelp($"The Tx audio frequency is automatically set to an unused part of the audio spectrum." +
                $"{nl}{nl}After a period of no replies being received, transmitting is temporarily suspended for one Tx cycle, the received audio is re-sampled, and the best Tx frequency is re-calculated.");
        }

        private void blockHelpLabel_Click(object sender, EventArgs e)
        {
            ShowHelp($"To block replies to a specific call sign:" +
                $"{nl}{nl}If the call sign is in the 'Stations calling' list:" +
                $"{nl}- Hold the 'Ctrl' key down and click on the call sign." +
                $"{nl}{nl}Otherwise," +
                $"{nl}- Enter the call sign in the 'Block any reply' box, with each call sign separated by a space." +
                $"{nl}{nl}Note: If you manually select a blocked call, it will be unblocked to allow replies.");
        }

        private void callAddedCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (formLoaded && callAddedCheckBox.Checked) wsjtxClient.PlaySoundEvent(true, soundFile_CallAdded);
        }

        private void exceptTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.KeyChar = char.ToUpper(e.KeyChar);
            char c = e.KeyChar;
            if (c == (char)Keys.Back || c == ' ' || c == '/' || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return;
            Console.Beep();
            e.Handled = true;
        }

        private void msgTextBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (!formLoaded || Control.ModifierKeys != Keys.Control) return;

            if (e.Button == MouseButtons.Left)
            {
                //available for ctrl/left-click action
            }
            else
            {
                //available for ctrl/right-click action
            }
        }

        private void callCqDxCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            ignoreNonDxCheckBox.Enabled = callCqDxCheckBox.Checked;

            if (callDirCqCheckBox.Checked || callNonDirCqCheckBox.Checked || replyDirCqCheckBox.Checked || replyDxCheckBox.Checked || replyLocalCheckBox.Checked)
            {
                if (callCqDxCheckBox.Checked) ignoreNonDxCheckBox.Checked = false;
            }

            ValidateDirCqTextBox();
            if (!callCqDxCheckBox.Checked && !callDirCqCheckBox.Checked && !callNonDirCqCheckBox.Checked)
            {
                callNonDirCqCheckBox.Checked = true;
            }

            optionsDlg?.UpdateView();
            SyncCqIntentFromCheckboxes();

            if (formLoaded) wsjtxClient.WsjtxSettingChanged();
        }

        private void directedTextBox_Leave(object sender, EventArgs e)
        {
            if (directedTextBox.Text == separateBySpaces) return;

            ValidateDirCqTextBox();

            if (directedTextBox.Text == "")
            {
                callDirCqCheckBox.Checked = false;
                return;
            }
        }

        private void ignoreNonDxCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (ignoreNonDxCheckBox.Checked)
            {
                callDirCqCheckBox.Checked = false;
                callNonDirCqCheckBox.Checked = false;
                replyDirCqCheckBox.Checked = false;
                replyLocalCheckBox.Checked = false;
                replyDxCheckBox.Checked = false;
            }
        }

        private void callDirCqCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            directedTextBox.Enabled = callDirCqCheckBox.Checked;
            if (callDirCqCheckBox.Checked && directedTextBox.Text == separateBySpaces)
            {
                ignoreDirectedChange = true;
                directedTextBox.Clear();
                directedTextBox.ForeColor = System.Drawing.Color.Black;
            }
            if (!callDirCqCheckBox.Checked && directedTextBox.Text == "") directedTextBox.Text = separateBySpaces;

            if (callDirCqCheckBox.Checked)
            {
                if (callCqDxCheckBox.Checked) ignoreNonDxCheckBox.Checked = false;
            }
            else
            {
                if (!callCqDxCheckBox.Checked)
                {
                    callNonDirCqCheckBox.Checked = true;
                }
            }
            wsjtxClient.WsjtxSettingChanged();              //resets CQ to not directed

            optionsDlg?.UpdateView();
        }

        private void callNonDirCqCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (callNonDirCqCheckBox.Checked)
            {
                if (callCqDxCheckBox.Checked) ignoreNonDxCheckBox.Checked = false;
            }
            else
            {
                ValidateDirCqTextBox();
                if (!callCqDxCheckBox.Checked && !callDirCqCheckBox.Checked)
                {
                    callNonDirCqCheckBox.Checked = true;
                }
            }
            if (formLoaded) wsjtxClient.WsjtxSettingChanged();              //resets CQ to non-directed

            optionsDlg?.UpdateView();
            SyncCqIntentFromCheckboxes();
        }

        private void alertTextBox_Leave(object sender, EventArgs e)
        {
            ValidateAlertTextBox();
        }

        private void ValidateAlertTextBox()
        {
            if (alertTextBox.Text == separateBySpaces) return;

            var dirArray = alertTextBox.Text.Trim().ToUpper().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string corrText = "";
            string delim = "";
            foreach (string dir in dirArray)
            {
                if (dir.Length >= 2 && dir.Length <= 4 && (!Regex.IsMatch(dir, alphaOnly) || !Regex.IsMatch(dir, numericOnly))) corrText = corrText + delim + dir;
                delim = " ";
            }
            alertTextBox.Text = corrText;
        }

        private void ValidateDirCqTextBox()
        {
            if (directedTextBox.Text == separateBySpaces) return;

            string text = directedTextBox.Text.Replace("*", "");        //obsoleted
            var dirArray = text.Trim().ToUpper().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string corrText = "";
            string delim = "";
            foreach (string dir in dirArray)
            {
                if (dir.Length >= 2 && dir.Length <= 4)
                {
                    corrText = corrText + delim + dir;
                    delim = " ";
                }
            }
            directedTextBox.Text = corrText;

            if (corrText == "") callDirCqCheckBox.Checked = false;
        }

        private void useRR73CheckBox_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            wsjtxClient.useRR73 = useRR73CheckBox.Checked;
        }

        private void replyLocalCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (replyLocalCheckBox.Checked) ignoreNonDxCheckBox.Checked = false;
            UpdateCqNewOnBand();
            CheckManualSelection();
            optionsDlg?.UpdateView();
        }

        private void CheckManualSelection()
        {
            if (formLoaded && listenModeButton.Checked && !replyDxCheckBox.Checked && !replyLocalCheckBox.Checked && !replyDirCqCheckBox.Checked)
            {
                ShowMsg($"Select calls manually in WSJT-X (alt/dbl-click)", true);
            }
        }

        private void replyDxCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (replyDxCheckBox.Checked) ignoreNonDxCheckBox.Checked = false;
            UpdateCqNewOnBand();
            CheckManualSelection();
            optionsDlg?.UpdateView();
        }

        private void Controller_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Y)
            {
                if (wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTuning();
                DemoSounds();
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.O)
            {
                optionsButton_Click(null, null);
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.C)
            {
                if (wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTx();
                OpenUdpConfig();
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.D)
            {
                verLabel_DoubleClick(null, null);
            }

            if (e.KeyData == hotkeyConfig[HotkeyAction.NavStatus])
            {
                if (!statusText.Focused)
                {
                    statusText.Focus();
                }
                BeginInvoke((Action)(() => SendKeys.Send("{UP}")));
            }

            //past this point all keys cause tuning to halt

            if (e.KeyData == hotkeyConfig[HotkeyAction.NavLoggedList])
            {
                if (formLoaded && wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTuning();
                logListBox.Focus();
            }

            if (e.KeyData == hotkeyConfig[HotkeyAction.NavLoggedCount])
            {
                if (formLoaded && wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTuning();
                loggedLabel.Focus();
            }

            if (e.KeyData == hotkeyConfig[HotkeyAction.NavCallList])
            {
                if (callListBox.Visible)
                {
                    if (formLoaded && wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTuning();
                    callListBox.Focus();
                }
            }

            if (e.KeyData == hotkeyConfig[HotkeyAction.NavPendingCount])
            {
                if (formLoaded && wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTuning();
                replyListLabel.Focus();
            }

            if (hotkeyConfig[HotkeyAction.NavAdvTx1] != Keys.None && e.KeyData == hotkeyConfig[HotkeyAction.NavAdvTx1])
            {
                if (advTx1ListBox.Visible)
                {
                    if (formLoaded && wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTuning();
                    advTx1ListBox.Focus();
                }
            }

            if (hotkeyConfig[HotkeyAction.NavAdvTx2] != Keys.None && e.KeyData == hotkeyConfig[HotkeyAction.NavAdvTx2])
            {
                if (advTx2ListBox.Visible)
                {
                    if (formLoaded && wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTuning();
                    advTx2ListBox.Focus();
                }
            }

            if (hotkeyConfig[HotkeyAction.NavAdvRaw] != Keys.None && e.KeyData == hotkeyConfig[HotkeyAction.NavAdvRaw])
            {
                if (advRawListBox.Visible)
                {
                    if (formLoaded && wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTuning();
                    advRawListBox.Focus();
                    if (advRawListBox.Items.Count > 0 && advRawListBox.SelectionMode != SelectionMode.None && advRawListBox.SelectedIndex < 0)
                        advRawListBox.SelectedIndex = 0;
                }
            }

            if (!formLoaded) return;

            if (e.KeyCode == Keys.Escape)               //halt Tx, return to Listen mode
            {
                var focused = this.ActiveControl;
                if (wsjtxClient.ConnectedToWsjtx())
                {
                    wsjtxClient.RequeueAbortedCall();   // must precede CancelQso (needs callInProg/replyDecode)
                    wsjtxClient.CancelQso();
                    wsjtxClient.HaltAndDisableTx();     // unconditional: works in both CQ and Listen mode
                    wsjtxClient.ResetTxToCq();
                    listenModeButton_Click(null, null);
                    ShowMsg("Tx halted", true);
                }
                else
                {
                    Console.Beep();
                }
                BeginInvoke((Action)(() =>
                    BeginInvoke((Action)(() => RestoreFocus(focused)))
                ));
            }
        }

        private void RestoreFocus(Control c)
        {
            if (c != null && !c.IsDisposed && c.IsHandleCreated && c.Visible && c.Enabled && c.CanFocus)
                c.Focus();
        }

        private void OpenCallWaitingRowOrderEditor()
        {
            if (iniFile == null || wsjtxClient == null) return;

            var currentOrder = wsjtxClient.callWaitingRowOrderFields;
            using (var dlg = new CallWaitingRowOrderDlg(currentOrder, wsjtxClient.debug))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var selectedFields = dlg.SelectedFields;
                iniFile.Write("callWaitingRowOrder", string.Join(",", selectedFields));
                wsjtxClient.callWaitingRowOrderFields = new List<string>(selectedFields);
                wsjtxClient.RefreshCallWaitingRows();
            }
        }

        public void ShowHelp(string s)
        {
            helpTimer.Tag = s;
            helpTimer.Start();
        }

        private void helpTimer_Tick(object sender, EventArgs e)
        {
            helpTimer.Stop();
            _helpReturnFocus = this.ActiveControl;
            if (helpDlg != null) helpDlg.Close();
            helpDlg = new HelpDlg(this, $"{wsjtxClient.pgmName}{helpSuffix}", (string)helpTimer.Tag);
            helpDlg.Show();
            helpDlg.Activate();
        }

        private void cqModeButton_CheckedChanged(object sender, EventArgs e)
        {
            SyncCqIntentFromMode();
        }

        private void listenModeButton_CheckedChanged(object sender, EventArgs e)
        {
            SyncCqIntentFromMode();
        }

        private void UpdateCqNewOnBand()
        {
            anyMsgRadioButton.Enabled = cqGridRadioButton.Enabled = cqOnlyRadioButton.Enabled = bandComboBox.Enabled = replyDxCheckBox.Checked || replyLocalCheckBox.Checked;
        }

        private void OpenSortOrderEditor()
        {
            if (iniFile == null || wsjtxClient == null) return;

            using (var dlg = new RankOrderDlg(
                wsjtxClient.rankOrderList,
                wsjtxClient.rankBeamMethod,
                wsjtxClient.categoryWeight,
                wsjtxClient.callingEnabled))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                wsjtxClient.ApplySortOrder(dlg.SelectedOrder, dlg.SelectedBeam);
                wsjtxClient.ApplyCategoryWeights(dlg.SelectedCategoryWeights);
                wsjtxClient.ApplyCallingPriorities(dlg.SelectedCallingPriorities);

                iniFile.Write("rankOrder",         string.Join(",", dlg.SelectedOrder.Select(m => MethodToRankId(m))));
                iniFile.Write("rankBeam",          dlg.SelectedBeam.HasValue ? MethodToBeamId(dlg.SelectedBeam.Value) : "none");
                iniFile.Write("rankMethod",        wsjtxClient.rankMethodIdx.ToString());
                iniFile.Write("categoryWeights",   FormatCategoryWeights(dlg.SelectedCategoryWeights));
                iniFile.Write("callingPriorities", FormatCallingPriorities(dlg.SelectedCallingPriorities));

                optionsDlg?.UpdateView();
            }
        }

        private static List<WsjtxClient.RankMethods> ParseRankOrder(string rankOrderStr, int legacyIdx)
        {
            if (!string.IsNullOrWhiteSpace(rankOrderStr))
            {
                var result = new List<WsjtxClient.RankMethods>();
                foreach (var tok in rankOrderStr.Split(','))
                {
                    WsjtxClient.RankMethods m;
                    if (RankIdToMethod(tok.Trim(), out m) && !result.Contains(m))
                        result.Add(m);
                }
                if (result.Count > 0) return result;
            }
            if (legacyIdx >= (int)WsjtxClient.RankMethods.AZ_NQUAD)
                return new List<WsjtxClient.RankMethods> { WsjtxClient.RankMethods.MOST_RECENT };
            if (Enum.IsDefined(typeof(WsjtxClient.RankMethods), legacyIdx))
                return new List<WsjtxClient.RankMethods> { (WsjtxClient.RankMethods)legacyIdx };
            return new List<WsjtxClient.RankMethods> { WsjtxClient.RankMethods.MOST_RECENT };
        }

        private static WsjtxClient.RankMethods? ParseRankBeam(string rankBeamStr, int legacyIdx)
        {
            if (!string.IsNullOrWhiteSpace(rankBeamStr))
            {
                WsjtxClient.RankMethods? b;
                if (BeamIdToMethod(rankBeamStr.Trim(), out b)) return b;
                return null;
            }
            if (legacyIdx >= (int)WsjtxClient.RankMethods.AZ_NQUAD &&
                Enum.IsDefined(typeof(WsjtxClient.RankMethods), legacyIdx))
                return (WsjtxClient.RankMethods)legacyIdx;
            return null;
        }

        private static bool RankIdToMethod(string id, out WsjtxClient.RankMethods method)
        {
            switch (id?.ToLowerInvariant())
            {
                case "call_order":  method = WsjtxClient.RankMethods.CALL_ORDER;  return true;
                case "most_recent": method = WsjtxClient.RankMethods.MOST_RECENT; return true;
                case "dist_near":   method = WsjtxClient.RankMethods.DIST_INCR;   return true;
                case "dist_far":    method = WsjtxClient.RankMethods.DIST_DECR;   return true;
                case "snr_weak":    method = WsjtxClient.RankMethods.SNR_INCR;    return true;
                case "snr_strong":  method = WsjtxClient.RankMethods.SNR_DECR;    return true;
                default:            method = default;                              return false;
            }
        }

        private static bool BeamIdToMethod(string id, out WsjtxClient.RankMethods? method)
        {
            switch (id?.ToLowerInvariant())
            {
                case "none":  method = null;                                  return true;
                case "az_n":  method = WsjtxClient.RankMethods.AZ_NQUAD;   return true;
                case "az_ne": method = WsjtxClient.RankMethods.AZ_NEQUAD;  return true;
                case "az_e":  method = WsjtxClient.RankMethods.AZ_EQUAD;   return true;
                case "az_se": method = WsjtxClient.RankMethods.AZ_SEQUAD;  return true;
                case "az_s":  method = WsjtxClient.RankMethods.AZ_SQUAD;   return true;
                case "az_sw": method = WsjtxClient.RankMethods.AZ_SWQUAD;  return true;
                case "az_w":  method = WsjtxClient.RankMethods.AZ_WQUAD;   return true;
                case "az_nw": method = WsjtxClient.RankMethods.AZ_NWQUAD;  return true;
                default:      method = null;                                  return false;
            }
        }

        private static string MethodToRankId(WsjtxClient.RankMethods method)
        {
            switch (method)
            {
                case WsjtxClient.RankMethods.CALL_ORDER:  return "call_order";
                case WsjtxClient.RankMethods.MOST_RECENT: return "most_recent";
                case WsjtxClient.RankMethods.DIST_INCR:   return "dist_near";
                case WsjtxClient.RankMethods.DIST_DECR:   return "dist_far";
                case WsjtxClient.RankMethods.SNR_INCR:    return "snr_weak";
                case WsjtxClient.RankMethods.SNR_DECR:    return "snr_strong";
                default:                                   return "most_recent";
            }
        }

        private static string MethodToBeamId(WsjtxClient.RankMethods method)
        {
            switch (method)
            {
                case WsjtxClient.RankMethods.AZ_NQUAD:  return "az_n";
                case WsjtxClient.RankMethods.AZ_NEQUAD: return "az_ne";
                case WsjtxClient.RankMethods.AZ_EQUAD:  return "az_e";
                case WsjtxClient.RankMethods.AZ_SEQUAD: return "az_se";
                case WsjtxClient.RankMethods.AZ_SQUAD:  return "az_s";
                case WsjtxClient.RankMethods.AZ_SWQUAD: return "az_sw";
                case WsjtxClient.RankMethods.AZ_WQUAD:  return "az_w";
                case WsjtxClient.RankMethods.AZ_NWQUAD: return "az_nw";
                default:                                 return "none";
            }
        }

        // Serialize categoryWeight to a comma-separated string of "CATEGORY=tier" pairs.
        // Order follows the CallCategory enum so it is stable and human-readable.
        private static string FormatCategoryWeights(Dictionary<WsjtxClient.CallCategory, int> weights)
        {
            var parts = new System.Text.StringBuilder();
            foreach (WsjtxClient.CallCategory cat in System.Enum.GetValues(typeof(WsjtxClient.CallCategory)))
            {
                if (parts.Length > 0) parts.Append(',');
                int tier;
                weights.TryGetValue(cat, out tier);
                parts.Append($"{cat}={tier}");
            }
            return parts.ToString();
        }

        // Parse a categoryWeights INI string back into a dictionary.
        // Returns null if the string is absent or malformed; caller falls back to defaults.
        private static Dictionary<WsjtxClient.CallCategory, int> ParseCategoryWeights(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var result = new Dictionary<WsjtxClient.CallCategory, int>();
            foreach (var tok in s.Split(','))
            {
                var kv = tok.Trim().Split('=');
                if (kv.Length != 2) return null;
                WsjtxClient.CallCategory cat;
                int tier;
                if (!System.Enum.TryParse(kv[0].Trim(), out cat)) return null;
                if (!int.TryParse(kv[1].Trim(), out tier) || tier < 0) return null;
                result[cat] = tier;
            }
            return result;
        }

        // Serialize callingEnabled to a comma-separated string preserving list order.
        private static string FormatCallingPriorities(List<WsjtxClient.CallCategory> enabled)
        {
            if (enabled == null) return string.Empty;
            return string.Join(",", enabled.Select(cat => cat.ToString()));
        }

        // Parse a callingPriorities INI string into an ordered List of enabled categories.
        // INI token order is preserved — that order drives Alt+N category selection.
        // callingStr: new "callingPriorities" key (comma-separated enabled categories in order).
        // legacyDisabledStr: old "categoryDisabled" key used for migration if callingStr absent.
        // Returns default priority order on missing/malformed input.
        private static List<WsjtxClient.CallCategory> ParseCallingPriorities(
            string callingStr, string legacyDisabledStr = null)
        {
            if (!string.IsNullOrWhiteSpace(callingStr))
            {
                var result = new List<WsjtxClient.CallCategory>();
                foreach (var tok in callingStr.Split(','))
                {
                    WsjtxClient.CallCategory cat;
                    if (System.Enum.TryParse(tok.Trim(), out cat) && !result.Contains(cat))
                        result.Add(cat);   // order preserved; DEFAULT (Ordinary CQ) is permitted
                }
                if (result.Count > 0) return result;
            }

            // Migration: if old categoryDisabled exists, derive callingPriorities from it.
            // Enabled categories go in default priority order; disabled ones are excluded.
            if (!string.IsNullOrWhiteSpace(legacyDisabledStr))
            {
                var disabled = new HashSet<WsjtxClient.CallCategory>();
                foreach (var tok in legacyDisabledStr.Split(','))
                {
                    WsjtxClient.CallCategory cat;
                    if (System.Enum.TryParse(tok.Trim(), out cat) && cat != WsjtxClient.CallCategory.DEFAULT)
                        disabled.Add(cat);
                }
                var result = new List<WsjtxClient.CallCategory>();
                foreach (WsjtxClient.CallCategory cat in DefaultCallingOrder)
                {
                    if (!disabled.Contains(cat)) result.Add(cat);
                }
                return result;
            }

            // Default: all non-DEFAULT categories in default priority order.
            return new List<WsjtxClient.CallCategory>(DefaultCallingOrder);
        }

        // Canonical default Alt+N priority order (highest → lowest).
        private static readonly WsjtxClient.CallCategory[] DefaultCallingOrder =
        {
            WsjtxClient.CallCategory.TO_MYCALL,
            WsjtxClient.CallCategory.NEW_COUNTRY_ON_BAND,
            WsjtxClient.CallCategory.NEW_COUNTRY,
            WsjtxClient.CallCategory.WANTED_CQ,
            WsjtxClient.CallCategory.ALWAYS_WANTED,
            WsjtxClient.CallCategory.WAS_NEEDED,
            WsjtxClient.CallCategory.DXCC_UNCONFIRMED,
            WsjtxClient.CallCategory.ZONE_NEEDED,
            WsjtxClient.CallCategory.STILL_NEEDED,
            WsjtxClient.CallCategory.DEFAULT,
        };

        // Serialize wantedCalls to a comma-separated list of callsigns.
        private static string FormatWantedCalls(HashSet<string> calls)
        {
            if (calls == null || calls.Count == 0) return string.Empty;
            var sorted = new List<string>(calls);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", sorted);
        }

        // Parse a wantedCalls INI string into a HashSet (uppercase, trimmed, no duplicates).
        private static HashSet<string> ParseWantedCalls(string s)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(s)) return result;
            foreach (var tok in s.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string call = tok.Trim().ToUpperInvariant();
                if (!string.IsNullOrEmpty(call)) result.Add(call);
            }
            return result;
        }

        // Serialize activeAwardRuleIds to a comma-separated list of Rule Definition Ids.
        public static string FormatActiveAwardRuleIds(HashSet<string> ids)
        {
            if (ids == null || ids.Count == 0) return string.Empty;
            var sorted = new List<string>(ids);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", sorted);
        }

        // Parse an activeAwardRuleIds INI string into a HashSet (trimmed, no duplicates).
        public static HashSet<string> ParseActiveAwardRuleIds(string s)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(s)) return result;
            foreach (var tok in s.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string id = tok.Trim();
                if (!string.IsNullOrEmpty(id)) result.Add(id);
            }
            return result;
        }

        // Called by OptionsDlg when the Wanted Calls tab is saved.
        public void ApplyAndSaveWantedCalls(HashSet<string> normalized)
        {
            wsjtxClient.ApplyWantedCalls(normalized);
            if (iniFile != null)
                iniFile.Write("wantedCalls", FormatWantedCalls(normalized));
        }

        private void callListBox_MouseDown(object sender, MouseEventArgs e)
        {
            mouseEventArgs = e;
            listBoxClickCount++;
            callListBoxClickTimer.Start();
        }

        private void callListBoxClickTimer_Tick(object sender, EventArgs e)
        {
            callListBoxClickTimer.Stop();
            bool dblClk = listBoxClickCount > 1;
            listBoxClickCount = 0;
            ProcessCallListBoxAnyClick(dblClk);
        }

        private void ProcessCallListBoxAnyClick(bool dblClk)
        {
            if (!formLoaded) return;

            int idx = callListBox.IndexFromPoint(mouseEventArgs.Location);

            if (mouseEventArgs.Button == MouseButtons.Right)
            {
                if (Control.ModifierKeys == Keys.Control)
                {
                    if (idx < 0 || callListBox.SelectionMode == SelectionMode.None) return;
                    //available for ctrl/right-click action
                }
                else   //right-click (no modifier)
                {
                    if (idx >= 0 && idx < callListBox.Items.Count && callListBox.SelectionMode != SelectionMode.None) callListBox.SelectedIndex = idx;
                    wsjtxClient.EditCallQueue(wsjtxClient.MapNormalListIndex(idx));
                }
            }
            else   //left-click
            {
                if (dblClk)   //left-dbl-click (no modifier)
                {
                    if (callListBox.SelectionMode == SelectionMode.None) return;
                    wsjtxClient.NextCall(false, wsjtxClient.MapNormalListIndex(idx), operatorSelected: true);
                }
                else
                {
                    if (idx < 0) return;

                    if (Control.ModifierKeys == Keys.Control)
                    {
                        wsjtxClient.BlockCall(idx);
                    }
                }
            }
        }

        private string MyCall()
        {
            return (wsjtxClient == null || wsjtxClient.myCall == null) ? "my call" : wsjtxClient.myCall;
        }

        private void cqOnlyRadioButton_Click(object sender, EventArgs e)
        {
            anyMsgRadioButton.Checked = cqGridRadioButton.Checked = false;
        }

        private void cqGridRadioButton_Click(object sender, EventArgs e)
        {
            anyMsgRadioButton.Checked = cqOnlyRadioButton.Checked = false;
        }

        private void anyMsgRadioButton_Click(object sender, EventArgs e)
        {
            cqGridRadioButton.Checked = cqOnlyRadioButton.Checked = false;
        }

        private void periodComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            wsjtxClient.TxPeriodIdxChanged(periodComboBox.SelectedIndex);
            optionsDlg?.UpdateView();
        }

        private void directedTextBox_TextChanged(object sender, EventArgs e)
        {
            if (ignoreDirectedChange)
            {
                ignoreDirectedChange = false;
                return;           //was cleared initially
            }
            if (directedTextBox.Text == "") callDirCqCheckBox.Checked = false;
            optionsDlg?.UpdateView();
            if (formLoaded) wsjtxClient.WsjtxSettingChanged();
        }

        public void GuideListenMode()
        {
            listenModeButton_Click(null, null);
            periodComboBox.SelectedIndex = (int)WsjtxClient.ListenModeTxPeriods.ANY;
        }

        public void GuideCqMode()
        {
            cqModeButton_Click(null, null);
        }
        public void ToggleDx()
        {
            replyDxCheckBox.Checked = !replyDxCheckBox.Checked;
        }

        public void ToggleLocal()
        {
            replyLocalCheckBox.Checked = !replyLocalCheckBox.Checked;
        }

        public void ToggleActivator()
        {
            ValidateDirCqTextBox();
            if (directedTextBox.Text == separateBySpaces || directedTextBox.Text == "") directedTextBox.Text = " ";
            if (directedTextBox.Text == "POTA" && callDirCqCheckBox.Checked && !callCqDxCheckBox.Checked && !callNonDirCqCheckBox.Checked)
            {
                directedTextBox.Text = directedTextBox.Text = "";
                callDirCqCheckBox.Checked = false;
            }
            else
            {
                directedTextBox.Text = "POTA";
                callDirCqCheckBox.Checked = true;
                callCqDxCheckBox.Checked = callNonDirCqCheckBox.Checked = false;
            }
            ValidateDirCqTextBox();
        }
        public void ToggleHunter()
        {
            bool origState = replyDirCqCheckBox.Checked;
            ValidateAlertTextBox();
            if (alertTextBox.Text == separateBySpaces || alertTextBox.Text == "") alertTextBox.Text = " ";
            if (alertTextBox.Text.Contains("POTA") && replyDirCqCheckBox.Checked)
            {
                alertTextBox.Text = alertTextBox.Text.Replace("POTA", "");
                if (alertTextBox.Text.Length == 0) replyDirCqCheckBox.Checked = false;
            }
            else
            {
                if (!alertTextBox.Text.Contains("POTA")) alertTextBox.Text = $"{alertTextBox.Text} POTA";
                replyDirCqCheckBox.Checked = true;
            }
            ValidateAlertTextBox();
        }

        private void alertTextBox_TextChanged(object sender, EventArgs e)
        {
            optionsDlg?.UpdateView();
        }

        private void rowOrderButton_Click(object sender, EventArgs e)
        {
            OpenCallWaitingRowOrderEditor();
        }

        private void sortOrderButton_Click(object sender, EventArgs e)
        {
            OpenSortOrderEditor();
        }

        public string[] CallDirCqEntries()
        {
            ValidateDirCqTextBox();
            return directedTextBox.Text.Trim().ToUpper().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public string[] ReplyDirCqEntries()
        {
            ValidateAlertTextBox();
            return alertTextBox.Text.Trim().ToUpper().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private void replyRR73CheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (formLoaded) wsjtxClient.ReplyRR73Changed(replyRR73CheckBox.Checked);
        }

        private void exceptTextBox_TextChanged(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            if (exceptTextBox.Text == separateBySpaces || exceptTextBox.Text.Trim() == "" || ignoreExceptChange) return;
            wsjtxClient.BlockedTextChanged(exceptTextBox.Text);
        }

        public bool ExceptTextBoxRemove(string call)
        {
            if (call == null || !exceptTextBox.Text.Contains(call)) return false;

            exceptTextBox_Enter(null, null);
            exceptTextBox.Text = exceptTextBox.Text.Replace(call, "");      //triggers exceptTextBox_TextChanged()
            exceptTextBox_Leave(null, null);
            return true;
        }

        public void ExceptTextBoxAdd(string call)
        {
            //call known to be non-null
            exceptTextBox_Enter(null, null);
            exceptTextBox.Text = $"{call} {exceptTextBox.Text}";      //triggers exceptTextBox_TextChanged()
            exceptTextBox_Leave(null, null);
        }

        private void exceptTextBox_Enter(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            exceptTextBox.ForeColor = Color.Black;
            if (exceptTextBox.Text == separateBySpaces)
            {
                exceptTextBox.Text = "";
            }
        }

        private void exceptTextBox_Leave(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            exceptTextBox.ForeColor = Color.Black;

            StringBuilder sb = new StringBuilder();
            string sep = "";
            var blockedCalls = exceptTextBox.Text.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList<string>();
            foreach (string call in blockedCalls)
            {
                sb.Append($"{sep}{call}");
                sep = " ";
            }

            ignoreExceptChange = true;
            exceptTextBox.Text = sb.ToString();
            ignoreExceptChange = false;

            if (exceptTextBox.Text == "")
            {
                exceptTextBox.Text = separateBySpaces;
                exceptTextBox.ForeColor = Color.Gray;
            }
        }

        private void callListBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!formLoaded) return;
            if (callListBox.SelectionMode == SelectionMode.None) return;
            if (e.KeyChar != (char)Keys.Space && e.KeyChar != (char)Keys.Enter) return;

            e.Handled = true;   // prevent Win32 ListBox type-ahead after our handler
            int idx = callListBox.SelectedIndex;
            wsjtxClient.NextCall(false, wsjtxClient.MapNormalListIndex(idx), operatorSelected: true);
        }

        private void statusText_TextChanged(object sender, EventArgs e)
        {

        }

        private void statusText_Enter(object sender, EventArgs e)
        {
            if (statusText.SelectionLength > 0)
            {
                statusText.SelectionStart = 0;
                statusText.SelectionLength = 0;
            }
        }

        private void Controller_Enter(object sender, EventArgs e)
        {
            //tempOnly
            //statusText_Enter(null, null);
            //statusText.Focus();
        }

        private void Controller_Activated(object sender, EventArgs e)
        {
        }

        private string SpacifyMyCall()
        {
            if (!formLoaded || !wsjtxClient.ConnectedToWsjtx()) return "me";

            return wsjtxClient.SpacifyMyCall();
        }

        private void DemoSounds()
        {
            callAddedCheckBox_CheckedChanged(null, null);
            Thread.Sleep(750);
            mycallCheckBox_CheckedChanged(null, null);
            Thread.Sleep(250);
            loggedCheckBox_CheckedChanged(null, null);
        }

        private void CallListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (!formLoaded) return;

            if (e.Control && e.KeyCode == Keys.C)
            {
                if (callListBox.SelectionMode == SelectionMode.None) return;
                int idx = callListBox.SelectedIndex;
                if (idx < 0) return;
                string call = wsjtxClient.GetCallAtIndex(wsjtxClient.MapNormalListIndex(idx));
                if (call != null)
                {
                    try { Clipboard.SetText(call); }
                    catch { MessageBox.Show("Could not copy callsign to the clipboard.", friendlyName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode != Keys.Delete) return;

            int selIdx = callListBox.SelectedIndex;
            wsjtxClient.EditCallQueue(wsjtxClient.MapNormalListIndex(selIdx));
        }

        private void OpenManualCallDialog()
        {
            if (!wsjtxClient.ConnectedToWsjtx())
            {
                MessageBox.Show("WSJT-X is not connected.", friendlyName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (var dlg = new ManualCallDlg())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string callsign = dlg.Callsign;
                if (wsjtxClient.IsBlockedCall(callsign))
                {
                    MessageBox.Show($"{callsign} is blocked.", friendlyName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                bool started = wsjtxClient.ManualEnqueueCall(callsign);
                if (started)
                    ShowMsg($"Manual call started for {callsign}", false);
            }
        }

        public void ApplyAdvancedLayout()
        {
            bool show     = advancedCallLayout;
            bool showTx1  = show && advShowTx1;
            bool showTx2  = show && advShowTx2;
            bool showRaw  = show && advShowRaw;
            bool anyAdvList = showTx1 || showTx2 || showRaw;

            SuspendLayout();

            // Normal call list is visible only when no advanced list is replacing it
            callListBox.Visible    = !anyAdvList;
            replyListLabel.Visible = !anyAdvList;

            advTx1Label.Visible   = showTx1;
            advTx1ListBox.Visible = showTx1;
            advTx2Label.Visible   = showTx2;
            advTx2ListBox.Visible = showTx2;
            advRawLabel.Visible   = showRaw;
            advRawListBox.Visible = showRaw;

            // Reposition and resize visible advanced lists so they stack tightly
            // starting just below the last main-control row, with height scaled to count.
            if (anyAdvList)
            {
                const int startY   = 376;   // first label Y (same as designer baseline)
                const int labelH   = 14;    // approx height of bold 8.25pt label
                const int labelGap = 2;     // gap between label bottom and list top
                const int groupGap = 6;     // gap between list bottom and next label
                const int listX    = 10;

                // Lists widen to fill the window, never below today's default 280px.
                int listW = Math.Max(280, this.ClientSize.Width - 2 * listX);

                int count = (showTx1 ? 1 : 0) + (showTx2 ? 1 : 0) + (showRaw ? 1 : 0);

                // Extra vertical room the current window height offers beyond the natural
                // (unstretched) size for however many lists are shown, split evenly between
                // them — grows TX1/TX2/Raw with the window without shrinking below the base sizes.
                int naturalBottom = NaturalAdvancedListsBottom(showTx1, showTx2, showRaw, out int baseListH, out int baseRawH);
                int extra = Math.Max(0, this.Height - (naturalBottom + 45));
                int extraPerList = count > 0 ? extra / count : 0;

                int listH = baseListH + extraPerList;
                int rawH  = baseRawH + extraPerList;

                int y = startY;
                if (showTx1)
                {
                    advTx1Label.Location   = new Point(listX, y);
                    y += labelH + labelGap;
                    advTx1ListBox.Location = new Point(listX, y);
                    advTx1ListBox.Size     = new Size(listW, listH);
                    y += listH + groupGap;
                }
                if (showTx2)
                {
                    advTx2Label.Location   = new Point(listX, y);
                    y += labelH + labelGap;
                    advTx2ListBox.Location = new Point(listX, y);
                    advTx2ListBox.Size     = new Size(listW, listH);
                    y += listH + groupGap;
                }
                if (showRaw)
                {
                    advRawLabel.Location   = new Point(listX, y);
                    y += labelH + labelGap;
                    advRawListBox.Location = new Point(listX, y);
                    advRawListBox.Size     = new Size(listW, rawH);
                }
            }

            ResumeLayout(false);

            if (!show)
                wsjtxClient?.UpdateCallListAccessibleName(force: true);

            UpdateDebug();

            if (show && wsjtxClient != null)
                wsjtxClient.RefreshAdvancedLists();
        }

        private void AdvTx1ListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (!formLoaded) return;

            if (e.Control && e.KeyCode == Keys.C)
            {
                int idx = advTx1ListBox.SelectedIndex;
                if (idx < 0) return;
                string call = wsjtxClient.GetCallAtTx1Index(idx);
                if (call != null)
                {
                    try { Clipboard.SetText(call); }
                    catch { MessageBox.Show("Could not copy callsign.", friendlyName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                int idx = advTx1ListBox.SelectedIndex;
                if (idx < 0) return;
                int queueIdx = wsjtxClient.GetQueueIndexForTx1(idx);
                if (queueIdx >= 0) wsjtxClient.EditCallQueue(queueIdx);
            }
        }

        private void AdvTx1ListBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!formLoaded) return;
            if (e.KeyChar != (char)Keys.Space && e.KeyChar != (char)Keys.Enter) return;

            e.Handled = true;   // prevent Win32 ListBox type-ahead after our handler
            int idx = advTx1ListBox.SelectedIndex;
            if (idx < 0) idx = 0;
            wsjtxClient.NextCallFromTx1(idx);
        }

        private void AdvTx2ListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (!formLoaded) return;

            if (e.Control && e.KeyCode == Keys.C)
            {
                int idx = advTx2ListBox.SelectedIndex;
                if (idx < 0) return;
                string call = wsjtxClient.GetCallAtTx2Index(idx);
                if (call != null)
                {
                    try { Clipboard.SetText(call); }
                    catch { MessageBox.Show("Could not copy callsign.", friendlyName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                int idx = advTx2ListBox.SelectedIndex;
                if (idx < 0) return;
                int queueIdx = wsjtxClient.GetQueueIndexForTx2(idx);
                if (queueIdx >= 0) wsjtxClient.EditCallQueue(queueIdx);
            }
        }

        private void AdvTx2ListBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!formLoaded) return;
            if (e.KeyChar != (char)Keys.Space && e.KeyChar != (char)Keys.Enter) return;

            e.Handled = true;   // prevent Win32 ListBox type-ahead after our handler
            int idx = advTx2ListBox.SelectedIndex;
            if (idx < 0) idx = 0;
            wsjtxClient.NextCallFromTx2(idx);
        }

        private void AdvRawListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (!formLoaded) return;

            if (e.Control && e.KeyCode == Keys.C)
            {
                int idx = advRawListBox.SelectedIndex;
                if (idx < 0) return;
                string text = wsjtxClient.GetRawDecodeCallOrText(idx);
                if (text != null)
                {
                    try { Clipboard.SetText(text); }
                    catch { MessageBox.Show("Could not copy to clipboard.", friendlyName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void AdvRawListBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!formLoaded) return;
            if (e.KeyChar != (char)Keys.Space && e.KeyChar != (char)Keys.Enter) return;

            e.Handled = true;   // prevent Win32 ListBox type-ahead after our handler
            int idx = advRawListBox.SelectedIndex;
            if (idx < 0) return;
            wsjtxClient.NextCallFromRawDecode(idx);
        }
    }
}



