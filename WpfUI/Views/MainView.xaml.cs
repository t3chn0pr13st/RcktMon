using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using RcktMon.Helpers;
using RcktMon.ViewModels;

namespace RcktMon.Views
{
    /// <summary>
    /// Interaction logic for TradeView.xaml
    /// </summary>
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is MainViewModel mainViewModel)
            {
                if (String.IsNullOrEmpty(mainViewModel.Settings.TiApiKey))
                    KeySettings.IsExpanded = true;
            }

            if (!Environment.GetCommandLineArgs().Contains("/arbitrage") && !Environment.MachineName.Equals("E5-2678-V3"))
            {
                USADataSettingsExpander.Visibility = Visibility.Collapsed;
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

        private void ShowUpdateDetailsHyperlinkClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel main)
            {
                try
                {
                    Process.Start("explorer", main.LastRelease.DetailsUrl);
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
    }
}
