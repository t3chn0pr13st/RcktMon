using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreData.Interfaces;
using CoreNgine.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Tinkoff.Trading.OpenApi.Models;

namespace APIServer.Models
{
    public class MainWebModel : MainModel
    {
        public MainWebModel(IServiceProvider serviceProvider, ILogger<MainModel> logger) : base(serviceProvider, logger)
        {
            
        }

        protected override void LoadAppSettings()
        {
            MinDayPriceChange = 0.05m;
            MinTenMinutesPriceChange = 0.01m;
            
            IsTelegramEnabled = true;
        }

        public override IStockModel CreateStockModel(MarketInstrument instrument)
        {
            var stock = base.CreateStockModel(instrument);
            //_hub.Clients.All.SendAsync("stock", stock);
            return stock;
        }

        public override IMessageModel AddMessage(string ticker, DateTime date, string text)
        {
            var message = base.AddMessage(ticker, date, text);
            //_hub.Clients.All.SendAsync("message", message);
            return message;
        }

        public override IMessageModel AddMessage(string ticker, DateTime date, decimal change, decimal volume, string text)
        {
            var message = base.AddMessage(ticker, date, change, volume, text);
            //_hub.Clients.All.SendAsync("message", message);
            return message;
        }
    }
}
