using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using CoreCodeGenerators;

namespace CoreData.Models
{    
    [ReadFromOtherAndNotify]
    public partial class InstrumentInfo
    {
        public string Ticker { get; private set; }
        public string Isin { get; private set; }
        public string Logo { get; private set; }
        public string Sector { get; private set; }
        public string Exchange { get; private set; }
        public TimeSpan MarketStartTime { get; private set; }
        public TimeSpan MarketEndTime { get; private set; }
        public bool ShortIsEnabled { get; private set; }
        public string ExchangeStatus { get; private set; }
        public string InstrumentStatus { get; private set; }
        public string InstrumentStatusShortDesc { get; private set; }

        public InstrumentInfo()
        {
            
        }

        public DateTimeOffset CurrentDateMsk => DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(3));

        public DateTimeOffset MarketStartDate
        {
            get
            {
                var currentMsk = CurrentDateMsk;

                return currentMsk.Date.AddHours(
                    currentMsk.Hour < MarketStartTime.Hours 
                    ? ( currentMsk.Hour > MarketEndTime.Hours ? MarketStartTime.TotalHours : MarketStartTime.TotalHours - 24 )
                    : MarketStartTime.TotalHours
                );
            }
        }            

        public DateTimeOffset MarketEndDate
        {
            get
            {
                var currentMsk = CurrentDateMsk;

                return currentMsk.Date.AddHours(
                   ( MarketEndTime.TotalHours < MarketStartTime.TotalHours
                    ? ( currentMsk.Hour < MarketStartTime.Hours ? MarketEndTime.TotalHours : 24 + MarketEndTime.TotalHours )
                    : MarketEndTime.TotalHours )
                );
            }
        }
            

        public bool IsActive
        {
            get
            {
                var currentMsk = CurrentDateMsk;

                return currentMsk >= MarketStartDate 
                       && currentMsk < MarketEndDate 
                       && ExchangeStatus == "Open" 
                       && InstrumentStatus?.StartsWith("Open") == true;
            }
        }

        public InstrumentInfo(TinkoffStocksInfoCollection.Value payloadValue)
        {
            Ticker = payloadValue.Symbol.Ticker;
            Isin = payloadValue.Symbol.Isin;
            Logo = payloadValue.Symbol.LogoName;
            Sector = payloadValue.Symbol.Sector;
            if (TimeSpan.TryParse(payloadValue.Symbol.MarketStartTime, out var marketStartTime))
                MarketStartTime = marketStartTime;
            if (TimeSpan.TryParse(payloadValue.Symbol.MarketEndTime, out var marketEndTime))
                MarketEndTime = marketEndTime;
            ShortIsEnabled = payloadValue.Symbol.ShortIsEnabled;
            ExchangeStatus = payloadValue.ExchangeStatus;
            InstrumentStatus = payloadValue.InstrumentStatus;
            InstrumentStatusShortDesc = payloadValue.InstrumentStatusShortDesc;
            Exchange = payloadValue.Symbol.Exchange;
        }
    }
}
