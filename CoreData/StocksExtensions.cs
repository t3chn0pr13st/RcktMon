using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CoreData.Interfaces;
using CoreData.Models;
using Tinkoff.Trading.OpenApi.Legacy.Models;

namespace CoreData
{
     public static class StockExt
     {
        public static TimeSpan Elapsed(this DateTime date )
        {
            return DateTime.Now.Subtract(date);
        }

        public static TimeSpan Elapsed(this DateTimeOffset date )
        {
            return DateTimeOffset.Now.Subtract(date);
        }

        public static IStockModel GetStockByMessage(this IMessageModel message, IEnumerable<IStockModel> stocks)
        {
            return stocks.FirstOrDefault(s => s.Ticker == message.Ticker) as IStockModel;
        }

        public static string Arrow(this decimal d, bool plain = false) => plain ? d > 0 ? "↑" : "↓" : d > 0 ? "🔼" : "🔻";

        public static string GetDayChangeInfoText(this IStockModel s)
        {
            return @$"
`{s.Ticker}` {s.TodayDate.ToLocalTime():ddd, dd.MM.yy, H:mm:ss} → {s.LastUpdatePrice:H:mm:ss}
*{s.Ticker}* *({s.Name})*
{s.DayChange.Arrow()} {s.DayChangeF} сегодня ({s.TodayOpenF} → {s.PriceF}) 🔹 vol {s.DayVolume.FormatNumber()} ({s.DayVolumeCostF})
".Trim();
        }

