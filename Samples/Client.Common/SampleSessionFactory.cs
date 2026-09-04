/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// Opens the kind of session the sample clients run on: a managed session on the V2
    /// subscription engine, on the endpoint a discovery of the server url selects.
    /// </summary>
    /// <remarks>
    /// This is what the shared connect control of the windows does, without the control,
    /// for the callers which have no window: the headless tests of the client models.
    /// </remarks>
    public static class SampleSessionFactory
    {
        /// <summary>
        /// The discovery timeout, in milliseconds.
        /// </summary>
        public const int DefaultDiscoverTimeout = 15000;

        /// <summary>
        /// The session timeout, in milliseconds.
        /// </summary>
        public const uint DefaultSessionTimeout = 60000;

        /// <summary>
        /// Discovers the endpoints of a server and opens a managed session on the best one.
        /// </summary>
        /// <param name="configuration">The configuration of the client.</param>
        /// <param name="endpointUrl">The url of the server.</param>
        /// <param name="useSecurity">Whether to prefer a secured endpoint.</param>
        /// <param name="identity">The user to open the session for. Null for anonymous.</param>
        /// <param name="sessionName">The name of the session, for the logs of the server.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        /// <param name="sessionTimeout">The session timeout, in milliseconds.</param>
        /// <param name="checkDomain">Whether the certificate of the server must match its host name.</param>
        /// <param name="preferredLocales">The locales to ask for, or null.</param>
        /// <param name="ct">The cancellation token.</param>
        #pragma warning disable CA1054 // Justification: the samples spell endpoint urls as strings.
        public static async Task<ISession> ConnectAsync(
            ApplicationConfiguration configuration,
            string endpointUrl,
            bool useSecurity,
            IUserIdentity identity,
            string sessionName,
            ITelemetryContext telemetry,
            uint sessionTimeout = DefaultSessionTimeout,
            bool checkDomain = false,
            string[] preferredLocales = null,
            CancellationToken ct = default)
        #pragma warning restore CA1054
        {
            EndpointDescription endpointDescription = await CoreClientUtils
                .SelectEndpointAsync(configuration, endpointUrl, useSecurity, DefaultDiscoverTimeout, telemetry, ct)
                .ConfigureAwait(false);

            var endpointConfiguration = EndpointConfiguration.Create(configuration);
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            // the managed session brings its own connection state machine and reconnect
            // policy, and its subscription manager is the V2 engine the models require
            return await new ManagedSessionFactory(telemetry)
                .CreateAsync(
                    configuration,
                    endpoint,
                    false,
                    checkDomain,
                    sessionName,
                    sessionTimeout,
                    identity ?? new UserIdentity(),
                    preferredLocales,
                    ct)
                .ConfigureAwait(false);
        }
    }
}
