using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace WSJTX_Controller
{
    // WPF port of CallCqDlg -- the main-window "Call CQ options..." button target. Configures
    // what kind of CQ Jimmy calls and which transmit slot it uses; opening/closing this window
    // never starts or stops calling CQ itself (Alt+C keeps doing that unchanged). Uses its own
    // fresh controls rather than binding directly to ctrl's shared callNonDirCqCheckBox/
    // callCqDxCheckBox/callDirCqCheckBox/directedTextBox: those are read on open and written
    // back on OK, matching the WinForms original's own documented reasoning (reparenting the
    // shared controls across windows there was found unsafe -- kept the same one-way-in/
    // one-way-out shape here even though WPF's own reparenting issues may differ, since the
    // shared fields are also driven independently by hotkeys/business logic while this window
    // might be open).
    public partial class CallCqWindow : Window
    {
        private readonly Controller ctrl;
        private readonly WsjtxClient wc;
        private readonly DispatcherTimer slotStatusTimer;
        private const string IniPlaceholder = "(separate by spaces)";

        public CallCqWindow(Controller controller, WsjtxClient wsjtxClient)
        {
            ctrl = controller;
            wc = wsjtxClient;
            InitializeComponent();
            KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };

            NonDirCqCheck.IsChecked = ctrl.callNonDirCqCheckBox.Checked;
            CqDxCheck.IsChecked = ctrl.callCqDxCheckBox.Checked;
            DirCqCheck.IsChecked = ctrl.callDirCqCheckBox.Checked;
            string currentDirText = ctrl.directedTextBox.Text;
            DirectedBox.Text = currentDirText == IniPlaceholder ? "" : currentDirText;
            DirectedBox.IsEnabled = ctrl.callDirCqCheckBox.Checked;
            DirCqCheck.Checked += (s, e) => { DirectedBox.IsEnabled = true; RefreshDirectedCombo(false); };
            DirCqCheck.Unchecked += (s, e) => { DirectedBox.IsEnabled = false; RefreshDirectedCombo(false); };
            DirectedBox.TextChanged += (s, e) => RefreshDirectedCombo(false);

            SlotComboBox.SelectedIndex = wc.txFirst ? 0 : 1;

            string slotKeyText = ctrl.hotkeyConfig != null && ctrl.hotkeyConfig[HotkeyAction.AnalyzeSlot] != System.Windows.Forms.Keys.None
                ? HotkeyConfig.FormatKeys(ctrl.hotkeyConfig[HotkeyAction.AnalyzeSlot])
                : "no hotkey assigned";
            FindSlotButton.Content = $"Find open slot ({slotKeyText})";
            AutomationProperties.SetName(FindSlotButton, $"Find open transmit slot, {slotKeyText}");

            RefreshDirectedCombo(true);
            RefreshSlotStatusLabel();

            slotStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            slotStatusTimer.Tick += (s, e) => RefreshSlotStatusLabel();
            slotStatusTimer.Start();
            Closed += (s, e) => slotStatusTimer.Stop();
        }

        private string[] LocalDirectedEntries() =>
            DirectedBox.Text.Trim().ToUpper().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        private void RefreshDirectedCombo(bool selectLocked)
        {
            string keepSelected = !selectLocked ? DirectedComboBox.SelectedItem as string : null;
            string[] entries = DirCqCheck.IsChecked == true ? LocalDirectedEntries() : Array.Empty<string>();

            DirectedComboBox.Items.Clear();
            DirectedComboBox.Items.Add("Random");
            foreach (string entry in entries) DirectedComboBox.Items.Add(entry);

            string wantSelected = selectLocked ? ctrl.directedCqLockedEntry : keepSelected;
            int idx = !string.IsNullOrEmpty(wantSelected) ? DirectedComboBox.Items.IndexOf(wantSelected) : -1;
            DirectedComboBox.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void FindSlotButton_Click(object sender, RoutedEventArgs e)
        {
            wc.StartSlotAnalysis(false);
            RefreshSlotStatusLabel();
        }

        private void RefreshSlotStatusLabel()
        {
            if (!ctrl.freqCheckBox.Checked)
                SlotStatusLabel.Text = "Slot finding is off (enable 'Use best Tx frequency' in Options to use it).";
            else if (wc.AnalysisNeeded)
                SlotStatusLabel.Text = "Not yet checked this session.";
            else
                SlotStatusLabel.Text = "Already checked this session.";
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            bool wantNonDir = NonDirCqCheck.IsChecked == true;
            bool wantCqDx = CqDxCheck.IsChecked == true;
            bool wantDirCq = DirCqCheck.IsChecked == true;
            if (!wantNonDir && !wantCqDx && !wantDirCq) wantNonDir = true;

            ctrl.callNonDirCqCheckBox.Checked = wantNonDir;
            ctrl.callCqDxCheckBox.Checked = wantCqDx;
            ctrl.callDirCqCheckBox.Checked = wantDirCq;
            ctrl.directedTextBox.Text = DirectedBox.Text.Trim();

            string selected = DirectedComboBox.SelectedItem as string;
            ctrl.directedCqLockedEntry = (string.IsNullOrEmpty(selected) || selected == "Random") ? "" : selected;

            bool desiredTxFirst = SlotComboBox.SelectedIndex == 0;
            if (desiredTxFirst != wc.txFirst) wc.ToggleTxFirst();

            Close();
        }
    }
}