        public static (string message, decimal volPercent) GetMinutesChangeInfo(this IStockModel s, 
            decimal change, int minutes, CandlePayload[] candles)
        {
            decimal sumVolume = 0, volPrice = 0;
            for (int i = 0; i < candles.Length; i++)
            {
                var c = candles[i];
                sumVolume += c.Volume;
                volPrice += (c.Open + c.Close) / 2;
            }
            volPrice = volPrice / candles.Length * sumVolume * s.Lot;
            var volPriceF = volPrice.FormatPrice(s.Currency);

            decimal volPercentOfChange = sumVolume / s.AvgDayVolumePerMonth;
            decimal volPercentOfDay = s.DayVolume / s.AvgDayVolumePerMonth;

            return (@$"
*{s.Ticker}* {change.Arrow()} {change.FormatPercent()} in {minutes}m ({candles[candles.Length-1].Open.FormatPrice(s.Currency, true),2} → {candles[0].Close.FormatPrice(s.Currency, true), -2}) 🔸 Vol {sumVolume} ({volPercentOfChange.FormatPercent()}), {volPriceF}
`{s.Ticker}` *({s.Name})* {candles[candles.Length-1].Time.ToLocalTime():ddd, dd.MM.yy, H:mm} → {s.LastUpdatePrice:H:mm:ss}
{s.DayChange.Arrow()} {s.DayChangeF} today ({s.TodayOpenF} → {s.PriceF}) 🔹 Vol {s.DayVolume} ({volPercentOfDay.FormatPercent()}), {s.DayVolumeCostF}
❇️ Yesterday AVG {s.YesterdayAvgPriceF} ◽️ Vol {s.YesterdayVolume.FormatNumber()} ({s.YesterdayVolumeCostF})
✳️ Month       AVG {s.AvgDayPricePerMonthF} ◽️ Vol {s.AvgDayVolumePerMonth.FormatNumber()} ({s.AvgDayVolumePerMonthCostF})
✴️ Month Total Vol {s.MonthVolume.FormatNumber()} ({s.MonthVolumeCostF})
".Trim(), volPercentOfChange);
        }

        public static (string message, decimal volPercent) GetMinutesVolumeChangeInfo(this IStockModel s, 
            decimal change, int minutes, CandlePayload[] candles)
        {
            decimal sumVolume = 0, volPrice = 0;
            for (int i = 0; i < candles.Length; i++)
            {
                var c = candles[i];
                sumVolume += c.Volume;
                volPrice += (c.Open + c.Close) / 2;
            }
            volPrice = volPrice / candles.Length * sumVolume * s.Lot;
            var volPriceF = volPrice.FormatPrice(s.Currency);

            decimal volPercentOfChange = sumVolume / s.AvgDayVolumePerMonth;
            decimal volPercentOfDay = s.DayVolume / s.AvgDayVolumePerMonth;

            return (@$"
*{s.Ticker}* 🔸 *VOL* {volPercentOfChange.FormatPercent()} in {minutes}m ({sumVolume.FormatNumber()} of {s.AvgDayVolumePerMonth.FormatNumber()} AVG) cost {volPriceF}
`{s.Ticker}` *({s.Name})* {candles[candles.Length-1].Time.ToLocalTime():ddd, dd.MM.yy, H:mm} → {s.LastUpdatePrice:H:mm:ss}
{change.Arrow()} {change.FormatPercent()} in {minutes}m ({candles[candles.Length-1].Open.FormatPrice(s.Currency),2} → {candles[0].Close.FormatPrice(s.Currency), -2})
{s.DayChange.Arrow()} {s.DayChangeF} today ({s.TodayOpenF} → {s.PriceF}) 🔹 Vol {s.DayVolume.FormatNumber()} ({volPercentOfDay.FormatPercent()}), {s.DayVolumeCostF}
❇️ Yesterday AVG {s.YesterdayAvgPriceF} ◽️ Vol {s.YesterdayVolume.FormatNumber()} ({s.YesterdayVolumeCostF})
✳️ Month       AVG {s.AvgDayPricePerMonthF} ◽️ Vol {s.AvgDayVolumePerMonth.FormatNumber()} ({s.AvgDayVolumePerMonthCostF})
✴️ Month Total Vol {s.MonthVolume.FormatNumber()} ({s.MonthVolumeCostF})
".Trim(), volPercentOfChange);
        }

        public static (decimal change, decimal volChange, int minutes, CandlePayload[] candles, bool volumeTrigger) 
            GetLastXMinChange(this IStockModel s, int minutesPrc, int minutesVol, decimal threshold, decimal volThreshold)
        {
            var startTime = DateTime.Now;
            int maxMins = Math.Max(minutesPrc, minutesVol);
            startTime = startTime.AddMilliseconds(-startTime.Millisecond)
                .AddSeconds(-startTime.Second).AddMinutes(-maxMins);

            var lastXminCandles = s.MinuteCandles
                .Where(p => p.Key >= startTime && p.Value.Volume > 10)
                .OrderBy(p => p.Key)
                .Select(p => p.Value)
                .ToList();

            if (lastXminCandles.Count == 0)
                return (0, 0, 0, Array.Empty<CandlePayload>(), false);

            decimal close = lastXminCandles[lastXminCandles.Count-1].Close;
            decimal volsum = 0;
            int numMin = 1;
            decimal change = 0, volChange = 0;
            bool checkVol = s.AvgDayVolumePerMonth > 0;

            HashSet<CandlePayload> candles = new HashSet<CandlePayload>();

            int minChangeMinIdx = lastXminCandles.Count - minutesPrc;
            int minVolMinIdx = lastXminCandles.Count - minutesVol;

            for (int i = lastXminCandles.Count - 1; i >= 0; i--)
            {
                if (candles.Count > 0 && candles.Last().Time.Subtract(lastXminCandles[i].Time).TotalSeconds > 60)
                    break; // игнорируем интервал если между свечами были паузы

                candles.Add(lastXminCandles[i]);
                decimal open = lastXminCandles[i].Open;
                change = (close - open) / open;
                decimal vol = lastXminCandles[i].Volume;
                volsum += vol;
                if (checkVol)
                    volChange = volsum / s.AvgDayVolumePerMonth;

                if (i >= minChangeMinIdx && change > threshold || change < -threshold)
                {
                    var candlesArray = candles.ToArray();
                    numMin = (int)Math.Round(candlesArray[0].Time.Subtract(candlesArray[candlesArray.Length-1].Time)
                        .TotalMinutes, 0) + 1;
                    return (change, volChange, numMin, candles.ToArray(), false);
                }

                if (i >= minVolMinIdx && volChange >= volThreshold)
                {
                    var candlesArray = candles.ToArray();
                    numMin = (int)Math.Round(candlesArray[0].Time.Subtract(candlesArray[candlesArray.Length-1].Time)
                        .TotalMinutes, 0) + 1;
                    return (change, volChange, numMin, candles.ToArray(), true);
                }
            }
            
            return (change, volChange, numMin, candles.ToArray(), false);
        }
    }
}
