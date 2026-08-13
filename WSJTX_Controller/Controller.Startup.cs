using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;

namespace WSJTX_Controller
{
    // WPF migration: settings load, engine/radio startup, and the timer pump ported from the
    // WinForms Controller's Form_Load/ApplyEngineMode/ApplyRadioSettings. Window-frame concerns
    // (this.Location/Height/WindowState, dynamically-added buttons) belonged to the WinForms Form
    // and are handled by MainWindow.xaml/.xaml.cs instead -- everything here is the same business-
    // logic initialization sequence, unchanged.
    public partial class Controller
    {
        private IniFile iniFile;
        private bool formLoaded;
        public bool FormLoaded => formLoaded;

        public System.Windows.Forms.Timer mainLoopTimer = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer statusMsgTimer = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer radioPollTimer = new System.Windows.Forms.Timer();

        private bool? _lastRadioPollOk;
        private bool _swrOverThreshold;
        private int _nativeEngineAutoRestartCount;
        private DateTime _nativeEngineAutoRestartWindowStart = DateTime.MinValue;
        private const int MaxNativeEngineAutoRestartsPerWindow = 5;
        private static readonly TimeSpan NativeEngineAutoRestartWindow = TimeSpan.FromMinutes(5);

        // Called once by MainWindow after StatusSink/ListSink are wired up.
        public void LoadSettingsAndStart()
        {
            string pgmName = Assembly.GetExecutingAssembly().GetName().Name;
            string path = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\{pgmName}";
            string pathFileNameExt = path + "\\" + pgmName + ".ini";
            List<string> parsedCallWaitingRowOrder = null;
            List<string> parsedRawDecodeRowOrder = null;

            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                iniFile = new IniFile(pathFileNameExt);
                hotkeyConfig.LoadFromIni(iniFile);
                try
                {
                    parsedCallWaitingRowOrder = ParseRowOrder(iniFile.Read("callWaitingRowOrder"), RowDisplayOrderDefaults.CallWaiting);
                    parsedRawDecodeRowOrder = ParseRowOrder(iniFile.Read("rawDecodeRowOrder"), RowDisplayOrderDefaults.RawDecode);
                }
                catch { /* swallow parse errors, leave both null */ }
            }
            catch (Exception ex)
            {
                StatusSink?.SetStatusText("Startup", "Unable to create settings file: " + pathFileNameExt + " -- continuing with defaults. " + ex.Message, Color.Red, Color.White);
            }

            string ipAddrStr = "127.0.0.1";
            int port = 2237;
            bool multicast = true;
            bool debug = false;
            bool diagLog = false;
            WsjtxClient.TxModes txMode = WsjtxClient.TxModes.LISTEN;
            int offsetHiLimit = -1, offsetLoLimit = -1;
            bool useRR73 = false;
            string myContinent = null;
            bool newOnBand = true;
            bool cmdPrompts = true;
            bool usePskReporter = true;
            friendlyName = pgmName;

            periodComboBox.SelectedIndex = 2;
            int rankMethodIdx = (int)WsjtxClient.RankMethods.MOST_RECENT;
            freqCheckBox.Checked = false;
            string rankOrderStr = null, rankBeamStr = null, categoryWeightsStr = null,
                callingPrioritiesStr = null, categoryDisabledStr = null, wantedCallsStr = null, spotWatchCallsStr = null;

