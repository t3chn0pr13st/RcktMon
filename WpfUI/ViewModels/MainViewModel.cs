using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AutoMapper;
using Caliburn.Micro;
using CoreData;
using CoreData.Interfaces;
using CoreData.Models;
using CoreNgine.Infra;
using CoreNgine.Models;
using CoreNgine.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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

        private DateTime _lastViewUpdate = DateTime.Now;
        private IStockModel _selectedStock;
        private HttpClient _wrapperClient;

        public IDictionary<string, IStockModel> Stocks { get; } = new ConcurrentDictionary<string, IStockModel>();
        public IDictionary<string, InstrumentInfo> Instruments => StocksManager.Instruments;
        public IEnumerable<IMessageModel> Messages { get; } = new ObservableCollection<MessageViewModel>();
        

        public StocksManager StocksManager => _stocksManager ??= _services.GetRequiredService<StocksManager>();
        public SettingsViewModel SettingsViewModel { get; }
        public StatusViewModel Status { get; }

        public ReleaseInfo LastRelease { get; private set; } = new ReleaseInfo();

        public AutoUpdate Updater { get; }

        public string UpdateLinkText { get; set; } = "Установить обновление";
        public bool UpdateInProgress { get; set; }

        public IServiceProvider Services => _services;

        public INgineSettings Settings => _settingsProvider.Settings;

        internal HttpClient WrapperClient => _wrapperClient ??= new HttpClient();

        public MainViewModel(IServiceProvider serviceProvider, ILogger<MainViewModel> logger, IEventAggregator2 eventAggregator, 
            ISettingsProvider settingsProvider, StatusViewModel status, AutoUpdate updater)
        {
            Updater = updater;
            _services = serviceProvider;
            _logger = logger;
            _uiContext = SynchronizationContext.Current;
            _settingsProvider = settingsProvider;
            _eventAggregator = eventAggregator;
            Status = status;

            LoadAppSettings();
            SettingsViewModel = new SettingsViewModel(_settingsProvider, this);
            CheckUpdates();
        }

        internal void LoadAppSettings()
        {
            _settingsProvider.ReadSettings();
        }

        internal void CheckUpdates()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        var releaseInfo = await Updater.GetLastRelease();
                        if (!releaseInfo.InvalidData)
                        {
                            if (LastRelease == null || releaseInfo.Version > LastRelease.Version)
                            {
                                LastRelease = releaseInfo;
                                NotifyOfPropertyChange(nameof(LastRelease));
                            }
                        }

                        await Task.Delay(50000);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Ошибка при проверке новой версии: {Error}", ex.Message);
                    }

                    await Task.Delay(5000);
                }
            });
        }

        public async Task InstallUpdate()
        {
            if (UpdateInProgress)
                return;
            UpdateInProgress = true;
            try
            {
                await Updater.InstallUpdate(LastRelease, status =>
                {
                    UpdateLinkText = status;
                });
            } catch (Exception ex)
            {
                AddMessage(MessageKind.Error, "ERROR", DateTime.Now, $"Ошибка при установке обновления: {ex.Message}");
            }

            UpdateLinkText = "Установить обновление";
            UpdateInProgress = false;
        }

        public IStockModel SelectedStock
        {
            get => _selectedStock;
            set
            {
                if (_selectedStock != value)
                {
                    _selectedStock = value;
                }
            }
        }

        public InstrumentInfo SelectedIstrument { get; set; }

        public void OpenInAurora(string ticker)
        {
            Task.Run(async () =>
            {
                if (!Stocks.TryGetValue(ticker, out var stock) ||
                    !stock.Currency.Equals("USD", StringComparison.InvariantCultureIgnoreCase)
                    || stock.Ticker == "TCS")
                    return;

                try
                {
                    var process = Process.GetProcessesByName("Terminal").FirstOrDefault();
                    if (process is null)
                        throw new InvalidOperationException("Аврора не запущена.");

                    process = Process.GetProcessesByName("AuroraWrapper").FirstOrDefault();
                    if (process is null)
                    {
                        process = Process.Start(AppContext.BaseDirectory + "\\AuroraWrapper.exe");
                        process.WaitForInputIdle();
                        await Task.Delay(1000);
                    }
                    await WrapperClient.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                        $"http://localhost:8000/?ticker={ticker}&group=ffd450"));
                }
                catch (Exception ex)
                {
                    AddMessage(MessageKind.Error, "ERROR", DateTime.Now, ex.Message);
                }
            });
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

        public IMessageModel AddMessage(MessageKind messageKind, string ticker, DateTime date, string text)
        {
            var message = new MessageViewModel()
            {
                MessageKind = messageKind,
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

        public IMessageModel AddMessage(MessageKind messageKind, string ticker, DateTime date, decimal change,
            decimal volume, string text)
        {
            var message = new MessageViewModel()
            {
                MessageKind = messageKind,
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
            _ = InitStocksManager();
        }

        public async Task OnStockUpdated(IStockModel stock)
        {
            if (stock.PriceUSA > 0 && stock.Price > 0)
                stock.DiffPercentUSA = (stock.PriceUSA - stock.Price) / stock.PriceUSA;

            if (stock.BestAskSpb > 0 && stock.BidUSA > 0)
                stock.USBidRUAskDiff = (stock.BidUSA - stock.BestAskSpb) / stock.BestAskSpb;

            if (stock.BestBidSpb > 0 && stock.AskUSA > 0)
                stock.RUBidUSAskDiff = (stock.BestBidSpb - stock.AskUSA) / stock.AskUSA;
            
            await _eventAggregator.PublishOnCurrentThreadAsync(stock);
        }

        public Task AddStocks(IEnumerable<IStockModel> stocks)
        {
            var tcs = new TaskCompletionSource();
            _uiContext.Post(obj =>
            {
                stocks.ToList().ForEach(s =>
                {
                    if (s.IsDead || s.Ticker.EndsWith("_old", StringComparison.InvariantCultureIgnoreCase))
                        return;
                    Stocks[s.Ticker] = s;
                });
                tcs.SetResult();
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Stocks)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Instruments)));
            }, null);

            return Task.WhenAll(_eventAggregator.PublishOnCurrentThreadAsync(stocks), tcs.Task);
        }

        private Task ExecuteOnUI(System.Action action)
        {
            var tcs = new TaskCompletionSource();
            _uiContext.Post(obj =>
            {
                action();
                tcs.SetResult();
            }, null);

            return tcs.Task;
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
                    success = false;
                    if (ex.Message.StartsWith("Unauthorized"))
                    {
                        AddMessage(MessageKind.Error, "ERROR", DateTime.Now, "Не удалось получить список инструментов: указан неверный токен Тинькофф Инвестиции (нет доступа)");
                        break;
                    }
                    else
                    {
                        AddMessage(MessageKind.Error, "ERROR", DateTime.Now, "Не удалось получить список инструментов: " + ex.Message);
                    }                    
                }
            }
        }

        public async Task RefreshAll()
        {
            await StocksManager.UpdateStocks(false);

            await ExecuteOnUI(() =>
            {
                foreach (var stock in Stocks.Values.ToList())
                {
                    if (stock.IsDead || stock.Ticker.EndsWith("_old", StringComparison.InvariantCultureIgnoreCase))
                        Stocks.Remove(stock.Ticker);
                }

                NotifyOfPropertyChange(nameof(Stocks));
            });

            StocksManager.SubscribeToStockEvents();
        }

    }
}
