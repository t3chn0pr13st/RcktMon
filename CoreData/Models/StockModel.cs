using System;
using System.Collections.Generic;
using System.Linq;
using CoreData.Interfaces;
using Tinkoff.Trading.OpenApi.Models;

namespace CoreData.Models
{
    public class StockModel : IStockModel
    {
        private HashSet<CandleModel> _candles = null;
        public string Figi { get; set; }
        public string Name { get; set; }
        public string Ticker { get; set; }
        public string Isin { get; set; }
        public string Currency { get; set; }
        public bool IsDead { get; set; }
        public bool CanBeShorted { get; set; }
        public string Exchange { get;set; }
        public int Lot { get; set; }
        public decimal LimitDown { get; set; }
        public decimal LimitUp { get; set; }
        public decimal MinPriceIncrement { get; set; }
        public DateTime TodayDate { get; set; }
        public decimal TodayOpen { get; set; }
        public decimal Price { get; set; }
        public decimal BestBidSpb { get; set; }
        public decimal BestAskSpb { get; set; }
        public decimal DayChange { get; set; }
        public decimal DayVolume { get; set; }
        public decimal DayVolumeCost => DayVolume * AvgPrice;
        public decimal AvgPrice => (TodayOpen + Price) / 2;
        public string Status { get; set; }
        public DateTime LastUpdateOrderbook { get; set; }
        public DateTime LastUpdatePrice { get; set; }
        public DateTime LastResubscribeAttempt { get; set; }
        public DateTime? LastAboveThresholdDate { get; set; }
        public DateTime? LastAboveThresholdCandleTime { get; set; }
        public DateTime? LastAboveVolThresholdCandleTime { get; set; }

        public decimal PriceUSA { get; set; }
        public decimal BidUSA { get; set; }
        public decimal AskUSA { get; set; }
        public decimal BidSizeUSA { get; set; }
        public decimal AskSizeUSA { get; set; }
        public DateTime? LastTradeUSA { get; set; }
        public DateTime? LastUpdateUSA { get; set; }
        public decimal DiffPercentUSA { get; set; }
        public decimal USBidRUAskDiff { get; set; }
        public decimal RUBidUSAskDiff { get; set; }

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

        public string TodayOpenF => TodayOpen.FormatPrice(Currency, true);
        public string PriceF => Price.FormatPrice(Currency, true);
        public string AvgPriceF => AvgPrice.FormatPrice(Currency);
        public string YesterdayVolumeCostF => YesterdayVolumeCost.FormatPrice(Currency);
        public string YesterdayAvgPriceF => YesterdayAvgPrice.FormatPrice(Currency);
        public string MonthVolumeCostF => MonthVolumeCost.FormatPrice(Currency);
        public string DayChangeF => DayChange.ToString("P2");
        public string DayVolumeCostF => DayVolumeCost.FormatPrice(Currency);
        public string AvgDayVolumePerMonthCostF => AvgDayVolumePerMonthCost.FormatPrice(Currency);
        public string AvgDayPricePerMonthF => AvgDayPricePerMonth.FormatPrice(Currency);
        public string AvgMonthPriceF => MonthAvg.FormatPrice(Currency);
        public DateTime LastMonthDataUpdate { get; set; }
        public decimal? DayVolChgOfAvg { get; set; }
        public bool MonthStatsExpired => DateTime.Now.Date.Subtract(LastMonthDataUpdate.Date).TotalDays > 1;

        public IDictionary<DateTime, CandlePayload> MinuteCandles { get; } = new Dictionary<DateTime, CandlePayload>();

        public void LogCandle(CandlePayload candle)
        {
            var t = DateTime.Now;
            t = t.AddMilliseconds(-t.Millisecond).AddSeconds(-t.Second);
            MinuteCandles[t] = candle;
            if (MinuteCandles.Count > 100)
            {
                MinuteCandles.OrderBy(p => p.Key).Take(50).ToList()
                    .ForEach(p => MinuteCandles.Remove(p.Key));
            }
        }

        public IEnumerable<ICandleModel> Candles => _candles ??= new HashSet<CandleModel>();

        public void AddCandle(CandlePayload candle)
        {
            if (_candles == null)
                _candles = new HashSet<CandleModel>();

            _candles.Add(new CandleModel()
            {
                Interval = candle.Interval,
                Open = candle.Open,
                Close = candle.Close,
                Low = candle.Low,
                High = candle.High,
                Time = candle.Time,
                Volume = candle.Volume
            });
        }
    }
}
