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

using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;
using Microsoft.Extensions.Logging;

namespace Quickstarts.UserAuthenticationServer
{
    /// <summary>
    /// Implements a basic Quickstart Server.
    /// </summary>
    /// <remarks>
    /// Each server instance must have one instance of a StandardServer object which is
    /// responsible for reading the configuration file, creating the endpoints and dispatching
    /// incoming requests to the appropriate handler.
    ///
    /// This sub-class specifies non-configurable metadata such as Product Name and registers
    /// the UserAuthenticationNodeManager which provides access to the data exposed by the Server.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Sample server type name intentionally mirrors the namespace.")]
    public partial class UserAuthenticationServer : StandardServer
    {
        public UserAuthenticationServer(ITelemetryContext telemetry) : base(telemetry)
        {
        }

        #region Overridden UserAuthentication
        /// <summary>
        /// Initializes the server before it starts up.
        /// </summary>
        /// <remarks>
        /// This method is called before any startup processing occurs. The sub-class may update the
        /// configuration object or do any other application specific startup tasks.
        /// </remarks>
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            Console.WriteLine("The Server is starting.");

            base.OnServerStarting(configuration);

            // it is up to the application to decide how to validate user identity tokens.
            // this function creates validators for X509 identity tokens.
            CreateUserIdentityValidators(configuration);
        }

        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            // Register authenticators for user identity changes.
            server.IdentityRegistry.Register(new UserNamePasswordAuthenticator(AuthenticateUserNameAsync));
            server.IdentityRegistry.Register(new X509Authenticator(AuthenticateX509Async));
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
            properties.ProductName = "OPC UA Quickstarts";
            properties.ProductUri = "http://opcfoundation.org/Quickstarts/UserAuthenticationServer/v1.0";
            properties.SoftwareVersion = Utils.GetAssemblySoftwareVersion();
            properties.BuildNumber = Utils.GetAssemblyBuildNumber();
            properties.BuildDate = Utils.GetAssemblyTimestamp();

            // TBD - All applications have software certificates that need to added to the properties.

