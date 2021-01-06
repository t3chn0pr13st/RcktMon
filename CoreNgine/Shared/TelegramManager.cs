using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
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

        public bool IsEnabled { get; set; }

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private readonly IServiceProvider _services;
        private readonly ILogger<TelegramManager> _logger;

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
        }

        public TelegramManager(IServiceProvider serviceProvider, string apiToken, long chatId)
        {
            _services = serviceProvider;
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

        private async Task BotMessageQueueLoopAsync()
        {
            var cancellationToken = _cancellationTokenSource.Token;
            while (!cancellationToken.IsCancellationRequested)
            {
                while (!cancellationToken.IsCancellationRequested && _botMessageQueue.TryDequeue(out var msg))
                {
                    InlineKeyboardMarkup markup = null;
                    if (msg.ticker != null)
                        markup = new InlineKeyboardMarkup(
                            new[]
                            {
                                new[]
                                {
                                    InlineKeyboardButton.WithUrl($"Открыть {msg.ticker} в Инвестициях",
                                        String.Format(TinkoffInvestStocksUrl, msg.ticker)),
                                }
                            });

                    _bot.SendTextMessageAsync(_chatId, msg.text, replyMarkup: markup, parseMode: ParseMode.Markdown);
                    await Task.Delay(500);
                }

                await Task.Delay(100);
            }
        }
    }
}
