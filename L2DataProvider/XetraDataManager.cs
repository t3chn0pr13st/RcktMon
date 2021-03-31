using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CoreData.Interfaces;
using CoreData.Models;
using CoreData.Settings;
using CoreNgine;
using CoreNgine.Infra;
using CoreNgine.Interfaces;
using CoreNgine.Models;
using Newtonsoft.Json.Linq;

namespace USADataProvider
{
    public class XetraDataManager : IHandler<SettingsChangeEventArgs>, IHandler<IEnumerable<IStockModel>>, IUSADataManager, IDisposable
    {
        private Task _mainTask;
        public INgineSettings Settings { get; }
        public IMainModel MainModel { get; }

        private string _login;
        private string _password;
        private string _sessionId;
        private bool _wasStarted = false;
        private HttpClient _httpClient = new HttpClient();
        private IEventAggregator2 _eventAggregator;

        private CancellationTokenSource _taskCancellation = new CancellationTokenSource();

        public IDictionary<string, QuoteData> QuoteDatas { get; } = new ConcurrentDictionary<string, QuoteData>();

        private HashSet<string> _subscribedTickers = new HashSet<string>();

        public XetraDataManager(ISettingsProvider settingsProvider, IMainModel mainModel, IEventAggregator2 eventAggregator)
        {
            _eventAggregator = eventAggregator;
            Settings = settingsProvider.Settings;
            MainModel = mainModel;
            _eventAggregator.SubscribeOnBackgroundThread(this);

            if (Settings.USAQuotesEnabled)
                Run();
        }

        public void Run()
        {
            _login = Settings.USAQuotesLogin;
            _password = Settings.USAQuotesPassword;
            _wasStarted = true;
            if (_mainTask != null)
            {
                _taskCancellation.Cancel();
                _taskCancellation = new CancellationTokenSource();
            }
            _mainTask = Task.Factory.StartLongRunningTask(
                () => GetDataAsync(_taskCancellation.Token)
                .ConfigureAwait(false), 
                _taskCancellation.Token);
        }

        public void Stop()
        {
            _taskCancellation.Cancel();
            _taskCancellation = new CancellationTokenSource();
            _wasStarted = false;
        }

        public void SubscribeTicker(string ticker)
        {
            if (ticker.Contains('@') || ticker == "TCS")
                return;
            lock (_subscribedTickers)
            {
                _subscribedTickers.Add(ticker);
            }
        }

        public void UnsubscribeTicker(string ticker)
        {
            lock (_subscribedTickers)
            {
                _subscribedTickers.Remove(ticker);
            }
        }

        public async Task<bool> Authorize(string login, string password)
        {
            var startUrl = Settings.USAQuotesURL.Split(new [] {'/'}, StringSplitOptions.RemoveEmptyEntries)[1];
            var response = await _httpClient.PostAsync($"https://{startUrl}/auth/g/authenticate/v0/501/{login}/{password}/", new StringContent(""), _taskCancellation.Token);
            dynamic result = JObject.Parse(await response.Content.ReadAsStringAsync());
            if (result.code.name != "Ok")
            {
                MainModel.AddErrorMessage("L2DataService: " + result.message);
                return false;
            }
            _sessionId = result.sid;
            return true;
        }

        public async Task GetDataAsync(CancellationToken token)
        {
            if (!await Authorize(Settings.USAQuotesLogin, Settings.USAQuotesPassword))
            {
                Stop();
                return;
            }
            
            var tickers = new List<string>();
            while (!token.IsCancellationRequested)
            {
                tickers.Clear();
                lock (_subscribedTickers)
                {
                    tickers.AddRange(_subscribedTickers);
                }

                if (tickers.Count == 0)
                {
                    await Task.Delay(100, token);
                    continue;
                }

                bool hasError = false;

                try
                {
                    int totalParts = (int)Math.Ceiling(tickers.Count / 100m);
                    var runResult = Parallel.For(0, totalParts, new ParallelOptions() { MaxDegreeOfParallelism = 4 },
                        (partNum) =>
                    {
                        var teekkaz = tickers.Skip(partNum * 100).Take(100).ToList();
                        var tstr = String.Join(",", teekkaz);
                        try
                        {
                            var resp = _httpClient.PostAsync(
                                $"{Settings.USAQuotesURL}?symbols={tstr}&webmasterId=501&sid={_sessionId}",
                                new StringContent(""), token).Result;
                            var text = resp.Content.ReadAsStringAsync().Result;
                            ParseQuotes(text);
                        }
                        catch (Exception ex)
                        {
                            MainModel.AddErrorMessage("L2DataService: " + ex.Message);
                            hasError = true;
                        }
                    });
                    while (!runResult.IsCompleted )
                        await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    MainModel.AddErrorMessage("L2DataService: " + ex.Message);
                }

                if (hasError)
                    await Authorize(Settings.USAQuotesLogin, Settings.USAQuotesPassword);

                await Task.Delay(1000, token);
            }
        }

