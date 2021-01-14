using System;
using System.Collections.Generic;
using System.Globalization;
using Tinkoff.Trading.OpenApi.Models;

namespace CoreData.Interfaces
{
    public interface IStockModel
    { 
         string Figi { get; set; }
         string Isin { get; set; }
         string Name { get; set; }
         string Ticker { get; set; }
         string Currency { get; set; }
         int Lot { get; set; }
         decimal MinPriceIncrement { get; set; }
         DateTime TodayDate { get; set; }
         decimal TodayOpen { get; set; }
         decimal Price { get; set; }
         decimal BestBidPrice { get; set; }
         decimal BestAskPrice { get; set; }
         decimal DayChange { get; set; }
         decimal DayVolume { get; set; }
         decimal DayVolumeCost { get; }
         decimal AvgPrice { get; }
         string Status { get; set; }
         DateTime LastUpdate { get; set; }
         DateTime? LastAboveThresholdDate { get; set; }
         DateTime? LastAboveThresholdCandleTime { get; set; }
         DateTime? LastAboveVolThresholdCandleTime { get; set; }

         decimal PriceUSA { get; set; }
         decimal BidUSA { get; set; }
         decimal AskUSA { get; set; }
         decimal BidSizeUSA { get; set; }
         decimal AskSizeUSA { get; set; }
         DateTime? LastTradeUSA { get; set; }
         decimal DiffPercentUSA { get; set; }

         decimal MonthOpen { get; set; }
         decimal MonthLow { get; set; }
         decimal MonthHigh { get; set; }
         decimal MonthAvg { get; }
         decimal MonthVolume { get; set; }
         decimal MonthVolumeCost { get; set; }
         decimal AvgDayVolumePerMonth { get; set; }
         decimal AvgDayPricePerMonth { get; set; }
         decimal AvgDayVolumePerMonthCost { get; set; }
         decimal YesterdayVolume { get; set; }
         decimal YesterdayVolumeCost { get; set; }
         decimal YesterdayAvgPrice { get; set; }

         string TodayOpenF { get; }
         string PriceF { get; }
         string AvgPriceF { get; }
         string YesterdayVolumeCostF { get; }
         string YesterdayAvgPriceF { get; }
         string MonthVolumeCostF { get; }
         string DayChangeF { get; }
         string DayVolumeCostF { get; }
         string AvgDayVolumePerMonthCostF { get; }
         string AvgDayPricePerMonthF { get; }
         string AvgMonthPriceF { get; }
         decimal? DayVolChgOfAvg { get; set; }

         DateTime LastMonthDataUpdate { get; set; }
         bool MonthStatsExpired { get; }

         void LogCandle(CandlePayload candle);

         IEnumerable<ICandleModel> Candles { get; }

         IDictionary<DateTime, CandlePayload> MinuteCandles { get; }

         void AddCandle(CandlePayload candle);
    }
}
