using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;

namespace CoreNgine.Shared
{
    public class TelegramMessage
    {
        public long ChatId { get; set; }
        public string Ticker { get; set; }
        public string Text { get; set; }
        public bool AddTickerImage { get; set; } = true;
        public ParseMode MessageMode { get; set; } = ParseMode.Markdown;


        public TelegramMessage( string ticker, string text, long chatId )
        {
            Text = text;
            Ticker = ticker;
            ChatId = chatId;
        }

        internal virtual async Task Send(TelegramManager tgManager)
        {
            InlineKeyboardMarkup markup = null;
            bool? sentState = null;

            if (Ticker != null)
            {
                markup = new InlineKeyboardMarkup(
                    new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithUrl($"Открыть {Ticker} в Инвестициях",
                                String.Format(TelegramManager.TinkoffInvestStocksUrl, Ticker))
                        }
                    });
                var chartUrl = tgManager.GetStockChart(Ticker);
                if (chartUrl != null && AddTickerImage)
                {
                    sentState = await tgManager.ExecuteWithBot(async (bot)
                        => await bot.SendPhotoAsync(ChatId, new InputOnlineFile(chartUrl),
                            Text, ParseMode.Markdown, replyMarkup: markup), this);
                    if (sentState == false)
                        return;
                }
            }
            if (sentState == null)
            {
                await tgManager.ExecuteWithBot(async (bot)
                    => await bot.SendTextMessageAsync(ChatId, Text, replyMarkup: markup, parseMode: ParseMode.Markdown), this);
            }
        }
    }
}
