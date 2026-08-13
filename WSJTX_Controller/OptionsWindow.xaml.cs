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

        private FrameworkElement stationPanel, radioPanel, hotkeysPanel;

        public OptionsWindow(Controller controller)
        {
            ctrl = controller;
            InitializeComponent();

            stationPanel = BuildStationPanel();
            radioPanel = BuildRadioPanel();
            hotkeysPanel = BuildHotkeysPanel();

            CategoryList.SelectedIndex = 0;
        }

        private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var panel = CategoryList.SelectedIndex switch
            {
                0 => stationPanel,
                1 => radioPanel,
                2 => hotkeysPanel,
                _ => stationPanel,
            };
            CategoryHost.Child = panel;

            // Fire a LiveRegion-style name-changed announcement on the host itself so JAWS/NVDA
            // announce the new category, rather than staying silent (a plain Child swap has no
            // UIA event of its own) or re-announcing the whole dialog title.
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(CategoryHost)
                ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(CategoryHost);
            peer?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }

        private FrameworkElement BuildStationPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "Station");

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

            ctrl.SaveHotkeyConfig();

            if (radioChanged) ctrl.ApplyRadioSettings();
            if (stationChanged) ctrl.ApplyEngineMode();

            DialogResult = true;
            Close();
        }
    }
}
