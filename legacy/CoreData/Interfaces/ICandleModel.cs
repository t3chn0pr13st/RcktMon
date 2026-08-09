using System;
using Tinkoff.Trading.OpenApi.Legacy.Models;

namespace CoreData.Interfaces
{
    public interface ICandleModel
    {
        CandleInterval Interval { get; set; }
        decimal Open { get; set; }
        decimal Close { get; set; }
        decimal Low { get; set; }
        decimal High { get; set; }
        DateTime Time { get; set; }
        decimal Volume { get; set; }
    }
}