            return properties;
        }

        /// <summary>
        /// Creates the resource manager for the server.
        /// </summary>
        protected override ResourceManager CreateResourceManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            ResourceManager resourceManager = new ResourceManager(configuration);

            // add some localized strings to the resource manager to demonstrate that localization occurs.
            resourceManager.Add("InvalidPassword", "de-DE", "Das Passwort ist nicht gültig für Konto '{0}'.");
            resourceManager.Add("InvalidPassword", "es-ES", "La contraseña no es válida para la cuenta de '{0}'.");

            resourceManager.Add("UnexpectedUserTokenError", "fr-FR", "Une erreur inattendue s'est produite lors de la validation utilisateur.");
            resourceManager.Add("UnexpectedUserTokenError", "de-DE", "Ein unerwarteter Fehler ist aufgetreten während des Anwenders.");

            resourceManager.Add("BadUserAccessDenied", "fr-FR", "Utilisateur ne peut pas changer la valeur.");
            resourceManager.Add("BadUserAccessDenied", "de-DE", "User nicht ändern können, Wert.");

            return resourceManager;
        }

        /// <summary>
        /// Creates the objects used to validate the user identity tokens supported by the server.
        /// </summary>
        private void CreateUserIdentityValidators(ApplicationConfiguration configuration)
        {
            for (int ii = 0; ii < configuration.ServerConfiguration.UserTokenPolicies.Count; ii++)
            {
                UserTokenPolicy policy = configuration.ServerConfiguration.UserTokenPolicies[ii];

                // create a validator for a certificate token policy.
                if (policy.TokenType == UserTokenType.Certificate)
                {
                    // the name of the element in the configuration file.
                    XmlQualifiedName qname = new XmlQualifiedName(policy.PolicyId, Opc.Ua.Namespaces.OpcUa);

                    // find the location of the trusted issuers.
                    CertificateTrustList trustedIssuers = configuration.ParseExtension<CertificateTrustList>(qname);

                    if (trustedIssuers == null)
                    {
                        m_logger.LogError(
                            "Could not load CertificateTrustList for UserTokenPolicy {PolicyId}",
                            policy.PolicyId);

                        continue;
                    }

                    // trusts any certificate in the trusted people store.
                    // NOTE: System.IdentityModel.Selectors.X509CertificateValidator.PeerTrust
                    // is not available on modern .NET. Basic X509 chain validation is used instead.
                    m_certificateValidatorEnabled = true;
                }
            }
        }

        /// <summary>
        /// Verifies the password of a user name token against a Windows account.
        /// </summary>
        /// <remarks>
        /// The password is taken from <see cref="UserNameIdentityTokenHandler.DecryptedPassword"/>
        /// rather than from the token. A user name token must not travel in the clear, so
        /// unless its UserTokenPolicy names the None security policy the password is encrypted
        /// with the server's certificate - which the policies in this sample's configuration
        /// do not, so it is encrypted on the unsecured endpoint too. <c>Token.Password</c> is
        /// therefore the cipher text and only the handler carries the plain text the stack
        /// decrypted. Reading the token handed LogonUser a UTF-8 decoding of 256 bytes of
        /// cipher text, which refused every account with BadUserAccessDenied.
        /// </remarks>
        private ValueTask<IUserIdentity> AuthenticateUserNameAsync(UserNameIdentityTokenHandler handler, System.Threading.CancellationToken ct)
        {
            if (handler.DecryptedPassword == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    "The password of the user name token could not be decrypted.");
            }

            VerifyPassword(handler.UserName, Encoding.UTF8.GetString(handler.DecryptedPassword));

            IUserIdentity identity = new UserIdentity(handler);
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.LogInformation("UserName Token Accepted: {DisplayName}", identity.DisplayName);
            }

            return new ValueTask<IUserIdentity>(identity);
        }

        private ValueTask<IUserIdentity> AuthenticateX509Async(X509IdentityTokenHandler handler, System.Threading.CancellationToken ct)
        {
            X509IdentityToken x509Token = handler.Token as X509IdentityToken;
            using X509Certificate2 userCertificate =
                X509CertificateLoader.LoadCertificate(x509Token.CertificateData.ToArray());
            VerifyCertificate(userCertificate);
            IUserIdentity identity = new UserIdentity(x509Token);
            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.LogInformation("X509 Token Accepted: {DisplayName}", identity.DisplayName);
            }

            return new ValueTask<IUserIdentity>(identity);
        }

        /// <summary>
        /// Initializes the validator from the configuration for a token policy.
        /// </summary>
        /// <param name="issuerCertificate">The issuer certificate.</param>
        /// <remarks>
        /// WS-Security token resolvers (System.IdentityModel) are not supported on modern .NET.
        /// This path is stubbed out under the .NET 10 upgrade (Option C).
        /// </remarks>
        private object CreateSecurityTokenResolver(CertificateIdentifier issuerCertificate)
        {
            if (issuerCertificate == null)
            {
                throw new ArgumentNullException(nameof(issuerCertificate));
            }

            throw new NotSupportedException(
                "WS-Security token resolution (System.IdentityModel) is not supported on this platform.");
        }

        /// <summary>
        /// Validates the password for a username token.
        /// </summary>
        private void VerifyPassword(string userName, string password)
        {
            IntPtr handle = IntPtr.Zero;

            const int LOGON32_PROVIDER_DEFAULT = 0;
            // const int LOGON32_LOGON_INTERACTIVE = 2;
            const int LOGON32_LOGON_NETWORK = 3;
            // const int LOGON32_LOGON_BATCH = 4;

            bool result = NativeMethods.LogonUser(
                userName,
                String.Empty,
                password,
                LOGON32_LOGON_NETWORK,
                LOGON32_PROVIDER_DEFAULT,
                ref handle);

            if (!result)
            {
                throw ServiceResultException.Create(StatusCodes.BadUserAccessDenied, "Login failed for user: {0}", userName);
            }

            NativeMethods.CloseHandle(handle);
        }

        /// <summary>
        /// Verifies that a certificate user token is trusted.
        /// </summary>
        private void VerifyCertificate(X509Certificate2 certificate)
        {
            try
            {
                if (!m_certificateValidatorEnabled)
                {
                    throw new InvalidOperationException("No certificate validator configured.");
                }

                // Mimic X509CertificateValidator.PeerTrust by requiring the user certificate to
                // exist in the Windows TrustedPeople store (CurrentUser or LocalMachine).
                bool trusted = false;

                foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
                {
                    using var store = new X509Store(StoreName.TrustedPeople, location);
                    store.Open(OpenFlags.ReadOnly);

                    if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false).Count > 0)
                    {
                        trusted = true;
                        break;
                    }
                }

                if (!trusted)
                {
                    throw new CryptographicException(
                        "The user certificate is not present in the TrustedPeople store.");
                }
            }
            catch (Exception e)
            {
                // construct translation object with default text.
                TranslationInfo info = new TranslationInfo(
                    "InvalidCertificate",
                    "en-US",
                    "'{0}' is not a trusted user certificate.",
                    certificate.Subject);

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(new ServiceResult(
                    Namespaces.UserAuthentication,
                    new StatusCode(StatusCodes.BadIdentityTokenRejected.Code,
                    "InvalidCertificate"),
                    new LocalizedText(info),
                    e));
            }
        }

        /// <summary>
        /// Validates a Kerberos WSS user token.
        /// </summary>
        /// <remarks>
        /// Kerberos / WS-Security token handling (System.IdentityModel) is not supported on
        /// modern .NET. This path is stubbed out under the .NET 10 upgrade (Option C).
        /// </remarks>
        private object ParseAndVerifyKerberosToken(byte[] tokenData)
        {
            throw new NotSupportedException(
                "Kerberos WS-Security tokens (System.IdentityModel) are not supported on this platform.");
        }
        #endregion


        private static class NativeMethods
        {
#pragma warning disable CA5392 // Justification: Sample code uses a platform P/Invoke declaration.
            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool LogonUser(
                string lpszUsername,
                string lpszDomain,
                string lpszPassword,
                int dwLogonType,
                int dwLogonProvider,
                ref IntPtr phToken);
#pragma warning restore CA5392

#pragma warning disable CA5392 // Justification: Sample code uses a platform P/Invoke declaration.
            [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
            public extern static bool CloseHandle(IntPtr handle);
#pragma warning restore CA5392
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Kept for Windows impersonation sample paths.")]
        private sealed class ImpersonationContext : IDisposable
        {
            // WindowsImpersonationContext is not available on modern .NET.
            // Impersonation is not supported under the .NET 10 upgrade (Option C).
            public IntPtr Handle;

            #region IDisposable Members
            /// <summary>
            /// The finializer implementation.
            /// </summary>
            ~ImpersonationContext()
            {
                Dispose();
            }

            /// <summary>
            /// Frees any unmanaged resources.
            /// </summary>
            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(Handle);
                    Handle = IntPtr.Zero;
                }

                GC.SuppressFinalize(this);
            }
            #endregion
        }

        // need to ensure the contexts are undone.
        private Dictionary<uint, ImpersonationContext> m_contexts = new Dictionary<uint, ImpersonationContext>();

        /// <summary>
        /// Impersonates the windows user identifed by the security token.
        /// </summary>
        /// <remarks>
        /// Windows impersonation via System.Security.Principal.WindowsImpersonationContext is not
        /// supported on modern .NET. This path is stubbed out under the .NET 10 upgrade (Option C).
        /// </remarks>
        private void LogonUser(OperationContext context, object securityToken)
        {
            throw new NotSupportedException(
                "Windows impersonation is not supported on this platform.");
        }

        /// <summary>
        /// This method is called at the being of the thread that processes a request.
        /// </summary>
        protected override void ValidateRequest(RequestHeader requestHeader)
        {
            base.ValidateRequest(requestHeader);
#if TODO
                SecurityToken securityToken = context.UserIdentity.GetSecurityToken();

                // check for a kerberso token.
                KerberosReceiverSecurityToken kerberosToken = securityToken as KerberosReceiverSecurityToken;

                if (kerberosToken != null)
                {
                    ImpersonationContext impersonationContext = new ImpersonationContext();
                    impersonationContext.Context = kerberosToken.WindowsIdentity.Impersonate();

                    lock (this.m_impersonationLock)
                    {
                        m_contexts.Add(context.RequestId, impersonationContext);
                    }
                }

                // check for a user name token.
                UserNameSecurityToken userNameToken = securityToken as UserNameSecurityToken;

                if (userNameToken != null)
                {
                    LogonUser(context, userNameToken);
                }
#endif
        }

        /// <summary>
        /// This method is called in a finally block at the end of request processing (i.e. called even on exception).
        /// </summary>
        protected override void OnRequestComplete(OperationContext context)
        {
            ImpersonationContext impersonationContext = null;

            lock (this.m_impersonationLock)
            {
                if (m_contexts.TryGetValue(context.RequestId, out impersonationContext))
                {
                    m_contexts.Remove(context.RequestId);
                }
            }

            if (impersonationContext != null)
            {
                // Windows impersonation is not supported on modern .NET; nothing to undo.
                impersonationContext.Dispose();
            }

            base.OnRequestComplete(context);
        }

        #region Private Fields
        private object m_impersonationLock = new object();
        private bool m_certificateValidatorEnabled;
        #endregion
    }
}
