/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Samples.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The registrations every sample shares, on top of the
    /// <c>services.AddOpcUa()</c> root of the stack.
    /// </summary>
    public static class SampleServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the application instance of a sample, and hands its lifetime to
        /// the generic host.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Describes the sample.</param>
        public static IServiceCollection AddSampleApplication(
            this IServiceCollection services,
            Action<SampleApplicationOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddOpcUa();
            services.Configure(configure);

            services.TryAddSingleton<SampleApplication>();
            services.TryAddSingleton(provider => provider.GetRequiredService<SampleApplication>().Instance);
            services.TryAddSingleton(provider => provider.GetRequiredService<SampleApplication>().Configuration);

            services.AddHostedService<SampleApplicationHostedService>();

            return services;
        }

        /// <summary>
        /// Points the logging at a file, for a sample which has no application
        /// configuration to take the path from.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="outputFilePath">The file to log to.</param>
        public static IServiceCollection AddSampleLogFile(
            this IServiceCollection services,
            string outputFilePath)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(outputFilePath);

            services.AddOpcUa();
            services.AddHostedService(_ => new SampleLogFileHostedService(outputFilePath));

            return services;
        }

        /// <summary>
        /// Registers the server of a sample. The host starts it once the configuration
        /// and the certificate of the application are in place, and stops it on
        /// shutdown.
        /// </summary>
        /// <remarks>
        /// This is the path for the samples whose user interface is built around the
        /// <see cref="ApplicationInstance"/> registered by
        /// <see cref="AddSampleApplication"/>. The other samples hand their
        /// configuration file to the hosted server of the stack instead, see
        /// <see cref="AddSampleServer{TServer}(IServiceCollection, string, Action{ApplicationConfiguration})"/>.
        /// </remarks>
        /// <typeparam name="TServer">The server class of the sample.</typeparam>
        /// <param name="services">The service collection.</param>
        public static IServiceCollection AddSampleServer<TServer>(this IServiceCollection services)
            where TServer : class, IServerBase
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<TServer>();

            return services.AddSampleServerCore<TServer>();
        }

        /// <summary>
        /// Registers the server of a sample which cannot be created by the container
        /// alone, for example because it takes a database or a set of node managers.
        /// </summary>
        /// <typeparam name="TServer">The server class of the sample.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="factory">Creates the server.</param>
        public static IServiceCollection AddSampleServer<TServer>(
            this IServiceCollection services,
            Func<IServiceProvider, TServer> factory)
            where TServer : class, IServerBase
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(factory);

            services.TryAddSingleton(factory);

            return services.AddSampleServerCore<TServer>();
        }

        /// <summary>
        /// Registers the server of a sample as a hosted OPC UA server of the stack,
        /// with the configuration loaded from the configuration XML file of the
        /// sample: <c>services.AddOpcUa().AddServer(configurationFile)</c>. The host
        /// starts the server before the main form is created, and stops it on
        /// shutdown.
        /// </summary>
        /// <typeparam name="TServer">The server class of the sample.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The application configuration XML file of
        /// the sample, for example <c>BoilerServer.Config.xml</c>.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express, such as a certificate
        /// validation callback.</param>
        public static IServiceCollection AddSampleServer<TServer>(
            this IServiceCollection services,
            string configurationFile,
            Action<ApplicationConfiguration> configure = null)
            where TServer : StandardServer
        {
            return services.AddSampleServer<TServer>(configurationFile, configureServer: null, configure);
        }

        /// <summary>
        /// Registers the server of a sample as a hosted OPC UA server of the stack,
        /// and lets the sample register what the server is made of - its node
        /// managers first of all - with the server builder of the stack:
        /// <c>server.AddNodeManager&lt;MyNodeManagerFactory&gt;()</c>. The factories are
        /// created by the container and handed to the server before it starts, so a
        /// server class registers nothing itself.
        /// </summary>
        /// <typeparam name="TServer">The server class of the sample.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The application configuration XML file of
        /// the sample, for example <c>BoilerServer.Config.xml</c>.</param>
        /// <param name="configureServer">Registers the node managers and the other
        /// parts of the server with the server builder of the stack.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express, such as a certificate
        /// validation callback.</param>
        public static IServiceCollection AddSampleServer<TServer>(
            this IServiceCollection services,
            string configurationFile,
            Action<IOpcUaServerBuilder> configureServer,
            Action<ApplicationConfiguration> configure = null)
            where TServer : StandardServer
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<TServer>();

            return services.AddSampleServerHost<TServer>(configurationFile, configureServer, configure);
        }

        /// <summary>
        /// Registers the server of a sample as a hosted OPC UA server of the stack,
        /// for a server which cannot be created by the container alone, for example
        /// because it takes a database or a set of node managers.
        /// </summary>
        /// <typeparam name="TServer">The server class of the sample.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The application configuration XML file of
        /// the sample.</param>
        /// <param name="factory">Creates the server.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddSampleServer<TServer>(
            this IServiceCollection services,
            string configurationFile,
            Func<IServiceProvider, TServer> factory,
            Action<ApplicationConfiguration> configure = null)
            where TServer : StandardServer
        {
            return services.AddSampleServer(configurationFile, factory, configureServer: null, configure);
        }

        /// <summary>
        /// Registers the server of a sample as a hosted OPC UA server of the stack,
        /// for a server which cannot be created by the container alone, and lets the
        /// sample register its node managers with the server builder of the stack.
        /// </summary>
        /// <typeparam name="TServer">The server class of the sample.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The application configuration XML file of
        /// the sample.</param>
        /// <param name="factory">Creates the server. The loaded
        /// <see cref="ApplicationConfiguration"/> can be resolved from the provider,
        /// for a server whose node managers depend on the configuration.</param>
        /// <param name="configureServer">Registers the node managers and the other
        /// parts of the server with the server builder of the stack.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddSampleServer<TServer>(
            this IServiceCollection services,
            string configurationFile,
            Func<IServiceProvider, TServer> factory,
            Action<IOpcUaServerBuilder> configureServer,
            Action<ApplicationConfiguration> configure = null)
            where TServer : StandardServer
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(factory);

            services.TryAddSingleton(factory);

            return services.AddSampleServerHost<TServer>(configurationFile, configureServer, configure);
        }

        /// <summary>
        /// Registers a node manager factory with the container, for a sample whose
        /// server is started by <see cref="AddSampleApplication"/> rather than by
        /// the hosted server of the stack. The registration is the one the server
        /// builder of the stack makes for <c>AddNodeManager&lt;TFactory&gt;()</c>, so a
        /// sample registers its node managers the same way on either path, and the
        /// hosted server of the stack picks them up too.
        /// </summary>
        /// <typeparam name="TFactory">The node manager factory, created by the
        /// container.</typeparam>
        /// <param name="services">The service collection.</param>
        public static IServiceCollection AddSampleNodeManager<TFactory>(this IServiceCollection services)
            where TFactory : class, IAsyncNodeManagerFactory
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<TFactory>();
            services.AddSingleton(provider => new OpcUaServerNodeManagerRegistration(
                provider.GetRequiredService<TFactory>()));

            return services;
        }

        /// <summary>
        /// Publishes an already registered server class as the server of the sample.
        /// </summary>
        private static IServiceCollection AddSampleServerCore<TServer>(this IServiceCollection services)
            where TServer : class, IServerBase
        {
            // the server is deliberately not registered as its own base classes: the
            // main forms of the samples are created by ActivatorUtilities, and every
            // additional base class makes their constructor selection ambiguous.
            services.AddSingleton<IServerBase>(provider => provider.GetRequiredService<TServer>());

            return services;
        }

        /// <summary>
        /// Hands an already registered server class and the configuration file of the
        /// sample to the hosted server of the stack.
        /// </summary>
        private static IServiceCollection AddSampleServerHost<TServer>(
            this IServiceCollection services,
            string configurationFile,
            Action<IOpcUaServerBuilder> configureServer,
            Action<ApplicationConfiguration> configure)
            where TServer : StandardServer
        {
            var startup = new SampleServerStartup();

            services.AddSingleton(startup);
            services.AddSingleton<IServerStartupTask>(startup);

            // the forms of the server samples show the running server and take the
            // shared StandardServer, so they do not have to know the server class.
            services.AddSingleton<StandardServer>(
                provider => provider.GetRequiredService<TServer>());
            services.TryAddSingleton(
                provider => provider.GetRequiredService<SampleServerStartup>().Configuration);

            IOpcUaServerBuilder server = services
                .AddOpcUa()
                .AddServer(
                    SampleConfigurationFile.Resolve(configurationFile),
                    configuration => {
                        // what the configuration file cannot express, for example a
                        // certificate validation callback.
                        configure?.Invoke(configuration);

                        startup.OnConfigurationLoaded(configuration);
                    });

            // the node managers of the sample. The hosted server of the stack creates
            // them from the registered factories and adds them to the server it
            // creates through the factory below, before it starts the server.
            configureServer?.Invoke(server);

            // the hosted server creates its server through this factory: the instance
            // of the sample from the container, which the main form resolves too.
            services.Replace(ServiceDescriptor.Singleton<IOpcUaServerFactory>(
                provider => new SampleServerFactory<TServer>(provider)));

            // registered after the hosted server, so the start of the host returns
            // with the server listening - which the samples and their tests rely on.
            services.AddHostedService(provider => new SampleServerReadyHostedService(
                provider.GetRequiredService<SampleServerStartup>(),
                provider));

            return services;
        }
    }
}
