/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// Hands the hosted server of the stack the server instance of the sample from
    /// the container, instead of a private instance of its own. The main form of the
    /// sample resolves the same instance, so it shows the server which is running.
    /// </summary>
    /// <typeparam name="TServer">The server class of the sample.</typeparam>
    internal sealed class SampleServerFactory<TServer> : IOpcUaServerFactory
        where TServer : StandardServer
    {
        private readonly IServiceProvider m_provider;

        /// <summary>
        /// Creates the factory.
        /// </summary>
        /// <param name="provider">The container holding the server of the sample.</param>
        public SampleServerFactory(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            m_provider = provider;
        }

        /// <inheritdoc/>
        public StandardServer CreateServer(ITelemetryContext telemetry, TimeProvider timeProvider)
        {
            // telemetry and time provider are not handed through: the server of the
            // sample takes both from the container it is created by.
            return m_provider.GetRequiredService<TServer>();
        }
    }
}
