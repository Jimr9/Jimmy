using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace WSJTX_Controller
{
    // WPF port of LookupInfoDlg -- shows all available lookup data for a callsign.
    public partial class LookupInfoWindow : Window
    {
        private readonly LookupManager _manager;
        private readonly string _call;
        private bool _canQrz;

        public bool QrzLookupOccurred { get; private set; }

        private TextBox _callValue, _nameValue, _licClassValue, _gridValue, _stateValue,
            _countryValue, _continentValue, _countyValue, _cqzoneValue, _ituzoneValue,
            _adifValue, _qslManagerValue, _emailValue, _lotwValue, _activityValue, _sourcesValue;

        public LookupInfoWindow(string call, LookupManager manager)
        {
            _call = call?.ToUpperInvariant() ?? "";
            _manager = manager;
            InitializeComponent();
            Title = $"Station Lookup -- {_call}";
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };

            _callValue = AddRow("Callsign:");
            _nameValue = AddRow("Name:");
            _licClassValue = AddRow("License Class:");
            _gridValue = AddRow("Grid:");
            _stateValue = AddRow("State/Province:");
            _countryValue = AddRow("Country:");
            _continentValue = AddRow("Continent:");
            _countyValue = AddRow("County:");
            _cqzoneValue = AddRow("CQ Zone:");
            _ituzoneValue = AddRow("ITU Zone:");
            _adifValue = AddRow("ADIF Entity:");
            _qslManagerValue = AddRow("QSL Manager:");
            _emailValue = AddRow("Email:");
            _lotwValue = AddRow("LoTW user:");
            _activityValue = AddRow("LoTW last upload:");
            RootPanel.Children.Insert(RootPanel.Children.IndexOf(StatusValue), new Separator { Margin = new Thickness(0, 6, 0, 6) });
            _sourcesValue = AddRow("Sources:");

            PopulateFromCache();

            Loaded += async (s, e) =>
            {
                if (!_canQrz) StatusValue.Text = "QRZ lookup disabled.";
                else if (_manager != null && _manager.QrzNeedsLookup(_call)) await DoQrzLookupAsync();
                else
                {
                    var cachedAt = _manager?.QrzCachedAt(_call);
                    StatusValue.Text = cachedAt.HasValue ? $"QRZ data from {FormatAge(cachedAt.Value)}." : "Using cached data.";
                }
            };
        }

        private TextBox AddRow(string labelText)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock { Text = labelText, VerticalAlignment = VerticalAlignment.Center };
            var val = new TextBox { IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent };
            AutomationProperties.SetName(val, labelText.TrimEnd(':'));
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(val, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(val);
            int insertAt = RootPanel.Children.IndexOf(StatusValue);
            RootPanel.Children.Insert(insertAt, grid);
            return val;
        }

        private void PopulateFromCache()
        {
            if (_manager == null) { ShowNoData(); return; }
            var info = _manager.GetInfoForDialog(_call);
            if (info == null) { ShowNoData(); return; }

            _callValue.Text = info.Callsign ?? _call;
            _nameValue.Text = info.Name ?? "--";
            _licClassValue.Text = FormatLicenseClass(info.LicenseClass);
            _gridValue.Text = info.Grid ?? "--";
            _stateValue.Text = info.State ?? "--";
            _countryValue.Text = info.Country ?? "--";
            _continentValue.Text = info.Continent ?? "--";
            _countyValue.Text = info.County ?? "--";
            _cqzoneValue.Text = info.CqZone > 0 ? info.CqZone.ToString() : "--";
            _ituzoneValue.Text = info.ItuZone > 0 ? info.ItuZone.ToString() : "--";
            _adifValue.Text = info.Dxcc > 0 ? info.Dxcc.ToString() : "--";
            _qslManagerValue.Text = info.QslManager ?? "--";
            _emailValue.Text = info.Email ?? "--";
            _lotwValue.Text = info.IsLoTWUser ? "Yes" : ((_manager.LoTW.IsEnabled && _manager.LoTW.UserCount > 0) ? "No" : "--");
            _activityValue.Text = info.LoTWLastActivity.HasValue ? info.LoTWLastActivity.Value.ToLocalTime().ToString("d") : "--";
            _sourcesValue.Text = info.SourcesText;

            _canQrz = _manager.Qrz.IsEnabled &&
                      (_manager.Policy == QrzLookupPolicy.FocusedOnly || _manager.Policy == QrzLookupPolicy.UnidentifiedQueue);
        }

        private void ShowNoData()
        {
            _callValue.Text = _call;
            foreach (var tb in new[] { _nameValue, _licClassValue, _gridValue, _stateValue, _countryValue,
                _continentValue, _countyValue, _cqzoneValue, _ituzoneValue, _adifValue, _qslManagerValue,
                _emailValue, _lotwValue, _activityValue, _sourcesValue })
                tb.Text = "--";
        }

        private async System.Threading.Tasks.Task DoQrzLookupAsync()
        {
            if (_manager == null) return;
            StatusValue.Text = "Looking up via QRZ...";
            var result = await _manager.LookupQrzAsync(_call);
            if (result != null)
            {
                QrzLookupOccurred = true;
                StatusValue.Text = "QRZ lookup complete.";
                PopulateFromCache();
            }
            else
            {
                StatusValue.Text = $"QRZ: {_manager.Qrz.LastError ?? "No data returned."}";
            }
            StatusValue.Focus();
        }

        private static string FormatAge(DateTime cachedAtUtc)
        {
            var age = DateTime.UtcNow - cachedAtUtc;
            if (age.TotalMinutes < 1) return "moments ago";
            if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min ago";
            if (age.TotalHours < 24) return $"{(int)age.TotalHours}h ago";
            if (age.TotalDays < 2) return "yesterday";
            return $"{(int)age.TotalDays} days ago";
        }

        private static readonly Dictionary<string, string> UsLicenseClassNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { { "N", "Novice" }, { "T", "Technician" }, { "G", "General" }, { "A", "Advanced" }, { "E", "Extra" } };

        private static string FormatLicenseClass(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "--";
            return UsLicenseClassNames.TryGetValue(raw, out var name) ? name : raw;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
