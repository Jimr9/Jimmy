using System;
using System.Windows;

namespace WSJTX_Controller
{
    // WPF port of EditQsoDlg.
    public partial class EditQsoWindow : Window
    {
        private static readonly string[] CommonModes =
        { "FT8", "FT4", "SSB", "CW", "RTTY", "PSK31", "FM", "AM", "WSPR", "JT65", "JT9", "MSK144", "FST4" };

        private readonly Func<string, LookupRecord> _lookupCallsign;
        private readonly Func<QsoRecord, string> _onSubmit;
        private readonly Action _onLogged;
        private readonly bool _isNewEntry;

        public EditQsoWindow(QsoRecord q, string title = "Edit QSO",
            Func<string, LookupRecord> lookupCallsign = null, bool isNewEntry = false,
            Func<QsoRecord, string> onSubmit = null, Action onLogged = null)
        {
            _lookupCallsign = lookupCallsign;
            _isNewEntry = isNewEntry;
            _onSubmit = onSubmit;
            _onLogged = onLogged;
            InitializeComponent();
            Title = title;

            foreach (var m in CommonModes) ModeCombo.Items.Add(m);

            CallBox.Text = q.Callsign ?? "";
            BandBox.Text = q.Band ?? "";
            ModeCombo.Text = q.Mode ?? "";
            DateBox.Text = q.QsoDate ?? "";
            TimeOnBox.Text = q.TimeOn ?? "";
            TimeOffBox.Text = q.TimeOff ?? "";
            StateBox.Text = q.State ?? "";
            CountryBox.Text = q.Country ?? "";
            GridBox.Text = q.Grid ?? "";
            NameBox.Text = q.Name ?? "";
            RstSentBox.Text = q.RstSent ?? "";
            RstRcvdBox.Text = q.RstRcvd ?? "";
            CommentBox.Text = q.Comment ?? "";

            CallBox.LostFocus += CallBox_LostFocus;
        }

        private void CallBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_lookupCallsign == null) return;
            string call = CallBox.Text.Trim();
            if (call.Length == 0) return;

            var rec = _lookupCallsign(call);
            if (rec == null) return;

            if (StateBox.Text.Trim().Length == 0 && !string.IsNullOrEmpty(rec.State)) StateBox.Text = rec.State;
            if (CountryBox.Text.Trim().Length == 0 && !string.IsNullOrEmpty(rec.Country)) CountryBox.Text = rec.Country;
            if (GridBox.Text.Trim().Length == 0 && !string.IsNullOrEmpty(rec.Grid)) GridBox.Text = rec.Grid;
            if (NameBox.Text.Trim().Length == 0 && !string.IsNullOrEmpty(rec.Name)) NameBox.Text = rec.Name;
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            string callsign = CallBox.Text.Trim();
            if (callsign.Length == 0)
            {
                StatusBox.Text = "Callsign cannot be blank.";
                CallBox.Focus();
                return;
            }

            string timeOff = TimeOffBox.Text.Trim();
            if (_isNewEntry && timeOff.Length == 0) timeOff = DateTime.UtcNow.ToString("HHmm");

            var record = new QsoRecord
            {
                Callsign = callsign,
                Band = BandBox.Text.Trim(),
                Mode = ModeCombo.Text.Trim().ToUpperInvariant(),
                QsoDate = DateBox.Text.Trim(),
                TimeOn = TimeOnBox.Text.Trim(),
                TimeOff = timeOff,
                State = StateBox.Text.Trim(),
                Country = CountryBox.Text.Trim(),
                Grid = GridBox.Text.Trim(),
                Name = NameBox.Text.Trim(),
                RstSent = RstSentBox.Text.Trim(),
                RstRcvd = RstRcvdBox.Text.Trim(),
                Comment = CommentBox.Text.Trim(),
            };

            string error = _onSubmit?.Invoke(record);
            if (error != null)
            {
                StatusBox.Text = error;
                return;
            }

            StatusBox.Text = (_isNewEntry ? "Added " : "Saved ") + callsign + ".";
            _onLogged?.Invoke();

            if (_isNewEntry)
            {
                ResetForNextEntry();
                CallBox.Focus();
            }
            else
            {
                Close();
            }
        }

        private void ResetForNextEntry()
        {
            CallBox.Text = "";
            DateBox.Text = DateTime.UtcNow.ToString("yyyyMMdd");
            TimeOnBox.Text = DateTime.UtcNow.ToString("HHmm");
            TimeOffBox.Text = "";
            StateBox.Text = "";
            CountryBox.Text = "";
            GridBox.Text = "";
            NameBox.Text = "";
            RstSentBox.Text = "";
            RstRcvdBox.Text = "";
            CommentBox.Text = "";
        }
    }
}
