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
        /// Registers the application instance of a sample, and has the host read its
        /// configuration on startup. For the samples whose user interface is built
        /// around the <see cref="ApplicationInstance"/> and the loaded
        /// <see cref="ApplicationConfiguration"/>: the clients, and the servers which
        /// are hosted through <see cref="AddSampleServer(IServiceCollection, Action{IOpcUaServerBuilder})"/>.
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
        /// Registers the server of a sample as the hosted OPC UA server of the stack,
        /// with the configuration loaded from the configuration XML file of the sample:
        /// <c>services.AddOpcUa().AddServer(configurationFile)</c>. The sample says what
        /// its server is made of on the server builder of the stack - its node managers
        /// first of all, <c>server.AddNodeManager&lt;MyNodeManagerFactory&gt;()</c> - and
        /// the stack creates those parts from the container and hands them to the server
        /// before it starts. The server itself is the <see cref="SampleServer"/> every
        /// sample shares; no sample has a server class of its own.
        /// </summary>
        /// <remarks>
        /// The host starts the server before the main form is created, and stops it on
        /// shutdown. The start of the host returns with the server listening.
        /// </remarks>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The application configuration XML file of
        /// the sample, for example <c>BoilerServer.Config.xml</c>.</param>
        /// <param name="configureServer">Registers the node managers and the other
        /// parts of the server with the server builder of the stack.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express, such as a certificate
        /// validation callback.</param>
        public static IServiceCollection AddSampleServer(
            this IServiceCollection services,
            string configurationFile,
            Action<IOpcUaServerBuilder> configureServer,
            Action<ApplicationConfiguration> configure = null)
        {
            return services.AddSampleServer<SampleServer>(configurationFile, configureServer, configure);
        }

        /// <summary>
        /// Registers the server of a sample as the hosted OPC UA server of the stack,
        /// for the rare sample whose server has to override a hook of
        /// <see cref="StandardServer"/> the stack offers no registration for. The
        /// server class is created by the container; a sample whose server the
        /// container cannot create registers it itself, before this call.
        /// </summary>
        /// <typeparam name="TServer">The server class of the sample, derived from
        /// <see cref="SampleServer"/> as a rule.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The application configuration XML file of
        /// the sample.</param>
        /// <param name="configureServer">Registers the node managers and the other
        /// parts of the server with the server builder of the stack.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddSampleServer<TServer>(
            this IServiceCollection services,
            string configurationFile,
            Action<IOpcUaServerBuilder> configureServer,
            Action<ApplicationConfiguration> configure = null)
            where TServer : StandardServer
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureServer);

            var startup = new SampleServerStartup();

            // the loaded configuration, for the forms of the sample
            services.TryAddSingleton(provider => provider.GetRequiredService<SampleServerStartup>().Configuration);

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

            return services.AddSampleServerCore<TServer>(server, startup, configureServer);
        }

        /// <summary>
        /// Registers the server of a sample as the hosted OPC UA server of the stack,
        /// running on the application instance registered by
        /// <see cref="AddSampleApplication"/> - whose configuration file, certificate
        /// and <see cref="ApplicationInstance"/> the user interface of the sample is
        /// built around. Everything else is as for
        /// <see cref="AddSampleServer(IServiceCollection, string, Action{IOpcUaServerBuilder}, Action{ApplicationConfiguration})"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configureServer">Registers the node managers and the other
        /// parts of the server with the server builder of the stack.</param>
        public static IServiceCollection AddSampleServer(
            this IServiceCollection services,
            Action<IOpcUaServerBuilder> configureServer)
        {
            return services.AddSampleServer<SampleServer>(configureServer);
        }

        /// <summary>
        /// Registers the server of a sample as the hosted OPC UA server of the stack,
        /// running on the application instance registered by
        /// <see cref="AddSampleApplication"/>, for the rare sample whose server has to
        /// override a hook of <see cref="StandardServer"/> the stack offers no
        /// registration for. The server class is created by the container; a sample
        /// whose server the container cannot create registers it itself, before this
        /// call.
        /// </summary>
        /// <typeparam name="TServer">The server class of the sample.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="configureServer">Registers the node managers and the other
        /// parts of the server with the server builder of the stack.</param>
        public static IServiceCollection AddSampleServer<TServer>(
            this IServiceCollection services,
            Action<IOpcUaServerBuilder> configureServer)
            where TServer : StandardServer
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureServer);

            IOpcUaServerBuilder server = services
                .AddOpcUa()
                .AddServer(_ => { });

            // no configuration file of its own: the hosted server of the stack runs on
            // the application instance of the sample and the configuration it loaded.
            services.AddSingleton<IOpcUaApplicationConfigurationProvider>(
                provider => provider.GetRequiredService<SampleApplication>());

            return services.AddSampleServerCore<TServer>(server, new SampleServerStartup(), configureServer);
        }

        /// <summary>
        /// What the samples rely on, on top of the hosted server of the stack.
        /// </summary>
        private static IServiceCollection AddSampleServerCore<TServer>(
            this IServiceCollection services,
            IOpcUaServerBuilder server,
            SampleServerStartup startup,
            Action<IOpcUaServerBuilder> configureServer)
            where TServer : StandardServer
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<TServer>();

            // the forms of the server samples show the running server and take the
            // shared StandardServer, so they do not have to know the server class.
            services.AddSingleton<StandardServer>(
                provider => provider.GetRequiredService<TServer>());

            // the hosted server creates its server through this factory: the instance
            // of the sample from the container, which the main form resolves too.
            services.Replace(ServiceDescriptor.Singleton<IOpcUaServerFactory>(
                provider => new SampleServerFactory<TServer>(provider)));

            // the parts of the server: the node managers of the sample first of all.
            configureServer(server);

            // registered after the parts of the sample, so its startup tasks have run
            // when the start of the host returns with the server listening - which the
            // samples and their tests rely on.
            services.AddSingleton(startup);
            services.AddSingleton<IServerStartupTask>(startup);
            services.AddHostedService(provider => new SampleServerReadyHostedService(
                provider.GetRequiredService<SampleServerStartup>(),
                provider));

            return services;
        }
    }
}
