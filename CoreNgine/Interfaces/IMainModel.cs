using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoreData.Interfaces;
using CoreData.Models;
using CoreNgine.Shared;
using Tinkoff.Trading.OpenApi.Models;

namespace CoreNgine.Models
{
    public interface IMainModel : IStocksInfoContainer
    {
        IStockModel CreateStockModel(MarketInstrument instrument);
        Task AddStocks(IEnumerable<IStockModel> stocks);

        IMessageModel AddErrorMessage(string text) => AddMessage(MessageKind.Error, "ERROR", DateTime.Now, text);

        IMessageModel AddMessage(MessageKind messageKind, string ticker, DateTime date, string text);

        IMessageModel AddMessage(MessageKind messageKind, string ticker, DateTime date, decimal change, decimal volume, string text);

        void Start();
        Task OnStockUpdated(IStockModel stock);
    }
}
