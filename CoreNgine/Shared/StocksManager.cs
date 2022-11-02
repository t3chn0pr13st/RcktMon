using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CoreData;
using CoreData.Interfaces;
using CoreData.Models;
using CoreData.Settings;
using CoreNgine.Data;
using CoreNgine.Infra;
using CoreNgine.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tinkoff.Trading.OpenApi.Legacy.Models;
using Tinkoff.Trading.OpenApi.Legacy.Network;
using static CoreData.Models.TinkoffStocksInfoCollection;
using static Tinkoff.Trading.OpenApi.Legacy.Models.StreamingRequest;

namespace CoreNgine.Shared
{
    public readonly struct ExchangeStatus
    {
        public string Exchange { get; }
        public string Status { get; }

        public ExchangeStatus(string exchange, string status)
        {
            Exchange = exchange;
            Status = status;
        }
    }

    public class StocksManager : IHandler<SettingsChangeEventArgs>
    {
        private readonly IMainModel _mainModel;
        private DateTime? _lastEventReceived = null;
        private DateTime _lastSubscriptionCheck = DateTime.Now;
        private int _recentUpdatedStocksCount;
        private DateTime? _lastRestartTime;
        private DateTime _lastUIUpdate;

        private readonly ILogger<StocksManager> _logger;

        private TinkoffStocksInfoCollection _tinkoffStocksInfoCollection = new TinkoffStocksInfoCollection();
        private readonly ConcurrentHashSet<string> _subscribedFigi = new ConcurrentHashSet<string>();
        private readonly ConcurrentHashSet<string> _subscribedMinuteFigi = new ConcurrentHashSet<string>();
        private TelegramManager _telegram;

        private readonly ConcurrentQueue<BrokerAction> CommonConnectionActions = new ConcurrentQueue<BrokerAction>();
        private ConcurrentQueue<object> _stockProcessingQueue = new ConcurrentQueue<object>();
        private ConcurrentQueue<IStockModel> _monthStatsQueue = new ConcurrentQueue<IStockModel>();
        private Task _monthStatsTask;
        private Task CommonConnectionQueueTask;
        private Task[] _responseProcessingTasks;

        internal Connection CommonConnection { get; set; }
        internal Connection CandleConnection { get; set; }
        internal Connection InstrumentInfoConnection { get; set; }

        public ExchangeStatus[] ExchangeStatus { get; private set; }

        internal string TiApiToken => Settings.TiApiKey;
        internal string TgBotToken => Settings.TgBotApiKey;
        internal long TgChatId 
        {
            get
            {
                return long.TryParse(Settings.TgChatId, out long result ) ? result : long.MinValue;
            }
        }

        public IDictionary<string, OrderbookModel> OrderbookInfoSpb { get; } =
            new ConcurrentDictionary<string, OrderbookModel>();

        public ConcurrentDictionary<string, InstrumentInfo> Instruments { get; } =
            new ConcurrentDictionary<string, InstrumentInfo>();

        public ConcurrentDictionary<string, IStockModel> ActiveStocks { get; } = new ConcurrentDictionary<string, IStockModel>();

        public DateTime? LastRestartTime => _lastRestartTime;

        public DateTime LastInstrumentsUpdate { get; private set; }

        public TimeSpan ElapsedFromLastRestart => DateTime.Now.Subtract(_lastRestartTime ?? DateTime.Now);

        public TelegramManager Telegram => _telegram;

        private readonly IServiceProvider _services;
        private readonly ISettingsProvider _settingsProvider;

        public IEventAggregator2 EventAggregator { get; }

        public INgineSettings Settings => _settingsProvider.Settings;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public void Shutdown()
        {
            _cancellationTokenSource.Cancel();
            Telegram.Stop();
        }

        public StocksManager(IServiceProvider services, IMainModel mainModel, ILogger<StocksManager> logger, 
            ISettingsProvider settingsProvider, IEventAggregator2 eventAggregator)
        {
            _settingsProvider = settingsProvider;
            _services = services;
            _mainModel = mainModel;
            _logger = logger;
            EventAggregator = eventAggregator;
            EventAggregator.SubscribeOnBackgroundThread(this);

            Init();
        }

