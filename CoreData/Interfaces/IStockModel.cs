using System;
using System.Collections.Generic;
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
         decimal DayChange { get; set; }
         decimal DayVolume { get; set; }
         decimal DayVolumeCost => DayVolume * AvgPrice * Lot;
         decimal AvgPrice => (TodayOpen + Price) / 2;
         string Status { get; set; }
         DateTime LastUpdate { get; set; }
         DateTime? LastAboveThresholdDate { get; set; }
         DateTime? LastAboveThresholdCandleTime { get; set; }

         decimal MonthOpen { get; set; }
         decimal MonthLow { get; set; }
         decimal MonthHigh { get; set; }
         decimal MonthAvg => (MonthHigh + MonthLow) / 2;
         decimal MonthVolume { get; set; }
         decimal MonthVolumeCost { get; set; }
         decimal AvgDayVolumePerMonth { get; set; }
         decimal AvgDayPricePerMonth { get; set; }
         decimal AvgDayVolumePerMonthCost { get; set; }
         decimal YesterdayVolume { get; set; }
         decimal YesterdayVolumeCost { get; set; }
         decimal YesterdayAvgPrice { get; set; }

         string TodayOpenF => TodayOpen.FormatPrice(Currency);
         string PriceF => Price.FormatPrice(Currency);
         string AvgPriceF => AvgPrice.FormatPrice(Currency);
         string YesterdayVolumeCostF => YesterdayVolumeCost.FormatPrice(Currency);
         string YesterdayAvgPriceF => YesterdayAvgPrice.FormatPrice(Currency);
         string MonthVolumeCostF => MonthVolumeCost.FormatPrice(Currency);
         string DayChangeF => DayChange.ToString("P2");
         string DayVolumeCostF => DayVolumeCost.FormatPrice(Currency);
         string AvgDayVolumePerMonthCostF => AvgDayVolumePerMonthCost.FormatPrice(Currency);
         string AvgDayPricePerMonthF => AvgDayPricePerMonth.FormatPrice(Currency);
         string AvgMonthPriceF => MonthAvg.FormatPrice(Currency);

         void LogCandle(CandlePayload candle);

         IEnumerable<ICandleModel> Candles { get; }

         IDictionary<DateTime, CandlePayload> MinuteCandles { get; }

         void AddCandle(CandlePayload candle);
    }
}
