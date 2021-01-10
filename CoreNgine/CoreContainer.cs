using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CoreNgine.Models;
using CoreNgine.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoreNgine
{
    public class CoreContainer
    {
        private IHost _host;

        public IHost Host => _host;

        public void Init()
        {
            _host = new HostBuilder()
                .ConfigureServices(ConfigureServices)
                .Build();
        }

        public virtual void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            services.AddSingleton<IMainModel, MainModel>();
            services.AddSingleton<StocksManager>();
        }

        public virtual void Run()
        {
            _host.Services.GetRequiredService<IMainModel>().Start();
        }
    }
}
