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
        public decimal MinDayPriceChange { get; set; }
        public decimal MinTenMinutesPriceChange { get; set; }
        public decimal MinVolumeDeviationFromDailyAverage { get; set; }
        public decimal MinTenMinutesVolPercentChange { get; set; }
        public bool IsTelegramEnabled { get; set; }
        public bool CheckRockets { get; set; }

        public bool USAQuotesEnabled { get; set; }
        public string USAQuotesURL { get; set; }
        public string USAQuotesLogin { get; set; }
        public string USAQuotesPassword { get; set; }

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
                    TiApiKey, TgBotApiKey, TgChatId, 
                    MinDayPriceChange, MinTenMinutesPriceChange, 
                    MinVolumeDeviationFromDailyAverage, MinTenMinutesVolPercentChange,
                    IsTelegramEnabled, CheckRockets,
                    USAQuotesEnabled, USAQuotesURL, USAQuotesLogin, USAQuotesPassword
                };
                var obj = JsonConvert.DeserializeAnonymousType(text, definition);
                var config = new MapperConfiguration(cfg => 
                    cfg.CreateMap(obj.GetType(), this.GetType()));
                var mapper = new Mapper(config);
                mapper.Map(obj, this, obj.GetType(), this.GetType());
                try { this.TiApiKey = CryptoHelper.Decrypt(this.TiApiKey); } catch { }
                try { this.TgBotApiKey = CryptoHelper.Decrypt(this.TgBotApiKey); } catch { }
                try { this.TgChatId = CryptoHelper.Decrypt(this.TgChatId); } catch { }
                try { this.USAQuotesPassword = CryptoHelper.Decrypt(this.USAQuotesPassword); } catch { }

                if (MinDayPriceChange == 0)
                {
                    MinDayPriceChange = 0.08m;
                    MinVolumeDeviationFromDailyAverage = 0.002m;
                    MinTenMinutesPriceChange = 0.05m;
                    MinTenMinutesVolPercentChange = 0.07m;
                    CheckRockets = true;
                    IsTelegramEnabled = true;
                }
            }

            return this;
        }

        private object AnonymousSettingsObj => new 
        {
            TiApiKey = CryptoHelper.Encrypt(TiApiKey),
            TgBotApiKey = CryptoHelper.Encrypt(TgBotApiKey),
            TgChatId = CryptoHelper.Encrypt(TgChatId),
            USAQuotesPassword = CryptoHelper.Encrypt(USAQuotesPassword),
            MinDayPriceChange, MinTenMinutesPriceChange, 
            MinVolumeDeviationFromDailyAverage, MinTenMinutesVolPercentChange,
            IsTelegramEnabled, CheckRockets,
            USAQuotesEnabled, USAQuotesURL, USAQuotesLogin
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
