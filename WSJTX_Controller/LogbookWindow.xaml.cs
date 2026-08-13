using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace WSJTX_Controller
{
    // WPF port of LogbookWindow, scoped to its two most-used pages for this migration pass:
    // My Log (dashboard stats + recent QSOs) and Edit Log (search/browse/add/edit/delete/export).
    // The WinForms original's other four tabs (Awards, Still Need, Lookup-standalone, Sync) are
    // deferred -- see the migration report. All logging/upload BUSINESS logic (LogbookDb,
    // RuleEngine, LiveQsoUploadOrchestrator) is unaffected; there is just no browsing UI yet for
    // those specific views. Category-list navigation, matching Options/the current WinForms
    // pattern -- not tabs.
    public partial class LogbookWindow : Window
    {
        private readonly Controller ctrl;
        private LogbookDb _db;
        private List<string> _editLogRowOrder;

        private FrameworkElement myLogPanel, editLogPanel;

        // My Log controls
        private TextBox statTotalTb, statLotwTb, statQrzTb, statConfTb, statWasTb, statDxccTb, statWazTb,
            statUploadQrzTb, statUploadClubLogTb, statUploadLotwTb, statUploadHrdLogTb;
        private ListView dashRecentLv;

        // Edit Log controls
        private TextBox editCallTb, editDateFromTb, editDateToTb, editCountLbl;
        private ComboBox editSourceCb;
        private ListView editLv;
        private Button editEditBtn, editDeleteBtn, editExportBtn;

        private static readonly Dictionary<string, int> EditLogFieldWidths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { { "date", 80 }, { "time", 55 }, { "callsign", 90 }, { "band", 55 }, { "mode", 55 },
          { "state", 50 }, { "country", 120 }, { "confirmed", 80 }, { "source", 60 } };

        public LogbookWindow(Controller controller)
        {
            ctrl = controller;
            InitializeComponent();

            try { _db = new LogbookDb(); }
            catch (Exception ex) { Loaded += (s, e) => SetStatus("Database error: " + ex.Message); }

            _editLogRowOrder = Controller.ParseRowOrder(ctrl.ReadSetting("editLogRowOrder"), EditLogRowOrderWindow.DefaultFields)
                ?? new List<string>(EditLogRowOrderWindow.DefaultFields);

            myLogPanel = BuildMyLogPanel();
            editLogPanel = BuildEditLogPanel();

            Closed += (s, e) => { _db?.Dispose(); _db = null; };

            CategoryList.SelectedIndex = 0;
        }

        public void OpenAddQsoDialog()
        {
            CategoryList.SelectedIndex = 1;
            AddQso();
        }

        private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var panel = CategoryList.SelectedIndex == 1 ? editLogPanel : myLogPanel;
            CategoryHost.Child = panel;
            var peer = UIElementAutomationPeer.FromElement(CategoryHost) ?? UIElementAutomationPeer.CreatePeerForElement(CategoryHost);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

            if (CategoryList.SelectedIndex == 0) PopulateMyLog();
        }

        private void SetStatus(string msg) => StatusBox.Text = msg;

        // ── My Log ──

        private FrameworkElement BuildMyLogPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "My Log");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBox AddStat(string label)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                row.Children.Add(new TextBlock { Text = label, Width = 200, VerticalAlignment = VerticalAlignment.Center });
                var tb = new TextBox { IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, MinWidth = 300 };
                AutomationProperties.SetName(tb, label.TrimEnd(':'));
                row.Children.Add(tb);
                panel.Children.Add(row);
                return tb;
            }

            statTotalTb = AddStat("Total QSOs:");
            statConfTb = AddStat("Confirmed:");
            statLotwTb = AddStat("LoTW confirmed:");
            statQrzTb = AddStat("QRZ confirmed:");
            statWasTb = AddStat("Worked All States:");
            statDxccTb = AddStat("DXCC:");
            statWazTb = AddStat("Worked All Zones:");
            statUploadQrzTb = AddStat("QRZ upload status:");
            statUploadClubLogTb = AddStat("Club Log upload status:");
            statUploadLotwTb = AddStat("LoTW upload status:");
            statUploadHrdLogTb = AddStat("HRDLog upload status:");

            panel.Children.Add(new TextBlock { Text = "Most recent QSOs:", Margin = new Thickness(0, 12, 0, 4), FontWeight = FontWeights.Bold });
            dashRecentLv = MakeListView(new[] { ("Date", 80), ("Time", 55), ("Callsign", 90), ("Band", 55), ("Mode", 55), ("Country", 140), ("Confirmed", 80) });
            dashRecentLv.Height = 220;
            AutomationProperties.SetName(dashRecentLv, "Most recent QSOs");
            panel.Children.Add(dashRecentLv);

            return panel;
        }

        private void PopulateMyLog()
        {
            if (_db == null) { statTotalTb.Text = "Database not available."; return; }
            try
            {
                int total = _db.TotalQsos();
                int confirmed = _db.ConfirmedQsos();
                int lotwConf = _db.LotwConfirmedQsos();
                int qrzConf = _db.QrzConfirmedQsos();
                var (wasW, wasC) = _db.WasProgress();
                var (dxccW, dxccC) = _db.DxccProgress();
                var (wazW, wazC) = _db.WazProgress();

                statTotalTb.Text = total.ToString("N0");
                statLotwTb.Text = lotwConf.ToString("N0");
                statQrzTb.Text = qrzConf.ToString("N0");
                statConfTb.Text = confirmed.ToString("N0") + (total > 0 ? $"  ({100.0 * confirmed / total:0.0}%)" : "");
                statWasTb.Text = $"{wasW} / 50 worked,  {wasC} / 50 confirmed";
                statDxccTb.Text = $"{dxccW} worked,  {dxccC} confirmed";
                statWazTb.Text = $"{wazW} / 40 worked,  {wazC} / 40 confirmed";

                statUploadQrzTb.Text = FormatUploadStatus(_db.GetUploadSyncStatus("QRZ"));
                statUploadClubLogTb.Text = FormatUploadStatus(_db.GetUploadSyncStatus("CLUBLOG"));
                statUploadLotwTb.Text = FormatUploadStatus(_db.GetUploadSyncStatus("LOTW"));
                statUploadHrdLogTb.Text = FormatUploadStatus(_db.GetUploadSyncStatus("HRDLOG"));

                dashRecentLv.Items.Clear();
                foreach (var q in _db.GetRecentQsos(10))
                    dashRecentLv.Items.Add(new[] { FormatDate(q.QsoDate), FormatTime(q.TimeOn), q.Callsign, q.Band, q.Mode, q.Country, ConfirmedText(q.LotwQslRcvd, q.QrzQslRcvd) });
            }
            catch (Exception ex) { statTotalTb.Text = "Error: " + ex.Message; }
        }

        private static string FormatUploadStatus(LogbookDb.UploadSyncStatus s)
        {
            string last = s.LastUploadUtc.HasValue ? s.LastUploadUtc.Value.ToLocalTime().ToString("g") : "never";
            string synced = $"{s.UploadedCount:N0} synced";
            return s.PendingCount == 0 ? $"Up to date, {synced}  (last sync: {last})" : $"{s.PendingCount} pending, {synced}  (last sync: {last})";
        }

        // ── Edit Log ──

        private FrameworkElement BuildEditLogPanel()
        {
            var panel = new DockPanel { Margin = new Thickness(10) };
            AutomationProperties.SetName(panel, "Edit Log");

            var filterRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(filterRow, Dock.Top);

            filterRow.Children.Add(new TextBlock { Text = "Callsign:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            editCallTb = new TextBox { Width = 100, Margin = new Thickness(0, 0, 12, 0) };
            AutomationProperties.SetName(editCallTb, "Callsign filter");
            editCallTb.KeyDown += (s, e) => { if (e.Key == Key.Enter) DoEditSearch(); };
            filterRow.Children.Add(editCallTb);

            filterRow.Children.Add(new TextBlock { Text = "Source:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            editSourceCb = new ComboBox { Width = 110, Margin = new Thickness(0, 0, 12, 0) };
            AutomationProperties.SetName(editSourceCb, "Source filter");
            editSourceCb.Items.Add("(Any)");
            foreach (var s in QsoRecord.KnownSources) editSourceCb.Items.Add(s);
            editSourceCb.SelectedIndex = 0;
            filterRow.Children.Add(editSourceCb);

            filterRow.Children.Add(new TextBlock { Text = "Date from:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            editDateFromTb = new TextBox { Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(editDateFromTb, "Date from, format year month day, optional");
            editDateFromTb.KeyDown += (s, e) => { if (e.Key == Key.Enter) DoEditSearch(); };
            filterRow.Children.Add(editDateFromTb);

            filterRow.Children.Add(new TextBlock { Text = "to:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            editDateToTb = new TextBox { Width = 80, Margin = new Thickness(0, 0, 12, 0) };
            AutomationProperties.SetName(editDateToTb, "Date to, format year month day, optional");
            editDateToTb.KeyDown += (s, e) => { if (e.Key == Key.Enter) DoEditSearch(); };
            filterRow.Children.Add(editDateToTb);

            var searchBtn = new Button { Content = "Search", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
            searchBtn.Click += (s, e) => DoEditSearch();
            filterRow.Children.Add(searchBtn);

            var clearBtn = new Button { Content = "Clear", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
            clearBtn.Click += (s, e) => ClearEditLog();
            filterRow.Children.Add(clearBtn);

            var rowOrderBtn = new Button { Content = "Row Order...", Padding = new Thickness(8, 2, 8, 2) };
            AutomationProperties.SetName(rowOrderBtn, "Choose Edit Log column order");
            rowOrderBtn.Click += (s, e) => OpenRowOrderEditor();
            filterRow.Children.Add(rowOrderBtn);

            panel.Children.Add(filterRow);

            editCountLbl = new TextBox { IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Margin = new Thickness(0, 0, 0, 6) };
            AutomationProperties.SetName(editCountLbl, "Result count");
            DockPanel.SetDock(editCountLbl, Dock.Top);
            panel.Children.Add(editCountLbl);

            var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(buttonRow, Dock.Bottom);
            var addBtn = new Button { Content = "Add New...", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(addBtn, "Add a new QSO");
            addBtn.Click += (s, e) => AddQso();
            buttonRow.Children.Add(addBtn);

            editEditBtn = new Button { Content = "Edit...", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
            AutomationProperties.SetName(editEditBtn, "Edit selected QSO");
            editEditBtn.Click += (s, e) => EditQso();
            buttonRow.Children.Add(editEditBtn);

            editDeleteBtn = new Button { Content = "Delete...", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
            AutomationProperties.SetName(editDeleteBtn, "Delete selected QSOs");
            editDeleteBtn.Click += (s, e) => DeleteQsos();
            buttonRow.Children.Add(editDeleteBtn);

            editExportBtn = new Button { Content = "Export Selected...", Padding = new Thickness(8, 4, 8, 4), IsEnabled = false };
            AutomationProperties.SetName(editExportBtn, "Export selected QSOs to ADIF");
            editExportBtn.Click += (s, e) => ExportSelected();
            buttonRow.Children.Add(editExportBtn);

            panel.Children.Add(buttonRow);

            editLv = MakeListView(Array.Empty<(string, int)>());
            AutomationProperties.SetName(editLv, "Edit Log results list");
            editLv.SelectionMode = SelectionMode.Extended;
            RebuildEditLogColumns();
            editLv.SelectionChanged += (s, e) => UpdateEditLogButtons();
            panel.Children.Add(editLv);

            return panel;
        }

        private void RebuildEditLogColumns()
        {
            var gridView = new GridView();
            foreach (var field in _editLogRowOrder)
            {
                string label = EditLogRowOrderWindow.FieldLabels.TryGetValue(field, out var l) ? l : field;
                int width = EditLogFieldWidths.TryGetValue(field, out var w) ? w : 80;
                gridView.Columns.Add(new GridViewColumn { Header = label, Width = width, DisplayMemberBinding = null });
            }
            editLv.View = gridView;
        }

        private ListView MakeListView((string Header, int Width)[] columns)
        {
            var lv = new ListView { Height = 300 };
            var gv = new GridView();
            foreach (var c in columns) gv.Columns.Add(new GridViewColumn { Header = c.Header, Width = c.Width });
            lv.View = gv;
            return lv;
        }

        private void ClearEditLog()
        {
            editCallTb.Text = "";
            editSourceCb.SelectedIndex = 0;
            editDateFromTb.Text = "";
            editDateToTb.Text = "";
            editLv.Items.Clear();
            editCountLbl.Text = "";
            UpdateEditLogButtons();
            editCallTb.Focus();
        }

        private void UpdateEditLogButtons()
        {
            int n = editLv.SelectedItems.Count;
            editEditBtn.IsEnabled = n == 1;
            editDeleteBtn.IsEnabled = n >= 1;
            editExportBtn.IsEnabled = n >= 1;
        }

        private static string NormalizeDateFilter(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string digits = new string(s.Where(char.IsDigit).ToArray());
            return digits.Length >= 8 ? digits.Substring(0, 8) : "";
        }

        private void DoEditSearch()
        {
            if (_db == null) return;
            try
            {
                string call = editCallTb.Text.Trim();
                string source = editSourceCb.SelectedIndex > 0 ? (string)editSourceCb.SelectedItem : null;
                string dFrom = NormalizeDateFilter(editDateFromTb.Text);
                string dTo = NormalizeDateFilter(editDateToTb.Text);

                var results = _db.SearchQsos(call, source, dFrom, dTo);
                editLv.Items.Clear();
                foreach (var q in results)
                {
                    var row = new EditLogRow { Id = q.Id };
                    row.Values = _editLogRowOrder.Select(f => GetEditLogFieldValue(q, f)).ToArray();
                    editLv.Items.Add(row);
                }
                editCountLbl.Text = results.Count == 0 ? "No QSOs found." : $"{results.Count} QSO{(results.Count == 1 ? "" : "s")} found.";
                UpdateEditLogButtons();
                RebindEditLogRows();
            }
            catch (Exception ex) { SetStatus("Edit Log search error: " + ex.Message); }
        }

        // GridView columns bind by index into EditLogRow.Values -- rebuilt whenever the row
        // order/columns change, since DisplayMemberBinding needs a fresh Binding per column.
        private void RebindEditLogRows()
        {
            var gv = (GridView)editLv.View;
            for (int i = 0; i < gv.Columns.Count; i++)
                gv.Columns[i].DisplayMemberBinding = new System.Windows.Data.Binding($"Values[{i}]");
        }

        private class EditLogRow
        {
            public int Id;
            public string[] Values;
        }

        private string GetEditLogFieldValue(QsoRecord q, string field)
        {
            switch (field.ToLowerInvariant())
            {
                case "date": return FormatDate(q.QsoDate);
                case "time": return FormatTime(q.TimeOn);
                case "callsign": return q.Callsign;
                case "band": return q.Band;
                case "mode": return q.Mode;
                case "state": return q.State;
                case "country": return q.Country;
                case "confirmed": return ConfirmedText(q.LotwQslRcvd, q.QrzQslRcvd);
                case "source": return q.Source;
                default: return "";
            }
        }

        private List<int> SelectedEditIds() => editLv.SelectedItems.Cast<EditLogRow>().Select(r => r.Id).ToList();

        private void OpenRowOrderEditor()
        {
            var dlg = new EditLogRowOrderWindow(_editLogRowOrder) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            _editLogRowOrder = dlg.SelectedFields;
            ctrl.SaveSetting("editLogRowOrder", string.Join(",", _editLogRowOrder));
            RebuildEditLogColumns();
            DoEditSearch();
        }

        private void AddQso()
        {
            if (_db == null) return;
            bool live = ctrl.wsjtxClient != null && ctrl.wsjtxClient.ConnectedToWsjtx();
            var blank = new QsoRecord
            {
                QsoDate = DateTime.UtcNow.ToString("yyyyMMdd"),
                TimeOn = DateTime.UtcNow.ToString("HHmm"),
                Band = live ? (ctrl.wsjtxClient.CurrentBandStr ?? "") : "",
                Mode = live ? (ctrl.wsjtxClient.CurrentMode ?? "") : "",
            };

            string SubmitNewQso(QsoRecord r)
            {
                try
                {
                    string dedupKey = AdifImporter.BuildDedupKey(r.Callsign, r.Band, r.Mode, r.QsoDate, r.TimeOn);
                    _db.Upsert(r.Callsign, r.Band, r.Mode, r.QsoDate, r.TimeOn, r.TimeOff,
                        0, r.RstSent, r.RstRcvd, r.State, r.Country, 0, 0,
                        r.Grid, r.Name, r.Comment, "",
                        "", "", "",
                        "", "", "", "",
                        "MANUAL", "", dedupKey,
                        "", 0, "", "", "", "", "", "", "", "",
                        "", "");
                    SetStatus($"Added {r.Callsign}.");
                    DoEditSearch();
                    return null;
                }
                catch (Exception ex)
                {
                    return "Add failed: " + ex.Message + " (a QSO with this callsign/band/mode/date/time may already exist)";
                }
            }

            var dlg = new EditQsoWindow(blank, "Add New QSO", call => ctrl.wsjtxClient?.lookupManager?.Build(call), isNewEntry: true,
                onSubmit: SubmitNewQso, onLogged: () => ctrl.wsjtxClient?.Sounds?.PlaySoundEvent(ctrl.loggedCheckBox.Checked, ctrl.soundFile_Logged))
            { Owner = this };
            dlg.ShowDialog();
        }

        private void EditQso()
        {
            if (_db == null || editLv.SelectedItems.Count != 1) return;
            int id = ((EditLogRow)editLv.SelectedItems[0]).Id;
            var q = _db.GetQso(id);
            if (q == null) { SetStatus("That QSO no longer exists -- refreshing."); DoEditSearch(); return; }

            string SubmitEdit(QsoRecord r)
            {
                try
                {
                    bool ok = _db.UpdateQso(id, r.Callsign, r.Band, r.Mode, r.QsoDate, r.TimeOn, r.TimeOff,
                        r.State, r.Country, r.Grid, r.Name, r.RstSent, r.RstRcvd, r.Comment);
                    SetStatus(ok ? $"Updated {r.Callsign}." : "No changes were saved.");
                    DoEditSearch();
                    return null;
                }
                catch (Exception ex)
                {
                    return "Edit failed: " + ex.Message + " (a QSO with this callsign/band/mode/date/time may already exist)";
                }
            }

            var dlg = new EditQsoWindow(q, lookupCallsign: call => ctrl.wsjtxClient?.lookupManager?.Build(call), onSubmit: SubmitEdit) { Owner = this };
            dlg.ShowDialog();
        }

        private void DeleteQsos()
        {
            if (_db == null || editLv.SelectedItems.Count == 0) return;
            var ids = SelectedEditIds();

            var sample = editLv.SelectedItems.Cast<EditLogRow>().Take(5).Select(r => string.Join("  ", r.Values.Take(4)));
            string sampleText = string.Join("\n", sample);
            if (ids.Count > 5) sampleText += $"\n... and {ids.Count - 5} more";

            var confDlg = new ConfirmDlg
            {
                Owner = this,
                Text = $"Delete {ids.Count} QSO(s) from Jimmy's local logbook?\n\n{sampleText}\n\n" +
                       "This only removes them locally -- it does not contact QRZ, Club Log, or LoTW, and cannot be undone.",
            };
            confDlg.ShowDialog();
            if (!confDlg.Confirmed) return;

            try
            {
                int n = _db.DeleteQsos(ids);
                SetStatus($"Deleted {n} QSO(s) from the local logbook.");
                DoEditSearch();
            }
            catch (Exception ex) { SetStatus("Delete failed: " + ex.Message); }
        }

        private void ExportSelected()
        {
            if (_db == null || editLv.SelectedItems.Count == 0) return;
            ExportAdif(SelectedEditIds());
        }

        private void ExportAdif(List<int> ids)
        {
            if (_db == null) return;

            var sourceDlg = new ExportSourceFilterWindow { Owner = this };
            if (sourceDlg.ShowDialog() != true) return;
            var sources = sourceDlg.SelectedSources;

            var saveDlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export ADIF File",
                Filter = "ADIF files (*.adi)|*.adi|All files (*.*)|*.*",
                FileName = $"jimmy_export_{DateTime.Now:yyyyMMdd_HHmmss}.adi",
            };
            if (saveDlg.ShowDialog() != true) return;
            try
            {
                var fields = _db.GetAdifFieldDicts(ids, sources);
                File.WriteAllText(saveDlg.FileName, AdifExporter.BuildFile(fields));
                SetStatus($"Exported {fields.Count:N0} QSO(s) to {saveDlg.FileName}.");
            }
            catch (Exception ex) { SetStatus("Export error: " + ex.Message); }
        }

        private static string FormatDate(string d)
        {
            if (d == null || d.Length < 8) return d ?? "";
            return $"{d.Substring(0, 4)}-{d.Substring(4, 2)}-{d.Substring(6, 2)}";
        }

        private static string FormatTime(string t)
        {
            if (t == null || t.Length < 4) return t ?? "";
            return $"{t.Substring(0, 2)}:{t.Substring(2, 2)}";
        }

        private static string ConfirmedText(string lotw, string qrz)
        {
            bool l = lotw == "Y", q = qrz == "Y";
            if (l && q) return "LoTW + QRZ";
            if (l) return "LoTW";
            if (q) return "QRZ";
            return "";
        }
    }
}
