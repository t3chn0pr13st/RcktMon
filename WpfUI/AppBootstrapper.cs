using System;
using System.Collections.Generic;
using System.Windows;
using Caliburn.Micro;
using CoreData.Interfaces;
using CoreNgine;
using CoreNgine.Infra;
using CoreNgine.Interfaces;
using CoreNgine.Models;
using CoreNgine.Shared;
using CoreNgine.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog.Extensions.Logging;
using RcktMon.Helpers;
using RcktMon.ViewModels;
using USADataProvider;

namespace RcktMon
{
    public class AppBootstrapper : BootstrapperBase
    {
        /// <summary>
        /// Stores the composition containers
        /// </summary>
        private CoreContainer _container = new CoreContainer();

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
            _container.Host.Start();
        }

        /// <summary>
        /// Configures the bindings
        /// </summary>
        protected override void Configure()
        {
            ViewLocator.AddNamespaceMapping("CoreNgine.Models", "RcktMon.Views");
            ViewLocator.NameTransformer.AddRule("IMainModel", "MainViewModel");

            _container.Configure(services =>
            {
                services.AddSingleton<IWindowManager, AppWindowManager>();
                services.AddSingleton<IMainModel, MainViewModel>();
                services.AddSingleton<ISettingsProvider, SettingsModel>();
                services.AddSingleton<IUSADataManager, USADataManager>();
                services.AddSingleton<StatusViewModel>();
                services.AddLogging(lb =>
                {
                    lb.AddNLog("NLog.config");
                });
            });
        }

        /// <summary>
        /// Gets all instances of the windows
        /// </summary>
        /// <param name="service">The service type</param>
        /// <returns>The list of windows</returns>
        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return _container.Services.GetServices(service);
        }

        /// <summary>
        /// Gets an instance of a window
        /// </summary>
        /// <param name="service">The service type</param>
        /// <param name="key">The key</param>
        /// <returns>The window instance</returns>
        protected override object GetInstance(Type service, string key)
        {
            var result = _container.Services.GetService(service);

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
            _container.RunMain();
            _container.Services.GetService<IUSADataManager>();
            this.DisplayRootViewFor<IMainModel>();
        }

        protected override void OnExit(object sender, EventArgs e)
        {
            base.OnExit(sender, e);
        }
    }
}
