using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WSJTX_Controller
{
    // WPF port of RankOrderDlg.
    public partial class SortOrderWindow : Window
    {
        public List<WsjtxClient.RankMethods> SelectedOrder { get; private set; }
        public WsjtxClient.RankMethods? SelectedBeam { get; private set; }
        public Dictionary<WsjtxClient.CallCategory, int> SelectedCategoryWeights { get; private set; }
        public List<WsjtxClient.CallCategory> SelectedCallingPriorities { get; private set; }

        private static readonly (WsjtxClient.RankMethods Method, string Label)[] DefaultSortEntries =
        {
            (WsjtxClient.RankMethods.CALL_ORDER, "Order received, oldest first"),
            (WsjtxClient.RankMethods.MOST_RECENT, "Most recent first"),
            (WsjtxClient.RankMethods.DIST_INCR, "Nearest callers first"),
            (WsjtxClient.RankMethods.DIST_DECR, "Farthest callers first"),
            (WsjtxClient.RankMethods.SNR_INCR, "Weakest signal first"),
            (WsjtxClient.RankMethods.SNR_DECR, "Strongest signal first"),
        };

        private static readonly (WsjtxClient.RankMethods? Method, string Label)[] BeamEntries =
        {
            (null, "None"),
            (WsjtxClient.RankMethods.AZ_NQUAD, "N"), (WsjtxClient.RankMethods.AZ_NEQUAD, "NE"),
            (WsjtxClient.RankMethods.AZ_EQUAD, "E"), (WsjtxClient.RankMethods.AZ_SEQUAD, "SE"),
            (WsjtxClient.RankMethods.AZ_SQUAD, "S"), (WsjtxClient.RankMethods.AZ_SWQUAD, "SW"),
            (WsjtxClient.RankMethods.AZ_WQUAD, "W"), (WsjtxClient.RankMethods.AZ_NWQUAD, "NW"),
        };

        private static readonly Dictionary<WsjtxClient.CallCategory, string> CategoryLabels = new Dictionary<WsjtxClient.CallCategory, string>
        {
            { WsjtxClient.CallCategory.NEW_COUNTRY, "New DXCC" },
            { WsjtxClient.CallCategory.NEW_COUNTRY_ON_BAND, "New DXCC on band" },
            { WsjtxClient.CallCategory.ALWAYS_WANTED, "Always Wanted Calls" },
            { WsjtxClient.CallCategory.TO_MYCALL, "Calling me" },
            { WsjtxClient.CallCategory.MANUAL_SEL, "Manual selection" },
            { WsjtxClient.CallCategory.WANTED_CQ, "Directed CQ" },
            { WsjtxClient.CallCategory.DEFAULT, "Ordinary CQ" },
            { WsjtxClient.CallCategory.WAS_NEEDED, "WAS Needed" },
            { WsjtxClient.CallCategory.WAS_UNCONFIRMED, "WAS Worked, Unconfirmed" },
            { WsjtxClient.CallCategory.DXCC_UNCONFIRMED, "DXCC Worked, Unconfirmed" },
            { WsjtxClient.CallCategory.ZONE_NEEDED, "Zones Needed" },
            { WsjtxClient.CallCategory.STILL_NEEDED, "Still Need (selected award)" },
        };

        private static readonly HashSet<WsjtxClient.CallCategory> HiddenCategories = new HashSet<WsjtxClient.CallCategory>
        { WsjtxClient.CallCategory.POTA, WsjtxClient.CallCategory.SOTA, WsjtxClient.CallCategory.MANUAL_SEL };

        private static readonly WsjtxClient.CallCategory[] AllFilterCategories =
        {
            WsjtxClient.CallCategory.NEW_COUNTRY, WsjtxClient.CallCategory.NEW_COUNTRY_ON_BAND,
            WsjtxClient.CallCategory.ALWAYS_WANTED, WsjtxClient.CallCategory.TO_MYCALL,
            WsjtxClient.CallCategory.WANTED_CQ, WsjtxClient.CallCategory.WAS_NEEDED,
            WsjtxClient.CallCategory.WAS_UNCONFIRMED, WsjtxClient.CallCategory.DXCC_UNCONFIRMED,
            WsjtxClient.CallCategory.ZONE_NEEDED, WsjtxClient.CallCategory.STILL_NEEDED,
            WsjtxClient.CallCategory.DEFAULT,
        };

        private static readonly List<WsjtxClient.CallCategory> DefaultCallingPriorities = new List<WsjtxClient.CallCategory>
        {
            WsjtxClient.CallCategory.NEW_COUNTRY, WsjtxClient.CallCategory.NEW_COUNTRY_ON_BAND,
            WsjtxClient.CallCategory.ALWAYS_WANTED, WsjtxClient.CallCategory.TO_MYCALL,
            WsjtxClient.CallCategory.WANTED_CQ, WsjtxClient.CallCategory.WAS_NEEDED,
            WsjtxClient.CallCategory.WAS_UNCONFIRMED, WsjtxClient.CallCategory.DXCC_UNCONFIRMED,
            WsjtxClient.CallCategory.ZONE_NEEDED, WsjtxClient.CallCategory.STILL_NEEDED,
        };

        public SortOrderWindow(List<WsjtxClient.RankMethods> currentOrder, WsjtxClient.RankMethods? currentBeam,
            List<WsjtxClient.CallCategory> currentCallingPriorities)
        {
            InitializeComponent();
            CallingList.RestoreDefaultsRequested += () => PopulateCallingList(DefaultCallingPriorities);
            SortList.RestoreDefaultsRequested += () => { PopulateSortList(new List<WsjtxClient.RankMethods> { WsjtxClient.RankMethods.MOST_RECENT }); BeamCombo.SelectedIndex = 0; };

            PopulateCallingList(currentCallingPriorities);
            PopulateSortList(currentOrder);
            PopulateBeamCombo(currentBeam);
        }

        private void PopulateCallingList(List<WsjtxClient.CallCategory> currentCallingPriorities)
        {
            var calling = (currentCallingPriorities != null && currentCallingPriorities.Count > 0)
                ? currentCallingPriorities : DefaultCallingPriorities;

            CallingList.Items.Clear();
            foreach (var cat in calling)
            {
                if (HiddenCategories.Contains(cat)) continue;
                CallingList.Items.Add(new CheckableItem { Tag = cat, Label = CategoryLabels.TryGetValue(cat, out var l) ? l : cat.ToString(), Checked = true });
            }
            foreach (var cat in AllFilterCategories)
            {
                if (HiddenCategories.Contains(cat) || calling.Contains(cat)) continue;
                CallingList.Items.Add(new CheckableItem { Tag = cat, Label = CategoryLabels.TryGetValue(cat, out var l) ? l : cat.ToString(), Checked = false });
            }
            CallingList.SelectFirst();
        }

        private void PopulateSortList(List<WsjtxClient.RankMethods> currentOrder)
        {
            var activeSet = new HashSet<WsjtxClient.RankMethods>(currentOrder ?? Enumerable.Empty<WsjtxClient.RankMethods>());
            var ordered = new List<(WsjtxClient.RankMethods Method, string Label)>();
            if (currentOrder != null)
                foreach (var m in currentOrder)
                {
                    var e = Array.Find(DefaultSortEntries, x => x.Method == m);
                    if (e.Label != null) ordered.Add(e);
                }
            foreach (var e in DefaultSortEntries)
                if (!ordered.Exists(x => x.Method == e.Method)) ordered.Add(e);

            SortList.Items.Clear();
            foreach (var e in ordered)
                SortList.Items.Add(new CheckableItem { Tag = e.Method, Label = e.Label, Checked = activeSet.Contains(e.Method) });
            SortList.SelectFirst();
        }

        private void PopulateBeamCombo(WsjtxClient.RankMethods? currentBeam)
        {
            BeamCombo.Items.Clear();
            int selectedIdx = 0;
            for (int i = 0; i < BeamEntries.Length; i++)
            {
                BeamCombo.Items.Add(new ComboBoxItem { Content = BeamEntries[i].Label });
                if (BeamEntries[i].Method == currentBeam) selectedIdx = i;
            }
            BeamCombo.SelectedIndex = selectedIdx;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = SortList.Items.Where(i => i.Checked).Select(i => (WsjtxClient.RankMethods)i.Tag).ToList();
            if (selected.Count == 0)
            {
                Tabs.SelectedIndex = 1;
                MessageBox.Show(this, "At least one sort option must be checked.", "Stations Available Sort Order",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedOrder = selected;
            SelectedBeam = BeamEntries[BeamCombo.SelectedIndex].Method;

            int checkedCount = CallingList.Items.Count(i => i.Checked);
            var weights = new Dictionary<WsjtxClient.CallCategory, int>();
            var callingList = new List<WsjtxClient.CallCategory>();
            int rank = checkedCount;
            foreach (var item in CallingList.Items)
            {
                var cat = (WsjtxClient.CallCategory)item.Tag;
                if (item.Checked) { weights[cat] = rank--; callingList.Add(cat); }
                else weights[cat] = 0;
            }
            weights[WsjtxClient.CallCategory.DEFAULT] = 0;
            SelectedCategoryWeights = weights;
            SelectedCallingPriorities = callingList;

            DialogResult = true;
            Close();
        }
    }
}
