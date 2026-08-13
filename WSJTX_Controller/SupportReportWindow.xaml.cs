using System.Windows;
using System.Windows.Controls;

namespace WSJTX_Controller
{
    // WPF port of SupportReportDlg.
    public partial class SupportReportWindow : Window
    {
        public string Callsign => CallsignBox.Text.Trim();
        public string PersonName => NameBox.Text.Trim();
        public string Email => EmailBox.Text.Trim();
        public string ProblemType => (ProblemTypeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "";
        public string Description => DescBox.Text.Trim();
        public string Steps => StepsBox.Text.Trim();

        public SupportReportWindow(string prefillCallsign)
        {
            InitializeComponent();
            CallsignBox.Text = prefillCallsign ?? "";
            ProblemTypeCombo.SelectedIndex = 0;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DescBox.Text))
            {
                MessageBox.Show("Please enter a problem description before continuing.", "Create Support Report",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DescBox.Focus();
                return;
            }
            DialogResult = true;
            Close();
        }
    }
}
