using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Tinkoff.Trading.OpenApi.Models;
using Tinkoff.Trading.OpenApi.Network;

using TradeApp.ViewModels;
using static Tinkoff.Trading.OpenApi.Models.Portfolio;
using static Tinkoff.Trading.OpenApi.Models.PortfolioCurrencies;

namespace TradeApp.Data
{
    public class TradeInfo
    {
        public StockViewModel Stock { get; set; }
        public decimal InitialPrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal MaxPrice { get; set; }
        public bool TenPercentRaised { get; set; }
        public int Lots { get; set; }

        public decimal Profit => (InitialPrice - CurrentPrice) * Lots;
        public TradeInfo()
        {

        }
    }

    public class CurrencyInfo
    {
        public Currency Currency { get; set; }
        public decimal Balance { get; set; }
        public decimal Blocked { get; set; }
    }

    public class SandboxBot
    {
        private readonly StocksManager _broker;
        private readonly TelegramManager _telegram;
        private SandboxContext _sandbox;
        private string _accountId;

        private ConcurrentDictionary<StockViewModel, TradeInfo> Trades { get; } = new ConcurrentDictionary<StockViewModel, TradeInfo>();

        private HashSet<TradeInfo> _failedTrades = new HashSet<TradeInfo>();

        private HashSet<TradeInfo> _archivedTrades = new HashSet<TradeInfo>();

        private HashSet<Position> _portfolioPositions = new HashSet<Position>();

        private ConcurrentDictionary<string, CurrencyInfo> _currencies = new ConcurrentDictionary<string, CurrencyInfo>();

        public SandboxBot(StocksManager broker, TelegramManager telegram)
        {
            _broker = broker;
            _telegram = telegram;
        }

        public async Task Init()
        {
            _sandbox = _broker.SandboxConnection.Context;
            var account = await _sandbox.RegisterAsync(BrokerAccountType.Tinkoff);
            _accountId = account.BrokerAccountId;

            //await _sandbox.ClearAsync(_accountId);
            // set balance
            //foreach (var currency in new[] { Currency.Rub, Currency.Usd })
            //    await _sandbox.SetCurrencyBalanceAsync(currency, 
            //        currency == Currency.Rub ? 100_000 : 10_000, _accountId);
            _currencies["USD"] = new CurrencyInfo { Balance = 2000, Currency = Currency.Usd };
            _currencies["RUB"] = new CurrencyInfo { Balance = 100000, Currency = Currency.Rub };

            _telegram.PostMessage($"Аккаунт песочницы создан. Id {_accountId}.", null);

            await ReportBalances();
        }

        //public async Task RefreshBalances()
        //{
        //    await UpdateCurrenciesAsync();

        //    var portfolio = await _sandbox.PortfolioAsync();
        //    foreach (var position in portfolio.Positions.Where(p => p.InstrumentType == InstrumentType.Stock))
        //    {
        //        var operations = await _sandbox.OperationsAsync(DateTime.Now.AddMonths(-1),
        //            DateTime.Now.AddDays(1), position.Figi, _accountId);
        //        //position.AveragePositionPrice.Value = operations.Average(op => op.Price);
        //        _portfolioPositions.Add(position);
        //    }
        //}

        public async Task ReportBalances()
        {
            //await RefreshBalances();

            var currencies = String.Join("\r\n", _currencies.Select(c => 
                $"{c.Value.Currency} {c.Value.Balance.FormatPrice(c.Value.Currency.ToString())} (Blocked: {c.Value.Blocked.FormatPrice(c.Value.Currency.ToString())})"));

            var stocks = String.Join("\r\n", Trades.Select(p =>
                $"{p.Key.Ticker} ({p.Key.Name}) {p.Value.Lots} шт. по цене {p.Value.InitialPrice.FormatPrice(p.Key.Currency)}" +
                $" тек. прибыль {p.Value.Profit.FormatPrice(p.Key.Currency)}"));

            var msg = @$"
Баланс: 
{currencies}
Активы:
{stocks}
".Trim();

            _telegram.PostMessage(msg, null);
            await Task.CompletedTask;
        }

