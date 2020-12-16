using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Telegram.Bot;
using Telegram.Bot.Types;

using Tinkoff.Trading.OpenApi.Models;
using Tinkoff.Trading.OpenApi.Network;

using TradeApp.ViewModels;
using static Tinkoff.Trading.OpenApi.Models.StreamingRequest;

namespace TradeApp.Data
{
    public class StocksManager
    {
        private readonly MainViewModel _tradingVM;
        private Connection _broker;
        private SandboxConnection _sandbox;
        private readonly SynchronizationContext _uiContext;
        private DateTime? _lastEventReceived = null;

        //private readonly Connection _broker2;
        //private readonly Context _account2;

        private readonly HashSet<string> _subscribedFigi = new HashSet<string>();
        private TelegramManager _telegram;

        private readonly ConcurrentQueue<BrokerAction> _brokerActions = new ConcurrentQueue<BrokerAction>();
        private Thread _brokerQueueThread;
        private Thread[] _responseProcessingThreads;

        public Connection Connection => _broker;
        public SandboxConnection SandboxConnection => _sandbox;
        public SandboxBot TradeBot { get; private set; }

        internal string TiApiToken => _tradingVM.TiApiKey;
        internal string TgBotToken => _tradingVM.TgBotApiKey;
        internal long TgChatId 
        {
            get
            {
                return long.TryParse(_tradingVM.TgChatId, out long result ) ? result : long.MinValue;
            }
        }

        internal decimal DayChangeTrigger => _tradingVM.MinDayPriceChange;
        internal decimal TenMinChangeTrigger => _tradingVM.MinTenMinutesPriceChange;

        internal bool IsTelegramEnabled => TgBotToken != null && _tradingVM.IsTelegramEnabled && _telegram != null;

        private void PrepareConnection()
        {
            if (_broker != null)
            {
                _broker.StreamingEventReceived -= Broker_StreamingEventReceived;
                _broker.Dispose();
            }

            _sandbox = ConnectionFactory.GetSandboxConnection(TiApiToken);
            _broker = ConnectionFactory.GetConnection(TiApiToken);
            _broker.StreamingEventReceived += Broker_StreamingEventReceived;
        }

        public void Init()
        {
            if (_telegram != null)
                _telegram.CancellationPending = true;

            if (TgBotToken != null && TgChatId > long.MinValue) 
                _telegram = new TelegramManager(TgBotToken, TgChatId) { IsEnabled = _tradingVM.IsTelegramEnabled };

            if (TiApiToken == null)
               return; 

            PrepareConnection();

            //TradeBot = new SandboxBot(this, _telegram);

            //_broker2 = ConnectionFactory.GetConnection("[token]");
            //_account2 = _broker2.Context;

            //_broker2.StreamingEventReceived += Broker_StreamingEventReceived;

            if (_brokerQueueThread == null) 
            {
                _brokerQueueThread = new Thread(BrokerQueueLoop)
                {
                    IsBackground = true,
                    Name = "BrokerActionQueueLoopThread"
                };
                _brokerQueueThread.Start();

                _responseProcessingThreads = new Thread[4];
                for (int i = 0; i < _responseProcessingThreads.Length; i++)
                {
                    _responseProcessingThreads[i] = new Thread(RespProcessingLoop)
                    {
                        IsBackground = true,
                        Name = $"ResponseProcessingLoopThread{i}"
                    };
                    _responseProcessingThreads[i].Start();
                }
            }
        }

        private void RespProcessingLoop()
        {
            while (true)
            {
                while (_candleProcessingQueue.TryDequeue(out var cr))
                {
                    try
                    {
                        CandleProcessingProc(cr).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Error while processing candle {candle}: {error}", cr, ex.Message);
                    }
                }
                Thread.Sleep(1);
            }
        }

        public StocksManager(MainViewModel tradingViewModel)
        {
            _tradingVM = tradingViewModel;
            _uiContext = SynchronizationContext.Current;
            Init();
        }

