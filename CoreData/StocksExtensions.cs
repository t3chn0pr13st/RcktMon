using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CoreData.Interfaces;
using CoreData.Models;
using Tinkoff.Trading.OpenApi.Models;

namespace CoreData
{
     public static class StockExt
    {
        public static IStockModel GetStockByMessage(this IMessageModel message, IEnumerable<IStockModel> stocks)
        {
            return stocks.FirstOrDefault(s => s.Ticker == message.Ticker) as IStockModel;
        }

        public static string Arrow(this decimal d, bool plain = false) => plain ? d > 0 ? "↑" : "↓" : d > 0 ? "🔼" : "🔻";

        public static string GetDayChangeInfoText(this IStockModel s)
        {
            return @$"
`{s.Ticker}` {s.TodayDate.ToLocalTime():ddd, dd.MM.yy, H:mm:ss} → {s.LastUpdate:H:mm:ss}
*{s.Ticker}* *({s.Name})*
{s.DayChange.Arrow()} {s.DayChangeF} сегодня ({s.TodayOpenF} → {s.PriceF}) 🔹 vol {s.DayVolume.FormatNumber()} ({s.DayVolumeCostF})
".Trim();
        }

        public static (decimal change, int minutes, CandlePayload[] candles) GetLast10MinChange(this IStockModel s, decimal threshold)
        {
            var startTime = DateTime.Now;
            startTime = startTime.AddMilliseconds(-startTime.Millisecond)
                .AddSeconds(-startTime.Second).AddMinutes(-10);

            var last10minCandles = s.MinuteCandles
                .Where(p => p.Key >= startTime)
                .OrderBy(p => p.Key)
                .Select(p => p.Value)
                .ToList();

            if (last10minCandles.Count == 0)
                return (0, 0, Array.Empty<CandlePayload>());

            decimal close = last10minCandles[^1].Close;
            int numMin = 0;
            decimal change = 0;

            HashSet<CandlePayload> candles = new HashSet<CandlePayload>();
            for (int i = last10minCandles.Count - 1; i >= 0; i--)
            {
                candles.Add(last10minCandles[i]);
                decimal open = last10minCandles[i].Open;
                change = (close - open) / open;
                if (change > threshold || change < -threshold)
                {
                    var candlesArray = candles.ToArray();
                    numMin = (int)Math.Round(candlesArray[0].Time.Subtract(candlesArray[^1].Time)
                        .TotalMinutes, 0) + 1;
                    return (change, numMin, candles.ToArray());
                }
            }
            
            return (change, numMin, candles.ToArray());
        }

        public static bool IsLast10minCandlesExceedThreshold(this IStockModel s, decimal threshold)
        {
            return Math.Abs(s.GetLast10MinChange(threshold).change) > Math.Abs(threshold);
        }

        public static (string message, decimal volPercent) GetMinutesChangeInfo(this IStockModel s, decimal change, int minutes, CandlePayload[] candles)
        {
            decimal sumVolume = 0, volPrice = 0;
            for (int i = 0; i < candles.Length; i++)
            {
                var c = candles[i];
                sumVolume += c.Volume;
                volPrice += (c.Open + c.Close) / 2;
            }
            volPrice = volPrice / candles.Length * sumVolume;
            var volPriceF = volPrice.FormatPrice(s.Currency);

            decimal volPercentOfChange = sumVolume / s.AvgDayVolumePerMonth;
            decimal volPercentOfDay = s.DayVolume / s.AvgDayVolumePerMonth;

            return (@$"
`{s.Ticker}` {candles[^1].Time.ToLocalTime():ddd, dd.MM.yy, H:mm} → {s.LastUpdate:H:mm:ss}
*{s.Ticker}* *({s.Name})*
{change.Arrow()} {change.FormatPercent()} in {minutes}m ({candles[^1].Open.FormatPrice(s.Currency),2} → {candles[0].Close.FormatPrice(s.Currency), -2}) 🔸 Vol {sumVolume} ({volPercentOfChange.FormatPercent()}), {volPriceF}
{s.DayChange.Arrow()} {s.DayChangeF} today ({s.TodayOpenF} → {s.PriceF}) 🔹 Vol {s.DayVolume} ({volPercentOfDay.FormatPercent()}), {s.DayVolumeCostF}
❇️ Yesterday AVG {s.YesterdayAvgPriceF} ◽️ Vol {s.YesterdayVolume.FormatNumber()} ({s.YesterdayVolumeCostF})
✳️ Month       AVG {s.AvgDayPricePerMonthF} ◽️ Vol {s.AvgDayVolumePerMonth.FormatNumber()} ({s.AvgDayVolumePerMonthCostF})
✴️ Month Total Vol {s.MonthVolume.FormatNumber()} ({s.MonthVolumeCostF})
".Trim(), volPercentOfChange);
        }
    }
}
