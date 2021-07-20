using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreData.Models
{
    public enum Exchange
    {
        Unknown = 0,
        Nyse = 1,
        Nasdaq = 2,
        NyseAmex = 50,
        OtcMarkets = 95
    }

    [Serializable]
    public class InvestingEquity
    {
        private Exchange _exchange;
        private int _exchangeId;

        public long TickerId { get; set; }
        public string Ticker { get; set; }
        public string SecurityType { get; set; }
        public int ExchangeId
        {
            get => _exchangeId;
            set
            {
                _exchangeId = value;
                try
                {
                    _exchange = (Exchange)value;
                }
                catch
                {
                    _exchange = Exchange.Unknown;
                }
            }
        }
        public string ExchangeName { get; set; }
        public int IndustryId { get; set; }
        public string IndustryName { get; set; }
        public int SectorId { get; set; }
        public string SectorName { get; set; }
        public string Country { get; set; }
        public string Name { get; set; }

        public Exchange Exchange => _exchange;

        public override string ToString() => $"[{TickerId} {Exchange}] {Ticker} ({Name})";

    }
}
