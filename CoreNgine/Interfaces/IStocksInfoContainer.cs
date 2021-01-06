using System;
using System.Collections.Generic;
using System.Text;
using CoreData.Interfaces;
using CoreNgine.Models;

namespace CoreNgine.Models
{
    public interface IStocksInfoContainer
    {
        public IEnumerable<IStockModel> Stocks { get; }
        public IEnumerable<IMessageModel> Messages { get; }
    }
}