        private void PrepareConnection()
        {
            if (CommonConnection != null)
            {
                _stockProcessingQueue.Clear();
                CommonConnection.StreamingEventReceived -= Broker_StreamingEventReceived;
                CommonConnection.Dispose();
            }
            if (InstrumentInfoConnection != null)
            {
                InstrumentInfoConnection.StreamingEventReceived -= Broker_StreamingEventReceived;
                InstrumentInfoConnection.Dispose();
            }

            if (CandleConnection != null)
            {
                CandleConnection.StreamingEventReceived -= Broker_StreamingEventReceived;
                CandleConnection.Dispose();
            }

            CommonConnection = ConnectionFactory.GetConnection(TiApiToken);
            CandleConnection = ConnectionFactory.GetConnection(TiApiToken);
            if (Settings.SubscribeInstrumentStatus)
            {
                InstrumentInfoConnection = ConnectionFactory.GetConnection(TiApiToken);
                InstrumentInfoConnection.StreamingEventReceived += Broker_StreamingEventReceived;
            }                
            CandleConnection.StreamingEventReceived += Broker_StreamingEventReceived;
            CommonConnection.StreamingEventReceived += Broker_StreamingEventReceived;

            RunMonthUpdateTaskIfNotRunning();
            _lastRestartTime = DateTime.Now;
        }

        public void Init()
        {
            if (_telegram != null)
                _telegram.Stop();

            if (TgBotToken != null && TgChatId > long.MinValue) 
                _telegram = new TelegramManager(_services, TgBotToken, TgChatId);

            if (TiApiToken == null)
               return; 

            PrepareConnection();

            if (CommonConnectionQueueTask == null)
            {
                CommonConnectionQueueTask = Task.Factory
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
                while (_stockProcessingQueue.TryDequeue(out var obj))
                {
                    if (obj is CandleResponse cr)
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
                    else if (obj is IStockModel stock)
                    {
                        await _mainModel.OnStockUpdated(stock);
                    }
                }

                await Task.Delay(100);
            }
        }

        public void QueueBrokerAction(Func<Connection, Task> act, string description)
        {
            var brAct = new BrokerAction(act, description);
            CommonConnectionActions.Enqueue(brAct);
        }

        private async Task UpdateCounters()
        {
            int upd1sec = 0, upd5sec = 0, resub10min = 0;

            var list = _mainModel.Stocks.Select(s => s.Value).ToArray();
            for (int i = 0; i < list.Length; i++ )
            {
                var stock = list[i];
                var elapsed = stock.LastUpdatePrice.Elapsed();
                if (elapsed.TotalSeconds <= 5)
                    upd5sec++;
                if (elapsed.TotalSeconds <= 1)
                    upd1sec++;
                if (stock.LastResubscribeAttempt.Elapsed().TotalMinutes <= 10)
                    resub10min++;
            }
            _recentUpdatedStocksCount = upd5sec;

            await EventAggregator.PublishOnCurrentThreadAsync(new CommonInfoMessage() 
            {               
                TotalStocksUpdatedInFiveSec = _recentUpdatedStocksCount, 
                TotalStocksUpdatedInLastSec = upd1sec,
                ResubscribeAttemptsInTenMin = resub10min
            });
            _lastUIUpdate = DateTime.Now;
        }

        internal static async Task<TinkoffStocksInfoCollection.Root> GetInstrumentsInfo()
        {
            //TODO: Use cache
            using (var httpClient = new HttpClient())
            {
                var json = await httpClient.GetStringAsync(
                        "https://api.tinkoff.ru/trading/stocks/list?sortType=ByName&orderType=Asc&country=All")
                    .ConfigureAwait(false);
                var root = JsonConvert.DeserializeObject<TinkoffStocksInfoCollection.Root>(json);
                return root;
            }
        }

