using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;

namespace TradeApp
{
    /// <summary>
    /// Interaction logic for TradeView.xaml
    /// </summary>
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
            Loaded += TradeView_Loaded;
        }

        private async void TradeView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is MainViewModel tradingViewModel)
            {
                await Task.Run(() => tradingViewModel.StocksManager.UpdateStocks());
            }
        }
    }
}
