using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TradeApp.Data
{
    public class TelegramManager
    {
        private readonly TelegramBotClient _bot;
        private readonly long _chatId;

        private readonly Thread _messageQueueThread;
        private readonly ConcurrentQueue<string> _botMessageQueue = new ConcurrentQueue<string>();

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
            catch
            {

            }
        }

        public void PostMessage(string text)
        {
            Debug.WriteLine("Telegram message:\r\n" + text + "\r\n");
            if (IsEnabled)
                _botMessageQueue.Enqueue(text);
        }

        private void BotMessageQueueLoop(object o)
        {
            var messageBuffer = new StringBuilder(2048);
            while (!CancellationPending)
            {
                while (messageBuffer.Length < 2048 && _botMessageQueue.TryDequeue(out string msg))
                {
                    if (messageBuffer.Length > 0)
                        messageBuffer.AppendLine("--------------------------------");
                    messageBuffer.AppendLine(msg);
                }
                _bot.SendTextMessageAsync(_chatId, messageBuffer.ToString());
                messageBuffer.Clear();
                Thread.Sleep(1000);
            }
        }
    }
}
