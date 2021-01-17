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

        public bool IsEnabled => Settings.IsTelegramEnabled;
        public INgineSettings Settings { get; }

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
            Settings = serviceProvider.GetRequiredService<ISettingsProvider>().Settings;
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
            var stock = _mainModel.Stocks[ticker];
            if (stock != null && stock.Currency.Equals("USD", StringComparison.InvariantCultureIgnoreCase))
            {
                return $"https://stockcharts.com/c-sc/sc?s={ticker}&p=D&yr=0&mn=3&dy=0&i=t8988066255c&r={DateTime.Now.ToFileTimeUtc()}";
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
                            try
                            {
                                await _bot.SendPhotoAsync(_chatId, new InputOnlineFile(chartUrl), msg.text,
                                    ParseMode.Markdown, replyMarkup: markup);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex.Message);
                                sent = false;
                            }
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
