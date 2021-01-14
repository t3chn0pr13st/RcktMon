using System;
using CoreData.Interfaces;
using Tinkoff.Trading.OpenApi.Models;

namespace CoreData.Models
{
    public class CandleModel : ICandleModel
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
