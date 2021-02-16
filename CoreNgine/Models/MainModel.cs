using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CoreData;
using CoreData.Interfaces;
using CoreData.Models;
using CoreNgine.Infra;
using CoreNgine.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tinkoff.Trading.OpenApi.Models;

namespace CoreNgine.Models
{
    public class MainModel : IMainModel
    {
        public IDictionary<string, IStockModel> Stocks { get; } = new ConcurrentDictionary<string, IStockModel>();
        public IEnumerable<IMessageModel> Messages { get; } = new HashSet<MessageModel>();
        public StocksManager StocksManager { get; private set; }
        private IHandler<IStockModel> _stockUpdateHandler;
        private ILogger<MainModel> _logger;

        private IServiceProvider _services;
        private ISettingsProvider _settingsProvider;
        private IEventAggregator2 _eventAggregator;
        public INgineSettings Settings { get; private set; }

        public MainModel(IServiceProvider serviceProvider, ILogger<MainModel> logger, ISettingsProvider settingsProvider, IEventAggregator2 eventAggregator)
        {
            _services = serviceProvider;
            _logger = logger;
            _settingsProvider = settingsProvider;
            _eventAggregator = eventAggregator;
            
            LoadAppSettings();
        }

        public void Start()
        {
            InitStocksManager();
        }

        protected virtual void LoadAppSettings()
        {
            Settings = _settingsProvider.ReadSettings();
        }

        public virtual async Task OnStockUpdated(IStockModel stock)
        {
            await _eventAggregator.PublishOnCurrentThreadAsync(stock);
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

        public virtual async Task AddStocks(IEnumerable<IStockModel> stocks)
        {
            stocks.ToList().ForEach(s => { Stocks[s.Ticker] = s; });
            await _eventAggregator.PublishOnCurrentThreadAsync(stocks);
        }

        public async Task InitStocksManager()
        {
            await RefreshAll();
        }

        public async Task RefreshAll()
        {
            await StocksManager.UpdateStocks();
        }

        public async Task RefreshStocks()
        {
            await StocksManager.SubscribeToStockEvents();
        }
    }
}
