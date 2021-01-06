using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using CoreData;
using CoreData.Interfaces;
using CoreNgine.Data;
using CoreNgine.Models;
using Microsoft.Extensions.Logging;
using Tinkoff.Trading.OpenApi.Models;
using Tinkoff.Trading.OpenApi.Network;
using static Tinkoff.Trading.OpenApi.Models.StreamingRequest;

namespace CoreNgine.Shared
{
    public class StocksManager
    {
        private readonly IMainModel _mainModel;
        private Connection _broker;
        private SandboxConnection _sandbox;
        private DateTime? _lastEventReceived = null;

        private volatile int _refreshPendingCount = 0;
        private DateTime? _lastRefresh = null;
        private readonly ILogger<StocksManager> _logger;

        //private readonly Connection _broker2;
        //private readonly Context _account2;

        private readonly HashSet<string> _subscribedFigi = new HashSet<string>();
        private TelegramManager _telegram;

        private readonly ConcurrentQueue<BrokerAction> _brokerActions = new ConcurrentQueue<BrokerAction>();
        private ConcurrentQueue<CandleResponse> _candleProcessingQueue = new ConcurrentQueue<CandleResponse>();
        private Task _brokerQueueTask;
        private Task[] _responseProcessingTasks;

        public Connection Connection => _broker;
        public SandboxConnection SandboxConnection => _sandbox;
        public SandboxBot TradeBot { get; private set; }

        internal string TiApiToken => _mainModel.TiApiKey;
        internal string TgBotToken => _mainModel.TgBotApiKey;
        internal long TgChatId 
        {
            get
            {
                return long.TryParse(_mainModel.TgChatId, out long result ) ? result : long.MinValue;
            }
        }

        internal decimal DayChangeTrigger => _mainModel.MinDayPriceChange;
        internal decimal TenMinChangeTrigger => _mainModel.MinTenMinutesPriceChange;

        internal bool IsTelegramEnabled => TgBotToken != null && _mainModel.IsTelegramEnabled && _telegram != null;

        private readonly IServiceProvider _services;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        
        public StocksManager(IServiceProvider services, IMainModel mainModel, ILogger<StocksManager> logger)
        {
            _services = services;
            _mainModel = mainModel;
            _logger = logger;

            Init();
        }

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
                _telegram.Stop();

            if (TgBotToken != null && TgChatId > long.MinValue) 
                _telegram = new TelegramManager(_services, TgBotToken, TgChatId) { IsEnabled = _mainModel.IsTelegramEnabled };

            if (TiApiToken == null)
               return; 

            PrepareConnection();

            //TradeBot = new SandboxBot(this, _telegram);

            //_broker2 = ConnectionFactory.GetConnection("[token]");
            //_account2 = _broker2.Context;

            //_broker2.StreamingEventReceived += Broker_StreamingEventReceived;

            if (_brokerQueueTask == null)
            {
                _brokerQueueTask = Task.Factory
                    .StartNew(() => BrokerQueueLoopAsync()
                            .ConfigureAwait(false),
                    _cancellationTokenSource.Token,
                    TaskCreationOptions.LongRunning, 
                    TaskScheduler.Default);

                _responseProcessingTasks = new Task[1];
                for (int i = 0; i < _responseProcessingTasks.Length; i++)
                {
                    _responseProcessingTasks[i] = Task.Factory
                        .StartNew(() => RespProcessingLoopAsync()
                                .ConfigureAwait(false),
                            _cancellationTokenSource.Token,
                            TaskCreationOptions.LongRunning, 
                            TaskScheduler.Default);
                }
            }
        }

