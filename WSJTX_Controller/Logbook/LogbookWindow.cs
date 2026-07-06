using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WSJTX_Controller
{
    // Non-modal Ham Radio Center / Logbook window.
    // Open via Controller.OpenLogbookWindow() — singleton (one instance at a time).
    public class LogbookWindow : Form
    {
        // ── Dependencies ──────────────────────────────────────────────────────────
        // Credentials are read live (via these delegates), not snapshotted once at
        // construction, so a change made in Options while this window is already open
        // takes effect immediately instead of requiring a close/reopen -- matches the
        // same live-read pattern LiveQsoUploadOrchestrator already uses for the same reason.
        private readonly IniFile    _ini;
        private readonly Func<string> _qrzApiKey;
        private readonly Func<string> _lotwUser;
        private readonly Func<string> _lotwPass;
        private readonly Func<string> _clubLogEmail;
        private readonly Func<string> _clubLogPassword;
        private readonly Func<string> _clubLogCallsign;
        private readonly Action   _onImportComplete;
        private readonly HashSet<string> _activeAwardRuleIds;
        private readonly Action<string, bool> _onActiveAwardRuleIdsChanged;

        // ── Database ──────────────────────────────────────────────────────────────
        private LogbookDb _db;

        // ── Navigation state ──────────────────────────────────────────────────────
        private Panel _activePage;

        // ── Layout controls ───────────────────────────────────────────────────────
        private TabControl _tabControl;
        private TextBox    _statusTb;

        // ── Page panels ───────────────────────────────────────────────────────────
        private Panel _myLogPanel;
        private Panel _awardsPanel;
        private Panel _stillNeedPanel;
        private Panel _lookupPanel;
        private Panel _syncPanel;

        // ── My Log controls ───────────────────────────────────────────────────────
        private TextBox  _statTotalTb;
        private TextBox  _statLotwTb;
        private TextBox  _statQrzTb;
        private TextBox  _statConfTb;
        private TextBox  _statWasTb;
        private TextBox  _statDxccTb;
        private TextBox  _statWazTb;
        private ListView _dashRecentLv;

        // ── Awards controls ───────────────────────────────────────────────────────
        private ComboBox _awardsViewCb;
        private Label    _awardsProgressLbl;
        private ListView _awardsLv;
        private List<RuleDefinition> _awardsDefs = new List<RuleDefinition>();
        private bool     _suppressAwardsEvent;

        // ── Still Need controls ────────────────────────────────────────────────────
        private ComboBox _neededTypeCb;
        private CheckBox _neededActiveCb;
        private ComboBox _neededBandCb;
        private ListView _neededLv;
        private Label    _neededCountLbl;
        private List<RuleDefinition> _neededDefs = new List<RuleDefinition>();
        private bool     _suppressNeededEvent;

        // ── Lookup controls ───────────────────────────────────────────────────────
        private TextBox  _searchTb;
        private Button   _searchBtn;
        private Label    _searchCountLbl;
        private ListView _searchLv;
        private Button   _searchClearBtn;

        // ── Sync controls ─────────────────────────────────────────────────────────
        private Button   _syncImportBtn;
        private Button   _syncQrzBtn;
        private Button   _syncLotwBtn;
        private Button   _syncClubLogBtn;
        private Label    _srcQrzStatusLbl;
        private Label    _srcLotwStatusLbl;
        private Label    _srcClubLogStatusLbl;
        private ListView _srcHistoryLv;

        // ── Page constants ────────────────────────────────────────────────────────
        private const int PAGE_MYLOG     = 0;
        private const int PAGE_AWARDS    = 1;
        private const int PAGE_STILLNEED = 2;
        private const int PAGE_LOOKUP    = 3;
        private const int PAGE_SYNC      = 4;

        private static readonly string[] AllBands =
        {
            "(All Bands)", "160m","80m","60m","40m","30m","20m","17m","15m","12m","10m","6m","2m","70cm"
        };

        // ── Constructor ───────────────────────────────────────────────────────────

        public LogbookWindow(IniFile ini, Func<string> qrzApiKey, Func<string> lotwUser, Func<string> lotwPass,
            Func<string> clubLogEmail = null, Func<string> clubLogPassword = null, Func<string> clubLogCallsign = null,
            Action onImportComplete = null,
            HashSet<string> initialActiveAwardRuleIds = null,
            Action<string, bool> onActiveAwardRuleIdsChanged = null)
        {
            _ini              = ini;
            _qrzApiKey        = qrzApiKey        ?? (() => "");
            _lotwUser         = lotwUser         ?? (() => "");
            _lotwPass         = lotwPass         ?? (() => "");
            _clubLogEmail     = clubLogEmail     ?? (() => "");
            _clubLogPassword  = clubLogPassword  ?? (() => "");
            _clubLogCallsign  = clubLogCallsign  ?? (() => "");
            _onImportComplete = onImportComplete;
            _activeAwardRuleIds = initialActiveAwardRuleIds ?? new HashSet<string>();
            _onActiveAwardRuleIdsChanged = onActiveAwardRuleIdsChanged;

            Text            = "Ham Radio Center — Logbook";
            MinimumSize     = new Size(720, 500);
            Size            = new Size(950, 660);
            StartPosition   = FormStartPosition.CenterScreen;
            ShowInTaskbar   = true;
            KeyPreview      = true;
            FormBorderStyle = FormBorderStyle.Sizable;

            try
            {
                _db = new LogbookDb();
            }
            catch (Exception ex)
            {
                // Show error after window is visible
                this.Load += (s, e) =>
                    SetStatus("Database error: " + ex.Message);
            }

            BuildUi();

            this.KeyDown    += LogbookWindow_KeyDown;
            this.FormClosed += (s, e) => { _db?.Dispose(); _db = null; };
        }

        // ── UI construction ──────────────────────────────────────────────────────

        private void BuildUi()
        {
            var font  = new Font("Microsoft Sans Serif", 8.25F);
            var hfont = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);

            // Status bar at the bottom — read-only TextBox so JAWS can focus and read it on demand.
            var statusPanel = new Panel { Dock = DockStyle.Bottom, Height = 22, BackColor = SystemColors.Control };

            // Close button — a single shared control (not per-tab) so it lands last in tab
            // order on every tab, satisfying "Close appears consistently on all tabs" without
            // duplicating a button inside each TabPage.
            var closeBtn = new Button
            {
                Text           = "Close",
                Dock           = DockStyle.Right,
                Width          = 70,
                Font           = font,
                TabIndex       = 2,
                AccessibleName = "Close",
            };
            closeBtn.Click += (s, e) => this.Close();
            statusPanel.Controls.Add(closeBtn);

            _statusTb = new TextBox
            {
                Dock           = DockStyle.Fill,
                ReadOnly       = true,
                BorderStyle    = BorderStyle.None,
                BackColor      = SystemColors.Control,
                Text           = "Ready",
                Font           = font,
                TabStop        = true,
                TabIndex       = 0,
                AccessibleName = "Status",
            };
            statusPanel.Controls.Add(_statusTb);

            // TabControl — JAWS announces tab name on each tab; Left/Right arrows switch sections.
            _tabControl = new TabControl
            {
                Dock           = DockStyle.Fill,
                Font           = font,
                TabIndex       = 1,
                AccessibleName = "Logbook sections",
            };

            BuildMyLogPage(font, hfont);
            BuildAwardsPage(font, hfont);
            BuildStillNeedPage(font, hfont);
            BuildLookupPage(font, hfont);
            BuildSyncPage(font, hfont);

            string[] tabNames  = { "My Log", "Awards", "Still Need", "Lookup", "Sync" };
            Panel[]  tabPanels = { _myLogPanel, _awardsPanel, _stillNeedPanel, _lookupPanel, _syncPanel };
            for (int i = 0; i < tabNames.Length; i++)
            {
                tabPanels[i].Dock    = DockStyle.Fill;
                tabPanels[i].Visible = true;
                var tp = new TabPage(tabNames[i]) { UseVisualStyleBackColor = true };
                tp.Controls.Add(tabPanels[i]);
                _tabControl.TabPages.Add(tp);
            }

            _tabControl.SelectedIndexChanged += (s, e) => NavigateToPage(_tabControl.SelectedIndex);

            Controls.Add(_tabControl);
            Controls.Add(statusPanel);

            this.Load += (s, e) =>
            {
                NavigateToPage(PAGE_MYLOG);
                if (RuleLibrary.LoadErrors.Count > 0)
                    SetStatus($"{RuleLibrary.LoadErrors.Count} Rule Definition load error(s) — see log_rules_errors.txt.");
                _tabControl.Focus();
            };
        }

        // ── Page construction ─────────────────────────────────────────────────────

        private void BuildMyLogPage(Font font, Font hfont)
        {
            _myLogPanel = MakePage();
            int y = 8;

            // Individual focusable read-only TextBoxes — JAWS can Tab to each and read the value.
            AddStatField(_myLogPanel, "Total QSOs",         font, ref y, out _statTotalTb, "Total QSOs");
            AddStatField(_myLogPanel, "LoTW confirmed",     font, ref y, out _statLotwTb,  "LoTW confirmed QSOs");
            AddStatField(_myLogPanel, "QRZ confirmed",      font, ref y, out _statQrzTb,   "QRZ confirmed QSOs");
            AddStatField(_myLogPanel, "Combined confirmed", font, ref y, out _statConfTb,  "Combined confirmed QSOs");
            y += 4;
            AddStatField(_myLogPanel, "WAS",  font, ref y, out _statWasTb,  "WAS worked and confirmed");
            AddStatField(_myLogPanel, "DXCC", font, ref y, out _statDxccTb, "DXCC entities worked and confirmed");
            AddStatField(_myLogPanel, "WAZ",  font, ref y, out _statWazTb,  "WAZ zones worked and confirmed");
            y += 8;

            var recentLbl = new Label
            {
                Text     = "Recent QSOs",
                Font     = hfont,
                Location = new Point(8, y),
                AutoSize = true,
            };
            _myLogPanel.Controls.Add(recentLbl);
            y += 22;

            _dashRecentLv = MakeListView(font);
            _dashRecentLv.Location = new Point(8, y);
            _dashRecentLv.Size     = new Size(700, 200);
            _dashRecentLv.Anchor   = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _dashRecentLv.Columns.Add("Date",      80);
            _dashRecentLv.Columns.Add("UTC",       60);
            _dashRecentLv.Columns.Add("Callsign",  90);
            _dashRecentLv.Columns.Add("Band",      60);
            _dashRecentLv.Columns.Add("Mode",      60);
            _dashRecentLv.Columns.Add("Country",  130);
            _dashRecentLv.Columns.Add("Confirmed", 80);
            _dashRecentLv.AccessibleName = "Recent QSOs list";
            _myLogPanel.Controls.Add(_dashRecentLv);
        }

        private void BuildSyncPage(Font font, Font hfont)
        {
            _syncPanel = MakePage();
            int y = 8;

            _syncImportBtn = new Button
            {
                Text           = "Import ADIF...",
                AccessibleName = "Import ADIF file",
                Size           = new Size(120, 26),
                Location       = new Point(8, y),
                Font           = font,
                TabIndex       = 1,
            };
            _syncImportBtn.Click += ImportBtn_Click;

            _syncQrzBtn = new Button
            {
                Text           = "Download from QRZ",
                AccessibleName = "Download from QRZ Logbook",
                Size           = new Size(140, 26),
                Location       = new Point(134, y),
                Font           = font,
                TabIndex       = 2,
                Enabled        = !string.IsNullOrWhiteSpace(_qrzApiKey()),
            };
            _syncQrzBtn.Click += QrzRefreshBtn_Click;

            _syncLotwBtn = new Button
            {
                Text           = "Download from LoTW",
                AccessibleName = "Download from LoTW",
                Size           = new Size(142, 26),
                Location       = new Point(280, y),
                Font           = font,
                TabIndex       = 3,
                Enabled        = !string.IsNullOrWhiteSpace(_lotwUser()) && !string.IsNullOrWhiteSpace(_lotwPass()),
            };
            _syncLotwBtn.Click += LoTWRefreshBtn_Click;

            _syncClubLogBtn = new Button
            {
                Text           = "Download from Club Log",
                AccessibleName = "Download from Club Log",
                Size           = new Size(160, 26),
                Location       = new Point(428, y),
                Font           = font,
                TabIndex       = 4,
                Enabled        = !string.IsNullOrWhiteSpace(_clubLogEmail()) &&
                                  !string.IsNullOrWhiteSpace(_clubLogPassword()) &&
                                  !string.IsNullOrWhiteSpace(_clubLogCallsign()),
            };
            _syncClubLogBtn.Click += ClubLogRefreshBtn_Click;

            _syncPanel.Controls.AddRange(new Control[] { _syncImportBtn, _syncQrzBtn, _syncLotwBtn, _syncClubLogBtn });
            y += 34;

            AddSectionLabel(_syncPanel, "QRZ Logbook", hfont, ref y);
            _srcQrzStatusLbl = AddInfoLabel(_syncPanel, "Status: not configured", font, ref y);
            y += 4;

            AddSectionLabel(_syncPanel, "LoTW", hfont, ref y);
            _srcLotwStatusLbl = AddInfoLabel(_syncPanel, "Status: not configured", font, ref y);
            y += 4;

            AddSectionLabel(_syncPanel, "Club Log", hfont, ref y);
            _srcClubLogStatusLbl = AddInfoLabel(_syncPanel, "Status: not configured", font, ref y);
            y += 12;

            var histLbl = new Label
            {
                Text     = "Import History (most recent first)",
                Font     = hfont,
                Location = new Point(8, y),
                AutoSize = true,
            };
            _syncPanel.Controls.Add(histLbl);
            y += 22;

            _srcHistoryLv = MakeListView(font);
            _srcHistoryLv.Location = new Point(8, y);
            _srcHistoryLv.Size     = new Size(700, 200);
            _srcHistoryLv.Anchor   = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _srcHistoryLv.Columns.Add("Date/Time",  135);
            _srcHistoryLv.Columns.Add("Source",      65);
            _srcHistoryLv.Columns.Add("New",         55);
            _srcHistoryLv.Columns.Add("Updated",     65);
            _srcHistoryLv.Columns.Add("Total",       55);
            _srcHistoryLv.Columns.Add("Errors",     200);
            _srcHistoryLv.AccessibleName = "Import history list";
            _syncPanel.Controls.Add(_srcHistoryLv);
        }

        private void BuildAwardsPage(Font font, Font hfont)
        {
            _awardsPanel = MakePage();

            var viewLbl = new Label
            {
                Text     = "Award:",
                Font     = font,
                Location = new Point(8, 10),
                AutoSize = true,
            };
            _awardsPanel.Controls.Add(viewLbl);

            _awardsViewCb = new ComboBox
            {
                DropDownStyle  = ComboBoxStyle.DropDownList,
                Font           = font,
                Location       = new Point(56, 7),
                Size           = new Size(300, 21),
                TabIndex       = 1,
                AccessibleName = "Award selector",
            };
            // Items are populated from RuleLibrary.Definitions in PopulateAwardsCombo() —
            // dropping a new .ini file into RuleDefinitions adds it here with no code change.
            _awardsViewCb.SelectedIndexChanged += (s, e) => { if (!_suppressAwardsEvent) PopulateAwards(); };
            _awardsPanel.Controls.Add(_awardsViewCb);

            _awardsProgressLbl = new Label
            {
                Text           = "",
                Font           = font,
                Location       = new Point(366, 10),
                AutoSize       = true,
                AccessibleName = "Award progress summary",
            };
            _awardsPanel.Controls.Add(_awardsProgressLbl);

            _awardsLv = MakeListView(font);
            _awardsLv.Location = new Point(8, 66);
            _awardsLv.Anchor   = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _awardsLv.Size     = new Size(700, 350);
            _awardsLv.TabIndex = 2;
            _awardsLv.AccessibleName = "Award detail list";
            _awardsPanel.Controls.Add(_awardsLv);

            var manageBtn = new Button
            {
                Text           = "Manage Rule Definitions...",
                Font           = font,
                Location       = new Point(8, 34),
                Size           = new Size(180, 24),
                TabIndex       = 3,
                AccessibleName = "Manage Rule Definitions",
            };
            manageBtn.Click += (s, e) => OpenRuleDefinitionManager();
            _awardsPanel.Controls.Add(manageBtn);

            var refreshBtn = new Button
            {
                Text           = "Refresh",
                Font           = font,
                Location       = new Point(196, 34),
                Size           = new Size(90, 24),
                TabIndex       = 4,
                AccessibleName = "Refresh award progress",
                AccessibleDescription = "Re-checks award progress against QSOs logged since this page was last shown.",
            };
            refreshBtn.Click += (s, e) => PopulateAwards();
            _awardsPanel.Controls.Add(refreshBtn);
        }

        // Opens the Rule Definition Manager and, if anything changed, refreshes
        // every view that reads RuleLibrary.Definitions: this window's Awards
        // and Still Need combos, plus (via _onImportComplete) the Controller's
        // HRC cache and Still Need live-tagging cache.
        private void OpenRuleDefinitionManager()
        {
            using (var mgr = new RuleDefinitionManagerDlg())
            {
                mgr.ShowDialog(this);
                if (mgr.RulesChanged)
                {
                    PopulateAwardsCombo();
                    PopulateNeededCombo();
                    _onImportComplete?.Invoke();
                }
            }
        }

        private void BuildStillNeedPage(Font font, Font hfont)
        {
            _stillNeedPanel = MakePage();

            var typeLbl = new Label
            {
                Text     = "Award:",
                Font     = font,
                Location = new Point(8, 10),
                AutoSize = true,
            };
            _stillNeedPanel.Controls.Add(typeLbl);

            _neededTypeCb = new ComboBox
            {
                DropDownStyle  = ComboBoxStyle.DropDownList,
                Font           = font,
                Location       = new Point(56, 7),
                Size           = new Size(300, 21),
                TabIndex       = 1,
                AccessibleName = "Still Need award selector",
            };
            // Items are populated from RuleLibrary.Definitions in PopulateNeededCombo() --
            // dropping a new .ini file into RuleDefinitions adds it here with no code change.
            _neededTypeCb.SelectedIndexChanged += (s, e) => { if (!_suppressNeededEvent) PopulateNeeded(); };
            _stillNeedPanel.Controls.Add(_neededTypeCb);

            var bandLbl = new Label
            {
                Text     = "Band:",
                Font     = font,
                Location = new Point(366, 10),
                AutoSize = true,
            };
            _stillNeedPanel.Controls.Add(bandLbl);

            _neededBandCb = new ComboBox
            {
                DropDownStyle  = ComboBoxStyle.DropDownList,
                Font           = font,
                Location       = new Point(402, 7),
                Size           = new Size(90, 21),
                TabIndex       = 2,
                AccessibleName = "Band filter for Needed list",
            };
            _neededBandCb.Items.AddRange(AllBands);
            _neededBandCb.SelectedIndex = 0;
            _neededBandCb.SelectedIndexChanged += (s, e) => PopulateNeeded();
            _stillNeedPanel.Controls.Add(_neededBandCb);

            _neededCountLbl = new Label
            {
                Text     = "",
                Font     = font,
                Location = new Point(500, 10),
                AutoSize = true,
                AccessibleName = "Count of needed entries",
            };
            _stillNeedPanel.Controls.Add(_neededCountLbl);

            _neededActiveCb = new CheckBox
            {
                Text           = "Actively track this award (alerts on decode, any number can be checked)",
                Font           = font,
                Location       = new Point(8, 32),
                AutoSize       = true,
                TabIndex       = 3,
                AccessibleName = "Actively track this award for live alerts",
            };
            _neededActiveCb.CheckedChanged += (s, e) =>
            {
                if (_suppressNeededEvent) return;
                int idx = _neededTypeCb.SelectedIndex;
                if (idx < 0 || idx >= _neededDefs.Count) return;
                _onActiveAwardRuleIdsChanged?.Invoke(_neededDefs[idx].Id, _neededActiveCb.Checked);
            };
            _stillNeedPanel.Controls.Add(_neededActiveCb);

            var neededRefreshBtn = new Button
            {
                Text           = "Refresh",
                Font           = font,
                Location       = new Point(600, 6),
                Size           = new Size(100, 23),
                TabIndex       = 5,
                AccessibleName = "Refresh needed list",
                AccessibleDescription = "Re-checks the needed list against QSOs logged since this page was last shown.",
            };
            neededRefreshBtn.Click += (s, e) => PopulateNeeded();
            _stillNeedPanel.Controls.Add(neededRefreshBtn);

            _neededLv = MakeListView(font);
            _neededLv.Location = new Point(8, 58);
            _neededLv.Anchor   = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _neededLv.Size     = new Size(700, 358);
            _neededLv.TabIndex = 4;
            _neededLv.AccessibleName = "Needed items list";
            _stillNeedPanel.Controls.Add(_neededLv);
        }

        private void BuildLookupPage(Font font, Font hfont)
        {
            _lookupPanel = MakePage();

            var searchLbl = new Label
            {
                Text     = "Callsign:",
                Font     = font,
                Location = new Point(8, 11),
                AutoSize = true,
            };
            _lookupPanel.Controls.Add(searchLbl);

            _searchTb = new TextBox
            {
                Font           = font,
                Location       = new Point(68, 8),
                Size           = new Size(140, 20),
                TabIndex       = 1,
                AccessibleName = "Callsign search",
            };
            _searchTb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); } };
            _lookupPanel.Controls.Add(_searchTb);

            _searchBtn = new Button
            {
                Text           = "Search",
                AccessibleName = "Search for callsign",
                Font           = font,
                Location       = new Point(214, 7),
                Size           = new Size(70, 23),
                TabIndex       = 2,
            };
            _searchBtn.Click += (s, e) => DoSearch();
            _lookupPanel.Controls.Add(_searchBtn);

            _searchCountLbl = new Label
            {
                Text     = "",
                Font     = font,
                Location = new Point(292, 11),
                AutoSize = true,
                AccessibleName = "Search result count",
            };
            _lookupPanel.Controls.Add(_searchCountLbl);

            _searchLv = MakeListView(font);
            _searchLv.Location = new Point(8, 36);
            _searchLv.Anchor   = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _searchLv.Size     = new Size(700, 380);
            _searchLv.TabIndex = 3;
            _searchLv.Columns.Add("Date",      80);
            _searchLv.Columns.Add("UTC",       55);
            _searchLv.Columns.Add("Callsign",  90);
            _searchLv.Columns.Add("Band",      55);
            _searchLv.Columns.Add("Mode",      55);
            _searchLv.Columns.Add("State",     50);
            _searchLv.Columns.Add("Country",  120);
            _searchLv.Columns.Add("Confirmed", 80);
            _searchLv.Columns.Add("Source",    60);
            _searchLv.AccessibleName = "Search results list";
            _lookupPanel.Controls.Add(_searchLv);

            _searchClearBtn = new Button
            {
                Text           = "Clear",
                AccessibleName = "Clear search results",
                Font           = font,
                Location       = new Point(8, 420),
                Size           = new Size(70, 23),
                Anchor         = AnchorStyles.Bottom | AnchorStyles.Left,
                TabIndex       = 4,
            };
            _searchClearBtn.Click += (s, e) => ClearSearch();
            _lookupPanel.Controls.Add(_searchClearBtn);
        }

        private void ClearSearch()
        {
            _searchTb.Text = "";
            _searchLv.Items.Clear();
            _searchCountLbl.Text = "";
            _searchTb.Focus();
        }

        // ── Navigation ────────────────────────────────────────────────────────────

        private void NavigateToPage(int page)
        {
            Panel[] pages = { _myLogPanel, _awardsPanel, _stillNeedPanel, _lookupPanel, _syncPanel };
            if (page >= 0 && page < pages.Length)
                _activePage = pages[page];

            // Keep TabControl in sync when called programmatically
            if (_tabControl != null && _tabControl.SelectedIndex != page)
                _tabControl.SelectedIndex = page;

            switch (page)
            {
                case PAGE_MYLOG:     PopulateMyLog();  break;
                case PAGE_AWARDS:    PopulateAwards(); break;
                case PAGE_STILLNEED: PopulateNeeded(); break;
                case PAGE_LOOKUP:
                    // Do NOT auto-focus the callsign box here — that steals focus from
                    // normal tab navigation.  GoToLookup() (Ctrl+F) focuses it explicitly.
                    break;
                case PAGE_SYNC:      PopulateSync();   break;
            }
        }

        // ── Population methods ────────────────────────────────────────────────────

        private void PopulateMyLog()
        {
            if (_db == null)
            {
                _statTotalTb.Text = "Database not available.";
                return;
            }
            try
            {
                int total     = _db.TotalQsos();
                int confirmed = _db.ConfirmedQsos();
                int lotwConf  = _db.LotwConfirmedQsos();
                int qrzConf   = _db.QrzConfirmedQsos();
                var (wasW, wasC)   = _db.WasProgress();
                var (dxccW, dxccC) = _db.DxccProgress();
                var (wazW, wazC)   = _db.WazProgress();

                _statTotalTb.Text = total.ToString("N0");
                _statLotwTb.Text  = lotwConf.ToString("N0");
                _statQrzTb.Text   = qrzConf.ToString("N0");
                _statConfTb.Text  = confirmed.ToString("N0") +
                    (total > 0 ? $"  ({100.0 * confirmed / total:0.0}%)" : "");
                _statWasTb.Text   = $"{wasW} / 50 worked,  {wasC} / 50 confirmed";
                _statDxccTb.Text  = $"{dxccW} worked,  {dxccC} confirmed";
                _statWazTb.Text   = $"{wazW} / 40 worked,  {wazC} / 40 confirmed";

                var recent = _db.GetRecentQsos(10);
                _dashRecentLv.Items.Clear();
                foreach (var q in recent)
                {
                    var item = new ListViewItem(FormatDate(q.QsoDate));
                    item.SubItems.Add(FormatTime(q.TimeOn));
                    item.SubItems.Add(q.Callsign);
                    item.SubItems.Add(q.Band);
                    item.SubItems.Add(q.Mode);
                    item.SubItems.Add(q.Country);
                    item.SubItems.Add(ConfirmedText(q.LotwQslRcvd, q.QrzQslRcvd));
                    _dashRecentLv.Items.Add(item);
                }
            }
            catch (Exception ex) { _statTotalTb.Text = "Error: " + ex.Message; }
        }

        private void PopulateSync()
        {
            if (_db == null) return;
            try
            {
                // QRZ status
                if (string.IsNullOrWhiteSpace(_qrzApiKey()))
                    _srcQrzStatusLbl.Text = "QRZ Logbook API key not configured.  (Options > Logbook)";
                else
                {
                    string dt = ReadableDate(_ini?.Read("LogbookLastQrzRefresh"));
                    int cnt   = _db.TotalQsos("QRZ");
                    _srcQrzStatusLbl.Text = $"API key configured.  Last refresh: {dt}.  QSOs: {cnt:N0}.";
                }

                // LoTW status
                if (string.IsNullOrWhiteSpace(_lotwUser()))
                    _srcLotwStatusLbl.Text = "LoTW credentials not configured.  (Options > Logbook)";
                else
                {
                    string dt = ReadableDate(_ini?.Read("LogbookLastLoTWRefresh"));
                    int cnt   = _db.TotalQsos("LOTW");
                    _srcLotwStatusLbl.Text = $"Username: {_lotwUser()}.  Last refresh: {dt}.  QSOs: {cnt:N0}.";
                }

                // Club Log status
                if (string.IsNullOrWhiteSpace(_clubLogEmail()) || string.IsNullOrWhiteSpace(_clubLogPassword()) || string.IsNullOrWhiteSpace(_clubLogCallsign()))
                    _srcClubLogStatusLbl.Text = "Club Log upload credentials not configured.  (Options > Logbook)";
                else
                {
                    string dt = ReadableDate(_ini?.Read("LogbookLastClubLogRefresh"));
                    int cnt   = _db.TotalQsos("CLUBLOG");
                    _srcClubLogStatusLbl.Text = $"Callsign: {_clubLogCallsign()}.  Last refresh: {dt}.  QSOs: {cnt:N0}.";
                }

                // History
                var hist = _db.GetImportHistory(25);
                _srcHistoryLv.Items.Clear();
                foreach (var h in hist)
                {
                    var item = new ListViewItem(h.StartedAt == DateTime.MinValue ? "?" : h.StartedAt.ToLocalTime().ToString("g"));
                    item.SubItems.Add(h.Source);
                    item.SubItems.Add(h.NewQso.ToString("N0"));
                    item.SubItems.Add(h.UpdatedQso.ToString("N0"));
                    item.SubItems.Add(h.TotalQso.ToString("N0"));
                    item.SubItems.Add(h.ErrorText?.Length > 0 ? h.ErrorText.Split('\n')[0] : "");
                    _srcHistoryLv.Items.Add(item);
                }
            }
            catch (Exception ex) { SetStatus("Sync error: " + ex.Message); }
        }

        // Rebuilds the Award selector from RuleLibrary.Definitions (enabled only),
        // preserving the current selection by Id across rebuilds. Called on every
        // PopulateAwards() so a future "Reload Rules" action is reflected without
        // reopening the window.
        private void PopulateAwardsCombo()
        {
            var defs = RuleLibrary.Definitions.Where(d => d.Enabled)
                .OrderBy(d => d.Category ?? "").ThenBy(d => d.Name).ToList();

            string prevId = (_awardsViewCb.SelectedIndex >= 0 && _awardsViewCb.SelectedIndex < _awardsDefs.Count)
                ? _awardsDefs[_awardsViewCb.SelectedIndex].Id : null;

            _awardsDefs = defs;

            _suppressAwardsEvent = true;
            _awardsViewCb.Items.Clear();
            if (_awardsDefs.Count == 0)
            {
                _awardsViewCb.Items.Add("(No Rule Definitions available)");
                _awardsViewCb.Enabled = false;
                _awardsViewCb.SelectedIndex = 0;
            }
            else
            {
                _awardsViewCb.Enabled = true;
                foreach (var d in _awardsDefs) _awardsViewCb.Items.Add(d.Name);
                int idx = prevId != null ? _awardsDefs.FindIndex(d => d.Id == prevId) : -1;
                _awardsViewCb.SelectedIndex = idx >= 0 ? idx : 0;
            }
            _suppressAwardsEvent = false;
        }

        private void PopulateAwards()
        {
            if (_db == null || _awardsViewCb == null) return;

            PopulateAwardsCombo();
            _awardsLv.Items.Clear();
            _awardsLv.Columns.Clear();

            if (_awardsDefs.Count == 0)
            {
                _awardsProgressLbl.Text = RuleLibrary.LoadErrors.Count > 0
                    ? $"No enabled Rule Definitions ({RuleLibrary.LoadErrors.Count} load error(s) — see log_rules_errors.txt)."
                    : "No Rule Definitions found.";
                return;
            }

            int idx = _awardsViewCb.SelectedIndex;
            if (idx < 0 || idx >= _awardsDefs.Count) return;
            var def = _awardsDefs[idx];

            try
            {
                var result = RuleEngine.Evaluate(def);
                RenderAwardResult(def, result);
            }
            catch (Exception ex) { SetStatus("Awards error: " + ex.Message); }
        }

        // Renders one RuleResult generically, driven entirely by the definition's
        // Target/GroupBy/Confirmation -- no per-award-name branching, so a new
        // Rule Definition file just works without a UI code change.
        private void RenderAwardResult(RuleDefinition def, RuleResult result)
        {
            if (result.EvaluationError != null)
            {
                _awardsProgressLbl.Text = "Error: " + result.EvaluationError;
                return;
            }

            string basisLabel = def.Confirmation == RuleConfirmation.None ? "logged" : "confirmed";
            int basis = def.Confirmation == RuleConfirmation.None ? result.Worked : result.Confirmed;

            switch (def.Target)
            {
                case RuleTargetType.All:
                    string workedNote = basis != result.Worked ? $"  ({result.Worked} worked)" : "";
                    _awardsProgressLbl.Text = $"{basis} / {result.UniverseSize} {basisLabel}{workedNote}" +
                        (result.Completed ? "  — Complete!" : "");
                    break;

                case RuleTargetType.Count:
                    _awardsProgressLbl.Text = $"{basis} / {def.Threshold} {basisLabel}" +
                        (result.Completed ? "  — Complete!" : "");
                    break;

                case RuleTargetType.Levels:
                    string tierText = result.CurrentTier != null ? $"Current: {result.CurrentTier}" : "No level reached yet";
                    string next = NextLevelText(def, basis);
                    _awardsProgressLbl.Text = $"{basis} {basisLabel}  —  {tierText}" +
                        (next != null ? $"  (next: {next})" : "");
                    break;
            }

            BuildAwardColumns(def);
            BuildAwardRows(def, result);
        }

        private static string NextLevelText(RuleDefinition def, int basis)
        {
            foreach (var lvl in def.Levels)
                if (basis < lvl.Threshold) return $"{lvl.Name} at {lvl.Threshold}";
            return null;
        }

        private void BuildAwardColumns(RuleDefinition def)
        {
            bool showWorkedCol = def.Target == RuleTargetType.All && def.GroupBy != RuleGroupBy.None;
            bool showBandsCol  = def.GroupBy != RuleGroupBy.None;
            string itemHeader  = def.GroupBy == RuleGroupBy.None ? "Endorsement" : GroupByHeader(def.GroupBy);

            _awardsLv.Columns.Add(itemHeader, 150);
            if (def.GroupBy == RuleGroupBy.Dxcc)
                _awardsLv.Columns.Add("Country", 170);
            if (showBandsCol)
                _awardsLv.Columns.Add("Band(s) worked", 150);
            if (showWorkedCol)
                _awardsLv.Columns.Add("Worked", 70);
            _awardsLv.Columns.Add(def.Confirmation == RuleConfirmation.None ? "Logged" : "Confirmed", 90);
        }

        // Renders the per-item checklist (states/entities/zones/etc.) and, when the
        // definition has an [Endorsements] section, a divider row followed by
        // per-band/per-mode sub-results below it (see the divider comment below for
        // why this is a plain row rather than a native ListView group).
        private void BuildAwardRows(RuleDefinition def, RuleResult result)
        {
            bool showWorkedCol   = def.Target == RuleTargetType.All && def.GroupBy != RuleGroupBy.None;
            bool showBandsCol    = def.GroupBy != RuleGroupBy.None;
            bool hasEndorsements = result.Endorsements != null && result.Endorsements.Count > 0;

            if (def.GroupBy != RuleGroupBy.None)
            {
                var worked    = new HashSet<string>(result.WorkedItems    ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                var confirmed = new HashSet<string>(result.ConfirmedItems ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                Dictionary<int, string> dxccNames =
                    def.GroupBy == RuleGroupBy.Dxcc ? _db.GetDxccCountryNames() : null;

                var items = (def.Target == RuleTargetType.All && result.UniverseItems != null)
                    ? result.UniverseItems
                    : (result.WorkedItems ?? new List<string>());

                foreach (var value in items)
                {
                    var row = new ListViewItem(value);

                    if (dxccNames != null)
                    {
                        string name = null;
                        int dxccNum;
                        if (int.TryParse(value, out dxccNum)) dxccNames.TryGetValue(dxccNum, out name);
                        row.SubItems.Add(name ?? "");
                    }

                    bool isWorked    = worked.Contains(value);
                    bool isConfirmed = confirmed.Contains(value);
                    if (showBandsCol)
                    {
                        List<string> bands;
                        row.SubItems.Add(result.WorkedBands != null && result.WorkedBands.TryGetValue(value, out bands)
                            ? string.Join(", ", bands) : "—");
                    }
                    if (showWorkedCol)
                        row.SubItems.Add(isWorked ? "Yes" : "—");
                    row.SubItems.Add(isConfirmed ? "Confirmed" : (isWorked ? "Not confirmed" : "—"));

                    _awardsLv.Items.Add(row);
                }
            }

            if (hasEndorsements)
            {
                // Plain divider row instead of a native ListView group -- a real ListView
                // group (ShowGroups=true) exposes a distinct accessibility-tree node that
                // some JAWS versions mis-announce when focus crosses back into the first
                // group's first row (full window/tab/list structure re-announced instead
                // of just the row). A flat list with a text divider avoids that node
                // entirely while still visually separating the two sections.
                _awardsLv.Items.Add(new ListViewItem("— Endorsements —"));

                foreach (var end in result.Endorsements)
                {
                    var row = new ListViewItem($"{end.Kind}: {end.Value}");

                    if (def.GroupBy == RuleGroupBy.Dxcc) row.SubItems.Add("");
                    if (showBandsCol) row.SubItems.Add("");
                    if (showWorkedCol) row.SubItems.Add(end.Worked.ToString());

                    string status = def.Target == RuleTargetType.Levels
                        ? (end.Tier ?? "—")
                        : (end.Completed ? "Yes" : "No");
                    row.SubItems.Add(status);

                    _awardsLv.Items.Add(row);
                }
            }
        }

        private static string GroupByHeader(RuleGroupBy g)
        {
            switch (g)
            {
                case RuleGroupBy.Dxcc:      return "DXCC#";
                case RuleGroupBy.Country:   return "Country";
                case RuleGroupBy.State:     return "State/Province";
                case RuleGroupBy.CqZone:    return "CQ Zone";
                case RuleGroupBy.ItuZone:   return "ITU Zone";
                case RuleGroupBy.Continent: return "Continent";
                case RuleGroupBy.County:    return "County";
                case RuleGroupBy.Grid:      return "Grid";
                case RuleGroupBy.Grid4:     return "Grid Square";
                case RuleGroupBy.Iota:      return "IOTA Ref";
                case RuleGroupBy.Prefix:    return "Prefix";
                case RuleGroupBy.Callsign:  return "Callsign";
                case RuleGroupBy.SigInfo:   return "Reference";
                case RuleGroupBy.DarcDok:   return "DOK";
                default:                    return "Item";
            }
        }

        // Rebuilds the Still Need award selector from RuleLibrary.Definitions
        // (enabled only), preserving the current selection by Id across rebuilds --
        // mirrors PopulateAwardsCombo() so both tabs offer the same award list.
        private void PopulateNeededCombo()
        {
            var defs = RuleLibrary.Definitions.Where(d => d.Enabled)
                .OrderBy(d => d.Category ?? "").ThenBy(d => d.Name).ToList();

            string prevId = (_neededTypeCb.SelectedIndex >= 0 && _neededTypeCb.SelectedIndex < _neededDefs.Count)
                ? _neededDefs[_neededTypeCb.SelectedIndex].Id : _activeAwardRuleIds.FirstOrDefault();

            _neededDefs = defs;

            _suppressNeededEvent = true;
            _neededTypeCb.Items.Clear();
            if (_neededDefs.Count == 0)
            {
                _neededTypeCb.Items.Add("(No Rule Definitions available)");
                _neededTypeCb.Enabled = false;
                _neededTypeCb.SelectedIndex = 0;
            }
            else
            {
                _neededTypeCb.Enabled = true;
                foreach (var d in _neededDefs) _neededTypeCb.Items.Add(d.Name);
                int idx = prevId != null ? _neededDefs.FindIndex(d => d.Id == prevId) : -1;
                _neededTypeCb.SelectedIndex = idx >= 0 ? idx : 0;
            }
            _suppressNeededEvent = false;
        }

        private void PopulateNeeded()
        {
            if (_db == null || _neededTypeCb == null) return;

            PopulateNeededCombo();
            _neededLv.Items.Clear();
            _neededLv.Columns.Clear();

            if (_neededDefs.Count == 0)
            {
                _neededCountLbl.Text = RuleLibrary.LoadErrors.Count > 0
                    ? $"No enabled Rule Definitions ({RuleLibrary.LoadErrors.Count} load error(s) — see log_rules_errors.txt)."
                    : "No Rule Definitions found.";
                return;
            }

            int idx = _neededTypeCb.SelectedIndex;
            if (idx < 0 || idx >= _neededDefs.Count) return;
            var def = _neededDefs[idx];

            // Reflect whether the currently-browsed award is one of the actively-tracked
            // ones -- checking/unchecking here only affects this one award's membership in
            // Controller.activeAwardRuleIds (see _onActiveAwardRuleIdsChanged), independent
            // of which award happens to be selected for browsing/viewing below.
            bool supportsLiveTag = RuleEngine.SupportsLiveTag(def);
            _suppressNeededEvent = true;
            _neededActiveCb.Enabled = supportsLiveTag;
            _neededActiveCb.Checked = supportsLiveTag && _activeAwardRuleIds.Contains(def.Id);
            _neededActiveCb.AccessibleDescription = supportsLiveTag
                ? ""
                : "This award has no fixed checklist that can be tagged live during decoding.";
            _suppressNeededEvent = false;

            try
            {
                string band = _neededBandCb.SelectedIndex == 0 ? null : (string)_neededBandCb.SelectedItem;
                var result = RuleEngine.EvaluateBand(def, band);
                RenderNeededResult(def, result, band);
            }
            catch (Exception ex) { SetStatus("Needed error: " + ex.Message); }
        }

        // Renders one RuleResult's StillNeeded list generically, driven by the
        // definition's GroupBy -- no per-award-name branching. Definitions whose
        // Target isn't ALL (or whose universe can't be resolved) have no fixed
        // checklist, so RuleResult.StillNeeded is null; that's shown plainly
        // rather than treated as an error.
        private void RenderNeededResult(RuleDefinition def, RuleResult result, string band)
        {
            if (result.EvaluationError != null)
            {
                _neededCountLbl.Text = "Error: " + result.EvaluationError;
                return;
            }

            if (result.StillNeeded == null)
            {
                _neededCountLbl.Text = "This rule does not have a fixed still-needed checklist. " +
                    "(Live decode tagging is unavailable for this award.)";
                return;
            }

            string itemHeader = GroupByHeader(def.GroupBy);
            _neededLv.Columns.Add(itemHeader, 150);
            if (def.GroupBy == RuleGroupBy.Dxcc)
                _neededLv.Columns.Add("Country", 200);
            _neededLv.Columns.Add("Status", 120);

            Dictionary<int, string> dxccNames =
                def.GroupBy == RuleGroupBy.Dxcc ? _db.GetDxccCountryNames() : null;

            foreach (var value in result.StillNeeded)
            {
                var item = new ListViewItem(value);
                if (dxccNames != null)
                {
                    string name = null;
                    int dxccNum;
                    if (int.TryParse(value, out dxccNum)) dxccNames.TryGetValue(dxccNum, out name);
                    item.SubItems.Add(name ?? "");
                }
                item.SubItems.Add("Not yet worked");
                _neededLv.Items.Add(item);
            }

            string bandNote = band != null ? $" on {band}" : "";
            string liveTagNote = RuleEngine.SupportsLiveTag(def)
                ? "  Live decode tagging: on."
                : "  Live decode tagging: unavailable for this award.";
            _neededCountLbl.Text = $"{result.StillNeeded.Count} {itemHeader.ToLowerInvariant()} needed{bandNote}.{liveTagNote}";
        }

        private void DoSearch()
        {
            if (_db == null) return;
            string pat = (_searchTb.Text ?? "").Trim();
            if (pat.Length == 0) { _searchCountLbl.Text = "Enter a callsign."; return; }

            try
            {
                var results = _db.SearchByCallsign(pat);
                _searchLv.Items.Clear();
                foreach (var q in results)
                {
                    var item = new ListViewItem(FormatDate(q.QsoDate));
                    item.SubItems.Add(FormatTime(q.TimeOn));
                    item.SubItems.Add(q.Callsign);
                    item.SubItems.Add(q.Band);
                    item.SubItems.Add(q.Mode);
                    item.SubItems.Add(q.State);
                    item.SubItems.Add(q.Country);
                    item.SubItems.Add(ConfirmedText(q.LotwQslRcvd, q.QrzQslRcvd));
                    item.SubItems.Add(q.Source);
                    _searchLv.Items.Add(item);
                }
                _searchCountLbl.Text = results.Count == 0
                    ? "No QSOs found."
                    : $"{results.Count} QSO{(results.Count == 1 ? "" : "s")} found.";
            }
            catch (Exception ex) { SetStatus("Search error: " + ex.Message); }
        }

        // ── Import handlers ───────────────────────────────────────────────────────

        private async void ImportBtn_Click(object sender, EventArgs e)
        {
            if (_db == null) { SetStatus("Database not available."); return; }

            using (var dlg = new OpenFileDialog
            {
                Title       = "Import ADIF File",
                Filter      = "ADIF files (*.adi;*.adif)|*.adi;*.adif|All files (*.*)|*.*",
                Multiselect = false,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                await RunImport(dlg.FileName, "MANUAL");
            }
        }

        private async void QrzRefreshBtn_Click(object sender, EventArgs e)
        {
            if (_db == null) { SetStatus("Database not available."); return; }
            if (string.IsNullOrWhiteSpace(_qrzApiKey())) { SetStatus("QRZ API key not configured."); return; }

            SetStatus("Fetching QRZ Logbook…");
            SetBusy(true);
            try
            {
                var client = new QrzLogbookClient();
                DateTime? since = (_db.TotalQsos("QRZ") > 0)
                    ? ParseMetaDate(_ini?.Read("LogbookLastQrzRefresh"))
                    : null;
                string adif = await client.FetchAdifAsync(_qrzApiKey(), since).ConfigureAwait(true);
                if (adif == null)
                {
                    string msg = "QRZ error: " + (client.LastError ?? "Unknown error") + " (see debug log for details)";
                    LogSyncFailure("QRZ", msg);
                    SetStatus(msg);
                    return;
                }
                if (adif.Length == 0)
                {
                    string msg = since.HasValue
                        ? "QRZ: no new records since last refresh."
                        : "QRZ: 0 QSOs returned. Verify this is your QRZ Logbook API key (qrz.com → Logbook → Settings), not the XML callsign key.";
                    LogSyncFailure("QRZ", msg);
                    SetStatus(msg);
                    return;
                }
                await RunImportFromText(adif, "QRZ", "LogbookLastQrzRefresh");
            }
            catch (Exception ex)
            {
                LogSyncFailure("QRZ", "QRZ refresh error: " + ex.Message);
                SetStatus("QRZ refresh error: " + ex.Message);
            }
            finally { SetBusy(false); }
        }

        private async void LoTWRefreshBtn_Click(object sender, EventArgs e)
        {
            if (_db == null) { SetStatus("Database not available."); return; }
            if (string.IsNullOrWhiteSpace(_lotwUser())) { SetStatus("LoTW credentials not configured."); return; }

            SetBusy(true);
            try
            {
                var client = new LoTWQsoClient();
                DateTime? since = (_db.TotalQsos("LOTW") > 0)
                    ? ParseMetaDate(_ini?.Read("LogbookLastLoTWRefresh"))
                    : null;

                // LoTW splits confirmed and unconfirmed QSOs into separate API responses.
                // Fetch both and concatenate; AdifParser handles multiple <EOH> tags.
                SetStatus("Fetching LoTW confirmed QSOs…");
                string adif1 = await client.FetchReportAsync(_lotwUser(), _lotwPass(), since, confirmedOnly: true).ConfigureAwait(true);
                if (adif1 == null)
                {
                    string msg = "LoTW error: " + (client.LastError ?? "Unknown error");
                    LogSyncFailure("LOTW", msg);
                    SetStatus(msg);
                    return;
                }

                SetStatus("Fetching LoTW unconfirmed QSOs…");
                string adif2 = await client.FetchReportAsync(_lotwUser(), _lotwPass(), since, confirmedOnly: false).ConfigureAwait(true);
                if (adif2 == null) adif2 = "";

                await RunImportFromText(adif1 + "\r\n" + adif2, "LOTW", "LogbookLastLoTWRefresh").ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LogSyncFailure("LOTW", "LoTW refresh error: " + ex.Message);
                SetStatus("LoTW refresh error: " + ex.Message);
            }
            finally { SetBusy(false); }
        }

        private async void ClubLogRefreshBtn_Click(object sender, EventArgs e)
        {
            if (_db == null) { SetStatus("Database not available."); return; }
            if (string.IsNullOrWhiteSpace(_clubLogEmail()) || string.IsNullOrWhiteSpace(_clubLogPassword()) || string.IsNullOrWhiteSpace(_clubLogCallsign()))
            {
                SetStatus("Club Log upload credentials not configured.");
                return;
            }

            SetStatus("Fetching Club Log…");
            SetBusy(true);
            try
            {
                var client = new ClubLogUploadClient();
                // getadif.php only supports whole-year filtering (no day-level "since"
                // like QRZ/LoTW), so only the year of the last refresh is passed.
                DateTime? since = (_db.TotalQsos("CLUBLOG") > 0)
                    ? ParseMetaDate(_ini?.Read("LogbookLastClubLogRefresh"))
                    : null;
                string adif = await client.FetchAdifAsync(_clubLogEmail(), _clubLogPassword(), _clubLogCallsign(), since?.Year).ConfigureAwait(true);
                if (adif == null)
                {
                    string msg = "Club Log error: " + (client.LastError ?? "Unknown error");
                    LogSyncFailure("CLUBLOG", msg);
                    SetStatus(msg);
                    return;
                }
                if (adif.Trim().Length == 0)
                {
                    SetStatus("Club Log: no records returned.");
                    return;
                }
                await RunImportFromText(adif, "CLUBLOG", "LogbookLastClubLogRefresh");
            }
            catch (Exception ex)
            {
                LogSyncFailure("CLUBLOG", "Club Log refresh error: " + ex.Message);
                SetStatus("Club Log refresh error: " + ex.Message);
            }
            finally { SetBusy(false); }
        }

        // Records a history row for a sync attempt that failed before any ADIF data
        // reached RunImportFromText (which logs its own row on success/parse-level errors).
        private void LogSyncFailure(string source, string message)
        {
            if (_db == null) return;
            int logId = _db.LogImportStart(source);
            _db.LogImportFinish(logId, 0, 0, 0, 0, message);
        }

        private async Task RunImport(string filePath, string source)
        {
            SetBusy(true);
            SetStatus($"Reading {Path.GetFileName(filePath)}…");
            try
            {
                string text = await Task.Run(() => File.ReadAllText(filePath)).ConfigureAwait(true);
                await RunImportFromText(text, source, null);
            }
            catch (Exception ex)
            {
                SetStatus("Import error: " + ex.Message);
            }
            finally { SetBusy(false); }
        }

        private async Task RunImportFromText(string adifText, string source, string metaKey)
        {
            SetBusy(true);
            int logId = _db.LogImportStart(source);
            ImportResult result = null;

            try
            {
                result = await Task.Run(() =>
                {
                    return AdifImporter.Import(_db, AdifParser.Parse(adifText), source,
                        count => BeginInvoke(new Action(() =>
                            SetStatus($"Importing {source}: {count:N0} processed…"))));
                }).ConfigureAwait(true);

                _db.LogImportFinish(logId, result.Processed, result.NewQsos, result.Updated, result.Skipped, result.Errors);

                if (metaKey != null)
                    _ini?.Write(metaKey, DateTime.UtcNow.ToString("o"));

                SetStatus($"{source} import complete: {result.NewQsos:N0} new, {result.Updated:N0} updated, {result.Skipped:N0} skipped.");

                if (!string.IsNullOrWhiteSpace(result.Errors))
                {
                    string summary = result.Errors.Split(new[]{'\n'}, StringSplitOptions.RemoveEmptyEntries).Length + " errors encountered.";
                    SetStatus(SetStatus_Text + "  " + summary);
                }

                // Refresh the active page to show new data; do not move focus.
                if (_activePage == _myLogPanel)   PopulateMyLog();
                else if (_activePage == _syncPanel) PopulateSync();

                // Notify Jimmy so it can refresh its HRC filter caches.
                _onImportComplete?.Invoke();
            }
            catch (Exception ex)
            {
                _db.LogImportFinish(logId, 0, 0, 0, 0, ex.Message);
                SetStatus($"{source} import error: " + ex.Message);
            }
            finally { SetBusy(false); }
        }

        // ── Keyboard shortcuts ────────────────────────────────────────────────────

        private void LogbookWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                RefreshCurrentPage();
                return;
            }
            if (e.Control && e.KeyCode == Keys.F)
            {
                e.Handled = true;
                GoToLookup();
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                _tabControl?.Focus();
                return;
            }
        }

        // Called both by the F5 shortcut below and externally by Controller when a
        // QSO is logged live, so an open Awards/Still Need page reflects it immediately.
        public void RefreshCurrentPage()
        {
            if      (_activePage == _myLogPanel)     PopulateMyLog();
            else if (_activePage == _awardsPanel)    PopulateAwards();
            else if (_activePage == _stillNeedPanel) PopulateNeeded();
            else if (_activePage == _lookupPanel)    DoSearch();
            else if (_activePage == _syncPanel)      PopulateSync();
        }

        private void GoToLookup()
        {
            _tabControl.SelectedIndex = PAGE_LOOKUP;
            _searchTb?.Focus();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Panel MakePage()
        {
            return new Panel
            {
                Dock       = DockStyle.Fill,
                AutoScroll = true,
                TabIndex   = 5,
            };
        }

        private static ListView MakeListView(Font font)
        {
            return new ListView
            {
                View          = View.Details,
                FullRowSelect = true,
                MultiSelect   = false,
                Font          = font,
                GridLines     = true,
                HideSelection = false,
                TabIndex      = 10,
            };
        }

        private void AddSectionLabel(Panel p, string text, Font f, ref int y)
        {
            var lbl = new Label { Text = text, Font = f, Location = new Point(8, y), AutoSize = true };
            p.Controls.Add(lbl);
            y += 22;
        }

        private Label AddInfoLabel(Panel p, string text, Font f, ref int y)
        {
            var lbl = new Label
            {
                Text     = text,
                Font     = f,
                Location = new Point(12, y),
                AutoSize = false,
                Size     = new Size(680, 18),
            };
            p.Controls.Add(lbl);
            y += 22;
            return lbl;
        }

        // Creates a label + read-only TextBox pair for a single stat on the My Log page.
        private void AddStatField(Panel p, string label, Font f, ref int y,
            out TextBox tb, string accessibleName)
        {
            var lbl = new Label
            {
                Text     = label + ":",
                Font     = f,
                Location = new Point(8, y + 2),
                Size     = new Size(130, 16),
                AutoSize = false,
            };
            p.Controls.Add(lbl);

            tb = new TextBox
            {
                ReadOnly       = true,
                BorderStyle    = BorderStyle.None,
                BackColor      = SystemColors.Control,
                Font           = f,
                Location       = new Point(142, y),
                Size           = new Size(400, 18),
                TabStop        = true,
                AccessibleName = accessibleName,
                Text           = "…",
            };
            p.Controls.Add(tb);
            y += 22;
        }

        private void SetBusy(bool busy)
        {
            _syncImportBtn.Enabled  = !busy;
            _syncQrzBtn.Enabled     = !busy && !string.IsNullOrWhiteSpace(_qrzApiKey());
            _syncLotwBtn.Enabled    = !busy && !string.IsNullOrWhiteSpace(_lotwUser()) && !string.IsNullOrWhiteSpace(_lotwPass());
            _syncClubLogBtn.Enabled = !busy && !string.IsNullOrWhiteSpace(_clubLogEmail()) &&
                                       !string.IsNullOrWhiteSpace(_clubLogPassword()) && !string.IsNullOrWhiteSpace(_clubLogCallsign());
        }

        private string SetStatus_Text;
        private void SetStatus(string msg)
        {
            SetStatus_Text = msg ?? "";
            if (_statusTb != null) _statusTb.Text = SetStatus_Text;
        }

        private static string FormatDate(string d)
        {
            if (d == null || d.Length < 8) return d ?? "";
            return $"{d.Substring(0,4)}-{d.Substring(4,2)}-{d.Substring(6,2)}";
        }

        private static string FormatTime(string t)
        {
            if (t == null || t.Length < 4) return t ?? "";
            return $"{t.Substring(0,2)}:{t.Substring(2,2)}";
        }

        private static string ConfirmedText(string lotw, string qrz)
        {
            if (lotw == "Y" && qrz == "Y") return "LoTW + QRZ";
            if (lotw == "Y")  return "LoTW";
            if (qrz  == "Y")  return "QRZ";
            return "—";
        }

        private static string ReadableDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "never";
            DateTime dt;
            return DateTime.TryParse(iso, out dt) ? dt.ToLocalTime().ToString("g") : "never";
        }

        private static DateTime? ParseMetaDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            DateTime dt;
            return DateTime.TryParse(iso, out dt) ? (DateTime?)dt : null;
        }

        private static int SrcCount(Dictionary<string, int> d, string k)
        {
            int v; return d.TryGetValue(k, out v) ? v : 0;
        }
    }
}
