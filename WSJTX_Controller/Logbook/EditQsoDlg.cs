using System;
using System.Drawing;
using System.Windows.Forms;

namespace WSJTX_Controller
{
    // Local-only edit dialog for a single QSO row on the Logbook window's Edit Log tab.
    // Never contacts QRZ/Club Log/LoTW -- only ever writes to Jimmy's own logbook.db
    // (see LogbookDb.UpdateQso). Field set is deliberately smaller than every column
    // the database stores: this exposes the fields a user would realistically want to
    // review or correct (callsign, band/mode/date/time, state/country/grid, name,
    // RST, a free-form comment), not the award-engine bookkeeping fields (SIG, DARC_DOK,
    // WPX prefix, etc.), which are left untouched by an edit.
    internal class EditQsoDlg : Form
    {
        private readonly TextBox _callTb, _bandTb, _modeTb, _dateTb, _timeOnTb, _timeOffTb;
        private readonly TextBox _stateTb, _countryTb, _gridTb, _nameTb, _rstSentTb, _rstRcvdTb;
        private readonly TextBox _commentTb;
        private readonly Button  _okButton, _cancelButton;

        public QsoRecord Result { get; private set; }

        // Also used for "Add New QSO" (LogbookWindow's AddQsoBtn_Click) with a mostly-blank
        // QsoRecord and title -- same fields either way, so one dialog covers both.
        public EditQsoDlg(QsoRecord q, string title = "Edit QSO")
        {
            Text            = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(460, 300);
            KeyPreview      = true;

            int yA = 12, yB = 12, tab = 1;
            _callTb    = AddField(this, "Callsign:",    12, ref yA, 90, 110, out _, ref tab, q.Callsign, upper: true);
            _bandTb    = AddField(this, "Band:",        12, ref yA, 90, 110, out _, ref tab, q.Band);
            _modeTb    = AddField(this, "Mode:",        12, ref yA, 90, 110, out _, ref tab, q.Mode, upper: true);
            _dateTb    = AddField(this, "Date (YYYYMMDD):", 12, ref yA, 90, 110, out _, ref tab, q.QsoDate);
            _timeOnTb  = AddField(this, "Time on (HHMM):",  12, ref yA, 90, 110, out _, ref tab, q.TimeOn);
            _timeOffTb = AddField(this, "Time off (HHMM):", 12, ref yA, 90, 110, out _, ref tab, q.TimeOff);

            _stateTb   = AddField(this, "State:",       242, ref yB, 70, 130, out _, ref tab, q.State, upper: true);
            _countryTb = AddField(this, "Country:",     242, ref yB, 70, 130, out _, ref tab, q.Country);
            _gridTb    = AddField(this, "Grid:",        242, ref yB, 70, 130, out _, ref tab, q.Grid, upper: true);
            _nameTb    = AddField(this, "Name:",        242, ref yB, 70, 130, out _, ref tab, q.Name);
            _rstSentTb = AddField(this, "RST sent:",    242, ref yB, 70, 130, out _, ref tab, q.RstSent);
            _rstRcvdTb = AddField(this, "RST rcvd:",    242, ref yB, 70, 130, out _, ref tab, q.RstRcvd);

            int yC = Math.Max(yA, yB) + 6;
            var commentLbl = new Label { Text = "Comment:", Location = new Point(12, yC + 2), AutoSize = true };
            Controls.Add(commentLbl);
            _commentTb = new TextBox
            {
                Location       = new Point(90, yC),
                Size           = new Size(358, 20),
                TabIndex       = tab++,
                AccessibleName = "Comment",
                Text           = q.Comment ?? "",
            };
            Controls.Add(_commentTb);
            yC += 30;

            _okButton = new Button
            {
                Text     = "OK",
                Location = new Point(282, yC),
                Size     = new Size(80, 26),
                TabIndex = tab++,
            };
            _okButton.Click += OkButton_Click;

            _cancelButton = new Button
            {
                Text         = "Cancel",
                Location     = new Point(368, yC),
                Size         = new Size(80, 26),
                TabIndex     = tab++,
                DialogResult = DialogResult.Cancel,
            };

            Controls.Add(_okButton);
            Controls.Add(_cancelButton);
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            ClientSize = new Size(460, yC + 40);
        }

        private static TextBox AddField(Form form, string label, int x, ref int y, int labelWidth, int boxWidth,
            out Label lbl, ref int tab, string value, bool upper = false)
        {
            lbl = new Label
            {
                Text     = label,
                Location = new Point(x, y + 2),
                Size     = new Size(labelWidth, 16),
                AutoSize = false,
            };
            form.Controls.Add(lbl);

            var tb = new TextBox
            {
                Location       = new Point(x + labelWidth, y),
                Size           = new Size(boxWidth, 20),
                TabIndex       = tab++,
                AccessibleName = label.TrimEnd(':', ' '),
                Text           = value ?? "",
                CharacterCasing = upper ? CharacterCasing.Upper : CharacterCasing.Normal,
            };
            form.Controls.Add(tb);
            y += 26;
            return tb;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            string callsign = _callTb.Text.Trim();
            if (callsign.Length == 0)
            {
                MessageBox.Show(this, "Callsign cannot be blank.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _callTb.Focus();
                return;
            }

            Result = new QsoRecord
            {
                Callsign = callsign,
                Band     = _bandTb.Text.Trim(),
                Mode     = _modeTb.Text.Trim(),
                QsoDate  = _dateTb.Text.Trim(),
                TimeOn   = _timeOnTb.Text.Trim(),
                TimeOff  = _timeOffTb.Text.Trim(),
                State    = _stateTb.Text.Trim(),
                Country  = _countryTb.Text.Trim(),
                Grid     = _gridTb.Text.Trim(),
                Name     = _nameTb.Text.Trim(),
                RstSent  = _rstSentTb.Text.Trim(),
                RstRcvd  = _rstRcvdTb.Text.Trim(),
                Comment  = _commentTb.Text.Trim(),
            };
            DialogResult = DialogResult.OK;
        }
    }
}
