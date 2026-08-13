using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace WSJTX_Controller
{
    // Reusable checkable + reorderable list: a ListBox where Space toggles a check mark on the
    // selected row and Move Up/Down buttons reorder it. WPF port of the WinForms
    // CheckedListBox-plus-move-buttons pattern used by RankOrderDlg (Priorities & Filters /
    // Normal Sort Order tabs) and RowDisplayOrderDlg (all three tabs) -- built once here rather
    // than five times, since all five instances are functionally identical.
    //
    // Uses the proven WPF accessibility techniques from the earlier POC: plain-text items (no
    // interactive child CheckBox competing for keyboard focus), Space handled at the ListBox
    // level so it never falls through to WPF's default type-ahead search, and an explicit
    // AutomationPeer.RaisePropertyChangedEvent/RaiseAutomationEvent on toggle so JAWS/NVDA
    // announce the new checked state immediately.
    public class CheckableItem : INotifyPropertyChanged
    {
        public object Tag { get; set; }
        public string Label { get; set; }

        private bool _checked;
        public bool Checked
        {
            get => _checked;
            set { if (_checked == value) return; _checked = value; OnChanged(); OnChanged(nameof(DisplayText)); }
        }

        public string DisplayText => $"[{(Checked ? "X" : " ")}] {Label}";

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class CheckableOrderedListBox : UserControl
    {
        public ObservableCollection<CheckableItem> Items { get; } = new ObservableCollection<CheckableItem>();
        public event Action RestoreDefaultsRequested;

        public CheckableOrderedListBox()
        {
            InitializeComponent();
            ListBox.ItemsSource = Items;
        }

        public CheckableItem SelectedItem => ListBox.SelectedItem as CheckableItem;

        public void SelectFirst()
        {
            if (Items.Count > 0) ListBox.SelectedIndex = 0;
            UpdateMoveButtons();
        }

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateMoveButtons();

        private void UpdateMoveButtons()
        {
            int index = ListBox.SelectedIndex;
            int count = Items.Count;
            MoveUpButton.IsEnabled = index > 0;
            MoveDownButton.IsEnabled = index >= 0 && index < count - 1;
        }

        private void ListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space) return;
            if (!(ListBox.SelectedItem is CheckableItem item)) return;
            e.Handled = true;
            item.Checked = !item.Checked;

            var container = ListBox.ItemContainerGenerator.ContainerFromItem(item) as UIElement;
            if (container != null)
            {
                var peer = UIElementAutomationPeer.FromElement(container) ?? UIElementAutomationPeer.CreatePeerForElement(container);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                peer?.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, "", item.DisplayText);
            }
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e) => MoveSelected(-1, MoveUpButton, MoveDownButton);
        private void MoveDownButton_Click(object sender, RoutedEventArgs e) => MoveSelected(1, MoveDownButton, MoveUpButton);

        private void MoveSelected(int direction, Button justClicked, Button fallback)
        {
            int index = ListBox.SelectedIndex;
            if (index < 0) return;
            int target = index + direction;
            if (target < 0 || target >= Items.Count) return;

            var item = Items[index];
            Items.RemoveAt(index);
            Items.Insert(target, item);
            ListBox.SelectedIndex = target;
            UpdateMoveButtons();

            Dispatcher.BeginInvoke(new Action(() => (justClicked.IsEnabled ? justClicked : (Control)ListBox).Focus()));
        }

        private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e) => RestoreDefaultsRequested?.Invoke();
    }
}