        private void ParseQuotes(string input)
        {
            var lines = input.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var fields = line.Split(',');
                var ticker = fields[0];
                var exchange = fields[6];
                decimal last = fields[9].ParseDecimal();
                if (exchange == "null" || last == 0)
                    continue;
                decimal bid = fields[17].ParseDecimal();
                decimal ask = fields[18].ParseDecimal();
                int.TryParse(fields[19], out int bidSize);
                int.TryParse(fields[20], out int askSize);
                DateTimeOffset lastTrade = fields[24].ParseDateTime();
                var test = lastTrade.ToLocalTime();
                UpdateQuoteData(ticker, exchange, lastTrade.LocalDateTime, last, bid, ask, bidSize, askSize);
            }
        }

        private void UpdateQuoteData(string ticker, string exchange, DateTime lastTrade,
            decimal last, decimal bid, decimal ask, int bidSize, int askSize)
        {
            bool changed = false;
            QuoteData quote = null;
            if (!QuoteDatas.ContainsKey(ticker))
            {
                quote = new QuoteData() { Ticker = ticker, Exchange = exchange };
                QuoteDatas[ticker] = quote;
                changed = true;
            }
            else
            {
                quote = QuoteDatas[ticker];
            }

            if (quote.Last != last)
            {
                quote.Last = last;
                changed = true;
            }
            if (quote.Bid != bid)
            {
                quote.Bid = bid;
                changed = true;
            }
            if (quote.Ask != ask)
            {
                quote.Ask = ask;
                changed = true;
            }
            if (quote.BidSize != bidSize)
            {
                quote.BidSize = bidSize;
                changed = true;
            }
            if (quote.AskSize != askSize)
            {
                quote.AskSize = askSize;
                changed = true;
            }
            if (quote.LastTrade != lastTrade)
            {
                quote.LastTrade = lastTrade;
                changed = true;
            }

            if (changed)
            {
                QuoteChanged(quote);
            }
        }

        private void QuoteChanged(QuoteData quote)
        {
            var stock = MainModel.Stocks[quote.Ticker];
            if (stock != null)
            {
                stock.PriceUSA = quote.Last;
                stock.BidUSA = quote.Bid;
                stock.AskUSA = quote.Ask;
                stock.BidSizeUSA = quote.BidSize;
                stock.AskSizeUSA = quote.AskSize;
                stock.LastTradeUSA = quote.LastTrade;
                stock.LastUpdateUSA = DateTime.Now;
                MainModel.OnStockUpdated(stock);
            }
        }

        public Task HandleAsync(IEnumerable<IStockModel> message, CancellationToken cancellationToken)
        {
            var tickers = message
                .Where(s => !s.IsDead && s.Exchange.StartsWith("SPB"))
                .Select(s => s.Ticker.ToLower()).ToList();
            lock (_subscribedTickers)
            {
                tickers.Apply(t => _subscribedTickers.Add(t));
            }
            return Task.CompletedTask;
        }

        public Task HandleAsync(SettingsChangeEventArgs message, CancellationToken cancellationToken)
        {
            if (!_wasStarted && message.NewSettings.USAQuotesEnabled)
            {
                Run();
            }
            else if (_wasStarted && _login != message.NewSettings.USAQuotesLogin && _password != message.NewSettings.USAQuotesPassword)
            {
                Run();
            } else if (_wasStarted && !message.NewSettings.USAQuotesEnabled)
            {
                Stop();
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _taskCancellation?.Cancel();
            _taskCancellation?.Dispose();
            _mainTask?.Dispose();
        }
    }
}
