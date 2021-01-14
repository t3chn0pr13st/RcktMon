using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CoreData.Interfaces;
using CoreNgine.Infra;
using CoreNgine.Models;
using CoreNgine.Shared;
using CoreNgine.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoreNgine
{
    public class CoreContainer
    {
        private IHost _host;
        private Action<IServiceCollection> _onConfigureServices;

        public CoreContainer()
        {

        }

        public CoreContainer(Action<IServiceCollection> onConfigureServices, bool autoStart)
        {
            _onConfigureServices = onConfigureServices;
            if (autoStart)
            {
                Configure();
                _host.Start();
                RunMain();
            }
        }

        public IHost Host => _host;

        public IServiceProvider Services => _host.Services;

        public void Configure(Action<IServiceCollection> onConfigureServices = null)
        {
            _onConfigureServices = onConfigureServices;
            _host = new HostBuilder()
                .ConfigureServices(ConfigureServices)
                .Build();
        }

        public virtual void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            services.AddSingleton<IMainModel, MainModel>();
            services.AddSingleton<IEventAggregator2, EventAggregator2>();
            services.AddSingleton<StocksManager>();
            services.AddSingleton<RocketMonitoringStrategy>();
            _onConfigureServices?.Invoke(services);
        }

        public virtual void RunMain()
        {
            // Create singleton instances
            Services.GetRequiredService<RocketMonitoringStrategy>();
            // Run main model
            _host.Services.GetRequiredService<IMainModel>().Start();
        }
    }
}
