using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace WSJTX_Controller
{
    // WPF port of EditLogRowOrderDlg.
    public partial class EditLogRowOrderWindow : Window
    {
        public static readonly string[] DefaultFields =
            { "date", "time", "callsign", "band", "mode", "state", "country", "confirmed", "source" };

        public static readonly Dictionary<string, string> FieldLabels = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "date", "Date" }, { "time", "UTC Time" }, { "callsign", "Callsign" }, { "band", "Band" },
            { "mode", "Mode" }, { "state", "State" }, { "country", "Country" },
            { "confirmed", "Confirmed" }, { "source", "Source" },
        };

        public List<string> SelectedFields { get; private set; }

        public EditLogRowOrderWindow(List<string> currentOrder)
        {
            InitializeComponent();
            FieldList.RestoreDefaultsRequested += () => Populate(new List<string>(DefaultFields));
            Populate(currentOrder);
        }

        private void Populate(List<string> currentOrder)
        {
            var selectedSet = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();
            if (currentOrder != null)
                foreach (var f in currentOrder)
                {
                    if (string.IsNullOrWhiteSpace(f) || !DefaultFields.Contains(f, System.StringComparer.OrdinalIgnoreCase)) continue;
                    if (ordered.Contains(f, System.StringComparer.OrdinalIgnoreCase)) continue;
                    ordered.Add(f);
                    selectedSet.Add(f);
                }
            foreach (var f in DefaultFields)
                if (!ordered.Contains(f, System.StringComparer.OrdinalIgnoreCase)) ordered.Add(f);

            FieldList.Items.Clear();
            foreach (var f in ordered)
                FieldList.Items.Add(new CheckableItem { Tag = f, Label = FieldLabels.TryGetValue(f, out var l) ? l : f, Checked = selectedSet.Contains(f) });
            FieldList.SelectFirst();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = FieldList.Items.Where(i => i.Checked).Select(i => (string)i.Tag).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Select at least one column.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedFields = selected;
            DialogResult = true;
            Close();
        }
    }
}
