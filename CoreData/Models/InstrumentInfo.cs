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
        public string MarketStartTime { get; private set; }
        public string MarketEndTime { get; private set; }
        public bool ShortIsEnabled { get; private set; }
        public string ExchangeStatus { get; private set; }
        public string InstrumentStatus { get; private set; }
        public string InstrumentStatusShortDesc { get; private set; }

        public InstrumentInfo()
        {
            
        }

        public InstrumentInfo(TinkoffStocksInfoCollection.Value payloadValue)
        {
            Ticker = payloadValue.Symbol.Ticker;
            Isin = payloadValue.Symbol.Isin;
            Logo = payloadValue.Symbol.LogoName;
            Sector = payloadValue.Symbol.Sector;
            MarketStartTime = payloadValue.Symbol.MarketStartTime;
            MarketEndTime = payloadValue.Symbol.MarketEndTime;
            ShortIsEnabled = payloadValue.Symbol.ShortIsEnabled;
            ExchangeStatus = payloadValue.ExchangeStatus;
            InstrumentStatus = payloadValue.InstrumentStatus;
            InstrumentStatusShortDesc = payloadValue.InstrumentStatusShortDesc;
            Exchange = payloadValue.Symbol.Exchange;
        }
    }
}
