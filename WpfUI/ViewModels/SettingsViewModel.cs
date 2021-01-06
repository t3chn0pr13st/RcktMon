using System.Threading.Tasks;
using Caliburn.Micro;
using RcktMon.Views;

namespace RcktMon.ViewModels
{
    public class SettingsViewModel : PropertyChangedBase
    {
        public string TiApiKey { get; set; }
        public string TgBotApiKey { get; set; }
        public string TgChatId { get; set; }

        public decimal MinDayPriceChangePercent { get; set; }
        public decimal MinTenMinutesPriceChangePercent { get; set; }
        public decimal MinVolumeDeviationFromDailyAveragePercent { get; set; }
        public bool IsTelegramEnabled { get; set; }

        private MainViewModel _tradingViewModel;

        public SettingsViewModel()
        {

        }

        public SettingsViewModel(MainViewModel tradingViewModel )
        {
            _tradingViewModel = tradingViewModel;
            TiApiKey = tradingViewModel.TiApiKey;
            TgBotApiKey = tradingViewModel.TgBotApiKey;
            TgChatId = tradingViewModel.TgChatId;
            MinDayPriceChangePercent = tradingViewModel.MinDayPriceChange * 100m;
            MinTenMinutesPriceChangePercent = tradingViewModel.MinTenMinutesPriceChange * 100m;
            MinVolumeDeviationFromDailyAveragePercent = tradingViewModel.MinVolumeDeviationFromDailyAverage * 100m;
            IsTelegramEnabled = tradingViewModel.IsTelegramEnabled;
            ResetKeys();
        }

        private void ResetKeys()
        {
            TiApiKey = PasswordBehavior.PassReplacement;
            TgBotApiKey = PasswordBehavior.PassReplacement;
        }

        public async Task AcceptKeys()
        {
            if (TiApiKey != PasswordBehavior.PassReplacement)
                _tradingViewModel.TiApiKey = TiApiKey;
            if (TgBotApiKey != PasswordBehavior.PassReplacement)
                _tradingViewModel.TgBotApiKey = TgBotApiKey;

            _tradingViewModel.TgChatId = TgChatId;
            _tradingViewModel.SaveAppSettings();
            ResetKeys();

            _tradingViewModel.StocksManager.Init();
            await _tradingViewModel.StocksManager.UpdateStocks();
        }

        public void AcceptOptions()
        {
            _tradingViewModel.MinDayPriceChange = MinDayPriceChangePercent / 100m;
            _tradingViewModel.MinTenMinutesPriceChange = MinTenMinutesPriceChangePercent / 100m;
            _tradingViewModel.MinVolumeDeviationFromDailyAverage = MinVolumeDeviationFromDailyAveragePercent / 100m;
            _tradingViewModel.IsTelegramEnabled = IsTelegramEnabled;
            _tradingViewModel.SaveAppSettings();
        }

    }
}
