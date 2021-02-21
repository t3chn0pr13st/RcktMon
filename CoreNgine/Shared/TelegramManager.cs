using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CoreData.Interfaces;
using CoreData.Models;
using CoreNgine.Infra;
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
        private TelegramBotClient _bot;
        private readonly long _chatId;
        private readonly string _apiToken;

        internal const string TinkoffInvestStocksUrl = "https://www.tinkoff.ru/invest/stocks/{0}/";
        

        private Task _messageQueueLoopTask;
        private readonly ConcurrentQueue<TelegramMessage> _botMessageQueue = new ConcurrentQueue<TelegramMessage>();

        public bool IsEnabled => Settings.IsTelegramEnabled;
        public INgineSettings Settings { get; }

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private readonly IServiceProvider _services;
        private readonly ILogger<TelegramManager> _logger;
        private readonly IMainModel _mainModel;
        private readonly IEventAggregator2 _eventAggregator;

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
        }

        public TelegramManager(IServiceProvider serviceProvider, string apiToken, long chatId)
        {
            _apiToken = apiToken;
            _services = serviceProvider;
            _mainModel = serviceProvider.GetRequiredService<IMainModel>();
            Settings = serviceProvider.GetRequiredService<ISettingsProvider>().Settings;
            _logger = (ILogger<TelegramManager>) serviceProvider.GetService(typeof(ILogger<TelegramManager>));
            _eventAggregator = (IEventAggregator2)serviceProvider.GetService(typeof(IEventAggregator2));
            try
            {
                _bot = new TelegramBotClient(_apiToken);
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

        public void PostMessage(TelegramMessage message )
        {
            _logger.LogTrace("Telegram message: {text}", message.Text);
            if (IsEnabled)
            {
                _botMessageQueue.Enqueue(message);
            }
        }

        public void PostMessage(string text, string ticker, long chatId = 0)
        {
            if (chatId == 0)
                chatId = _chatId;
            _logger.LogTrace("Telegram message: {text}", text);
            var message = new TelegramMessage(ticker, text, chatId);
            if (IsEnabled)
            {
                _botMessageQueue.Enqueue(message);
            }
        }

        internal string GetStockChart(string ticker)
        {
            var stock = _mainModel.Stocks[ticker];
            if (stock != null && !ticker.Equals("TCS", StringComparison.InvariantCultureIgnoreCase) 
                              && stock.Currency.Equals("USD", StringComparison.InvariantCultureIgnoreCase))
            {
                return $"https://stockcharts.com/c-sc/sc?s={ticker}&p=D&yr=0&mn=3&dy=0&i=t8988066255c&r={DateTime.Now.ToFileTimeUtc()}";
            }

            return null;
        }

        internal async Task<bool?> ExecuteWithBot(Func<TelegramBotClient, Task> botAction, TelegramMessage messageForEnqueueOnTooMuchRequests)
        {
            try
            {
                await botAction(_bot);
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx.Message);
                if (httpEx.Message.Contains("429"))
                {
                    await Task.Delay(1000);
                    if (messageForEnqueueOnTooMuchRequests != null)
                        _botMessageQueue.Enqueue(messageForEnqueueOnTooMuchRequests);
                    return false;
                }
            }
            catch
            {
                return null;
            }
            return true;
        }

        private async Task BotMessageQueueLoopAsync()
        {
            var cancellationToken = _cancellationTokenSource.Token;

            while (!cancellationToken.IsCancellationRequested)
            {
                while (!cancellationToken.IsCancellationRequested && _botMessageQueue.TryDequeue(out var msg))
                {
                    try
                    {
                        await msg.Send(this);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex.Message);
                        _bot = new TelegramBotClient(_apiToken);
                    }

                    await _eventAggregator.PublishOnCurrentThreadAsync(new CommonInfoMessage() { TelegramMessageQuery = _botMessageQueue.Count });
                    await Task.Delay( 300 );
                    
                }
                await Task.Delay(100);
            }
        }
    }
}
