using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using WinKeys = System.Windows.Forms.Keys;

namespace WSJTX_Controller
{
    // The WPF presentation layer. Implements the two seams Controller drives (IWpfStatusSink/
    // IWpfListSink) so Controller itself stays free of any WPF reference -- a future Linux/
    // Raspberry Pi front end would implement the same two interfaces instead of this class.
    public partial class MainWindow : Window, IWpfStatusSink, IWpfListSink
    {
        private readonly Controller ctrl;
        private readonly ObservableCollection<string> callQueueItems = new ObservableCollection<string>();
        private readonly ObservableCollection<string> loggedItems = new ObservableCollection<string>();
        private bool suppressEvents;

        private ManualCallWindow _manualCallWindow;
        private HelpWindow _helpWindow;
        private LogbookWindow _logbookWindow;
        private string _lastManualCall = "";

        private static readonly System.Windows.Size DefaultWindowSize = new System.Windows.Size(760, 820);

        public MainWindow(Controller controller)
        {
            ctrl = controller;
            InitializeComponent();

            ctrl.StatusSink = this;
            ctrl.ListSink = this;

            CallQueueList.ItemsSource = callQueueItems;
            LoggedList.ItemsSource = loggedItems;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Deferred, same as the WinForms Form_Load's own deferred BeginInvoke: lets the
            // window paint once before the (potentially slow) engine/lookup startup work runs.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ctrl.LoadSettingsAndStart();
                SyncAllControlsFromController();
                RestoreWindowBounds();
            }));
        }

        // Window position/size persistence, ported from the WinForms Controller_FormClosing/
        // Form_Load pair -- WPF has no direct equivalent of WinForms' multi-Screen bounds-
        // matching, so this uses SystemParameters.WorkArea instead: still clamps a saved size/
        // position from a monitor that's no longer present back onto the primary work area.
        private void RestoreWindowBounds()
        {
            if (!ctrl.SettingExists("windowWd")) return;
            if (double.TryParse(ctrl.ReadSetting("windowPosX"), out double x) &&
                double.TryParse(ctrl.ReadSetting("windowPosY"), out double y))
            {
                Left = x;
                Top = y;
            }
            if (double.TryParse(ctrl.ReadSetting("windowWd"), out double w) && w > 0)
                Width = Math.Max(MinWidth, Math.Min(w, SystemParameters.WorkArea.Width));
            if (double.TryParse(ctrl.ReadSetting("windowHt"), out double h) && h > 0)
                Height = Math.Max(MinHeight, Math.Min(h, SystemParameters.WorkArea.Height));
            if (ctrl.ReadSetting("windowState") == "Maximized")
                WindowState = WindowState.Maximized;

            // Off-screen guard: a saved position from a monitor that's since been unplugged
            // would otherwise leave the window permanently unreachable.
            var wa = SystemParameters.WorkArea;
            if (Left + Width < wa.Left || Left > wa.Right || Top + Height < wa.Top || Top > wa.Bottom)
            {
                Left = wa.Left;
                Top = wa.Top;
            }
        }

        private void SaveWindowBounds()
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            ctrl.SaveSetting("windowPosX", ((int)bounds.X).ToString());
            ctrl.SaveSetting("windowPosY", ((int)bounds.Y).ToString());
            ctrl.SaveSetting("windowWd", ((int)bounds.Width).ToString());
            ctrl.SaveSetting("windowHt", ((int)bounds.Height).ToString());
            ctrl.SaveSetting("windowState", WindowState.ToString());
        }

        // HotkeyAction.ResetWindowSize (Ctrl+Shift+R)
        private void ResetWindowSize()
        {
            WindowState = WindowState.Normal;
            Width = DefaultWindowSize.Width;
            Height = DefaultWindowSize.Height;
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + (wa.Height - Height) / 2;
            ctrl.ShowMsg("Window size and position reset to default", false);
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowBounds();
            ctrl.SaveAllSettings();
            ctrl.CloseComm();
            _callCqWindow?.Close();
            _manualCallWindow?.Close();
            _helpWindow?.Close();
            _logbookWindow?.Close();
        }

        // Pulls every shim control's current value into the real WPF controls once, after
        // LoadSettingsAndStart finishes. Ongoing changes flow the other way (WPF control ->
        // ctrl field) via the event handlers below; ctrl fields that change from business logic
        // itself (not the operator) push back into the UI via the shim's PropertyChanged.
        private void SyncAllControlsFromController()
        {
            suppressEvents = true;
            try
            {
                ListenRadio.IsChecked = ctrl.listenModeButton.Checked;
                CqRadio.IsChecked = !ctrl.listenModeButton.Checked;
                VerLabel.Text = ctrl.verLabel.Text;
            }
            finally { suppressEvents = false; }

            // Keep the UI in sync with changes business logic itself makes to these fields
            // (e.g. mode flipping between Listen/CQ from a hotkey or from WsjtxSettingChanged).
            ctrl.listenModeButton.PropertyChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                suppressEvents = true;
                ListenRadio.IsChecked = ctrl.listenModeButton.Checked;
                CqRadio.IsChecked = !ctrl.listenModeButton.Checked;
                suppressEvents = false;
            }));
            ctrl.verLabel.PropertyChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => VerLabel.Text = ctrl.verLabel.Text));

            ApplyLayoutMode();
        }

        // Simple vs advanced main-window layout, matching the WinForms Controller's own
        // ApplyAdvancedLayout(): Settings.AdvancedCallLayout picks between the single call
        // queue list and the Tx1/Tx2/Raw (+ Spot Watch, its own independent toggle) panes.
        // Defaults to true (advanced) -- same default JimmySettings itself uses, so a fresh
        // install shows the advanced layout, not the simple one.
        public void RefreshLayout() => ApplyLayoutMode();

        private void ApplyLayoutMode()
        {
            bool advanced = ctrl.Settings.AdvancedCallLayout;
            bool showTx1 = advanced && ctrl.Settings.AdvShowTx1;
            bool showTx2 = advanced && ctrl.Settings.AdvShowTx2;
            bool showRaw = advanced && ctrl.Settings.AdvShowRaw;
            bool showSpot = advanced && ctrl.Settings.ShowSpotWatch;
            bool anyAdv = showTx1 || showTx2 || showRaw || showSpot;

            SimpleCallPanel.Visibility = anyAdv ? Visibility.Collapsed : Visibility.Visible;
            AdvancedCallPanel.Visibility = anyAdv ? Visibility.Visible : Visibility.Collapsed;
            Tx1Panel.Visibility = showTx1 ? Visibility.Visible : Visibility.Collapsed;
            Tx2Panel.Visibility = showTx2 ? Visibility.Visible : Visibility.Collapsed;
            RawPanel.Visibility = showRaw ? Visibility.Visible : Visibility.Collapsed;
            SpotWatchPanel.Visibility = showSpot ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Control event handlers: write operator input back into ctrl's shim fields ──

        private void OptionsButton_Click(object sender, RoutedEventArgs e) => OpenOptions();
        private void LogbookButton_Click(object sender, RoutedEventArgs e) => OpenLogbookWindow();
        private void HelpButton_Click(object sender, RoutedEventArgs e) => OpenHelp();

        private void OpenOptions()
        {
            var win = new OptionsWindow(ctrl) { Owner = this };
            win.ShowDialog();
        }

        private void OpenManualCallDialog()
        {
            if (ctrl.wsjtxClient == null || !ctrl.wsjtxClient.ConnectedToWsjtx())
            {
                MessageBox.Show("WSJT-X is not connected.", ctrl.friendlyName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var dlg = new ManualCallWindow(_lastManualCall, ctrl.wsjtxClient.lookupManager) { Owner = this };
            _manualCallWindow = dlg;
            if (dlg.ShowDialog() != true) { _manualCallWindow = null; return; }
            _manualCallWindow = null;

            string callsign = dlg.Callsign;
            if (ctrl.wsjtxClient.IsBlockedCall(callsign))
            {
                MessageBox.Show($"{callsign} is blocked.", ctrl.friendlyName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _lastManualCall = callsign;
            bool started = ctrl.wsjtxClient.ManualEnqueueCall(callsign);
            if (started)
                ctrl.ShowMsg($"Manual call started for {callsign}", false);
            else
                ctrl.ShowMsg($"Manual call to {callsign} could not be started -- no connection to the radio engine.", true);
        }

        private void OpenHelp()
        {
            if (ctrl.wsjtxClient != null && ctrl.wsjtxClient.ConnectedToWsjtx()) ctrl.wsjtxClient.HaltTuning();
            _helpWindow?.Close();
            _helpWindow = new HelpWindow(ctrl, $"{ctrl.wsjtxClient?.pgmName} Help", ctrl.BuildHelpText());
            _helpWindow.Closed += (s, e) => _helpWindow = null;
            _helpWindow.Show();
            _helpWindow.Activate();
        }

        private void OpenLogbookWindow()
        {
            if (_logbookWindow != null)
            {
                _logbookWindow.Activate();
                return;
            }
            _logbookWindow = new LogbookWindow(ctrl);
            _logbookWindow.Closed += (s, e) => _logbookWindow = null;
            _logbookWindow.Show();
        }

        private void OpenSortOrderEditor()
        {
            if (ctrl.wsjtxClient == null) return;
            var dlg = new SortOrderWindow(ctrl.wsjtxClient.Ranker.rankOrderList, ctrl.wsjtxClient.Ranker.rankBeamMethod,
                ctrl.wsjtxClient.Ranker.callingEnabled)
            { Owner = this };
            if (dlg.ShowDialog() != true) return;

            ctrl.wsjtxClient.ApplySortOrder(dlg.SelectedOrder, dlg.SelectedBeam);
            ctrl.wsjtxClient.ApplyCategoryWeights(dlg.SelectedCategoryWeights);
            ctrl.wsjtxClient.ApplyCallingPriorities(dlg.SelectedCallingPriorities);
            ctrl.wsjtxClient.SortCallsPublic();

            ctrl.SaveSetting("rankOrder", string.Join(",", dlg.SelectedOrder.Select(Controller.MethodToRankId)));
            ctrl.SaveSetting("rankBeam", dlg.SelectedBeam.HasValue ? Controller.MethodToBeamId(dlg.SelectedBeam.Value) : "none");
            ctrl.SaveSetting("rankMethod", ctrl.wsjtxClient.Ranker.rankMethodIdx.ToString());
            ctrl.SaveSetting("categoryWeights", Controller.FormatCategoryWeightsPublic(dlg.SelectedCategoryWeights));
            ctrl.SaveSetting("callingPriorities", Controller.FormatCallingPrioritiesPublic(dlg.SelectedCallingPriorities));
        }

        private void OpenRowDisplayOrderEditor()
        {
            if (ctrl.wsjtxClient == null) return;
            var dlg = new RowDisplayOrderWindow(ctrl.wsjtxClient.callWaitingRowOrderFields, ctrl.wsjtxClient.rawDecodeRowOrderFields,
                ctrl.spotWatchRowOrderFields, ctrl.wsjtxClient.debug)
            { Owner = this };
            if (dlg.ShowDialog() != true) return;

            ctrl.SaveSetting("callWaitingRowOrder", string.Join(",", dlg.SelectedCallWaitingFields));
            ctrl.wsjtxClient.callWaitingRowOrderFields = new List<string>(dlg.SelectedCallWaitingFields);

            ctrl.SaveSetting("rawDecodeRowOrder", string.Join(",", dlg.SelectedRawDecodeFields));
            ctrl.wsjtxClient.rawDecodeRowOrderFields = new List<string>(dlg.SelectedRawDecodeFields);

            ctrl.SaveSetting("spotWatchRowOrder", string.Join(",", dlg.SelectedSpotWatchFields));
            ctrl.spotWatchRowOrderFields = new List<string>(dlg.SelectedSpotWatchFields);

            ctrl.wsjtxClient.RefreshCallWaitingRows();
            ctrl.wsjtxClient.RefreshAdvancedLists();
            ctrl.RenderSpotWatchList();
        }

        private void ListenRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (suppressEvents) return;
            ctrl.listenModeButton_Click(null, null);
        }

        private void CqRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (suppressEvents) return;
            ctrl.cqModeButton_Click(null, null);
        }

        private CallCqWindow _callCqWindow;
        private void CallCqOptionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_callCqWindow != null)
            {
                _callCqWindow.Activate();
                return;
            }
            _callCqWindow = new CallCqWindow(ctrl, ctrl.wsjtxClient);
            _callCqWindow.Closed += (s, e2) => _callCqWindow = null;
            _callCqWindow.Show();
        }

        private void RowOrderButton_Click(object sender, RoutedEventArgs e) => OpenRowDisplayOrderEditor();
        private void SortOrderButton_Click(object sender, RoutedEventArgs e) => OpenSortOrderEditor();

        // verLabel double-click: hidden debug-mode toggle, ported verbatim from
        // verLabel_DoubleClick.
        private void VerLabel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 || !ctrl.FormLoaded) return;
            ctrl.wsjtxClient.debug = !ctrl.wsjtxClient.debug;
            ctrl.wsjtxClient.DebugChanged();
        }

        // verLabel2 click: opens the update-check page, ported verbatim from verLabel2_Click.
        private void VerLabel2_Click(object sender, MouseButtonEventArgs e)
        {
            string ver = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? string.Empty;
            string url = "https://blindsea.com/jimmy?v=" + Uri.EscapeDataString(ver);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }

        // ── Call queue interaction: Enter/Space replies, Ctrl+C copies the callsign ──

        private void CallQueueList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!ctrl.FormLoaded) return;
            int idx = CallQueueList.SelectedIndex;

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                if (idx < 0) return;
                int mapped = ctrl.wsjtxClient.MapNormalListIndex(idx);
                ctrl.wsjtxClient.NextCall(false, mapped, operatorSelected: true, expectedCall: ctrl.wsjtxClient.GetCallAtIndex(mapped));
                e.Handled = true;
                return;
            }

            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (idx < 0) return;
                string call = ctrl.wsjtxClient.GetCallAtIndex(ctrl.wsjtxClient.MapNormalListIndex(idx));
                if (call != null) Clipboard.SetText(call);
                e.Handled = true;
            }
        }

        // ── Global hotkeys, ported from the WinForms Controller.ProcessCmdKey ──

        private static WinKeys ToFormsKeys(Key key, ModifierKeys modifiers)
        {
            var formsKey = (WinKeys)KeyInterop.VirtualKeyFromKey(key == Key.System ? Key.None : key);
            if ((modifiers & ModifierKeys.Control) != 0) formsKey |= WinKeys.Control;
            if ((modifiers & ModifierKeys.Alt) != 0) formsKey |= WinKeys.Alt;
            if ((modifiers & ModifierKeys.Shift) != 0) formsKey |= WinKeys.Shift;
            return formsKey;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key effectiveKey = e.Key == Key.System ? e.SystemKey : e.Key;
            WinKeys keyData = ToFormsKeys(effectiveKey, Keyboard.Modifiers);

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.NavStatus])
            {
                StatusReviewBox.Focus();
                e.Handled = true;
                return;
            }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.NavCallList])
            {
                if (CallQueueList.Visibility == Visibility.Visible) CallQueueList.Focus();
                e.Handled = true;
                return;
            }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.NavLoggedList])
            {
                LoggedList.Focus();
                e.Handled = true;
                return;
            }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.NavLoggedCount])
            {
                LoggedHeader.Focus();
                e.Handled = true;
                return;
            }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.NavPendingCount])
            {
                CallQueueHeader.Focus();
                e.Handled = true;
                return;
            }
            if (ctrl.hotkeyConfig[HotkeyAction.NavAdvTx1] != WinKeys.None && keyData == ctrl.hotkeyConfig[HotkeyAction.NavAdvTx1])
            {
                if (Tx1Panel.Visibility == Visibility.Visible) Tx1List.Focus();
                e.Handled = true;
                return;
            }
            if (ctrl.hotkeyConfig[HotkeyAction.NavAdvTx2] != WinKeys.None && keyData == ctrl.hotkeyConfig[HotkeyAction.NavAdvTx2])
            {
                if (Tx2Panel.Visibility == Visibility.Visible) Tx2List.Focus();
                e.Handled = true;
                return;
            }
            if (ctrl.hotkeyConfig[HotkeyAction.NavAdvRaw] != WinKeys.None && keyData == ctrl.hotkeyConfig[HotkeyAction.NavAdvRaw])
            {
                if (RawPanel.Visibility == Visibility.Visible) RawList.Focus();
                e.Handled = true;
                return;
            }
            if (ctrl.hotkeyConfig[HotkeyAction.NavSpotWatch] != WinKeys.None && keyData == ctrl.hotkeyConfig[HotkeyAction.NavSpotWatch])
            {
                if (SpotWatchPanel.Visibility == Visibility.Visible) SpotWatchList.Focus();
                e.Handled = true;
                return;
            }

            if (effectiveKey == Key.Escape)
            {
                if (ctrl.wsjtxClient != null && ctrl.wsjtxClient.ConnectedToWsjtx())
                {
                    ctrl.wsjtxClient.RequeueAbortedCall();
                    ctrl.wsjtxClient.CancelQso();
                    ctrl.wsjtxClient.HaltAndDisableTx();
                    ctrl.wsjtxClient.ResetTxToCq();
                    ctrl.listenModeButton_Click(null, null);
                    ctrl.ShowMsg("Tx halted", true);
                }
                e.Handled = true;
                return;
            }

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.Options])
            {
                OpenOptions();
                e.Handled = true;
                return;
            }

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.Help])
            {
                OpenHelp();
                e.Handled = true;
                return;
            }

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.OpenLogbook] && ctrl.hotkeyConfig[HotkeyAction.OpenLogbook] != WinKeys.None)
            {
                OpenLogbookWindow();
                e.Handled = true;
                return;
            }

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.AddManualQso] && ctrl.hotkeyConfig[HotkeyAction.AddManualQso] != WinKeys.None)
            {
                OpenLogbookWindow();
                _logbookWindow?.OpenAddQsoDialog();
                e.Handled = true;
                return;
            }

            if (!ctrl.FormLoaded || ctrl.wsjtxClient == null) return;
            if (!ctrl.wsjtxClient.WsjtxConnecting()) return;

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.ToggleMode]) { e.Handled = ctrl.wsjtxClient.ToggleOperatingMode(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.BandUp]) { e.Handled = ctrl.wsjtxClient.BandUp(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.BandDown]) { e.Handled = ctrl.wsjtxClient.BandDown(); return; }

            for (int bi = 0; bi < ctrl.Frequencies.Bands.Length; bi++)
                foreach (var entry in ctrl.Frequencies.Bands[bi])
                    if (entry.Hotkey != WinKeys.None && keyData == entry.Hotkey)
                    { e.Handled = ctrl.wsjtxClient.SelectFrequency(bi, entry.Mode, entry.FreqKHz); return; }

            if (!ctrl.wsjtxClient.ConnectedToWsjtx()) return;

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.PSKReporter]) { e.Handled = ctrl.wsjtxClient.TogglePskReporter(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.Prompts]) { e.Handled = ctrl.wsjtxClient.TogglePrompts(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.UploadLotw]) { e.Handled = ctrl.wsjtxClient.UploadLotw(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.EnableTx]) { e.Handled = ctrl.wsjtxClient.EnableMode(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.DeleteAllCalls]) { e.Handled = ctrl.wsjtxClient.ClearCallQueue(); return; }

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.HaltTx])
            {
                if (ctrl.wsjtxClient.ConnectedToWsjtx())
                {
                    ctrl.wsjtxClient.RequeueAbortedCall();
                    ctrl.wsjtxClient.CancelQso();
                    ctrl.wsjtxClient.HaltAndDisableTx();
                    ctrl.wsjtxClient.ResetTxToCq();
                    ctrl.listenModeButton_Click(null, null);
                    ctrl.ShowMsg("Tx halted", true);
                }
                e.Handled = true;
                return;
            }

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.CallCqMode])
            {
                if (ctrl.wsjtxClient.ConnectedToWsjtx())
                {
                    if (ctrl.wsjtxClient.txMode == WsjtxClient.TxModes.LISTEN && ctrl.wsjtxClient.AnalysisNeeded)
                    {
                        var confDlg = new ConfirmDlg { Text = "Transmit slot has not been analyzed.\nRun recommended analysis now?", Owner = this };
                        confDlg.ShowDialog();
                        if (confDlg.Confirmed) ctrl.wsjtxClient.StartSlotAnalysis(true);
                        else
                        {
                            ctrl.ShowMsg("Transmit slot analysis skipped.", true);
                            ctrl.cqModeButton_Click(null, null);
                        }
                    }
                    else ctrl.cqModeButton_Click(null, null);
                }
                e.Handled = true;
                return;
            }

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.AnalyzeSlot] && ctrl.hotkeyConfig[HotkeyAction.AnalyzeSlot] != WinKeys.None)
            {
                ctrl.wsjtxClient.StartSlotAnalysis(false);
                e.Handled = true;
                return;
            }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.LookupStation] && ctrl.hotkeyConfig[HotkeyAction.LookupStation] != WinKeys.None)
            {
                LookupSelectedCall();
                e.Handled = true;
                return;
            }

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.ListenMode]) { ctrl.listenModeButton_Click(null, null); e.Handled = true; return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.NextCall]) { ctrl.wsjtxClient.NextBestPriorityCall(); e.Handled = true; return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.ManualCall]) { OpenManualCallDialog(); e.Handled = true; return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.TxPeriod]) { e.Handled = ctrl.wsjtxClient.ToggleTxFirst(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.HoldTimeout]) { e.Handled = ctrl.wsjtxClient.ToggleHoldCheckBox(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.PowerSwr]) { e.Handled = ctrl.wsjtxClient.ReportPowerSwr(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.TuneMode]) { e.Handled = ctrl.wsjtxClient.ToggleTuningProcess(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.SortOrder]) { OpenSortOrderEditor(); e.Handled = true; return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.RowOrder]) { OpenRowDisplayOrderEditor(); e.Handled = true; return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.ResetWindowSize]) { ResetWindowSize(); e.Handled = true; return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.AudioUp]) { e.Handled = ctrl.wsjtxClient.AudioLevel(true); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.AudioDown]) { e.Handled = ctrl.wsjtxClient.AudioLevel(false); return; }
        }

        // ── IWpfStatusSink ──

        public void SetStatusText(string headingText, string statusText, System.Drawing.Color foreColor, System.Drawing.Color backColor)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                StatusReviewBox.Text = statusText;
            }));
        }

        public void Announce(string text)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string old = StatusAnnounceText.Text;
                StatusAnnounceText.Text = text;
                AutomationProperties.SetName(StatusAnnounceText, text);
                var peer = UIElementAutomationPeer.FromElement(StatusAnnounceText) ?? UIElementAutomationPeer.CreatePeerForElement(StatusAnnounceText);
                if (peer != null)
                {
                    peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, old, text);
                    peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                }
            }));
        }

        public void Dispatch(Action action) => Dispatcher.BeginInvoke(action);

        public void ActivateWindow()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
            }));
        }

        // ── IWpfListSink ──

        public void RenderCallQueue(string headerText, List<string> items, List<string> keys, List<WsjtxClient.CallCategory> categories, int preservedIndex)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CallQueueHeader.Text = string.IsNullOrEmpty(headerText) ? "Stations calling:" : headerText;
                callQueueItems.Clear();
                foreach (var it in items) callQueueItems.Add(it);
                if (preservedIndex >= 0 && preservedIndex < callQueueItems.Count) CallQueueList.SelectedIndex = preservedIndex;
            }));
        }

        private readonly ObservableCollection<string> rawItems = new ObservableCollection<string>();
        public void RenderRawDecodes(List<string> items, List<string> keys, List<WsjtxClient.CallCategory> categories)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (RawList.ItemsSource == null) RawList.ItemsSource = rawItems;
                rawItems.Clear();
                foreach (var it in items) rawItems.Add(it);
            }));
        }

        private readonly ObservableCollection<string> tx1Items = new ObservableCollection<string>();
        private readonly ObservableCollection<string> tx2Items = new ObservableCollection<string>();
        public void RenderAdvancedList(bool isTx1Side, string accessibleName, List<string> items, List<string> keys, List<WsjtxClient.CallCategory> categories)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var list = isTx1Side ? Tx1List : Tx2List;
                var backing = isTx1Side ? tx1Items : tx2Items;
                if (list.ItemsSource == null) list.ItemsSource = backing;
                backing.Clear();
                foreach (var it in items) backing.Add(it);
                if (!string.IsNullOrEmpty(accessibleName)) AutomationProperties.SetName(list, accessibleName);
            }));
        }

        // Enter/Space replies (via NextCallFromTx1/Tx2/NextCallFromRawDecode), Ctrl+C copies --
        // ported from AdvTx1ListBox_KeyDown/AdvTx2ListBox_KeyDown/AdvRawListBox_KeyDown. The
        // Delete-to-edit-queue-entry behavior those three also have is not yet ported (see
        // migration report).
        private void AdvancedList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!ctrl.FormLoaded) return;
            var list = (System.Windows.Controls.ListBox)sender;
            int idx = list.SelectedIndex;

            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (idx < 0) return;
                string call = list == Tx1List ? ctrl.wsjtxClient.GetCallAtTx1Index(idx)
                    : list == Tx2List ? ctrl.wsjtxClient.GetCallAtTx2Index(idx)
                    : ctrl.wsjtxClient.GetRawDecodeCallOrText(idx);
                if (call != null) Clipboard.SetText(call);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                e.Handled = true;
                if (list == Tx1List) { if (idx < 0) idx = 0; ctrl.wsjtxClient.NextCallFromTx1(idx); }
                else if (list == Tx2List) { if (idx < 0) idx = 0; ctrl.wsjtxClient.NextCallFromTx2(idx); }
                else { if (idx < 0) return; ctrl.wsjtxClient.NextCallFromRawDecode(idx); }
            }
        }

        private List<string> _loggedKeys = new List<string>();
        public void RenderLoggedList(string headerText, List<string> items, List<string> keys)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LoggedHeader.Text = string.IsNullOrEmpty(headerText) ? "Auto-logged:" : headerText;
                loggedItems.Clear();
                foreach (var it in items) loggedItems.Add(it);
                _loggedKeys = keys;
            }));
        }

        private readonly ObservableCollection<string> spotWatchItems = new ObservableCollection<string>();
        public void RenderSpotWatchList(List<string> items, List<string> keys)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (SpotWatchList.ItemsSource == null) SpotWatchList.ItemsSource = spotWatchItems;
                spotWatchItems.Clear();
                foreach (var it in items) spotWatchItems.Add(it);
            }));
        }

        // HotkeyAction.LookupStation -- whichever list currently has keyboard focus wins,
        // falling back to the call queue's own selection (matches LookupFocusedCall's own
        // fallback order). Advanced Tx1/Tx2/Raw panes are deferred, so only the two lists
        // this WPF pass actually builds are checked.
        private void LookupSelectedCall()
        {
            string call = null;
            if (LoggedList.IsFocused)
            {
                int idx = LoggedList.SelectedIndex;
                if (idx >= 0 && idx < _loggedKeys.Count) call = _loggedKeys[idx];
            }
            else if (Tx1List.IsFocused)
            {
                int idx = Tx1List.SelectedIndex;
                if (idx >= 0) call = ctrl.wsjtxClient.GetCallAtTx1Index(idx);
            }
            else if (Tx2List.IsFocused)
            {
                int idx = Tx2List.SelectedIndex;
                if (idx >= 0) call = ctrl.wsjtxClient.GetCallAtTx2Index(idx);
            }
            else if (RawList.IsFocused)
            {
                int idx = RawList.SelectedIndex;
                if (idx >= 0) call = ctrl.wsjtxClient.GetRawDecodeCallOrText(idx);
            }
            else if (CallQueueList.Visibility == Visibility.Visible)
            {
                int idx = CallQueueList.SelectedIndex;
                if (idx >= 0) call = ctrl.wsjtxClient.GetCallAtIndex(ctrl.wsjtxClient.MapNormalListIndex(idx));
            }
            else
            {
                int idx = Tx1List.SelectedIndex;
                if (idx >= 0) call = ctrl.wsjtxClient.GetCallAtTx1Index(idx);
                if (call == null)
                {
                    idx = Tx2List.SelectedIndex;
                    if (idx >= 0) call = ctrl.wsjtxClient.GetCallAtTx2Index(idx);
                }
            }
            if (string.IsNullOrEmpty(call)) return;

            var dlg = new LookupInfoWindow(call, ctrl.wsjtxClient.lookupManager) { Owner = this };
            dlg.ShowDialog();
            if (dlg.QrzLookupOccurred) ctrl.wsjtxClient?.DebugChanged();
        }
    }
}
