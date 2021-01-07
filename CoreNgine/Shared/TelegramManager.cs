using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreData.Interfaces;
using CoreNgine.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;

namespace CoreNgine.Shared
{
    public class TelegramManager
    {
        private readonly TelegramBotClient _bot;
        private readonly long _chatId;

        const string TinkoffInvestStocksUrl = "https://www.tinkoff.ru/invest/stocks/{0}/";
        

        private Task _messageQueueLoopTask;
        private readonly ConcurrentQueue<(string text, string ticker)> _botMessageQueue = new ConcurrentQueue<(string text, string ticker)>();

        public bool IsEnabled => _mainModel.IsTelegramEnabled;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private readonly IServiceProvider _services;
        private readonly ILogger<TelegramManager> _logger;
        private readonly IMainModel _mainModel;

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
        }

        public TelegramManager(IServiceProvider serviceProvider, string apiToken, long chatId)
        {
            _services = serviceProvider;
            _mainModel = serviceProvider.GetRequiredService<IMainModel>();
            _logger = (ILogger<TelegramManager>) serviceProvider.GetService(typeof(ILogger<TelegramManager>));
            try
            {
                _bot = new TelegramBotClient(apiToken);
                _chatId = chatId;

                _messageQueueLoopTask = Task.Factory
                    .StartNew(() => BotMessageQueueLoopAsync()
                            .ConfigureAwait(false),
                        _cancellationTokenSource.Token,
                        TaskCreationOptions.LongRunning, 
                        TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                _logger.LogError("Telegram init error: {exception}", ex.Message);
            }
        }

        public void PostMessage(string text, string ticker)
        {
            _logger.LogTrace("Telegram message: {text}", text);
            if (IsEnabled)
            {
                _botMessageQueue.Enqueue((text, ticker));
            }
        }

        private string GetStockChart(string ticker)
        {
            var stock = _mainModel.Stocks.FirstOrDefault(s => s.Ticker == ticker);
            if (stock != null && stock.Currency.Equals("USD", StringComparison.InvariantCultureIgnoreCase))
            {
                return $"https://www.stockscores.com/chart.asp?TickerSymbol={ticker}&TimeRange=3&Interval=10&Volume=1&ChartType=CandleStick&Stockscores=1&ChartWidth=1100&ChartHeight=480&LogScale=None&Band=None&avgType1=None&movAvg1=&avgType2=None&movAvg2=&Indicator1=RSI&Indicator2=MACD&Indicator3=MDX&Indicator4=None&endDate=2021-1-7&CompareWith=&entryPrice=&stopLossPrice=&candles=redgreen";
            }

            return null;
        }

        private async Task BotMessageQueueLoopAsync()
        {
            var cancellationToken = _cancellationTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                while (!cancellationToken.IsCancellationRequested && _botMessageQueue.TryDequeue(out var msg))
                {
                    InlineKeyboardMarkup markup = null;
                    bool sent = false;
                    if (msg.ticker != null)
                    {
                        markup = new InlineKeyboardMarkup(
                            new[]
                            {
                                new[]
                                {
                                    InlineKeyboardButton.WithUrl($"Открыть {msg.ticker} в Инвестициях",
                                        String.Format(TinkoffInvestStocksUrl, msg.ticker)),
                                }
                            });
                        var chartUrl = GetStockChart(msg.ticker);
                        if (chartUrl != null)
                        {
                            sent = true;
                            await _bot.SendPhotoAsync(_chatId, new InputOnlineFile(chartUrl), msg.text, ParseMode.Markdown, replyMarkup: markup);
                        }
                    }
                    if (!sent)
                    {
                        await _bot.SendTextMessageAsync(_chatId, msg.text, replyMarkup: markup, parseMode: ParseMode.Markdown);
                    }
                    await Task.Delay(200);
                }

                await Task.Delay(100);
            }
        }
    }
}
