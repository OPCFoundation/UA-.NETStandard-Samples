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

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// What a discovery of one server answered: the url which worked, the endpoints it
    /// offered, or the reason the last attempt failed.
    /// </summary>
    /// <param name="DiscoveryUrl">The url the endpoints came from, or null on a failure.</param>
    /// <param name="Endpoints">The endpoints the server offers; empty on a failure.</param>
    /// <param name="Error">Why the last url failed, or empty on success.</param>
    public sealed record EndpointDiscoveryResult(
        Uri DiscoveryUrl,
        IReadOnlyList<EndpointDescription> Endpoints,
        string Error)
    {
        /// <summary>
        /// True when a discovery url answered.
        /// </summary>
        public bool Succeeded => DiscoveryUrl != null;
    }

    /// <summary>
    /// Asks a server which endpoints it offers, without a window.
    /// </summary>
    /// <remarks>
    /// <c>CoreClientUtils.SelectEndpointAsync</c> of the stack discovers <em>one</em> url and
    /// picks the endpoint to connect to. This is the other shape of the same question, the
    /// one an endpoint editor asks: a server found through discovery advertises a list of
    /// discovery urls, any of which may be unreachable, and what the editor wants is every
    /// endpoint of the first url which answers - so that a person can choose among them.
    /// </remarks>
    public static class SampleDiscovery
    {
        /// <summary>
        /// How long a discovery attempt is given before the next url is tried.
        /// </summary>
        public const int DefaultDiscoveryTimeout = 20000;

        /// <summary>
        /// Reads the endpoints of one discovery url.
        /// </summary>
        /// <param name="configuration">The configuration of the client.</param>
        /// <param name="discoveryUrl">The url to ask.</param>
        /// <param name="timeout">The operation timeout, in milliseconds.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<List<EndpointDescription>> GetEndpointsAsync(
            ApplicationConfiguration configuration,
            Uri discoveryUrl,
            int timeout = DefaultDiscoveryTimeout,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(discoveryUrl);

            var endpointConfiguration = EndpointConfiguration.Create(configuration);

            endpointConfiguration.OperationTimeout = timeout;

            DiscoveryClient client = await DiscoveryClient
                .CreateAsync(discoveryUrl, endpointConfiguration, configuration, DiagnosticsMasks.None, ct)
                .ConfigureAwait(false);

            try
            {
                return new List<EndpointDescription>((await client.GetEndpointsAsync(default, ct).ConfigureAwait(false)).ToArray());
            }
            finally
            {
                await client.CloseAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads the endpoints of a server, trying its discovery urls in order until one
        /// answers.
        /// </summary>
        /// <remarks>
        /// A server which was found through a discovery server advertises the urls it can be
        /// reached on, and some of them regularly cannot be: a host name which only resolves
        /// inside the plant, an address the server picked up from an interface which is down.
        /// A url which does not answer is therefore not an error, it is the reason to try the
        /// next one; only when none of them answers does the caller get the message of the
        /// last failure to show.
        /// </remarks>
        /// <param name="configuration">The configuration of the client.</param>
        /// <param name="discoveryUrls">The urls the server advertises.</param>
        /// <param name="onFailedUrl">Called with the url and the reason for every attempt
        /// which failed, for a caller which logs them. Optional.</param>
        /// <param name="timeout">The operation timeout of one attempt, in milliseconds.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<EndpointDiscoveryResult> DiscoverEndpointsAsync(
            ApplicationConfiguration configuration,
            IEnumerable<string> discoveryUrls,
            Action<Uri, string> onFailedUrl = null,
            int timeout = DefaultDiscoveryTimeout,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            string error = String.Empty;

            if (discoveryUrls != null)
            {
                foreach (string discoveryUrl in discoveryUrls)
                {
                    Uri url = Utils.ParseUri(discoveryUrl);

                    if (url == null)
                    {
                        continue;
                    }

                    try
                    {
                        List<EndpointDescription> endpoints = await GetEndpointsAsync(configuration, url, timeout, ct)
                            .ConfigureAwait(false);

                        return new EndpointDiscoveryResult(url, endpoints, String.Empty);
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                        onFailedUrl?.Invoke(url, error);
                    }
                }
            }

            return new EndpointDiscoveryResult(null, Array.Empty<EndpointDescription>(), error);
        }
    }
}
