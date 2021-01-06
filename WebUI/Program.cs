using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BlazorWebAssemblyWebUI;
using CoreData.Interfaces;
using CoreData.Models;

namespace WebUI
{
    public class Program
    {
        public static async Task Main( string[] args )
        {
            var builder = WebAssemblyHostBuilder.CreateDefault( args );
            builder.RootComponents.Add<App>( "#app" );

            builder.Services.AddScoped( sp => new HttpClient { BaseAddress = new Uri( builder.HostEnvironment.BaseAddress ) } );

            await builder.Build().RunAsync();
        }
    }

    public static class Ext
    {
        public static IStockModel GetStockByMessage(this IMessageModel message, HashSet<StockModel> stocks)
        {
            return stocks.FirstOrDefault(s => s.Ticker == message.Ticker) as IStockModel;
        }
    }
}