            if (iniFile == null || !iniFile.KeyExists("firstRun"))
            {
                timeoutNumUpDown.Value = 3;
                loggedCheckBox.Checked = true;
                mycallCheckBox.Checked = true;
                replyDxCheckBox.Checked = true;
                replyLocalCheckBox.Checked = true;
                replyRR73CheckBox.Checked = true;
                optimizeCheckBox.Checked = true;
                callNonDirCqCheckBox.Checked = true;
                showUsStateCheckBox.Checked = true;
                bandComboBox.SelectedIndex = 1;
            }
            else
            {
                debug = iniFile.Read("debug") == "True";
                if (IPAddress.TryParse(iniFile.Read("ipAddress"), out var savedIp)) ipAddrStr = savedIp.ToString();
                if (int.TryParse(iniFile.Read("port"), out int savedPort) && savedPort > 0) port = savedPort;
                if (iniFile.KeyExists("multicast")) multicast = iniFile.Read("multicast") == "True";
                int i;
                int.TryParse(iniFile.Read("timeout"), out i);
                timeoutNumUpDown.Value = i > 0 ? i : 3;
                directedTextBox.Text = iniFile.Read("directeds");
                callDirCqCheckBox.Checked = iniFile.Read("useDirected") == "True";
                if (iniFile.KeyExists("directedCqLockedEntry")) directedCqLockedEntry = iniFile.Read("directedCqLockedEntry");
                mycallCheckBox.Checked = iniFile.Read("playMyCall") != "False";
                loggedCheckBox.Checked = iniFile.Read("playLogged") != "False";
                callAddedCheckBox.Checked = iniFile.Read("playCallAdded") != "False";
                replyDirCqCheckBox.Checked = iniFile.Read("useAlertDirected") == "True";
                logEarlyCheckBox.Checked = iniFile.Read("logEarly") == "True";
                alwaysOnTop = iniFile.Read("alwaysOnTop") == "True";
                useRR73 = iniFile.Read("useRR73") == "True";
                useRR73CheckBox.Checked = useRR73;
                replyDxCheckBox.Checked = iniFile.Read("enableReplyDx") != "False";
                diagLog = iniFile.Read("diagLog") == "True";
                freqCheckBox.Checked = iniFile.Read("bestOffset") == "True";
                replyRR73CheckBox.Checked = iniFile.Read("replyRR73") == "True";
                cmdPrompts = iniFile.Read("cmdPrompts") != "False";

                if (iniFile.KeyExists("offsetHiLimit")) int.TryParse(iniFile.Read("offsetHiLimit"), out offsetHiLimit);
                if (iniFile.KeyExists("offsetLoLimit")) int.TryParse(iniFile.Read("offsetLoLimit"), out offsetLoLimit);
                replyLocalCheckBox.Checked = iniFile.Read("enableReplyLocal") != "False";
                optimizeCheckBox.Checked = iniFile.Read("optimizeTx") == "True";
                exceptTextBox.Text = iniFile.Read("exceptCalls");
                callCqDxCheckBox.Checked = iniFile.Read("callCqDx") == "True";
                ignoreNonDxCheckBox.Checked = iniFile.Read("ignoreNonDx") == "True";
                callNonDirCqCheckBox.Checked = iniFile.Read("callNonDirCq") == "True";
                cqOnlyRadioButton.Checked = iniFile.Read("cqOnly") != "False";
                newOnBand = iniFile.Read("newOnBand") != "False";
                bandComboBox.SelectedIndex = newOnBand ? 1 : 0;
                if (iniFile.KeyExists("myContinent")) myContinent = iniFile.Read("myContinent");
                if (iniFile.KeyExists("useClassificationEngine")) ClassificationCutover.UseClassificationEngine = iniFile.Read("useClassificationEngine") != "False";
                if (iniFile.KeyExists("logClassificationParityMismatches")) ClassificationParityLogger.Enabled = iniFile.Read("logClassificationParityMismatches") == "True";
                NativeEngine.LoadFromIni(iniFile);
                Radio.LoadFromIni(iniFile);
                Decode.LoadFromIni(iniFile);
                Frequencies.LoadFromIni(iniFile);
                Notifications.LoadFromIni(iniFile);
                if (iniFile.KeyExists("rankMethod")) int.TryParse(iniFile.Read("rankMethod"), out rankMethodIdx);
                if (iniFile.KeyExists("rankOrder")) rankOrderStr = iniFile.Read("rankOrder");
                if (iniFile.KeyExists("rankBeam")) rankBeamStr = iniFile.Read("rankBeam");
                if (iniFile.KeyExists("categoryWeights")) categoryWeightsStr = iniFile.Read("categoryWeights");
                if (iniFile.KeyExists("callingPriorities")) callingPrioritiesStr = iniFile.Read("callingPriorities");
                else if (iniFile.KeyExists("categoryDisabled")) categoryDisabledStr = iniFile.Read("categoryDisabled");
                if (iniFile.KeyExists("wantedCalls")) wantedCallsStr = iniFile.Read("wantedCalls");
                if (iniFile.KeyExists("spotWatchCalls")) spotWatchCallsStr = iniFile.Read("spotWatchCalls");
                if (iniFile.KeyExists("wantedCallAnywhereEnabled")) wantedCallAnywhereEnabled = iniFile.Read("wantedCallAnywhereEnabled") == "True";
                rawPriorityTags = iniFile.Read("rawPriorityTags") == "True";
                cqGridRadioButton.Checked = iniFile.Read("cqGrid") == "True";
                anyMsgRadioButton.Checked = iniFile.Read("anyMsg") == "True";
                if (iniFile.KeyExists("txPeriodIdx")) { int.TryParse(iniFile.Read("txPeriodIdx"), out i); periodComboBox.SelectedIndex = i; }
                usePskReporter = iniFile.Read("usePskReporter") != "False";
                showUsStateCheckBox.Checked = iniFile.Read("showUsState") == "True";
                Settings.LoadFromIni(iniFile);
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
                rawShowDistAz = iniFile.Read("rawShowDistAz") == "True";
                rawOnlyCallsigns = iniFile.Read("rawOnlyCallsigns") == "True";
                rawOnlyUnworked = iniFile.Read("rawOnlyUnworked") == "True";
                rawOnlyRanked = iniFile.Read("rawOnlyRanked") == "True";
                rawNewestFirst = iniFile.Read("rawNewestFirst") == "True";
                int rawMax;
                if (iniFile.KeyExists("rawMaxRows") && int.TryParse(iniFile.Read("rawMaxRows"), out rawMax) && rawMax >= 10 && rawMax <= 5000) rawMaxRows = rawMax;
                int maxQueued;
                if (iniFile.KeyExists("maxQueuedCalls") && int.TryParse(iniFile.Read("maxQueuedCalls"), out maxQueued) && maxQueued >= 4 && maxQueued <= 100) maxQueuedCallsBase = maxQueued;
                int maxAgePeriods;
                if (iniFile.KeyExists("maxCallQueueAgePeriods") && int.TryParse(iniFile.Read("maxCallQueueAgePeriods"), out maxAgePeriods) && maxAgePeriods >= 4 && maxAgePeriods <= 200) maxCallQueueAgePeriods = maxAgePeriods;
                int statusBatchMs;
                if (iniFile.KeyExists("statusBatchDelayMs") && int.TryParse(iniFile.Read("statusBatchDelayMs"), out statusBatchMs) && statusBatchMs >= 0 && statusBatchMs <= 5000) statusBatchDelayMs = statusBatchMs;
                keepTransmitListDuringTx = iniFile.Read("keepTransmitListDuringTx") == "True";
                keepListPositionDuringRefresh = iniFile.Read("keepListPositionDuringRefresh") == "True";
                moveFocusToStatusOnCallSelect = iniFile.Read("moveFocusToStatusOnCallSelect") == "True";
                checkForUpdatesOnStartup = iniFile.Read("checkForUpdatesOnStartup") == "True";
                if (iniFile.KeyExists("soundFile_CallAdded")) soundFile_CallAdded = iniFile.Read("soundFile_CallAdded");
                if (iniFile.KeyExists("soundFile_CallingMe")) soundFile_CallingMe = iniFile.Read("soundFile_CallingMe");
                if (iniFile.KeyExists("soundFile_Logged")) soundFile_Logged = iniFile.Read("soundFile_Logged");
                if (iniFile.KeyExists("soundEnabled_TxEnabled")) soundEnabled_TxEnabled = iniFile.Read("soundEnabled_TxEnabled") != "False";
                if (iniFile.KeyExists("soundFile_TxEnabled")) soundFile_TxEnabled = iniFile.Read("soundFile_TxEnabled");
                if (iniFile.KeyExists("soundEnabled_Disconnected")) soundEnabled_Disconnected = iniFile.Read("soundEnabled_Disconnected") != "False";
                if (iniFile.KeyExists("soundFile_Disconnected")) soundFile_Disconnected = iniFile.Read("soundFile_Disconnected");
                if (iniFile.KeyExists("soundEnabled_NewDxcc")) soundEnabled_NewDxcc = iniFile.Read("soundEnabled_NewDxcc") == "True";
                if (iniFile.KeyExists("soundFile_NewDxcc")) soundFile_NewDxcc = iniFile.Read("soundFile_NewDxcc");
                if (iniFile.KeyExists("soundEnabled_NewDxccOnBand")) soundEnabled_NewDxccOnBand = iniFile.Read("soundEnabled_NewDxccOnBand") == "True";
                if (iniFile.KeyExists("soundFile_NewDxccOnBand")) soundFile_NewDxccOnBand = iniFile.Read("soundFile_NewDxccOnBand");
                if (iniFile.KeyExists("soundEnabled_AlwaysWanted")) soundEnabled_AlwaysWanted = iniFile.Read("soundEnabled_AlwaysWanted") == "True";
                if (iniFile.KeyExists("soundFile_AlwaysWanted")) soundFile_AlwaysWanted = iniFile.Read("soundFile_AlwaysWanted");
                if (iniFile.KeyExists("soundEnabled_DirectedCq")) soundEnabled_DirectedCq = iniFile.Read("soundEnabled_DirectedCq") == "True";
                if (iniFile.KeyExists("soundFile_DirectedCq")) soundFile_DirectedCq = iniFile.Read("soundFile_DirectedCq");
                if (iniFile.KeyExists("soundEnabled_Pota")) soundEnabled_Pota = iniFile.Read("soundEnabled_Pota") == "True";
                if (iniFile.KeyExists("soundFile_Pota")) soundFile_Pota = iniFile.Read("soundFile_Pota");
                if (iniFile.KeyExists("soundEnabled_Sota")) soundEnabled_Sota = iniFile.Read("soundEnabled_Sota") == "True";
                if (iniFile.KeyExists("soundFile_Sota")) soundFile_Sota = iniFile.Read("soundFile_Sota");
                if (iniFile.KeyExists("soundEnabled_WantedAnywhere")) soundEnabled_WantedAnywhere = iniFile.Read("soundEnabled_WantedAnywhere") == "True";
                if (iniFile.KeyExists("soundFile_WantedAnywhere")) soundFile_WantedAnywhere = iniFile.Read("soundFile_WantedAnywhere");
                if (iniFile.KeyExists("soundEnabled_OppositePeriod")) soundEnabled_OppositePeriod = iniFile.Read("soundEnabled_OppositePeriod") == "True";
                if (iniFile.KeyExists("soundFile_OppositePeriod")) soundFile_OppositePeriod = iniFile.Read("soundFile_OppositePeriod");
                if (iniFile.KeyExists("soundEnabled_AwardNeeded")) soundEnabled_AwardNeeded = iniFile.Read("soundEnabled_AwardNeeded") == "True";
                if (iniFile.KeyExists("soundFile_AwardNeeded")) soundFile_AwardNeeded = iniFile.Read("soundFile_AwardNeeded");
                if (iniFile.KeyExists("soundsEnabled")) soundsEnabled = iniFile.Read("soundsEnabled") != "False";
                if (iniFile.KeyExists("useLookupData")) useLookupData = iniFile.Read("useLookupData") == "True";
                if (iniFile.KeyExists("qrzEnabled")) qrzEnabled = iniFile.Read("qrzEnabled") == "True";
                if (iniFile.KeyExists("qrzUsername")) qrzUsername = iniFile.Read("qrzUsername");
                if (iniFile.KeyExists("qrzPassword")) qrzPassword = CredentialProtector.Unprotect(iniFile.Read("qrzPassword"));
                int qrzcd; if (iniFile.KeyExists("qrzCacheDays") && int.TryParse(iniFile.Read("qrzCacheDays"), out qrzcd) && qrzcd >= 1) qrzCacheDays = qrzcd;
                int qrzpol; if (iniFile.KeyExists("qrzLookupPolicy") && int.TryParse(iniFile.Read("qrzLookupPolicy"), out qrzpol)) qrzLookupPolicy = (QrzLookupPolicy)qrzpol;
                int qrzint; if (iniFile.KeyExists("qrzMinIntervalSeconds") && int.TryParse(iniFile.Read("qrzMinIntervalSeconds"), out qrzint) && qrzint >= 5) qrzMinIntervalSeconds = qrzint;
                if (iniFile.KeyExists("lotwEnabled")) lotwEnabled = iniFile.Read("lotwEnabled") == "True";
                if (iniFile.KeyExists("lotwBoostEnabled")) lotwBoostEnabled = iniFile.Read("lotwBoostEnabled") == "True";
                int lotwd; if (iniFile.KeyExists("lotwRefreshDays") && int.TryParse(iniFile.Read("lotwRefreshDays"), out lotwd) && lotwd >= 1) lotwRefreshDays = lotwd;
                int clgd; if (iniFile.KeyExists("clubLogRefreshDays") && int.TryParse(iniFile.Read("clubLogRefreshDays"), out clgd) && clgd >= 1) clubLogRefreshDays = clgd;
                if (iniFile.KeyExists("fccUlsEnabled")) fccUlsEnabled = iniFile.Read("fccUlsEnabled") == "True";
                int fccd; if (iniFile.KeyExists("fccUlsRefreshDays") && int.TryParse(iniFile.Read("fccUlsRefreshDays"), out fccd) && fccd >= 1) fccUlsRefreshDays = fccd;
                if (iniFile.KeyExists("qrzLogbookApiKey")) qrzLogbookApiKey = CredentialProtector.Unprotect(iniFile.Read("qrzLogbookApiKey"));
                if (iniFile.KeyExists("qrzUploadEnabled")) qrzUploadEnabled = iniFile.Read("qrzUploadEnabled") == "True";
                if (iniFile.KeyExists("qrzUploadRealtime")) qrzUploadRealtime = iniFile.Read("qrzUploadRealtime") == "True";
                if (iniFile.KeyExists("lotwUploadEnabled")) lotwUploadEnabled = iniFile.Read("lotwUploadEnabled") == "True";
                if (iniFile.KeyExists("tqslStationLocation")) tqslStationLocation = iniFile.Read("tqslStationLocation") ?? "";
                if (iniFile.KeyExists("clubLogUploadEnabled")) clubLogUploadEnabled = iniFile.Read("clubLogUploadEnabled") == "True";
                if (iniFile.KeyExists("clubLogUploadRealtime")) clubLogUploadRealtime = iniFile.Read("clubLogUploadRealtime") == "True";
                if (iniFile.KeyExists("clubLogUploadEmail")) clubLogUploadEmail = iniFile.Read("clubLogUploadEmail") ?? "";
                if (iniFile.KeyExists("clubLogUploadPassword")) clubLogUploadPassword = CredentialProtector.Unprotect(iniFile.Read("clubLogUploadPassword"));
                if (iniFile.KeyExists("clubLogUploadCallsign")) clubLogUploadCallsign = iniFile.Read("clubLogUploadCallsign") ?? "";
                if (iniFile.KeyExists("hrdLogUploadEnabled")) hrdLogUploadEnabled = iniFile.Read("hrdLogUploadEnabled") == "True";
                if (iniFile.KeyExists("hrdLogUploadRealtime")) hrdLogUploadRealtime = iniFile.Read("hrdLogUploadRealtime") == "True";
                if (iniFile.KeyExists("hrdLogUploadCode")) hrdLogUploadCode = CredentialProtector.Unprotect(iniFile.Read("hrdLogUploadCode"));
                if (iniFile.KeyExists("hrdLogUploadCallsign")) hrdLogUploadCallsign = iniFile.Read("hrdLogUploadCallsign") ?? "";
                if (iniFile.KeyExists("activeAwardRuleIds")) activeAwardRuleIds = ParseActiveAwardRuleIds(iniFile.Read("activeAwardRuleIds")).ToList();
            }

