using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Caliburn.Micro;
using CoreData;
using CoreData.Interfaces;
using CoreData.Models;
using CoreNgine.Models;
using Tinkoff.Trading.OpenApi.Models;

namespace RcktMon.ViewModels
{
    [DebuggerDisplay("{Ticker} {Price} {DayChangeF}")]
    public class StockViewModel : PropertyChangedBase, IStockModel
    {
        private HashSet<ICandleModel> _candles = null;
        public string Figi { get; set; }
        public string Name { get; set; }
        public string Ticker { get; set; }
        public string Isin { get; set; }
        public string Currency { get; set; }
        public int Lot { get; set; }
        public decimal LimitDown { get; set; }
        public decimal LimitUp { get; set; }
        public bool IsDead { get; set; }
        public bool CanBeShorted { get; set; }
        public string Exchange { get;set; }
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
        public DateTime LastUpdate { get; set; }
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
        public decimal? DayVolChgOfAvg { get; set; }
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
        public DateTime LastMonthDataUpdate { get; set; }
        public bool MonthStatsExpired => DateTime.Now.Date.Subtract(LastMonthDataUpdate.Date).TotalDays > 1;

        public IDictionary<DateTime, CandlePayload> MinuteCandles { get; } =
            new ConcurrentDictionary<DateTime, CandlePayload>();

        public void LogCandle(CandlePayload candle)
        {
            MinuteCandles[candle.Time.ToLocalTime()] = candle;
            if (MinuteCandles.Count > 100)
            {
                MinuteCandles.OrderBy(p => p.Key).Take(50).ToList()
                    .ForEach(p => MinuteCandles.Remove(p.Key, out _));
            }
        }

        public IEnumerable<ICandleModel> Candles => _candles ??= new HashSet<ICandleModel>();

        public void AddCandle(CandlePayload candle)
        {
            if (_candles == null)
                _candles = new HashSet<ICandleModel>();

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
        public decimal Volume { get; set; }
    }
}
