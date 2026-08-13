using System;
using System.Windows;

namespace WSJTX_Controller
{
    // WPF port of HelpDlg.
    public partial class HelpWindow : Window
    {
        private readonly Controller ctrl;

        public HelpWindow(Controller controller, string titleText, string bodyText)
        {
            ctrl = controller;
            InitializeComponent();
            Title = titleText;
            HelpTextBox.Text = bodyText;
            Loaded += (s, e) => { HelpTextBox.Focus(); HelpTextBox.Select(0, 0); };
            Closing += (s, e) => ctrl.HelpClosed();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void SupportReportButton_Click(object sender, RoutedEventArgs e)
        {
            string prefill = null;
            try { prefill = ctrl.wsjtxClient?.myCall; } catch { }

            var dlg = new SupportReportWindow(prefill) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            string confirmText =
                "Jimmy will create a support report ZIP in your Downloads folder.\n\n" +
                "The ZIP may contain diagnostic information including Jimmy version, settings, " +
                "recent log files, recent decode history, Windows information, WSJT-X connection " +
                "information, and your written description.\n\n" +
                "Nothing will be emailed automatically.\n\n" +
                "You may review the ZIP before sending it.\n\n" +
                "Create the report?";

            if (MessageBox.Show(confirmText, "Create Support Report",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            var result = SupportReportBuilder.Build(
                ctrl,
                dlg.Callsign, dlg.PersonName, dlg.Email,
                dlg.ProblemType, dlg.Description, dlg.Steps);

            if (result.Success)
            {
                MessageBox.Show(
                    $"Support report saved:\n{result.ZipPath}\n\nYou may review the ZIP before sending it to support.",
                    "Support Report Created", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Could not create support report:\n{result.Error}", "Support Report Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
