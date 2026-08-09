using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using Newtonsoft.Json;
using RcktMon.Helpers;
using RcktMon.Models;
using RcktMon.ViewModels;

namespace RcktMon.Views
{
    /// <summary>
    /// Interaction logic for MainView.xaml
    /// </summary>
    public partial class MainView : UserControl
    {
        private readonly DispatcherTimer _saveDataGridSettingsTimer = new DispatcherTimer() { IsEnabled = false, Interval = TimeSpan.FromSeconds(1) };
        private List<DataGridColumnSettings> _lastColWidthsList = new List<DataGridColumnSettings>();
        private Window _mainWindow = null;

        public MainView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            _ = LoadDataGridColumns();
            _saveDataGridSettingsTimer.Tick += _saveDataGridSettingsTimer_Tick;
        }

        private async void _saveDataGridSettingsTimer_Tick(object sender, EventArgs e)
        {
            _saveDataGridSettingsTimer.Stop();
            await TrySaveDataGridColumnsAsync();
        }

        private async Task LoadDataGridColumns()
        {
            try
            {
                var json = await File.ReadAllTextAsync("columns.json");
                _lastColWidthsList = JsonConvert.DeserializeObject<List<DataGridColumnSettings>>(json);
                foreach (var colSettings in _lastColWidthsList)
                {
                    var grid = FindName(colSettings.GridName);
                    if (grid is DataGrid dg)
                    {
                        if (dg.Columns.FirstOrDefault(c => c.Header is string title
                        && title == colSettings.ColumnName) is DataGridColumn col)
                        {
                            if (col.ActualWidth != colSettings.ColumnWidth)
                                col.Width = new DataGridLength(colSettings.ColumnWidth);
                            if (col.DisplayIndex != colSettings.DisplayIndex)
                                col.DisplayIndex = colSettings.DisplayIndex;
                        }
                    }
                }
            } 
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private void DataGrid_LayoutUpdated(object sender, EventArgs e)
        {
            // перезапускаем таймер, чтобы сработал через секунду после последнего события обновления грида
            _saveDataGridSettingsTimer.Stop();
            _saveDataGridSettingsTimer.Start();
        }

        public async Task TrySaveDataGridColumnsAsync()
        {
            var colWidthsList = new List<DataGridColumnSettings>();

            var grids = new[] { StocksDataGrid, InstrumentsDataGrid, LogDataGrid };

            foreach (var grid in grids)
            {
                foreach (var column in grid.Columns)
                {
                    if (column.Header is string title)
                    {
                        colWidthsList.Add(new (grid.Name, title, column.ActualWidth, column.DisplayIndex));
                    }
                }
            }

            bool changed = colWidthsList.Count != _lastColWidthsList.Count;
            if (!changed)
            {
                for (int i = 0; i < colWidthsList.Count; i++)
                {
                    if (colWidthsList[i] != _lastColWidthsList[i])
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                _lastColWidthsList = colWidthsList;
                //Debug.WriteLine("Columns width|pos changed!");
                try
                {
                    var json = JsonConvert.SerializeObject(colWidthsList);
                    await File.WriteAllTextAsync("columns.json", json);
                } 
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
            else
            {
                //Debug.WriteLine("Columns width|pos NOT changed!");
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is MainViewModel mainViewModel)
            {
                if (String.IsNullOrEmpty(mainViewModel.Settings.TiApiKey))
                    Settings.KeySettings.IsExpanded = true;
            }

            if (!Environment.GetCommandLineArgs().Contains("/arbitrage") && !Environment.MachineName.Equals("E5-2678-V3"))
            {
                Settings.USADataSettingsExpander.Visibility = Visibility.Collapsed;
                AskDiffUSAColumn.Visibility = Visibility.Collapsed;
                BidAskUSAColumn.Visibility = Visibility.Collapsed;
                BidDiffUSAColumn.Visibility = Visibility.Collapsed;
                BidUSAColumn.Visibility = Visibility.Collapsed;
                DiffUSAColumn.Visibility = Visibility.Collapsed;
                PriceUSAColumn.Visibility = Visibility.Collapsed;
            }
        }

        private void HyperlinkCopyTicker_OnClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Hyperlink el && el.DataContext is MessageViewModel message)
            {
                try
                {
                    Clipboard.SetText(message.Ticker);
                }
                catch
                {

                }
            }
        }

        private void CloseUpdateNotificationHyperlinkClick(object sender, RoutedEventArgs e)
        {
            UpdateNotificationBorder.Visibility = Visibility.Collapsed;
        }

        private void HyperlinkOpenInAurora_OnClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Hyperlink el && el.DataContext is MessageViewModel message)
            {
                if (DataContext is MainViewModel main)
                    main.OpenInAurora(message.Ticker);
            }
        }

        private void StocksDataGrid_OnPreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement el && el.DataContext is StockViewModel stock 
                                                        && this.DataContext is MainViewModel main)
            {
                main.OpenInAurora(stock.Ticker);
            }
        }

        private void HyperlinkOpenInBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Hyperlink link && !string.IsNullOrWhiteSpace(link.NavigateUri?.ToString()))
            {
                try
                {
                    var psi = new ProcessStartInfo(link.NavigateUri.ToString()) {
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                catch
                {

                }
            }

        }

        private void ShowUpdateDetailsHyperlinkClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel main)
            {
                try
                {
                    var psi = new ProcessStartInfo(main.LastRelease.DetailsUrl) { UseShellExecute = true };
                    Process.Start(psi);
                } 
                catch
                {

                }
            }
                
        }

        private void InstallUpdateHyperlinkClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel main)
                _ = main.InstallUpdate();
        }

        private void SetTerminalGroupOnButtonClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Button btn && btn.Tag != null
                && int.TryParse(btn.Tag.ToString(), out var groupNum)
                && btn.DataContext is MessageViewModel message)
            {
                if (DataContext is MainViewModel main)
                    _ = main.ChangeTickerViaExt(message.Ticker, groupNum);
            }
        }

    }
}
