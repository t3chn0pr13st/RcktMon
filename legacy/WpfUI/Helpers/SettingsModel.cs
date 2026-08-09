using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using CoreData.Interfaces;
using CoreNgine.Infra;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RcktMon.Properties;
using CoreCodeGenerators;
using CoreData.Models;
using CoreData;
using CoreData.Settings;
using Caliburn.Micro;

namespace RcktMon.Helpers
{
    public class SettingsModel : SettingsContainer
    {
        private ILogger<SettingsModel> _logger;

        protected IEventAggregator2 _eventAggregator;

        public SettingsModel(ILogger<SettingsModel> logger, IEventAggregator2 eventAggregator) : base()
        {
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public override INgineSettings ReadSettings()
        {
            if (File.Exists("settings.json"))
            {
                var text = File.ReadAllText("settings.json");
                var obj = JsonConvert.DeserializeObject<SettingsModel>(text);
                obj.DecryptProperties();

                var config = new MapperConfiguration(cfg =>
                    cfg.CreateMap(obj.GetType(), this.GetType()));
                var mapper = new Mapper(config);
                mapper.Map(obj, this, obj.GetType(), GetType());
            }

            if (MinDayPriceChange == 0)
            {
                MinDayPriceChange = 0.08m;
                CheckRockets = true;
                IsTelegramEnabled = true;
            }
            if (MinVolumeDeviationFromDailyAverage == 0)
                MinVolumeDeviationFromDailyAverage = 0.002m;
            if (MinXMinutesPriceChange == 0)
                MinXMinutesPriceChange = 0.03m;
            if (MinXMinutesVolChange == 0)
                MinXMinutesVolChange = 0.5m;
            if (NumOfMinToCheck == 0)
                NumOfMinToCheck = 10;
            if (NumOfMinToCheckVol == 0)
                NumOfMinToCheckVol = 10;

            if (String.IsNullOrWhiteSpace(ChartUrlTemplate))
                ChartUrlTemplate = "https://stockcharts.com/c-sc/sc?s={ticker}&p=D&yr=0&mn=3&dy=0&i=t8988066255c&r={unixTime}";
            else if (ChartUrlTemplate.Contains("stockcharts.com") && !ChartUrlTemplate.Contains("&r="))
                ChartUrlTemplate = ChartUrlTemplate + "&r={unixTime}";
            else if (ChartUrlTemplate == "!disabled")
                ChartUrlTemplate = "";

            if (String.IsNullOrWhiteSpace(TgCallbackUrl))
                TgCallbackUrl = "https://kvalood.ru/ticker";

            InitCurrencyGroupSettings();

            return base.ReadSettings();
        }

        private void InitCurrencyGroupSettings()
        {
            (string Name, string Sign, Action<IAssetGroupSettings> InitValuesAction)[] currencies = new[] {
                ("RUB", "₽", new Action<IAssetGroupSettings>(obj => {
                    obj.IsTelegramEnabled = obj.IsSubscriptionEnabled = !HideRussianStocks;
                })), 
                ("USD", "$", new Action<IAssetGroupSettings>(obj => {
                    obj.ChartUrlTemplate = ChartUrlTemplate;
                })), 
                ("EUR", "€", null), 
                ("HKD", "HK$", null)
            };

            if (AssetGroupSettingsByCurrency == null)
                AssetGroupSettingsByCurrency = new Dictionary<string, AssetGroupSettingsModel>();
            foreach (var currency in currencies)
            {
                if (!AssetGroupSettingsByCurrency.ContainsKey(currency.Name))
                {
                    var groupSettings = new AssetGroupSettingsModel()
                    {
                        Currency = currency.Name,
                        CurrencyDisplay = currency.Sign,
                        MinDayPriceChange = MinDayPriceChange,
                        MinXMinutesPriceChange = MinXMinutesPriceChange,
                        NumOfMinToCheck = NumOfMinToCheck,
                        NumOfMinToCheckVol = NumOfMinToCheckVol,
                        MinVolumeDeviationFromDailyAverage = MinVolumeDeviationFromDailyAverage,
                        MinXMinutesVolChange = MinXMinutesVolChange,
                        IncludePattern = IncludePattern,
                        ExcludePattern = ExcludePattern,
                        IsSubscriptionEnabled = true,
                        IsTelegramEnabled = true
                    };
                    currency.InitValuesAction?.Invoke(groupSettings);
                    AssetGroupSettingsByCurrency.Add(currency.Name, groupSettings);
                }
            }
        }

        private void DecryptProperties()
        {
            try { TiApiKey = CryptoHelper.Decrypt(TiApiKey); } catch { TiApiKey = null; }
            try { TgBotApiKey = CryptoHelper.Decrypt(TgBotApiKey); } catch { TgBotApiKey = null; }
            try { TgChatId = CryptoHelper.Decrypt(TgChatId); } catch { TgChatId = null; }
            try { TgChatIdRu = CryptoHelper.Decrypt(TgChatIdRu); } catch { TgChatIdRu = null; }
            try { USAQuotesPassword = CryptoHelper.Decrypt(USAQuotesPassword); } catch { USAQuotesPassword = null; }
        }

        private void EncryptProperties()
        {
            TiApiKey = CryptoHelper.Encrypt(TiApiKey);
            TgBotApiKey = CryptoHelper.Encrypt(TgBotApiKey);
            TgChatId = CryptoHelper.Encrypt(TgChatId);
            TgChatIdRu = CryptoHelper.Encrypt(TgChatIdRu);
            USAQuotesPassword = CryptoHelper.Encrypt(USAQuotesPassword);
        }

        public override void SaveSettings(INgineSettings settings)
        {
            try
            {
                if (settings is SettingsModel sm)
                {
                    if (String.IsNullOrWhiteSpace(sm.ChartUrlTemplate))
                        sm.ChartUrlTemplate = "!disabled";

                    var objToSave = (SettingsModel)sm.MemberwiseClone();
                    objToSave.EncryptProperties();

                    System.IO.File.WriteAllText("settings.json",
                        JsonConvert.SerializeObject(objToSave, Formatting.Indented));
                    _eventAggregator.PublishOnCurrentThreadAsync(new SettingsChangeEventArgs(LastSettings, settings));
                    LastSettings = settings.Clone() as INgineSettings;
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, ex.Message);
            }
        }

        public override object Clone()
        {
            var copy = (SettingsContainer)base.Clone();
            var newDict = new Dictionary<string, AssetGroupSettingsModel>();
            foreach (var pair in copy.AssetGroupSettingsByCurrency)
                newDict.Add(pair.Key, (AssetGroupSettingsModel)pair.Value.Clone());
            copy.AssetGroupSettingsByCurrency = newDict;
            return copy;
        }
    }
}
