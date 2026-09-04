/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// The client half of a reverse connection: listens on the client endpoints of the
    /// application configuration and hands out the connections servers open to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the case a firewall forces: the server cannot be reached from the client,
    /// so the server dials the client and offers the open socket with a <c>ReverseHello</c>
    /// message. The client listens, matches the message against what it is waiting for and
    /// opens its session on the connection it was handed instead of one it opened itself.
    /// See OPC UA Part 6 §7.1.3.
    /// </para>
    /// <para>
    /// The listener stays bound for the lifetime of this object, which is what lets one
    /// listener serve several servers: register or wait once per server, keyed by its
    /// endpoint URL and server URI. The aggregation server sample uses the same
    /// <see cref="ReverseConnectManager"/> to reach the servers it aggregates; this is the
    /// other direction, a plain client waiting to be called.
    /// </para>
    /// </remarks>
    public sealed class ReverseConnectListener : IAsyncDisposable
    {
        private readonly ApplicationConfiguration m_configuration;
        private readonly ReverseConnectManager m_manager;
        private bool m_started;

        /// <summary>
        /// Creates the listener for the client endpoints of a configuration.
        /// </summary>
        /// <param name="configuration">The application configuration, whose
        /// <see cref="ClientConfiguration.ReverseConnect"/> block names the endpoints to
        /// listen on and the timeouts to hold unmatched connections for.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public ReverseConnectListener(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            m_configuration = configuration;
            m_manager = new ReverseConnectManager(telemetry);
        }

        /// <summary>
        /// The endpoint URLs the configuration asks the client to listen on; empty when the
        /// configuration carries no reverse connect block.
        /// </summary>
        public static IReadOnlyList<string> GetClientEndpointUrls(ApplicationConfiguration configuration)
        {
            var urls = new List<string>();

            ReverseConnectClientConfiguration reverseConnect = configuration?.ClientConfiguration?.ReverseConnect;

            if (reverseConnect?.ClientEndpoints != null)
            {
                foreach (ReverseConnectClientEndpoint endpoint in reverseConnect.ClientEndpoints)
                {
                    if (!String.IsNullOrEmpty(endpoint?.EndpointUrl))
                    {
                        urls.Add(endpoint.EndpointUrl);
                    }
                }
            }

            return urls;
        }

        /// <summary>
        /// Whether the configuration names at least one client endpoint to listen on.
        /// </summary>
        public static bool IsConfigured(ApplicationConfiguration configuration)
        {
            return GetClientEndpointUrls(configuration).Count > 0;
        }

        /// <summary>
        /// Opens the listeners named by the configuration. Does nothing when they are open.
        /// </summary>
        /// <exception cref="ServiceResultException">The configuration names no client
        /// endpoint, or a listener could not be bound.</exception>
        public async Task StartAsync(CancellationToken ct = default)
        {
            if (m_started)
            {
                return;
            }

            if (!IsConfigured(m_configuration))
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The client configuration carries no ReverseConnect block, so there is no endpoint to listen on.");
            }

            // the manager reads the ClientConfiguration.ReverseConnect block itself, which
            // is what keeps the endpoints and the timeouts of a sample in its config file.
            await m_manager.StartServiceAsync(m_configuration, ct).ConfigureAwait(false);

            m_started = true;
        }

        /// <summary>
        /// Waits for a server to open a connection to this client.
        /// </summary>
        /// <remarks>
        /// A wait is always for one named server, which is what lets one listener serve
        /// several of them: the manager matches the <c>ReverseHello</c> against the
        /// endpoint URL, and against the server URI as well when one is given. Part 2 §6.14
        /// asks a client to validate both.
        /// </remarks>
        /// <param name="serverEndpointUrl">The endpoint URL of the server to wait for.</param>
        /// <param name="serverUri">The application URI of the server to wait for, or null
        /// to accept the connection on the endpoint URL alone.</param>
        /// <param name="ct">The cancellation token, which is how a waiting client gives up.</param>
        #pragma warning disable CA1054 // Justification: mirrors ReverseConnectManager.WaitForConnectionAsync, whose serverUri is the application URI string of the ApplicationDescription.
        public async Task<ITransportWaitingConnection> WaitForConnectionAsync(
            Uri serverEndpointUrl,
            string serverUri,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(serverEndpointUrl);

            await StartAsync(ct).ConfigureAwait(false);

            return await m_manager.WaitForConnectionAsync(serverEndpointUrl, serverUri, ct).ConfigureAwait(false);
        }
        #pragma warning restore CA1054

        /// <summary>
        /// Waits for a server and opens a session on the connection it offers, repeating
        /// the wait for the connections the handshake consumes.
        /// </summary>
        /// <remarks>
        /// A secure reverse connection takes more than one <c>ReverseHello</c>: the first
        /// connection is spent on <c>GetEndpoints</c>, which fetches the server certificate
        /// and closes the channel, so the client has to wait for the server to call again
        /// before it can open the session. <paramref name="connectAsync"/> reports that by
        /// returning <c>null</c>, which is the contract the connect control of the sample
        /// client controls already follows.
        /// </remarks>
        /// <param name="serverEndpointUrl">The endpoint URL of the server to wait for.</param>
        /// <param name="serverUri">The application URI of the server to wait for, or null
        /// to accept the connection on the endpoint URL alone.</param>
        /// <param name="connectAsync">Opens the session on a connection, or returns null
        /// when it used the connection for discovery and another one is needed.</param>
        /// <param name="maxAttempts">How many connections to spend before giving up.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The session, or null when no attempt was left.</returns>
        #pragma warning disable CA1054 // Justification: mirrors ReverseConnectManager.WaitForConnectionAsync, whose serverUri is the application URI string of the ApplicationDescription.
        public async Task<TSession> ConnectAsync<TSession>(
            Uri serverEndpointUrl,
            string serverUri,
            Func<ITransportWaitingConnection, CancellationToken, Task<TSession>> connectAsync,
            int maxAttempts = 3,
            CancellationToken ct = default)
            where TSession : class
        {
            ArgumentNullException.ThrowIfNull(connectAsync);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                ITransportWaitingConnection connection =
                    await WaitForConnectionAsync(serverEndpointUrl, serverUri, ct).ConfigureAwait(false);

                TSession session = await connectAsync(connection, ct).ConfigureAwait(false);

                if (session != null)
                {
                    return session;
                }
            }

            return null;
        }
        #pragma warning restore CA1054

        /// <summary>
        /// Closes the listeners and releases the connections still being held.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            m_started = false;

            // disposing is what releases the held connections right away; a server whose
            // hold time runs out instead would keep the socket until it expires.
            await m_manager.DisposeAsync().ConfigureAwait(false);
        }
    }
}
