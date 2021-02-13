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

namespace RcktMon.Helpers
{
    public class SettingsModel : ISettingsProvider, INgineSettings
    {
        #region App Settings 
        
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
        public bool CheckRockets { get; set; }

        public bool USAQuotesEnabled { get; set; }
        public string USAQuotesURL { get; set; }
        public string USAQuotesLogin { get; set; }
        public string USAQuotesPassword { get; set; }
        public string TgArbitrageLongUSAChatId { get; set; }
        public string TgArbitrageShortUSAChatId { get; set; }

        #endregion App Settings

        public INgineSettings Settings => this;

        private ILogger<SettingsModel> _logger;
        private IEventAggregator2 _eventAggregator;

        public SettingsModel(ILogger<SettingsModel> logger, IEventAggregator2 eventAggregator)
        {
            _logger = logger;
            _eventAggregator = eventAggregator;
            ReadSettings();
        }

        public INgineSettings ReadSettings()
        {
            if (File.Exists("settings.json"))
            {
                var text = File.ReadAllText("settings.json");
                var definition = new 
                {
                    TiApiKey, TgBotApiKey, TgChatId, TgChatIdRu,
                    MinDayPriceChange, MinXMinutesPriceChange, 
                    MinVolumeDeviationFromDailyAverage, MinXMinutesVolChange,
                    NumOfMinToCheck, NumOfMinToCheckVol,
                    IsTelegramEnabled, CheckRockets,
                    USAQuotesEnabled, USAQuotesURL, USAQuotesLogin, USAQuotesPassword, TgArbitrageLongUSAChatId, TgArbitrageShortUSAChatId
                };
                var obj = JsonConvert.DeserializeAnonymousType(text, definition);
                var config = new MapperConfiguration(cfg => 
                    cfg.CreateMap(obj.GetType(), this.GetType()));
                var mapper = new Mapper(config);
                mapper.Map(obj, this, obj.GetType(), this.GetType());
                try { this.TiApiKey = CryptoHelper.Decrypt(this.TiApiKey); } catch { }
                try { this.TgBotApiKey = CryptoHelper.Decrypt(this.TgBotApiKey); } catch { }
                try { this.TgChatId = CryptoHelper.Decrypt(this.TgChatId); } catch { }
                try { this.TgChatIdRu = CryptoHelper.Decrypt(this.TgChatIdRu); } catch { }
                try { this.USAQuotesPassword = CryptoHelper.Decrypt(this.USAQuotesPassword); } catch { }
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

            return this;
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
            NumOfMinToCheck, NumOfMinToCheckVol,
            IsTelegramEnabled, CheckRockets,
            USAQuotesEnabled, USAQuotesURL, USAQuotesLogin, TgArbitrageLongUSAChatId, TgArbitrageShortUSAChatId
        };

        public void SaveSettings(INgineSettings settings)
        {
            try 
            {
                if (settings is SettingsModel sm)
                {
                    System.IO.File.WriteAllText("settings.json", 
                        JsonConvert.SerializeObject(sm.AnonymousSettingsObj, Formatting.Indented));
                    _eventAggregator.PublishOnCurrentThreadAsync(settings);
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, ex.Message);
            }
        }
    }
}
