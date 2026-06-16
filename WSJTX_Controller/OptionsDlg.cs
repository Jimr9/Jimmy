using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace WSJTX_Controller
{
    public partial class OptionsDlg : Form
    {
        private Color normalFore;
        private Color normalBack;
        private Color highlightFore;
        private Color highlightBack;
        private Color highlightBackDisabled;

        private bool cqButtonEnabled = false;
        private bool activatorEnabled = false;
        private bool hunterEnabled = false;
        private bool cqDxButtonEnabled = false;
        private bool nonDxButtonEnabled = false;
        private bool dxButtonEnabled = false;
        private bool dxccButtonEnabled = false;

        private List<CheckBox> disableList;

        private WsjtxClient wsjtxClient;
        private Controller ctrl;

        private bool startOnUdpTab;

        private const int HotkeysTabIndex      = 1;
        private const int AdvUiTabIndex        = 3;
        private const int WantedCallsTabIndex  = 4;
        private const int SoundsTabIndex       = 5;
        private const int UdpTabIndex          = 6;

        // Advanced UI tab — controls created dynamically in BuildAdvancedUiTab()
        private System.Windows.Forms.CheckBox advCallLayoutCheckBox;
        private System.Windows.Forms.CheckBox advShowTx1CheckBox;
        private System.Windows.Forms.CheckBox advShowTx2CheckBox;
        private System.Windows.Forms.CheckBox advShowRawCheckBox;
        private System.Windows.Forms.NumericUpDown rawMaxRowsNumeric;
        private System.Windows.Forms.CheckBox rawShowCqCheckBox;
        private System.Windows.Forms.CheckBox rawShowDirectedCheckBox;
        private System.Windows.Forms.CheckBox rawShowReportsCheckBox;
        private System.Windows.Forms.CheckBox rawShowRR73CheckBox;
        private System.Windows.Forms.CheckBox rawShow73CheckBox;
        private System.Windows.Forms.CheckBox rawShowPotaCheckBox;
        private System.Windows.Forms.CheckBox rawShowSotaCheckBox;
        private System.Windows.Forms.CheckBox rawShowDxCheckBox;
        private System.Windows.Forms.CheckBox rawShowSnrCheckBox;
        private System.Windows.Forms.CheckBox rawShowGridCheckBox;
        private System.Windows.Forms.CheckBox rawShowCountryCheckBox;
        private System.Windows.Forms.CheckBox rawShowStateCheckBox;
        private System.Windows.Forms.CheckBox rawShowDistAzCheckBox;
        private System.Windows.Forms.CheckBox rawOnlyCallsignsCheckBox;
        private System.Windows.Forms.CheckBox rawOnlyUnworkedCheckBox;
        private System.Windows.Forms.CheckBox rawOnlyRankedCheckBox;
        private System.Windows.Forms.CheckBox rawPriorityTagsCheckBox;
        private List<System.Windows.Forms.Control> _advUiDependentControls;

        // Sounds tab state
        private List<SoundRow> _soundRows;

        private sealed class SoundRow
        {
            public string Key;
            public System.Windows.Forms.CheckBox EnabledCb;
            public System.Windows.Forms.TextBox  FileTb;
        }

        // Hotkeys tab state
        private Dictionary<HotkeyAction, Keys> _pendingKeys;
        private List<HotkeyAction?>             _listActionMap;
        private HotkeyCaptureBox                _sharedCaptureBox;
        private ListBox                         _actionListBox;

        // Wanted Calls tab
        private System.Windows.Forms.TextBox wantedCallsTextBox;

        private Dictionary<Control, Control> originalParents = new Dictionary<Control, Control>();
        private Dictionary<Control, Point> originalLocations = new Dictionary<Control, Point>();
        private List<Control> reparentedControls = new List<Control>();

        public OptionsDlg(WsjtxClient wsjtxClient, Controller ctrl, bool startOnUdpTab = false)
        {
            InitializeComponent();

            this.wsjtxClient = wsjtxClient;
            this.ctrl = ctrl;
            this.startOnUdpTab = startOnUdpTab;

            normalFore = okButton.ForeColor;
            normalBack = okButton.BackColor;
            highlightFore = Color.White;
            highlightBack = Color.Gray;
            highlightBackDisabled = Color.LightGray;

            disableList = new List<CheckBox>
            {
                listenButton, callCqButton,
                cqButton, cqDxButton,
                dxButton, nonDxButton,
                potaButton, hunterButton,
                allButton, recentButton
            };
        }

        public void UpdateView()
        {
            UpdateAllButtons();
        }

        public void SelectUdpTab()
        {
            tabControl1.SelectedIndex = UdpTabIndex;
        }

        private void OptionsDlg_Load(object sender, EventArgs e)
        {
            Screen screen = Screen.FromControl(ctrl);
            Location = new Point(
                screen.Bounds.X + (screen.Bounds.Width - Width) / 2,
                screen.Bounds.Y + (screen.Bounds.Height - Height) / 2);

            LoadUdpTab();
            BuildHotkeysTab();
            BuildAdvancedUiTab();
            BuildWantedCallsTab();
            BuildSoundsTab();
            ReparentControlsToDialog();

            UpdateAllButtons();
            dxccButtonEnabled =
                wsjtxClient.txMode == WsjtxClient.TxModes.LISTEN &&
                ctrl.periodComboBox.SelectedIndex == (int)WsjtxClient.ListenModeTxPeriods.ANY &&
                ctrl.replyNewDxccCheckBox.Checked &&
                ctrl.replyNewOnlyCheckBox.Checked;
            UpdateAllButtons();

            if (startOnUdpTab)
                tabControl1.SelectedIndex = UdpTabIndex;
            else
                subtitleLabel.Focus();
        }

        private void LoadUdpTab()
        {
            udpOverrideCheckBox.Checked = wsjtxClient.overrideUdpDetect;
            if (wsjtxClient.ipAddress != null) udpAddrTextBox.Text = wsjtxClient.ipAddress.ToString();
            if (wsjtxClient.port != 0) udpPortTextBox.Text = wsjtxClient.port.ToString();
            udpMulticastCheckBox.Checked = wsjtxClient.multicast;
            udpMulticastCheckBox_CheckedChanged(null, null);
            udpOnTopCheckBox.Checked = ctrl.alwaysOnTop;
            udpDiagLogCheckBox.Checked = wsjtxClient.diagLog;
            udpOverrideCheckBox_CheckedChanged(null, null);
        }

        private void OptionsDlg_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (dxButtonEnabled || nonDxButtonEnabled)
            {
                ctrl.cqOnlyRadioButton.Checked = true;
                ctrl.bandComboBox.SelectedIndex = (int)WsjtxClient.NewCallBands.ANY;
            }
        }

        private void OptionsDlg_FormClosed(object sender, FormClosedEventArgs e)
        {
            ReparentControlsBack();
            ctrl.OptionsDlgClosed();
        }

        private void OptionsDlg_KeyDown(object sender, KeyEventArgs e)
        {
            // When the capture box has focus, let the key pass through to it.
            if (IsCaptureFieldFocused()) return;
            if (e.Control && e.KeyCode == Keys.Q) Close();
        }

        // ===== OK / CANCEL =====

        private void okButton_Click(object sender, EventArgs e)
        {
            if (!ApplyUdpSettings()) return;
            if (!ValidateHotkeys()) return;
            SaveHotkeysTab();
            SaveAdvancedUiTab();
            SaveWantedCallsTab();
            SaveSoundsTab();
            ctrl.ApplyAdvancedLayout();
            Close();
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private bool ApplyUdpSettings()
        {
            bool multicast = udpMulticastCheckBox.Checked;
            bool overrideUdp = udpOverrideCheckBox.Checked;
            UInt16 port;
            IPAddress ipAddress;

            if (!UInt16.TryParse(udpPortTextBox.Text, out port))
            {
                MessageBox.Show("A port number must be between 0 and 65535.\n\nExample: 2237",
                    wsjtxClient.pgmName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                tabControl1.SelectedIndex = UdpTabIndex;
                udpPortTextBox.Focus();
                return false;
            }

            var a = udpAddrTextBox.Text.Split('.');
            if (a.Length != 4)
            {
                string ex = multicast ? "239.255.0.0" : "127.0.0.1";
                MessageBox.Show($"An IP address must be 4 numbers between 0 and 255, each separated by a period.\n\nExample: {ex}",
                    wsjtxClient.pgmName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                tabControl1.SelectedIndex = UdpTabIndex;
                udpAddrTextBox.Focus();
                return false;
            }

            if (overrideUdp && multicast && (a[0] != "239" || a[1] != "255"))
            {
                MessageBox.Show("Multicast addresses must start with '239.255'.\n\nExample: 239.255.0.0",
                    wsjtxClient.pgmName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                tabControl1.SelectedIndex = UdpTabIndex;
                udpAddrTextBox.Focus();
                return false;
            }

            try
            {
                ipAddress = IPAddress.Parse(udpAddrTextBox.Text);
            }
            catch
            {
                string ex = multicast ? "239.255.0.0" : "127.0.0.1";
                MessageBox.Show($"An IP address must be 4 numbers between 0 and 255, each separated by a period.\n\nExample: {ex}",
                    wsjtxClient.pgmName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                tabControl1.SelectedIndex = UdpTabIndex;
                udpAddrTextBox.Focus();
                return false;
            }

            ctrl.alwaysOnTop = udpOnTopCheckBox.Checked;
            wsjtxClient.LogModeChanged(udpDiagLogCheckBox.Checked);

            if (wsjtxClient.ipAddress.ToString() == ipAddress.ToString() &&
                wsjtxClient.port == port &&
                wsjtxClient.multicast == multicast &&
                wsjtxClient.overrideUdpDetect == overrideUdp)
            {
                return true;
            }

            wsjtxClient.UpdateAddrPortMulti(ipAddress, port, multicast, overrideUdp);
            return true;
        }

        // ===== UDP TAB EVENT HANDLERS =====

        private void udpMulticastCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            udpAddrLabel.Text = udpMulticastCheckBox.Checked
                ? "(Standard: 239.255.0.0)"
                : "(Standard: 127.0.0.1)";
        }

        private void udpOverrideCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            udpAddrTextBox.Enabled = udpPortTextBox.Enabled = udpMulticastCheckBox.Enabled =
                udpOverrideCheckBox.Checked;
        }

        private void udpHelpButton_Click(object sender, EventArgs e)
        {
            new Thread(new ThreadStart(delegate
            {
                MessageBox.Show(
                    $"This information allows communication with WSJT-X.{Environment.NewLine}{Environment.NewLine}" +
                    $"- In the WSJT-X program, select File | Settings | Reporting.{Environment.NewLine}{Environment.NewLine}" +
                    $"- Enter the UDP server address (xxx.xxx.xxx.xxx) and port number shown there.{Environment.NewLine}{Environment.NewLine}" +
                    $"- Make sure 'Accept UDP requests' is enabled in WSJT-X.{Environment.NewLine}{Environment.NewLine}" +
                    $"Note: Select 'Multicast' here (entering the standard UDP address and port number) if other WSJT-X helper programs (loggers, maps, etc.) will be used.",
                    wsjtxClient.pgmName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            })).Start();
        }

        // ===== ADVANCED UI TAB =====

        private void BuildAdvancedUiTab()
        {
            advUiPanel.Controls.Clear();
            _advUiDependentControls = new List<System.Windows.Forms.Control>();

            var font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F);
            var boldFont = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold);
            int y = 8;
            const int left = 5;
            const int right = 330;
            const int groupW = 315;
            const int fullW = 650;

            // ── Group: Advanced call waiting layout ──────────────────────────────
            var layoutGroup = new System.Windows.Forms.GroupBox
            {
                Text = "Advanced call waiting layout",
                Location = new System.Drawing.Point(left, y),
                Size = new System.Drawing.Size(fullW, 110),
                Font = font,
                TabStop = false,
                AccessibleName = "Advanced call waiting layout options"
            };

            advCallLayoutCheckBox = new System.Windows.Forms.CheckBox
            {
                Text = "Enable advanced call waiting layout",
                AccessibleName = "Enable advanced call waiting layout",
                AutoSize = true,
                Location = new System.Drawing.Point(8, 20),
                TabIndex = 0,
                Font = boldFont,
                Checked = ctrl.advancedCallLayout
            };
            advCallLayoutCheckBox.CheckedChanged += (s, e) => UpdateAdvUiDependentEnabled();
            layoutGroup.Controls.Add(advCallLayoutCheckBox);

            advShowTx1CheckBox = MakeCheck(layoutGroup, "Show TX1 calls waiting", "Show TX1 calls waiting", 8, 44, 1, ctrl.advShowTx1, font);
            advShowTx2CheckBox = MakeCheck(layoutGroup, "Show TX2 calls waiting", "Show TX2 calls waiting", 210, 44, 2, ctrl.advShowTx2, font);
            advShowRawCheckBox = MakeCheck(layoutGroup, "Show raw decodes", "Show raw decodes", 8, 66, 3, ctrl.advShowRaw, font);
            var maxLabel = new System.Windows.Forms.Label
            {
                Text = "Maximum raw decode rows:",
                AutoSize = true,
                Location = new System.Drawing.Point(8, 91),
                Font = font,
                TabStop = false
            };
            layoutGroup.Controls.Add(maxLabel);

            rawMaxRowsNumeric = new System.Windows.Forms.NumericUpDown
            {
                AccessibleName = "Maximum raw decode rows",
                Location = new System.Drawing.Point(195, 88),
                Size = new System.Drawing.Size(70, 20),
                TabIndex = 4,
                Minimum = 10,
                Maximum = 5000,
                Value = Math.Max(10, Math.Min(5000, ctrl.rawMaxRows)),
                Font = font
            };
            layoutGroup.Controls.Add(rawMaxRowsNumeric);
            _advUiDependentControls.Add(maxLabel);
            _advUiDependentControls.Add(rawMaxRowsNumeric);

            advUiPanel.Controls.Add(layoutGroup);
            y += 118;

            // ── Group: Message types ──────────────────────────────────────────────
            var msgGroup = new System.Windows.Forms.GroupBox
            {
                Text = "Message types to show in raw decodes",
                Location = new System.Drawing.Point(left, y),
                Size = new System.Drawing.Size(groupW, 125),
                Font = font,
                TabStop = false,
                AccessibleName = "Message types group"
            };
            rawShowCqCheckBox       = MakeCheck(msgGroup, "CQ messages",     "CQ messages",       8,   22,  5, ctrl.rawShowCq,       font);
            rawShowDirectedCheckBox = MakeCheck(msgGroup, "Directed calls",   "Directed calls",    8,   44,  6, ctrl.rawShowDirected,  font);
            rawShowReportsCheckBox  = MakeCheck(msgGroup, "Signal reports",   "Signal reports",    8,   66,  7, ctrl.rawShowReports,   font);
            rawShowRR73CheckBox     = MakeCheck(msgGroup, "RR73 messages",    "RR73 messages",     8,   88,  8, ctrl.rawShowRR73,      font);
            rawShow73CheckBox       = MakeCheck(msgGroup, "73 messages",      "73 messages",       165, 22,  9, ctrl.rawShow73,        font);
            rawShowPotaCheckBox     = MakeCheck(msgGroup, "POTA messages",    "POTA messages",     165, 44, 10, ctrl.rawShowPota,      font);
            rawShowSotaCheckBox     = MakeCheck(msgGroup, "SOTA messages",    "SOTA messages",     165, 66, 11, ctrl.rawShowSota,      font);
            rawShowDxCheckBox       = MakeCheck(msgGroup, "DX messages",      "DX messages",       165, 88, 12, ctrl.rawShowDx,        font);
            advUiPanel.Controls.Add(msgGroup);

            // ── Group: Display fields ─────────────────────────────────────────────
            var displayGroup = new System.Windows.Forms.GroupBox
            {
                Text = "Display fields in raw decodes",
                Location = new System.Drawing.Point(right, y),
                Size = new System.Drawing.Size(groupW, 125),
                Font = font,
                TabStop = false,
                AccessibleName = "Display fields group"
            };
            rawShowSnrCheckBox     = MakeCheck(displayGroup, "SNR",               "Show SNR",                8,   22, 13, ctrl.rawShowSnr,     font);
            rawShowGridCheckBox    = MakeCheck(displayGroup, "Grid",              "Show Grid",               8,   44, 14, ctrl.rawShowGrid,     font);
            rawShowCountryCheckBox = MakeCheck(displayGroup, "Country",           "Show Country",            8,   66, 15, ctrl.rawShowCountry,  font);
            rawShowStateCheckBox   = MakeCheck(displayGroup, "State",             "Show State",              165, 22, 16, ctrl.rawShowState,    font);
            rawShowDistAzCheckBox  = MakeCheck(displayGroup, "Distance/Azimuth",  "Show Distance Azimuth",  165, 44, 17, ctrl.rawShowDistAz,   font);
            advUiPanel.Controls.Add(displayGroup);
            y += 132;

            // ── Group: Advanced filters ───────────────────────────────────────────
            var filtersGroup = new System.Windows.Forms.GroupBox
            {
                Text = "Advanced filters",
                Location = new System.Drawing.Point(left, y),
                Size = new System.Drawing.Size(fullW, 120),
                Font = font,
                TabStop = false,
                AccessibleName = "Advanced filters group"
            };
            rawOnlyCallsignsCheckBox = MakeCheck(filtersGroup, "Show only decodes containing callsigns",           "Only callsigns",         8, 20, 18, ctrl.rawOnlyCallsigns, font);
            rawOnlyUnworkedCheckBox  = MakeCheck(filtersGroup, "Show only stations not previously worked",          "Only unworked",           8, 44, 19, ctrl.rawOnlyUnworked,  font);
            rawOnlyRankedCheckBox    = MakeCheck(filtersGroup, "Show only stations matching current ranking filters","Only ranked",             8, 68, 20, ctrl.rawOnlyRanked,    font);
            rawPriorityTagsCheckBox  = MakeCheck(filtersGroup, "Show priority tags in Raw Decodes",                 "Show priority tags",      8, 92, 21, ctrl.rawPriorityTags,  font);
            advUiPanel.Controls.Add(filtersGroup);

            // All groups (except the enable checkbox itself) are dependent controls
            _advUiDependentControls.Add(advShowTx1CheckBox);
            _advUiDependentControls.Add(advShowTx2CheckBox);
            _advUiDependentControls.Add(advShowRawCheckBox);
            _advUiDependentControls.Add(msgGroup);
            _advUiDependentControls.Add(displayGroup);
            _advUiDependentControls.Add(filtersGroup);
            _advUiDependentControls.Add(rawPriorityTagsCheckBox);

            UpdateAdvUiDependentEnabled();
        }

        private System.Windows.Forms.CheckBox MakeCheck(
            System.Windows.Forms.Control parent, string text, string accessibleName,
            int x, int y, int tabIndex, bool chk, System.Drawing.Font font)
        {
            var cb = new System.Windows.Forms.CheckBox
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                Location = new System.Drawing.Point(x, y),
                TabIndex = tabIndex,
                Checked = chk,
                Font = font
            };
            parent.Controls.Add(cb);
            return cb;
        }

        private void UpdateAdvUiDependentEnabled()
        {
            bool en = advCallLayoutCheckBox?.Checked ?? false;
            if (_advUiDependentControls == null) return;
            foreach (var c in _advUiDependentControls)
                c.Enabled = en;
        }

        private void SaveAdvancedUiTab()
        {
            ctrl.advancedCallLayout = advCallLayoutCheckBox?.Checked ?? false;
            ctrl.advShowTx1 = advShowTx1CheckBox?.Checked ?? true;
            ctrl.advShowTx2 = advShowTx2CheckBox?.Checked ?? true;
            ctrl.advShowRaw = advShowRawCheckBox?.Checked ?? true;
            ctrl.rawShowCq        = rawShowCqCheckBox?.Checked ?? true;
            ctrl.rawShowDirected  = rawShowDirectedCheckBox?.Checked ?? true;
            ctrl.rawShowReports   = rawShowReportsCheckBox?.Checked ?? true;
            ctrl.rawShowRR73      = rawShowRR73CheckBox?.Checked ?? false;
            ctrl.rawShow73        = rawShow73CheckBox?.Checked ?? false;
            ctrl.rawShowPota      = rawShowPotaCheckBox?.Checked ?? true;
            ctrl.rawShowSota      = rawShowSotaCheckBox?.Checked ?? true;
            ctrl.rawShowDx        = rawShowDxCheckBox?.Checked ?? true;
            ctrl.rawShowSnr       = rawShowSnrCheckBox?.Checked ?? true;
            ctrl.rawShowGrid      = rawShowGridCheckBox?.Checked ?? true;
            ctrl.rawShowCountry   = rawShowCountryCheckBox?.Checked ?? true;
            ctrl.rawShowState     = rawShowStateCheckBox?.Checked ?? true;
            ctrl.rawShowDistAz    = rawShowDistAzCheckBox?.Checked ?? false;
            ctrl.rawOnlyCallsigns  = rawOnlyCallsignsCheckBox?.Checked ?? false;
            ctrl.rawOnlyUnworked   = rawOnlyUnworkedCheckBox?.Checked ?? false;
            ctrl.rawOnlyRanked     = rawOnlyRankedCheckBox?.Checked ?? false;
            ctrl.rawPriorityTags   = rawPriorityTagsCheckBox?.Checked ?? false;
            if (ctrl.wsjtxClient != null) ctrl.wsjtxClient.rawPriorityTags = ctrl.rawPriorityTags;
            int rawMax = (int)(rawMaxRowsNumeric?.Value ?? 100);
            ctrl.rawMaxRows = Math.Max(10, Math.Min(5000, rawMax));
        }

        // ===== WANTED CALLS TAB =====

        private void BuildWantedCallsTab()
        {
            wantedCallsPanel.Controls.Clear();

            var font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F);
            int y = 8;
            const int left = 8;
            const int w = 640;

            // Instruction label
            var instrBox = new System.Windows.Forms.TextBox
            {
                ReadOnly       = true,
                Multiline      = true,
                BorderStyle    = System.Windows.Forms.BorderStyle.None,
                BackColor      = wantedCallsPanel.BackColor,
                ForeColor      = System.Drawing.SystemColors.ControlText,
                Location       = new System.Drawing.Point(left, y),
                Size           = new System.Drawing.Size(w, 60),
                Text           = "Enter callsigns to always elevate in priority (one per line, or comma/space separated).\r\n" +
                                 "Examples: W1AW/0, VP8SGI, 3Y0K\r\n" +
                                 "Matching calls receive the \"Always Wanted Calls\" category. Case-insensitive. Duplicates are ignored.",
                TabStop        = false,
                AccessibleName = "Wanted Calls instructions",
                Font           = font,
            };
            wantedCallsPanel.Controls.Add(instrBox);
            y += 68;

            // Edit box label
            var editLabel = new System.Windows.Forms.Label
            {
                Text           = "Wanted callsigns:",
                AccessibleName = "Wanted callsigns label",
                AutoSize       = true,
                Location       = new System.Drawing.Point(left, y),
                Font           = font,
                TabStop        = false,
            };
            wantedCallsPanel.Controls.Add(editLabel);
            y += 20;

            // Multiline text box
            wantedCallsTextBox = new System.Windows.Forms.TextBox
            {
                Multiline      = true,
                ScrollBars     = System.Windows.Forms.ScrollBars.Vertical,
                Location       = new System.Drawing.Point(left, y),
                Size           = new System.Drawing.Size(w, 220),
                TabIndex       = 0,
                AccessibleName = "Wanted callsigns",
                AccessibleDescription = "Enter callsigns to always prioritize. One per line, or comma or space separated. Case-insensitive.",
                Font           = font,
            };
            // Populate from current wanted calls (sorted for readability)
            var sorted = new List<string>(wsjtxClient.wantedCalls);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            wantedCallsTextBox.Text = string.Join(Environment.NewLine, sorted);
            wantedCallsPanel.Controls.Add(wantedCallsTextBox);
        }

        private void SaveWantedCallsTab()
        {
            if (wantedCallsTextBox == null) return;
            var raw = wantedCallsTextBox.Text ?? string.Empty;
            // Parse: accept newlines, commas, and spaces as separators
            var tokens = raw.Split(new char[] { '\r', '\n', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tok in tokens)
            {
                string call = tok.Trim().ToUpperInvariant();
                if (!string.IsNullOrEmpty(call))
                    normalized.Add(call);
            }
            ctrl.ApplyAndSaveWantedCalls(normalized);
        }

        // ===== SOUNDS TAB =====

        private void BuildSoundsTab()
        {
            soundsPanel.Controls.Clear();
            _soundRows = new List<SoundRow>();

            var font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F);

            // Instruction label
            var instrBox = new System.Windows.Forms.TextBox
            {
                ReadOnly       = true,
                Multiline      = true,
                BorderStyle    = System.Windows.Forms.BorderStyle.None,
                BackColor      = soundsPanel.BackColor,
                ForeColor      = System.Drawing.SystemColors.ControlText,
                Location       = new System.Drawing.Point(8, 6),
                Size           = new System.Drawing.Size(648, 32),
                Text           = "Enable or disable each sound event and choose a WAV file. Leave the path empty to disable a sound.\r\n" +
                                 "Call added / Calling me / Logged enable state is controlled by the Advanced tab Sound checkboxes.",
                TabStop        = false,
                AccessibleName = "Sounds tab instructions",
                Font           = font,
            };
            soundsPanel.Controls.Add(instrBox);

            // Column headers
            var hdrEnabled = new System.Windows.Forms.Label { Text = "On",      AutoSize = true, Location = new System.Drawing.Point(8,   44), Font = font, TabStop = false };
            var hdrEvent   = new System.Windows.Forms.Label { Text = "Event",   AutoSize = true, Location = new System.Drawing.Point(32,  44), Font = font, TabStop = false };
            var hdrFile    = new System.Windows.Forms.Label { Text = "WAV file path (empty = no sound)", AutoSize = true, Location = new System.Drawing.Point(190, 44), Font = font, TabStop = false };
            soundsPanel.Controls.Add(hdrEnabled);
            soundsPanel.Controls.Add(hdrEvent);
            soundsPanel.Controls.Add(hdrFile);

            var eventDefs = new[]
            {
                new { Key = "CallAdded",      Label = "Call added",          Enabled = ctrl.callAddedCheckBox.Checked, File = ctrl.soundFile_CallAdded,   EnabledEditable = false },
                new { Key = "CallingMe",      Label = "Calling me",          Enabled = ctrl.mycallCheckBox.Checked,    File = ctrl.soundFile_CallingMe,   EnabledEditable = false },
                new { Key = "Logged",         Label = "Logged",              Enabled = ctrl.loggedCheckBox.Checked,    File = ctrl.soundFile_Logged,      EnabledEditable = false },
                new { Key = "TxEnabled",      Label = "TX enabled",          Enabled = ctrl.soundEnabled_TxEnabled,    File = ctrl.soundFile_TxEnabled,   EnabledEditable = true  },
                new { Key = "Disconnected",   Label = "WSJT-X disconnected", Enabled = ctrl.soundEnabled_Disconnected, File = ctrl.soundFile_Disconnected,EnabledEditable = true  },
                new { Key = "NewDxcc",        Label = "New DXCC",            Enabled = ctrl.soundEnabled_NewDxcc,      File = ctrl.soundFile_NewDxcc,     EnabledEditable = true  },
                new { Key = "NewDxccOnBand",  Label = "New DXCC on band",    Enabled = ctrl.soundEnabled_NewDxccOnBand,File = ctrl.soundFile_NewDxccOnBand,EnabledEditable = true },
                new { Key = "AlwaysWanted",   Label = "Always Wanted",       Enabled = ctrl.soundEnabled_AlwaysWanted, File = ctrl.soundFile_AlwaysWanted, EnabledEditable = true },
                new { Key = "DirectedCq",     Label = "Directed CQ",         Enabled = ctrl.soundEnabled_DirectedCq,   File = ctrl.soundFile_DirectedCq,   EnabledEditable = true },
                new { Key = "Pota",           Label = "POTA",                Enabled = ctrl.soundEnabled_Pota,         File = ctrl.soundFile_Pota,         EnabledEditable = true },
                new { Key = "Sota",           Label = "SOTA",                Enabled = ctrl.soundEnabled_Sota,         File = ctrl.soundFile_Sota,         EnabledEditable = true },
            };

            int y = 62;
            int tabIdx = 0;

            foreach (var ev in eventDefs)
            {
                var row = new SoundRow { Key = ev.Key };

                var enabledCb = new System.Windows.Forms.CheckBox
                {
                    Checked         = ev.Enabled,
                    Location        = new System.Drawing.Point(8, y),
                    Size            = new System.Drawing.Size(20, 17),
                    TabIndex        = tabIdx++,
                    TabStop         = ev.EnabledEditable,
                    Enabled         = ev.EnabledEditable,
                    AccessibleName  = ev.Label + " sound enabled",
                    Font            = font,
                };
                soundsPanel.Controls.Add(enabledCb);
                row.EnabledCb = enabledCb;

                var evLabel = new System.Windows.Forms.Label
                {
                    Text     = ev.Label,
                    Location = new System.Drawing.Point(32, y + 1),
                    Size     = new System.Drawing.Size(155, 17),
                    Font     = font,
                    TabStop  = false,
                };
                soundsPanel.Controls.Add(evLabel);

                var fileTb = new System.Windows.Forms.TextBox
                {
                    Text            = ev.File ?? "",
                    Location        = new System.Drawing.Point(190, y - 1),
                    Size            = new System.Drawing.Size(295, 20),
                    TabIndex        = tabIdx++,
                    AccessibleName  = ev.Label + " sound file path",
                    AccessibleDescription = "Full path to WAV file for " + ev.Label + " event. Leave empty for no sound.",
                    Font            = font,
                };
                soundsPanel.Controls.Add(fileTb);
                row.FileTb = fileTb;

                string capturedLabel = ev.Label;
                System.Windows.Forms.TextBox capturedTb = fileTb;

                var browseBtn = new System.Windows.Forms.Button
                {
                    Text            = "Browse",
                    Location        = new System.Drawing.Point(490, y - 1),
                    Size            = new System.Drawing.Size(60, 22),
                    TabIndex        = tabIdx++,
                    AccessibleName  = "Browse " + ev.Label + " sound file",
                    AccessibleDescription = "Open file browser to select WAV file for " + ev.Label,
                    Font            = font,
                };
                browseBtn.Click += (s, e) => BrowseSoundFile(capturedLabel, capturedTb);
                soundsPanel.Controls.Add(browseBtn);

                var testBtn = new System.Windows.Forms.Button
                {
                    Text            = "Test",
                    Location        = new System.Drawing.Point(555, y - 1),
                    Size            = new System.Drawing.Size(48, 22),
                    TabIndex        = tabIdx++,
                    AccessibleName  = "Test " + ev.Label + " sound",
                    AccessibleDescription = "Play the selected WAV file as a test for " + ev.Label,
                    Font            = font,
                };
                testBtn.Click += (s, e) => TestSoundFile(capturedTb.Text);
                soundsPanel.Controls.Add(testBtn);

                _soundRows.Add(row);
                y += 26;
            }
        }

        private void BrowseSoundFile(string eventLabel, System.Windows.Forms.TextBox fileTb)
        {
            using (var dlg = new System.Windows.Forms.OpenFileDialog())
            {
                dlg.Title = "Select sound file for: " + eventLabel;
                dlg.Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*";
                dlg.FilterIndex = 1;
                dlg.CheckFileExists = true;
                string current = fileTb.Text ?? "";
                if (!string.IsNullOrEmpty(current))
                {
                    try
                    {
                        if (System.IO.File.Exists(current))
                            dlg.InitialDirectory = System.IO.Path.GetDirectoryName(current);
                    }
                    catch { }
                }
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    fileTb.Text = dlg.FileName;
            }
        }

        private void TestSoundFile(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
                wsjtxClient.TestPlaySound(filePath);
        }

        private void SaveSoundsTab()
        {
            if (_soundRows == null) return;
            foreach (var row in _soundRows)
            {
                bool enabled = row.EnabledCb?.Checked ?? false;
                string file  = row.FileTb?.Text ?? "";
                switch (row.Key)
                {
                    case "CallAdded":
                        ctrl.callAddedCheckBox.Checked = enabled;
                        ctrl.soundFile_CallAdded = file;
                        break;
                    case "CallingMe":
                        ctrl.mycallCheckBox.Checked = enabled;
                        ctrl.soundFile_CallingMe = file;
                        break;
                    case "Logged":
                        ctrl.loggedCheckBox.Checked = enabled;
                        ctrl.soundFile_Logged = file;
                        break;
                    case "TxEnabled":
                        ctrl.soundEnabled_TxEnabled = enabled;
                        ctrl.soundFile_TxEnabled = file;
                        break;
                    case "Disconnected":
                        ctrl.soundEnabled_Disconnected = enabled;
                        ctrl.soundFile_Disconnected = file;
                        break;
                    case "NewDxcc":
                        ctrl.soundEnabled_NewDxcc = enabled;
                        ctrl.soundFile_NewDxcc = file;
                        break;
                    case "NewDxccOnBand":
                        ctrl.soundEnabled_NewDxccOnBand = enabled;
                        ctrl.soundFile_NewDxccOnBand = file;
                        break;
                    case "AlwaysWanted":
                        ctrl.soundEnabled_AlwaysWanted = enabled;
                        ctrl.soundFile_AlwaysWanted = file;
                        break;
                    case "DirectedCq":
                        ctrl.soundEnabled_DirectedCq = enabled;
                        ctrl.soundFile_DirectedCq = file;
                        break;
                    case "Pota":
                        ctrl.soundEnabled_Pota = enabled;
                        ctrl.soundFile_Pota = file;
                        break;
                    case "Sota":
                        ctrl.soundEnabled_Sota = enabled;
                        ctrl.soundFile_Sota = file;
                        break;
                }
            }
        }

        // ===== REPARENTING =====

        private void ReparentControlsToDialog()
        {
            // Calling group
            ReparentTo(ctrl.callNonDirCqCheckBox, callingGroupBox, new Point(10, 18));
            ReparentTo(ctrl.callCqDxCheckBox,     callingGroupBox, new Point(10, 40));
            ReparentTo(ctrl.callDirCqCheckBox,    callingGroupBox, new Point(10, 62));
            ReparentTo(ctrl.directedTextBox,      callingGroupBox, new Point(155, 60));
            ReparentTo(ctrl.UseDirectedHelpLabel, callingGroupBox, new Point(270, 63));
            ReparentTo(ctrl.ignoreNonDxCheckBox,  callingGroupBox, new Point(10, 86));
            ReparentTo(ctrl.IgnoreNonDxHelpLabel, callingGroupBox, new Point(270, 89));

            // Replying group
            ReparentTo(ctrl.replyDirCqCheckBox,      replyingGroupBox, new Point(10, 18));
            ReparentTo(ctrl.alertTextBox,            replyingGroupBox, new Point(155, 16));
            ReparentTo(ctrl.AlertDirectedHelpLabel,  replyingGroupBox, new Point(270, 19));
            ReparentTo(ctrl.replyNewDxccCheckBox,    replyingGroupBox, new Point(10, 42));
            ReparentTo(ctrl.replyNewOnlyCheckBox,    replyingGroupBox, new Point(200, 42));
            ReparentTo(ctrl.ReplyNewHelpLabel,       replyingGroupBox, new Point(280, 44));
            ReparentTo(ctrl.replyRR73CheckBox,       replyingGroupBox, new Point(10, 66));
            ReparentTo(ctrl.ReplyRR73HelpLabel,      replyingGroupBox, new Point(135, 68));
            ReparentTo(ctrl.exceptLabel,             replyingGroupBox, new Point(10, 92));
            ReparentTo(ctrl.exceptTextBox,           replyingGroupBox, new Point(115, 89));
            ReparentTo(ctrl.blockHelpLabel,          replyingGroupBox, new Point(280, 92));

            // Transmit group
            ReparentTo(ctrl.freqCheckBox,       transmitGroupBox, new Point(10, 18));
            ReparentTo(ctrl.AutoFreqHelpLabel,  transmitGroupBox, new Point(150, 20));
            ReparentTo(ctrl.skipGridCheckBox,   transmitGroupBox, new Point(10, 40));
            ReparentTo(ctrl.useRR73CheckBox,    transmitGroupBox, new Point(110, 40));
            ReparentTo(ctrl.logEarlyCheckBox,   transmitGroupBox, new Point(10, 62));
            ReparentTo(ctrl.LogEarlyHelpLabel,  transmitGroupBox, new Point(140, 64));
            ReparentTo(ctrl.optimizeCheckBox,   transmitGroupBox, new Point(10, 84));
            ReparentTo(ctrl.holdCheckBox,       transmitGroupBox, new Point(90, 84));
            ReparentTo(ctrl.limitLabel,         transmitGroupBox, new Point(10, 108));
            ReparentTo(ctrl.timeoutNumUpDown,   transmitGroupBox, new Point(57, 105));
            ctrl.timeoutNumUpDown.TabStop = true;
            ReparentTo(ctrl.repeatLabel,        transmitGroupBox, new Point(95, 108));
            ReparentTo(ctrl.LimitTxHelpLabel,   transmitGroupBox, new Point(240, 108));
            ReparentTo(ctrl.periodLabel,        transmitGroupBox, new Point(10, 132));
            ReparentTo(ctrl.periodComboBox,     transmitGroupBox, new Point(67, 129));
            ReparentTo(ctrl.PeriodHelpLabel,    transmitGroupBox, new Point(127, 132));

            // Sound group
            ReparentTo(ctrl.playSoundLabel,    soundGroupBox, new Point(10, 22));
            ReparentTo(ctrl.callAddedCheckBox, soundGroupBox, new Point(79, 20));
            ReparentTo(ctrl.mycallCheckBox,    soundGroupBox, new Point(161, 20));
            ReparentTo(ctrl.loggedCheckBox,    soundGroupBox, new Point(222, 20));
            ctrl.callAddedCheckBox.TabStop = true;
            ctrl.mycallCheckBox.TabStop    = true;
            ctrl.loggedCheckBox.TabStop    = true;

            // Display options (Basic tab)
            ReparentTo(ctrl.showUsStateCheckBox, basicTabPage, new Point(8, 295));

            // Filter group (Basic tab)
            ReparentTo(ctrl.replyNormCqLabel,   filterGroupBox, new Point(8, 22));
            ReparentTo(ctrl.bandComboBox,        filterGroupBox, new Point(112, 18));
            ReparentTo(ctrl.forLabel,            filterGroupBox, new Point(190, 22));
            ReparentTo(ctrl.ExcludeHelpLabel,    filterGroupBox, new Point(215, 22));
            ReparentTo(ctrl.includeLabel,        filterGroupBox, new Point(8, 45));
            ReparentTo(ctrl.cqOnlyRadioButton,   filterGroupBox, new Point(100, 43));
            ReparentTo(ctrl.cqGridRadioButton,   filterGroupBox, new Point(162, 43));
            ReparentTo(ctrl.anyMsgRadioButton,   filterGroupBox, new Point(232, 43));
            ReparentTo(ctrl.IncludeHelpLabel,    filterGroupBox, new Point(282, 45));
        }

        private void ReparentTo(Control c, Control newParent, Point newLocation)
        {
            originalParents[c] = c.Parent;
            originalLocations[c] = c.Location;
            c.Parent?.Controls.Remove(c);
            newParent.Controls.Add(c);
            c.Location = newLocation;
            c.Visible = true;
            reparentedControls.Add(c);
        }

        private void ReparentControlsBack()
        {
            foreach (Control c in reparentedControls)
            {
                c.Parent?.Controls.Remove(c);
                if (originalParents.TryGetValue(c, out Control origParent) && origParent != null)
                {
                    origParent.Controls.Add(c);
                    if (originalLocations.TryGetValue(c, out Point origLoc))
                        c.Location = origLoc;
                    c.Visible = false;
                }
            }
            reparentedControls.Clear();
            originalParents.Clear();
            originalLocations.Clear();
        }

        // ===== BASIC TAB WIZARD LOGIC (ported from Guide.cs) =====

        private void UpdateAllButtons()
        {
            foreach (CheckBox b in disableList)
                b.Enabled = !dxccButtonEnabled;

            SetState(listenButton,  wsjtxClient.txMode == WsjtxClient.TxModes.LISTEN && ctrl.periodComboBox.SelectedIndex == (int)WsjtxClient.ListenModeTxPeriods.ANY, true);
            SetState(callCqButton,  wsjtxClient.txMode == WsjtxClient.TxModes.CALL_CQ, true);

            SetState(cqButton,    (cqButtonEnabled    = ctrl.callNonDirCqCheckBox.Checked && !ctrl.callDirCqCheckBox.Checked), true);
            SetState(cqDxButton,  (cqDxButtonEnabled  = ctrl.callCqDxCheckBox.Checked    && !ctrl.callDirCqCheckBox.Checked), true);

            SetState(dxButton,    (dxButtonEnabled    = ctrl.replyDxCheckBox.Checked), true);
            SetState(nonDxButton, (nonDxButtonEnabled = ctrl.replyLocalCheckBox.Checked), true);

            SetState(potaButton, (activatorEnabled =
                wsjtxClient.txMode == WsjtxClient.TxModes.CALL_CQ &&
                ctrl.directedTextBox.Text == "POTA" &&
                ctrl.callDirCqCheckBox.Checked &&
                !ctrl.callCqDxCheckBox.Checked &&
                !ctrl.callNonDirCqCheckBox.Checked), true);

            SetState(hunterButton, (hunterEnabled =
                wsjtxClient.txMode == WsjtxClient.TxModes.LISTEN &&
                ctrl.alertTextBox.Text.Contains("POTA") &&
                ctrl.replyDirCqCheckBox.Checked), true);

            SetState(allButton,    (wsjtxClient.rankOrderList.Count > 0 && wsjtxClient.rankOrderList[0] == WsjtxClient.RankMethods.CALL_ORDER), true);
            SetState(recentButton, (wsjtxClient.rankOrderList.Count > 0 && wsjtxClient.rankOrderList[0] == WsjtxClient.RankMethods.MOST_RECENT), true);

            if (callCqButton.Checked)
                label9.Text = ", You're now ready to start. Press OK to close this Options dialog, then enable CQ mode using Ctrl, E.";
            else
                label9.Text = ", You're now ready to start. Press OK to close this Options dialog, and Listen mode is enabled.";
        }

        private void callCqButton_Click(object sender, EventArgs e)
        {
            ctrl.GuideCqMode();
            UpdateAllButtons();
        }

        private void listenButton_Click(object sender, EventArgs e)
        {
            ctrl.GuideListenMode();
            if (wsjtxClient.txMode == WsjtxClient.TxModes.LISTEN)
                ctrl.periodComboBox.SelectedIndex = (int)WsjtxClient.ListenModeTxPeriods.ANY;
            UpdateAllButtons();
        }

        private void cqButton_Click(object sender, EventArgs e)
        {
            UpdateAllButtons();
            if (cqButtonEnabled)
                ctrl.callNonDirCqCheckBox.Checked = false;
            else
            {
                ctrl.callNonDirCqCheckBox.Checked = true;
                ctrl.callDirCqCheckBox.Checked = false;
            }
            UpdateAllButtons();
        }

        private void cqDxButton_Click(object sender, EventArgs e)
        {
            UpdateAllButtons();
            if (cqDxButtonEnabled)
                ctrl.callCqDxCheckBox.Checked = false;
            else
            {
                ctrl.callCqDxCheckBox.Checked = true;
                ctrl.callDirCqCheckBox.Checked = false;
                ctrl.periodComboBox.SelectedIndex = (int)WsjtxClient.ListenModeTxPeriods.ANY;
            }
            UpdateAllButtons();
        }

        private void dxButton_Click(object sender, EventArgs e)
        {
            UpdateAllButtons();
            ctrl.ToggleDx();
            UpdateAllButtons();
        }

        private void nonDxButton_Click(object sender, EventArgs e)
        {
            UpdateAllButtons();
            ctrl.ToggleLocal();
            UpdateAllButtons();
        }

        private void potaButton_Click(object sender, EventArgs e)
        {
            UpdateAllButtons();
            if (!activatorEnabled && hunterEnabled) ctrl.ToggleHunter();
            ctrl.ToggleActivator();
            ctrl.cqModeButton_Click(null, null);
            UpdateAllButtons();
        }

        private void hunterButton_Click(object sender, EventArgs e)
        {
            UpdateAllButtons();
            if (!hunterEnabled && activatorEnabled) ctrl.ToggleActivator();
            ctrl.ToggleHunter();
            ctrl.listenModeButton_Click(null, null);
            ctrl.periodComboBox.SelectedIndex = (int)WsjtxClient.ListenModeTxPeriods.ANY;
            UpdateAllButtons();
        }

        private void allButton_Click(object sender, EventArgs e)
        {
            UpdateAllButtons();
            if (!(wsjtxClient.rankOrderList.Count > 0 && wsjtxClient.rankOrderList[0] == WsjtxClient.RankMethods.CALL_ORDER) || ctrl.timeoutNumUpDown.Value != 3)
                wsjtxClient.ApplySortOrder(new List<WsjtxClient.RankMethods> { WsjtxClient.RankMethods.CALL_ORDER }, null);
            UpdateAllButtons();
        }

        private void recentButton_Click(object sender, EventArgs e)
        {
            UpdateAllButtons();
            if (!(wsjtxClient.rankOrderList.Count > 0 && wsjtxClient.rankOrderList[0] == WsjtxClient.RankMethods.MOST_RECENT) || ctrl.timeoutNumUpDown.Value != 1)
                wsjtxClient.ApplySortOrder(new List<WsjtxClient.RankMethods> { WsjtxClient.RankMethods.MOST_RECENT }, null);
            UpdateAllButtons();
        }

        private void SetState(CheckBox button, bool selected, bool enabled)
        {
            if (selected) HighLight(button, enabled);
            else Normal(button, enabled);
        }

        private void HighLight(CheckBox button, bool enabled)
        {
            button.ForeColor = highlightFore;
            button.BackColor = enabled ? highlightBack : highlightBackDisabled;
            button.Checked = true;
        }

        private void Normal(CheckBox button, bool enabled)
        {
            button.ForeColor = normalFore;
            button.BackColor = normalBack;
            button.Checked = false;
        }

        // ===== TEXTBOX FOCUS HANDLERS (suppress cursor on read-only labels) =====

        private void subtitleLabel_Enter(object sender, EventArgs e)
        { subtitleLabel.SelectionStart = 0; subtitleLabel.SelectionLength = 0; }

        private void modeLabel_Enter(object sender, EventArgs e)
        { modeLabel.SelectionStart = 0; modeLabel.SelectionLength = 0; }

        private void label12_Enter(object sender, EventArgs e)
        { label12.SelectionStart = 0; label12.SelectionLength = 0; }

        private void label2_Enter(object sender, EventArgs e)
        { label2.SelectionStart = 0; label2.SelectionLength = 0; }

        private void label4_Enter(object sender, EventArgs e)
        { label4.SelectionStart = 0; label4.SelectionLength = 0; }

        private void label5_Enter(object sender, EventArgs e)
        { label5.SelectionStart = 0; label5.SelectionLength = 0; }

        private void label9_Enter(object sender, EventArgs e)
        { label9.SelectionStart = 0; label9.SelectionLength = 0; }

        // ===== HOTKEYS TAB =====

        private bool IsCaptureFieldFocused()
            => _sharedCaptureBox != null && _sharedCaptureBox.Focused;

        private void BuildHotkeysTab()
        {
            hotkeysPanel.Controls.Clear();
            _listActionMap = new List<HotkeyAction?>();
            _pendingKeys   = new Dictionary<HotkeyAction, Keys>();

            // Initialise pending keys from the live config
            foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
                _pendingKeys[action] = ctrl.hotkeyConfig[action];

            // Instruction text (no tab stop)
            var instrBox = new TextBox
            {
                ReadOnly       = true,
                Multiline      = true,
                BorderStyle    = BorderStyle.None,
                BackColor      = hotkeysPanel.BackColor,
                ForeColor      = SystemColors.ControlText,
                Location       = new Point(8, 8),
                Size           = new Size(640, 34),
                Text           = "Choose an action, then tab to the shortcut field and press the new shortcut.",
                TabStop        = false,
                AccessibleName = "Instruction",
            };
            hotkeysPanel.Controls.Add(instrBox);

            // Actions list box — Tab stop 0
            _actionListBox = new ListBox
            {
                Location       = new Point(8, 50),
                Size           = new Size(330, 262),
                TabIndex       = 0,
                AccessibleName = "Hotkey actions",
                Name           = "hkActionListBox",
            };
            BuildActionList();
            _actionListBox.SelectedIndexChanged += ActionListBox_SelectedIndexChanged;
            hotkeysPanel.Controls.Add(_actionListBox);

            // "Current shortcut:" static label (no tab stop)
            hotkeysPanel.Controls.Add(new Label
            {
                Text     = "Current shortcut:",
                Location = new Point(356, 50),
                Size     = new Size(140, 18),
                TabStop  = false,
            });

            // Shared capture box — Tab stop 1
            _sharedCaptureBox = new HotkeyCaptureBox
            {
                Location       = new Point(356, 70),
                Size           = new Size(240, 22),
                TabIndex       = 1,
                AccessibleName = "Shortcut key",
                Name           = "hkSharedCaptureBox",
            };
            _sharedCaptureBox.KeyCaptured += (s, ev) =>
                OnKeyCaptured(GetSelectedAction(), (HotkeyCaptureBox)s, ev.Keys);
            hotkeysPanel.Controls.Add(_sharedCaptureBox);

            // Reset All to Defaults button — Tab stop 2
            var resetBtn = new Button
            {
                Text     = "Reset All to Defaults",
                Location = new Point(356, 104),
                Size     = new Size(160, 27),
                TabIndex = 2,
                Name     = "hkResetButton",
            };
            resetBtn.Click += ResetHotkeys_Click;
            hotkeysPanel.Controls.Add(resetBtn);

            // Select the first real action (index 0 is the "General Commands" header)
            _actionListBox.SelectedIndex = 1;
        }

        private void BuildActionList()
        {
            var generalActions = new HotkeyAction[]
            {
                HotkeyAction.Options,
                HotkeyAction.Help,
                HotkeyAction.UpdateCheck,
                HotkeyAction.CallCqMode,
                HotkeyAction.ListenMode,
                HotkeyAction.EnableTx,
                HotkeyAction.HaltTx,
                HotkeyAction.NextCall,
                HotkeyAction.ManualCall,
                HotkeyAction.DeleteAllCalls,
                HotkeyAction.TxPeriod,
                HotkeyAction.HoldTimeout,
                HotkeyAction.TuneMode,
                HotkeyAction.AudioUp,
                HotkeyAction.AudioDown,
                HotkeyAction.PowerSwr,
                HotkeyAction.BandUp,
                HotkeyAction.BandDown,
                HotkeyAction.ToggleMode,
                HotkeyAction.PSKReporter,
                HotkeyAction.Prompts,
                HotkeyAction.UploadLotw,
                HotkeyAction.SortOrder,
                HotkeyAction.RowOrder,
            };

            var navActions = new HotkeyAction[]
            {
                HotkeyAction.NavStatus,
                HotkeyAction.NavCallList,
                HotkeyAction.NavPendingCount,
                HotkeyAction.NavLoggedList,
                HotkeyAction.NavLoggedCount,
                HotkeyAction.NavAdvTx1,
                HotkeyAction.NavAdvTx2,
                HotkeyAction.NavAdvRaw,
            };

            // Group header: General Commands
            _actionListBox.Items.Add("General Commands");
            _listActionMap.Add(null);

            foreach (var a in generalActions)
            {
                _actionListBox.Items.Add("  " + HotkeyConfig.DisplayNames[a]);
                _listActionMap.Add(a);
            }

            // Group header: Accessibility Navigation
            _actionListBox.Items.Add("Accessibility Navigation");
            _listActionMap.Add(null);

            foreach (var a in navActions)
            {
                _actionListBox.Items.Add("  " + HotkeyConfig.DisplayNames[a]);
                _listActionMap.Add(a);
            }
        }

        private void ActionListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            int idx = _actionListBox?.SelectedIndex ?? -1;
            if (idx < 0 || idx >= _listActionMap.Count) return;

            // Group header selected — skip forward to the next real action
            if (_listActionMap[idx] == null)
            {
                for (int next = idx + 1; next < _listActionMap.Count; next++)
                {
                    if (_listActionMap[next] != null)
                    {
                        _actionListBox.SelectedIndex = next;
                        return;
                    }
                }
                return;
            }

            HotkeyAction action  = _listActionMap[idx].Value;
            string       name    = HotkeyConfig.DisplayNames[action];

            if (_sharedCaptureBox != null)
            {
                _sharedCaptureBox.AccessibleName = name + " shortcut key";
                _sharedCaptureBox.SetValue(_pendingKeys[action]);
            }
        }

        private HotkeyAction? GetSelectedAction()
        {
            int idx = _actionListBox?.SelectedIndex ?? -1;
            if (idx < 0 || idx >= _listActionMap.Count) return null;
            return _listActionMap[idx];
        }

        private void OnKeyCaptured(HotkeyAction? action, HotkeyCaptureBox box, Keys keys)
        {
            if (action == null) return;

            string keyStr = HotkeyConfig.FormatKeys(keys);

            if (HotkeyConfig.IsReserved(keys))
            {
                MessageBox.Show(
                    $"{keyStr} is a reserved system shortcut and cannot be assigned.",
                    ctrl.friendlyName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!HotkeyConfig.IsValid(keys))
            {
                MessageBox.Show(
                    $"{keyStr} is not a valid shortcut.\r\n\r\n" +
                    "Use a combination with Alt or Ctrl, or a function key (F1-F24).\r\n" +
                    "Bare letters, numbers, and navigation keys are not allowed.",
                    ctrl.friendlyName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Check for conflicts among all pending assignments
            foreach (var kv in _pendingKeys)
            {
                if (kv.Key == action.Value) continue;
                if (kv.Value == keys)
                {
                    string conflictName = HotkeyConfig.DisplayNames[kv.Key];
                    MessageBox.Show(
                        $"{keyStr} is already assigned to {conflictName}.",
                        ctrl.friendlyName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            _pendingKeys[action.Value] = keys;
            box.SetValue(keys);
        }

        private bool ValidateHotkeys()
        {
            if (_pendingKeys == null) return true;

            var seen = new HashSet<Keys>();
            foreach (var kv in _pendingKeys)
            {
                Keys k = kv.Value;
                if (k == Keys.None)
                {
                    if (HotkeyConfig.OptionalActions.Contains(kv.Key)) continue;
                    string name = HotkeyConfig.DisplayNames[kv.Key];
                    MessageBox.Show(
                        $"Shortcut for '{name}' is not set.",
                        ctrl.friendlyName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    tabControl1.SelectedIndex = HotkeysTabIndex;
                    return false;
                }
                if (seen.Contains(k))
                {
                    MessageBox.Show(
                        "Duplicate shortcut detected. Please correct the Hotkeys settings.",
                        ctrl.friendlyName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    tabControl1.SelectedIndex = HotkeysTabIndex;
                    return false;
                }
                seen.Add(k);
            }
            return true;
        }

        private void SaveHotkeysTab()
        {
            if (_pendingKeys == null || ctrl.hotkeyConfig == null) return;
            foreach (var kv in _pendingKeys)
                ctrl.hotkeyConfig.Apply(kv.Key, kv.Value);
            ctrl.SaveHotkeyConfig();
        }

        private void ResetHotkeys_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                "Reset all shortcuts to their default values?",
                ctrl.friendlyName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            {
                if (HotkeyConfig.Defaults.TryGetValue(action, out Keys def))
                    _pendingKeys[action] = def;
            }

            // Refresh the capture box for the currently selected action
            ActionListBox_SelectedIndexChanged(null, null);
        }
    }
}
