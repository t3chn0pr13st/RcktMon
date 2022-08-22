using System.Threading.Tasks;
using Caliburn.Micro;
using CoreData.Interfaces;
using CoreData.Settings;
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
        public decimal MinXMinutesPriceChangePercent { get; set; }
        public decimal MinVolumeDeviationFromDailyAveragePercent { get; set; }
        public decimal MinXMinutesVolPercentChangePercent { get; set; }
        public int NumOfMinToCheck { get; set; }
        public int NumOfMinToCheckVol { get; set; }
        public bool IsTelegramEnabled { get; set; }
        public string TgCallbackUrl { get; set; }

        public bool SubscribeInstrumentStatus { get; set; }
        public bool HideRussianStocks { get; set; }
        public bool UseInvesting { get; set; }
        public bool USAQuotesEnabled { get; set; }
        public string USAQuotesURL { get; set; }
        public string USAQuotesLogin { get; set; }
        public string USAQuotesPassword { get; set; }
        public string TgArbitrageShortUSAChatId { get; set; }
        public string TgArbitrageLongUSAChatId { get; set; }
        public string ChartUrlTemplate { get; set; }
        public string IncludePattern { get; set; }
        public string ExcludePattern { get; set; }

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
            MinXMinutesPriceChangePercent = Settings.MinXMinutesPriceChange * 100m;
            MinVolumeDeviationFromDailyAveragePercent = Settings.MinVolumeDeviationFromDailyAverage * 100m;
            MinXMinutesVolPercentChangePercent = Settings.MinXMinutesVolChange * 100m;
            NumOfMinToCheck = Settings.NumOfMinToCheck;
            NumOfMinToCheckVol = Settings.NumOfMinToCheckVol;
            IsTelegramEnabled = Settings.IsTelegramEnabled;
            TgCallbackUrl = Settings.TgCallbackUrl;
            USAQuotesEnabled = Settings.USAQuotesEnabled;
            USAQuotesURL = Settings.USAQuotesURL;
            USAQuotesLogin = Settings.USAQuotesLogin;
            USAQuotesPassword = Settings.USAQuotesPassword;
            TgArbitrageLongUSAChatId = Settings.TgArbitrageLongUSAChatId;
            TgArbitrageShortUSAChatId = Settings.TgArbitrageShortUSAChatId;
            SubscribeInstrumentStatus = Settings.SubscribeInstrumentStatus;
            HideRussianStocks = Settings.HideRussianStocks;
            ChartUrlTemplate = Settings.ChartUrlTemplate;
            ExcludePattern = Settings.ExcludePattern;
            IncludePattern = Settings.IncludePattern;
            HideKeys();
        }

        private void HideKeys()
        {
            TiApiKey = PasswordBehavior.PassReplacement;
            TgBotApiKey = PasswordBehavior.PassReplacement;
            USAQuotesPassword = PasswordBehavior.PassReplacement;
        }

        public async Task AcceptKeys()
        {
            if (TiApiKey != PasswordBehavior.PassReplacement)
                Settings.TiApiKey = TiApiKey;
            if (TgBotApiKey != PasswordBehavior.PassReplacement)
                Settings.TgBotApiKey = TgBotApiKey;

            Settings.TgChatId = TgChatId;
            Settings.TgChatIdRu = TgChatIdRu;
            Settings.ChartUrlTemplate = ChartUrlTemplate;
            Settings.TgCallbackUrl = TgCallbackUrl;

            var last = _settingsProvider.LastSettings;
            bool needReconnect = last.TiApiKey != Settings.TiApiKey
                || last.TgBotApiKey != Settings.TgBotApiKey
                || last.TgChatId != Settings.TgChatId
                || last.TgChatIdRu != Settings.TgChatId;

            _settingsProvider.SaveSettings(_settingsProvider.Settings);

            HideKeys();
            
            if (needReconnect)
            {
                StocksManager.Init();
                await _mainViewModel.RefreshAll();
            }   
        }

        public void AcceptOptions()
        {
            Settings.MinDayPriceChange = MinDayPriceChangePercent / 100m;
            Settings.MinXMinutesPriceChange = MinXMinutesPriceChangePercent / 100m;
            Settings.MinVolumeDeviationFromDailyAverage = MinVolumeDeviationFromDailyAveragePercent / 100m;
            Settings.MinXMinutesVolChange = MinXMinutesVolPercentChangePercent / 100m;
            Settings.IsTelegramEnabled = IsTelegramEnabled;
            Settings.TgCallbackUrl = TgCallbackUrl;
            Settings.NumOfMinToCheck = NumOfMinToCheck;
            Settings.NumOfMinToCheckVol = NumOfMinToCheckVol;

            Settings.USAQuotesEnabled = USAQuotesEnabled;
            Settings.USAQuotesURL = USAQuotesURL;
            Settings.USAQuotesLogin = USAQuotesLogin;
            Settings.TgArbitrageShortUSAChatId = TgArbitrageShortUSAChatId;
            Settings.TgArbitrageLongUSAChatId = TgArbitrageLongUSAChatId;
            Settings.HideRussianStocks = HideRussianStocks;
            Settings.SubscribeInstrumentStatus = SubscribeInstrumentStatus;
            Settings.ChartUrlTemplate = ChartUrlTemplate;
            Settings.IncludePattern = IncludePattern;
            Settings.ExcludePattern = ExcludePattern;

            if (USAQuotesPassword != PasswordBehavior.PassReplacement)
            {
                Settings.USAQuotesPassword = USAQuotesPassword;
                HideKeys();
            }

            _settingsProvider.SaveSettings(_settingsProvider.Settings);
        }

    }
}