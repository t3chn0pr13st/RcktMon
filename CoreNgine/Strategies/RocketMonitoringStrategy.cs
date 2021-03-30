using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreData;
using CoreData.Interfaces;
using CoreData.Models;
using CoreData.Settings;
using CoreNgine.Infra;
using CoreNgine.Models;
using CoreNgine.Shared;
using Microsoft.Extensions.Logging;
using Tinkoff.Trading.OpenApi.Models;

namespace CoreNgine.Strategies
{
    public class RocketMonitoringStrategy : IHandler<IStockModel>
    {
        public INgineSettings Settings { get; }
        public IMainModel MainModel { get; }
        public StocksManager StocksManager { get; }
        public ILogger<RocketMonitoringStrategy> Logger { get; }

        private readonly DateTime _strategyLoaded = DateTime.Now;

        public TimeSpan Elapsed => DateTime.Now.Subtract(_strategyLoaded);

        public RocketMonitoringStrategy(ISettingsProvider settingsProvider, IMainModel mainModel, StocksManager stocksManager, IEventAggregator2 eventAggregator, ILogger<RocketMonitoringStrategy> logger)
        {
            StocksManager = stocksManager;
            Settings = settingsProvider.Settings;
            MainModel = mainModel;
            Logger = logger;
            eventAggregator.SubscribeOnPublishedThread(this);
        }

        public bool IsSendToTelegramEnabled =>
            Settings.IsTelegramEnabled && Elapsed.TotalSeconds > 10;

