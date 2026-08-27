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
    }
}