        private async Task UpdateInstrumentsInfo(HashSet<IStockModel> stocks = null)
        {
            if (stocks == null)
                stocks = new HashSet<IStockModel>(_mainModel.Stocks.Select(s => s.Value));

            try
            {
                _lastRestartTime = DateTime.Now;
                var info = await GetInstrumentsInfo();
                _lastRestartTime = DateTime.Now; // обновление инструментов часто занимает долгое время
                if (info.Status.Equals("Ok", StringComparison.InvariantCultureIgnoreCase))
                {
                    LastInstrumentsUpdate = DateTime.Now;
                    var instruments = info.Payload.Values.ToDictionary(v => v.Symbol.Ticker);

                    ExchangeStatus = info.Payload.Values
                        .Select(v => new { v.Symbol.Exchange, v.ExchangeStatus }).Distinct()
                        .Select(s => new ExchangeStatus(s.Exchange, s.ExchangeStatus)).ToArray();

                    foreach (var stock in stocks)
                    {
                        if (instruments.ContainsKey(stock.Ticker))
                        {
                            var instrumentInfo = new InstrumentInfo(instruments[stock.Ticker]);
                            if (Instruments.ContainsKey(stock.Ticker))
                                Instruments[stock.Ticker].ReadFrom(instrumentInfo);
                            else
                                Instruments[stock.Ticker] = instrumentInfo;

                            if (stock.Exchange != instrumentInfo.Exchange)
                                stock.Exchange = instrumentInfo.Exchange;
                            if (stock.CanBeShorted != instrumentInfo.ShortIsEnabled)
                                stock.CanBeShorted = instrumentInfo.ShortIsEnabled;
                        }
                        else
                        {
                            stock.Status = "Инструмент не торгуется";
                            stock.IsDead = true;
                        }
                    }
                }
                else
                {
                    throw new Exception(info.Status);
                }
            }
            catch (Exception ex)
            {
                LogError($"Ошибка получения сведений об инструментах: {ex.Message}");
                LastInstrumentsUpdate = DateTime.MinValue;
            }
        }

