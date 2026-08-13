using System.Windows;

namespace WSJTX_Controller
{
    public partial class ConfirmDlg : Window
    {
        public string Text
        {
            get => MessageText.Text;
            set => MessageText.Text = value;
        }

        public bool Confirmed { get; private set; }

        public ConfirmDlg()
        {
            InitializeComponent();
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
