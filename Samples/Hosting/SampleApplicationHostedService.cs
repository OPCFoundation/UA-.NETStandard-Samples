/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// Gives the generic host ownership of the lifetime of a sample: it reads the
    /// configuration, sets up the certificate and starts and stops the server the
    /// sample registered, if it has one.
    /// </summary>
    public sealed class SampleApplicationHostedService : IHostedService
    {
        private readonly SampleApplication m_application;
        private readonly IServiceProvider m_provider;
        private readonly ILogger m_logger;
        private bool m_started;

        /// <summary>
        /// Creates the hosted service.
        /// </summary>
        /// <param name="application">The application instance of the sample.</param>
        /// <param name="telemetry">The telemetry context of the host.</param>
        /// <param name="provider">The container the server of the sample comes from.</param>
        public SampleApplicationHostedService(
            SampleApplication application,
            ITelemetryContext telemetry,
            IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(application);
            ArgumentNullException.ThrowIfNull(telemetry);
            ArgumentNullException.ThrowIfNull(provider);

            m_application = application;
            m_provider = provider;
            m_logger = telemetry.CreateLogger<SampleApplicationHostedService>();
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await m_application.InitializeAsync(cancellationToken).ConfigureAwait(false);

            // the server is resolved here and not injected: the host creates every
            // hosted service before it starts the first one, and the servers of the
            // samples are meant to see the configuration which was just read.
            IServerBase server = m_provider.GetService<IServerBase>();
            if (server == null)
            {
                return;
            }

            // the transports registered with the container - AddOpcUa() seeds opc.tcp,
            // a sample which serves opc.https adds AddHttpsTransport(). Without this the
            // server would fall back to a private registry which only knows opc.tcp, and
            // a base address of any other scheme would be skipped without an error.
            if (server is ServerBase serverBase)
            {
                serverBase.TransportBindings =
                    m_provider.GetRequiredService<ITransportBindingRegistry>();
            }

            // the node managers registered with the container, the way the hosted
            // server of the stack hands them to the server it creates.
            if (server is StandardServer standardServer)
            {
                foreach (OpcUaServerNodeManagerRegistration registration
                    in m_provider.GetServices<OpcUaServerNodeManagerRegistration>())
                {
                    if (registration.AsyncFactory != null)
                    {
                        standardServer.AddNodeManager(registration.AsyncFactory);
                    }

                    if (registration.SyncFactory != null)
                    {
                        standardServer.AddNodeManager(registration.SyncFactory);
                    }
                }
            }

            await m_application.Instance.StartAsync(server, cancellationToken).ConfigureAwait(false);
            m_started = true;

            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.LogInformation(
                    "{Application} is listening.",
                    m_application.Instance.ApplicationName);
            }
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (m_started)
            {
                m_started = false;
                await m_application.Instance.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
