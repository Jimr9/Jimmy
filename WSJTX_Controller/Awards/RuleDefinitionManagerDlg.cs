using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WSJTX_Controller
{
    // View/create/edit/duplicate/rename/delete/enable/disable Rule Definitions.
    // Operates directly on RuleLibrary.Definitions/RuleLoader.RulesFolder -- the
    // same data every other award-aware view (Awards tab, Still Need tab, live
    // tagging) already reads, so a Reload() here is exactly what those views
    // need too (the caller is expected to refresh them after this dialog closes
    // if RulesChanged is true).
    public class RuleDefinitionManagerDlg : Form
    {
        private TextBox   _searchTb;
        private ListView  _listView;
        private TextBox   _detailTb;
        private Label     _statusLbl;
        private Button    _viewErrorsBtn;
        private Button    _editBtn, _duplicateBtn, _renameBtn, _deleteBtn, _enableBtn, _disableBtn;

        private List<RuleDefinition> _allDefs = new List<RuleDefinition>();
        private List<RuleDefinition> _shownDefs = new List<RuleDefinition>();

        // Set true whenever an action here could have changed what's on disk or
        // in RuleLibrary -- the caller should refresh Awards/Still Need/live
        // tagging views after ShowDialog() returns if this is true.
        public bool RulesChanged { get; private set; }

        public RuleDefinitionManagerDlg()
        {
            Text            = "Rule Definition Manager";
            StartPosition   = FormStartPosition.CenterParent;
            MinimumSize     = new Size(720, 480);
            Size            = new Size(860, 560);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox     = true;
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            KeyPreview      = true;

            BuildUi();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Load    += (s, e) => LoadFromLibrary();
        }

        private void BuildUi()
        {
            var font = Font;
            int tab = 1;

            var searchLbl = new Label
            {
                Text = "&Search:",
                Location = new Point(10, 12),
                AutoSize = true,
            };
            Controls.Add(searchLbl);

            _searchTb = new TextBox
            {
                Location       = new Point(70, 9),
                Size           = new Size(260, 20),
                Anchor         = AnchorStyles.Top | AnchorStyles.Left,
                TabIndex       = tab++,
                AccessibleName = "Search Rule Definitions by name, ID, sponsor, or category",
            };
            _searchTb.TextChanged += (s, e) => ApplyFilter();
            Controls.Add(_searchTb);

            _listView = new ListView
            {
                View            = View.Details,
                FullRowSelect   = true,
                MultiSelect     = false,
                HideSelection   = false,
                Location        = new Point(10, 38),
                Size            = new Size(500, 330),
                Anchor          = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                TabIndex        = tab++,
                AccessibleName  = "Rule Definitions list",
            };
            _listView.Columns.Add("Name", 190);
            _listView.Columns.Add("Sponsor", 90);
            _listView.Columns.Add("Category", 110);
            _listView.Columns.Add("Enabled", 60);
            _listView.Columns.Add("Status", 90);
            _listView.SelectedIndexChanged += (s, e) => { UpdateDetail(); UpdateButtonStates(); };
            _listView.DoubleClick += (s, e) => EditSelected();
            Controls.Add(_listView);

            _detailTb = new TextBox
            {
                Multiline       = true,
                ReadOnly        = true,
                ScrollBars      = ScrollBars.Vertical,
                Location        = new Point(10, 374),
                Size            = new Size(500, 90),
                Anchor          = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                TabIndex        = tab++,
                AccessibleName  = "Selected Rule Definition details",
            };
            Controls.Add(_detailTb);

            _statusLbl = new Label
            {
                Location       = new Point(10, 470),
                Size           = new Size(400, 18),
                Anchor         = AnchorStyles.Bottom | AnchorStyles.Left,
                AccessibleName = "Rule Definitions load status",
            };
            Controls.Add(_statusLbl);

            _viewErrorsBtn = new Button
            {
                Text           = "View Load Errors...",
                Location       = new Point(10, 490),
                Size           = new Size(140, 24),
                Anchor         = AnchorStyles.Bottom | AnchorStyles.Left,
                TabIndex       = tab++,
                AccessibleName = "View Rule Definition load errors",
                Visible        = false,
            };
            _viewErrorsBtn.Click += (s, e) => ShowLoadErrors();
            Controls.Add(_viewErrorsBtn);

            // ── Button column ──────────────────────────────────────────────
            int bx = 522, by = 38, bw = 200, bh = 26, gap = 6;

            Button MakeBtn(string text, string accName, EventHandler onClick)
            {
                var b = new Button
                {
                    Text           = text,
                    Location       = new Point(bx, by),
                    Size           = new Size(bw, bh),
                    Anchor         = AnchorStyles.Top | AnchorStyles.Right,
                    TabIndex       = tab++,
                    AccessibleName = accName,
                };
                b.Click += onClick;
                Controls.Add(b);
                by += bh + gap;
                return b;
            }

            MakeBtn("&New Rule...", "Create a new Rule Definition", (s, e) => NewRule());
            _editBtn      = MakeBtn("&Edit...", "Edit the selected Rule Definition", (s, e) => EditSelected());
            _duplicateBtn = MakeBtn("&Duplicate...", "Duplicate the selected Rule Definition", (s, e) => DuplicateSelected());
            _renameBtn    = MakeBtn("Rena&me...", "Rename the selected Rule Definition", (s, e) => RenameSelected());
            _deleteBtn    = MakeBtn("De&lete...", "Delete the selected Rule Definition", (s, e) => DeleteSelected());
            by += gap;
            _enableBtn    = MakeBtn("En&able", "Enable the selected Rule Definition", (s, e) => SetEnabled(true));
            _disableBtn   = MakeBtn("&Disable", "Disable the selected Rule Definition", (s, e) => SetEnabled(false));
            by += gap;
            MakeBtn("&Reload from Disk", "Reload Rule Definitions from disk", (s, e) => { LoadFromLibrary(); RulesChanged = true; });
            MakeBtn("Open Rule Definitions Folder", "Open the Rule Definitions folder", (s, e) => OpenFolder(RuleLoader.RulesFolder));
            MakeBtn("Open Lists Folder", "Open the companion Lists folder", (s, e) => OpenFolder(RuleLoader.ListsFolder));
            by += gap;
            MakeBtn("Manage Companion &Lists...", "Manage companion list files", (s, e) => OpenCompanionListEditor());

            var closeBtn = new Button
            {
                Text           = "Close",
                Location       = new Point(bx, 490),
                Size           = new Size(bw, bh),
                Anchor         = AnchorStyles.Bottom | AnchorStyles.Right,
                TabIndex       = tab++,
                DialogResult   = DialogResult.Cancel,
                AccessibleName = "Close Rule Definition Manager",
            };
            Controls.Add(closeBtn);
            CancelButton = closeBtn;
        }

        private void LoadFromLibrary()
        {
            try { RuleLibrary.Load(); } catch { }
            _allDefs = RuleLibrary.Definitions
                .OrderBy(d => d.Category ?? "").ThenBy(d => d.Name).ToList();
            ApplyFilter();
            UpdateStatus();
        }

        private void ApplyFilter()
        {
            string q = (_searchTb.Text ?? "").Trim();
            _shownDefs = q.Length == 0
                ? _allDefs
                : _allDefs.Where(d =>
                    (d.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (d.Id ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (d.Sponsor ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (d.Category ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                  ).ToList();

            string prevId = SelectedDef?.Id;
            _listView.BeginUpdate();
            _listView.Items.Clear();
            foreach (var d in _shownDefs)
            {
                var item = new ListViewItem(d.Name) { Tag = d };
                item.SubItems.Add(d.Sponsor ?? "");
                item.SubItems.Add(d.Category ?? "");
                item.SubItems.Add(d.Enabled ? "Yes" : "No");
                item.SubItems.Add(File.Exists(d.SourceFile) ? "OK" : "Missing file");
                _listView.Items.Add(item);
            }
            _listView.EndUpdate();

            if (prevId != null)
            {
                var again = _listView.Items.Cast<ListViewItem>()
                    .FirstOrDefault(i => ((RuleDefinition)i.Tag).Id == prevId);
                if (again != null) again.Selected = true;
            }
            UpdateDetail();
            UpdateButtonStates();
        }

        private RuleDefinition SelectedDef =>
            _listView.SelectedItems.Count > 0 ? (RuleDefinition)_listView.SelectedItems[0].Tag : null;

        private void UpdateDetail()
        {
            var d = SelectedDef;
            _detailTb.Text = d == null ? "" :
                $"Id: {d.Id}\r\n" +
                $"Description: {d.Description}\r\n" +
                $"GroupBy: {d.GroupBy}   Target: {d.Target}   Confirmation: {d.Confirmation}\r\n" +
                $"Source file: {d.SourceFile}";
        }

        private void UpdateButtonStates()
        {
            bool has = SelectedDef != null;
            _editBtn.Enabled = has;
            _duplicateBtn.Enabled = has;
            _renameBtn.Enabled = has;
            _deleteBtn.Enabled = has;
            _enableBtn.Enabled = has && !SelectedDef.Enabled;
            _disableBtn.Enabled = has && SelectedDef.Enabled;
        }

        private void UpdateStatus()
        {
            int errCount = RuleLibrary.LoadErrors.Count;
            _statusLbl.Text = $"{_allDefs.Count} Rule Definitions loaded" +
                (errCount > 0 ? $", {errCount} load error(s)" : "");
            _viewErrorsBtn.Visible = errCount > 0;
        }

        private void ShowLoadErrors()
        {
            MessageBox.Show(this,
                string.Join("\r\n\r\n", RuleLibrary.LoadErrors),
                "Rule Definition Load Errors",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open folder: " + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenCompanionListEditor()
        {
            using (var dlg = new CompanionListEditorDlg())
            {
                dlg.ShowDialog(this);
                // Companion list *content* can change evaluation results even
                // though no Rule Definition file changed -- treat this the same
                // as RulesChanged so the caller refreshes Awards/Still Need/live
                // tagging just in case.
                RulesChanged = true;
            }
        }

        private void NewRule()
        {
            using (var dlg = new NewRuleWizardDlg())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    RulesChanged = true;
                    LoadFromLibrary();
                    SelectById(dlg.SavedId);
                }
            }
        }

        private void EditSelected()
        {
            var d = SelectedDef;
            if (d == null) return;
            using (var dlg = new RuleDefinitionEditDlg(d))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    RulesChanged = true;
                    LoadFromLibrary();
                    SelectById(dlg.SavedId);
                }
            }
        }

        private void DuplicateSelected()
        {
            var d = SelectedDef;
            if (d == null) return;

            using (var prompt = new TextPromptDlg(
                "Duplicate Rule Definition", "New Rule Id (letters, digits, _ and - only):",
                d.Id + "_COPY", "New Rule Id"))
            {
                if (prompt.ShowDialog(this) != DialogResult.OK) return;
                string newId = prompt.Value.ToUpperInvariant();

                if (!System.Text.RegularExpressions.Regex.IsMatch(newId, @"^[A-Za-z0-9_-]+$"))
                {
                    MessageBox.Show(this, "Rule Id may only contain letters, digits, '_' and '-'.",
                        "Invalid Id", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (RuleLibrary.Definitions.Any(x => string.Equals(x.Id, newId, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(this, $"A Rule Definition with Id '{newId}' already exists.",
                        "Duplicate Id", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var copy = CloneDefinition(d);
                copy.Id = newId;
                copy.Name = d.Name + " (Copy)";
                string path = Path.Combine(RuleLoader.RulesFolder, newId + ".ini");
                try
                {
                    RuleWriter.Save(copy, path);
                    RulesChanged = true;
                    LoadFromLibrary();
                    SelectById(newId);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the duplicated Rule Definition: " + ex.Message,
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void RenameSelected()
        {
            var d = SelectedDef;
            if (d == null) return;

            // Renames the display Name only, never the Id -- the Id is a stable
            // identifier referenced elsewhere (e.g. the saved Still Need live-tag
            // selection), so changing it here would silently orphan that setting.
            using (var prompt = new TextPromptDlg("Rename Rule Definition", "Name:", d.Name, "Rule Definition name"))
            {
                if (prompt.ShowDialog(this) != DialogResult.OK) return;
                d.Name = prompt.Value;
                try
                {
                    RuleWriter.Save(d, d.SourceFile);
                    RulesChanged = true;
                    LoadFromLibrary();
                    SelectById(d.Id);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the rename: " + ex.Message,
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void DeleteSelected()
        {
            var d = SelectedDef;
            if (d == null) return;

            var confirm = MessageBox.Show(this,
                $"Delete '{d.Name}' ({d.Id})? This cannot be undone from here.",
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            try
            {
                if (File.Exists(d.SourceFile)) File.Delete(d.SourceFile);
                RulesChanged = true;
                LoadFromLibrary();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not delete: " + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetEnabled(bool enabled)
        {
            var d = SelectedDef;
            if (d == null || d.Enabled == enabled) return;
            d.Enabled = enabled;
            try
            {
                RuleWriter.Save(d, d.SourceFile);
                RulesChanged = true;
                LoadFromLibrary();
                SelectById(d.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save: " + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SelectById(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var item = _listView.Items.Cast<ListViewItem>()
                .FirstOrDefault(i => ((RuleDefinition)i.Tag).Id == id);
            if (item != null)
            {
                item.Selected = true;
                item.EnsureVisible();
            }
        }

        internal static RuleDefinition CloneDefinition(RuleDefinition d) => new RuleDefinition
        {
            Id = d.Id,
            Name = d.Name,
            Sponsor = d.Sponsor,
            Category = d.Category,
            FormatVersion = d.FormatVersion,
            Enabled = d.Enabled,
            Description = d.Description,
            Website = d.Website,
            GroupBy = d.GroupBy,
            Universe = d.Universe,
            LimitTo = d.LimitTo,
            Bands = new List<string>(d.Bands),
            Modes = new List<string>(d.Modes),
            CallsignPattern = d.CallsignPattern,
            Sig = d.Sig,
            DateFrom = d.DateFrom,
            DateTo = d.DateTo,
            Confirmation = d.Confirmation,
            Target = d.Target,
            Threshold = d.Threshold,
            Levels = d.Levels.Select(l => new RuleLevel { Name = l.Name, Threshold = l.Threshold }).ToList(),
            Endorsements = d.Endorsements == null ? null : new RuleEndorsements
            {
                Bands = new List<string>(d.Endorsements.Bands),
                Modes = new List<string>(d.Endorsements.Modes),
            },
        };
    }
}
