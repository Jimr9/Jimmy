using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
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
            StationText.Text = string.IsNullOrWhiteSpace(ctrl.NativeEngine.MyCall)
                ? "No callsign set -- open Options to configure your station."
                : $"{ctrl.NativeEngine.MyCall}  {ctrl.NativeEngine.MyGrid}";

            // Deferred, same as the WinForms Form_Load's own deferred BeginInvoke: lets the
            // window paint once before the (potentially slow) engine/lookup startup work runs.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ctrl.LoadSettingsAndStart();
                SyncAllControlsFromController();
                StationText.Text = string.IsNullOrWhiteSpace(ctrl.NativeEngine.MyCall)
                    ? "No callsign set -- open Options (Alt+O) to configure your station."
                    : $"{ctrl.NativeEngine.MyCall}  {ctrl.NativeEngine.MyGrid}";
            }));
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ctrl.SaveAllSettings();
            ctrl.CloseComm();
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
                CqOptionsPanel.IsEnabled = !ctrl.listenModeButton.Checked;
                NonDirCqCheck.IsChecked = ctrl.callNonDirCqCheckBox.Checked;
                CqDxCheck.IsChecked = ctrl.callCqDxCheckBox.Checked;
                DirCqCheck.IsChecked = ctrl.callDirCqCheckBox.Checked;
                DirectedBox.Text = ctrl.directedTextBox.Text;
                DirectedBox.IsEnabled = ctrl.directedTextBox.Enabled;
                HoldCheck.IsChecked = ctrl.holdCheckBox.Checked;
                FreqCheck.IsChecked = ctrl.freqCheckBox.Checked;
                BandCombo.SelectedIndex = Math.Max(0, ctrl.bandComboBox.SelectedIndex);
                PeriodCombo.SelectedIndex = Math.Max(0, ctrl.periodComboBox.SelectedIndex);
                TimeoutBox.Text = ((int)ctrl.timeoutNumUpDown.Value).ToString();
                RepeatLabel.Text = ctrl.repeatLabel.Text;

                ReplyDxCheck.IsChecked = ctrl.replyDxCheckBox.Checked;
                ReplyLocalCheck.IsChecked = ctrl.replyLocalCheckBox.Checked;
                ReplyDirCqCheck.IsChecked = ctrl.replyDirCqCheckBox.Checked;
                AlertBox.Text = ctrl.alertTextBox.Text;
                AlertBox.IsEnabled = ctrl.alertTextBox.Enabled;
                ReplyRR73Check.IsChecked = ctrl.replyRR73CheckBox.Checked;
                UseRR73Check.IsChecked = ctrl.useRR73CheckBox.Checked;
                IgnoreNonDxCheck.IsChecked = ctrl.ignoreNonDxCheckBox.Checked;
                LogEarlyCheck.IsChecked = ctrl.logEarlyCheckBox.Checked;
                OptimizeCheck.IsChecked = ctrl.optimizeCheckBox.Checked;
                ShowUsStateCheck.IsChecked = ctrl.showUsStateCheckBox.Checked;
                MyCallCheck.IsChecked = ctrl.mycallCheckBox.Checked;
                LoggedCheck.IsChecked = ctrl.loggedCheckBox.Checked;
                CallAddedCheck.IsChecked = ctrl.callAddedCheckBox.Checked;

                ExceptBox.Text = ctrl.exceptTextBox.Text;
            }
            finally { suppressEvents = false; }

            // Keep the UI in sync with changes business logic itself makes to these fields
            // (e.g. ignoreNonDxCheckBox getting force-unchecked by the coupling rules).
            ctrl.listenModeButton.PropertyChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                suppressEvents = true;
                ListenRadio.IsChecked = ctrl.listenModeButton.Checked;
                CqRadio.IsChecked = !ctrl.listenModeButton.Checked;
                CqOptionsPanel.IsEnabled = !ctrl.listenModeButton.Checked;
                suppressEvents = false;
            }));
            SyncOneWay(ctrl.callNonDirCqCheckBox, v => NonDirCqCheck.IsChecked = v);
            SyncOneWay(ctrl.callCqDxCheckBox, v => CqDxCheck.IsChecked = v);
            SyncOneWay(ctrl.callDirCqCheckBox, v => DirCqCheck.IsChecked = v);
            SyncOneWay(ctrl.ignoreNonDxCheckBox, v => IgnoreNonDxCheck.IsChecked = v);
            SyncOneWay(ctrl.replyDirCqCheckBox, v => ReplyDirCqCheck.IsChecked = v);
            SyncOneWayText(ctrl.directedTextBox, t => { if (DirectedBox.Text != t) DirectedBox.Text = t; DirectedBox.IsEnabled = ctrl.directedTextBox.Enabled; });
            SyncOneWayText(ctrl.alertTextBox, t => { if (AlertBox.Text != t) AlertBox.Text = t; AlertBox.IsEnabled = ctrl.alertTextBox.Enabled; });
            ctrl.timeoutNumUpDown.PropertyChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                suppressEvents = true;
                TimeoutBox.Text = ((int)ctrl.timeoutNumUpDown.Value).ToString();
                RepeatLabel.Text = ctrl.repeatLabel.Text;
                suppressEvents = false;
            }));
        }

        private void SyncOneWay(CheckState cs, Action<bool> apply) =>
            cs.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(CheckState.Checked)) return;
                Dispatcher.BeginInvoke(new Action(() => { suppressEvents = true; apply(cs.Checked); suppressEvents = false; }));
            };

        private void SyncOneWayText(TextState ts, Action<string> apply) =>
            ts.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(TextState.Text)) return;
                Dispatcher.BeginInvoke(new Action(() => { suppressEvents = true; apply(ts.Text); suppressEvents = false; }));
            };

        // ── Control event handlers: write operator input back into ctrl's shim fields ──

        private void OptionsButton_Click(object sender, RoutedEventArgs e) => OpenOptions();

        private void OpenOptions()
        {
            var win = new OptionsWindow(ctrl) { Owner = this };
            win.ShowDialog();
        }

        private void ListenRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (suppressEvents) return;
            CqOptionsPanel.IsEnabled = false;
            ctrl.listenModeButton_Click(null, null);
        }

        private void CqRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (suppressEvents) return;
            CqOptionsPanel.IsEnabled = true;
            ctrl.cqModeButton_Click(null, null);
        }

        private void NonDirCqCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.callNonDirCqCheckBox.Checked = NonDirCqCheck.IsChecked == true; }
        private void CqDxCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.callCqDxCheckBox.Checked = CqDxCheck.IsChecked == true; }
        private void DirCqCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.callDirCqCheckBox.Checked = DirCqCheck.IsChecked == true; }
        private void DirectedBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        { if (!suppressEvents) ctrl.directedTextBox.Text = DirectedBox.Text; }

        private void HoldCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.holdCheckBox.Checked = HoldCheck.IsChecked == true; }
        private void FreqCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.freqCheckBox.Checked = FreqCheck.IsChecked == true; }

        private void BandCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        { if (!suppressEvents) ctrl.bandComboBox.SelectedIndex = BandCombo.SelectedIndex; }
        private void PeriodCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        { if (!suppressEvents) ctrl.periodComboBox.SelectedIndex = PeriodCombo.SelectedIndex; }
        private void TimeoutBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (suppressEvents) return;
            if (int.TryParse(TimeoutBox.Text, out int v)) ctrl.timeoutNumUpDown.Value = v;
        }

        private void ReplyDxCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.replyDxCheckBox.Checked = ReplyDxCheck.IsChecked == true; }
        private void ReplyLocalCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.replyLocalCheckBox.Checked = ReplyLocalCheck.IsChecked == true; }
        private void ReplyDirCqCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.replyDirCqCheckBox.Checked = ReplyDirCqCheck.IsChecked == true; }
        private void AlertBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        { if (!suppressEvents) ctrl.alertTextBox.Text = AlertBox.Text; }
        private void ReplyRR73Check_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.replyRR73CheckBox.Checked = ReplyRR73Check.IsChecked == true; }
        private void UseRR73Check_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.useRR73CheckBox.Checked = UseRR73Check.IsChecked == true; }
        private void IgnoreNonDxCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.ignoreNonDxCheckBox.Checked = IgnoreNonDxCheck.IsChecked == true; }
        private void LogEarlyCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.logEarlyCheckBox.Checked = LogEarlyCheck.IsChecked == true; }
        private void OptimizeCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.optimizeCheckBox.Checked = OptimizeCheck.IsChecked == true; }
        private void ShowUsStateCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.showUsStateCheckBox.Checked = ShowUsStateCheck.IsChecked == true; }
        private void MyCallCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.mycallCheckBox.Checked = MyCallCheck.IsChecked == true; }
        private void LoggedCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.loggedCheckBox.Checked = LoggedCheck.IsChecked == true; }
        private void CallAddedCheck_Changed(object sender, RoutedEventArgs e)
        { if (!suppressEvents) ctrl.callAddedCheckBox.Checked = CallAddedCheck.IsChecked == true; }

        private void ExceptBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        { if (!suppressEvents) ctrl.exceptTextBox.Text = ExceptBox.Text; }

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
                CallQueueList.Focus();
                e.Handled = true;
                return;
            }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.NavLoggedList])
            {
                LoggedList.Focus();
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

            if (keyData == ctrl.hotkeyConfig[HotkeyAction.ListenMode]) { ctrl.listenModeButton_Click(null, null); e.Handled = true; return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.NextCall]) { ctrl.wsjtxClient.NextBestPriorityCall(); e.Handled = true; return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.TxPeriod]) { e.Handled = ctrl.wsjtxClient.ToggleTxFirst(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.HoldTimeout]) { e.Handled = ctrl.wsjtxClient.ToggleHoldCheckBox(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.PowerSwr]) { e.Handled = ctrl.wsjtxClient.ReportPowerSwr(); return; }
            if (keyData == ctrl.hotkeyConfig[HotkeyAction.TuneMode]) { e.Handled = ctrl.wsjtxClient.ToggleTuningProcess(); return; }
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
                CallQueueHeader.Text = string.IsNullOrEmpty(headerText) ? $"Available stations ({items.Count})" : headerText;
                callQueueItems.Clear();
                foreach (var it in items) callQueueItems.Add(it);
                if (preservedIndex >= 0 && preservedIndex < callQueueItems.Count) CallQueueList.SelectedIndex = preservedIndex;
            }));
        }

        public void RenderRawDecodes(List<string> items, List<string> keys, List<WsjtxClient.CallCategory> categories)
        {
            // Advanced Tx1/Tx2/Raw panes are deferred in this pass -- see migration report.
        }

        public void RenderAdvancedList(bool isTx1Side, string accessibleName, List<string> items, List<string> keys, List<WsjtxClient.CallCategory> categories)
        {
            // Advanced Tx1/Tx2/Raw panes are deferred in this pass -- see migration report.
        }

        public void RenderLoggedList(string headerText, List<string> items, List<string> keys)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LoggedHeader.Text = string.IsNullOrEmpty(headerText) ? $"Logged ({items.Count})" : headerText;
                loggedItems.Clear();
                foreach (var it in items) loggedItems.Add(it);
            }));
        }
    }
}
