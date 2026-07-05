using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WSJTX_Controller
{
    public partial class CallWaitingRowOrderDlg : Form
    {
        public static readonly string[] DefaultFields = new string[] { "callp", "pri", "tag", "grid", "snr", "country", "distAz", "oe", "descr", "rankStr" };

        public static readonly Dictionary<string, string> FieldLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "callp", "Call Sign" },
            { "pri", "Reply Status" },
            { "tag", "Alert" },
            { "grid", "Grid" },
            { "snr", "SNR" },
            { "country", "Country" },
            { "distAz", "Distance and Direction" },
            { "oe", "Age" },
            { "descr", "Reason" },
            { "rankStr", "Rank" }
        };

        public List<string> SelectedFields { get; private set; }

        private readonly bool _debug;

        public CallWaitingRowOrderDlg(List<string> currentOrder, bool debug)
        {
            _debug = debug;
            InitializeComponent();
            PopulateList(currentOrder);
        }

        private void PopulateList(List<string> currentOrder)
        {
            var selectedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orderedFields = new List<string>();

            if (currentOrder != null)
            {
                foreach (var field in currentOrder)
                {
                    if (string.IsNullOrWhiteSpace(field)) continue;
                    if (!DefaultFields.Contains(field, StringComparer.OrdinalIgnoreCase)) continue;
                    if (orderedFields.Exists(s => string.Equals(s, field, StringComparison.OrdinalIgnoreCase))) continue;
                    orderedFields.Add(field);
                    selectedSet.Add(field);
                }
            }

            foreach (var field in DefaultFields)
            {
                if (!orderedFields.Exists(s => string.Equals(s, field, StringComparison.OrdinalIgnoreCase)))
                {
                    orderedFields.Add(field);
                }
            }

            checkedListBox.Items.Clear();
            foreach (var field in orderedFields)
            {
                if (!_debug && (string.Equals(field, "descr", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(field, "oe", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(field, "rankStr", StringComparison.OrdinalIgnoreCase))) continue;
                checkedListBox.Items.Add(new FieldItem(field), selectedSet.Contains(field));
            }

            if (checkedListBox.Items.Count > 0)
            {
                checkedListBox.SelectedIndex = 0;
            }

            UpdateMoveButtons();
        }

        private void MoveUpButton_Click(object sender, EventArgs e)
        {
            MoveSelectedItem(-1);
            BeginInvoke((Action)(() =>
            {
                if (moveUpButton.Enabled) moveUpButton.Focus();
                else checkedListBox.Focus();
            }));
        }

        private void MoveDownButton_Click(object sender, EventArgs e)
        {
            MoveSelectedItem(1);
            BeginInvoke((Action)(() =>
            {
                if (moveDownButton.Enabled) moveDownButton.Focus();
                else checkedListBox.Focus();
            }));
        }

        private void MoveSelectedItem(int direction)
        {
            int index = checkedListBox.SelectedIndex;
            if (index < 0) return;

            int targetIndex = index + direction;
            if (targetIndex < 0 || targetIndex >= checkedListBox.Items.Count) return;

            object currentItem = checkedListBox.Items[index];
            bool currentChecked = checkedListBox.GetItemChecked(index);
            object targetItem = checkedListBox.Items[targetIndex];
            bool targetChecked = checkedListBox.GetItemChecked(targetIndex);

            checkedListBox.Items[targetIndex] = currentItem;
            checkedListBox.Items[index] = targetItem;
            checkedListBox.SetItemChecked(targetIndex, currentChecked);
            checkedListBox.SetItemChecked(index, targetChecked);
            checkedListBox.SelectedIndex = targetIndex;
            UpdateMoveButtons();
        }

        private void RestoreDefaultButton_Click(object sender, EventArgs e)
        {
            PopulateList(new List<string>(DefaultFields));
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            var selectedFields = new List<string>();
            for (int i = 0; i < checkedListBox.Items.Count; i++)
            {
                if (checkedListBox.GetItemChecked(i))
                {
                    selectedFields.Add(((FieldItem)checkedListBox.Items[i]).Id);
                }
            }

            if (selectedFields.Count == 0)
            {
                MessageBox.Show(this, "Select at least one field to show in stations available rows.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SelectedFields = selectedFields;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void CheckedListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateMoveButtons();
        }

        private void UpdateMoveButtons()
        {
            int index = checkedListBox.SelectedIndex;
            moveUpButton.Enabled = (index > 0);
            moveDownButton.Enabled = (index >= 0 && index < checkedListBox.Items.Count - 1);
        }

        private class FieldItem
        {
            public string Id { get; }
            public string Label { get; }

            public FieldItem(string id)
            {
                Id = id;
                Label = FieldLabels.TryGetValue(id, out var label) ? label : id;
            }

            public override string ToString()
            {
                return Label;
            }
        }
    }
}
