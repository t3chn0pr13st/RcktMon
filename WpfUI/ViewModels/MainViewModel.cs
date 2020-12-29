using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using AutoMapper;
using Caliburn.Micro;
using Newtonsoft.Json;
using RcktMon.Managers;
using TradeApp.Data;
using TradeApp.ViewModels;

namespace TradeApp
{
    public class MainViewModel : PropertyChangedBase
    {
        public BindableCollection<StockViewModel> Stocks { get; } = new BindableCollection<StockViewModel>();
        public BindableCollection<MessageViewModel> Messages { get; } = new BindableCollection<MessageViewModel>();
        public StocksManager StocksManager { get; private set; }
        public SettingsViewModel SettingsViewModel { get; } 

        #region App Settings 
        
        public string TiApiKey { get; set; }
        public string TgBotApiKey { get; set; }
        public string TgChatId { get; set; }
        public decimal MinDayPriceChange { get; set; }
        public decimal MinTenMinutesPriceChange { get; set; }
        public bool IsTelegramEnabled { get; set; }

        #endregion App Settings

        private IEventAggregator _eventAggregator;

        public MainViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            LoadAppSettings();
            SettingsViewModel = new SettingsViewModel(this);
            StocksManager = new StocksManager(this);
        }

        internal void LoadAppSettings()
        {
            MinDayPriceChange = 0.1m;
            MinTenMinutesPriceChange = 0.05m;
            if (File.Exists("settings.json"))
            {
                var text = File.ReadAllText("settings.json");
                var definition = new 
                {
                    TiApiKey, TgBotApiKey, TgChatId, 
                    MinDayPriceChange, MinTenMinutesPriceChange, IsTelegramEnabled
                };
                var obj = JsonConvert.DeserializeAnonymousType(text, definition);
                var config = new MapperConfiguration(cfg => 
                    cfg.CreateMap(obj.GetType(), this.GetType()));
                var mapper = new Mapper(config);
                mapper.Map(obj, this, obj.GetType(), this.GetType());
                try { this.TiApiKey = CryptoHelper.Decrypt(this.TiApiKey); } catch { }
                try { this.TgBotApiKey = CryptoHelper.Decrypt(this.TgBotApiKey); } catch { }
                try { this.TgChatId = CryptoHelper.Decrypt(this.TgChatId); } catch { }
            }
        }

        private object AnonymousSettingsObj => new 
        {
            TiApiKey = CryptoHelper.Encrypt(TiApiKey),
            TgBotApiKey = CryptoHelper.Encrypt(TgBotApiKey),
            TgChatId = CryptoHelper.Encrypt(TgChatId),
            MinDayPriceChange, MinTenMinutesPriceChange, IsTelegramEnabled
        };

        internal void SaveAppSettings()
        {
            try 
            {
                System.IO.File.WriteAllText("settings.json", 
                    JsonConvert.SerializeObject(AnonymousSettingsObj, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        public void UpdateBalanceInfo()
        {

        }

        public async Task RefreshStocks()
        {
            await StocksManager.UpdatePrices();
        }
    }
}
