using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CoreData.Interfaces;
using CoreData.Models;
using CoreNgine.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tinkoff.Trading.OpenApi.Models;

namespace CoreNgine.Models
{
    public class MainModel : IMainModel
    {
        public IEnumerable<IStockModel> Stocks { get; } = new HashSet<StockModel>();
        public IEnumerable<IMessageModel> Messages { get; } = new HashSet<MessageModel>();
        public StocksManager StocksManager { get; private set; }

        private ILogger<MainModel> _logger;

        #region App Settings 
        
        public string TiApiKey { get; set; }
        public string TgBotApiKey { get; set; }
        public string TgChatId { get; set; }
        public decimal MinDayPriceChange { get; set; }
        public decimal MinTenMinutesPriceChange { get; set; }
        public decimal MinVolumeDeviationFromDailyAverage { get; set; } = 0.01m;
        public bool IsTelegramEnabled { get; set; }

        #endregion App Settings

        private IServiceProvider _services;

        public MainModel(IServiceProvider serviceProvider, ILogger<MainModel> logger)
        {
            _services = serviceProvider;
            _logger = logger;
            
            LoadAppSettings();
        }

        public void Start()
        {
            InitStocksManager();
        }

        protected virtual void LoadAppSettings()
        {

        }

        public virtual IStockModel CreateStockModel(MarketInstrument instrument)
        {
            return new StockModel {
                Figi = instrument.Figi,
                Isin = instrument.Isin,
                Name = instrument.Name,
                Lot = instrument.Lot,
                MinPriceIncrement = instrument.MinPriceIncrement,
                Ticker = instrument.Ticker,
                Currency = instrument.Currency.ToString()
            };
        }

        public virtual IMessageModel AddMessage(string ticker, DateTime date, string text)
        {
            var message = new MessageModel()
            {
                Ticker = ticker,
                Date = date,
                Text = text
            };
            (Messages as HashSet<MessageModel>)?.Add(message);
            return message;
        }

        public virtual IMessageModel AddMessage(string ticker, DateTime date, decimal change, decimal volume, string text)
        {
            var message = new MessageModel()
            {
                Ticker = ticker,
                Date = date,
                Change = change,
                Volume = volume,
                Text = text
            };
            (Messages as HashSet<MessageModel>)?.Add(message);
            return message;
        }

        public Task AddStocks(IEnumerable<IStockModel> stocks)
        {
            stocks.OfType<StockModel>().ToList().ForEach(s => { (Stocks as HashSet<StockModel>)?.Add(s); });
            return Task.CompletedTask;
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
