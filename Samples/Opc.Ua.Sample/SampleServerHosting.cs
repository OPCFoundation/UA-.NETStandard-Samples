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
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The composition root of the UA sample server: the node managers the server is
    /// made of and the user token authenticators it verifies its sessions with.
    /// </summary>
    /// <remarks>
    /// The sample server is hosted two ways: the sample server application and the
    /// sample client, which is a client and a server at once, run it on the application
    /// instance their user interface is built around, and the tests host it from its
    /// configuration file. Both describe the server on the server builder of the stack
    /// through <see cref="AddUaSampleServer(IOpcUaServerBuilder)"/>, so the sample has
    /// no server class of its own.
    /// </remarks>
    public static class UaSampleServerHosting
    {
        /// <summary>
        /// The application configuration file of the sample server.
        /// </summary>
        public const string ConfigurationFile = "Opc.Ua.SampleServer.Config.xml";

        /// <summary>
        /// What the UA sample server is made of: the test data, the memory buffer and
        /// the boiler node managers, and the authenticators which accept a user name
        /// token with a password, and a user certificate the trust lists of the
        /// configuration know.
        /// </summary>
        /// <param name="server">The server builder of the stack.</param>
        public static IOpcUaServerBuilder AddUaSampleServer(this IOpcUaServerBuilder server)
        {
            ArgumentNullException.ThrowIfNull(server);

            return server
                .AddNodeManager<global::TestData.TestDataNodeManagerFactory>()
                .AddNodeManager<global::MemoryBuffer.MemoryBufferNodeManagerFactory>()
                .AddNodeManager<global::Boiler.BoilerNodeManagerFactory>()
                .AddIdentityAuthenticator(
                    (_, _) => new UserNamePasswordAuthenticator(AuthenticateUserNameAsync))
                .AddIdentityAuthenticator(
                    (_, certificateValidator) => new X509Authenticator(certificateValidator));
        }

        /// <summary>
        /// Registers the sample server as the hosted OPC UA server of the stack, with
        /// the configuration loaded from its configuration file.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configurationFile">The configuration file to load, when the
        /// sample is hosted from somewhere else than its own directory; the file of
        /// the sample server when <c>null</c>.</param>
        /// <param name="configure">Applied to the configuration right after it has
        /// been read, for the settings the file cannot express.</param>
        public static IServiceCollection AddUaSampleServer(
            this IServiceCollection services,
            string configurationFile = null,
            Action<ApplicationConfiguration> configure = null)
        {
            return services.AddSampleServer(
                configurationFile ?? ConfigurationFile,
                server => server.AddUaSampleServer(),
                configure);
        }

        /// <summary>
        /// The sample accepts every user name, as long as a password was sent with it.
        /// </summary>
        private static ValueTask<IUserIdentity> AuthenticateUserNameAsync(
            UserNameIdentityTokenHandler handler,
            CancellationToken ct)
        {
            if (handler.DecryptedPassword == null || handler.DecryptedPassword.Length == 0)
            {
                var info = new TranslationInfo(
                    "InvalidPassword",
                    "en-US",
                    "Specified password is not valid for user '{0}'.",
                    handler.UserName);

                throw new ServiceResultException(new ServiceResult(
                    "http://opcfoundation.org/UA/Sample/",
                    new StatusCode(StatusCodes.BadIdentityTokenRejected.Code, "InvalidPassword"),
                    new LocalizedText(info)));
            }

            return new ValueTask<IUserIdentity>(new UserIdentity(handler));
        }
    }
}
