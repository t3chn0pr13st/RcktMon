using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using AutoMapper;
using Caliburn.Micro;
using CoreData;
using CoreData.Interfaces;
using CoreNgine.Infra;
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
        private StocksManager _stocksManager;
        private ILogger<MainViewModel> _logger;
        private ISettingsProvider _settingsProvider;
        private IEventAggregator2 _eventAggregator;
        private readonly SynchronizationContext _uiContext;
        private IServiceProvider _services;

        public IDictionary<string, IStockModel> Stocks { get; } = new ConcurrentDictionary<string, IStockModel>();
        public IEnumerable<IMessageModel> Messages { get; } = new ObservableCollection<MessageViewModel>();

        public StocksManager StocksManager =>
            _stocksManager ?? (_stocksManager = _services.GetRequiredService<StocksManager>());
        public SettingsViewModel SettingsViewModel { get; }
        public StatusViewModel Status { get; }

        public INgineSettings Settings => _settingsProvider.Settings;

        public MainViewModel(IServiceProvider serviceProvider, ILogger<MainViewModel> logger, IEventAggregator2 eventAggregator, ISettingsProvider settingsProvider, StatusViewModel status)
        {
            _services = serviceProvider;
            _logger = logger;
            _uiContext = SynchronizationContext.Current;
            _settingsProvider = settingsProvider;
            _eventAggregator = eventAggregator;
            Status = status;

            LoadAppSettings();
            SettingsViewModel = new SettingsViewModel(_settingsProvider, this);
        }

        internal void LoadAppSettings()
        {
            _settingsProvider.ReadSettings();
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

        private DateTime _lastViewUpdate = DateTime.Now;

        public async Task OnStockUpdated(IStockModel stock)
        {
            if (stock.PriceUSA > 0 && stock.Price > 0)
                stock.DiffPercentUSA = (stock.PriceUSA - stock.Price) / stock.PriceUSA;
            await _eventAggregator.PublishOnCurrentThreadAsync(stock);
            //if (DateTime.Now.Subtract(_lastViewUpdate).TotalMilliseconds > 500)
            //{
            //    _lastViewUpdate = DateTime.Now;
            //    _uiContext.Post(obj =>
            //    {
            //        CollectionViewSource.GetDefaultView(Stocks.Values).Refresh();
            //    }, null);
            //}
        }

        public Task AddStocks(IEnumerable<IStockModel> stocks)
        {
            var tcs = new TaskCompletionSource();
            _uiContext.Post(obj =>
            {
                stocks.ToList().ForEach(s =>
                {
                    Stocks[s.Ticker] = s;
                });
                tcs.SetResult();
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Stocks)));
            }, null);

            return Task.WhenAll(_eventAggregator.PublishOnCurrentThreadAsync(stocks), tcs.Task);
        }

        public async Task InitStocksManager()
        {
            bool success = false;
            while (!success)
            {
                await Task.Delay(1000);
                try
                {
                    await RefreshAll();
                    success = true;
                }
                catch (Exception ex)
                {
                    AddMessage("ERR", DateTime.Now, "Не удалось получить список инструментов: " + ex.Message);
                    success = false;
                }
            }
            //var l2data = _services.GetRequiredService<L2DataConnector>();
            //await l2data.ConnectAsync();
            //var message = "+[" + String.Join(",", Stocks.Where(s => s.Currency == "Usd").Select(s => s.Ticker)) + "]";
            //await l2data.SendMessageAsync(message);
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
