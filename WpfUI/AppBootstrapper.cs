using System;
using System.Collections.Generic;
using System.Windows;
using Caliburn.Micro;
using CoreNgine.Models;
using CoreNgine.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog.Extensions.Logging;
using RcktMon.ViewModels;

namespace RcktMon
{
    public class AppBootstrapper : BootstrapperBase
    {
        /// <summary>
        /// Stores the composition containers
        /// </summary>
        private IHost _host;

        /// <summary>
        /// Initializes a new instance of the <see cref="AppBootstrapper"/> class
        /// </summary>
        public AppBootstrapper()
        {
            this.Initialize();
        }

        /// <summary>
        /// Builds up the container
        /// </summary>
        /// <param name="instance">The instance</param>
        protected override void BuildUp(object instance)
        {
            _host.Start();
        }

        /// <summary>
        /// Configures the bindings
        /// </summary>
        protected override void Configure()
        {
            ViewLocator.AddNamespaceMapping("CoreNgine.Models", "RcktMon.Views");
            ViewLocator.NameTransformer.AddRule("IMainModel", "MainViewModel");

            _host = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IWindowManager, AppWindowManager>();
                    services.AddSingleton<IEventAggregator, EventAggregator>();
                    services.AddSingleton<IMainModel, MainViewModel>();
                    services.AddSingleton<L2DataConnector>();
                    services.AddSingleton<StocksManager>();
                    services.AddLogging(lb =>
                    {
                        lb.AddNLog("NLog.config");
                    });
                })
                .Build();
        }

        //private void ConfigureRebus(IServiceCollection services)
        //{
        //    services.AutoRegisterHandlersFromAssemblyOf<StocksManager>();
        //    services.AutoRegisterHandlersFromAssemblyOf<MainViewModel>();

        //    services.AddRebus(configure =>
        //    {
        //        var result = configure
        //            .Logging(l => l.ColoredConsole(minLevel: LogLevel.Info))
        //            .Routing(r => r.TypeBased().MapAssemblyOf<TestRequest>("Messages"))
        //            .Transport(t => t
        //                .UseInMemoryTransport(new InMemNetwork(true), "Messages"));
        //        return result;
        //    });
        //}

        /// <summary>
        /// Gets all instances of the windows
        /// </summary>
        /// <param name="service">The service type</param>
        /// <returns>The list of windows</returns>
        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return _host.Services.GetServices(service);
        }

        /// <summary>
        /// Gets an instance of a window
        /// </summary>
        /// <param name="service">The service type</param>
        /// <param name="key">The key</param>
        /// <returns>The window instance</returns>
        protected override object GetInstance(Type service, string key)
        {
            var result = _host.Services.GetService(service);

            if (result == null)
                throw new Exception("Could not locate any instances.");

            return result;
        }

        /// <summary>
        /// Runs on application startup
        /// </summary>
        /// <param name="sender">The sender object</param>
        /// <param name="e">The startup events</param>
        protected override void OnStartup(object sender, StartupEventArgs e)
        {
            _host.Services.GetRequiredService<IMainModel>().Start();
            this.DisplayRootViewFor<IMainModel>();
        }

        protected override void OnExit(object sender, EventArgs e)
        {
            base.OnExit(sender, e);
        }
    }
}
