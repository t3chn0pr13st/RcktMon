using Caliburn.Micro;
using MahApps.Metro.Controls;
using System.Windows;
using System.Linq;
using System.ComponentModel.Composition.Hosting;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.ComponentModel.Composition;
using System.Windows.Media;

namespace TradeApp
{
    public class AppBootstrapper : BootstrapperBase
    {
        /// <summary>
        /// Stores the composition containers
        /// </summary>
        private SimpleContainer container;

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
            this.container.BuildUp(instance);
        }

        /// <summary>
        /// Configures the bindings
        /// </summary>
        protected override void Configure()
        {
            this.container = new SimpleContainer();
            this.container.Singleton<IWindowManager, AppWindowManager>();
            this.container.Singleton<IEventAggregator, EventAggregator>();
            this.container.Singleton<MainViewModel>();
        }

        /// <summary>
        /// Gets all instances of the windows
        /// </summary>
        /// <param name="service">The service type</param>
        /// <returns>The list of windows</returns>
        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return this.container.GetAllInstances(service);
        }

        /// <summary>
        /// Gets an instance of a window
        /// </summary>
        /// <param name="service">The service type</param>
        /// <param name="key">The key</param>
        /// <returns>The window instance</returns>
        protected override object GetInstance(Type service, string key)
        {
            var instance = this.container.GetInstance(service, key);
            if (instance != null)
            {
                return instance;
            }

            throw new Exception("Could not locate any instances.");
        }

        /// <summary>
        /// Runs on application startup
        /// </summary>
        /// <param name="sender">The sender object</param>
        /// <param name="e">The startup events</param>
        protected override void OnStartup(object sender, StartupEventArgs e)
        {
            this.DisplayRootViewFor<MainViewModel>();
        }
    }
}
