using System;
using System.Drawing;
using System.Windows.Forms;
using WsjtxUdpLib.Messages.Out;

namespace WSJTX_Controller
{
    internal class ManualCallDlg : Form
    {
        private TextBox _callTextBox;

        public string Callsign { get; private set; }

        public ManualCallDlg()
        {
            Text = "Call Callsign";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(270, 92);

            var lbl = new Label
            {
                Text = "Callsign:",
                Location = new Point(12, 16),
                AutoSize = true,
            };

            _callTextBox = new TextBox
            {
                Location = new Point(82, 13),
                Size = new Size(168, 20),
                AccessibleName = "Callsign",
                CharacterCasing = CharacterCasing.Upper,
            };

            var okButton = new Button
            {
                Text = "OK",
                Location = new Point(82, 54),
                Size = new Size(80, 26),
            };
            okButton.Click += OkButton_Click;

            var cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(170, 54),
                Size = new Size(80, 26),
                DialogResult = DialogResult.Cancel,
            };

            Controls.AddRange(new Control[] { lbl, _callTextBox, okButton, cancelButton });
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            string callsign = _callTextBox.Text.Trim().ToUpper();

            if (callsign.Length == 0)
            {
                MessageBox.Show("Please enter a callsign.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _callTextBox.Focus();
                return;
            }

            if (WsjtxMessage.IsInvalidCall(callsign))
            {
                MessageBox.Show($"'{callsign}' does not appear to be a valid callsign.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _callTextBox.SelectAll();
                _callTextBox.Focus();
                return;
            }

            Callsign = callsign;
            DialogResult = DialogResult.OK;
        }
    }
}
