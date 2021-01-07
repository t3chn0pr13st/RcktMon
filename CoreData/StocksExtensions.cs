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

        public static string Arrow(this decimal d) => d > 0 ? "🔼" : "🔻";

        public static string GetDayChangeInfoText(this IStockModel s)
        {
            return @$"
`{s.Ticker}` {s.TodayDate.ToLocalTime():ddd, dd.MM.yy, H:mm:ss} → {s.LastUpdate:H:mm:ss}
*{s.Ticker}* *({s.Name})*
{s.DayChange.Arrow()} {s.DayChangeF} сегодня ({s.TodayOpenF} → {s.PriceF}) vol {Math.Ceiling(s.DayVolume)} ({s.DayVolumeCostF})
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

            decimal volPercentOfChange = Math.Round(sumVolume / s.AvgDayVolumePerMonth * 100, 2);
            decimal volPercentOfDay = Math.Round(s.DayVolume / s.AvgDayVolumePerMonth * 100, 2);

            return (@$"
`{s.Ticker}` {candles[^1].Time.ToLocalTime():ddd, dd.MM.yy, H:mm} → {candles[0].Time.ToLocalTime():H:mm:ss}
*{s.Ticker}* *({s.Name})*
{change.Arrow()} {change.FormatPercent()} за {minutes} мин. ({candles[^1].Open.FormatPrice(s.Currency),2} → {candles[0].Close.FormatPrice(s.Currency), -2}) vol {sumVolume} ({volPercentOfChange.FormatPercent()}% of avg), {volPriceF}
{s.DayChange.Arrow()} {s.DayChangeF} сегодня ({s.TodayOpenF} → {s.PriceF}) vol {s.DayVolume} ({volPercentOfDay}% of avg), {s.DayVolumeCostF}
Средная цена вчера: {s.YesterdayAvgPriceF} объем {s.YesterdayVolume} лотов ({s.YesterdayVolumeCostF})
Средняя цена за месяц: {s.AvgDayPricePerMonthF} объем {s.AvgDayVolumePerMonth} лотов ({s.AvgDayVolumePerMonthCostF})
Общий объем за месяц {s.MonthVolume} лотов ({s.MonthVolumeCostF})
".Trim(), volPercentOfChange);
//            return @$"
//Цена {Ticker} ({Name}) изменилась на {change:P2} за {minutes} мин. 
//({candles[^1].Time.ToLocalTime():dd.MM.yy H:mm:ss} - {candles[0].Time.ToLocalTime(): H:mm:ss} c {candles[^1].Open.FormatPrice(Currency)} до {candles[0].Close.FormatPrice(Currency)}) 
//Объем торгов ({minutes} мин) {sumVolume} стоимостью {volPriceF}
//Курс на начало дня: {TodayOpenF}; Текущий: {PriceF}; Изменение за день: {DayChangeF} 
//Объем торгов за день: {DayVolume} акций общей стоимостью (в среднем) {DayVolumeCostF}
//Объём за прошлый день: {YesterdayVolume} акций стоимостью {YesterdayVolumeCostF}; 
//Средний курс за прошлый день: {YesterdayAvgPriceF}
//Средний дневной объём за месяц: {AvgDayVolumePerMonth} акций стоимостью {AvgDayVolumePerMonthCostF} 
//Средний дневной курс за месяц: {AvgDayPricePerMonthF}
//Объем за месяц (не включая сегодня): {MonthVolume} стоимостью {MonthVolumeCostF}
//".Trim();
        }
    }
}
