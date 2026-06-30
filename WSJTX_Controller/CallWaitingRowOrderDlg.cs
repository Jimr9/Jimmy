using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WSJTX_Controller
{
    public class CallWaitingRowOrderDlg : Form
    {
        private CheckedListBox checkedListBox;
        private Button moveUpButton;
        private Button moveDownButton;
        private Button restoreDefaultButton;
        private Button okButton;
        private Button cancelButton;
        private Label instructionsLabel;

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

        private void InitializeComponent()
        {
            this.instructionsLabel = new Label();
            this.checkedListBox = new CheckedListBox();
            this.moveUpButton = new Button();
            this.moveDownButton = new Button();
            this.restoreDefaultButton = new Button();
            this.okButton = new Button();
            this.cancelButton = new Button();
            this.SuspendLayout();
            // 
            // instructionsLabel
            // 
            this.instructionsLabel.AutoSize = true;
            this.instructionsLabel.Location = new Point(12, 12);
            this.instructionsLabel.Name = "instructionsLabel";
            this.instructionsLabel.Size = new Size(314, 13);
            this.instructionsLabel.TabIndex = 0;
            this.instructionsLabel.Text = "Checked fields are shown; use Move Up/Move Down to change display order.";
            this.instructionsLabel.AccessibleName = "Stations available row editor instructions";
            this.instructionsLabel.AccessibleDescription = "Explains that checked fields are visible and that items can be reordered with Move Up and Move Down.";
            // 
            // checkedListBox
            // 
            this.checkedListBox.FormattingEnabled = true;
            this.checkedListBox.Location = new Point(12, 32);
            this.checkedListBox.Name = "checkedListBox";
            this.checkedListBox.Size = new Size(336, 220);
            this.checkedListBox.TabIndex = 1;
            this.checkedListBox.CheckOnClick = true;
            this.checkedListBox.AccessibleName = "Stations Available Row Fields List Box";
            this.checkedListBox.AccessibleDescription = "List of possible fields for stations available rows. Check fields to show them and uncheck fields to hide them. Select an item and use Move Up or Move Down to reorder.";
            this.checkedListBox.SelectedIndexChanged += new EventHandler(this.CheckedListBox_SelectedIndexChanged);
            // 
            // moveUpButton
            // 
            this.moveUpButton.Location = new Point(12, 262);
            this.moveUpButton.Name = "moveUpButton";
            this.moveUpButton.Size = new Size(100, 28);
            this.moveUpButton.TabIndex = 2;
            this.moveUpButton.Text = "Move Up";
            this.moveUpButton.UseVisualStyleBackColor = true;
            this.moveUpButton.AccessibleName = "Move selected field up";
            this.moveUpButton.AccessibleDescription = "Moves the selected field one position earlier in the display order.";
            this.moveUpButton.Click += new EventHandler(this.MoveUpButton_Click);
            // 
            // moveDownButton
            // 
            this.moveDownButton.Location = new Point(118, 262);
            this.moveDownButton.Name = "moveDownButton";
            this.moveDownButton.Size = new Size(100, 28);
            this.moveDownButton.TabIndex = 3;
            this.moveDownButton.Text = "Move Down";
            this.moveDownButton.UseVisualStyleBackColor = true;
            this.moveDownButton.AccessibleName = "Move selected field down";
            this.moveDownButton.AccessibleDescription = "Moves the selected field one position later in the display order.";
            this.moveDownButton.Click += new EventHandler(this.MoveDownButton_Click);
            // 
            // restoreDefaultButton
            // 
            this.restoreDefaultButton.Location = new Point(224, 262);
            this.restoreDefaultButton.Name = "restoreDefaultButton";
            this.restoreDefaultButton.Size = new Size(124, 28);
            this.restoreDefaultButton.TabIndex = 4;
            this.restoreDefaultButton.Text = "Restore Default";
            this.restoreDefaultButton.UseVisualStyleBackColor = true;
            this.restoreDefaultButton.AccessibleName = "Restore default order";
            this.restoreDefaultButton.AccessibleDescription = "Resets the list to the default stations available row order and shows all fields.";
            this.restoreDefaultButton.Click += new EventHandler(this.RestoreDefaultButton_Click);
            // 
            // okButton
            // 
            this.okButton.Location = new Point(170, 300);
            this.okButton.Name = "okButton";
            this.okButton.Size = new Size(88, 28);
            this.okButton.TabIndex = 5;
            this.okButton.Text = "OK";
            this.okButton.UseVisualStyleBackColor = true;
            this.okButton.AccessibleName = "OK";
            this.okButton.AccessibleDescription = "Save the selected order and close the editor.";
            this.okButton.Click += new EventHandler(this.OkButton_Click);
            // 
            // cancelButton
            // 
            this.cancelButton.Location = new Point(264, 300);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new Size(88, 28);
            this.cancelButton.TabIndex = 6;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            this.cancelButton.AccessibleName = "Cancel";
            this.cancelButton.AccessibleDescription = "Close the editor without saving changes.";
            this.cancelButton.DialogResult = DialogResult.Cancel;
            // 
            // CallWaitingRowOrderDlg
            // 
            this.AcceptButton = this.okButton;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new Size(360, 341);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.okButton);
            this.Controls.Add(this.restoreDefaultButton);
            this.Controls.Add(this.moveDownButton);
            this.Controls.Add(this.moveUpButton);
            this.Controls.Add(this.checkedListBox);
            this.Controls.Add(this.instructionsLabel);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CallWaitingRowOrderDlg";
            this.Padding = new Padding(9);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Stations Available Row Fields";
            this.AccessibleName = "Stations Available Row Fields";
            this.ShowInTaskbar = false;
            this.ResumeLayout(false);
            this.PerformLayout();
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
                if (!_debug && string.Equals(field, "descr", StringComparison.OrdinalIgnoreCase)) continue;
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
