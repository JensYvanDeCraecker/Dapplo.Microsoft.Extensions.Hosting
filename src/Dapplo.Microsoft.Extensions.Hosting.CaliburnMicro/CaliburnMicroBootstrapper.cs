﻿//  Dapplo - building blocks for desktop applications
//  Copyright (C) 2016-2018 Dapplo
// 
//  For more information see: http://dapplo.net/
//  Dapplo repositories are hosted on GitHub: https://github.com/dapplo
// 
//  This file is part of Dapplo.CaliburnMicro
// 
//  Dapplo.CaliburnMicro is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  Dapplo.CaliburnMicro is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
// 
//  You should have a copy of the GNU Lesser General Public License
//  along with Dapplo.CaliburnMicro. If not, see <http://www.gnu.org/licenses/lgpl.txt>.

using System;
using System.Collections.Generic;
using System.Windows;
using Caliburn.Micro;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Dapplo.Microsoft.Extensions.Hosting.Wpf;

namespace Dapplo.Microsoft.Extensions.Hosting.CaliburnMicro
{
    /// <summary>
    ///     An implementation of the Caliburn Micro Bootstrapper which is started from
    ///     the generic host.
    /// </summary>
    public class CaliburnMicroBootstrapper : BootstrapperBase, IHostedService
    {
        private readonly ILogger<CaliburnMicroBootstrapper> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IWindowManager _windowManager;
        private readonly IWpfContext _wpfContext;

        /// <summary>
        /// CaliburnMicroBootstrapper
        /// </summary>
        /// <param name="logger">ILogger</param>
        /// <param name="serviceProvider">IServiceProvider</param>
        /// <param name="loggerFactory">ILoggerFactory</param>
        /// <param name="windowManager">IWindowManager</param>
        /// <param name="wpfContext">IWpfContext</param>
        public CaliburnMicroBootstrapper(
            ILogger<CaliburnMicroBootstrapper> logger,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory,
            IWindowManager windowManager,
            IWpfContext wpfContext)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _loggerFactory = loggerFactory;
            _windowManager = windowManager;
            _wpfContext = wpfContext ?? throw new ArgumentNullException(nameof(wpfContext));
        }

        /// <summary>
        ///     Fill imports of the supplied instance
        /// </summary>
        /// <param name="instance">some object to fill</param>
        protected override void BuildUp(object instance)
        {
            _logger.LogDebug("Should buildup {0}", instance?.GetType().Name);
            // TODO: don't know how to fill imports yet?
            //_bootstrapper.Container.InjectProperties(instance);
        }

        /// <summary>
        ///     Configure the Dapplo.Addon.Bootstrapper with the AssemblySource.Instance values
        /// </summary>
        [SuppressMessage("Sonar Code Smell", "S2696:Instance members should not write to static fields", Justification = "This is the only location where it makes sense.")]
        protected override void Configure()
        {
            // Create a logger to log caliburn message
            LogManager.GetLog = type => new CaliburnLogger(_loggerFactory.CreateLogger(type));
            ConfigureViewLocator();

            // TODO: Documentation
            // This make it possible to pass the data-context of the originally clicked object in the Message.Attach event bubbling.
            // E.G. the parent Menu-Item Click will get the Child MenuItem that was actually clicked.
            MessageBinder.SpecialValues.Add("$originalDataContext", context =>
            {
                var routedEventArgs = context.EventArgs as RoutedEventArgs;
                var frameworkElement = routedEventArgs?.OriginalSource as FrameworkElement;
                return frameworkElement?.DataContext;
            });
        }

        /// <summary>
        ///     Add logic to find the base viewtype if the default locator can't find a view.
        /// </summary>
        [SuppressMessage("Sonar Code Smell", "S2696:Instance members should not write to static fields", Justification = "This is the only location where it makes sense.")]
        private void ConfigureViewLocator()
        {
            var defaultLocator = ViewLocator.LocateTypeForModelType;
            ViewLocator.LocateTypeForModelType = (modelType, displayLocation, context) =>
            {
                var viewType = defaultLocator(modelType, displayLocation, context);
                bool initialViewFound = viewType != null;

                if (initialViewFound)
                {
                    return viewType;
                }
                _logger.LogDebug("No view for {0}, looking into base types.", modelType);
                var currentModelType = modelType;
                while (viewType == null && currentModelType != null && currentModelType != typeof(object) && currentModelType != typeof(Screen))
                {
                    currentModelType = currentModelType.BaseType;
                    viewType = defaultLocator(currentModelType, displayLocation, context);
                }
                if (viewType != null)
                {
                    _logger.LogDebug("Found view for {0} in base type {1}, the view is {2}", modelType, currentModelType, viewType);
                }

                return viewType;
            };
        }

        /// <summary>
        ///     Return all instances of a certain service type
        /// </summary>
        /// <param name="service">Type</param>
        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return _serviceProvider.GetServices(service);
        }

        /// <summary>
        ///     Locate an instance of a service, used in Caliburn.
        /// </summary>
        /// <param name="service">Type for the service to locate</param>
        /// <param name="contractName">string with the name of the contract</param>
        /// <returns>instance of the service</returns>
        [SuppressMessage("Sonar Code Smell", "S927:Name parameter to match the base definition", Justification = "The base name is not so clear.")]
        protected override object GetInstance(Type service, string contractName)
        {
            // There is no way to get the service by name
            return _serviceProvider.GetService(service);
        }

        /// <inheritdoc />
        protected override IEnumerable<Assembly> SelectAssemblies()
        {
            return AssemblyLoadContext.Default.Assemblies;
        }

        /// <inheritdoc />
        protected override void OnStartup(object sender, StartupEventArgs e)
        {
            foreach (var shell in _serviceProvider.GetServices<ICaliburnMicroShell>())
            {
                var viewModel = shell;
                _windowManager.ShowWindow(viewModel);
            }
        }

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _wpfContext.WpfApplication.Dispatcher.InvokeAsync(() =>
            {
                // Make sure the Application from the IWpfContext is used
                Application = _wpfContext.WpfApplication;
                Initialize();
                // Startup the bootstrapper
                OnStartup(this, null);
            });
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}