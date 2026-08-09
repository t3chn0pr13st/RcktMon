using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Caliburn.Micro;
using CoreData.Interfaces;
using CoreData.Settings;
using CoreNgine.Models;
using CoreNgine.Shared;
using RcktMon.Helpers;
using RcktMon.Views;

namespace RcktMon.ViewModels
{
    public class AssetGroupSettingsViewModel : PropertyChangedBase, IAssetGroupSettings
    {
        public bool IsSubscriptionEnabled { get; set; }
        public bool IsTelegramEnabled { get; set; }
        public string Currency { get; set; }
        public string CurrencyDisplay { get; set; }
        public decimal MinDayPriceChange { get; set; }
        public decimal MinXMinutesPriceChange { get; set; }
        public int NumOfMinToCheck { get; set; }
        public int NumOfMinToCheckVol { get; set; }
        public decimal MinVolumeDeviationFromDailyAverage { get; set; }
        public decimal MinXMinutesVolChange { get; set; }
        public string IncludePattern { get; set; }
        public string ExcludePattern { get; set; }
        public string ChartUrlTemplate { get; set; }
    }

    public class SettingsViewModel : PropertyChangedBase, INgineSettings
    {
        public string TiApiKey { get; set; }
        public string TgBotApiKey { get; set; }
        public string TgChatId { get; set; }
        public string TgChatIdRu { get; set; }

        public decimal MinDayPriceChange { get; set; }
        public decimal MinXMinutesPriceChange { get; set; }
        public decimal MinVolumeDeviationFromDailyAverage { get; set; }
        public decimal MinXMinutesVolChange { get; set; }
        public int NumOfMinToCheck { get; set; }
        public int NumOfMinToCheckVol { get; set; }
        public bool IsTelegramEnabled { get; set; }
        public string TgCallbackUrl { get; set; }
        public string KvtToken { get; set; }

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

        public bool CheckRockets { get; set; }

        private ISettingsProvider _settingsProvider;
        private MainViewModel _mainViewModel;
        private Mapper _settingsMapper;

        public StocksManager StocksManager => _mainViewModel.StocksManager;

        public INgineSettings Settings => _settingsProvider.Settings;

        public ObservableCollection<AssetGroupSettingsViewModel> AssetGroupSettingsCollection { get; } = new ObservableCollection<AssetGroupSettingsViewModel>();


        public SettingsViewModel()
        {

        }

        public SettingsViewModel(ISettingsProvider settingsProvider, IMainModel mainModel)
        {
            _mainViewModel = mainModel as MainViewModel;
            _settingsProvider = settingsProvider;

            _settingsMapper = new Mapper(new MapperConfiguration(cfg =>
            {
                cfg.CreateMap(GetType(), Settings.GetType());
                cfg.CreateMap(Settings.GetType(), GetType());
                cfg.CreateMap<SettingsViewModel, SettingsModel>();
                cfg.CreateMap<SettingsViewModel, SettingsContainer>();
                cfg.CreateMap<SettingsModel, SettingsViewModel>();
                cfg.CreateMap<SettingsContainer, SettingsViewModel>();
                cfg.CreateMap<AssetGroupSettingsViewModel, IAssetGroupSettings>();
                cfg.CreateMap<IAssetGroupSettings, AssetGroupSettingsViewModel>();
            }));

            _settingsMapper.Map(Settings, this, Settings.GetType(), this.GetType());
            HideKeys();

            LoadGroupSettings();
        }

        private void LoadGroupSettings()
        {
            foreach (var settingGroup in _settingsProvider.AssetGroupSettingsByCurrency)
            {
                var groupSettingsViewModel = new AssetGroupSettingsViewModel();
                _settingsMapper.Map(settingGroup.Value, groupSettingsViewModel);
                AssetGroupSettingsCollection.Add(groupSettingsViewModel);
            }
        }

        private void HideKeys()
        {
            TiApiKey = PasswordBehavior.PassReplacement;
            TgBotApiKey = PasswordBehavior.PassReplacement;
            USAQuotesPassword = PasswordBehavior.PassReplacement;
            KvtToken = PasswordBehavior.PassReplacement;
        }

        private void ShowKeys()
        {
            if (TiApiKey == PasswordBehavior.PassReplacement)
                TiApiKey = Settings.TiApiKey;
            if (TgBotApiKey == PasswordBehavior.PassReplacement)
                TgBotApiKey = Settings.TgBotApiKey;
            if (USAQuotesPassword == PasswordBehavior.PassReplacement)
                USAQuotesPassword = Settings.USAQuotesPassword;
            if (KvtToken == PasswordBehavior.PassReplacement)
                KvtToken = Settings.KvtToken;
        }

        public void AcceptKeys()
        {
            ShowKeys();

            _settingsMapper.Map(this, Settings, this.GetType(), Settings.GetType());

            foreach (var settingGroupViewModel in AssetGroupSettingsCollection)
            {
                if (_settingsProvider.AssetGroupSettingsByCurrency.TryGetValue(settingGroupViewModel.Currency, 
                    out var settingsGroupModel))
                    _settingsMapper.Map(settingGroupViewModel, settingsGroupModel);
            }

            _settingsProvider.SaveSettings(_settingsProvider.Settings);

            HideKeys();
        }

        public void AcceptOptions()
        {
            AcceptKeys();
        }

        public object Clone()
        {
            throw new System.NotImplementedException();
        }
    }
}