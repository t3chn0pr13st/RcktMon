using System.Threading.Tasks;
using Caliburn.Micro;
using CoreData.Interfaces;
using CoreNgine.Models;
using CoreNgine.Shared;
using RcktMon.Views;

namespace RcktMon.ViewModels
{
    public class SettingsViewModel : PropertyChangedBase
    {
        public string TiApiKey { get; set; }
        public string TgBotApiKey { get; set; }
        public string TgChatId { get; set; }
        public string TgChatIdRu { get; set; }

        public decimal MinDayPriceChangePercent { get; set; }
        public decimal MinTenMinutesPriceChangePercent { get; set; }
        public decimal MinVolumeDeviationFromDailyAveragePercent { get; set; }
        public decimal MinTenMinutesVolPercentChangePercent { get; set; }
        public bool IsTelegramEnabled { get; set; }

        public bool USAQuotesEnabled { get; set; }
        public string USAQuotesURL { get; set; }
        public string USAQuotesLogin { get; set; }
        public string USAQuotesPassword { get; set; }
        public string TgArbitrageShortUSAChatId { get; set; }
        public string TgArbitrageLongUSAChatId { get; set; }

        private ISettingsProvider _settingsProvider;
        private MainViewModel _mainViewModel;

        public StocksManager StocksManager => _mainViewModel.StocksManager;

        public INgineSettings Settings => _settingsProvider.Settings;

        public SettingsViewModel()
        {

        }

        public SettingsViewModel(ISettingsProvider settingsProvider, IMainModel mainModel)
        {
            _mainViewModel = mainModel as MainViewModel;
            _settingsProvider = settingsProvider;

            TiApiKey = Settings.TiApiKey;
            TgBotApiKey = Settings.TgBotApiKey;
            TgChatId = Settings.TgChatId;
            TgChatIdRu = Settings.TgChatIdRu;
            MinDayPriceChangePercent = Settings.MinDayPriceChange * 100m;
            MinTenMinutesPriceChangePercent = Settings.MinTenMinutesPriceChange * 100m;
            MinVolumeDeviationFromDailyAveragePercent = Settings.MinVolumeDeviationFromDailyAverage * 100m;
            MinTenMinutesVolPercentChangePercent = Settings.MinTenMinutesVolPercentChange * 100m;
            IsTelegramEnabled = Settings.IsTelegramEnabled;
            USAQuotesEnabled = Settings.USAQuotesEnabled;
            USAQuotesURL = Settings.USAQuotesURL;
            USAQuotesLogin = Settings.USAQuotesLogin;
            USAQuotesPassword = Settings.USAQuotesPassword;
            TgArbitrageLongUSAChatId = Settings.TgArbitrageLongUSAChatId;
            TgArbitrageShortUSAChatId = Settings.TgArbitrageShortUSAChatId;
            ResetKeys();
        }

        private void ResetKeys()
        {
            TiApiKey = PasswordBehavior.PassReplacement;
            TgBotApiKey = PasswordBehavior.PassReplacement;
            USAQuotesPassword = PasswordBehavior.PassReplacement;
        }

        public async Task AcceptKeys()
        {
            if (TiApiKey != PasswordBehavior.PassReplacement)
                _settingsProvider.Settings.TiApiKey = TiApiKey;
            if (TgBotApiKey != PasswordBehavior.PassReplacement)
                _settingsProvider.Settings.TgBotApiKey = TgBotApiKey;

            _settingsProvider.Settings.TgChatId = TgChatId;
            _settingsProvider.Settings.TgChatIdRu = TgChatIdRu;
            _settingsProvider.SaveSettings(_settingsProvider.Settings);
            ResetKeys();

            StocksManager.Init();
            await StocksManager.UpdateStocks();
        }

        public void AcceptOptions()
        {
            Settings.MinDayPriceChange = MinDayPriceChangePercent / 100m;
            Settings.MinTenMinutesPriceChange = MinTenMinutesPriceChangePercent / 100m;
            Settings.MinVolumeDeviationFromDailyAverage = MinVolumeDeviationFromDailyAveragePercent / 100m;
            Settings.MinTenMinutesVolPercentChange = MinTenMinutesVolPercentChangePercent / 100m;
            Settings.IsTelegramEnabled = IsTelegramEnabled;

            Settings.USAQuotesEnabled = USAQuotesEnabled;
            Settings.USAQuotesURL = USAQuotesURL;
            Settings.USAQuotesLogin = USAQuotesLogin;
            Settings.TgArbitrageShortUSAChatId = TgArbitrageShortUSAChatId;
            Settings.TgArbitrageLongUSAChatId = TgArbitrageLongUSAChatId;
            if (USAQuotesPassword != PasswordBehavior.PassReplacement)
            {
                Settings.USAQuotesPassword = USAQuotesPassword;
                ResetKeys();
            }

            _settingsProvider.SaveSettings(_settingsProvider.Settings);
        }

    }
}