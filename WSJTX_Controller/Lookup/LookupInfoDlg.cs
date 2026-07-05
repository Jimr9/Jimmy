using System;
using System.Drawing;
using System.Windows.Forms;

namespace WSJTX_Controller
{
    // Accessible dialog showing all available lookup data for a callsign.
    // Built entirely in code; no Designer.cs file.
    public class LookupInfoDlg : Form
    {
        private readonly TextBox _statusValue;
        private readonly Button  _closeButton;
        private readonly Button  _qrzButton;

        // Rows: (label, read-only value textbox — must be focusable so JAWS/NVDA
        // users can reach each field with Tab; a plain Label can never take focus).
        private readonly TextBox _callValue, _nameValue, _gridValue, _stateValue,
                                 _countryValue, _continentValue, _countyValue, _cqzoneValue,
                                 _ituzoneValue, _adifValue, _qslManagerValue, _emailValue,
                                 _lotwValue, _activityValue, _sourcesValue;

        private readonly LookupManager _manager;
        private readonly string        _call;

        public bool QrzLookupOccurred { get; private set; }

        public LookupInfoDlg(string call, LookupManager manager)
        {
            _call    = call?.ToUpperInvariant() ?? "";
            _manager = manager;

            Text            = $"Station Lookup — {_call}";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.CenterParent;
            Size            = new Size(480, 486);
            Font            = new Font("Microsoft Sans Serif", 9F);
            KeyPreview      = true;
            KeyDown        += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            int lx = 16, vx = 160, y = 14, rh = 24, fw = 300, tabIndex = 0;

            _callValue      = AddRow("Callsign:",        ref y, lx, vx, fw, rh, ref tabIndex);
            _nameValue      = AddRow("Name:",            ref y, lx, vx, fw, rh, ref tabIndex);
            _gridValue      = AddRow("Grid:",            ref y, lx, vx, fw, rh, ref tabIndex);
            _stateValue     = AddRow("State/Province:",  ref y, lx, vx, fw, rh, ref tabIndex);
            _countryValue   = AddRow("Country:",         ref y, lx, vx, fw, rh, ref tabIndex);
            _continentValue = AddRow("Continent:",       ref y, lx, vx, fw, rh, ref tabIndex);
            _countyValue    = AddRow("County:",          ref y, lx, vx, fw, rh, ref tabIndex);
            _cqzoneValue    = AddRow("CQ Zone:",         ref y, lx, vx, fw, rh, ref tabIndex);
            _ituzoneValue   = AddRow("ITU Zone:",        ref y, lx, vx, fw, rh, ref tabIndex);
            _adifValue      = AddRow("ADIF Entity:",     ref y, lx, vx, fw, rh, ref tabIndex);
            _qslManagerValue= AddRow("QSL Manager:",     ref y, lx, vx, fw, rh, ref tabIndex);
            _emailValue     = AddRow("Email:",           ref y, lx, vx, fw, rh, ref tabIndex);
            _lotwValue      = AddRow("LoTW user:",       ref y, lx, vx, fw, rh, ref tabIndex);
            _activityValue  = AddRow("LoTW last upload:",ref y, lx, vx, fw, rh, ref tabIndex);

            y += 4;
            var sepLine = new Label { BorderStyle = BorderStyle.Fixed3D, Location = new Point(lx, y), Size = new Size(fw + vx - lx, 2), TabStop = false };
            Controls.Add(sepLine);
            y += 8;

            _sourcesValue = AddRow("Sources:", ref y, lx, vx, fw, rh, ref tabIndex);

            _statusValue = new TextBox
            {
                Location       = new Point(lx, y + 6),
                Size           = new Size(fw + vx - lx, 18),
                ReadOnly       = true,
                BorderStyle    = BorderStyle.None,
                BackColor      = SystemColors.Control,
                TabIndex       = tabIndex++,
                ForeColor      = Color.DimGray,
                AccessibleName = "Lookup status",
            };
            Controls.Add(_statusValue);

            y += 32;

            _qrzButton = new Button
            {
                Text           = "Lookup Online (QRZ)",
                Location       = new Point(lx, y),
                Size           = new Size(160, 26),
                TabIndex       = tabIndex++,
                AccessibleName = "Look up this callsign online via QRZ",
            };
            _qrzButton.Click += QrzButton_Click;
            Controls.Add(_qrzButton);

            _closeButton = new Button
            {
                Text           = "Close",
                Location       = new Point(fw + vx - 70, y),
                Size           = new Size(70, 26),
                TabIndex       = tabIndex++,
                DialogResult   = DialogResult.OK,
                AccessibleName = "Close lookup dialog",
            };
            Controls.Add(_closeButton);
            AcceptButton = _closeButton;

            PopulateFromCache();
        }

