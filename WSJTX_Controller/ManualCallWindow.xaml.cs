using System;
using System.Windows;
using WsjtxUdpLib.Messages.Out;

namespace WSJTX_Controller
{
    // WPF port of ManualCallDlg.
    public partial class ManualCallWindow : Window
    {
        private readonly LookupManager _lookupManager;
        public string Callsign { get; private set; }

        public ManualCallWindow(string lastCallsign, LookupManager lookupManager)
        {
            _lookupManager = lookupManager;
            InitializeComponent();
            CallTextBox.Text = lastCallsign ?? "";
            Loaded += (s, e) => { CallTextBox.SelectAll(); CallTextBox.Focus(); };
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string callsign = CallTextBox.Text.Trim().ToUpperInvariant();

            if (callsign.Length == 0)
            {
                MessageBox.Show("Please enter a callsign.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                CallTextBox.Focus();
                return;
            }

            if (WsjtxMessage.IsInvalidCall(callsign))
            {
                MessageBox.Show($"'{callsign}' does not appear to be a valid callsign.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                CallTextBox.SelectAll();
                CallTextBox.Focus();
                return;
            }

            if (_lookupManager != null && _lookupManager.Enabled)
            {
                OkButton.IsEnabled = false;
                CancelButton.IsEnabled = false;
                LookupRecord rec = null;
                try { rec = await _lookupManager.Qrz.LookupAsync(callsign); }
                catch { /* best-effort -- treat like "not found" below */ }
                OkButton.IsEnabled = true;
                CancelButton.IsEnabled = true;

                string gridText = !string.IsNullOrEmpty(rec?.Grid) ? rec.Grid : "not found";
                var result = MessageBox.Show(this,
                    $"Grid square: {gridText}{Environment.NewLine}{Environment.NewLine}Call {callsign}?",
                    "Confirm Call", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    CallTextBox.SelectAll();
                    CallTextBox.Focus();
                    return;
                }
            }

            Callsign = callsign;
            DialogResult = true;
            Close();
        }
    }
}
