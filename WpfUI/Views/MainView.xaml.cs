using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
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
            if (e.NewValue is MainViewModel mainViewModel && String.IsNullOrEmpty(mainViewModel.Settings.TiApiKey))
            {
                KeySettings.IsExpanded = true;
            }
        }
    }
}
