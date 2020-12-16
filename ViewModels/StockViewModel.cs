using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Caliburn.Micro;

using Tinkoff.Trading.OpenApi.Models;

namespace TradeApp.ViewModels
{
    public class StockViewModel : PropertyChangedBase
    {
        private BindableCollection<CandleViewModel> _candles = null;

        public string Figi { get; set; }
        public string Name { get; set; }
        public string Ticker { get; set; }
        public string Currency { get; set; }
        public DateTime TodayDate { get; set; }
        public decimal TodayOpen { get; set; }
        public decimal Price { get; set; }
        public decimal DayChange { get; set; }
        public decimal DayVolume { get; set; }
        public decimal DayVolumeCost => DayVolume * AvgPrice;
        public decimal AvgPrice => (TodayOpen + Price) / 2;
        public string Status { get; set; }
        public DateTime LastUpdate { get; set; }
        public DateTime? LastAboveThreshholdDate { get; set; }
        public DateTime? LastAboveThreshholdCandleTime { get; set; }

        public decimal MonthOpen { get; set; }
        public decimal MonthLow { get; set; }
        public decimal MonthHigh { get; set; }
        public decimal MonthAvg => (MonthHigh + MonthLow) / 2;
        public decimal MonthVolume { get; set; }
        public decimal MonthVolumeCost { get; set; }
        public decimal AvgDayVolumePerMonth { get; set; }
        public decimal AvgDayPricePerMonth { get; set; }
        public decimal AvgDayVolumePerMonthCost { get; set; }
        public decimal YesterdayVolume { get; set; }
        public decimal YesterdayVolumeCost { get; set; }
        public decimal YesterdayAvgPrice { get; set; }

        public string TodayOpenF => TodayOpen.FormatPrice(Currency);
        public string PriceF => Price.FormatPrice(Currency);
        public string AvgPriceF => AvgPrice.FormatPrice(Currency);
        public string YesterdayVolumeCostF => YesterdayVolumeCost.FormatPrice(Currency);
        public string YesterdayAvgPriceF => YesterdayAvgPrice.FormatPrice(Currency);
        public string MonthVolumeCostF => MonthVolumeCost.FormatPrice(Currency);
        public string DayChangeF => DayChange.ToString("P2");
        public string DayVolumeCostF => DayVolumeCost.FormatPrice(Currency);
        public string AvgDayVolumePerMonthCostF => AvgDayVolumePerMonthCost.FormatPrice(Currency);
        public string AvgDayPricePerMonthF => AvgDayPricePerMonth.FormatPrice(Currency);
        public string AvgMonthPriceF => MonthAvg.FormatPrice(Currency);

        public string GetDayChangeInfoText()
        {
            return @$"
Цена {Ticker} ({Name}) изменилась на {DayChangeF} за день. 
Курс на начало дня: {TodayOpenF}; Текущий: {PriceF}; 
Объем торгов за день: {DayVolume} акций общей стоимостью (в среднем) {DayVolumeCostF}
".Trim();
        }

        public string GetMinutesChangeInfoText(decimal change, int minutes, CandlePayload[] candles)
        {
            decimal sumVolume = 0, volPrice = 0;
            for (int i = 0; i < candles.Length; i++)
            {
                var c = candles[i];
                sumVolume += c.Volume;
                volPrice += (c.Open + c.Close) / 2;
            }
            volPrice = volPrice / candles.Length * sumVolume;
            var volPriceF = volPrice.FormatPrice(Currency);
            return @$"
`{Ticker}` ({Name}) ({candles[^1].Time.ToLocalTime():dd.MM.yy H:mm:ss} - {candles[0].Time.ToLocalTime(): H:mm:ss}
↑ {minutes} min. {change:P2} {candles[^1].Open.FormatPrice(Currency)} → {candles[0].Close.FormatPrice(Currency)}) 
Объем торгов ({minutes} мин) {sumVolume} стоимостью {volPriceF}
Курс на начало дня: {TodayOpenF}; Текущий: {PriceF}; Изменение за день: {DayChangeF} 
Объем торгов за день: {DayVolume} акций общей стоимостью (в среднем) {DayVolumeCostF}
Объём за прошлый день: {YesterdayVolume} акций стоимостью {YesterdayVolumeCostF}; 
Средний курс за прошлый день: {YesterdayAvgPriceF}
Средний дневной объём за месяц: {AvgDayVolumePerMonth} акций стоимостью {AvgDayVolumePerMonthCostF} 
Средний дневной курс за месяц: {AvgDayPricePerMonthF}
Объем за месяц (не включая сегодня): {MonthVolume} стоимостью {MonthVolumeCostF}
".Trim();
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

        public ConcurrentDictionary<DateTime, CandlePayload> MinuteCandles { get; } = new ConcurrentDictionary<DateTime, CandlePayload>();

        public void LogCandle(CandlePayload candle)
        {
            var t = DateTime.Now;
            t = t.AddMilliseconds(-t.Millisecond).AddSeconds(-t.Second);
            MinuteCandles[t] = candle;
            if (MinuteCandles.Count > 100)
            {
                MinuteCandles.OrderBy(p => p.Key).Take(50).ToList()
                    .ForEach(p => MinuteCandles.TryRemove(p.Key, out _));
            }
        }

        public (decimal change, int minutes, CandlePayload[] candles) GetLast10MinChange(decimal threshhold)
        {
            var startTime = DateTime.Now;
            startTime = startTime.AddMilliseconds(-startTime.Millisecond)
                .AddSeconds(-startTime.Second).AddMinutes(-10);

            var last10minCandles = MinuteCandles
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
                if (change > threshhold || change < -threshhold)
                {
                    var candlesArray = candles.ToArray();
                    numMin = (int)Math.Round(candlesArray[0].Time.Subtract(candlesArray[^1].Time)
                        .TotalMinutes, 0) + 1;
                    return (change, numMin, candles.ToArray());
                }
            }
            
            return (change, numMin, candles.ToArray());
        }

        public bool IsLast10minCandlesExceedThreshhold(decimal threshhold)
        {
            return Math.Abs(GetLast10MinChange(threshhold).change) > Math.Abs(threshhold);
        }

        public BindableCollection<CandleViewModel> Candles => _candles ??= new BindableCollection<CandleViewModel>();
    }

    public static class PriceExt
    {
        public static string FormatPrice(this decimal price, string currency)
        {
            string mod = "";
            price = Math.Round(price, 2);
            if (price > 1_000_000_000)
            {
                mod = " млрд.";
                price /= 1_000_000_000;
                price = Math.Round(price, 4);
            }
            else if (price > 1_000_000)
            {
                mod = " млн.";
                price /= 1_000_000;
                price = Math.Round(price, 3);
            }
            else if (price > 10000)
            {
                mod = " тыс.";
                price /= 1000;
                price = Math.Round(price, 2);
            }

            switch (currency.ToUpper())
            {
                case "RUB":
                    return $"{price}{mod} руб.";
                case "USD":
                    return $"${price}{mod}";
                default:
                    return $"{currency} {price}{mod}";
            }
        }

    }

    public class CandleViewModel : PropertyChangedBase
    {
        public CandleInterval Interval { get; set; }
        public decimal Open { get; set; }
        public decimal Close { get; set; }
        public decimal Low { get; set; }
        public decimal High { get; set; }
        public DateTime Time { get; set; }
    }
}