        public async Task Check(StockViewModel stock)
        {
            if (Trades.TryGetValue(stock, out var info))
            {
                info.CurrentPrice = stock.Price;
                info.MaxPrice = Math.Max(info.MaxPrice, info.CurrentPrice);
                var changeFromInitial = (info.CurrentPrice - info.InitialPrice) / info.InitialPrice;
                if (changeFromInitial > 0.1m)
                    info.TenPercentRaised = true;

                decimal sellPrice = Math.Round(info.MaxPrice * 0.97m, 2);
                if (info.TenPercentRaised && sellPrice > info.CurrentPrice)
                {
                    await Sell(stock, $"Цена достигла максимума в размере {info.MaxPrice}, после чего снизилась на 3%." +
                        $"\r\nИзменение с момента покупки составило {changeFromInitial:P2}");
                }
                else if (info.CurrentPrice < info.StopLoss)
                {
                    await Sell(stock, $"Stop Loss triggered at {changeFromInitial:P2} change.");
                }
                else if (changeFromInitial > 0.2m)
                {
                    await Sell(stock, $"Цена выросла на {changeFromInitial:P2} с момента покупки. Фиксация прибыли.");
                }
            }
        }

        public async Task Sell(StockViewModel stock, string reason)
        {
            if (!Trades.ContainsKey(stock))
                return;

            Trades.TryRemove(stock, out TradeInfo info);

            try
            {
                UpdateBalance(info, OperationType.Sell);

                _telegram.PostMessage($"Продажа {info.Lots} акций {stock.Ticker} по рыночной цене (примерно {info.CurrentPrice.FormatPrice(stock.Currency)}): {reason}" +
                    $"\r\nЦена покупки: {info.InitialPrice.FormatPrice(stock.Currency)} Цена продажи: {info.CurrentPrice.FormatPrice(stock.Currency)}\r\n" +
                    $"Profit: {info.Profit.FormatPrice(stock.Currency)}", stock.Ticker);
                //await MakeOrder(stock.Figi, info.Lots, OperationType.Sell);
                _archivedTrades.Add(info);
                //await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                _telegram.PostMessage(ex.Message, null);
                _failedTrades.Add(info);
                return;
            }

            await ReportBalances();
        }

        public async Task Buy(StockViewModel stock)
        {
            if (Trades.ContainsKey(stock))
                return;

            //await UpdateCurrenciesAsync();

            decimal price = stock.Price;
            decimal balance = _currencies[stock.Currency.ToUpper()].Balance;
            int lots = (int)(balance * 0.2m / price);

            var tradeInfo = new TradeInfo
            {
                Stock = stock,
                InitialPrice = price,
                MaxPrice = price,
                StopLoss = price * 0.9m,
                Lots = lots
            };

            try
            {
                _telegram.PostMessage($"Покупка {lots} акций {stock.Ticker} по рыночной цене (примерно {price.FormatPrice(stock.Currency)})", null);
                //await MakeOrder(stock.Figi, lots, OperationType.Buy);
                UpdateBalance(tradeInfo, OperationType.Buy);
                //await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                _telegram.PostMessage(ex.Message, null);
                _failedTrades.Add(tradeInfo);
                return;
            }

            Trades[stock] = tradeInfo;

            await ReportBalances();
        }

        private void UpdateBalance(TradeInfo trade, OperationType operation)
        {
            var currencyInfo = _currencies[trade.Stock.Currency.ToUpper()];
            lock (currencyInfo)
            {
                if (operation == OperationType.Buy)
                    currencyInfo.Balance -= trade.InitialPrice * trade.Lots;
                else if (operation == OperationType.Sell)
                    currencyInfo.Balance += trade.CurrentPrice * trade.Lots;
            }
        }

        public async Task MakeOrder(string figi, int lots, OperationType operation, decimal? price = null)
        {
            if (price == null)
                await _sandbox.PlaceMarketOrderAsync(new MarketOrder(figi, lots, operation, _accountId));
            else
                await _sandbox.PlaceLimitOrderAsync(new LimitOrder(figi, lots, operation, price.Value, _accountId));
        }

        //public async Task UpdateCurrenciesAsync()
        //{
        //    var portfolio = await _sandbox.PortfolioCurrenciesAsync(_accountId);
        //    foreach (var currency in portfolio.Currencies)
        //    {
        //        if (_currencies.TryGetValue(currency.Currency.ToString().ToUpper(), out var curr))
        //        {
        //            curr.Balance = currency.Balance;
        //            curr.Blocked = currency.Blocked;
        //        }
        //        else
        //        {
        //            _currencies[currency.Currency.ToString().ToUpper()] = new CurrencyInfo
        //            {
        //                Balance = currency.Balance,
        //                Blocked = currency.Blocked,
        //                Currency = currency.Currency
        //            };
        //        }
        //    }
        //}
    }
}