        private void QueueBrokerAction(Func<Connection, Task> act, string description)
        {
            var brAct = new BrokerAction(act, description);
            _brokerActions.Enqueue(brAct);
        }

        private volatile int _refreshPendingCount = 0;
        private DateTime? _lastRefresh = null;

        private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private void BrokerQueueLoop(object o)
        {
            if (TradeBot != null)
                TradeBot.Init().Wait();
            while (true)
            {
                while (_brokerActions.TryDequeue(out BrokerAction act))
                {
                    try
                    {
                        //var msg = $"{DateTime.Now} Выполнение операции '{act.Description}'...";
                        Logger.Trace("Выполнение операции {OperationDescription}", act.Description);
                        //Debug.WriteLine(msg);
                        var t = act.Action(_broker);
                        t.Wait();
                    }
                    catch (Exception ex)
                    {
                        //_brokerActions.Push(act);
                        var errorMsg = $"Ошибка при выполнении операции '{act.Description}': {ex.Message}";
                        LogError(errorMsg);
                        ResetConnection().Wait();
                    }
                }
                Thread.Sleep(1);
                if (_lastEventReceived != null && DateTime.Now.Subtract(_lastEventReceived.Value).TotalSeconds > 5)
                {
                    if (!_tradingVM.Stocks.Any(s => s.Status != null && s.Status != "not_available_for_trading") 
                        || (DateTime.Now.TimeOfDay.Hours < 10 && DateTime.Now.TimeOfDay.Hours > 2))
                    {
                        Thread.Sleep(1000);
                        continue;
                    }    
                    _lastEventReceived = null;
                    _brokerActions.Clear();
                    _subscribedFigi.Clear();
                    PrepareConnection();
                    var t = UpdatePrices();
                    t.Wait();
                }
                if (_refreshPendingCount > 0 && (_lastRefresh == null || DateTime.Now.Subtract(_lastRefresh.Value).TotalMilliseconds > 1000))
                {
                    Interlocked.Exchange(ref _refreshPendingCount, 0);
                    if (!_tradingVM.Stocks.IsNotifying)
                        _tradingVM.Stocks.Refresh();
                    //App.Current.Dispatcher.BeginInvoke((Action)(
                    //                () => _tradingVM.StocksCollectionView.Refresh()),
                    //                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    _lastRefresh = DateTime.Now;
                }
                Thread.Sleep(1);
            }
        }

        private void LogError(string msg)
        {
            _uiContext.Post(obj =>
            {
                _tradingVM.Messages.Add(new MessageViewModel
                {
                    Ticker = "ERROR",
                    Date = DateTime.Now,
                    Text = msg
                });
            }, null);
            Logger.Error(msg);
            //Debug.WriteLine(msg);
        }

        private async Task ResetConnection(string errorMsg)
        {
            LogError(errorMsg);
            await ResetConnection();
        }

        private async Task ResetConnection()
        {
            _lastEventReceived = null;
            _candleProcessingQueue.Clear();
            _brokerActions.Clear();
            _subscribedFigi.Clear();
            PrepareConnection();
            await UpdatePrices();
        }

        private ConcurrentQueue<CandleResponse> _candleProcessingQueue = new ConcurrentQueue<CandleResponse>();

