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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace Opc.Ua.Sample
{
    public partial class SampleServer
    {
        #region User Validation Functions
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
                    // check if user certificate trust lists are specified in configuration.
                    if (configuration.SecurityConfiguration.TrustedUserCertificates != null &&
                        configuration.SecurityConfiguration.UserIssuerCertificates != null)
                    {
                        // The server's CertificateManager already maps the configured
                        // TrustedUserCertificates / UserIssuerCertificates onto the Users
                        // trust list. Validate user certificates against it per call.
                        m_certificateValidator = CertificateManager;
                    }
                }
            }
        }

        /// <summary>
        /// Registers the user token authenticators supported by this sample with the server
        /// identity registry. This replaces the obsolete <c>ISessionManager.ImpersonateUser</c>
        /// event with the <see cref="IUserTokenAuthenticator"/> +
        /// <see cref="IServerIdentityRegistry"/> model.
        /// </summary>
        internal void RegisterUserTokenAuthenticators(IServerInternal server)
        {
            server.IdentityRegistry.Register(new UserNameTokenAuthenticator(this));
            server.IdentityRegistry.Register(new CertificateTokenAuthenticator(this));
        }

        /// <summary>
        /// Validates a UserName identity token and produces the associated user identity.
        /// </summary>
        private AuthenticationResult AuthenticateUserNameToken(UserNameIdentityTokenHandler tokenHandler)
        {
            try
            {
                VerifyPassword(tokenHandler.UserName, tokenHandler.DecryptedPassword);

                var identity = new UserIdentity(tokenHandler);
                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.LogInformation("UserName Token Accepted: {DisplayName}", identity.DisplayName);
                }

                return AuthenticationResult.Accept(identity);
            }
            catch (ServiceResultException e)
            {
                return AuthenticationResult.Reject(e.Result);
            }
        }

        /// <summary>
        /// Validates an X509 identity token and produces the associated user identity.
        /// </summary>
        private AuthenticationResult AuthenticateCertificateToken(X509IdentityTokenHandler tokenHandler)
        {
            try
            {
                var wireToken = (X509IdentityToken)tokenHandler.Token;
                VerifyCertificate(wireToken.CertificateData);

                var identity = new UserIdentity(tokenHandler);
                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.LogInformation("X509 Token Accepted: {DisplayName}", identity.DisplayName);
                }

                return AuthenticationResult.Accept(identity);
            }
            catch (ServiceResultException e)
            {
                return AuthenticationResult.Reject(e.Result);
            }
        }

        /// <summary>
        /// Validates the password for a username token.
        /// </summary>
        private void VerifyPassword(string userName, byte[] password)
        {
            if (password == null || password.Length == 0)
            {
                // construct translation object with default text.
                TranslationInfo info = new TranslationInfo(
                    "InvalidPassword",
                    "en-US",
                    "Specified password is not valid for user '{0}'.",
                    userName);

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(new ServiceResult(
                    "http://opcfoundation.org/UA/Sample/",
                    new StatusCode(
                    StatusCodes.BadIdentityTokenRejected.Code,
                    "InvalidPassword"),
                    new LocalizedText(info)));
            }
        }

        /// <summary>
        /// Verifies that a certificate user token is trusted.
        /// </summary>
        private void VerifyCertificate(ByteString certificateData)
        {
            using Certificate certificate = Certificate.FromRawData(certificateData.Span.ToArray());

            try
            {
                if (m_certificateValidator == null)
                {
                    throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
                }

                CertificateValidationResult validationResult = m_certificateValidator
                    .ValidateAsync(certificate, TrustListIdentifier.Users, System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();

                if (!validationResult.IsValid)
                {
                    throw new ServiceResultException(validationResult.StatusCode);
                }
            }
            catch (Exception e)
            {
                TranslationInfo info;
                StatusCode result = StatusCodes.BadIdentityTokenRejected;
                ServiceResultException se = e as ServiceResultException;
                if (se != null && se.StatusCode == StatusCodes.BadCertificateUseNotAllowed)
                {
                    info = new TranslationInfo(
                        "InvalidCertificate",
                        "en-US",
                        "'{0}' is an invalid user certificate.",
                        certificate.Subject);

                    result = StatusCodes.BadIdentityTokenInvalid;
                }
                else
                {
                    // construct translation object with default text.
                    info = new TranslationInfo(
                        "UntrustedCertificate",
                        "en-US",
                        "'{0}' is not a trusted user certificate.",
                        certificate.Subject);
                }

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(new ServiceResult(
                    "http://opcfoundation.org/UA/Sample/",
                    result,
                    new LocalizedText(info)));
            }
        }
        #endregion

        #region User Token Authenticators
        /// <summary>
        /// Authenticates UserName identity tokens supported by the sample server.
        /// </summary>
        private sealed class UserNameTokenAuthenticator : IUserTokenAuthenticator
        {
            private readonly SampleServer m_server;

            public UserNameTokenAuthenticator(SampleServer server)
            {
                m_server = server;
            }

            /// <inheritdoc/>
            public UserTokenType TokenType => UserTokenType.UserName;

            /// <inheritdoc/>
            public string IssuedTokenProfileUri => null;

            /// <inheritdoc/>
            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                if (context.TokenHandler is not UserNameIdentityTokenHandler tokenHandler)
                {
                    return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
                }

                return new ValueTask<AuthenticationResult>(
                    m_server.AuthenticateUserNameToken(tokenHandler));
            }
        }

        /// <summary>
        /// Authenticates X509 identity tokens supported by the sample server.
        /// </summary>
        private sealed class CertificateTokenAuthenticator : IUserTokenAuthenticator
        {
            private readonly SampleServer m_server;

            public CertificateTokenAuthenticator(SampleServer server)
            {
                m_server = server;
            }

            /// <inheritdoc/>
            public UserTokenType TokenType => UserTokenType.Certificate;

            /// <inheritdoc/>
            public string IssuedTokenProfileUri => null;

            /// <inheritdoc/>
            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                if (context.TokenHandler is not X509IdentityTokenHandler tokenHandler)
                {
                    return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
                }

                return new ValueTask<AuthenticationResult>(
                    m_server.AuthenticateCertificateToken(tokenHandler));
            }
        }
        #endregion

        #region Private Fields
        private ICertificateValidatorEx m_certificateValidator;
        #endregion 
    }
}
