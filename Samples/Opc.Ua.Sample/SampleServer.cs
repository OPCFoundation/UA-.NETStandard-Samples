/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Sample
{
    /// <summary>
    /// A class which implements an instance of a UA server.
    /// </summary>
    public partial class SampleServer : ReverseConnectServer
    {
        #region Constructors
        /// <summary>
        /// Initializes the server instance with the required telemetry context.
        /// </summary>
        public SampleServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            // register the node manager factories. the server creates the node
            // managers from them while it builds the master node manager on startup.
            AddNodeManager(new global::TestData.TestDataNodeManagerFactory());
            AddNodeManager(new global::MemoryBuffer.MemoryBufferNodeManagerFactory());
            AddNodeManager(new global::Boiler.BoilerNodeManagerFactory());
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Initializes the server before it starts up.
        /// </summary>
        /// <remarks>
        /// This method is called before any startup processing occurs. The sub-class may update the 
        /// configuration object or do any other application specific startup tasks.
        /// </remarks>
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            Debug.WriteLine("The server is starting.");

            base.OnServerStarting(configuration);

            // it is up to the application to decide how to validate user identity tokens.
            // this function creates validator for X509 identity tokens.
            CreateUserIdentityValidators(configuration);
        }

        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            // register the user token authenticators for the identity token types supported by
            // this sample. all valid users are accepted by default (anonymous tokens fall back to
            // the default identity because no authenticator claims them).
            RegisterUserTokenAuthenticators(server);
        }

        /// <summary>
        /// Cleans up before the server shuts down.
        /// </summary>
        /// <remarks>
        /// This method is called before any shutdown processing occurs.
        /// </remarks>
        protected override async ValueTask OnServerStoppingAsync(CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("The Server is stopping.");

            await base.OnServerStoppingAsync(cancellationToken);
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties();

            properties.ManufacturerName = "OPC Foundation";
            properties.ProductName = "OPC UA SDK Samples";
            properties.ProductUri = "http://opcfoundation.org/UA/Samples/v1.0";
            properties.SoftwareVersion = Utils.GetAssemblySoftwareVersion();
            properties.BuildNumber = Utils.GetAssemblyBuildNumber();
            properties.BuildDate = Utils.GetAssemblyTimestamp();

            // TBD - All applications have software certificates that need to added to the properties.

            // for (int ii = 0; ii < certificates.Count; ii++)
            // {
            //    properties.SoftwareCertificates.Add(certificates[ii]);
            // }

            return properties;
        }

        /// <summary>
        /// Initializes the address space after the NodeManagers have started.
        /// </summary>
        /// <remarks>
        /// This method can be used to create any initialization that requires access to node managers.
        /// </remarks>
        protected override void OnNodeManagerStarted(IServerInternal server)
        {
            Debug.WriteLine("The NodeManagers have started.");

            // allow base class processing to happen first.
            base.OnNodeManagerStarted(server);
        }

#if USER_AUTHENTICATION
        /// <summary>
        /// Creates the resource manager for the server.
        /// </summary>
        protected override ResourceManager CreateResourceManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            ResourceManager resourceManager = new ResourceManager(server, configuration);
            
            // add some localized strings to the resource manager to demonstrate that localization occurs.
            resourceManager.Add("InvalidPassword", "de-DE", "Das Passwort ist nicht gültig für Konto '{0}'.");
            resourceManager.Add("InvalidPassword", "es-ES", "La contraseña no es válida para la cuenta de '{0}'.");

            resourceManager.Add("UnexpectedUserTokenError", "fr-FR", "Une erreur inattendue s'est produite lors de la validation utilisateur.");
            resourceManager.Add("UnexpectedUserTokenError", "de-DE", "Ein unerwarteter Fehler ist aufgetreten während des Anwenders.");
           
            return resourceManager;
        }
#endif
        #endregion
    }
}
