using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoreNgine.Models;
using CoreNgine.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CoreNgine
{

    public static class CoreExtensions
    {
        public static void UseStocksManager(this IApplicationBuilder app)
        {
            app.ApplicationServices.GetRequiredService<IMainModel>().Start();
        }

        public static void AddStocksManager(this IServiceCollection services)
        {
            services.AddSingleton<StocksManager>();
        }
    }
}