            txMode = WsjtxClient.TxModes.LISTEN;

            if (directedTextBox.Text == "") callDirCqCheckBox.Checked = false;
            directedTextBox.Enabled = callDirCqCheckBox.Checked;
            if (exceptTextBox.Text == null) exceptTextBox.Text = "";

            callCqOptionsButton.Text = "Call CQ options...";

            wsjtxClient = new WsjtxClient(this, IPAddress.Parse(ipAddrStr), port, multicast, debug, diagLog, txMode);
            if (parsedCallWaitingRowOrder != null) wsjtxClient.callWaitingRowOrderFields = parsedCallWaitingRowOrder;
            if (parsedRawDecodeRowOrder != null) wsjtxClient.rawDecodeRowOrderFields = parsedRawDecodeRowOrder;
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
            wsjtxClient.ApplySortOrder(ParseRankOrder(rankOrderStr, rankMethodIdx), ParseRankBeam(rankBeamStr, rankMethodIdx));
            wsjtxClient.ApplyCategoryWeights(ParseCategoryWeights(categoryWeightsStr));
            wsjtxClient.ApplyCallingPriorities(ParseCallingPriorities(callingPrioritiesStr, categoryDisabledStr));
            if (!wsjtxClient.Ranker.callingEnabled.Contains(WsjtxClient.CallCategory.DEFAULT)
                && (replyDxCheckBox.Checked || replyLocalCheckBox.Checked))
            {
                wsjtxClient.Ranker.callingEnabled.Add(WsjtxClient.CallCategory.DEFAULT);
                iniFile?.Write("callingPriorities", FormatCallingPriorities(wsjtxClient.Ranker.callingEnabled));
            }
            if (!string.IsNullOrWhiteSpace(callingPrioritiesStr)
                && !wsjtxClient.Ranker.callingEnabled.Contains(WsjtxClient.CallCategory.STILL_NEEDED))
            {
                wsjtxClient.Ranker.callingEnabled.Add(WsjtxClient.CallCategory.STILL_NEEDED);
                iniFile?.Write("callingPriorities", FormatCallingPriorities(wsjtxClient.Ranker.callingEnabled));
            }
            wsjtxClient.ApplyWantedCalls(ParseWantedCalls(wantedCallsStr));
            wsjtxClient.ApplySpotWatchCalls(ParseSpotWatchCalls(spotWatchCallsStr));

