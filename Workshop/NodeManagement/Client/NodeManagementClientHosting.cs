/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Microsoft.Extensions.DependencyInjection.Extensions;
using Quickstarts.NodeManagement.Client.Model;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the NodeManagement client sample: the client model its window
    /// is built on.
    /// </summary>
    /// <remarks>
    /// The counterpart of the <c>AddNodeManagementServer</c> of the server half. The window
    /// takes the model through its constructor and the container creates both, so the
    /// entry point of the sample and the tests which drive its window share this one
    /// registration.
    ///
    /// The model is a singleton because there is one window and the model is its
    /// connection to the server. It is created when the window is - on the thread which
    /// owns the message loop - which is what lets the model raise its events there.
    /// </remarks>
    public static class NodeManagementClientHosting
    {
        /// <summary>
        /// Registers the client model of the NodeManagement sample.
        /// </summary>
        /// <param name="services">The service collection.</param>
        public static IServiceCollection AddNodeManagementClient(this IServiceCollection services)
        {
            services.TryAddSingleton<NodeManagementClientModel>();

            return services;
        }
    }
}