        private async Task CandleProcessingProc(CandleResponse cr)
        {
            _lastEventReceived = DateTime.Now;
                var candle = cr.Payload;
                var stock = _tradingVM.Stocks.FirstOrDefault(s => s.Figi == candle.Figi);
                if (stock != null)
                {
                    stock.IsNotifying = false;
                    if (candle.Interval == CandleInterval.Day)
                    {
                        stock.TodayOpen = candle.Open;
                        stock.TodayDate = candle.Time;
                        stock.LastUpdate = DateTime.Now;
                        stock.Price = candle.Close;
                        if (stock.TodayOpen > 0)
                            stock.DayChange = (stock.Price - stock.TodayOpen) / stock.TodayOpen;
                        stock.DayVolume = candle.Volume;
                        Interlocked.Increment(ref _refreshPendingCount);
                        // отписываемся от дневного апдейта и подписываемся на минутную свечу
                        QueueBrokerAction(b => b.SendStreamingRequestAsync(
                            UnsubscribeCandle(stock.Figi, CandleInterval.Day)),
                            $"Отписка от часовой свечи {stock.Ticker} ({stock.Figi})");
                        QueueBrokerAction(b => b.SendStreamingRequestAsync(
                            SubscribeCandle(stock.Figi, CandleInterval.Minute)),
                            $"Подписка на минутную свечу {stock.Ticker} ({stock.Figi})");
                    }
                    else if (candle.Interval == CandleInterval.Minute)
                    {
                        if (candle.Time.Date > stock.TodayDate)
                        {
                            QueueBrokerAction(b => b.SendStreamingRequestAsync(
                               UnsubscribeCandle(stock.Figi, CandleInterval.Minute)),
                               $"Отписка от минутной свечи {stock.Ticker} ({stock.Figi})");
                            QueueBrokerAction(b => b.SendStreamingRequestAsync(
                                SubscribeCandle(stock.Figi, CandleInterval.Day)),
                                $"Подписка на дневную свечу {stock.Ticker} ({stock.Figi})");
                            return;
                        }
                       
                        stock.Price = candle.Close;
                        if (stock.TodayOpen > 0)
                            stock.DayChange = (stock.Price - stock.TodayOpen) / stock.TodayOpen;

                        stock.LastUpdate = DateTime.Now;
                        stock.LogCandle(candle);

                        if (TradeBot != null)
                            await TradeBot.Check(stock);

                        if (stock.DayChange > DayChangeTrigger
                            && (stock.LastAboveThreshholdDate == null
                            || stock.LastAboveThreshholdDate.Value.Date < stock.LastUpdate.Date))
                        {
                            stock.LastAboveThreshholdDate = stock.LastUpdate;

                            var infoReq = StreamingRequest.SubscribeInstrumentInfo(stock.Figi);
                            QueueBrokerAction(b => b.SendStreamingRequestAsync(infoReq),
                                $"Подписка на статус инструмента {stock.Ticker} ({stock.Figi})");

                            if (IsTelegramEnabled)
                                _telegram.PostMessage(stock.GetDayChangeInfoText(), stock.Ticker);

                            BackgroundInvoke(() =>
                            {
                                _tradingVM.Messages.Add(new MessageViewModel
                                {
                                    Ticker = stock.Ticker,
                                    Date = DateTime.Now,
                                    Change = stock.DayChange,
                                    Volume = candle.Volume,
                                    Text = $"Цена {stock.Ticker} изменилась на {stock.DayChange:P2} с начала дня."
                                });
                            });
                            Interlocked.Increment(ref _refreshPendingCount);
                        }

                        var change = stock.GetLast10MinChange(TenMinChangeTrigger);
                        if (Math.Abs(change.change) > TenMinChangeTrigger && stock.DayChange > TenMinChangeTrigger && (stock.LastAboveThreshholdCandleTime == null
                            || stock.LastAboveThreshholdCandleTime < candle.Time.AddMinutes(-change.minutes)))
                        {
                            stock.LastAboveThreshholdCandleTime = candle.Time;
                            try
                            {
                                await GetMonthStats(stock);
                            }
                            catch (Exception ex)
                            {
                                await ResetConnection("Ошибка при получении статистики за месяц: " + ex.Message);
                            }
                            if (IsTelegramEnabled)
                                _telegram.PostMessage(stock.GetMinutesChangeInfoText(change.change, change.minutes, change.candles), stock.Ticker);
                            _uiContext.Post(obj => {
                                _tradingVM.Messages.Add(new MessageViewModel
                                {
                                    Ticker = stock.Ticker,
                                    Date = DateTime.Now,
                                    Change = change.change,
                                    Volume = candle.Volume,
                                    Text = $"Цена {stock.Ticker} изменилась на {change.change:P2} за {change.minutes} мин."
                                });
                            }, null);

                            if (TradeBot != null && change.change > TenMinChangeTrigger && stock.DayChange < 0.2m && change.candles.Sum(c => c.Volume) > 100)
                                await TradeBot.Buy(stock);

                            Interlocked.Increment(ref _refreshPendingCount);
                        }
                    }
                    stock.IsNotifying = true;
                }
        }