            // Order matters under HamlibRigctld: ApplyEngineMode() launches the engine host,
            // which owns and spawns the real rigctld; ApplyRadioSettings() only ever connects.
            ApplyEngineMode();
            ApplyRadioSettings();
            wsjtxClient.rawPriorityTags = rawPriorityTags;
            wsjtxClient.cmdPrompts = cmdPrompts;
            wsjtxClient.usePskReporter = usePskReporter;

            lookupManager.Initialize(
                useLookupData,
                qrzEnabled, qrzUsername, qrzPassword, qrzCacheDays,
                lotwEnabled, lotwRefreshDays,
                ClubLogAppKey.Resolve(), clubLogRefreshDays,
                fccUlsEnabled,
                qrzLookupPolicy, qrzMinIntervalSeconds);
            wsjtxClient.lookupManager = lookupManager;
            wsjtxClient.lotwBoostEnabled = lotwBoostEnabled;
            BackfillMissingStates();
            LoadHrcCache();
            lookupManager.OnLookupCompleted = () => BeginInvoke(new Action(() => wsjtxClient.RefreshQueueDisplay()));
            lookupManager.StartBackgroundRefreshIfNeeded(lotwRefreshDays, clubLogRefreshDays, fccUlsRefreshDays);

            RuleLibrary.ClubLog = lookupManager.ClubLog;
            try { RuleLibrary.Load(); } catch { }
            RefreshStillNeedCache();

            mainLoopTimer.Interval = 10;
            mainLoopTimer.Tick += (s, e) => { if (mainLoopTimer != null) wsjtxClient.UdpLoop(); };
            mainLoopTimer.Start();

            statusMsgTimer.Tick += (s, e) => { statusMsgTimer.Stop(); wsjtxClient.UpdateCallInProg(); };

            radioPollTimer.Tick += RadioPollTimer_Tick;

            wsjtxClient.UpdateModeVisible();
            wsjtxClient.UpdateModeSelection();
            SyncCqIntentFromMode();

            formLoaded = true;
        }

        private void RadioPollTimer_Tick(object sender, EventArgs e)
        {
            if (rigctldClient == null) return;
            lastRadioStatus = rigctldClient.PollOnce();

            if (_lastRadioPollOk != lastRadioStatus.Ok)
            {
                if (lastRadioStatus.Ok)
                {
                    if (_lastRadioPollOk == false) ShowMessage("Radio CAT link OK", false);
                }
                else
                {
                    wsjtxClient?.Notify?.Publish(new ErrorWarningEvent(ErrorSeverity.Error, "Radio CAT link lost", lastRadioStatus.LastError ?? "no response"));
                }
                _lastRadioPollOk = lastRadioStatus.Ok;
            }

            bool swrOver = Radio.HaltTxOnHighSwr && lastRadioStatus.Ok && lastRadioStatus.Swr.HasValue
                           && lastRadioStatus.Swr.Value > Radio.SwrHaltThreshold;
            if (swrOver && !_swrOverThreshold)
            {
                wsjtxClient?.HaltTx();
                ShowMessage($"Tx halted: SWR {lastRadioStatus.Swr.Value:F1} exceeds threshold {Radio.SwrHaltThreshold:F1}", true);
            }
            _swrOverThreshold = swrOver;
        }

        public void ApplyEngineMode()
        {
            int jimmyPort = wsjtxClient?.port > 0 ? wsjtxClient.port : 2237;
            if (!TestModeGuard.IsTestMode)
            {
                wsjtxClient?.DisconnectDirectEngine();
                wsjtxClient?.ConnectDirectEngine(NativeEngine.MyCall, NativeEngine.MyGrid);
            }
            else
            {
                wsjtxClient?.DisconnectDirectEngine();
                wsjtxClient?.ConnectNativeEngine(IPAddress.Parse("127.0.0.1"), jimmyPort);
            }

            nativeEngineClient?.Dispose();
            nativeEngineClient = null;
            if (TestModeGuard.IsTestMode) return;

            var client = new NativeEngineClient();
            nativeEngineClient = client;
            string myCall = NativeEngine.MyCall, myGrid = NativeEngine.MyGrid;
            string inDevice = NativeEngine.AudioInputDevice, outDevice = NativeEngine.AudioOutputDevice;
            RadioSettings radioSnapshot = Radio;
            DecodeSettings decodeSnapshot = Decode;
            WsjtxClient wsjtx = wsjtxClient;

            System.Threading.Tasks.Task.Run(() =>
            {
                bool ok = client.Launch(myCall, myGrid, inDevice, jimmyPort, outDevice, radioSnapshot,
                    msg => wsjtx?.DebugOutput(msg),
                    () => BeginInvoke(new Action(() => OnNativeEngineUnexpectedExit(client))),
                    decodeSnapshot, wsjtx != null && wsjtx.usePskReporter);
                if (!ok && nativeEngineClient == client)
                    BeginInvoke(new Action(() => ShowMessage($"Native engine: {client.LastError}", false)));
            });
        }

        public void ApplyRadioSettings()
        {
            radioPollTimer.Stop();

            if (TestModeGuard.IsTestMode)
            {
                rigctldClient?.Dispose();
                rigctldClient = null;
                lastRadioStatus = null;
                return;
            }

            if (Radio.Mode != RadioControlMode.HamlibRigctld)
            {
                rigctldClient?.Dispose();
                rigctldClient = null;
                lastRadioStatus = null;
                return;
            }

            rigctldClient?.Dispose();
            rigctldClient = new RigctldClient(
                Radio.UseExternalRigctld ? Radio.RigctldHost : "127.0.0.1",
                Radio.RigctldPort);

            ScheduleRigConnectKick(rigctldClient);

            radioPollTimer.Interval = Math.Max(200, Radio.PollIntervalMs);
            if (Radio.PollEnabled) radioPollTimer.Start();
        }

