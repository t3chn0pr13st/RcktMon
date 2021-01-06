using System;
using System.Collections.Generic;
using System.Linq;
using Caliburn.Micro;
using CoreData.Interfaces;
using CoreNgine.Models;
using Tinkoff.Trading.OpenApi.Models;

namespace RcktMon.ViewModels
{
    public class StockViewModel : PropertyChangedBase, IStockModel
    {
        private HashSet<CandleViewModel> _candles = null;
        public string Figi { get; set; }
        public string Name { get; set; }
        public string Ticker { get; set; }
        public string Isin { get; set; }
        public string Currency { get; set; }
        public int Lot { get; set; }
        public decimal MinPriceIncrement { get; set; }

        public DateTime TodayDate { get; set; }
        public decimal TodayOpen { get; set; }
        public decimal Price { get; set; }
        public decimal DayChange { get; set; }
        public decimal DayVolume { get; set; }
        public decimal DayVolumeCost => DayVolume * AvgPrice;
        public decimal AvgPrice => (TodayOpen + Price) / 2;
        public string Status { get; set; }
        public DateTime LastUpdate { get; set; }
        public DateTime? LastAboveThresholdDate { get; set; }
        public DateTime? LastAboveThresholdCandleTime { get; set; }

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

        public IDictionary<DateTime, CandlePayload> MinuteCandles { get; } = new Dictionary<DateTime, CandlePayload>();

        public void LogCandle(CandlePayload candle)
        {
            var t = DateTime.Now;
            t = t.AddMilliseconds(-t.Millisecond).AddSeconds(-t.Second);
            MinuteCandles[t] = candle;
            if (MinuteCandles.Count > 100)
            {
                MinuteCandles.OrderBy(p => p.Key).Take(50).ToList()
                    .ForEach(p => MinuteCandles.Remove(p.Key, out _));
            }
        }

        public IEnumerable<ICandleModel> Candles => _candles ??= new HashSet<CandleViewModel>();

        public void AddCandle(CandlePayload candle)
        {
            if (_candles == null)
                _candles = new HashSet<CandleViewModel>();

            _candles.Add(new CandleViewModel()
            {
                Interval = candle.Interval,
                Open = candle.Open,
                Close = candle.Close,
                Low = candle.Low,
                High = candle.High,
                Time = candle.Time
            });
        }
    }

    public class CandleViewModel : PropertyChangedBase, ICandleModel
    {
        public CandleInterval Interval { get; set; }
        public decimal Open { get; set; }
        public decimal Close { get; set; }
        public decimal Low { get; set; }
        public decimal High { get; set; }
        public DateTime Time { get; set; }
    }
}
