using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace WSJTX_Controller
{
    // WPF port of ExportSourceFilterDlg.
    public partial class ExportSourceFilterWindow : Window
    {
        public System.Collections.Generic.List<string> SelectedSources { get; private set; }
        private readonly ObservableCollection<CheckableItem> _items = new ObservableCollection<CheckableItem>();

        public ExportSourceFilterWindow()
        {
            InitializeComponent();
            SourceList.ItemsSource = _items;
            foreach (var source in QsoRecord.KnownSources)
                _items.Add(new CheckableItem { Tag = source, Label = source, Checked = true });
            if (_items.Count > 0) SourceList.SelectedIndex = 0;
        }

        private void SourceList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space) return;
            if (!(SourceList.SelectedItem is CheckableItem item)) return;
            e.Handled = true;
            item.Checked = !item.Checked;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var sources = _items.Where(i => i.Checked).Select(i => (string)i.Tag).ToList();
            if (sources.Count == 0)
            {
                MessageBox.Show(this, "Select at least one logging service.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedSources = sources;
            DialogResult = true;
            Close();
        }
    }
}
