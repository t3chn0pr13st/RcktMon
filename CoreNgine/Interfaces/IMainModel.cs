using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoreData.Interfaces;
using CoreNgine.Shared;
using Tinkoff.Trading.OpenApi.Models;

namespace CoreNgine.Models
{
    public interface IMainModel : INgineSettings, IStocksInfoContainer
    {
        IStockModel CreateStockModel(MarketInstrument instrument);
        Task AddStocks(IEnumerable<IStockModel> stocks);

        IMessageModel AddMessage(string ticker, DateTime date, string text);

        IMessageModel AddMessage(string ticker, DateTime date, decimal change, decimal volume, string text);

        void Start();
        void OnStockUpdated(IStockModel stock);
    }
}
