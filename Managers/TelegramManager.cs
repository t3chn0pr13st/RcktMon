using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TradeApp.Data
{
    public class TelegramManager
    {
        private readonly TelegramBotClient _bot;
        private readonly long _chatId;

        const string TinkoffInvestStocksUrl = "https://www.tinkoff.ru/invest/stocks/{0}/";

        private readonly Thread _messageQueueThread;
        private readonly ConcurrentQueue<(string text, string ticker)> _botMessageQueue = new ConcurrentQueue<(string text, string ticker)>();

        public bool IsEnabled { get; set; }

        public bool CancellationPending { get; set; }

        public TelegramManager(string apiToken, long chatId)
        {
            try
            {
                _bot = new TelegramBotClient(apiToken);
                _chatId = chatId;

                _messageQueueThread = new Thread(BotMessageQueueLoop)
                {
                    IsBackground = true,
                    Name = "TelegramMessageQueueLoopThread"
                };
                _messageQueueThread.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        public void PostMessage(string text, string ticker)
        {
            Debug.WriteLine("Telegram message:\r\n" + text + "\r\n");
            if (IsEnabled)
            {
                _botMessageQueue.Enqueue((text, ticker));
            }
        }

        private void BotMessageQueueLoop(object o)
        {
            while (!CancellationPending)
            {
                if (_botMessageQueue.TryDequeue(out var msg))
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

                    Thread.Sleep(500);
                }
            }
        }
    }
}
