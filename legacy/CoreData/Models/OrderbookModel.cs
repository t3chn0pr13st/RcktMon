using System;
using System.Collections.Generic;
using System.Text;

namespace CoreData.Models
{
    public class OrderbookModel
    {
        public int Depth { get; }

        public List<Decimal[]> Bids { get; }

        public List<Decimal[]> Asks { get; }

        public string Figi { get; }

        public string Ticker { get; }

        public string Isin { get; }

        public OrderbookModel(int depth, List<Decimal[]> bids, List<Decimal[]> asks, string figi, string ticker, string isin)
        {
            this.Depth = depth;
            this.Bids = bids;
            this.Asks = asks;
            this.Figi = figi;
            this.Ticker = ticker;
            this.Isin = isin;
        }
    }
}