        private async Task RespProcessingLoopAsync()
        {
            var cancellationToken = _cancellationTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                while (_candleProcessingQueue.TryDequeue(out var cr))
                {
                    try
                    {
                        await CandleProcessingProc(cr);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error while processing candle {candle}: {error}", cr, ex.Message);
                    }
                }

                await Task.Delay(100);
            }
        }

        private void QueueBrokerAction(Func<Connection, Task> act, string description)
        {
            var brAct = new BrokerAction(act, description);
            _brokerActions.Enqueue(brAct);
        }

        private async Task BrokerQueueLoopAsync()
        {
            var cancellationToken = _cancellationTokenSource.Token;
            if (TradeBot != null)
                TradeBot.Init().Wait();

            while (!cancellationToken.IsCancellationRequested)
            {
                while (_brokerActions.TryDequeue(out BrokerAction act))
                {
                    try
                    {
                        //var msg = $"{DateTime.Now} Выполнение операции '{act.Description}'...";
                        _logger.LogTrace("Выполнение операции {OperationDescription}", act.Description);
                        //Debug.WriteLine(msg);
                        await act.Action(_broker);
                    }
                    catch (Exception ex)
                    {
                        //_brokerActions.Push(act);
                        var errorMsg = $"Ошибка при выполнении операции '{act.Description}': {ex.Message}";
                        LogError(errorMsg);
                        await ResetConnection();
                    }
                }

                if (_lastEventReceived != null && DateTime.Now.Subtract(_lastEventReceived.Value).TotalSeconds > 5)
                {
                    if (!_mainModel.Stocks.Any(s => s.Status != null && s.Status != "not_available_for_trading") 
                        || (DateTime.Now.TimeOfDay.Hours < 10 && DateTime.Now.TimeOfDay.Hours > 2))
                    {
                        Thread.Sleep(1000);
                        continue;
                    }    
                    _lastEventReceived = null;
                    _brokerActions.Clear();
                    _subscribedFigi.Clear();
                    PrepareConnection();
                    await UpdatePrices();
                }

                await Task.Delay(100);
            }
        }

        private void LogError(string msg)
        {
            _mainModel.AddMessage(
                ticker: "ERROR",
                date: DateTime.Now,
                text: msg
            );
            _logger.LogError(msg);
            //Debug.WriteLine(msg);
        }

        private async Task ResetConnection(string errorMsg)
        {
            LogError("Переподключение: " + errorMsg);
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

        private async Task CandleProcessingProc(CandleResponse cr)
        {
            _lastEventReceived = DateTime.Now;
                var candle = cr.Payload;
                var stock = _mainModel.Stocks.FirstOrDefault(s => s.Figi == candle.Figi);
                if (stock != null)
                {
                    //stock.IsNotifying = false;
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
                            && (stock.LastAboveThresholdDate == null
                            || stock.LastAboveThresholdDate.Value.Date < stock.LastUpdate.Date))
                        {
                            stock.LastAboveThresholdDate = stock.LastUpdate;

                            var infoReq = StreamingRequest.SubscribeInstrumentInfo(stock.Figi);
                            QueueBrokerAction(b => b.SendStreamingRequestAsync(infoReq),
                                $"Подписка на статус инструмента {stock.Ticker} ({stock.Figi})");

                            if (IsTelegramEnabled && stock.DayVolume > 10)
                                _telegram.PostMessage(stock.GetDayChangeInfoText(), stock.Ticker);

                            _mainModel.AddMessage(
                                stock.Ticker,
                                DateTime.Now,
                                stock.DayChange,
                                candle.Volume,
                                $"{stock.Ticker}: {stock.DayChange:P2} с начала дня ({stock.TodayOpenF} → {stock.PriceF})"
                            );

                            Interlocked.Increment(ref _refreshPendingCount);
                        }

                        var change = stock.GetLast10MinChange(TenMinChangeTrigger);
                        if (Math.Abs(change.change) > TenMinChangeTrigger && stock.DayChange > TenMinChangeTrigger && (stock.LastAboveThresholdCandleTime == null
                            || stock.LastAboveThresholdCandleTime < candle.Time.AddMinutes(-change.minutes)))
                        {
                            stock.LastAboveThresholdCandleTime = candle.Time;
                            try
                            {
                                await GetMonthStats(stock);
                            }
                            catch (Exception ex)
                            {
                                await ResetConnection("Ошибка при получении статистики за месяц: " + ex.Message);
                            }

                            if (IsTelegramEnabled)
                            {
                                var changeInfo = stock.GetMinutesChangeInfo(change.change, change.minutes, change.candles);
                                if (changeInfo.volPercent / 100m >= _mainModel.MinVolumeDeviationFromDailyAverage)
                                    _telegram.PostMessage(changeInfo.message, stock.Ticker);
                            }

                            _mainModel.AddMessage(
                                stock.Ticker,
                                DateTime.Now,
                                stock.DayChange,
                                candle.Volume,
                                $"{stock.Ticker}: {change.change:P2} за {change.minutes} мин. ({change.candles[^1].Open.FormatPrice(stock.Currency),2} → {change.candles[0].Close.FormatPrice(stock.Currency), -2}) "
                            );

                            if (TradeBot != null && change.change > TenMinChangeTrigger && stock.DayChange < 0.2m && change.candles.Sum(c => c.Volume) > 100)
                                await TradeBot.Buy(stock);

                            Interlocked.Increment(ref _refreshPendingCount);
                        }
                    }
                    //stock.IsNotifying = true;
                }
        }

        private async void Broker_StreamingEventReceived(object sender, StreamingEventReceivedEventArgs e)
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
                        var stock = _mainModel.Stocks.FirstOrDefault(s => s.Figi == or.Payload.Figi);
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
                        var stock = _mainModel.Stocks.FirstOrDefault(s => s.Figi == info.Figi);
                        if (stock != null)
                        {
                            stock.Status = info.TradeStatus;
                        }

                        break;
                    }
            }
        }

        private async Task GetMonthStats(IStockModel stock)
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
                AddCandleToStock(Tuple.Create(stock, candle));
            }

            monthAvgPrice = (monthLow + monthHigh) / 2;
            yesterdayAvgPrice = (yesterdayMin + yesterdayMax) / 2;
            avgDayVolumePerMonth = monthVolume / (prices.Candles.Count - 1);
            avgDayPricePerMonthCost /= prices.Candles.Count - 1;
            
            stock.MonthOpen = monthOpen;
            stock.MonthHigh = monthHigh;
            stock.MonthLow = monthLow;
            stock.MonthVolume = monthVolume;
            stock.MonthVolumeCost = monthVolume * monthAvgPrice * stock.Lot;
            stock.AvgDayVolumePerMonth = Math.Round(avgDayVolumePerMonth);
            stock.AvgDayPricePerMonth = avgDayPricePerMonthCost;
            stock.AvgDayVolumePerMonthCost = avgDayPricePerMonthCost * avgDayVolumePerMonth * stock.Lot;
            stock.YesterdayVolume = yesterdayVolume;
            stock.YesterdayVolumeCost = yesterdayVolume * yesterdayAvgPrice;
            stock.YesterdayAvgPrice = yesterdayAvgPrice;
        }

        public async Task UpdatePrices()
        {
            foreach (var stock in _mainModel.Stocks)
            {
                if (!_subscribedFigi.Contains(stock.Figi))
                {
                    var request = new CandleSubscribeRequest(stock.Figi, CandleInterval.Day);
                    QueueBrokerAction(b => b.SendStreamingRequestAsync(request),
                        $"Подписка на дневную свечу {stock.Ticker} ({stock.Figi})");

                    _subscribedFigi.Add(stock.Figi);
                }
            }
            await Task.CompletedTask;
        }

        private void AddCandleToStock(object data)
        {
            if (data is Tuple<IStockModel, CandlePayload> stocandle)
            {
                var stock = stocandle.Item1;
                var candle = stocandle.Item2;
                if (stock.Candles.Any(c => c.Time == candle.Time && c.Interval == candle.Interval))
                    return;
                stock.AddCandle(candle);
                //stock.LastUpdate = DateTime.Now;
            }
        }

        public async Task UpdateStocks()
        {
            if (_broker == null)
                return;
            var stocks = await _broker.Context.MarketStocksAsync();
            var stocksToAdd = new HashSet<IStockModel>();
            foreach (var instr in stocks.Instruments)
            {
                var stock = _mainModel.Stocks.FirstOrDefault(s => s.Figi == instr.Figi);
                if (stock == null)
                {
                    stock = _mainModel.CreateStockModel(instr);
                    stocksToAdd.Add(stock);
                }
            }
            await _mainModel.AddStocks(stocksToAdd);
            await UpdatePrices();
            //_mainModel.IsNotifying = false;
        }
    }
}
