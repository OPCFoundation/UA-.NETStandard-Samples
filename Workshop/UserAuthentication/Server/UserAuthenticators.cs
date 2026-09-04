/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua;
using Opc.Ua.Identity;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Quickstarts.UserAuthenticationServer
{
    /// <summary>
    /// How the sample verifies its users: a user name and password are checked against
    /// the Windows accounts of the machine, a user certificate against the
    /// <c>TrustedPeople</c> store of the machine. Each check is handed to the matching
    /// authenticator of the stack, which the composition root registers with the
    /// server builder; the identity registry of the server calls it for every session
    /// which presents that kind of token.
    /// </summary>
    public static class UserAuthenticators
    {
        /// <summary>
        /// The authenticator for user name tokens: the account has to exist on the
        /// machine, and the password has to be right.
        /// </summary>
        public static IUserTokenAuthenticator UserName()
        {
            return new UserNamePasswordAuthenticator(AuthenticateUserNameAsync);
        }

        /// <summary>
        /// The authenticator for certificate tokens: the certificate has to be in the
        /// <c>TrustedPeople</c> store of the current user or the machine. It only
        /// accepts anything when the configuration names a trust list for the
        /// certificate user token policy, the way the pre 2.0 sample did.
        /// </summary>
        /// <param name="configuration">The loaded configuration of the server.</param>
        public static IUserTokenAuthenticator Certificate(ApplicationConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            bool enabled = HasCertificateTrustList(configuration);

            return new X509Authenticator((handler, _) => AuthenticateX509(handler, enabled));
        }

        private static bool HasCertificateTrustList(ApplicationConfiguration configuration)
        {
            foreach (UserTokenPolicy policy in configuration.ServerConfiguration.UserTokenPolicies)
            {
                if (policy.TokenType != UserTokenType.Certificate)
                {
                    continue;
                }

                var qname = new XmlQualifiedName(policy.PolicyId, Opc.Ua.Namespaces.OpcUa);
                if (configuration.ParseExtension<CertificateTrustList>(qname) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static ValueTask<IUserIdentity> AuthenticateUserNameAsync(
            UserNameIdentityTokenHandler handler,
            CancellationToken ct)
        {
            if (handler.DecryptedPassword == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    "The password of the user name token could not be decrypted.");
            }

            VerifyPassword(handler.UserName, Encoding.UTF8.GetString(handler.DecryptedPassword));

            return new ValueTask<IUserIdentity>(new UserIdentity(handler));
        }

        private static ValueTask<IUserIdentity> AuthenticateX509(
            X509IdentityTokenHandler handler,
            bool enabled)
        {
            var token = (X509IdentityToken)handler.Token;

            using X509Certificate2 userCertificate =
                X509CertificateLoader.LoadCertificate(token.CertificateData.ToArray());

            VerifyCertificate(userCertificate, enabled);

            return new ValueTask<IUserIdentity>(new UserIdentity(token));
        }

        private static void VerifyPassword(string userName, string password)
        {
            const int LOGON32_PROVIDER_DEFAULT = 0;
            const int LOGON32_LOGON_NETWORK = 3;

            IntPtr handle = IntPtr.Zero;
            bool result = NativeMethods.LogonUser(
                userName,
                string.Empty,
                password,
                LOGON32_LOGON_NETWORK,
                LOGON32_PROVIDER_DEFAULT,
                ref handle);

            if (!result)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Login failed for user: {0}",
                    userName);
            }

            NativeMethods.CloseHandle(handle);
        }

        private static void VerifyCertificate(X509Certificate2 certificate, bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    throw new InvalidOperationException(
                        "The configuration names no trust list for the certificate user token policy.");
                }

                bool trusted = false;
                foreach (StoreLocation location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
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
                var info = new TranslationInfo(
                    "InvalidCertificate",
                    "en-US",
                    "'{0}' is not a trusted user certificate.",
                    certificate.Subject);

                throw new ServiceResultException(new ServiceResult(
                    Namespaces.UserAuthentication,
                    new StatusCode(StatusCodes.BadIdentityTokenRejected.Code, "InvalidCertificate"),
                    new LocalizedText(info),
                    e));
            }
        }

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

            [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
            public static extern bool CloseHandle(IntPtr handle);
#pragma warning restore CA5392
        }
    }

    /// <summary>
    /// The translations of the errors the authenticators report, added to the resource
    /// manager of the server once it has started: a client whose session asked for one
    /// of these locales gets the message in its language.
    /// </summary>
    public sealed class UserAuthenticationTranslations : IServerStartupTask
    {
        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken)
        {
            // the resource manager is a subsystem the ambient server context
            // deliberately does not hand out; the live server does.
            ResourceManager resources = ((IServerInternal)server).ResourceManager;

            resources.Add("InvalidPassword", "de-DE", "Das Passwort ist nicht gültig für Konto '{0}'.");
            resources.Add("InvalidPassword", "es-ES", "La contraseña no es válida para la cuenta de '{0}'.");
            resources.Add("UnexpectedUserTokenError", "fr-FR", "Une erreur inattendue s'est produite lors de la validation utilisateur.");
            resources.Add("UnexpectedUserTokenError", "de-DE", "Ein unerwarteter Fehler ist aufgetreten während des Anwenders.");
            resources.Add("BadUserAccessDenied", "fr-FR", "Utilisateur ne peut pas changer la valeur.");
            resources.Add("BadUserAccessDenied", "de-DE", "User nicht ändern können, Wert.");

            return ValueTask.CompletedTask;
        }
    }
}