        private async Task<bool> EnsureHistoryLoaded(IStockModel stock)
        {
            if (!stock.MonthStatsExpired)
                return true;

            try
            {
                await StocksManager.CheckMonthStatsAsync(stock).TimeoutAfter(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                var errorText = $"Не удалось загрузить историю {stock.Ticker}: {ex.Message}";
                MainModel.AddErrorMessage(errorText);
                Logger?.LogError(ex, errorText);
                return false;
            }

            if (stock.AvgDayVolumePerMonth < 0.01m)
            {
                var errorText = $"Не удалось загрузить историю {stock.Ticker}";
                MainModel.AddErrorMessage(errorText);
                Logger?.LogError(errorText);
                return false;
            }

            return true;
        }

        public async Task PerformQuotePercentChangeCheck(IStockModel stock)
         {
             if (stock.Price == 0 || stock.MinuteCandles.Count == 0)
                 return;

             var lastCandleTime = stock.MinuteCandles.Max(c => c.Value.Time);
             var candle = stock.MinuteCandles.Last(c => c.Value.Time == lastCandleTime).Value;
             if (Math.Abs(stock.DayChange) > Settings.MinDayPriceChange && Settings.MinDayPriceChange > 0
                && (stock.LastAboveThresholdDate == null
                || stock.LastAboveThresholdDate.Value.Date < stock.LastUpdate.Date))
             { 
                 stock.LastAboveThresholdDate = stock.LastUpdate;

                 if (!await EnsureHistoryLoaded(stock))
                     return;

                var volPerc = stock.DayVolume / stock.AvgDayVolumePerMonth;
                if (volPerc > Settings.MinVolumeDeviationFromDailyAverage && Settings.MinVolumeDeviationFromDailyAverage > 0)
                {
                    string chatId = Settings.TgChatId; 
                    if (stock.Currency.Equals("RUB", StringComparison.InvariantCultureIgnoreCase) ||
                        stock.Ticker == "TCS")
                        chatId = Settings.TgChatIdRu;

                    if (IsSendToTelegramEnabled && long.TryParse(chatId, out var lChatId) && lChatId != 0)
                        StocksManager.Telegram.PostMessage(stock.GetDayChangeInfoText(), stock.Ticker, lChatId);

                    MainModel.AddMessage(
                        MessageKind.DayChange,
                        stock.Ticker,
                        DateTime.Now,
                        stock.DayChange,
                        stock.DayVolume,
                        $"{stock.Ticker}: {stock.DayChange.Arrow(true)} {stock.DayChange:P2} с начала дня ({stock.TodayOpenF} → {stock.PriceF})"
                    );
                }
             }

            var change = stock.GetLastXMinChange(
                Settings.NumOfMinToCheck,
                Settings.NumOfMinToCheckVol,
                Settings.MinXMinutesPriceChange,
                Settings.MinXMinutesVolChange);

            if (!change.volumeTrigger && Settings.MinXMinutesPriceChange > 0 && Math.Abs(change.change) > Settings.MinXMinutesPriceChange 
                && (stock.LastAboveThresholdCandleTime == null || stock.LastAboveThresholdCandleTime < candle.Time.AddMinutes(-change.minutes)))
            {
                stock.LastAboveThresholdCandleTime = candle.Time;

                if (!await EnsureHistoryLoaded(stock))
                    return;
                
                var changeInfo = stock.GetMinutesChangeInfo(change.change, change.minutes, change.candles);
                
                string chatId = Settings.TgChatId;
                if (stock.Currency.Equals("RUB", StringComparison.InvariantCultureIgnoreCase) ||
                    stock.Ticker == "TCS")
                    chatId = Settings.TgChatIdRu;

                if (IsSendToTelegramEnabled && long.TryParse(chatId, out var lChatId) && lChatId != 0)
                {
                    if (changeInfo.volPercent >= Settings.MinVolumeDeviationFromDailyAverage)
                        StocksManager.Telegram.PostMessage(changeInfo.message, stock.Ticker, lChatId);
                }

                int lastIdx = change.candles.Length - 1;
                MainModel.AddMessage(
                    MessageKind.MinutesChanges,
                    stock.Ticker,
                    DateTime.Now,
                    stock.DayChange,
                    candle.Volume,
                    $"{stock.Ticker}: {change.change:P2} за {change.minutes} мин. ({change.candles[lastIdx].Open.FormatPrice(stock.Currency),2} → {change.candles[0].Close.FormatPrice(stock.Currency),-2}) "
                );
            }

            if (change.volumeTrigger && Settings.MinXMinutesVolChange > 0 && 
                change.volChange > Settings.MinXMinutesVolChange && (stock.LastAboveVolThresholdCandleTime == null
                || stock.LastAboveVolThresholdCandleTime < candle.Time.AddMinutes(-change.minutes)))
            {
                stock.LastAboveVolThresholdCandleTime = candle.Time;

                if (!await EnsureHistoryLoaded(stock))
                    return;
                
                var changeInfo = stock.GetMinutesVolumeChangeInfo(change.change, change.minutes, change.candles);
                
                string chatId = Settings.TgChatId;
                if (stock.Currency.Equals("RUB", StringComparison.InvariantCultureIgnoreCase) ||
                    stock.Ticker == "TCS")
                    chatId = Settings.TgChatIdRu;

                if (IsSendToTelegramEnabled && long.TryParse(chatId, out var lChatId) && lChatId != 0)
                {
                    if (changeInfo.volPercent >= Settings.MinVolumeDeviationFromDailyAverage)
                        StocksManager.Telegram.PostMessage(changeInfo.message, stock.Ticker, lChatId);
                }

                int lastIdx = change.candles.Length - 1;
                MainModel.AddMessage(
                    MessageKind.VolumeChange,
                    stock.Ticker,
                    DateTime.Now,
                    stock.DayChange,
                    candle.Volume,
                    $"{stock.Ticker}: VOL {change.volChange:P2} за {change.minutes} мин. ({change.candles[lastIdx].Open.FormatPrice(stock.Currency),2} → {change.candles[0].Close.FormatPrice(stock.Currency),-2}) "
                );
            }
        }

        public async Task HandleAsync(IStockModel message, CancellationToken cancellationToken)
        {
            if (Settings.CheckRockets)
                await PerformQuotePercentChangeCheck(message);
        }
    }
}