        private void ScheduleRigConnectKick(RigctldClient client)
        {
            var kickTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            kickTimer.Tick += (s, e) =>
            {
                kickTimer.Stop();
                kickTimer.Dispose();
                if (client != rigctldClient)
                {
                    wsjtxClient?.DebugOutput("[RIG-KICK] skipped -- superseded by a newer ApplyRadioSettings() call");
                    return;
                }
                try
                {
                    var status = client.PollOnce();
                    wsjtxClient?.DebugOutput($"[RIG-KICK] poll ok:{status.Ok} freq:{status.FrequencyHz} err:'{status.LastError}'");
                    bool okSplit = client.SetSplit(Radio.SplitMode == RadioSplitMode.Rig);
                    wsjtxClient?.DebugOutput($"[RIG-KICK] split={Radio.SplitMode}: ok:{okSplit}");
                    if (client.GetMode(out string curMode, out int curPassband) && !string.IsNullOrWhiteSpace(curMode))
                    {
                        bool okModeNudge = client.SetMode("FM", 0);
                        bool okModeRestore = client.SetMode(curMode, curPassband);
                        wsjtxClient?.DebugOutput($"[RIG-KICK] mode nudge FM->{curMode}: nudge_ok:{okModeNudge} restore_ok:{okModeRestore}");
                    }
                }
                catch (Exception ex)
                {
                    wsjtxClient?.DebugOutput($"[RIG-KICK] EXCEPTION: {ex.Message}");
                }
            };
            kickTimer.Start();
        }

        private void OnNativeEngineUnexpectedExit(NativeEngineClient exitedClient)
        {
            if (nativeEngineClient != exitedClient) return;

            DateTime now = DateTime.UtcNow;
            if (now - _nativeEngineAutoRestartWindowStart > NativeEngineAutoRestartWindow)
            {
                _nativeEngineAutoRestartWindowStart = now;
                _nativeEngineAutoRestartCount = 0;
            }
            _nativeEngineAutoRestartCount++;

            if (_nativeEngineAutoRestartCount > MaxNativeEngineAutoRestartsPerWindow)
            {
                ShowMessage("Native engine host stopped unexpectedly -- gave up auto-restarting after repeated crashes. Check Options > Radio/Decode Engine and try again.", true);
                return;
            }

            ShowMessage($"Native engine host stopped unexpectedly -- restarting ({_nativeEngineAutoRestartCount}/{MaxNativeEngineAutoRestartsPerWindow})...", true);
            var restartTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            restartTimer.Tick += (s, e) =>
            {
                restartTimer.Stop();
                restartTimer.Dispose();
                if (nativeEngineClient == exitedClient) ApplyEngineMode();
            };
            restartTimer.Start();
        }

