using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace WSJTX_Controller
{
    // WPF migration: minimal, WPF-bindable stand-ins for the WinForms control types Jimmy's
    // business logic (WsjtxClient.*.cs and friends) reads/writes directly via `ctrl.xyzCheckBox.
    // Checked` etc. Deliberately named/shaped to match the WinForms member names being replaced
    // (Checked, Text, Value, Visible, Enabled) so those call sites need zero changes -- only the
    // declared TYPE on Controller changes, from System.Windows.Forms.CheckBox to CheckState.
    // Real WPF controls in MainWindow.xaml/OptionsWindow.xaml TwoWay-bind to these.

    public class CheckState : INotifyPropertyChanged
    {
        private bool _checked;
        public bool Checked
        {
            get => _checked;
            set { if (_checked == value) return; _checked = value; OnChanged(); }
        }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; OnChanged(); }
        }

        private string _text = "";
        public string Text
        {
            get => _text;
            set { value ??= ""; if (_text == value) return; _text = value; OnChanged(); }
        }

        private Color _foreColor = Color.Black;
        public Color ForeColor
        {
            get => _foreColor;
            set { if (_foreColor == value) return; _foreColor = value; OnChanged(); }
        }

        public CheckState() { }
        public CheckState(bool initial) { _checked = initial; }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TextState : INotifyPropertyChanged
    {
        private string _text = "";
        public string Text
        {
            get => _text;
            set { value ??= ""; if (_text == value) return; _text = value; OnChanged(); }
        }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; OnChanged(); }
        }

        // Caret/selection -- WsjtxClient sets these after updating Text (e.g. to park the
        // caret at the start after a full-text replace). Real WPF binding target is a TextBox;
        // MainWindow applies these to the real caret when they change.
        public int SelectionStart { get; set; }
        public int SelectionLength { get; set; }
        public bool Focused { get; set; }
        public Color ForeColor { get; set; } = Color.Black;
        public Color BackColor { get; set; } = Color.White;

        public TextState() { }
        public TextState(string initial) { _text = initial ?? ""; }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Stands in for a WinForms Label used as plain read-only display text (e.g. periodLabel,
    // limitLabel, repeatLabel) -- Text + Visible, the only two members those call sites use.
    public class LabelState : INotifyPropertyChanged
    {
        private string _text = "";
        public string Text
        {
            get => _text;
            set { value ??= ""; if (_text == value) return; _text = value; OnChanged(); }
        }

        private bool _visible = true;
        public bool Visible
        {
            get => _visible;
            set { if (_visible == value) return; _visible = value; OnChanged(); }
        }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; OnChanged(); }
        }

        private Color _foreColor = Color.Black;
        public Color ForeColor
        {
            get => _foreColor;
            set { if (_foreColor == value) return; _foreColor = value; OnChanged(); }
        }

        private Color _backColor = Color.FromKnownColor(KnownColor.Control);
        public Color BackColor
        {
            get => _backColor;
            set { if (_backColor == value) return; _backColor = value; OnChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Stands in for a WinForms NumericUpDown (minSnrNumUpDown, timeoutNumUpDown).
    public class NumericState : INotifyPropertyChanged
    {
        private decimal _value;
        public decimal Value
        {
            get => _value;
            set
            {
                value = value < Minimum ? Minimum : value > Maximum ? Maximum : value;
                if (_value == value) return;
                _value = value; OnChanged();
            }
        }

        public decimal Minimum { get; set; } = 0;
        public decimal Maximum { get; set; } = 100;

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; OnChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Stands in for a WinForms ComboBox (bandComboBox, periodComboBox) where the business logic
    // only ever reads/sets .Text or .SelectedIndex, never manipulates .Items from WsjtxClient
    // itself (Items are populated by the view).
    public class ComboState : INotifyPropertyChanged
    {
        private string _text = "";
        public string Text
        {
            get => _text;
            set { value ??= ""; if (_text == value) return; _text = value; OnChanged(); }
        }

        private int _selectedIndex = -1;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set { if (_selectedIndex == value) return; _selectedIndex = value; OnChanged(); }
        }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; OnChanged(); }
        }

        private bool _visible = true;
        public bool Visible
        {
            get => _visible;
            set { if (_visible == value) return; _visible = value; OnChanged(); }
        }

        public List<string> Items { get; } = new List<string>();

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Stands in for a WinForms RadioButton (anyMsgRadioButton, cqGridRadioButton, etc.).
    public class RadioState : INotifyPropertyChanged
    {
        private bool _checked;
        public bool Checked
        {
            get => _checked;
            set { if (_checked == value) return; _checked = value; OnChanged(); }
        }

        private bool _visible = true;
        public bool Visible
        {
            get => _visible;
            set { if (_visible == value) return; _visible = value; OnChanged(); }
        }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; OnChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
