using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WSJTX_Controller
{
    public class RankOrderDlg : Form
    {
        // ── Tab infrastructure ─────────────────────────────────────────────────
        private TabControl tabControl;
        private TabPage    listPrioritiesTabPage;
        private TabPage    callingPrioritiesTabPage;
        private TabPage    sortTabPage;
        private TabPage    helpTabPage;

        // ── Tab 1 – List Priorities ────────────────────────────────────────────
        private Label            listPriorityInfoLabel;
        private ListBox          listPriorityListBox;
        private Button           listMoveUpButton;
        private Button           listMoveDownButton;
        private Button           restoreListDefaultsButton;

        // ── Tab 2 – Calling Priorities ─────────────────────────────────────────
        private Label            callingInfoLabel;
        private CheckedListBox   callingCheckedListBox;
        private Button           restoreCallingDefaultsButton;

        // ── Tab 3 – Normal Sort Order ──────────────────────────────────────────
        private Label            sortInfoLabel;
        private CheckedListBox   sortCheckedListBox;
        private Button           sortMoveUpButton;
        private Button           sortMoveDownButton;
        private Label            beamStaticLabel;
        private ComboBox         beamComboBox;
        private Button           restoreSortDefaultsButton;

        // ── Tab 4 – Help / Notes ───────────────────────────────────────────────
        private TextBox          helpTextBox;

        // ── Form-level buttons (always last in tab order) ──────────────────────
        private Button           okButton;
        private Button           cancelButton;

        // ── Public results ─────────────────────────────────────────────────────
        public List<WsjtxClient.RankMethods>              SelectedOrder              { get; private set; }
        public WsjtxClient.RankMethods?                   SelectedBeam               { get; private set; }
        public Dictionary<WsjtxClient.CallCategory, int>  SelectedCategoryWeights    { get; private set; }
        public HashSet<WsjtxClient.CallCategory>          SelectedCallingPriorities  { get; private set; }

        // ── Static data ────────────────────────────────────────────────────────
        private static readonly SortEntry[] DefaultSortEntries = new SortEntry[]
        {
            new SortEntry(WsjtxClient.RankMethods.CALL_ORDER,  "Order received, oldest first"),
            new SortEntry(WsjtxClient.RankMethods.MOST_RECENT, "Most recent first"),
            new SortEntry(WsjtxClient.RankMethods.DIST_INCR,   "Nearest callers first"),
            new SortEntry(WsjtxClient.RankMethods.DIST_DECR,   "Farthest callers first"),
            new SortEntry(WsjtxClient.RankMethods.SNR_INCR,    "Weakest signal first"),
            new SortEntry(WsjtxClient.RankMethods.SNR_DECR,    "Strongest signal first"),
        };

        private static readonly BeamEntry[] BeamEntries = new BeamEntry[]
        {
            new BeamEntry(null,                               "None"),
            new BeamEntry(WsjtxClient.RankMethods.AZ_NQUAD,  "N"),
            new BeamEntry(WsjtxClient.RankMethods.AZ_NEQUAD, "NE"),
            new BeamEntry(WsjtxClient.RankMethods.AZ_EQUAD,  "E"),
            new BeamEntry(WsjtxClient.RankMethods.AZ_SEQUAD, "SE"),
            new BeamEntry(WsjtxClient.RankMethods.AZ_SQUAD,  "S"),
            new BeamEntry(WsjtxClient.RankMethods.AZ_SWQUAD, "SW"),
            new BeamEntry(WsjtxClient.RankMethods.AZ_WQUAD,  "W"),
            new BeamEntry(WsjtxClient.RankMethods.AZ_NWQUAD, "NW"),
        };

        private static readonly Dictionary<WsjtxClient.CallCategory, string> CategoryLabels =
            new Dictionary<WsjtxClient.CallCategory, string>
        {
            { WsjtxClient.CallCategory.NEW_COUNTRY,         "New DXCC" },
            { WsjtxClient.CallCategory.NEW_COUNTRY_ON_BAND, "New DXCC on band" },
            { WsjtxClient.CallCategory.ALWAYS_WANTED,       "Always Wanted Calls" },
            { WsjtxClient.CallCategory.TO_MYCALL,           "Calling me" },
            { WsjtxClient.CallCategory.MANUAL_SEL,          "Manual selection" },
            { WsjtxClient.CallCategory.WANTED_CQ,           "Directed CQ" },
            { WsjtxClient.CallCategory.POTA,                "POTA" },
            { WsjtxClient.CallCategory.SOTA,                "SOTA" },
            { WsjtxClient.CallCategory.DEFAULT,             "Default" },
        };

        private static readonly Dictionary<WsjtxClient.CallCategory, int> DefaultCategoryWeights =
            new Dictionary<WsjtxClient.CallCategory, int>
        {
            { WsjtxClient.CallCategory.NEW_COUNTRY,         8 },
            { WsjtxClient.CallCategory.NEW_COUNTRY_ON_BAND, 7 },
            { WsjtxClient.CallCategory.ALWAYS_WANTED,       6 },
            { WsjtxClient.CallCategory.TO_MYCALL,           5 },
            { WsjtxClient.CallCategory.MANUAL_SEL,          4 },
            { WsjtxClient.CallCategory.WANTED_CQ,           3 },
            { WsjtxClient.CallCategory.POTA,                2 },
            { WsjtxClient.CallCategory.SOTA,                1 },
            { WsjtxClient.CallCategory.DEFAULT,             0 },
        };

        private static readonly HashSet<WsjtxClient.CallCategory> DefaultCallingPriorities =
            new HashSet<WsjtxClient.CallCategory>
        {
            WsjtxClient.CallCategory.NEW_COUNTRY,
            WsjtxClient.CallCategory.NEW_COUNTRY_ON_BAND,
            WsjtxClient.CallCategory.ALWAYS_WANTED,
            WsjtxClient.CallCategory.TO_MYCALL,
            WsjtxClient.CallCategory.MANUAL_SEL,
            WsjtxClient.CallCategory.WANTED_CQ,
            WsjtxClient.CallCategory.POTA,
            WsjtxClient.CallCategory.SOTA,
        };

        // ── Constructor ────────────────────────────────────────────────────────
        public RankOrderDlg(
            List<WsjtxClient.RankMethods> currentOrder,
            WsjtxClient.RankMethods? currentBeam,
            Dictionary<WsjtxClient.CallCategory, int> currentCategoryWeights,
            HashSet<WsjtxClient.CallCategory> currentCallingPriorities)
        {
            InitializeComponent();
            PopulateListPriorities(currentCategoryWeights);
            PopulateCallingList(currentCallingPriorities);
            PopulateSortList(currentOrder);
            PopulateBeamCombo(currentBeam);
        }

        // ── InitializeComponent ────────────────────────────────────────────────
        private void InitializeComponent()
        {
            var font = new Font("Microsoft Sans Serif", 8.25F);

            // ── Top-level containers ───────────────────────────────────────────
            this.tabControl               = new TabControl();
            this.listPrioritiesTabPage    = new TabPage();
            this.callingPrioritiesTabPage = new TabPage();
            this.sortTabPage              = new TabPage();
            this.helpTabPage              = new TabPage();

            // ── Tab 1 controls ─────────────────────────────────────────────────
            this.listPriorityInfoLabel      = new Label();
            this.listPriorityListBox        = new ListBox();
            this.listMoveUpButton           = new Button();
            this.listMoveDownButton         = new Button();
            this.restoreListDefaultsButton  = new Button();

            // ── Tab 2 controls ─────────────────────────────────────────────────
            this.callingInfoLabel              = new Label();
            this.callingCheckedListBox         = new CheckedListBox();
            this.restoreCallingDefaultsButton  = new Button();

            // ── Tab 3 controls ─────────────────────────────────────────────────
            this.sortInfoLabel          = new Label();
            this.sortCheckedListBox     = new CheckedListBox();
            this.sortMoveUpButton       = new Button();
            this.sortMoveDownButton     = new Button();
            this.beamStaticLabel        = new Label();
            this.beamComboBox           = new ComboBox();
            this.restoreSortDefaultsButton = new Button();

            // ── Tab 4 controls ─────────────────────────────────────────────────
            this.helpTextBox = new TextBox();

            // ── Form-level buttons ─────────────────────────────────────────────
            this.okButton     = new Button();
            this.cancelButton = new Button();

            this.tabControl.SuspendLayout();
            this.listPrioritiesTabPage.SuspendLayout();
            this.callingPrioritiesTabPage.SuspendLayout();
            this.sortTabPage.SuspendLayout();
            this.helpTabPage.SuspendLayout();
            this.SuspendLayout();

            // ══ TAB CONTROL ════════════════════════════════════════════════════
            this.tabControl.Controls.Add(this.listPrioritiesTabPage);
            this.tabControl.Controls.Add(this.callingPrioritiesTabPage);
            this.tabControl.Controls.Add(this.sortTabPage);
            this.tabControl.Controls.Add(this.helpTabPage);
            this.tabControl.Location      = new Point(8, 8);
            this.tabControl.Name          = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size          = new Size(406, 306);
            this.tabControl.TabIndex      = 0;
            this.tabControl.AccessibleName        = "Call Waiting Sort Order settings";
            this.tabControl.AccessibleDescription = "Four tabs: List Priorities, Calling Priorities, Normal Sort Order, Help.";

            // ══ TAB 1 – LIST PRIORITIES ════════════════════════════════════════
            this.listPrioritiesTabPage.Controls.Add(this.listPriorityInfoLabel);
            this.listPrioritiesTabPage.Controls.Add(this.listPriorityListBox);
            this.listPrioritiesTabPage.Controls.Add(this.listMoveUpButton);
            this.listPrioritiesTabPage.Controls.Add(this.listMoveDownButton);
            this.listPrioritiesTabPage.Controls.Add(this.restoreListDefaultsButton);
            this.listPrioritiesTabPage.Name           = "listPrioritiesTabPage";
            this.listPrioritiesTabPage.Text           = "List Priorities";
            this.listPrioritiesTabPage.Padding        = new Padding(6);
            this.listPrioritiesTabPage.AccessibleName = "List Priorities tab";
            this.listPrioritiesTabPage.AccessibleDescription =
                "Controls display order in the main call waiting list, TX1 list, and TX2 list. " +
                "Select a row and use Move Up or Move Down to reorder. Default is always last.";

            // info label (no tab stop)
            this.listPriorityInfoLabel.AutoSize  = false;
            this.listPriorityInfoLabel.Font      = font;
            this.listPriorityInfoLabel.Location  = new Point(8, 6);
            this.listPriorityInfoLabel.Name      = "listPriorityInfoLabel";
            this.listPriorityInfoLabel.Size      = new Size(384, 26);
            this.listPriorityInfoLabel.TabStop   = false;
            this.listPriorityInfoLabel.Text      = "Select a row and use Move Up/Down to reorder. Default is always last.";
            this.listPriorityInfoLabel.AccessibleName = "List priority instructions";

            // ListBox – TabIndex 0
            this.listPriorityListBox.Font              = font;
            this.listPriorityListBox.FormattingEnabled = true;
            this.listPriorityListBox.Location          = new Point(8, 36);
            this.listPriorityListBox.Name              = "listPriorityListBox";
            this.listPriorityListBox.Size              = new Size(384, 160);
            this.listPriorityListBox.TabIndex          = 0;
            this.listPriorityListBox.AccessibleName    = "List priorities category list";
            this.listPriorityListBox.AccessibleDescription =
                "Categories listed top to bottom from highest to lowest display priority. " +
                "Select a category and press Tab to reach Move Up or Move Down. " +
                "Default is always last and cannot be moved.";
            this.listPriorityListBox.SelectedIndexChanged += new EventHandler(this.ListPriorityListBox_SelectedIndexChanged);

            // Move list priority up – TabIndex 1
            this.listMoveUpButton.Font                  = font;
            this.listMoveUpButton.Location              = new Point(8, 204);
            this.listMoveUpButton.Name                  = "listMoveUpButton";
            this.listMoveUpButton.Size                  = new Size(182, 26);
            this.listMoveUpButton.TabIndex              = 1;
            this.listMoveUpButton.Text                  = "Move Up";
            this.listMoveUpButton.UseVisualStyleBackColor = true;
            this.listMoveUpButton.AccessibleName        = "Move list priority up";
            this.listMoveUpButton.AccessibleDescription = "Moves the selected category one position higher in the list.";
            this.listMoveUpButton.Click                += new EventHandler(this.ListMoveUpButton_Click);

            // Move list priority down – TabIndex 2
            this.listMoveDownButton.Font                  = font;
            this.listMoveDownButton.Location              = new Point(198, 204);
            this.listMoveDownButton.Name                  = "listMoveDownButton";
            this.listMoveDownButton.Size                  = new Size(182, 26);
            this.listMoveDownButton.TabIndex              = 2;
            this.listMoveDownButton.Text                  = "Move Down";
            this.listMoveDownButton.UseVisualStyleBackColor = true;
            this.listMoveDownButton.AccessibleName        = "Move list priority down";
            this.listMoveDownButton.AccessibleDescription = "Moves the selected category one position lower in the list.";
            this.listMoveDownButton.Click                += new EventHandler(this.ListMoveDownButton_Click);

            // Restore list defaults – TabIndex 3
            this.restoreListDefaultsButton.Font                  = font;
            this.restoreListDefaultsButton.Location              = new Point(8, 238);
            this.restoreListDefaultsButton.Name                  = "restoreListDefaultsButton";
            this.restoreListDefaultsButton.Size                  = new Size(182, 26);
            this.restoreListDefaultsButton.TabIndex              = 3;
            this.restoreListDefaultsButton.Text                  = "Restore Defaults";
            this.restoreListDefaultsButton.UseVisualStyleBackColor = true;
            this.restoreListDefaultsButton.AccessibleName        = "Restore list priority defaults";
            this.restoreListDefaultsButton.AccessibleDescription =
                "Resets the list priority order to defaults. Does not save until OK is pressed.";
            this.restoreListDefaultsButton.Click += new EventHandler(this.RestoreListDefaultsButton_Click);

            // ══ TAB 2 – CALLING PRIORITIES ═════════════════════════════════════
            this.callingPrioritiesTabPage.Controls.Add(this.callingInfoLabel);
            this.callingPrioritiesTabPage.Controls.Add(this.callingCheckedListBox);
            this.callingPrioritiesTabPage.Controls.Add(this.restoreCallingDefaultsButton);
            this.callingPrioritiesTabPage.Name           = "callingPrioritiesTabPage";
            this.callingPrioritiesTabPage.Text           = "Calling Priorities";
            this.callingPrioritiesTabPage.Padding        = new Padding(6);
            this.callingPrioritiesTabPage.AccessibleName = "Calling Priorities tab";
            this.callingPrioritiesTabPage.AccessibleDescription =
                "Controls which categories Alt+N is allowed to call. " +
                "Checked categories may be called by Alt+N. Unchecked categories are skipped by Alt+N. " +
                "Space and Enter always call the selected row regardless of this setting.";

            // info label (no tab stop)
            this.callingInfoLabel.AutoSize  = false;
            this.callingInfoLabel.Font      = font;
            this.callingInfoLabel.Location  = new Point(8, 6);
            this.callingInfoLabel.Name      = "callingInfoLabel";
            this.callingInfoLabel.Size      = new Size(384, 40);
            this.callingInfoLabel.TabStop   = false;
            this.callingInfoLabel.Text      = "Checked = Alt+N may call this category. Unchecked = Alt+N skips it.\r\nSpace/Enter always call the selected row regardless of this setting.";
            this.callingInfoLabel.AccessibleName = "Calling priority instructions";

            // CheckedListBox – TabIndex 0
            this.callingCheckedListBox.CheckOnClick          = true;
            this.callingCheckedListBox.Font                  = font;
            this.callingCheckedListBox.FormattingEnabled     = true;
            this.callingCheckedListBox.Location              = new Point(8, 52);
            this.callingCheckedListBox.Name                  = "callingCheckedListBox";
            this.callingCheckedListBox.Size                  = new Size(384, 152);
            this.callingCheckedListBox.TabIndex              = 0;
            this.callingCheckedListBox.AccessibleName        = "Calling priorities category list";
            this.callingCheckedListBox.AccessibleDescription =
                "List of categories. Check a category to allow Alt+N to call it. " +
                "Uncheck to have Alt+N skip it. Default category is never called by Alt+N.";

            // Restore calling defaults – TabIndex 1
            this.restoreCallingDefaultsButton.Font                  = font;
            this.restoreCallingDefaultsButton.Location              = new Point(8, 212);
            this.restoreCallingDefaultsButton.Name                  = "restoreCallingDefaultsButton";
            this.restoreCallingDefaultsButton.Size                  = new Size(182, 26);
            this.restoreCallingDefaultsButton.TabIndex              = 1;
            this.restoreCallingDefaultsButton.Text                  = "Restore Defaults";
            this.restoreCallingDefaultsButton.UseVisualStyleBackColor = true;
            this.restoreCallingDefaultsButton.AccessibleName        = "Restore calling priority defaults";
            this.restoreCallingDefaultsButton.AccessibleDescription =
                "Checks all categories so Alt+N may call any of them. Does not save until OK is pressed.";
            this.restoreCallingDefaultsButton.Click += new EventHandler(this.RestoreCallingDefaultsButton_Click);

            // ══ TAB 3 – NORMAL SORT ORDER ══════════════════════════════════════
            this.sortTabPage.Controls.Add(this.sortInfoLabel);
            this.sortTabPage.Controls.Add(this.sortCheckedListBox);
            this.sortTabPage.Controls.Add(this.sortMoveUpButton);
            this.sortTabPage.Controls.Add(this.sortMoveDownButton);
            this.sortTabPage.Controls.Add(this.beamStaticLabel);
            this.sortTabPage.Controls.Add(this.beamComboBox);
            this.sortTabPage.Controls.Add(this.restoreSortDefaultsButton);
            this.sortTabPage.Name           = "sortTabPage";
            this.sortTabPage.Text           = "Normal Sort Order";
            this.sortTabPage.Padding        = new Padding(6);
            this.sortTabPage.AccessibleName = "Normal Sort Order tab";
            this.sortTabPage.AccessibleDescription =
                "Choose how Default category calls are ordered. " +
                "Check and order sort methods. Optionally set a beam direction preference.";

            // info label (no tab stop)
            this.sortInfoLabel.AutoSize  = false;
            this.sortInfoLabel.Font      = font;
            this.sortInfoLabel.Location  = new Point(8, 6);
            this.sortInfoLabel.Name      = "sortInfoLabel";
            this.sortInfoLabel.Size      = new Size(384, 26);
            this.sortInfoLabel.TabStop   = false;
            this.sortInfoLabel.Text      = "Checked = active. First checked item is primary sort. Select a row and use Move Up/Down.";
            this.sortInfoLabel.AccessibleName = "Sort order instructions";

            // Sort methods list – TabIndex 0
            this.sortCheckedListBox.CheckOnClick          = true;
            this.sortCheckedListBox.Font                  = font;
            this.sortCheckedListBox.FormattingEnabled     = true;
            this.sortCheckedListBox.Location              = new Point(8, 36);
            this.sortCheckedListBox.Name                  = "sortCheckedListBox";
            this.sortCheckedListBox.Size                  = new Size(384, 110);
            this.sortCheckedListBox.TabIndex              = 0;
            this.sortCheckedListBox.AccessibleName        = "Sort methods list";
            this.sortCheckedListBox.AccessibleDescription =
                "List of sort methods for Default calls. Check to activate. " +
                "The first checked item is the primary sort. " +
                "Select a row then press Tab to reach the Move Up or Move Down buttons.";
            this.sortCheckedListBox.SelectedIndexChanged += new EventHandler(this.SortCheckedListBox_SelectedIndexChanged);

            // Move sort method up – TabIndex 1
            this.sortMoveUpButton.Font                  = font;
            this.sortMoveUpButton.Location              = new Point(8, 154);
            this.sortMoveUpButton.Name                  = "sortMoveUpButton";
            this.sortMoveUpButton.Size                  = new Size(182, 26);
            this.sortMoveUpButton.TabIndex              = 1;
            this.sortMoveUpButton.Text                  = "Move Up";
            this.sortMoveUpButton.UseVisualStyleBackColor = true;
            this.sortMoveUpButton.AccessibleName        = "Move sort method up";
            this.sortMoveUpButton.AccessibleDescription = "Moves the selected sort method one position higher in priority.";
            this.sortMoveUpButton.Click                += new EventHandler(this.SortMoveUpButton_Click);

            // Move sort method down – TabIndex 2
            this.sortMoveDownButton.Font                  = font;
            this.sortMoveDownButton.Location              = new Point(198, 154);
            this.sortMoveDownButton.Name                  = "sortMoveDownButton";
            this.sortMoveDownButton.Size                  = new Size(182, 26);
            this.sortMoveDownButton.TabIndex              = 2;
            this.sortMoveDownButton.Text                  = "Move Down";
            this.sortMoveDownButton.UseVisualStyleBackColor = true;
            this.sortMoveDownButton.AccessibleName        = "Move sort method down";
            this.sortMoveDownButton.AccessibleDescription = "Moves the selected sort method one position lower in priority.";
            this.sortMoveDownButton.Click                += new EventHandler(this.SortMoveDownButton_Click);

            // Beam static label (no tab stop)
            this.beamStaticLabel.AutoSize       = true;
            this.beamStaticLabel.Font           = font;
            this.beamStaticLabel.Location       = new Point(8, 192);
            this.beamStaticLabel.Name           = "beamStaticLabel";
            this.beamStaticLabel.TabStop        = false;
            this.beamStaticLabel.Text           = "Beam preference:";
            this.beamStaticLabel.AccessibleName = "Beam preference label";

            // Beam combo – TabIndex 3
            this.beamComboBox.DropDownStyle     = ComboBoxStyle.DropDownList;
            this.beamComboBox.Font              = font;
            this.beamComboBox.FormattingEnabled = true;
            this.beamComboBox.Location          = new Point(120, 188);
            this.beamComboBox.Name              = "beamComboBox";
            this.beamComboBox.Size              = new Size(100, 21);
            this.beamComboBox.TabIndex          = 3;
            this.beamComboBox.AccessibleName    = "Beam preference";
            this.beamComboBox.AccessibleDescription =
                "Select a beam heading to favor callers in that direction. None disables beam ranking.";

            // Restore sort defaults – TabIndex 4
            this.restoreSortDefaultsButton.Font                  = font;
            this.restoreSortDefaultsButton.Location              = new Point(8, 220);
            this.restoreSortDefaultsButton.Name                  = "restoreSortDefaultsButton";
            this.restoreSortDefaultsButton.Size                  = new Size(182, 26);
            this.restoreSortDefaultsButton.TabIndex              = 4;
            this.restoreSortDefaultsButton.Text                  = "Restore Sort Defaults";
            this.restoreSortDefaultsButton.UseVisualStyleBackColor = true;
            this.restoreSortDefaultsButton.AccessibleName        = "Restore sort defaults";
            this.restoreSortDefaultsButton.AccessibleDescription =
                "Resets the sort method order and beam preference to defaults. Does not save until OK is pressed.";
            this.restoreSortDefaultsButton.Click += new EventHandler(this.RestoreSortDefaultsButton_Click);

            // ══ TAB 4 – HELP / NOTES ══════════════════════════════════════════
            this.helpTabPage.Controls.Add(this.helpTextBox);
            this.helpTabPage.Name           = "helpTabPage";
            this.helpTabPage.Text           = "Help";
            this.helpTabPage.Padding        = new Padding(6);
            this.helpTabPage.AccessibleName = "Help tab";
            this.helpTabPage.AccessibleDescription = "Explanation of how list priorities, calling priorities, and sort order work.";

            // Help text – TabIndex 0 (read-only, focusable for screen reader)
            this.helpTextBox.Multiline      = true;
            this.helpTextBox.ReadOnly       = true;
            this.helpTextBox.ScrollBars     = ScrollBars.Vertical;
            this.helpTextBox.BorderStyle    = BorderStyle.None;
            this.helpTextBox.BackColor      = SystemColors.Control;
            this.helpTextBox.Font           = font;
            this.helpTextBox.Location       = new Point(8, 8);
            this.helpTextBox.Name           = "helpTextBox";
            this.helpTextBox.Size           = new Size(384, 260);
            this.helpTextBox.TabIndex       = 0;
            this.helpTextBox.TabStop        = true;
            this.helpTextBox.AccessibleName = "Help notes";
            this.helpTextBox.AccessibleDescription = "Explanation of call ranking settings.";
            this.helpTextBox.Text =
                "List Priorities\r\n" +
                "───────────────\r\n" +
                "Controls the display order in the Main Call Waiting list, TX1, and TX2.\r\n" +
                "Categories higher in the list appear before categories lower in the list.\r\n" +
                "All non-Default categories are always elevated above Default calls.\r\n" +
                "Default is always last and cannot be moved.\r\n" +
                "\r\n" +
                "Calling Priorities\r\n" +
                "──────────────────\r\n" +
                "Controls which categories Alt+N is allowed to call.\r\n" +
                "Checked = Alt+N may call this category.\r\n" +
                "Unchecked = Alt+N skips this category and looks for the next eligible one.\r\n" +
                "Example: POTA can appear at the top of the list but be unchecked so\r\n" +
                "Alt+N skips POTA and calls the next eligible category instead.\r\n" +
                "Space and Enter always call the selected row, unaffected by this setting.\r\n" +
                "\r\n" +
                "Normal Sort Order\r\n" +
                "─────────────────\r\n" +
                "Controls how Default category calls are sorted within their tier.\r\n" +
                "Check one or more methods. The first checked item is the primary sort.\r\n" +
                "Beam preference favors callers in a compass direction.\r\n" +
                "\r\n" +
                "Keyboard shortcuts\r\n" +
                "──────────────────\r\n" +
                "Space or Enter — call the selected row in the call list.\r\n" +
                "Alt+N — call the best eligible category call in the current view context.";

            // ══ FORM-LEVEL BUTTONS ═════════════════════════════════════════════
            // OK – TabIndex 1 (second stop on form, after TabControl)
            this.okButton.Font                  = font;
            this.okButton.Location              = new Point(246, 322);
            this.okButton.Name                  = "okButton";
            this.okButton.Size                  = new Size(78, 28);
            this.okButton.TabIndex              = 1;
            this.okButton.Text                  = "OK";
            this.okButton.UseVisualStyleBackColor = true;
            this.okButton.AccessibleName        = "OK, save and close";
            this.okButton.AccessibleDescription =
                "Save the sort order and ranking settings and close the dialog. " +
                "At least one sort method must be checked.";
            this.okButton.Click += new EventHandler(this.OkButton_Click);

            // Cancel – TabIndex 2 (third stop on form, after OK)
            this.cancelButton.Font                  = font;
            this.cancelButton.Location              = new Point(330, 322);
            this.cancelButton.Name                  = "cancelButton";
            this.cancelButton.Size                  = new Size(78, 28);
            this.cancelButton.TabIndex              = 2;
            this.cancelButton.Text                  = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            this.cancelButton.AccessibleName        = "Cancel, discard changes";
            this.cancelButton.AccessibleDescription = "Close the dialog without saving any changes.";
            this.cancelButton.DialogResult          = DialogResult.Cancel;

            // ══ FORM ═══════════════════════════════════════════════════════════
            this.AcceptButton    = this.okButton;
            this.CancelButton    = this.cancelButton;
            this.ClientSize      = new Size(422, 360);
            this.Controls.Add(this.tabControl);
            this.Controls.Add(this.okButton);
            this.Controls.Add(this.cancelButton);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox     = false;
            this.MinimizeBox     = false;
            this.Name            = "RankOrderDlg";
            this.StartPosition   = FormStartPosition.CenterParent;
            this.Text            = "Call Waiting Sort Order";
            this.AccessibleName  = "Call Waiting Sort Order dialog";
            this.ShowInTaskbar   = false;

            this.helpTabPage.ResumeLayout(false);
            this.sortTabPage.ResumeLayout(false);
            this.sortTabPage.PerformLayout();
            this.callingPrioritiesTabPage.ResumeLayout(false);
            this.listPrioritiesTabPage.ResumeLayout(false);
            this.tabControl.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        // ── Tab 1: List Priorities logic ───────────────────────────────────────

        private void PopulateListPriorities(
            Dictionary<WsjtxClient.CallCategory, int> currentWeights)
        {
            var weights = (currentWeights != null && currentWeights.Count > 0)
                ? currentWeights
                : DefaultCategoryWeights;

            var nonDefault = new List<WsjtxClient.CallCategory>();
            foreach (WsjtxClient.CallCategory cat in Enum.GetValues(typeof(WsjtxClient.CallCategory)))
            {
                if (cat != WsjtxClient.CallCategory.DEFAULT) nonDefault.Add(cat);
            }
            nonDefault.Sort((a, b) =>
            {
                int wa = weights.ContainsKey(a) ? weights[a] : 0;
                int wb = weights.ContainsKey(b) ? weights[b] : 0;
                return wb.CompareTo(wa); // descending: highest tier first
            });

            listPriorityListBox.Items.Clear();
            foreach (var cat in nonDefault)
            {
                string label = CategoryLabels.ContainsKey(cat) ? CategoryLabels[cat] : cat.ToString();
                listPriorityListBox.Items.Add(new CategoryEntry(cat, label));
            }
            // DEFAULT always last
            listPriorityListBox.Items.Add(
                new CategoryEntry(WsjtxClient.CallCategory.DEFAULT, CategoryLabels[WsjtxClient.CallCategory.DEFAULT]));

            if (listPriorityListBox.Items.Count > 0)
                listPriorityListBox.SelectedIndex = 0;

            UpdateListMoveButtons();
        }

        private void ListMoveUpButton_Click(object sender, EventArgs e)   => MoveListItem(-1);
        private void ListMoveDownButton_Click(object sender, EventArgs e) => MoveListItem(1);

        private void MoveListItem(int direction)
        {
            int index = listPriorityListBox.SelectedIndex;
            if (index < 0) return;

            var entry = (CategoryEntry)listPriorityListBox.Items[index];
            if (entry.Category == WsjtxClient.CallCategory.DEFAULT) return;

            int target = index + direction;
            if (target < 0) return;

            if (target >= listPriorityListBox.Items.Count) return;
            var targetEntry = (CategoryEntry)listPriorityListBox.Items[target];
            if (targetEntry.Category == WsjtxClient.CallCategory.DEFAULT) return;

            object currentItem = listPriorityListBox.Items[index];
            object targetItem  = listPriorityListBox.Items[target];

            listPriorityListBox.Items[target] = currentItem;
            listPriorityListBox.Items[index]  = targetItem;
            listPriorityListBox.SelectedIndex = target;
            listPriorityListBox.Focus();
            UpdateListMoveButtons();
        }

        private void ListPriorityListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateListMoveButtons();
        }

        private void UpdateListMoveButtons()
        {
            int index = listPriorityListBox.SelectedIndex;
            int count = listPriorityListBox.Items.Count;
            if (index < 0 || count == 0)
            {
                listMoveUpButton.Enabled = listMoveDownButton.Enabled = false;
                return;
            }
            var entry     = (CategoryEntry)listPriorityListBox.Items[index];
            bool isDefault = (entry.Category == WsjtxClient.CallCategory.DEFAULT);
            listMoveUpButton.Enabled   = !isDefault && index > 0;
            listMoveDownButton.Enabled = !isDefault && index < count - 2; // DEFAULT is always last
        }

        private void RestoreListDefaultsButton_Click(object sender, EventArgs e)
        {
            PopulateListPriorities(DefaultCategoryWeights);
        }

        // ── Tab 2: Calling Priorities logic ────────────────────────────────────

        private void PopulateCallingList(HashSet<WsjtxClient.CallCategory> currentCallingPriorities)
        {
            var calling = (currentCallingPriorities != null)
                ? currentCallingPriorities
                : DefaultCallingPriorities;

            // Order follows list priorities order (same as current list weights default)
            var ordered = new List<WsjtxClient.CallCategory>
            {
                WsjtxClient.CallCategory.NEW_COUNTRY,
                WsjtxClient.CallCategory.NEW_COUNTRY_ON_BAND,
                WsjtxClient.CallCategory.ALWAYS_WANTED,
                WsjtxClient.CallCategory.TO_MYCALL,
                WsjtxClient.CallCategory.MANUAL_SEL,
                WsjtxClient.CallCategory.WANTED_CQ,
                WsjtxClient.CallCategory.POTA,
                WsjtxClient.CallCategory.SOTA,
            };

            callingCheckedListBox.Items.Clear();
            foreach (var cat in ordered)
            {
                string label = CategoryLabels.ContainsKey(cat) ? CategoryLabels[cat] : cat.ToString();
                bool enabled = calling.Contains(cat);
                callingCheckedListBox.Items.Add(new CategoryEntry(cat, label), enabled);
            }

            if (callingCheckedListBox.Items.Count > 0)
                callingCheckedListBox.SelectedIndex = 0;
        }

        private void RestoreCallingDefaultsButton_Click(object sender, EventArgs e)
        {
            PopulateCallingList(DefaultCallingPriorities);
        }

        // ── Tab 3: Normal Sort Order logic ─────────────────────────────────────

        private void PopulateSortList(List<WsjtxClient.RankMethods> currentOrder)
        {
            var activeSet      = new HashSet<WsjtxClient.RankMethods>(
                currentOrder ?? Enumerable.Empty<WsjtxClient.RankMethods>());
            var orderedEntries = new List<SortEntry>();

            if (currentOrder != null)
            {
                foreach (var m in currentOrder)
                {
                    var entry = Array.Find(DefaultSortEntries, e => e.Method == m);
                    if (entry != null) orderedEntries.Add(entry);
                }
            }
            foreach (var entry in DefaultSortEntries)
            {
                if (!orderedEntries.Exists(e => e.Method == entry.Method))
                    orderedEntries.Add(entry);
            }

            sortCheckedListBox.Items.Clear();
            foreach (var entry in orderedEntries)
                sortCheckedListBox.Items.Add(entry, activeSet.Contains(entry.Method));

            if (sortCheckedListBox.Items.Count > 0)
                sortCheckedListBox.SelectedIndex = 0;

            UpdateSortMoveButtons();
        }

        private void PopulateBeamCombo(WsjtxClient.RankMethods? currentBeam)
        {
            beamComboBox.Items.Clear();
            int selectedIdx = 0;
            for (int i = 0; i < BeamEntries.Length; i++)
            {
                beamComboBox.Items.Add(BeamEntries[i]);
                if (BeamEntries[i].Method == currentBeam)
                    selectedIdx = i;
            }
            beamComboBox.SelectedIndex = selectedIdx;
        }

        private void SortMoveUpButton_Click(object sender, EventArgs e)   => MoveSortItem(-1);
        private void SortMoveDownButton_Click(object sender, EventArgs e) => MoveSortItem(1);

        private void MoveSortItem(int direction)
        {
            int index = sortCheckedListBox.SelectedIndex;
            if (index < 0) return;
            int target = index + direction;
            if (target < 0 || target >= sortCheckedListBox.Items.Count) return;

            object currentItem = sortCheckedListBox.Items[index];
            bool   currentChk  = sortCheckedListBox.GetItemChecked(index);
            object targetItem  = sortCheckedListBox.Items[target];
            bool   targetChk   = sortCheckedListBox.GetItemChecked(target);

            sortCheckedListBox.Items[target] = currentItem;
            sortCheckedListBox.Items[index]  = targetItem;
            sortCheckedListBox.SetItemChecked(target, currentChk);
            sortCheckedListBox.SetItemChecked(index, targetChk);
            sortCheckedListBox.SelectedIndex = target;
            sortCheckedListBox.Focus();
            UpdateSortMoveButtons();
        }

        private void SortCheckedListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateSortMoveButtons();
        }

        private void UpdateSortMoveButtons()
        {
            int index = sortCheckedListBox.SelectedIndex;
            sortMoveUpButton.Enabled   = (index > 0);
            sortMoveDownButton.Enabled = (index >= 0 && index < sortCheckedListBox.Items.Count - 1);
        }

        private void RestoreSortDefaultsButton_Click(object sender, EventArgs e)
        {
            PopulateSortList(new List<WsjtxClient.RankMethods> { WsjtxClient.RankMethods.MOST_RECENT });
            beamComboBox.SelectedIndex = 0; // None
        }

        // ── OK ─────────────────────────────────────────────────────────────────

        private void OkButton_Click(object sender, EventArgs e)
        {
            // Validate: at least one sort method must be checked
            var selected = new List<WsjtxClient.RankMethods>();
            for (int i = 0; i < sortCheckedListBox.Items.Count; i++)
            {
                if (sortCheckedListBox.GetItemChecked(i))
                    selected.Add(((SortEntry)sortCheckedListBox.Items[i]).Method);
            }

            if (selected.Count == 0)
            {
                tabControl.SelectedTab = sortTabPage;
                MessageBox.Show(this,
                    "At least one sort option must be checked.",
                    "Call Waiting Sort Order",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                sortCheckedListBox.Focus();
                return;
            }

            SelectedOrder = selected;
            SelectedBeam  = ((BeamEntry)beamComboBox.SelectedItem)?.Method;

            // Build category weights from list priority order (position 0 = highest tier)
            int nonDefaultCount = listPriorityListBox.Items.Count - 1; // exclude DEFAULT
            var weights = new Dictionary<WsjtxClient.CallCategory, int>();

            for (int i = 0; i < listPriorityListBox.Items.Count; i++)
            {
                var entry = (CategoryEntry)listPriorityListBox.Items[i];
                if (entry.Category == WsjtxClient.CallCategory.DEFAULT)
                {
                    weights[entry.Category] = 0;
                }
                else
                {
                    weights[entry.Category] = nonDefaultCount - i; // position 0 → highest tier
                }
            }

            SelectedCategoryWeights = weights;

            // Build calling priorities from checked items
            var callingSet = new HashSet<WsjtxClient.CallCategory>();
            for (int i = 0; i < callingCheckedListBox.Items.Count; i++)
            {
                if (callingCheckedListBox.GetItemChecked(i))
                {
                    var entry = (CategoryEntry)callingCheckedListBox.Items[i];
                    callingSet.Add(entry.Category);
                }
            }
            SelectedCallingPriorities = callingSet;

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        // ── Inner record types ─────────────────────────────────────────────────

        private class SortEntry
        {
            public WsjtxClient.RankMethods Method { get; }
            private readonly string label;
            public SortEntry(WsjtxClient.RankMethods method, string label) { Method = method; this.label = label; }
            public override string ToString() => label;
        }

        private class BeamEntry
        {
            public WsjtxClient.RankMethods? Method { get; }
            private readonly string label;
            public BeamEntry(WsjtxClient.RankMethods? method, string label) { Method = method; this.label = label; }
            public override string ToString() => label;
        }

        private class CategoryEntry
        {
            public WsjtxClient.CallCategory Category { get; }
            private readonly string label;
            public CategoryEntry(WsjtxClient.CallCategory cat, string label) { Category = cat; this.label = label; }
            public override string ToString() => label;
        }
    }
}
