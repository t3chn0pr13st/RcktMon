using System;
using System.Globalization;

namespace CoreData.Models
{
    public class QuoteData
    {
        public string Ticker { get;set; }
        public DateTime LastTrade { get; set; }
        public decimal Last { get; set; }
        public string Exchange { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public int BidSize { get; set; }
        public int AskSize { get; set; }
    }

    public static class ParseHelpers
    {
        public static decimal ParseDecimal(this string str, decimal defaultValue = 0)
        {
            if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
                return result;
            return 0;
        }

        public static DateTimeOffset ParseDateTime(this string str)
        {
            if (DateTimeOffset.TryParse(str, out var res))
                return res;
            return DateTimeOffset.MinValue;
        }
    }
}
