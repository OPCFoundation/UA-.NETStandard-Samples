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
using Opc.Ua;
using Opc.Ua.Identity;
using Opc.Ua.Samples.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The registrations the samples make on the server builder of the stack which
    /// the stack itself has no method for. Each is a one-liner over a public
    /// registration type of the stack, so what it registers is exactly what the
    /// corresponding <c>Add...&lt;T&gt;()</c> of the stack registers.
    /// </summary>
    public static class SampleServerBuilderExtensions
    {
        /// <summary>
        /// Registers a node manager factory the sample already holds as an instance,
        /// the way <c>AddNodeManager&lt;TFactory&gt;()</c> registers a factory type.
        /// </summary>
        /// <param name="server">The server builder of the stack.</param>
        /// <param name="factory">The node manager factory.</param>
        public static IOpcUaServerBuilder AddNodeManager(
            this IOpcUaServerBuilder server,
            IAsyncNodeManagerFactory factory)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(factory);

            server.Services.AddSingleton(new OpcUaServerNodeManagerRegistration(factory));

            return server;
        }

        /// <summary>
        /// Registers a synchronous node manager factory the sample already holds as an
        /// instance, the way <c>AddSyncNodeManager&lt;TFactory&gt;()</c> registers a
        /// factory type.
        /// </summary>
        /// <param name="server">The server builder of the stack.</param>
        /// <param name="factory">The node manager factory.</param>
        public static IOpcUaServerBuilder AddNodeManager(
            this IOpcUaServerBuilder server,
            INodeManagerFactory factory)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(factory);

            server.Services.AddSingleton(new OpcUaServerNodeManagerRegistration(factory));

            return server;
        }

        /// <summary>
        /// Registers the node managers whose number, or whose parameters, are only
        /// known once the configuration of the sample has been loaded - one node manager
        /// per configured endpoint, say. The stack collects its node manager
        /// registrations before it reads the configuration, so those factories cannot
        /// be registered up front; <paramref name="factories"/> runs with the
        /// configuration loaded, right before the server starts, and the factories it
        /// returns are added to the server next to the registered ones.
        /// </summary>
        /// <param name="server">The server builder of the stack.</param>
        /// <param name="factories">Creates the node manager factories. The loaded
        /// <see cref="ApplicationConfiguration"/> can be resolved from the provider.</param>
        public static IOpcUaServerBuilder AddNodeManagers(
            this IOpcUaServerBuilder server,
            Func<IServiceProvider, IEnumerable<IAsyncNodeManagerFactory>> factories)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(factories);

            server.Services.AddSingleton(new ConfiguredNodeManagerFactories(factories));

            return server;
        }

        /// <summary>
        /// Registers an identity authenticator the sample creates itself, the way
        /// <c>AddIdentityAuthenticator&lt;TAuthenticator&gt;()</c> registers an
        /// authenticator type. For the authenticators of the stack which take a
        /// verification callback - <see cref="UserNamePasswordAuthenticator"/>,
        /// <see cref="X509Authenticator"/> - and cannot be created by the container.
        /// </summary>
        /// <param name="server">The server builder of the stack.</param>
        /// <param name="factory">Creates the authenticator once the server has started,
        /// from the container and the certificate validator of the server.</param>
        public static IOpcUaServerBuilder AddIdentityAuthenticator(
            this IOpcUaServerBuilder server,
            Func<IServiceProvider, ICertificateValidatorEx, IUserTokenAuthenticator> factory)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(factory);

            server.Services.AddSingleton(new OpcUaServerIdentityAuthenticatorRegistration(
                (provider, certificateValidator) => [factory(provider, certificateValidator)],
                isFallback: false));

            return server;
        }

        /// <summary>
        /// Registers a task the hosted server runs once the server has started, with
        /// the live server: the seam of the stack for what needs the complete address
        /// space, and used to take an override of <c>OnServerStarted</c> in a server
        /// class.
        /// </summary>
        /// <typeparam name="TTask">The task, created by the container.</typeparam>
        /// <param name="server">The server builder of the stack.</param>
        public static IOpcUaServerBuilder AddStartupTask<TTask>(this IOpcUaServerBuilder server)
            where TTask : class, IServerStartupTask
        {
            ArgumentNullException.ThrowIfNull(server);

            server.Services.AddSingleton<IServerStartupTask, TTask>();

            return server;
        }
    }
}