        private TextBox AddRow(string labelText, ref int y, int lx, int vx, int fw, int rh, ref int tabIndex)
        {
            var lbl = new Label
            {
                Text      = labelText,
                Location  = new Point(lx, y + 3),
                Size      = new Size(vx - lx - 4, rh - 4),
                TabStop   = false,
            };
            var val = new TextBox
            {
                Location       = new Point(vx, y + 1),
                Size           = new Size(fw, rh - 4),
                ReadOnly       = true,
                BorderStyle    = BorderStyle.None,
                BackColor      = SystemColors.Control,
                TabIndex       = tabIndex++,
                AccessibleName = labelText.TrimEnd(':'),
            };
            Controls.Add(lbl);
            Controls.Add(val);
            y += rh;
            return val;
        }

        private void PopulateFromCache()
        {
            if (_manager == null) { ShowNoData(); return; }

            var info = _manager.GetInfoForDialog(_call);
            if (info == null) { ShowNoData(); return; }

            _callValue.Text      = info.Callsign  ?? _call;
            _nameValue.Text      = info.Name       ?? "—";
            _gridValue.Text      = info.Grid       ?? "—";
            _stateValue.Text     = info.State      ?? "—";
            _countryValue.Text   = info.Country    ?? "—";
            _continentValue.Text = info.Continent  ?? "—";
            _countyValue.Text    = info.County     ?? "—";
            _cqzoneValue.Text    = info.CqZone > 0 ? info.CqZone.ToString() : "—";
            _ituzoneValue.Text   = info.ItuZone > 0 ? info.ItuZone.ToString() : "—";
            _adifValue.Text      = info.AdifEntity > 0 ? info.AdifEntity.ToString() : "—";
            _qslManagerValue.Text= info.QslManager ?? "—";
            _emailValue.Text     = info.Email      ?? "—";
            _lotwValue.Text      = info.IsLoTWUser ? "Yes" : ((_manager.LoTW.IsEnabled && _manager.LoTW.UserCount > 0) ? "No" : "—");
            _activityValue.Text  = info.LoTWActivity.HasValue
                                   ? info.LoTWActivity.Value.ToLocalTime().ToString("d")
                                   : "—";
            _sourcesValue.Text   = info.Sources ?? "—";

            bool canQrz = _manager.Qrz.IsEnabled &&
                          (_manager.Policy == QrzLookupPolicy.FocusedOnly ||
                           _manager.Policy == QrzLookupPolicy.UnidentifiedQueue);
            _qrzButton.Enabled = canQrz;
            if (!canQrz) _qrzButton.Text = "QRZ lookup disabled";
        }

        private void ShowNoData()
        {
            _callValue.Text    = _call;
            foreach (var lbl in new[] { _nameValue, _gridValue, _stateValue, _countryValue,
                                        _continentValue, _countyValue, _cqzoneValue, _ituzoneValue,
                                        _adifValue, _qslManagerValue, _emailValue,
                                        _lotwValue, _activityValue, _sourcesValue })
                lbl.Text = "—";
            _qrzButton.Enabled = false;
        }

        private async void QrzButton_Click(object sender, EventArgs e)
        {
            if (_manager == null) return;
            _qrzButton.Enabled  = false;
            _statusValue.Text   = "Looking up via QRZ…";
            var result = await _manager.LookupQrzAsync(_call);
            if (IsDisposed) return;
            if (result != null)
            {
                QrzLookupOccurred = true;
                _statusValue.Text = "QRZ lookup complete.";
                PopulateFromCache();
            }
            else
            {
                _statusValue.Text = $"QRZ: {_manager.Qrz.LastError ?? "No data returned."}";
                _qrzButton.Enabled = true;
            }

            // Move focus to the status field so JAWS/NVDA announce the async result —
            // the label never regains focus on its own, so silently updating .Text
            // would otherwise go unnoticed by screen-reader users.
            _statusValue.Focus();
        }
    }
}
