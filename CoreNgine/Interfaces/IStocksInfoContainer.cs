using System;
using System.Collections.Generic;
using System.Text;
using CoreData.Interfaces;
using CoreNgine.Models;

namespace CoreNgine.Models
{
    public interface IStocksInfoContainer
    {
        public IDictionary<string, IStockModel> Stocks { get; }
        public IEnumerable<IMessageModel> Messages { get; }
    }
}
