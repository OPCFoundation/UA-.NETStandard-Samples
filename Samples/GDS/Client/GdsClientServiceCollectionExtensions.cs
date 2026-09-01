/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// The registrations the Global Discovery Client is composed of.
    /// </summary>
    /// <remarks>
    /// The stack registers the GDS, push configuration and local discovery clients itself
    /// through <c>AddGdsClient</c>, so the sample does not construct them. It is written as an
    /// extension rather than inline in <c>Program.cs</c> so that a test can build the sample
    /// the way the sample builds itself instead of repeating its wiring.
    /// </remarks>
    public static class GdsClientServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the three clients the main form is built from, on a session factory
        /// which produces managed sessions.
        /// </summary>
        /// <remarks>
        /// <c>AddGdsClient</c> takes the <see cref="ISessionFactory"/> out of the container,
        /// so this registration is the single place which decides how the sample talks to the
        /// global discovery server and to the servers it manages: a
        /// <see cref="ManagedSessionFactory"/> gives every one of those connections the
        /// built-in reconnect and failover of a <see cref="ManagedSession"/>, and the V2
        /// subscription engine which the server status panel monitors through.
        /// <para>
        /// The <see cref="ApplicationConfiguration"/> has to be registered as well, which
        /// <c>AddSampleApplication</c> does.
        /// </para>
        /// </remarks>
        /// <param name="services">The service collection.</param>
        public static IServiceCollection AddGlobalDiscoveryClient(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<ISessionFactory>(
                provider => new ManagedSessionFactory(provider.GetRequiredService<ITelemetryContext>()));

            services.AddOpcUa()
                .AddGdsClient()
                .AddLocalDiscoveryServerClient();

            return services;
        }
    }
}