        private async Task BrokerQueueLoopAsync()
        {
            var cancellationToken = _cancellationTokenSource.Token;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    while (CommonConnectionActions.TryDequeue(out BrokerAction act))
                    {
                        if (DateTime.Now.Subtract(_lastUIUpdate).TotalMilliseconds > 900)
                        {
                            try 
                            {
                                await UpdateCounters().ConfigureAwait(false);
                            } 
                            catch (Exception ex )
                            {
                                LogError("Ошибка при обновлении показателей: " + ex.Message);
                            }
                        }
                        

                        bool bSuccess = true;
                        try
                        {
                            //var msg = $"{DateTime.Now} Выполнение операции '{act.Description}'...";
                            _logger.LogTrace("Выполнение операции {OperationDescription}", act.Description);
                            //Debug.WriteLine(msg);
                            await act.Action(CommonConnection);
                        }
                        catch (Exception ex)
                        {
                            bSuccess = false;
                            //CommonConnectionActions.Push(act);
                            var errorMsg = $"Ошибка при выполнении операции '{act.Description}': {ex.Message}";
                            LogError(errorMsg);
                        }
                        while (!bSuccess && !cancellationToken.IsCancellationRequested)
                        {
                            try
                            {
                                await ResetConnection().ConfigureAwait(false);
                                bSuccess = true;
                            } 
                            catch (Exception ex)
                            {
                                var errorMsg = $"Ошибка при переподключении: {ex.Message}";
                                LogError(errorMsg);
                            }
                        }
                    }

                    if (DateTime.Now.Subtract(_lastUIUpdate).TotalMilliseconds > 500)
                    {
                        try
                        {
                            await UpdateCounters().ConfigureAwait(false);
                        } 
                        catch (Exception ex )
                        {
                            LogError("Ошибка при обновлении показателей: " + ex.Message);
                        }
                    }

#if DEBUG
                    if (DateTime.Now.Subtract(LastInstrumentsUpdate).TotalSeconds > 20)
#else
                    if (DateTime.Now.Subtract(LastInstrumentsUpdate).TotalMinutes > 60)
#endif
                    {
                        LastInstrumentsUpdate = DateTime.Now;
                        _ = UpdateInstrumentsInfo().ConfigureAwait(false);
                    }

                    if (_mainModel.Stocks.Count > 0 && _lastRestartTime.HasValue && DateTime.Now.Subtract(_lastRestartTime.Value).TotalSeconds > 10)
                    {

                        if (_lastEventReceived == null || DateTime.Now.Subtract(_lastEventReceived.Value).TotalSeconds > 5)
                        {
                            if (ExchangeClosed || ActiveStocks.Count < 300) // || IsHolidays)
                            {
                                await Task.Delay(1000);
                                continue;
                            }

                            try 
                            {
                                await ResetConnection("Давно не поступало событий от биржи");
                            } 
                            catch (Exception ex )
                            {
                                LogError("Ошибка при переподключении: " + ex.Message);
                            }                        
                        }
                        //if (_recentUpdatedStocksCount < 10 && !ExchangeClosed)
                        //{
                        //    await ResetConnection("Не приходило обновлений по большей части акций");
                        //}
                    }
                    if (_lastSubscriptionCheck.Elapsed().TotalMinutes > 1)
                    {
                        try 
                        {    
                            CheckSubscription();
                        } 
                        catch (Exception ex)
                        {
                            LogError("Ошибка при проверке состояния подписок: " + ex.Message);
                        }   
                        _lastSubscriptionCheck = DateTime.Now;
                    }
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    LogError("Критическая ошибка потока: " + ex.Message);
                }               
            }
        }

        public bool ExchangeClosed => ExchangeStatus == null 
            || ExchangeStatus.All(s => s.Status == "Close" || s.Status == "Suspend")
            || Instruments.All(instrument => !instrument.Value.IsActive);

        private void LogError(string msg)
        {
            _mainModel.AddErrorMessage(msg);
            _logger.LogError(msg);
            //Debug.WriteLine(msg);
        }

        public async Task ResetConnection(string errorMsg, bool subscribe = true)
        {
            LogError("Переподключение: " + errorMsg);
            await ResetConnection(subscribe);
        }

        public async Task ResetConnection(bool subscribe = true)
        {
            _lastEventReceived = null;
            _lastRestartTime = null;
            _stockProcessingQueue.Clear();
            CommonConnectionActions.Clear();
            _subscribedFigi.Clear();
            _subscribedMinuteFigi.Clear();
            if (subscribe)
                await Task.Delay(5000);

            PrepareConnection();
            if (subscribe)
                SubscribeToStockEvents();
        }

        private async Task CandleProcessingProc(CandleResponse cr)
        {
            var candle = cr.Payload;
            var stock = _mainModel.Stocks.FirstOrDefault(s => s.Value.Figi == candle.Figi).Value;
            if (stock != null)
            {
                //stock.IsNotifying = false;
                if (candle.Interval == CandleInterval.Day)
                {
                    if (Instruments.TryGetValue(stock.Ticker, out var instrument)) {
                        if (candle.Time.Date < instrument.MarketStartDate.Date && stock.LastUpdatePrice > DateTime.MinValue)
                            return;
                    }                    
                    stock.TodayOpen = candle.Open;
                    stock.TodayDate = candle.Time.ToLocalTime();
                    stock.LastUpdatePrice = DateTime.Now;
                    stock.Price = candle.Close;
                    if (stock.TodayOpen > 0)
                        stock.DayChange = (stock.Price - stock.TodayOpen) / stock.TodayOpen;
                    stock.DayVolume = Math.Truncate(candle.Volume);
                    if (stock.AvgDayVolumePerMonth > 0)
                        stock.DayVolChgOfAvg = stock.DayVolume / stock.AvgDayVolumePerMonth;
                    await _mainModel.OnStockUpdated(stock);
                    if (!_subscribedMinuteFigi.Contains(stock.Figi))
                    {
                        QueueBrokerAction(b => b.SendStreamingRequestAsync(
                                SubscribeCandle(stock.Figi, CandleInterval.Minute)),
                            $"Подписка на минутную свечу {stock.Ticker} ({stock.Figi})");
                        _subscribedMinuteFigi.Add(stock.Figi);
                    }
                }
                else if (candle.Interval == CandleInterval.Minute)
                {
                    if (candle.Time.Date > stock.TodayDate && candle.Time.ToLocalTime().Hour > 3 && stock.MinuteCandles.Count > 1)
                    {
                        await ResetConnection(
                            $"Новый день ({stock.Ticker} {stock.TodayDate} -> {candle.Time.Date})");
                        return;
                    }
                    stock.LastUpdatePrice = DateTime.Now;
                    stock.LogCandle(candle);
                    await _mainModel.OnStockUpdated(stock);
                }
            }
        }

        private void Broker_StreamingEventReceived(object sender, StreamingEventReceivedEventArgs e)
        {
            //Debug.WriteLine(JsonConvert.SerializeObject(e.Response));
            _lastEventReceived = DateTime.Now;
            switch (e.Response)
            {
                case CandleResponse cr:
                    {
                        _stockProcessingQueue.Enqueue(cr);
                        break;
                    }

                case OrderbookResponse or:
                    {
                        var stock = _mainModel.Stocks.FirstOrDefault(s => s.Value.Figi == or.Payload.Figi).Value;
                        if (stock != null && or.Payload.Asks.Count > 0 && or.Payload.Bids.Count > 0)
                        {
                            var bids = or.Payload.Bids.Where(b => b[1] >= 1).ToList();
                            var asks = or.Payload.Asks.Where(a => a[1] >= 1).ToList();
                            OrderbookInfoSpb[stock.Ticker] = new OrderbookModel(or.Payload.Depth, bids, asks,
                                or.Payload.Figi, stock.Ticker, stock.Isin);
                            stock.BestBidSpb = bids[0][0];
                            stock.BestAskSpb = asks[0][0];
                            stock.LastUpdateOrderbook = DateTime.Now;
                            _stockProcessingQueue.Enqueue(stock); // raise stock update (but only in sync with other updates)
                        }

                        break;
                    }

                case InstrumentInfoResponse ir:
                    {
                        var info = ir.Payload;
                        var stock = _mainModel.Stocks.FirstOrDefault(s => s.Value.Figi == info.Figi).Value;
                        if (stock != null)
                        {
                            if ( String.IsNullOrWhiteSpace( stock.Status ) )
                            {
                                string status = info.TradeStatus;
                                if ( status == "normal_trading" )
                                    status = "Торгуется";
                                stock.Status = status;
                                if (String.IsNullOrEmpty(stock.Status))
                                    stock.Status = Instruments[stock.Ticker].InstrumentStatusShortDesc;
                            }
                            if (stock.LimitDown != ir.Payload.LimitDown)
                                stock.LimitDown = ir.Payload.LimitDown;
                            if (stock.LimitUp != ir.Payload.LimitUp)
                                stock.LimitUp = ir.Payload.LimitUp;
                            if (stock.MinPriceIncrement != ir.Payload.MinPriceIncrement)
                                stock.MinPriceIncrement = ir.Payload.MinPriceIncrement;
                        }
                        break;
                    }
            }
        }

        private int _apiCount = 0;

