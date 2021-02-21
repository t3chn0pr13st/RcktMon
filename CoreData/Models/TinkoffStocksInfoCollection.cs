using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CoreData.Models
{
    public class TinkoffStocksInfoCollection
    {
        public class Symbol
        {
            [JsonPropertyName( "ticker" )]
            public string Ticker { get; set; }

            [JsonPropertyName( "symbolType" )]
            public string SymbolType { get; set; }

            [JsonPropertyName( "classCode" )]
            public string ClassCode { get; set; }

            [JsonPropertyName( "bcsClassCode" )]
            public string BcsClassCode { get; set; }

            [JsonPropertyName( "isin" )]
            public string Isin { get; set; }

            [JsonPropertyName( "currency" )]
            public string Currency { get; set; }

            [JsonPropertyName( "lotSize" )]
            public int LotSize { get; set; }

            [JsonPropertyName( "minPriceIncrement" )]
            public double MinPriceIncrement { get; set; }

            [JsonPropertyName( "exchange" )]
            public string Exchange { get; set; }

            [JsonPropertyName( "exchangeShowName" )]
            public string ExchangeShowName { get; set; }

            [JsonPropertyName( "exchangeLogoUrl" )]
            public string ExchangeLogoUrl { get; set; }

            [JsonPropertyName( "sessionOpen" )]
            public DateTime SessionOpen { get; set; }

            [JsonPropertyName( "sessionClose" )]
            public DateTime SessionClose { get; set; }

            [JsonPropertyName( "showName" )]
            public string ShowName { get; set; }

            [JsonPropertyName( "logoName" )]
            public string LogoName { get; set; }

            [JsonPropertyName( "color" )]
            public string Color { get; set; }

            [JsonPropertyName( "textColor" )]
            public string TextColor { get; set; }

            [JsonPropertyName( "sector" )]
            public string Sector { get; set; }

            [JsonPropertyName( "countryOfRiskBriefName" )]
            public string CountryOfRiskBriefName { get; set; }

            [JsonPropertyName( "countryOfRiskLogoUrl" )]
            public string CountryOfRiskLogoUrl { get; set; }

            [JsonPropertyName( "brand" )]
            public string Brand { get; set; }

            [JsonPropertyName( "blackout" )]
            public bool Blackout { get; set; }

            [JsonPropertyName( "noTrade" )]
            public bool NoTrade { get; set; }

            [JsonPropertyName( "premarketStartTime" )]
            public string PremarketStartTime { get; set; }

            [JsonPropertyName( "premarketEndTime" )]
            public string PremarketEndTime { get; set; }

            [JsonPropertyName( "marketStartTime" )]
            public string MarketStartTime { get; set; }

            [JsonPropertyName( "marketEndTime" )]
            public string MarketEndTime { get; set; }

            [JsonPropertyName( "brokerAccountTypesList" )]
            public List<string> BrokerAccountTypesList { get; set; }

            [JsonPropertyName( "timeToOpen" )]
            public int TimeToOpen { get; set; }

            [JsonPropertyName( "isOTC" )]
            public bool IsOTC { get; set; }

            [JsonPropertyName( "shortIsEnabled" )]
            public bool ShortIsEnabled { get; set; }

            [JsonPropertyName( "longIsEnabled" )]
            public bool LongIsEnabled { get; set; }

            [JsonPropertyName( "bbGlobal" )]
            public string BbGlobal { get; set; }

            [JsonPropertyName( "timeZone" )]
            public string TimeZone { get; set; }

            [JsonPropertyName( "description" )]
            public string Description { get; set; }

            [JsonPropertyName( "fullDescription" )]
            public string FullDescription { get; set; }
        }

        public class Buy
        {
            [JsonPropertyName( "currency" )]
            public string Currency { get; set; }

            [JsonPropertyName( "value" )]
            public decimal Value { get; set; }

            [JsonPropertyName( "fromCache" )]
            public bool FromCache { get; set; }
        }

        public class Sell
        {
            [JsonPropertyName( "currency" )]
            public string Currency { get; set; }

            [JsonPropertyName( "value" )]
            public decimal Value { get; set; }

            [JsonPropertyName( "fromCache" )]
            public bool FromCache { get; set; }
        }

        public class Last
        {
            [JsonPropertyName( "currency" )]
            public string Currency { get; set; }

            [JsonPropertyName( "value" )]
            public decimal Value { get; set; }

            [JsonPropertyName( "fromCache" )]
            public bool FromCache { get; set; }
        }

        public class Close
        {
            [JsonPropertyName( "currency" )]
            public string Currency { get; set; }

            [JsonPropertyName( "value" )]
            public decimal Value { get; set; }

            [JsonPropertyName( "fromCache" )]
            public bool FromCache { get; set; }
        }

        public class Prices
        {
            [JsonPropertyName( "buy" )]
            public Buy Buy { get; set; }

            [JsonPropertyName( "sell" )]
            public Sell Sell { get; set; }

            [JsonPropertyName( "last" )]
            public Last Last { get; set; }

            [JsonPropertyName( "close" )]
            public Close Close { get; set; }
        }

        public class Price
        {
            [JsonPropertyName( "currency" )]
            public string Currency { get; set; }

            [JsonPropertyName( "value" )]
            public decimal Value { get; set; }

            [JsonPropertyName( "fromCache" )]
            public bool FromCache { get; set; }
        }

        public class LotPrice
        {
            [JsonPropertyName( "currency" )]
            public string Currency { get; set; }

            [JsonPropertyName( "value" )]
            public decimal Value { get; set; }

            [JsonPropertyName( "fromCache" )]
            public bool FromCache { get; set; }
        }

        public class Absolute
        {
            [JsonPropertyName( "currency" )]
            public string Currency { get; set; }

            [JsonPropertyName( "value" )]
            public decimal Value { get; set; }
        }

        public class Earnings
        {
            [JsonPropertyName( "absolute" )]
            public Absolute Absolute { get; set; }

            [JsonPropertyName( "relative" )]
            public decimal Relative { get; set; }
        }

        public class EarningsInfo
        {
            [JsonPropertyName( "absolute" )]
            public Absolute Absolute { get; set; }

            [JsonPropertyName( "relative" )]
            public decimal Relative { get; set; }
        }

        public class HistoricalPrice
        {
            [JsonPropertyName( "amount" )]
            public double Amount { get; set; }

            [JsonPropertyName( "time" )]
            public DateTime Time { get; set; }

            [JsonPropertyName( "unixtime" )]
            public int Unixtime { get; set; }

            [JsonPropertyName( "earningsInfo" )]
            public EarningsInfo EarningsInfo { get; set; }
        }

        public class ContentMarker
        {
            [JsonPropertyName( "news" )]
            public bool News { get; set; }

            [JsonPropertyName( "ideas" )]
            public bool Ideas { get; set; }

            [JsonPropertyName( "dividends" )]
            public bool Dividends { get; set; }

            [JsonPropertyName( "prognosis" )]
            public bool Prognosis { get; set; }

            [JsonPropertyName( "events" )]
            public bool Events { get; set; }

            [JsonPropertyName( "fundamentals" )]
            public bool Fundamentals { get; set; }

            [JsonPropertyName( "recalibration" )]
            public bool Recalibration { get; set; }

            [JsonPropertyName( "coupons" )]
            public bool Coupons { get; set; }
        }

        public class Value
        {
            [JsonPropertyName( "symbol" )]
            public Symbol Symbol { get; set; }

            [JsonPropertyName( "prices" )]
            public Prices Prices { get; set; }

            [JsonPropertyName( "price" )]
            public Price Price { get; set; }

            [JsonPropertyName( "lotPrice" )]
            public LotPrice LotPrice { get; set; }

            [JsonPropertyName( "earnings" )]
            public Earnings Earnings { get; set; }

            [JsonPropertyName( "exchangeStatus" )]
            public string ExchangeStatus { get; set; }

            [JsonPropertyName( "instrumentStatus" )]
            public string InstrumentStatus { get; set; }

            [JsonPropertyName( "instrumentStatusShortDesc" )]
            public string InstrumentStatusShortDesc { get; set; }

            [JsonPropertyName( "historicalPrices" )]
            public List<HistoricalPrice> HistoricalPrices { get; set; }

            [JsonPropertyName( "contentMarker" )]
            public ContentMarker ContentMarker { get; set; }

            [JsonPropertyName( "isFavorite" )]
            public bool IsFavorite { get; set; }

            [JsonPropertyName( "riskCategory" )]
            public int RiskCategory { get; set; }

            [JsonPropertyName( "profitable" )]
            public bool Profitable { get; set; }

            [JsonPropertyName( "reliable" )]
            public bool Reliable { get; set; }

            [JsonPropertyName( "rate" )]
            public int Rate { get; set; }

            [JsonPropertyName( "depository" )]
            public string Depository { get; set; }

            [JsonPropertyName( "depoAccountSection" )]
            public string DepoAccountSection { get; set; }

            [JsonPropertyName( "instrumentStatusDesc" )]
            public string InstrumentStatusDesc { get; set; }

            [JsonPropertyName( "instrumentStatusComment" )]
            public string InstrumentStatusComment { get; set; }
        }

        public class Payload
        {
            [JsonPropertyName( "values" )]
            public List<Value> Values { get; set; }

            [JsonPropertyName( "total" )]
            public int Total { get; set; }
        }

        public class Root
        {
            [JsonPropertyName( "payload" )]
            public Payload Payload { get; set; }

            [JsonPropertyName( "trackingId" )]
            public string TrackingId { get; set; }

            [JsonPropertyName( "time" )]
            public DateTime Time { get; set; }

            [JsonPropertyName( "status" )]
            public string Status { get; set; }
        }
    }
}
