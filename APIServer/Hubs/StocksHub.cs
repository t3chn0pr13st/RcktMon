using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreData.Models;
using CoreNgine.Models;
using Microsoft.AspNetCore.SignalR;

namespace APIServer
{
    public class StocksHub : Hub
    {
        private readonly IMainModel _mainModel;
        private bool _firstConnect = true;

        public StocksHub(IMainModel mainModel)
        {
            _mainModel = mainModel;
        }

        [HubMethodName("stocks")]
        public Task GetStocks()
        {
            return Clients.Caller.SendAsync("stocks", _mainModel.Stocks.OrderByDescending(s => s.DayChange).Cast<StockModel>().ToArray());
        }

        [HubMethodName("messages")]
        public Task GetMessages()
        {
            return Clients.Caller.SendAsync("messages", _mainModel.Messages.Cast<MessageModel>().ToArray());
        }

        public override Task OnConnectedAsync()
        {
            return base.OnConnectedAsync();
        }
    }
}