#region Month Stats

        private async Task<bool> GetMonthStats(IStockModel stock)
        {
            if (!stock.MonthStatsExpired)
                return true;

            _apiCount++;
            //Debug.WriteLine($"API Request {++_apiCount} for {stock.Ticker} {stock.PriceF} {stock.DayChangeF} {DateTime.Now}");

            CandleList prices = null;
            try
            {
                prices = await CommonConnection.Context.MarketCandlesAsync(stock.Figi,
                    DateTime.Now.Date.AddMonths(-1),
                    DateTime.Now.Date.AddDays(1), CandleInterval.Day);
                stock.LastMonthDataUpdate = DateTime.Now;
            }
            catch
            {
                return false;
            }

            if (prices.Candles.Count == 0)
                return false;

            decimal monthVolume = 0, monthHigh = 0, monthLow = 0, monthAvgPrice = 0, 
                avgDayVolumePerMonth = 0, avgDayPricePerMonth = 0, monthOpen = -1,
                yesterdayVolume = 0, yesterdayMin = 0, yesterdayMax = 0, yesterdayAvgPrice = 0;

            var todayCandle = prices.Candles[prices.Candles.Count-1];
            foreach (var candle in prices.Candles)
            {
                if (monthOpen == -1)
                    monthOpen = candle.Open;
                
                monthLow = monthLow == 0 ? candle.Low : Math.Min(monthLow, candle.Low);
                monthHigh = monthHigh == 0 ? candle.High : Math.Min(monthHigh, candle.High);
                monthVolume += candle.Volume;
                avgDayPricePerMonth += (candle.High + candle.Low) / 2;
                yesterdayVolume = candle.Volume;
                yesterdayMin = candle.Low;
                yesterdayMax = candle.High;

                AddCandleToStock(Tuple.Create(stock, candle));
            }

            monthAvgPrice = (monthLow + monthHigh) / 2;
            yesterdayAvgPrice = (yesterdayMin + yesterdayMax) / 2;
            avgDayPricePerMonth /= prices.Candles.Count;
            avgDayVolumePerMonth = monthVolume / prices.Candles.Count;

            stock.MonthOpen = monthOpen;
            stock.MonthHigh = monthHigh;
            stock.MonthLow = monthLow;
            stock.MonthVolume = monthVolume;
            stock.MonthVolumeCost = monthVolume * monthAvgPrice * stock.Lot;
            stock.AvgDayVolumePerMonth = Math.Round(avgDayVolumePerMonth);
            stock.AvgDayPricePerMonth = avgDayPricePerMonth;
            stock.AvgDayVolumePerMonthCost = avgDayPricePerMonth * avgDayVolumePerMonth * stock.Lot;
            stock.DayVolChgOfAvg = stock.DayVolume / stock.AvgDayVolumePerMonth;
            stock.YesterdayAvgPrice = yesterdayAvgPrice;
            stock.YesterdayVolume = yesterdayVolume;
            stock.YesterdayVolumeCost = yesterdayVolume * yesterdayAvgPrice * stock.Lot;

            return true;
        }

        public async Task<bool> CheckMonthStatsAsync(IStockModel stock, CancellationToken token = default(CancellationToken))
        {
            if (stock.MonthStatsExpired)
            {
                lock (stock)
                {
                    if (!_monthStatsQueue.Contains(stock))
                    {
                        _monthStatsQueue.Enqueue(stock);
                    }
                }

                while (stock.MonthStatsExpired && !token.IsCancellationRequested)
                {
                    await Task.Delay(100, token);
                }

                if (token.IsCancellationRequested)
                    return false;
            }

            return true;
        }

        private void EnqueueStockForMonthStatsIfExpired(IStockModel stock)
        {
            if (stock.MonthStatsExpired)
            {
                lock (stock)
                {
                    if (stock.MonthStatsExpired && !_monthStatsQueue.Contains(stock))
                    {
                        _monthStatsQueue.Enqueue(stock);
                    }
                }
            }
        }

        private DateTime _lastBatchMonthStatsEnqueued;

        private async Task ReportStatsCheckerProgress()
        {
            var stocks = _mainModel.Stocks
                .Where(s => !s.Value.IsDead).ToList()  // this shit is maybe more thread-safe 
                .Select(pair => pair.Value).ToList();
            var completed = stocks.Count(s => !s.MonthStatsExpired);
            if (stocks.Count > 0)
                await EventAggregator.PublishOnCurrentThreadAsync(new StatsUpdateMessage(completed, stocks.Count,
                    completed == stocks.Count, _apiCount));
        }

        private async Task MonthStatsCheckerLoop()
        {
            var token = _cancellationTokenSource.Token;
            _lastBatchMonthStatsEnqueued = DateTime.Now;

            while (!token.IsCancellationRequested)
            {
                if (_monthStatsQueue.TryDequeue(out var stock))
                {
                    try
                    {
                        if (await GetMonthStats(stock))
                            await ReportStatsCheckerProgress();
                        else
                        {
                            await Task.Delay(500);
                            EnqueueStockForMonthStatsIfExpired(stock);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при загрузке истории");
                        EnqueueStockForMonthStatsIfExpired(stock);
                    }
                }

                if (DateTime.Now.Subtract(_lastBatchMonthStatsEnqueued).TotalSeconds > 31)
                {
                    _mainModel.Stocks
                        .Select(s => s.Value)
                        .Where(s => !_monthStatsQueue.Contains(s)
                                    && s.MonthStatsExpired && s.Price > 0)
                        .OrderByDescending(s => Math.Abs(s.DayChange))
                        .Take(30).ToList().ForEach(s => _monthStatsQueue.Enqueue(s));
                    _lastBatchMonthStatsEnqueued = DateTime.Now;
                }

                if (!_monthStatsQueue.Any())
                    await Task.Delay(100);
            }
        }

        private void RunMonthUpdateTaskIfNotRunning()
        {
            if (_monthStatsTask == null)
                _monthStatsTask = Task.Factory.StartLongRunningTask(
                    () => MonthStatsCheckerLoop().ConfigureAwait(false), 
                    _cancellationTokenSource.Token);
        }

#endregion

        public void CheckSubscription()
        {
            foreach (var pair in _mainModel.Stocks)
            {
                var stock = pair.Value;
                if ( (stock.LastUpdatePrice.Elapsed().TotalMinutes > 1 || stock.LastUpdateOrderbook.Elapsed().TotalMinutes > 1) && Instruments[stock.Ticker].IsActive )
                {
                    stock.LastResubscribeAttempt = DateTime.Now;
                    if ( _subscribedFigi.Contains( stock.Figi ) )
                    {
                        var request = new CandleUnsubscribeRequest(stock.Figi, CandleInterval.Day);
                        QueueBrokerAction(b => b.SendStreamingRequestAsync(request),
                            $"Отписка от дневной свечи {stock.Ticker} ({stock.Figi})");

                        var request2 = new OrderbookUnsubscribeRequest(stock.Figi, 5);
                        QueueBrokerAction(b => CandleConnection.SendStreamingRequestAsync(request2),
                            $"Отписка от стакана {stock.Ticker} ({stock.Figi}");

                        if (Settings.SubscribeInstrumentStatus)
                        {
                            var request3 = new InstrumentInfoUnsubscribeRequest( stock.Figi );
                            QueueBrokerAction( b => InstrumentInfoConnection.SendStreamingRequestAsync( request3 ),
                                $"Отписка от статуса {stock.Ticker} ({stock.Figi}" );
                        }

                        _subscribedFigi.TryRemove(stock.Figi);
                    }
                    if (_subscribedMinuteFigi.Contains( stock.Figi ) )
                    {
                        QueueBrokerAction(b => b.SendStreamingRequestAsync(
                                    UnsubscribeCandle(stock.Figi, CandleInterval.Minute)),
                                $"Отписка от минутной свечи {stock.Ticker} ({stock.Figi})");

                        _subscribedMinuteFigi.TryRemove(stock.Figi);
                    }
                }
            }
            SubscribeToStockEvents();
        }

        public void SubscribeToStockEvents()
        {
            //var toSubscribeInstr = new HashSet<IStockModel>();
            foreach (var pair in _mainModel.Stocks)
            {
                var stock = pair.Value;
                if (stock.IsDead)
                    continue;

                if (!_subscribedFigi.Contains(stock.Figi) && Instruments[stock.Ticker].IsActive)
                {
                    var request = new CandleSubscribeRequest(stock.Figi, CandleInterval.Day);
                    QueueBrokerAction(b => b.SendStreamingRequestAsync(request),
                        $"Подписка на дневную свечу {stock.Ticker} ({stock.Figi})");

                    var request2 = new OrderbookSubscribeRequest(stock.Figi, 5);
                    QueueBrokerAction(b => CandleConnection.SendStreamingRequestAsync(request2),
                        $"Подписка на стакан {stock.Ticker} ({stock.Figi}");

                    if (Settings.SubscribeInstrumentStatus)
                    {
                        var request3 = new InstrumentInfoSubscribeRequest( stock.Figi );
                            QueueBrokerAction( b => InstrumentInfoConnection.SendStreamingRequestAsync( request3 ),
                            $"Подписка на статус {stock.Ticker} ({stock.Figi}" );
                    }                    

                    //toSubscribeInstr.Add(stock);
                    _subscribedFigi.Add(stock.Figi);
                }
            }
            //int n = 0;
            //foreach ( var stock in toSubscribeInstr )
            //{
            //    var request3 = new InstrumentInfoSubscribeRequest( stock.Figi );
            //    QueueBrokerAction( b => InstrumentInfoConnection.SendStreamingRequestAsync( request3 ),
            //        $"Подписка на статус {stock.Ticker} ({stock.Figi}" );

            //    if ( ++n % 100 == 0 )
            //        await Task.Delay( 1000 );
            //}
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

        public async Task UpdateStocks(bool subscribeToPrices = true, Action<string> statusCallback = null)
        {
            if (CommonConnection == null)
                return;

            statusCallback?.Invoke("Загрузка инструментов...");
            var stocks = await CommonConnection.Context.MarketStocksAsync();
            var stocksToAdd = new HashSet<IStockModel>();
            var groupOpts = (Settings as SettingsContainer).AssetGroupSettingsByCurrency;

            foreach (var iwo in from s in stocks.Instruments 
                                  join opt in groupOpts on s.Currency.ToString().ToUpper() equals opt.Value.Currency.ToUpper()
                                  select new { Instrument = s, FilterOptions = opt })
            {
                var instr = iwo.Instrument;

                bool ignoreInstr = !iwo.FilterOptions.Value.IsSubscriptionEnabled;

                if (!ignoreInstr && !String.IsNullOrWhiteSpace(Settings.ExcludePattern))
                {
                    ignoreInstr = Regex.IsMatch(instr.Ticker, Settings.ExcludePattern);
                }

                if (!String.IsNullOrWhiteSpace(Settings.IncludePattern))
                {
                    ignoreInstr = !Regex.IsMatch(instr.Ticker, Settings.IncludePattern);
                }

                var stock = _mainModel.Stocks.FirstOrDefault(s => s.Value.Figi == instr.Figi).Value;
                if (stock == null)
                {
                    if (!ignoreInstr) 
                    {
                        stock = _mainModel.CreateStockModel(instr);
                        stocksToAdd.Add(stock);
                        ActiveStocks.TryAdd(stock.Ticker, stock);
                    }
                } 
                else if (ignoreInstr)
                {
                    _mainModel.Stocks.Remove(stock.Ticker);
                    ActiveStocks.TryRemove(stock.Ticker, out _);
                }
            }

            statusCallback?.Invoke("Загрузка сведений об инструментах...");
            await UpdateInstrumentsInfo(stocksToAdd);

            statusCallback?.Invoke("Подписка на события по инструментам...");
            await _mainModel.AddStocks(stocksToAdd);

            if (subscribeToPrices)
                SubscribeToStockEvents();
            //_mainModel.IsNotifying = false;
        }

        public async Task HandleAsync( SettingsChangeEventArgs message, CancellationToken cancellationToken )
        {
            var last = message.PrevSettings as SettingsContainer;
            var current = message.NewSettings as SettingsContainer;

            bool needReconnect = last.TiApiKey != current.TiApiKey
                || last.TgBotApiKey != current.TgBotApiKey
                || last.TgChatId != current.TgChatId
                || last.TgChatIdRu != current.TgChatId
                || current.HideRussianStocks != last.HideRussianStocks
                || current.IncludePattern != last.IncludePattern
                || current.ExcludePattern != last.ExcludePattern
                || current.SubscribeInstrumentStatus != last.SubscribeInstrumentStatus;

            needReconnect = needReconnect || (from lgs in last.AssetGroupSettingsByCurrency
                               join cgs in current.AssetGroupSettingsByCurrency
                               on lgs.Key equals cgs.Key
                               where lgs.Value.IsSubscriptionEnabled != cgs.Value.IsSubscriptionEnabled
                               select true).Any();

            if (needReconnect)
            {
                await ResetConnection("Изменение настроек.", false);
                await UpdateStocks(true);
            }
        }
    }
}
