using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Caliburn.Micro;
using CoreData.Interfaces;
using CoreNgine.Models;
using CoreNgine.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RcktMon.Helpers;
using Tinkoff.Trading.OpenApi.Models;

namespace RcktMon.ViewModels
{
    public class MainViewModel : PropertyChangedBase, IMainModel
    {
        public IEnumerable<IStockModel> Stocks { get; } = new ObservableCollection<StockViewModel>();
        public IEnumerable<IMessageModel> Messages { get; } = new ObservableCollection<MessageViewModel>();
        public StocksManager StocksManager { get; private set; }
        public SettingsViewModel SettingsViewModel { get; }

        private ILogger<MainViewModel> _logger;
        
        private readonly SynchronizationContext _uiContext;

        #region App Settings 
        
        public string TiApiKey { get; set; }
        public string TgBotApiKey { get; set; }
        public string TgChatId { get; set; }
        public decimal MinDayPriceChange { get; set; }
        public decimal MinTenMinutesPriceChange { get; set; }
        public decimal MinVolumeDeviationFromDailyAverage { get; set; }
        public bool IsTelegramEnabled { get; set; }

        #endregion App Settings

        private IEventAggregator _eventAggregator;
        private IServiceProvider _services;

        public MainViewModel(IServiceProvider serviceProvider, IEventAggregator eventAggregator, ILogger<MainViewModel> logger)
        {
            _services = serviceProvider;
            _eventAggregator = eventAggregator;
            _logger = logger;
            _uiContext = SynchronizationContext.Current;

            LoadAppSettings();
            SettingsViewModel = new SettingsViewModel(this);
        }

        internal void LoadAppSettings()
        {
            MinDayPriceChange = 0.1m;
            MinVolumeDeviationFromDailyAverage = 0.002m;
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

        public IStockModel CreateStockModel(MarketInstrument instrument)
        {
            return new StockViewModel {
                Figi = instrument.Figi,
                Isin = instrument.Isin,
                Name = instrument.Name,
                Lot = instrument.Lot,
                MinPriceIncrement = instrument.MinPriceIncrement,
                Ticker = instrument.Ticker,
                Currency = instrument.Currency.ToString()
            };
        }

        public IMessageModel AddMessage(string ticker, DateTime date, string text)
        {
            var message = new MessageViewModel()
            {
                Ticker = ticker,
                Date = date,
                Text = text
            };
            _uiContext.Post(obj =>
            {
                (Messages as ObservableCollection<MessageViewModel>)?.Add(message);
            }, null);
            return message;
        }

        public IMessageModel AddMessage(string ticker, DateTime date, decimal change,
            decimal volume, string text)
        {
            var message = new MessageViewModel()
            {
                Ticker = ticker,
                Date = date,
                Change = change,
                Volume = volume,
                Text = text
            };
            _uiContext.Post(obj =>
            {
                (Messages as ObservableCollection<MessageViewModel>)?.Add(message);
            }, null);
            return message;
        }

        public void Start()
        {
            InitStocksManager();
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
                _logger.LogCritical(ex, ex.Message);
            }
        }

        public Task AddStocks(IEnumerable<IStockModel> stocks)
        {
            var tcs = new TaskCompletionSource();
            _uiContext.Post(obj =>
            {
                stocks.OfType<StockViewModel>().ToList().ForEach(s =>
                {
                    (Stocks as ObservableCollection<StockViewModel>)?.Add(s);
                });
                tcs.SetResult();
            }, null);
            return tcs.Task;
        }

        public async Task InitStocksManager()
        {
            await Task.Delay(1000);
            if (StocksManager == null)
                StocksManager = _services.GetRequiredService<StocksManager>();
            await RefreshAll();
        }

        public async Task RefreshAll()
        {
            await StocksManager.UpdateStocks();
        }

        public async Task RefreshStocks()
        {
            await StocksManager.UpdatePrices();
        }
    }
}