        private void Broker_StreamingEventReceived(object sender, StreamingEventReceivedEventArgs e)
        {
            //Debug.WriteLine(JsonConvert.SerializeObject(e.Response));
            switch (e.Response)
            {
                case CandleResponse cr:
                    {
                        _candleProcessingQueue.Enqueue(cr);
                        break;
                    }

                case OrderbookResponse or:
                    {
                        var stock = _tradingVM.Stocks.FirstOrDefault(s => s.Figi == or.Payload.Figi);
                        if (stock != null && or.Payload.Asks.Count > 0 && or.Payload.Bids.Count > 0)
                        {
                            stock.Price = (or.Payload.Asks[0][0] + or.Payload.Bids[0][0]) / 2;
                            if (stock.TodayOpen > 0)
                            {
                                stock.DayChange = (stock.Price - stock.TodayOpen) / stock.TodayOpen;
                                stock.LastUpdate = or.Time.ToLocalTime();
                            }
                        }

                        break;
                    }

                case InstrumentInfoResponse ir:
                    {
                        var info = ir.Payload;
                        var stock = _tradingVM.Stocks.FirstOrDefault(s => s.Figi == info.Figi);
                        if (stock != null)
                        {
                            stock.Status = info.TradeStatus;
                        }

                        break;
                    }
            }
        }

        private async Task GetMonthStats(StockViewModel stock)
        {
            var prices = await _broker.Context.MarketCandlesAsync(stock.Figi,
                DateTime.Now.Date.AddMonths(-1),
                DateTime.Now.Date.AddDays(1), CandleInterval.Day);

            decimal monthVolume = 0, monthHigh = 0, monthLow = 0, monthAvgPrice = 0, 
                avgDayVolumePerMonth = 0, avgDayPricePerMonthCost = 0, monthOpen = -1,
                yesterdayVolume = 0, yesterdayMin = 0, yesterdayMax = 0, yesterdayAvgPrice = 0;

            var todayCandle = prices.Candles[^1];
            foreach (var candle in prices.Candles)
            {
                if (candle == todayCandle)
                {
                    stock.DayVolume = candle.Volume;
                }
                else
                {
                    if (monthOpen == -1)
                        monthOpen = candle.Open;
                    monthLow = monthLow == 0 ? candle.Low : Math.Min(monthLow, candle.Low);
                    monthHigh = monthHigh == 0 ? candle.High : Math.Min(monthHigh, candle.High);
                    monthVolume += candle.Volume;
                    avgDayPricePerMonthCost += (candle.High + candle.Low) / 2;
                    yesterdayVolume = candle.Volume;
                    yesterdayMin = candle.Low;
                    yesterdayMax = candle.High;
                }
                BackgroundInvoke(AddCandleToStock, Tuple.Create(stock, candle));
            }

            monthAvgPrice = (monthLow + monthHigh) / 2;
            yesterdayAvgPrice = (yesterdayMin + yesterdayMax) / 2;
            avgDayVolumePerMonth = monthVolume / (prices.Candles.Count - 1);
            avgDayPricePerMonthCost /= prices.Candles.Count - 1;
            
            stock.MonthOpen = monthOpen;
            stock.MonthHigh = monthHigh;
            stock.MonthLow = monthLow;
            stock.MonthVolume = monthVolume;
            stock.MonthVolumeCost = monthVolume * monthAvgPrice;
            stock.AvgDayVolumePerMonth = Math.Round(avgDayVolumePerMonth);
            stock.AvgDayPricePerMonth = avgDayPricePerMonthCost;
            stock.AvgDayVolumePerMonthCost = avgDayPricePerMonthCost * avgDayVolumePerMonth;
            stock.YesterdayVolume = yesterdayVolume;
            stock.YesterdayVolumeCost = yesterdayVolume * yesterdayAvgPrice;
            stock.YesterdayAvgPrice = yesterdayAvgPrice;
        }

