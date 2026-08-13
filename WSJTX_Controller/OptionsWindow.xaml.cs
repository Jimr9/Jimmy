using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using WinKeys = System.Windows.Forms.Keys;

namespace WSJTX_Controller
{
    // A deliberately scoped-down Options window for this pass: Station (callsign/grid/audio
    // devices -- required just to get on the air), Radio (CAT/PTT, optional), and Hotkeys
    // (view + rebind). The WinForms OptionsDlg's other 14 categories (Sounds, Lookup/Data,
    // Uploads, Wanted Calls, Spot Watch, Awards, Frequencies editor, Advanced layout, etc.) are
    // deferred -- see the migration report. Their settings still load/save via ctrl's fields
    // with reasonable defaults; there is just no editor UI for them yet.
    //
    // Category-list + single-panel-host navigation (CategoryList/CategoryHost in the XAML),
    // matching the CURRENT WinForms Jimmy Test Options design -- not the older TabControl
    // layout. Each category panel is built once here and swapped into CategoryHost.Child on
    // selection, with AccessibleName set on the panel itself so JAWS/NVDA announce the category
    // name, not a re-announced dialog title (same fix as the WinForms ListBox+Panel redesign).
    public partial class OptionsWindow : Window
    {
        private readonly Controller ctrl;
        private HotkeyAction? captureAction;

        private TextBox MyCallBox, MyGridBox, RigModelBox, ComPortBox, BaudRateBox;
        private ComboBox AudioInCombo, AudioOutCombo, RadioModeCombo, PttMethodCombo;
        private CheckBox PttEnabledCheck, PollEnabledCheck;
        private ListBox HotkeyList;

        private CheckBox PskReporterCheck, MoveFocusToStatusCheck, CheckForUpdatesCheck, AlwaysOnTopCheck, DiagLogCheck;
        private TextBox MaxCallQueueAgeBox;

        private ComboBox DecodeDepthCombo;
        private TextBox DecodeFLowBox, DecodeFHighBox;
        private CheckBox DecodeApDecodeCheck, DecodeApCqOnlyCheck, DecodeSingleDecodeCheck;

        // Receive / Auto Reply
        private CheckBox IgnoreNonDxCheck, ReplyDxCheck, ReplyLocalCheck,
            ReplyDirCqCheck, ReplyRR73Check, IgnoreWeakSnrCheck, RemoveOnWeakSnrCheck;
        private RadioButton CqOnlyRadio, CqGridRadio, AnyMsgRadio;
        private ComboBox NewOnBandCombo;
        private TextBox AlertBox, ExceptBox;
        private TextBox MinSnrBox;

        // Transmit
        private CheckBox FreqCheck, SkipGridCheck, UseRR73Check, LogEarlyCheck, OptimizeCheck, HoldCheck;
        private TextBox TimeoutBox;
        private ComboBox PeriodCombo;

        // Advanced UI
        private CheckBox AdvancedLayoutCheck, AdvShowTx1Check, AdvShowTx2Check, AdvShowRawCheck, ShowSpotWatchCheck;

        // Sounds -- one row per event: enable checkbox + file textbox (Browse deferred; the
        // path can still be edited directly).
        private CheckBox SoundsEnabledCheck;
        private CheckBox SndCallAddedCheck, SndCallingMeCheck, SndLoggedCheck;
        private TextBox SndCallAddedFile, SndCallingMeFile, SndLoggedFile;

        private FrameworkElement generalPanel, receiveReplyPanel, transmitPanel, hotkeysPanel, advUiPanel, soundsPanel,
            radioPanel, decodeEnginePanel, decodePanel;

        public OptionsWindow(Controller controller)
        {
            ctrl = controller;
            InitializeComponent();

            generalPanel = BuildGeneralPanel();
            receiveReplyPanel = BuildReceiveReplyPanel();
            transmitPanel = BuildTransmitPanel();
            hotkeysPanel = BuildHotkeysPanel();
            advUiPanel = BuildAdvancedUiPanel();
            soundsPanel = BuildSoundsPanel();
            radioPanel = BuildRadioPanel();
            decodeEnginePanel = BuildDecodeEnginePanel();
            decodePanel = BuildDecodePanel();

            CategoryList.SelectedIndex = 0;
        }

        private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var panel = CategoryList.SelectedIndex switch
            {
                0 => generalPanel,
                1 => receiveReplyPanel,
                2 => transmitPanel,
                3 => hotkeysPanel,
                4 => advUiPanel,
                5 => soundsPanel,
                6 => radioPanel,
                7 => decodeEnginePanel,
                8 => decodePanel,
                _ => generalPanel,
            };
            CategoryHost.Child = panel;