        // Cross-checkbox coupling rules ported from the WinForms CheckedChanged handlers.
        // Wired via CheckState.PropertyChanged (fires only on an actual value change, same as
        // WinForms' CheckedChanged) so the business rules apply identically regardless of
        // whether the change came from the operator clicking a WPF CheckBox or from a saved
        // setting being loaded here in the constructor/LoadSettingsAndStart.
        private void WireCheckboxCoupling()
        {
            callCqDxCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(CheckState.Checked)) return;
                ignoreNonDxCheckBox.Enabled = callCqDxCheckBox.Checked;
                if ((callDirCqCheckBox.Checked || callNonDirCqCheckBox.Checked || replyDirCqCheckBox.Checked
                     || replyDxCheckBox.Checked || replyLocalCheckBox.Checked) && callCqDxCheckBox.Checked)
                    ignoreNonDxCheckBox.Checked = false;
                ValidateDirCqTextBox();
                if (!callCqDxCheckBox.Checked && !callDirCqCheckBox.Checked && !callNonDirCqCheckBox.Checked)
                    callNonDirCqCheckBox.Checked = true;
                SyncCqIntentFromCheckboxes();
                if (formLoaded) wsjtxClient.WsjtxSettingChanged();
            };

            callNonDirCqCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(CheckState.Checked)) return;
                if (callNonDirCqCheckBox.Checked)
                {
                    if (callCqDxCheckBox.Checked) ignoreNonDxCheckBox.Checked = false;
                }
                else
                {
                    ValidateDirCqTextBox();
                    if (!callCqDxCheckBox.Checked && !callDirCqCheckBox.Checked) callNonDirCqCheckBox.Checked = true;
                }
                if (formLoaded) wsjtxClient.WsjtxSettingChanged();
                SyncCqIntentFromCheckboxes();
            };

            callDirCqCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(CheckState.Checked)) return;
                if (!formLoaded) return;
                directedTextBox.Enabled = callDirCqCheckBox.Checked;
                if (callDirCqCheckBox.Checked)
                {
                    if (callCqDxCheckBox.Checked) ignoreNonDxCheckBox.Checked = false;
                }
                else if (!callCqDxCheckBox.Checked)
                {
                    callNonDirCqCheckBox.Checked = true;
                }
                wsjtxClient.WsjtxSettingChanged();
            };

            ignoreNonDxCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(CheckState.Checked)) return;
                if (!ignoreNonDxCheckBox.Checked) return;
                callDirCqCheckBox.Checked = false;
                callNonDirCqCheckBox.Checked = false;
                replyDirCqCheckBox.Checked = false;
                replyLocalCheckBox.Checked = false;
                replyDxCheckBox.Checked = false;
            };

            replyLocalCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(CheckState.Checked)) return;
                if (replyLocalCheckBox.Checked) ignoreNonDxCheckBox.Checked = false;
                UpdateCqNewOnBand();
                CheckManualSelection();
            };

            replyDxCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(CheckState.Checked)) return;
                if (replyDxCheckBox.Checked) ignoreNonDxCheckBox.Checked = false;
                UpdateCqNewOnBand();
                CheckManualSelection();
            };

            mycallCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CheckState.Checked) && formLoaded && mycallCheckBox.Checked)
                    wsjtxClient.Sounds.PlaySoundEvent(true, soundFile_CallingMe);
            };
            loggedCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CheckState.Checked) && formLoaded && loggedCheckBox.Checked)
                    wsjtxClient.Sounds.PlaySoundEvent(true, soundFile_Logged);
            };
            callAddedCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CheckState.Checked) && formLoaded && callAddedCheckBox.Checked)
                    wsjtxClient.Sounds.PlaySoundEvent(true, soundFile_CallAdded);
            };
            useRR73CheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(CheckState.Checked) || !formLoaded) return;
                wsjtxClient.useRR73 = useRR73CheckBox.Checked;
                wsjtxClient.WsjtxSettingChanged();
            };
            optimizeCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CheckState.Checked) && formLoaded) wsjtxClient.TxRepeatChanged();
            };
            holdCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CheckState.Checked) && formLoaded) wsjtxClient.HoldCheckBoxChanged();
            };
            freqCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(CheckState.Checked) || !formLoaded) return;
                wsjtxClient.WsjtxSettingChanged();
                wsjtxClient.AutoFreqChanged(freqCheckBox.Checked, false);
            };
            replyRR73CheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CheckState.Checked) && formLoaded) wsjtxClient.ReplyRR73Changed(replyRR73CheckBox.Checked);
            };
            replyDirCqCheckBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(CheckState.Checked)) return;
                alertTextBox.Enabled = replyDirCqCheckBox.Checked;
                if (formLoaded) wsjtxClient.WsjtxSettingChanged();
            };
            alertTextBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(TextState.Text)) return;
                if (alertTextBox.Text == "") replyDirCqCheckBox.Checked = false;
                if (formLoaded) wsjtxClient.WsjtxSettingChanged();
            };

            directedTextBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(TextState.Text)) return;
                if (ignoreDirectedChange) { ignoreDirectedChange = false; return; }
                if (directedTextBox.Text == "") callDirCqCheckBox.Checked = false;
                if (formLoaded) wsjtxClient.WsjtxSettingChanged();
            };
            timeoutNumUpDown.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(NumericState.Value) || !formLoaded) return;
                if (timeoutNumUpDown.Value < 1) timeoutNumUpDown.Value = 1;
                if (timeoutNumUpDown.Value > 20) timeoutNumUpDown.Value = 20;
                UpdateTxLabel();
                wsjtxClient.TxRepeatChanged();
            };
            periodComboBox.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ComboState.SelectedIndex) && formLoaded) wsjtxClient.TxPeriodIdxChanged(periodComboBox.SelectedIndex);
            };
        }

        private bool ignoreDirectedChange;

        private void UpdateTxLabel()
        {
            repeatLabel.Text = timeoutNumUpDown.Value == 1 ? "Tx per msg" : "repeated Tx";
        }

        private static readonly System.Text.RegularExpressions.Regex AlphaOnly = new System.Text.RegularExpressions.Regex("[^A-Za-z]");
        private static readonly System.Text.RegularExpressions.Regex NumericOnly = new System.Text.RegularExpressions.Regex("[^0-9]");

        private void ValidateDirCqTextBox()
        {
            string text = (directedTextBox.Text ?? "").Replace("*", "");
            var dirArray = text.Trim().ToUpper().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string corrText = "";
            string delim = "";
            foreach (string dir in dirArray)
            {
                if (dir.Length >= 2 && dir.Length <= 4) { corrText = corrText + delim + dir; delim = " "; }
            }
            directedTextBox.Text = corrText;
            if (corrText == "") callDirCqCheckBox.Checked = false;
        }

        private void ValidateAlertTextBox()
        {
            var dirArray = (alertTextBox.Text ?? "").Trim().ToUpper().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string corrText = "";
            string delim = "";
            foreach (string dir in dirArray)
            {
                if (dir.Length >= 2 && dir.Length <= 4 && (!AlphaOnly.IsMatch(dir) || !NumericOnly.IsMatch(dir)))
                {
                    corrText = corrText + delim + dir;
                    delim = " ";
                }
            }
            alertTextBox.Text = corrText;
        }

        private void UpdateCqNewOnBand()
        {
            bool enabled = replyDxCheckBox.Checked || replyLocalCheckBox.Checked;
            anyMsgRadioButton.Enabled = cqGridRadioButton.Enabled = cqOnlyRadioButton.Enabled = enabled;
            bandComboBox.Enabled = enabled;
        }

        private void CheckManualSelection()
        {
            if (formLoaded && listenModeButton.Checked && !replyDxCheckBox.Checked && !replyLocalCheckBox.Checked && !replyDirCqCheckBox.Checked)
                ShowMsg("No auto-reply filter is enabled -- Jimmy will not reply to any station automatically.", true);
        }

        public void SyncCqIntentFromMode()
        {
            if (wsjtxClient == null) return;
            listenModeButton.Checked = wsjtxClient.txMode == WsjtxClient.TxModes.LISTEN;
        }

        public void SyncCqIntentFromCheckboxes()
        {
            // WinForms original synced a separate 4-radio CQ-intent group inside CallCqDlg;
            // that dialog is deferred here (its checkboxes now live directly on MainWindow), so
            // there is no separate intent radio group left to sync.
        }

        private void BackfillMissingStates()
        {
            try
            {
                using (var db = new LogbookDb())
                {
                    int fixedCount = db.BackfillMissingStates(call => lookupManager?.Build(call)?.State);
                    if (fixedCount > 0) db.SetMeta("state_backfill_last_fixed", $"{DateTime.UtcNow:o} ({fixedCount} rows)");
                }
            }
            catch { /* best-effort repair -- must never block startup */ }
        }

        // Ported from the WinForms Controller_FormClosing -- everything except window
        // bounds (MainWindow saves its own position/size directly). Called once, from
        // MainWindow's Closing handler, before CloseComm().
        public void SaveAllSettings()
        {
            if (iniFile == null) return;

            iniFile.Write("debug", wsjtxClient.debug.ToString());
            if (wsjtxClient.ipAddress != null) iniFile.Write("ipAddress", wsjtxClient.ipAddress.ToString());
            if (wsjtxClient.port != 0) iniFile.Write("port", wsjtxClient.port.ToString());
            iniFile.Write("multicast", wsjtxClient.multicast.ToString());
            iniFile.Write("timeout", ((int)timeoutNumUpDown.Value).ToString());
            iniFile.Write("useDirected", callDirCqCheckBox.Checked.ToString());
            iniFile.Write("directedCqLockedEntry", directedCqLockedEntry ?? "");
            iniFile.Write("directeds", (directedTextBox.Text ?? "").Trim());
            iniFile.Write("playMyCall", mycallCheckBox.Checked.ToString());
            iniFile.Write("playLogged", loggedCheckBox.Checked.ToString());
            iniFile.Write("playCallAdded", callAddedCheckBox.Checked.ToString());
            iniFile.Write("useAlertDirected", replyDirCqCheckBox.Checked.ToString());
            iniFile.Write("alertDirecteds", (alertTextBox.Text ?? "").Trim());
            iniFile.Write("logEarly", logEarlyCheckBox.Checked.ToString());
            iniFile.Write("alwaysOnTop", alwaysOnTop.ToString());
            iniFile.Write("useRR73", wsjtxClient.useRR73.ToString());
            iniFile.Write("firstRun", "False");
            iniFile.Write("enableReplyDx", replyDxCheckBox.Checked.ToString());
            iniFile.Write("enableReplyLocal", replyLocalCheckBox.Checked.ToString());
            iniFile.Write("diagLog", wsjtxClient.diagLog.ToString());
            iniFile.Write("bestOffset", freqCheckBox.Checked.ToString());
            iniFile.Write("optimizeTx", optimizeCheckBox.Checked.ToString());
            iniFile.Write("exceptCalls", (exceptTextBox.Text ?? "").Trim());
            iniFile.Write("callCqDx", callCqDxCheckBox.Checked.ToString());
            iniFile.Write("ignoreNonDx", ignoreNonDxCheckBox.Checked.ToString());
            iniFile.Write("callNonDirCq", callNonDirCqCheckBox.Checked.ToString());
            iniFile.Write("cqOnly", cqOnlyRadioButton.Checked.ToString());
            iniFile.Write("newOnBand", (bandComboBox.SelectedIndex == 1).ToString());
            iniFile.Write("myContinent", wsjtxClient.myContinent);
            iniFile.Write("rankMethod", wsjtxClient.Ranker.rankMethodIdx.ToString());
            iniFile.Write("categoryWeights", FormatCategoryWeights(wsjtxClient.Ranker.categoryWeight));
            iniFile.Write("callingPriorities", FormatCallingPriorities(wsjtxClient.Ranker.callingEnabled));
            iniFile.Write("wantedCalls", FormatWantedCalls(wsjtxClient.wantedCalls));
            iniFile.Write("spotWatchCalls", FormatSpotWatchCalls(wsjtxClient.spotWatchCalls));
            iniFile.Write("wantedCallAnywhereEnabled", wantedCallAnywhereEnabled.ToString());
            iniFile.Write("rawPriorityTags", rawPriorityTags.ToString());
            iniFile.Write("replyRR73", replyRR73CheckBox.Checked.ToString());
            iniFile.Write("cqGrid", cqGridRadioButton.Checked.ToString());
            iniFile.Write("anyMsg", anyMsgRadioButton.Checked.ToString());
            iniFile.Write("txPeriodIdx", periodComboBox.SelectedIndex.ToString());
            iniFile.Write("cmdPrompts", wsjtxClient.cmdPrompts.ToString());
            iniFile.Write("usePskReporter", wsjtxClient.usePskReporter.ToString());
            iniFile.Write("showUsState", showUsStateCheckBox.Checked.ToString());
            Settings.SaveToIni(iniFile);
            Radio.SaveToIni(iniFile);
            Decode.SaveToIni(iniFile);
            Frequencies.SaveToIni(iniFile);
            Notifications.SaveToIni(iniFile);
            NativeEngine.SaveToIni(iniFile);
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
            iniFile.Write("rawShowDistAz", rawShowDistAz.ToString());
            iniFile.Write("rawOnlyCallsigns", rawOnlyCallsigns.ToString());
            iniFile.Write("rawOnlyUnworked", rawOnlyUnworked.ToString());
            iniFile.Write("rawOnlyRanked", rawOnlyRanked.ToString());
            iniFile.Write("rawNewestFirst", rawNewestFirst.ToString());
            iniFile.Write("rawMaxRows", rawMaxRows.ToString());
            iniFile.Write("maxQueuedCalls", maxQueuedCallsBase.ToString());
            iniFile.Write("maxCallQueueAgePeriods", maxCallQueueAgePeriods.ToString());
            iniFile.Write("statusBatchDelayMs", statusBatchDelayMs.ToString());
            iniFile.Write("keepTransmitListDuringTx", keepTransmitListDuringTx.ToString());
            iniFile.Write("keepListPositionDuringRefresh", keepListPositionDuringRefresh.ToString());
            iniFile.Write("moveFocusToStatusOnCallSelect", moveFocusToStatusOnCallSelect.ToString());
            iniFile.Write("checkForUpdatesOnStartup", checkForUpdatesOnStartup.ToString());
            iniFile.Write("soundFile_CallAdded", soundFile_CallAdded ?? "");
            iniFile.Write("soundFile_CallingMe", soundFile_CallingMe ?? "");
            iniFile.Write("soundFile_Logged", soundFile_Logged ?? "");
            iniFile.Write("soundEnabled_TxEnabled", soundEnabled_TxEnabled.ToString());
            iniFile.Write("soundFile_TxEnabled", soundFile_TxEnabled ?? "");
            iniFile.Write("soundEnabled_Disconnected", soundEnabled_Disconnected.ToString());
            iniFile.Write("soundFile_Disconnected", soundFile_Disconnected ?? "");
            iniFile.Write("soundsEnabled", soundsEnabled.ToString());
            iniFile.Write("txOddOffset", wsjtxClient.cachedOddOffset.ToString());
            iniFile.Write("txEvenOffset", wsjtxClient.cachedEvenOffset.ToString());
            iniFile.Write("useLookupData", useLookupData.ToString());
            iniFile.Write("qrzEnabled", qrzEnabled.ToString());
            iniFile.Write("qrzUsername", qrzUsername ?? "");
            iniFile.Write("qrzPassword", CredentialProtector.Protect(qrzPassword));
            iniFile.Write("qrzCacheDays", qrzCacheDays.ToString());
            iniFile.Write("qrzLookupPolicy", ((int)qrzLookupPolicy).ToString());
            iniFile.Write("qrzMinIntervalSeconds", qrzMinIntervalSeconds.ToString());
            iniFile.Write("lotwEnabled", lotwEnabled.ToString());
            iniFile.Write("lotwBoostEnabled", lotwBoostEnabled.ToString());
            iniFile.Write("lotwRefreshDays", lotwRefreshDays.ToString());
            iniFile.Write("clubLogRefreshDays", clubLogRefreshDays.ToString());
            iniFile.Write("fccUlsEnabled", fccUlsEnabled.ToString());
            iniFile.Write("fccUlsRefreshDays", fccUlsRefreshDays.ToString());
            iniFile.Write("qrzLogbookApiKey", CredentialProtector.Protect(qrzLogbookApiKey));
            iniFile.Write("qrzUploadEnabled", qrzUploadEnabled.ToString());
            iniFile.Write("qrzUploadRealtime", qrzUploadRealtime.ToString());
            iniFile.Write("lotwUploadEnabled", lotwUploadEnabled.ToString());
            iniFile.Write("clubLogUploadEnabled", clubLogUploadEnabled.ToString());
            iniFile.Write("clubLogUploadRealtime", clubLogUploadRealtime.ToString());
            iniFile.Write("clubLogUploadEmail", clubLogUploadEmail ?? "");
            iniFile.Write("clubLogUploadPassword", CredentialProtector.Protect(clubLogUploadPassword));
            iniFile.Write("clubLogUploadCallsign", clubLogUploadCallsign ?? "");
            iniFile.Write("hrdLogUploadEnabled", hrdLogUploadEnabled.ToString());
            iniFile.Write("hrdLogUploadRealtime", hrdLogUploadRealtime.ToString());
            iniFile.Write("hrdLogUploadCode", CredentialProtector.Protect(hrdLogUploadCode));
            iniFile.Write("hrdLogUploadCallsign", hrdLogUploadCallsign ?? "");
            iniFile.Write("tqslStationLocation", tqslStationLocation ?? "");
            iniFile.Write("activeAwardRuleIds", string.Join(",", activeAwardRuleIds ?? new List<string>()));
            hotkeyConfig?.SaveToIni(iniFile);
        }

        private static string FormatWantedCalls(HashSet<string> calls)
        {
            if (calls == null || calls.Count == 0) return string.Empty;
            var sorted = new List<string>(calls);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", sorted);
        }

        public static string FormatSpotWatchCalls(HashSet<string> calls) => FormatWantedCalls(calls);

        public void SaveHotkeyConfig()
        {
            if (iniFile != null) hotkeyConfig.SaveToIni(iniFile);
        }

        public void SaveSetting(string key, string value)
        {
            iniFile?.Write(key, value);
        }

        public void CloseComm()
        {
            mainLoopTimer.Stop();
            statusMsgTimer.Stop();
            radioPollTimer.Stop();
            rigctldClient?.Dispose();
            nativeEngineClient?.Dispose();
            nativeEngineClient = null;
            wsjtxClient?.Closing();
        }

        // ── Static parse/format helpers, ported verbatim from the WinForms Controller ──

        public static List<string> ParseRowOrder(string orderStr, IEnumerable<string> allowedFields)
        {
            if (string.IsNullOrWhiteSpace(orderStr)) return null;
            var allowed = new HashSet<string>(allowedFields, StringComparer.OrdinalIgnoreCase);
            var parsed = new List<string>();
            foreach (var tok in orderStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = tok.Trim();
                if (f.Length == 0) continue;
                if (!allowed.Contains(f)) continue;
                if (parsed.Exists(s => string.Equals(s, f, StringComparison.OrdinalIgnoreCase))) continue;
                parsed.Add(f);
            }
            return parsed.Count > 0 ? parsed : null;
        }

        private static List<WsjtxClient.RankMethods> ParseRankOrder(string rankOrderStr, int legacyIdx)
        {
            if (!string.IsNullOrWhiteSpace(rankOrderStr))
            {
                var result = new List<WsjtxClient.RankMethods>();
                foreach (var tok in rankOrderStr.Split(','))
                    if (RankIdToMethod(tok.Trim(), out var m) && !result.Contains(m)) result.Add(m);
                if (result.Count > 0) return result;
            }
            if (legacyIdx >= (int)WsjtxClient.RankMethods.AZ_NQUAD) return new List<WsjtxClient.RankMethods> { WsjtxClient.RankMethods.MOST_RECENT };
            if (Enum.IsDefined(typeof(WsjtxClient.RankMethods), legacyIdx)) return new List<WsjtxClient.RankMethods> { (WsjtxClient.RankMethods)legacyIdx };
            return new List<WsjtxClient.RankMethods> { WsjtxClient.RankMethods.MOST_RECENT };
        }

        private static WsjtxClient.RankMethods? ParseRankBeam(string rankBeamStr, int legacyIdx)
        {
            if (!string.IsNullOrWhiteSpace(rankBeamStr))
            {
                if (BeamIdToMethod(rankBeamStr.Trim(), out var b)) return b;
                return null;
            }
            if (legacyIdx >= (int)WsjtxClient.RankMethods.AZ_NQUAD && Enum.IsDefined(typeof(WsjtxClient.RankMethods), legacyIdx))
                return (WsjtxClient.RankMethods)legacyIdx;
            return null;
        }

        private static bool RankIdToMethod(string id, out WsjtxClient.RankMethods method)
        {
            switch (id?.ToLowerInvariant())
            {
                case "call_order": method = WsjtxClient.RankMethods.CALL_ORDER; return true;
                case "most_recent": method = WsjtxClient.RankMethods.MOST_RECENT; return true;
                case "dist_near": method = WsjtxClient.RankMethods.DIST_INCR; return true;
                case "dist_far": method = WsjtxClient.RankMethods.DIST_DECR; return true;
                case "snr_weak": method = WsjtxClient.RankMethods.SNR_INCR; return true;
                case "snr_strong": method = WsjtxClient.RankMethods.SNR_DECR; return true;
                default: method = default; return false;
            }
        }

        private static bool BeamIdToMethod(string id, out WsjtxClient.RankMethods? method)
        {
            switch (id?.ToLowerInvariant())
            {
                case "none": method = null; return true;
                case "az_n": method = WsjtxClient.RankMethods.AZ_NQUAD; return true;
                case "az_ne": method = WsjtxClient.RankMethods.AZ_NEQUAD; return true;
                case "az_e": method = WsjtxClient.RankMethods.AZ_EQUAD; return true;
                case "az_se": method = WsjtxClient.RankMethods.AZ_SEQUAD; return true;
                case "az_s": method = WsjtxClient.RankMethods.AZ_SQUAD; return true;
                case "az_sw": method = WsjtxClient.RankMethods.AZ_SWQUAD; return true;
                case "az_w": method = WsjtxClient.RankMethods.AZ_WQUAD; return true;
                case "az_nw": method = WsjtxClient.RankMethods.AZ_NWQUAD; return true;
                default: method = null; return false;
            }
        }

        private static string FormatCategoryWeights(Dictionary<WsjtxClient.CallCategory, int> weights)
        {
            var parts = new System.Text.StringBuilder();
            foreach (WsjtxClient.CallCategory cat in Enum.GetValues(typeof(WsjtxClient.CallCategory)))
            {
                if (parts.Length > 0) parts.Append(',');
                weights.TryGetValue(cat, out int tier);
                parts.Append($"{cat}={tier}");
            }
            return parts.ToString();
        }

        private static Dictionary<WsjtxClient.CallCategory, int> ParseCategoryWeights(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var result = new Dictionary<WsjtxClient.CallCategory, int>();
            foreach (var tok in s.Split(','))
            {
                var kv = tok.Trim().Split('=');
                if (kv.Length != 2) return null;
                if (!Enum.TryParse(kv[0].Trim(), out WsjtxClient.CallCategory cat)) return null;
                if (!int.TryParse(kv[1].Trim(), out int tier) || tier < 0) return null;
                result[cat] = tier;
            }
            return result;
        }

        private static string FormatCallingPriorities(List<WsjtxClient.CallCategory> enabled)
        {
            if (enabled == null) return string.Empty;
            return string.Join(",", enabled.Select(cat => cat.ToString()));
        }

        private static readonly WsjtxClient.CallCategory[] DefaultCallingOrder =
        {
            WsjtxClient.CallCategory.TO_MYCALL, WsjtxClient.CallCategory.NEW_COUNTRY_ON_BAND,
            WsjtxClient.CallCategory.NEW_COUNTRY, WsjtxClient.CallCategory.WANTED_CQ,
            WsjtxClient.CallCategory.ALWAYS_WANTED, WsjtxClient.CallCategory.WAS_NEEDED,
            WsjtxClient.CallCategory.WAS_UNCONFIRMED, WsjtxClient.CallCategory.DXCC_UNCONFIRMED,
            WsjtxClient.CallCategory.ZONE_NEEDED, WsjtxClient.CallCategory.STILL_NEEDED,
            WsjtxClient.CallCategory.DEFAULT,
        };

        private static List<WsjtxClient.CallCategory> ParseCallingPriorities(string callingStr, string legacyDisabledStr = null)
        {
            if (!string.IsNullOrWhiteSpace(callingStr))
            {
                var result = new List<WsjtxClient.CallCategory>();
                foreach (var tok in callingStr.Split(','))
                    if (Enum.TryParse(tok.Trim(), out WsjtxClient.CallCategory cat) && !result.Contains(cat)) result.Add(cat);
                if (result.Count > 0) return result;
            }
            if (!string.IsNullOrWhiteSpace(legacyDisabledStr))
            {
                var disabled = new HashSet<WsjtxClient.CallCategory>();
                foreach (var tok in legacyDisabledStr.Split(','))
                    if (Enum.TryParse(tok.Trim(), out WsjtxClient.CallCategory cat) && cat != WsjtxClient.CallCategory.DEFAULT) disabled.Add(cat);
                var result = new List<WsjtxClient.CallCategory>();
                foreach (var cat in DefaultCallingOrder) if (!disabled.Contains(cat)) result.Add(cat);
                return result;
            }
            return new List<WsjtxClient.CallCategory>(DefaultCallingOrder);
        }

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

        public static HashSet<string> ParseSpotWatchCalls(string s)
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
    }

    // Row-display-order defaults (WinForms RowDisplayOrderDlg's static default field lists) --
    // that editor dialog is deferred, but WsjtxClient still reads these two default orderings.
    internal static class RowDisplayOrderDefaults
    {
        public static readonly List<string> CallWaiting = new List<string>
        { "Call", "Category", "Snr", "Grid", "Distance", "Country" };
        public static readonly List<string> RawDecode = new List<string>
        { "Time", "Snr", "Freq", "Message" };
    }
}