        public async Task UpdatePrices()
        {
            //var count = _tradingVM.Stocks.Count(s => s.Status != null);
            foreach (var stock in _tradingVM.Stocks)
            {
                //var prices = await _account.MarketCandlesAsync(stock.Figi, 
                //    DateTime.Now.Date.AddHours(10),
                //    DateTime.Now.AddMinutes(1), CandleInterval.Hour);

                //CandlePayload lastCandle = null;
                //CandlePayload firstCandle = null;
                //foreach (var candle in prices.Candles)
                //{
                //    if (firstCandle == null)
                //        firstCandle = candle;
                //    lastCandle = candle;
                //    _uiContext.Post(AddCandleToStock, Tuple.Create(stock, candle));
                //}
                //if (firstCandle != null)
                //{
                //    stock.TodayOpen = firstCandle.Open;
                //    stock.Price = lastCandle.Close;
                //    stock.DayChange = (lastCandle.Close - firstCandle.Open) / firstCandle.Open;
                //}

                if (!_subscribedFigi.Contains(stock.Figi))
                {
                    var request = new CandleSubscribeRequest(stock.Figi, CandleInterval.Day);
                    QueueBrokerAction(b => b.SendStreamingRequestAsync(request),
                        $"Подписка на дневную свечу {stock.Ticker} ({stock.Figi})");

                    //var req2 = new OrderbookSubscribeRequest(stock.Figi, 2);
                    //await _broker.SendStreamingRequestAsync(req2);

                    //var infoReq = StreamingRequest.SubscribeInstrumentInfo(stock.Figi);
                    //QueueBrokerAction(b => b.SendStreamingRequestAsync(infoReq),
                    //    $"Подписка на статус инструмента {stock.Ticker} ({stock.Figi})");

                    //await Task.Delay(100);

                    _subscribedFigi.Add(stock.Figi);
                }
            }
            await Task.CompletedTask;
        }

        private void AddCandleToStock(object data)
        {
            if (data is Tuple<StockViewModel, CandlePayload> stocandle)
            {
                var stock = stocandle.Item1;
                var candle = stocandle.Item2;
                if (stock.Candles.Any(c => c.Time == candle.Time && c.Interval == candle.Interval))
                    return;
                stock.Candles.Add(new CandleViewModel()
                {
                    Interval = candle.Interval,
                    Open = candle.Open,
                    Close = candle.Close,
                    Low = candle.Low,
                    High = candle.High,
                    Time = candle.Time
                });
                //stock.LastUpdate = DateTime.Now;
            }
        }

        private void BackgroundInvoke<T>(Action<T> act, T arg, DispatcherPriority prio = DispatcherPriority.Background)
        {
            App.Current.Dispatcher.BeginInvoke(act, prio, arg);
        }

        private void BackgroundInvoke(Action act, DispatcherPriority prio = DispatcherPriority.Background)
        {
            App.Current.Dispatcher.BeginInvoke(act, prio);
        }

        public async Task UpdateStocks()
        {
            if (_broker == null)
                return;
            var stocks = await _broker.Context.MarketStocksAsync();
            var stocksToAdd = new HashSet<StockViewModel>();
            foreach (var instr in stocks.Instruments)
            {
                var stock = _tradingVM.Stocks.FirstOrDefault(s => s.Figi == instr.Figi);
                if (stock == null)
                {
                    stock = new StockViewModel()
                    {
                        Figi = instr.Figi,
                        Name = instr.Name,
                        Ticker = instr.Ticker,
                        Currency = instr.Currency.ToString()
                    };
                    stocksToAdd.Add(stock);
                }
            }
            _tradingVM.Stocks.AddRange(stocksToAdd);
            await UpdatePrices();
            _tradingVM.IsNotifying = false;
        }
    }
}