            // Fire a LiveRegion-style name-changed announcement on the host itself so JAWS/NVDA
            // announce the new category, rather than staying silent (a plain Child swap has no
            // UIA event of its own) or re-announcing the whole dialog title.
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(CategoryHost)
                ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(CategoryHost);
            peer?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }

        private FrameworkElement BuildGeneralPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "General");

            PskReporterCheck = new CheckBox { Content = "PSK Reporter Enabled", Margin = new Thickness(0, 4, 0, 8), IsChecked = ctrl.wsjtxClient?.usePskReporter ?? true };
            AutomationProperties.SetName(PskReporterCheck, "PSK Reporter enabled");
            panel.Children.Add(PskReporterCheck);

            MoveFocusToStatusCheck = new CheckBox { Content = "Move focus to status after selecting a call", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.moveFocusToStatusOnCallSelect };
            panel.Children.Add(MoveFocusToStatusCheck);

            CheckForUpdatesCheck = new CheckBox { Content = "Check for updates on startup", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.checkForUpdatesOnStartup };
            panel.Children.Add(CheckForUpdatesCheck);

            AlwaysOnTopCheck = new CheckBox { Content = "Always on top", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.alwaysOnTop };
            panel.Children.Add(AlwaysOnTopCheck);

            DiagLogCheck = new CheckBox { Content = "Diagnostic logging", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.wsjtxClient?.diagLog ?? false };
            panel.Children.Add(DiagLogCheck);

            panel.Children.Add(new TextBlock { Text = "Max call-queue age (periods):", Margin = new Thickness(0, 8, 0, 4) });
            MaxCallQueueAgeBox = new TextBox { Text = ctrl.maxCallQueueAgePeriods.ToString(), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(MaxCallQueueAgeBox, "Max call-queue age in periods");
            panel.Children.Add(MaxCallQueueAgeBox);

            return panel;
        }

        // Group headers/layout mirror the baseline's 5 reparented GroupBoxes (Calling,
        // Replying, Directed CQ Alert, Reply Behavior, Block List) inside receiveReplyPanel --
        // these controls are never shown on the main window in baseline, only here.
        private FrameworkElement BuildReceiveReplyPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "Receive / Auto Reply");
            var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            GroupBox Group(string header)
            {
                var gb = new GroupBox { Header = header, Margin = new Thickness(0, 0, 0, 10) };
                var stack = new StackPanel { Margin = new Thickness(6) };
                gb.Content = stack;
                panel.Children.Add(gb);
                return gb;
            }
            StackPanel Inner(GroupBox gb) => (StackPanel)gb.Content;

            var callingGroup = Group("Calling");
            IgnoreNonDxCheck = new CheckBox { Content = "Ignore non-DX reply", IsChecked = ctrl.ignoreNonDxCheckBox.Checked };
            Inner(callingGroup).Children.Add(IgnoreNonDxCheck);

            var replyingGroup = Group("Replying");
            var replyingStack = Inner(replyingGroup);
            replyingStack.Children.Add(new TextBlock { Text = "Reply to new calls:" });
            var replyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            ReplyDxCheck = new CheckBox { Content = "DX stations", IsChecked = ctrl.replyDxCheckBox.Checked, Margin = new Thickness(0, 0, 16, 0) };
            ReplyLocalCheck = new CheckBox { Content = ctrl.replyLocalCheckBox.Text is { Length: > 0 } lc ? lc : "local continent", IsChecked = ctrl.replyLocalCheckBox.Checked };
            replyRow.Children.Add(ReplyDxCheck);
            replyRow.Children.Add(ReplyLocalCheck);
            replyingStack.Children.Add(replyRow);
            var bandRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            bandRow.Children.Add(new TextBlock { Text = "Band scope:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            NewOnBandCombo = new ComboBox { Width = 140 };
            NewOnBandCombo.Items.Add(new ComboBoxItem { Content = "Current band" });
            NewOnBandCombo.Items.Add(new ComboBoxItem { Content = "New on band" });
            NewOnBandCombo.SelectedIndex = ctrl.bandComboBox.SelectedIndex == 1 ? 1 : 0;
            bandRow.Children.Add(NewOnBandCombo);
            replyingStack.Children.Add(bandRow);
            replyingStack.Children.Add(new TextBlock { Text = "Include from messages:", Margin = new Thickness(0, 0, 0, 4) });
            var msgRow = new StackPanel { Orientation = Orientation.Horizontal };
            CqOnlyRadio = new RadioButton { Content = "CQ/73", GroupName = "MsgScope", IsChecked = ctrl.cqOnlyRadioButton.Checked, Margin = new Thickness(0, 0, 12, 0) };
            CqGridRadio = new RadioButton { Content = "CQ/grid", GroupName = "MsgScope", IsChecked = ctrl.cqGridRadioButton.Checked, Margin = new Thickness(0, 0, 12, 0) };
            AnyMsgRadio = new RadioButton { Content = "any", GroupName = "MsgScope", IsChecked = ctrl.anyMsgRadioButton.Checked };
            msgRow.Children.Add(CqOnlyRadio); msgRow.Children.Add(CqGridRadio); msgRow.Children.Add(AnyMsgRadio);
            replyingStack.Children.Add(msgRow);

            var directedGroup = Group("Directed CQ Alert");
            var directedStack = Inner(directedGroup);
            var directedRow = new StackPanel { Orientation = Orientation.Horizontal };
            ReplyDirCqCheck = new CheckBox { Content = "Queue directed CQ calls for:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            ReplyDirCqCheck.IsChecked = ctrl.replyDirCqCheckBox.Checked;
            AlertBox = new TextBox { Width = 150 };
            AutomationProperties.SetName(AlertBox, "Directed CQ codes to reply to, separated by spaces");
            AlertBox.Text = ctrl.alertTextBox.Text;
            directedRow.Children.Add(ReplyDirCqCheck);
            directedRow.Children.Add(AlertBox);
            directedStack.Children.Add(directedRow);

            var replyBehaviorGroup = Group("Reply Behavior");
            ReplyRR73Check = new CheckBox { Content = "Reply to RR73 msg", IsChecked = ctrl.replyRR73CheckBox.Checked };
            Inner(replyBehaviorGroup).Children.Add(ReplyRR73Check);

            var blockGroup = Group("Block List");
            var blockStack = Inner(blockGroup);
            var blockRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            blockRow.Children.Add(new TextBlock { Text = "Block any reply to:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            ExceptBox = new TextBox { Width = 200 };
            AutomationProperties.SetName(ExceptBox, "Callsigns to never call or reply to, separated by spaces");
            ExceptBox.Text = ctrl.exceptTextBox.Text;
            blockRow.Children.Add(ExceptBox);
            blockStack.Children.Add(blockRow);
            var snrRow = new StackPanel { Orientation = Orientation.Horizontal };
            IgnoreWeakSnrCheck = new CheckBox { Content = "Ignore SNR at or below", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            IgnoreWeakSnrCheck.IsChecked = ctrl.ignoreWeakSnrCheckBox.Checked;
            MinSnrBox = new TextBox { Width = 50, Text = ((int)ctrl.minSnrNumUpDown.Value).ToString() };
            AutomationProperties.SetName(MinSnrBox, "Weak signal SNR floor");
            snrRow.Children.Add(IgnoreWeakSnrCheck);
            snrRow.Children.Add(MinSnrBox);
            blockStack.Children.Add(snrRow);
            RemoveOnWeakSnrCheck = new CheckBox { Content = "Remove from list immediately when signal drops below floor", Margin = new Thickness(0, 6, 0, 0) };
            RemoveOnWeakSnrCheck.IsChecked = ctrl.removeOnWeakSnrCheckBox.Checked;
            blockStack.Children.Add(RemoveOnWeakSnrCheck);

            return scroll;
        }

        private FrameworkElement BuildTransmitPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "Transmit");

            FreqCheck = new CheckBox { Content = "Use best Tx frequency", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.freqCheckBox.Checked };
            panel.Children.Add(FreqCheck);
            SkipGridCheck = new CheckBox { Content = "Skip grid msg", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.skipGridCheckBox.Checked };
            panel.Children.Add(SkipGridCheck);
            UseRR73Check = new CheckBox { Content = "Use RR73 msg", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.useRR73CheckBox.Checked };
            panel.Children.Add(UseRR73Check);
            LogEarlyCheck = new CheckBox { Content = "Log early, after RRR", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.logEarlyCheckBox.Checked };
            panel.Children.Add(LogEarlyCheck);
            OptimizeCheck = new CheckBox { Content = "Optimize throughput", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.optimizeCheckBox.Checked };
            panel.Children.Add(OptimizeCheck);
            HoldCheck = new CheckBox { Content = "Hold", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.holdCheckBox.Checked };
            panel.Children.Add(HoldCheck);

            var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            limitRow.Children.Add(new TextBlock { Text = "Limit to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            TimeoutBox = new TextBox { Width = 40, Text = ((int)ctrl.timeoutNumUpDown.Value).ToString() };
            AutomationProperties.SetName(TimeoutBox, "Repeat limit");
            limitRow.Children.Add(TimeoutBox);
            limitRow.Children.Add(new TextBlock { Text = "repeated Tx", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            panel.Children.Add(limitRow);

            var periodRow = new StackPanel { Orientation = Orientation.Horizontal };
            periodRow.Children.Add(new TextBlock { Text = "Tx period:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            PeriodCombo = new ComboBox { Width = 100 };
            foreach (var s in new[] { "Even", "Odd", "Any" }) PeriodCombo.Items.Add(new ComboBoxItem { Content = s });
            PeriodCombo.SelectedIndex = Math.Max(0, ctrl.periodComboBox.SelectedIndex);
            periodRow.Children.Add(PeriodCombo);
            panel.Children.Add(periodRow);

            return panel;
        }

        private FrameworkElement BuildAdvancedUiPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "Advanced UI");

            panel.Children.Add(new TextBlock
            {
                Text = "Advanced layout replaces the single 'Stations calling' list with separate TX1/TX2/Raw decode lists (plus Spot Watch, its own independent toggle).",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });

            AdvancedLayoutCheck = new CheckBox { Content = "Use advanced call layout", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.advancedCallLayout };
            panel.Children.Add(AdvancedLayoutCheck);
            AdvShowTx1Check = new CheckBox { Content = "Show TX1 available stations", Margin = new Thickness(20, 0, 0, 8), IsChecked = ctrl.advShowTx1 };
            panel.Children.Add(AdvShowTx1Check);
            AdvShowTx2Check = new CheckBox { Content = "Show TX2 available stations", Margin = new Thickness(20, 0, 0, 8), IsChecked = ctrl.advShowTx2 };
            panel.Children.Add(AdvShowTx2Check);
            AdvShowRawCheck = new CheckBox { Content = "Show raw decodes", Margin = new Thickness(20, 0, 0, 8), IsChecked = ctrl.advShowRaw };
            panel.Children.Add(AdvShowRawCheck);
            ShowSpotWatchCheck = new CheckBox { Content = "Show Spot Watch (requires advanced layout)", Margin = new Thickness(20, 0, 0, 8), IsChecked = ctrl.Settings.ShowSpotWatch };
            panel.Children.Add(ShowSpotWatchCheck);

            return panel;
        }

        private FrameworkElement BuildSoundsPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "Sounds");

            SoundsEnabledCheck = new CheckBox { Content = "Sounds enabled", Margin = new Thickness(0, 0, 0, 10), IsChecked = ctrl.soundsEnabled };
            panel.Children.Add(SoundsEnabledCheck);

            (CheckBox, TextBox) SoundRow(string label, bool enabled, string file)
            {
                panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2), FontWeight = FontWeights.Bold });
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var chk = new CheckBox { Content = "Enabled", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), IsChecked = enabled };
                var box = new TextBox { Width = 200, Text = file };
                AutomationProperties.SetName(box, label + " sound file");
                row.Children.Add(chk);
                row.Children.Add(box);
                panel.Children.Add(row);
                return (chk, box);
            }

            (SndCallAddedCheck, SndCallAddedFile) = SoundRow("Call added", ctrl.callAddedCheckBox.Checked, ctrl.soundFile_CallAdded);
            (SndCallingMeCheck, SndCallingMeFile) = SoundRow("Calling me", ctrl.mycallCheckBox.Checked, ctrl.soundFile_CallingMe);
            (SndLoggedCheck, SndLoggedFile) = SoundRow("Logged", ctrl.loggedCheckBox.Checked, ctrl.soundFile_Logged);

            return panel;
        }

        private FrameworkElement BuildDecodePanel()
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "Decode");

            panel.Children.Add(new TextBlock
            {
                Text = "WSJT-X's own decode settings, for Jimmy Native. Decode depth takes effect immediately; the rest take effect the next time the engine restarts.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });

            panel.Children.Add(new TextBlock { Text = "Decode depth:", Margin = new Thickness(0, 0, 0, 4) });
            DecodeDepthCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, Width = 120 };
            AutomationProperties.SetName(DecodeDepthCombo, "Decode depth");
            foreach (var s in new[] { "Fast", "Normal", "Deep" }) DecodeDepthCombo.Items.Add(new ComboBoxItem { Content = s });
            DecodeDepthCombo.SelectedIndex = Math.Max(0, Math.Min(2, ctrl.Decode.DecodeDepth - 1));
            panel.Children.Add(DecodeDepthCombo);

            panel.Children.Add(new TextBlock { Text = "F Low (Hz):", Margin = new Thickness(0, 10, 0, 4) });
            DecodeFLowBox = new TextBox { Text = ctrl.Decode.DecodeFLowHz.ToString(), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(DecodeFLowBox, "Decode F Low Hz");
            panel.Children.Add(DecodeFLowBox);

            panel.Children.Add(new TextBlock { Text = "F High (Hz):", Margin = new Thickness(0, 10, 0, 4) });
            DecodeFHighBox = new TextBox { Text = ctrl.Decode.DecodeFHighHz.ToString(), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(DecodeFHighBox, "Decode F High Hz");
            panel.Children.Add(DecodeFHighBox);

            DecodeApDecodeCheck = new CheckBox { Content = "Enable AP", Margin = new Thickness(0, 12, 0, 8), IsChecked = ctrl.Decode.ApDecode };
            DecodeApDecodeCheck.Checked += (s, e) => DecodeApCqOnlyCheck.IsEnabled = true;
            DecodeApDecodeCheck.Unchecked += (s, e) => DecodeApCqOnlyCheck.IsEnabled = false;
            panel.Children.Add(DecodeApDecodeCheck);

            DecodeApCqOnlyCheck = new CheckBox { Content = "AP for CQ only (expert)", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.Decode.ApCqOnly, IsEnabled = ctrl.Decode.ApDecode };
            panel.Children.Add(DecodeApCqOnlyCheck);

            DecodeSingleDecodeCheck = new CheckBox { Content = "Single decode (+/- 25 Hz of RX offset)", Margin = new Thickness(0, 0, 0, 8), IsChecked = ctrl.Decode.SingleDecode };
            panel.Children.Add(DecodeSingleDecodeCheck);

            return panel;
        }

        private FrameworkElement BuildDecodeEnginePanel()
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "Decode Engine");

            panel.Children.Add(new TextBlock { Text = "Your callsign:", Margin = new Thickness(0, 4, 0, 4) });
            MyCallBox = new TextBox();
            AutomationProperties.SetName(MyCallBox, "Your callsign");
            MyCallBox.Text = ctrl.NativeEngine.MyCall;
            panel.Children.Add(MyCallBox);

            panel.Children.Add(new TextBlock { Text = "Your Maidenhead grid square:", Margin = new Thickness(0, 8, 0, 4) });
            MyGridBox = new TextBox();
            AutomationProperties.SetName(MyGridBox, "Your grid square");
            MyGridBox.Text = ctrl.NativeEngine.MyGrid;
            panel.Children.Add(MyGridBox);

            bool engineSessionActive = ctrl.nativeEngineClient != null && ctrl.nativeEngineClient.Running;

            panel.Children.Add(new TextBlock { Text = "Audio input device (from radio):", Margin = new Thickness(0, 8, 0, 4) });
            AudioInCombo = new ComboBox { IsEditable = true };
            AutomationProperties.SetName(AudioInCombo, "Audio input device");
            AudioInCombo.Items.Add("");
            foreach (var dev in NativeEngineClient.ListAudioDevices(engineSessionActive)) AudioInCombo.Items.Add(dev);
            AudioInCombo.Text = ctrl.NativeEngine.AudioInputDevice;
            panel.Children.Add(AudioInCombo);

            panel.Children.Add(new TextBlock { Text = "Audio output device (to radio, TX):", Margin = new Thickness(0, 8, 0, 4) });
            AudioOutCombo = new ComboBox { IsEditable = true };
            AutomationProperties.SetName(AudioOutCombo, "Audio output device");
            AudioOutCombo.Items.Add("");
            foreach (var dev in NativeEngineClient.ListOutputAudioDevices(engineSessionActive)) AudioOutCombo.Items.Add(dev);
            AudioOutCombo.Text = ctrl.NativeEngine.AudioOutputDevice;
            panel.Children.Add(AudioOutCombo);

            return panel;
        }

        private FrameworkElement BuildRadioPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "Radio");

            panel.Children.Add(new TextBlock { Text = "Radio control mode:", Margin = new Thickness(0, 4, 0, 4) });
            RadioModeCombo = new ComboBox();
            AutomationProperties.SetName(RadioModeCombo, "Radio control mode");
            RadioModeCombo.Items.Add(new ComboBoxItem { Content = "WSJT-X style (no separate CAT link)" });
            RadioModeCombo.Items.Add(new ComboBoxItem { Content = "Hamlib rigctld (real CAT control)" });
            RadioModeCombo.SelectedIndex = ctrl.Radio.Mode == RadioControlMode.HamlibRigctld ? 1 : 0;
            panel.Children.Add(RadioModeCombo);

            panel.Children.Add(new TextBlock { Text = "Rig model (Hamlib):", Margin = new Thickness(0, 8, 0, 4) });
            RigModelBox = new TextBox { Text = ctrl.Radio.RigModel };
            AutomationProperties.SetName(RigModelBox, "Rig model");
            panel.Children.Add(RigModelBox);

            panel.Children.Add(new TextBlock { Text = "COM port:", Margin = new Thickness(0, 8, 0, 4) });
            ComPortBox = new TextBox { Text = ctrl.Radio.ComPort };
            AutomationProperties.SetName(ComPortBox, "Radio COM port");
            panel.Children.Add(ComPortBox);

            panel.Children.Add(new TextBlock { Text = "Baud rate:", Margin = new Thickness(0, 8, 0, 4) });
            BaudRateBox = new TextBox { Text = ctrl.Radio.BaudRate };
            AutomationProperties.SetName(BaudRateBox, "Radio baud rate");
            panel.Children.Add(BaudRateBox);

            PttEnabledCheck = new CheckBox { Content = "Enable PTT via this connection", Margin = new Thickness(0, 10, 0, 4), IsChecked = ctrl.Radio.PttEnabled };
            AutomationProperties.SetName(PttEnabledCheck, "Enable PTT");
            panel.Children.Add(PttEnabledCheck);

            panel.Children.Add(new TextBlock { Text = "PTT method:", Margin = new Thickness(0, 8, 0, 4) });
            PttMethodCombo = new ComboBox();
            AutomationProperties.SetName(PttMethodCombo, "PTT method");
            foreach (var s in new[] { "CAT", "VOX", "Serial RTS", "Serial DTR" }) PttMethodCombo.Items.Add(new ComboBoxItem { Content = s });
            PttMethodCombo.SelectedIndex = (int)ctrl.Radio.PttMethod;
            panel.Children.Add(PttMethodCombo);

            PollEnabledCheck = new CheckBox { Content = "Poll radio for S-meter / SWR / power", Margin = new Thickness(0, 10, 0, 4), IsChecked = ctrl.Radio.PollEnabled };
            AutomationProperties.SetName(PollEnabledCheck, "Poll radio status");
            panel.Children.Add(PollEnabledCheck);

            return panel;
        }

        private FrameworkElement BuildHotkeysPanel()
        {
            var panel = new DockPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "Hotkeys");

            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(buttonRow, Dock.Bottom);
            var changeBtn = new Button { Content = "_Change selected...", Width = 140, Margin = new Thickness(0, 0, 8, 0) };
            changeBtn.Click += ChangeHotkeyButton_Click;
            var resetBtn = new Button { Content = "Reset all to defaults", Width = 140 };
            resetBtn.Click += ResetHotkeysButton_Click;
            buttonRow.Children.Add(changeBtn);
            buttonRow.Children.Add(resetBtn);
            panel.Children.Add(buttonRow);

            HotkeyList = new ListBox();
            AutomationProperties.SetName(HotkeyList, "Keyboard shortcuts");
            panel.Children.Add(HotkeyList);

            RefreshHotkeyList();
            PreviewKeyDown += OptionsWindow_PreviewKeyDown;

            return panel;
        }

        private void RefreshHotkeyList()
        {
            HotkeyList.Items.Clear();
            foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            {
                string name = HotkeyConfig.DisplayNames.TryGetValue(action, out var n) ? n : action.ToString();
                string keys = HotkeyConfig.FormatKeys(ctrl.hotkeyConfig[action]);
                HotkeyList.Items.Add(new HotkeyRow { Action = action, Display = $"{name}: {(string.IsNullOrEmpty(keys) ? "(none)" : keys)}" });
            }
            HotkeyList.DisplayMemberPath = nameof(HotkeyRow.Display);
        }

        private class HotkeyRow
        {
            public HotkeyAction Action;
            public string Display;
            public override string ToString() => Display;
        }

        private void ChangeHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(HotkeyList.SelectedItem is HotkeyRow row)) return;
            captureAction = row.Action;
            ctrl.ShowMsg($"Press the new key combination for '{HotkeyConfig.DisplayNames[row.Action]}', or Escape to cancel.", false);
        }

        private void OptionsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (captureAction == null) return;
            e.Handled = true;

            Key effectiveKey = e.Key == Key.System ? e.SystemKey : e.Key;
            if (effectiveKey == Key.Escape) { captureAction = null; return; }
            if (effectiveKey == Key.LeftCtrl || effectiveKey == Key.RightCtrl || effectiveKey == Key.LeftAlt
                || effectiveKey == Key.RightAlt || effectiveKey == Key.LeftShift || effectiveKey == Key.RightShift)
                return;

            var formsKey = (WinKeys)KeyInterop.VirtualKeyFromKey(effectiveKey);
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) formsKey |= WinKeys.Control;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) formsKey |= WinKeys.Alt;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) formsKey |= WinKeys.Shift;

            var action = captureAction.Value;
            captureAction = null;

            if (!HotkeyConfig.IsValid(formsKey))
            {
                ctrl.ShowMsg("That key combination is not usable as a hotkey.", true);
                return;
            }
            if (HotkeyConfig.IsReserved(formsKey))
            {
                ctrl.ShowMsg("That key combination is reserved by Windows.", true);
                return;
            }
            var conflict = ctrl.hotkeyConfig.FindConflict(formsKey, action);
            if (conflict != null)
            {
                ctrl.ShowMsg($"That key combination is already used by '{HotkeyConfig.DisplayNames[conflict.Value]}'.", true);
                return;
            }

            ctrl.hotkeyConfig.Apply(action, formsKey);
            ctrl.ShowMsg($"'{HotkeyConfig.DisplayNames[action]}' set to {HotkeyConfig.FormatKeys(formsKey)}.", false);
            RefreshHotkeyList();
        }

        private void ResetHotkeysButton_Click(object sender, RoutedEventArgs e)
        {
            ctrl.hotkeyConfig.ResetToDefaults();
            RefreshHotkeyList();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (PskReporterCheck.IsChecked != ctrl.wsjtxClient?.usePskReporter) ctrl.wsjtxClient?.TogglePskReporter();
            ctrl.moveFocusToStatusOnCallSelect = MoveFocusToStatusCheck.IsChecked == true;
            ctrl.checkForUpdatesOnStartup = CheckForUpdatesCheck.IsChecked == true;
            ctrl.alwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
            Owner.Topmost = ctrl.alwaysOnTop;
            ctrl.wsjtxClient?.LogModeChanged(DiagLogCheck.IsChecked == true);
            if (int.TryParse(MaxCallQueueAgeBox.Text, out int maxAge))
                ctrl.maxCallQueueAgePeriods = Math.Max(4, Math.Min(200, maxAge));

            bool decodeChanged = ctrl.Decode.DecodeDepth != DecodeDepthCombo.SelectedIndex + 1;
            ctrl.Decode.DecodeDepth = DecodeDepthCombo.SelectedIndex + 1;
            if (int.TryParse(DecodeFLowBox.Text, out int fLow)) { decodeChanged |= ctrl.Decode.DecodeFLowHz != fLow; ctrl.Decode.DecodeFLowHz = Math.Max(200, Math.Min(3900, fLow)); }
            if (int.TryParse(DecodeFHighBox.Text, out int fHigh)) { decodeChanged |= ctrl.Decode.DecodeFHighHz != fHigh; ctrl.Decode.DecodeFHighHz = Math.Max(200, Math.Min(3900, fHigh)); }
            decodeChanged |= ctrl.Decode.ApDecode != (DecodeApDecodeCheck.IsChecked == true);
            ctrl.Decode.ApDecode = DecodeApDecodeCheck.IsChecked == true;
            decodeChanged |= ctrl.Decode.ApCqOnly != (DecodeApCqOnlyCheck.IsChecked == true);
            ctrl.Decode.ApCqOnly = DecodeApCqOnlyCheck.IsChecked == true;
            decodeChanged |= ctrl.Decode.SingleDecode != (DecodeSingleDecodeCheck.IsChecked == true);
            ctrl.Decode.SingleDecode = DecodeSingleDecodeCheck.IsChecked == true;

            bool stationChanged = ctrl.NativeEngine.MyCall != MyCallBox.Text.Trim()
                || ctrl.NativeEngine.MyGrid != MyGridBox.Text.Trim()
                || ctrl.NativeEngine.AudioInputDevice != AudioInCombo.Text.Trim()
                || ctrl.NativeEngine.AudioOutputDevice != AudioOutCombo.Text.Trim();

            ctrl.NativeEngine.MyCall = MyCallBox.Text.Trim().ToUpperInvariant();
            ctrl.NativeEngine.MyGrid = MyGridBox.Text.Trim().ToUpperInvariant();
            ctrl.NativeEngine.AudioInputDevice = AudioInCombo.Text.Trim();
            ctrl.NativeEngine.AudioOutputDevice = AudioOutCombo.Text.Trim();

            bool radioChanged = ctrl.Radio.Mode != (RadioModeCombo.SelectedIndex == 1 ? RadioControlMode.HamlibRigctld : RadioControlMode.WsjtxCat)
                || ctrl.Radio.RigModel != RigModelBox.Text.Trim()
                || ctrl.Radio.ComPort != ComPortBox.Text.Trim()
                || ctrl.Radio.BaudRate != BaudRateBox.Text.Trim()
                || ctrl.Radio.PttEnabled != (PttEnabledCheck.IsChecked == true)
                || ctrl.Radio.PttMethod != (PttMethod)PttMethodCombo.SelectedIndex;

            ctrl.Radio.Mode = RadioModeCombo.SelectedIndex == 1 ? RadioControlMode.HamlibRigctld : RadioControlMode.WsjtxCat;
            ctrl.Radio.RigModel = RadioSettings.ExtractRigModelId(RigModelBox.Text.Trim());
            ctrl.Radio.ComPort = ComPortBox.Text.Trim();
            ctrl.Radio.BaudRate = BaudRateBox.Text.Trim();
            ctrl.Radio.PttEnabled = PttEnabledCheck.IsChecked == true;
            ctrl.Radio.PttMethod = (PttMethod)PttMethodCombo.SelectedIndex;
            ctrl.Radio.PollEnabled = PollEnabledCheck.IsChecked == true;

            // Receive / Auto Reply -- setting .Checked directly (not through a WPF binding)
            // fires the SAME PropertyChanged-driven coupling rules the WinForms CheckedChanged
            // handlers used to (WireCheckboxCoupling in Controller.Startup.cs), so e.g. "at
            // least one CQ type must stay selected" still holds here exactly as in baseline.
            ctrl.ignoreNonDxCheckBox.Checked = IgnoreNonDxCheck.IsChecked == true;
            ctrl.replyDxCheckBox.Checked = ReplyDxCheck.IsChecked == true;
            ctrl.replyLocalCheckBox.Checked = ReplyLocalCheck.IsChecked == true;
            ctrl.bandComboBox.SelectedIndex = NewOnBandCombo.SelectedIndex;
            ctrl.cqOnlyRadioButton.Checked = CqOnlyRadio.IsChecked == true;
            ctrl.cqGridRadioButton.Checked = CqGridRadio.IsChecked == true;
            ctrl.anyMsgRadioButton.Checked = AnyMsgRadio.IsChecked == true;
            ctrl.alertTextBox.Text = AlertBox.Text;
            ctrl.replyDirCqCheckBox.Checked = ReplyDirCqCheck.IsChecked == true;
            ctrl.replyRR73CheckBox.Checked = ReplyRR73Check.IsChecked == true;
            ctrl.exceptTextBox.Text = ExceptBox.Text;
            ctrl.ignoreWeakSnrCheckBox.Checked = IgnoreWeakSnrCheck.IsChecked == true;
            if (int.TryParse(MinSnrBox.Text, out int minSnr)) ctrl.minSnrNumUpDown.Value = Math.Max(-30, Math.Min(20, minSnr));
            ctrl.removeOnWeakSnrCheckBox.Checked = RemoveOnWeakSnrCheck.IsChecked == true;
            ctrl.SaveSetting("ignoreWeakSnr", ctrl.ignoreWeakSnrCheckBox.Checked.ToString());
            ctrl.SaveSetting("minSnr", ((int)ctrl.minSnrNumUpDown.Value).ToString());
            ctrl.SaveSetting("removeOnWeakSnr", ctrl.removeOnWeakSnrCheckBox.Checked.ToString());

            // Transmit
            ctrl.freqCheckBox.Checked = FreqCheck.IsChecked == true;
            ctrl.skipGridCheckBox.Checked = SkipGridCheck.IsChecked == true;
            ctrl.SaveSetting("skipGrid", ctrl.skipGridCheckBox.Checked.ToString());
            ctrl.useRR73CheckBox.Checked = UseRR73Check.IsChecked == true;
            ctrl.logEarlyCheckBox.Checked = LogEarlyCheck.IsChecked == true;
            ctrl.optimizeCheckBox.Checked = OptimizeCheck.IsChecked == true;
            ctrl.holdCheckBox.Checked = HoldCheck.IsChecked == true;
            if (int.TryParse(TimeoutBox.Text, out int timeout)) ctrl.timeoutNumUpDown.Value = timeout;
            ctrl.periodComboBox.SelectedIndex = PeriodCombo.SelectedIndex;

            // Advanced UI -- main-window layout toggles; MainWindow re-applies immediately,
            // matching the WinForms original's ctrl.ApplyAdvancedLayout() call here.
            ctrl.advancedCallLayout = AdvancedLayoutCheck.IsChecked == true;
            ctrl.advShowTx1 = AdvShowTx1Check.IsChecked == true;
            ctrl.advShowTx2 = AdvShowTx2Check.IsChecked == true;
            ctrl.advShowRaw = AdvShowRawCheck.IsChecked == true;
            ctrl.Settings.ShowSpotWatch = ShowSpotWatchCheck.IsChecked == true;
            (Owner as MainWindow)?.RefreshLayout();

            // Sounds
            ctrl.soundsEnabled = SoundsEnabledCheck.IsChecked == true;
            ctrl.callAddedCheckBox.Checked = SndCallAddedCheck.IsChecked == true;
            ctrl.soundFile_CallAdded = SndCallAddedFile.Text.Trim();
            ctrl.mycallCheckBox.Checked = SndCallingMeCheck.IsChecked == true;
            ctrl.soundFile_CallingMe = SndCallingMeFile.Text.Trim();
            ctrl.loggedCheckBox.Checked = SndLoggedCheck.IsChecked == true;
            ctrl.soundFile_Logged = SndLoggedFile.Text.Trim();

            ctrl.SaveHotkeyConfig();

            if (radioChanged) ctrl.ApplyRadioSettings();
            if (stationChanged || decodeChanged) ctrl.ApplyEngineMode();

            DialogResult = true;
            Close();
        }
    }
}
