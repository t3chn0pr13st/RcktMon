using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreData.Interfaces;
using CoreData.Models;
using CoreData.Settings;
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

        private bool _updateConflict = false;
        private DateTime _lastConflictTime = DateTime.MinValue;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private readonly IServiceProvider _services;
        private readonly ILogger<TelegramManager> _logger;
        private readonly IMainModel _mainModel;
        private readonly IEventAggregator2 _eventAggregator;
        private readonly HttpClient _httpClient = new HttpClient();

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
            if (String.IsNullOrWhiteSpace(Settings.ChartUrlTemplate) || Settings.ChartUrlTemplate == "!disabled")
                return null;

            var stock = _mainModel.Stocks[ticker];
            if (stock != null && !ticker.Equals("TCS", StringComparison.InvariantCultureIgnoreCase) 
                              && stock.Currency.Equals("USD", StringComparison.InvariantCultureIgnoreCase))
            {
                var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var url = Settings.ChartUrlTemplate
                    .Replace("{0}", ticker)
                    .Replace("{ticker}", ticker)
                    .Replace("{unixTime}", unixTime.ToString());
                return url;
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

                    await CheckIncomingBotMessages();
                }
                await Task.Delay(100);

                await CheckIncomingBotMessages();
            }
        }

        private int? _lastUpdateId = null;

        private async Task CheckIncomingBotMessages()
        {
            if (_updateConflict)
            {
                if (DateTime.Now.Subtract(_lastConflictTime).TotalSeconds > 10)
                    _updateConflict = false;
                return;
            }

            try
            {
                var updates = await _bot.GetUpdatesAsync(_lastUpdateId + 1);
                foreach (var update in updates)
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                        break;

                    _lastUpdateId = update.Id;
                    if (update.CallbackQuery?.Data is String callbackData)
                    {
                        var dataArr = callbackData.Split(";");
                        Debug.WriteLine($"{update.Id} data {callbackData} from {update.CallbackQuery.From}");
                        switch (dataArr[0])
                        {
                            case "stig":
                                if (dataArr.Length > 2)
                                {
                                    var senderId = update.CallbackQuery.From.Id;
                                    var ticker = dataArr[1];
                                    var groupNum = int.Parse(dataArr[2]);
                                    var postUrl = Settings.TgCallbackUrl;
                                    var token = Settings.KvtToken;
                                    string result = "Ошибка: ";
                                    if (!String.IsNullOrWhiteSpace(postUrl) && !String.IsNullOrWhiteSpace(token))
                                    {
                                        var postObj = new
                                        {
                                            id = senderId,
                                            ticker,
                                            group = groupNum,
                                            token
                                        };
                                        try
                                        {
                                            var body = JsonSerializer.Serialize(postObj);
                                            var resp = await _httpClient.PostAsync(postUrl, new StringContent(
                                                body, Encoding.UTF8, "application/json"
                                            ));
                                            var reason = resp.ReasonPhrase;
                                            if (string.IsNullOrWhiteSpace(reason))
                                                reason = $"{(int)resp.StatusCode} {resp.StatusCode}";
                                            var content = await resp.Content.ReadAsStringAsync();
                                            if (content.Contains("Client is not connected"))
                                                content = "ОШИБКА: У вас не запущен терминал с расширением KvaloodTools";
                                            Debug.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {content}");
                                            if (resp.IsSuccessStatusCode)
                                                result = null; // await resp.Content.ReadAsStringAsync();
                                            else
                                                result = $"{content}";
                                        }
                                        catch (Exception ex)
                                        {
                                            result = $"{result}{ex.Message}";
                                        }
                                    }
                                    else
                                    {
                                        result += "не задан токен KvaloodTools";
                                    }


                                    try
                                    {
                                        await _bot.AnswerCallbackQueryAsync(update.CallbackQuery.Id, result);
                                    }
                                    catch
                                    {

                                    }
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.StartsWith("Conflict"))
                {
                    _updateConflict = true;
                    if (DateTime.Now.Subtract(_lastConflictTime).TotalMinutes > 10)
                        _mainModel.AddErrorMessage("Токен телеграм бота уже используется: проверка входящий сообщений для бота отключена на 10 секунд.");
                    _lastConflictTime = DateTime.Now;
                }
            }
        }
    }
}
