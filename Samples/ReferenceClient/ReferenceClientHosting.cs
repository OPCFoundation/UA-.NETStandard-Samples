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
using Opc.Ua.Samples.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the reference client sample: what its window is built
    /// on, next to the application instance and the configuration every client sample
    /// registers.
    /// </summary>
    public static class ReferenceClientHosting
    {
        /// <summary>
        /// Registers the parts of the reference client.
        /// </summary>
        /// <remarks>
        /// The reverse connect listener is armed and released from the Server menu, so
        /// the window creates one per wait rather than holding a single instance. It
        /// asks the container for it through a factory delegate instead of constructing
        /// it out of its own fields, which is what keeps the configuration and the
        /// telemetry of the listener the ones the host loaded.
        /// </remarks>
        /// <param name="services">The service collection.</param>
        public static IServiceCollection AddReferenceClient(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddTransient<ReverseConnectListener>();
            services.TryAddSingleton<Func<ReverseConnectListener>>(
                provider => provider.GetRequiredService<ReverseConnectListener>);

            return services;
        }
    }
}
