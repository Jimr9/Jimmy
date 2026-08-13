using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WSJTX_Controller
{
    // WPF migration: Controller is no longer a Form. It's the same "hub" object the business
    // logic (WsjtxClient.*.cs and friends) has always read/written via `ctrl.xyz`, now backed by
    // WPF instead of WinForms -- same field names throughout (so those call sites need zero
    // changes), same sub-objects (Radio/NativeEngine/Frequencies/Notifications/hotkeyConfig/
    // lookupManager/rigctldClient), same IJimmyStatusView/IJimmyQueueView/IJimmyLogView contract.
    // WinForms-control-typed fields (CheckBox, TextBox, etc.) are replaced by the small
    // WPF-bindable shim classes in ViewState.cs; the five ListBox-typed fields business logic
    // reads directly (callListBox, logListBox, advTx1/2ListBox, advRawListBox) stay real,
    // headless System.Windows.Forms.ListBox instances -- UseWindowsForms is still referenced
    // (see Jimmy.csproj) specifically so this and HotkeyConfig's Keys usage need no changes.
    // The actual visible WPF lists are separate controls in MainWindow, kept in sync by the
    // Render* methods below.
    public partial class Controller : IJimmyStatusView, IJimmyQueueView, IJimmyLogView
    {
        // ── Real WPF UI hookup (set once by MainWindow at startup) ──
        public IWpfStatusSink StatusSink;
        public IWpfListSink ListSink;

        // ── Core sub-objects (business logic, unchanged types) ──
        public JimmySettings Settings = new JimmySettings();
        public RadioSettings Radio = new RadioSettings();
        public NativeEngineSettings NativeEngine = new NativeEngineSettings();
        public DecodeSettings Decode = new DecodeSettings();
        public FrequencySettings Frequencies = new FrequencySettings();
        public NotificationSettings Notifications = new NotificationSettings();

        // Lookup / Data settings (Options > Lookup, deferred UI -- fields still real so
        // lookupManager.Initialize gets sensible values; defaults are all "off").
        public bool useLookupData = false;
        public bool qrzEnabled = false;
        public string qrzUsername = "";
        public string qrzPassword = "";
        public int qrzCacheDays = 7;
        public QrzLookupPolicy qrzLookupPolicy = QrzLookupPolicy.Disabled;
        public int qrzMinIntervalSeconds = 10;
        public bool lotwEnabled = false;
        public bool lotwBoostEnabled = false;
        public int lotwRefreshDays = 30;
        public int clubLogRefreshDays = 30;
        public bool fccUlsEnabled = false;
        public int fccUlsRefreshDays = 7;
        public HotkeyConfig hotkeyConfig;
        public RigctldClient rigctldClient;
        public RadioStatus lastRadioStatus;
        public NativeEngineClient nativeEngineClient;
        public LookupManager lookupManager;
        public WsjtxClient wsjtxClient;
        public string friendlyName = "Jimmy Test";
        public List<string> activeAwardRuleIds = new List<string>();

        // ── Plain settings fields (verbatim from the WinForms Controller) ──
        public bool alwaysOnTop = false;
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
        public bool rawShowDistAz = false;
        public bool rawOnlyCallsigns = false;
        public bool rawOnlyUnworked = false;
        public bool rawOnlyRanked = false;
        public bool rawNewestFirst = false;
        public int rawMaxRows = 100;
        public int maxQueuedCallsBase = 5;
        public int maxCallQueueAgePeriods = 16;
        public int statusBatchDelayMs = 500;
        public bool keepTransmitListDuringTx = false;
        public bool keepListPositionDuringRefresh = false;
        public bool moveFocusToStatusOnCallSelect = false;
        public bool checkForUpdatesOnStartup = false;
        public bool soundsEnabled = true;
        public bool wantedCallAnywhereEnabled = true;
        public string qrzLogbookApiKey = "";
        public bool qrzUploadEnabled = false;
        public bool qrzUploadRealtime = false;
        public bool lotwUploadEnabled = true;
        public string tqslStationLocation = "";
        public string directedCqLockedEntry = "";
        public bool clubLogUploadEnabled = false;
        public bool clubLogUploadRealtime = false;
        public string clubLogUploadEmail = "";
        public string clubLogUploadPassword = "";
        public string clubLogUploadCallsign = "";
        public bool hrdLogUploadEnabled = false;
        public bool hrdLogUploadRealtime = false;
        public string hrdLogUploadCode = "";
        public string hrdLogUploadCallsign = "";

        // advancedCallLayout/advShowTx1/2/advShowRaw delegate to JimmySettings, matching the
        // original Controller exactly.
        public bool advancedCallLayout { get => Settings.AdvancedCallLayout; set => Settings.AdvancedCallLayout = value; }
        public bool advShowTx1 { get => Settings.AdvShowTx1; set => Settings.AdvShowTx1 = value; }
        public bool advShowTx2 { get => Settings.AdvShowTx2; set => Settings.AdvShowTx2 = value; }
        public bool advShowRaw { get => Settings.AdvShowRaw; set => Settings.AdvShowRaw = value; }

        // Sound event enable/file fields -- plain data, Options > Sounds (deferred UI, fields
        // still real so business logic reads sensible values).
        public bool soundEnabled_AlwaysWanted = true, soundEnabled_DirectedCq = true, soundEnabled_Disconnected = true,
            soundEnabled_NewDxcc = true, soundEnabled_NewDxccOnBand = true, soundEnabled_Pota = true,
            soundEnabled_Sota = true, soundEnabled_TxEnabled = false, soundEnabled_WantedAnywhere = true;
        public string soundFile_AlwaysWanted = "chime.wav", soundFile_CallAdded = "blip.wav", soundFile_CallingMe = "dingding.wav",
            soundFile_DirectedCq = "trumpet.wav", soundFile_Disconnected = "dive.wav", soundFile_Logged = "echo.wav",
            soundFile_NewDxcc = "beepbeep.wav", soundFile_NewDxccOnBand = "chime.wav", soundFile_Pota = "trumpet.wav",
            soundFile_Sota = "trumpet.wav", soundFile_TxEnabled = "blip.wav", soundFile_WantedAnywhere = "chime.wav";
        public bool soundEnabled_OppositePeriod = false;
        public string soundFile_OppositePeriod = "";
        public bool soundEnabled_AwardNeeded = false;
        public string soundFile_AwardNeeded = "";
        public bool rawPriorityTags = false;

        // ── Shim-typed fields standing in for WinForms controls (see ViewState.cs) ──
        public CheckState holdCheckBox = new CheckState();
        // Not read by any WsjtxClient*.cs call site (confirmed unused business-logic-wise in
        // the WinForms baseline too -- purely a persisted Options checkbox with no live effect).
        public CheckState skipGridCheckBox = new CheckState();
        public CheckState freqCheckBox = new CheckState();
        public CheckState callCqDxCheckBox = new CheckState();
        public CheckState callDirCqCheckBox = new CheckState();
        public CheckState callNonDirCqCheckBox = new CheckState();
        public CheckState callAddedCheckBox = new CheckState(true);
        public CheckState ignoreNonDxCheckBox = new CheckState();
        public CheckState logEarlyCheckBox = new CheckState();
        public CheckState loggedCheckBox = new CheckState(true);
        public CheckState mycallCheckBox = new CheckState(true);
        public CheckState optimizeCheckBox = new CheckState();
        public CheckState replyDirCqCheckBox = new CheckState();
        public CheckState replyDxCheckBox = new CheckState(true);
        public CheckState replyLocalCheckBox = new CheckState(true);
        public CheckState replyRR73CheckBox = new CheckState(true);
        public CheckState showUsStateCheckBox = new CheckState();
        public CheckState useRR73CheckBox = new CheckState(true);
        public CheckState ignoreWeakSnrCheckBox = new CheckState();
        public CheckState removeOnWeakSnrCheckBox = new CheckState();

        public RadioState anyMsgRadioButton = new RadioState();
        public RadioState cqGridRadioButton = new RadioState();
        public RadioState cqModeButton = new RadioState();
        public RadioState cqOnlyRadioButton = new RadioState();
        public RadioState listenModeButton = new RadioState();

        public TextState directedTextBox = new TextState();
        public TextState alertTextBox = new TextState();
        public TextState exceptTextBox = new TextState();
        public TextState statusText = new TextState("Ready.");

        public ComboState bandComboBox = new ComboState();
        public ComboState periodComboBox = new ComboState();

        public LabelState modeGroupBox = new LabelState();
        public LabelState callCqOptionsButton = new LabelState();
        public LabelState PeriodHelpLabel = new LabelState();
        public LabelState advTx1Label = new LabelState();
        public LabelState advTx2Label = new LabelState();
        public LabelState limitLabel = new LabelState();
        public LabelState periodLabel = new LabelState();
        public LabelState repeatLabel = new LabelState();
        public LabelState verLabel = new LabelState();
        public LabelState verLabel2 = new LabelState();

        public NumericState minSnrNumUpDown = new NumericState { Minimum = -30, Maximum = 20, Value = -24 };
        public NumericState timeoutNumUpDown = new NumericState { Minimum = 1, Maximum = 20, Value = 3 };

        // Generic labelN fields -- WinForms Designer-style numbered labels scattered through
        // Basic/General/Receive tab question text. Kept as a flat set (not an array) to match
        // the original field names exactly.
        public LabelState label1 = new LabelState(), label2 = new LabelState(), label4 = new LabelState(),
            label5 = new LabelState(), label6 = new LabelState(), label7 = new LabelState(),
            label8 = new LabelState(), label9 = new LabelState(), label10 = new LabelState(),
            label11 = new LabelState(), label12 = new LabelState(), label13 = new LabelState(),
            label14 = new LabelState(), label15 = new LabelState(), label16 = new LabelState(),
            label17 = new LabelState(), label18 = new LabelState(), label19 = new LabelState(),
            label20 = new LabelState(), label21 = new LabelState(), label22 = new LabelState(),
            label23 = new LabelState(), label24 = new LabelState(), label25 = new LabelState(),
            label26 = new LabelState(), label27 = new LabelState(), label28 = new LabelState(),
            label29 = new LabelState(), label30 = new LabelState(), label31 = new LabelState(),
            label32 = new LabelState(), label33 = new LabelState(), label34 = new LabelState();

        // ── Headless WinForms controls: satisfy pre-existing call sites verbatim (BeginUpdate/
        // EndUpdate/Items/AccessibleName/Timer.Tick) with zero changes to WsjtxClient*.cs. The
        // REAL, visible lists the operator uses are separate WPF ListBoxes in MainWindow; the
        // Render* methods below populate both from the same items/keys parameters. ──
        public ListBox callListBox = new ListBox();
        public ListBox logListBox = new ListBox();
        public ListBox advTx1ListBox = new ListBox();
        public ListBox advTx2ListBox = new ListBox();
        public ListBox advRawListBox = new ListBox();
        public System.Windows.Forms.Timer initialConnFaultTimer = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer debugHighlightTimer = new System.Windows.Forms.Timer();

        public Controller()
        {
            hotkeyConfig = new HotkeyConfig();
            lookupManager = new LookupManager();
            WireCheckboxCoupling();
        }

        // ── Window-affinity methods WsjtxClient calls on ctrl directly ──
        public void BeginInvoke(Delegate d) => StatusSink?.Dispatch(() => d.DynamicInvoke());
        public void Activate() => StatusSink?.ActivateWindow();
        public void BringToFront() => StatusSink?.ActivateWindow();
        public string Text { get => friendlyName; set { } }

        public void WsjtxSettingConfirmed()
        {
            // Original: a brief visual/audible confirmation that a setting change reached
            // WSJT-X (relevant to the classic UDP path only -- Direct engine mode has no
            // equivalent round-trip to confirm). No-op here; Direct mode is the only
            // production transport now (see UDP-to-Direct parity work), so this was already
            // close to dormant.
        }

        // The real "switch to CQ mode" handler -- called both from the main window's mode
        // toggle and from several business-logic call sites (WsjtxClient.cs/BandAudio.cs/
        // Protocol.cs) that need to force a resync into CALL_CQ mode (slot-analysis timeout,
        // unsupported-mode recovery, etc.).
        public void cqModeButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;
            wsjtxClient.TxModeChanged(WsjtxClient.TxModes.CALL_CQ);
        }

        public void listenModeButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;
            wsjtxClient.TxModeChanged(WsjtxClient.TxModes.LISTEN);
        }

        public void GuideListenMode()
        {
            listenModeButton_Click(null, null);
            periodComboBox.SelectedIndex = (int)WsjtxClient.ListenModeTxPeriods.ANY;
        }

        public void GuideCqMode() => cqModeButton_Click(null, null);

        // Original: appends a callsign to the Except-calls list (Options > Receive/Auto Reply),
        // used when a call gets blocked from auto-queueing.
        public void ExceptTextBoxAdd(string call)
        {
            if (string.IsNullOrWhiteSpace(call)) return;
            var entries = new List<string>((exceptTextBox.Text ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (entries.Contains(call, StringComparer.OrdinalIgnoreCase)) return;
            entries.Add(call);
            exceptTextBox.Text = string.Join(", ", entries);
        }

        public string[] CallDirCqEntries()
        {
            ValidateDirCqTextBox();
            return (directedTextBox.Text ?? "").Trim().ToUpper().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public string[] ReplyDirCqEntries()
        {
            ValidateAlertTextBox();
            return (alertTextBox.Text ?? "").Trim().ToUpper().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public void OnJimmyReachedActive()
        {
            // Original: first-time-active bookkeeping (e.g. clearing a "connecting..." state).
            // StatusSink already reflects real connection state via RenderStatus; nothing
            // additional needed here yet.
        }

        public void ShowUploadStatus(string text, bool sound) => ShowMsg(text, sound);

        public void RefreshLogbookWindowIfOpen()
        {
            // Logbook browsing window is deferred in this pass (see migration report) --
            // logging itself (LogbookDb) is fully live and unaffected.
        }

        public void LoadHrcCache()
        {
            if (wsjtxClient == null) return;
            try
            {
                using (var db = new LogbookDb())
                {
                    HashSet<string> neededStates;
                    HashSet<string> unconfirmedStates;
                    HashSet<int> unconfirmedDxcc;
                    HashSet<int> neededZones;
                    db.LoadHrcCache(out neededStates, out unconfirmedStates, out unconfirmedDxcc, out neededZones);
                    wsjtxClient.hrcNeededStates = neededStates;
                    wsjtxClient.hrcUnconfirmedStates = unconfirmedStates;
                    wsjtxClient.hrcUnconfirmedDxcc = unconfirmedDxcc;
                    wsjtxClient.hrcNeededZones = neededZones;
                }
            }
            catch { }
        }

        public void RefreshStillNeedCache()
        {
            if (wsjtxClient == null) return;

            var tags = new Dictionary<string, WsjtxClient.ActiveAwardTag>();
            foreach (string ruleId in activeAwardRuleIds)
            {
                var def = RuleLibrary.Definitions.FirstOrDefault(d => d.Enabled && d.Id == ruleId);
                if (!RuleEngine.SupportsLiveTag(def)) continue;
                if (!BandAppliesToLiveTag(def.Bands, wsjtxClient.CurrentBandStr)) continue;

                try
                {
                    string band = def.Bands.Count > 0 ? wsjtxClient.CurrentBandStr : null;
                    var result = RuleEngine.EvaluateBand(def, band);
                    if (result.StillNeeded == null) continue;

                    tags[ruleId] = new WsjtxClient.ActiveAwardTag
                    {
                        RuleId = ruleId,
                        RuleName = def.Name,
                        GroupBy = def.GroupBy,
                        Set = new HashSet<string>(result.StillNeeded, StringComparer.OrdinalIgnoreCase),
                    };
                }
                catch { /* skip this rule, keep the others */ }
            }
            wsjtxClient.activeAwardTags = tags;
            wsjtxClient.RefreshQueuedAwardTags();
        }

        public static bool BandAppliesToLiveTag(List<string> defBands, string currentBand)
        {
            if (defBands == null || defBands.Count == 0) return true;
            if (string.IsNullOrEmpty(currentBand)) return false;
            return defBands.Any(b => b.Equals(currentBand, StringComparison.OrdinalIgnoreCase));
        }

        public static int FindPreservedSelectionIndex(List<string> oldKeys, int oldSelectedIndex, List<string> newKeys)
        {
            if (oldKeys == null || newKeys == null) return -1;
            if (oldSelectedIndex < 0 || oldSelectedIndex >= oldKeys.Count) return -1;
            return newKeys.IndexOf(oldKeys[oldSelectedIndex]);
        }

        // ── IJimmyStatusView ──

        private static readonly TimeSpan RepeatStatusAnnounceSuppressWindow = TimeSpan.FromSeconds(3);
        private string _lastAnnouncedStatusText;
        private DateTime _lastAnnouncedStatusTime = DateTime.MinValue;

        // WPF replacement for the WinForms SendKeys.Send("{UP}") + Form.ActiveForm re-announce
        // guard: StatusSink pushes to a dedicated LiveRegion element via AutomationPeer.
        // RaisePropertyChangedEvent (see MainWindow.xaml.cs), the proven technique from the WPF
        // accessibility POC -- not manual speech, a standard WPF/UIA mechanism. No foreground-
        // window guard is needed here (unlike SendKeys, RaiseAutomationEvent only ever touches
        // Jimmy's own element, never any other application), so that whole class of risk is
        // gone, not just relocated.
        public void RenderStatus(string headingText, string statusText, Color foreColor, Color backColor)
        {
            this.statusText.Text = statusText;
            StatusSink?.SetStatusText(headingText, statusText, foreColor, backColor);

            bool isNearImmediateRepeat = statusText == _lastAnnouncedStatusText
                && (DateTime.UtcNow - _lastAnnouncedStatusTime) < RepeatStatusAnnounceSuppressWindow;
            wsjtxClient?.DebugOutput($"{wsjtxClient.Time()} [ANNOUNCE announced={!isNearImmediateRepeat}]{(isNearImmediateRepeat ? " (repeat suppressed)" : "")} '{statusText}'");
            if (isNearImmediateRepeat) return;

            _lastAnnouncedStatusText = statusText;
            _lastAnnouncedStatusTime = DateTime.UtcNow;
            StatusSink?.Announce(statusText);
        }

        public void ShowMessage(string text, bool sound) => ShowMsg(text, sound);

        public void ShowMsg(string text, bool sound)
        {
            // No raw Windows system beep here, ever -- only Jimmy's own configured
            // notification sounds (Options > Sounds) are audible. `sound` kept as a parameter
            // so every existing call site stays valid.
            statusText.Text = text;
            wsjtxClient?.DebugOutput($"{wsjtxClient.Time()} [ANNOUNCE announced=True] '{text}'");
            StatusSink?.Announce(text);
        }

        // ── IJimmyQueueView ──

        private List<string> _callQueueKeys = new List<string>();
        public void RenderCallQueue(string headerText, List<string> items, List<string> keys, List<WsjtxClient.CallCategory> categories, SelectionMode selectionMode)
        {
            bool changed = callListBox.Items.Count != items.Count;
            if (!changed)
                for (int i = 0; i < items.Count; i++)
                    if ((string)callListBox.Items[i] != items[i]) { changed = true; break; }

            int newIndex = -1;
            if (changed)
            {
                int prevIndex = callListBox.SelectedIndex;
                newIndex = FindPreservedSelectionIndex(_callQueueKeys, prevIndex, keys);
                _callQueueKeys = keys;
                callListBox.BeginUpdate();
                try { callListBox.Items.Clear(); callListBox.Items.AddRange(items.ToArray()); }
                finally { callListBox.EndUpdate(); }
                if (newIndex >= 0) callListBox.SelectedIndex = newIndex;
            }

            ListSink?.RenderCallQueue(headerText, items, keys, categories, newIndex);
        }

        private List<string> _rawDecodeKeys = new List<string>();
        public void RenderRawDecodes(List<string> items, List<string> keys, List<WsjtxClient.CallCategory> categories)
        {
            bool changed = advRawListBox.Items.Count != items.Count;
            if (!changed)
                for (int i = 0; i < items.Count; i++)
                    if ((string)advRawListBox.Items[i] != items[i]) { changed = true; break; }
            if (!changed) return;

            _rawDecodeKeys = keys;
            advRawListBox.BeginUpdate();
            try { advRawListBox.Items.Clear(); advRawListBox.Items.AddRange(items.ToArray()); }
            finally { advRawListBox.EndUpdate(); }

            ListSink?.RenderRawDecodes(items, keys, categories);
        }

        private List<string> _tx1Keys = new List<string>();
        private List<string> _tx2Keys = new List<string>();
        public void RenderAdvancedList(bool isTx1Side, string accessibleName, List<string> items, List<string> keys, List<WsjtxClient.CallCategory> categories)
        {
            ListBox lb = isTx1Side ? advTx1ListBox : advTx2ListBox;
            bool changed = lb.Items.Count != items.Count;
            if (!changed)
                for (int i = 0; i < items.Count; i++)
                    if ((string)lb.Items[i] != items[i]) { changed = true; break; }
            if (changed)
            {
                lb.BeginUpdate();
                try { lb.Items.Clear(); lb.Items.AddRange(items.ToArray()); }
                finally { lb.EndUpdate(); }
            }
            if (isTx1Side) _tx1Keys = keys; else _tx2Keys = keys;

            ListSink?.RenderAdvancedList(isTx1Side, accessibleName, items, keys, categories);
        }

        // ── IJimmyLogView ──

        private List<string> _loggedKeys = new List<string>();
        public void RenderLoggedList(string headerText, List<string> items, List<string> keys)
        {
            bool changed = logListBox.Items.Count != items.Count;
            if (!changed)
                for (int i = 0; i < items.Count; i++)
                    if ((string)logListBox.Items[i] != items[i]) { changed = true; break; }
            if (!changed) return;

            _loggedKeys = keys;
            logListBox.BeginUpdate();
            try { logListBox.Items.Clear(); logListBox.Items.AddRange(items.ToArray()); }
            finally { logListBox.EndUpdate(); }

            ListSink?.RenderLoggedList(headerText, items, keys);
        }
    }

    // Small seams MainWindow implements so Controller (plain class, no WPF dependency of its
    // own beyond these two interfaces) can drive the real UI -- keeps Controller free of any
    // direct reference to System.Windows.Controls, matching "presentation layer stays
    // replaceable" (a Raspberry Pi/Linux front end would implement these two interfaces
    // instead, same as this WPF one does).
    public interface IWpfStatusSink
    {
        void SetStatusText(string headingText, string statusText, Color foreColor, Color backColor);
        void Announce(string text);
        void Dispatch(Action action);
        void ActivateWindow();
    }

    public interface IWpfListSink
    {
        void RenderCallQueue(string headerText, List<string> items, List<string> keys, List<WsjtxClient.CallCategory> categories, int preservedIndex);
        void RenderRawDecodes(List<string> items, List<string> keys, List<WsjtxClient.CallCategory> categories);
        void RenderAdvancedList(bool isTx1Side, string accessibleName, List<string> items, List<string> keys, List<WsjtxClient.CallCategory> categories);
        void RenderLoggedList(string headerText, List<string> items, List<string> keys);
        void RenderSpotWatchList(List<string> items, List<string> keys);
    }
}
