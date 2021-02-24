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
                var definition = new 
                {
                    TiApiKey, TgBotApiKey, TgChatId, TgChatIdRu,
                    MinDayPriceChange, MinXMinutesPriceChange, 
                    MinVolumeDeviationFromDailyAverage, MinXMinutesVolChange,
                    NumOfMinToCheck, NumOfMinToCheckVol, ChartUrlTemplate,
                    IsTelegramEnabled, CheckRockets, SubscribeInstrumentStatus, HideRussianStocks,
                    USAQuotesEnabled, USAQuotesURL, USAQuotesLogin, USAQuotesPassword, TgArbitrageLongUSAChatId, TgArbitrageShortUSAChatId
                };
                var obj = JsonConvert.DeserializeAnonymousType(text, definition);
                var config = new MapperConfiguration(cfg => 
                    cfg.CreateMap(obj.GetType(), this.GetType()));
                var mapper = new Mapper(config);
                mapper.Map(obj, this, obj.GetType(), this.GetType());
                try { this.TiApiKey = CryptoHelper.Decrypt(this.TiApiKey); } catch { TiApiKey = null; }
                try { this.TgBotApiKey = CryptoHelper.Decrypt(this.TgBotApiKey); } catch { TgBotApiKey = null; }
                try { this.TgChatId = CryptoHelper.Decrypt(this.TgChatId); } catch { TgChatId = null; }
                try { this.TgChatIdRu = CryptoHelper.Decrypt(this.TgChatIdRu); } catch { TgChatIdRu = null; }
                try { this.USAQuotesPassword = CryptoHelper.Decrypt(this.USAQuotesPassword); } catch { USAQuotesPassword = null; }
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
                ChartUrlTemplate = "https://stockcharts.com/c-sc/sc?s={0}&p=D&yr=0&mn=3&dy=0&i=t8988066255c";
            else if (ChartUrlTemplate == "!disabled")
                ChartUrlTemplate = "";

            return base.ReadSettings();
        }

        private object AnonymousSettingsObj => new 
        {
            TiApiKey = CryptoHelper.Encrypt(TiApiKey),
            TgBotApiKey = CryptoHelper.Encrypt(TgBotApiKey),
            TgChatId = CryptoHelper.Encrypt(TgChatId),
            TgChatIdRu = CryptoHelper.Encrypt(TgChatIdRu),
            USAQuotesPassword = CryptoHelper.Encrypt(USAQuotesPassword),
            MinDayPriceChange, MinXMinutesPriceChange, 
            MinVolumeDeviationFromDailyAverage, MinXMinutesVolChange,
            NumOfMinToCheck, NumOfMinToCheckVol, ChartUrlTemplate,
            IsTelegramEnabled, CheckRockets, SubscribeInstrumentStatus, HideRussianStocks,
            USAQuotesEnabled, USAQuotesURL, USAQuotesLogin, TgArbitrageLongUSAChatId, TgArbitrageShortUSAChatId
        };

        public override void SaveSettings(INgineSettings settings)
        {
            try 
            {
                if (settings is SettingsModel sm)
                {
                    if (String.IsNullOrWhiteSpace(sm.ChartUrlTemplate))
                        sm.ChartUrlTemplate = "!disabled";
                    System.IO.File.WriteAllText("settings.json", 
                        JsonConvert.SerializeObject(sm.AnonymousSettingsObj, Formatting.Indented));
                    _eventAggregator.PublishOnCurrentThreadAsync(new SettingsChangeEventArgs(LastSettings, settings));
                    LastSettings = settings.Clone() as INgineSettings;
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, ex.Message);
            }
        }
    }
}
