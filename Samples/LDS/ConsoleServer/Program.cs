/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Samples.Hosting;

namespace Opc.Ua.Lds.Server.Sample
{
    /// <summary>
    /// A minimal .NET console host for the managed OPC UA Local Discovery
    /// Server (LDS / LDS-ME) shipped in the
    /// <c>OPCFoundation.NetStandard.Opc.Ua.Lds.Server</c> package.
    /// </summary>
    /// <remarks>
    /// The LDS is the cross-platform, managed replacement for the legacy C++
    /// LDS. It answers <c>FindServers</c>, <c>FindServersOnNetwork</c>,
    /// <c>GetEndpoints</c>, <c>RegisterServer</c> and <c>RegisterServer2</c>,
    /// and (when multicast is enabled) re-publishes registered endpoints over
    /// mDNS as an LDS-ME per OPC 10000-12. Run it side by side with the sample
    /// GDS: point the GDS <c>RegistrationEndpoint</c> at this LDS so the GDS
    /// becomes visible on the local network, and add this LDS discovery URL to
    /// the GDS <c>LdsScannerConfiguration</c> so the GDS can pull the servers
    /// the LDS has discovered.
    /// </remarks>
    internal static class Program
    {
        /// <summary>
        /// The canonical LDS discovery endpoint per OPC 10000-12 is
        /// <c>opc.tcp://{host}:4840</c>.
        /// </summary>
        private const string DefaultEndpointUrl = "opc.tcp://localhost:4840";

        public static void Main(string[] args)
        {
            Console.WriteLine(".NET OPC UA Local Discovery Server (LDS / LDS-ME)");

            HostApplicationBuilder builder = SampleHost.CreateBuilder(args);

            // a console sample logs to the console as well as to a file, the same way
            // every other sample does.
            builder.Logging.AddSampleConsole();

            builder.Services.AddSampleLogFile(@"Logs\Opc.Ua.LocalDiscoveryServer.log.txt");

            builder.Services
                .AddOpcUa()
                .AddLdsServer(options =>
                {
                    options.ApplicationName = "Local Discovery Server";
                    options.ApplicationUri = "urn:localhost:UA:LocalDiscoveryServer";
                    options.ProductUri = "uri:opcfoundation.org:LocalDiscoveryServer";
                    options.EndpointUrls.Add(DefaultEndpointUrl);

                    // LDS-ME: announce registered servers over mDNS. Disable on
                    // platforms or test environments without multicast support.
                    options.EnableMulticast = true;

                    // Convenience for the sample so servers can register without
                    // pre-provisioning trust. Do NOT enable this in production;
                    // instead exchange the application instance certificates.
                    options.AutoAcceptUntrustedCertificates = true;
                })
                .AddOpcTcpTransport();

            using IHost host = builder.Build();

            Console.WriteLine("LDS listening on {0}. Press Ctrl-C to exit...", DefaultEndpointUrl);

            host.Run();
        }
    }
}
