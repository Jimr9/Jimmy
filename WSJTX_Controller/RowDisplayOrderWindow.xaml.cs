using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace WSJTX_Controller
{
    // WPF port of RowDisplayOrderDlg.
    public partial class RowDisplayOrderWindow : Window
    {
        public static readonly string[] CallWaitingDefaultFields = RowDisplayOrderDefaults.CallWaiting.ToArray();
        public static readonly Dictionary<string, string> CallWaitingFieldLabels = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "callp", "Call Sign" }, { "pri", "Reply Status" }, { "tag", "Alert" }, { "grid", "Grid" },
            { "snr", "SNR" }, { "country", "Country" }, { "distAz", "Distance and Direction" },
            { "oe", "Age" }, { "descr", "Reason" }, { "rankStr", "Rank" }
        };

        public static readonly string[] RawDecodeDefaultFields = RowDisplayOrderDefaults.RawDecode.ToArray();
        public static readonly Dictionary<string, string> RawDecodeFieldLabels = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "callsign", "Call Sign" }, { "side", "TX1/TX2" }, { "tag", "Alert" },
            { "message", "Raw Message" }, { "snr", "SNR" }, { "grid", "Grid" },
            { "country", "Country/State" }, { "distAz", "Distance and Direction" }
        };

        public static readonly string[] SpotWatchDefaultFields = RowDisplayOrderDefaults.SpotWatch.ToArray();
        public static readonly Dictionary<string, string> SpotWatchFieldLabels = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "callsign", "Call Sign" }, { "age", "Last Spotted" }, { "band", "Band" }, { "frequency", "Frequency" },
            { "mode", "Mode" }, { "evenOdd", "Even/Odd" }, { "snr", "SNR" }, { "senderGrid", "Grid" },
            { "country", "Country" }, { "spottercall", "Spotted By" },
            { "spottercountry", "Spotter Country/State" }, { "spottergrid", "Spotter Grid" }
        };

        public List<string> SelectedCallWaitingFields { get; private set; }
        public List<string> SelectedRawDecodeFields { get; private set; }
        public List<string> SelectedSpotWatchFields { get; private set; }

        private readonly bool _debug;

        public RowDisplayOrderWindow(List<string> currentCallWaitingOrder, List<string> currentRawDecodeOrder,
            List<string> currentSpotWatchOrder, bool debug)
        {
            _debug = debug;
            InitializeComponent();

            CallWaitingList.RestoreDefaultsRequested += () => PopulateList(CallWaitingList, CallWaitingDefaultFields, CallWaitingFieldLabels, new List<string>(CallWaitingDefaultFields));
            RawDecodeList.RestoreDefaultsRequested += () => PopulateList(RawDecodeList, RawDecodeDefaultFields, RawDecodeFieldLabels, new List<string>(RawDecodeDefaultFields));
            SpotWatchList.RestoreDefaultsRequested += () => PopulateList(SpotWatchList, SpotWatchDefaultFields, SpotWatchFieldLabels, new List<string>(SpotWatchDefaultFields));

            PopulateList(CallWaitingList, CallWaitingDefaultFields, CallWaitingFieldLabels, currentCallWaitingOrder);
            PopulateList(RawDecodeList, RawDecodeDefaultFields, RawDecodeFieldLabels, currentRawDecodeOrder);
            PopulateList(SpotWatchList, SpotWatchDefaultFields, SpotWatchFieldLabels, currentSpotWatchOrder);
        }

        private void PopulateList(CheckableOrderedListBox list, string[] defaultFields,
            Dictionary<string, string> fieldLabels, List<string> currentOrder)
        {
            var selectedSet = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var orderedFields = new List<string>();

            if (currentOrder != null)
                foreach (var field in currentOrder)
                {
                    if (string.IsNullOrWhiteSpace(field)) continue;
                    if (!defaultFields.Contains(field, System.StringComparer.OrdinalIgnoreCase)) continue;
                    if (orderedFields.Exists(s => string.Equals(s, field, System.StringComparison.OrdinalIgnoreCase))) continue;
                    orderedFields.Add(field);
                    selectedSet.Add(field);
                }

            foreach (var field in defaultFields)
                if (!orderedFields.Exists(s => string.Equals(s, field, System.StringComparison.OrdinalIgnoreCase)))
                    orderedFields.Add(field);

            list.Items.Clear();
            foreach (var field in orderedFields)
            {
                if (!_debug && (string.Equals(field, "descr", System.StringComparison.OrdinalIgnoreCase)
                             || string.Equals(field, "oe", System.StringComparison.OrdinalIgnoreCase)
                             || string.Equals(field, "rankStr", System.StringComparison.OrdinalIgnoreCase))) continue;
                list.Items.Add(new CheckableItem { Tag = field, Label = fieldLabels.TryGetValue(field, out var l) ? l : field, Checked = selectedSet.Contains(field) });
            }
            list.SelectFirst();
        }

        private static List<string> CheckedFields(CheckableOrderedListBox list) =>
            list.Items.Where(i => i.Checked).Select(i => (string)i.Tag).ToList();

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var callWaitingFields = CheckedFields(CallWaitingList);
            var rawDecodeFields = CheckedFields(RawDecodeList);
            var spotWatchFields = CheckedFields(SpotWatchList);

            if (callWaitingFields.Count == 0 || rawDecodeFields.Count == 0 || spotWatchFields.Count == 0)
            {
                MessageBox.Show(this, "Select at least one field on each tab.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedCallWaitingFields = callWaitingFields;
            SelectedRawDecodeFields = rawDecodeFields;
            SelectedSpotWatchFields = spotWatchFields;
            DialogResult = true;
            Close();
        }
    }
}
