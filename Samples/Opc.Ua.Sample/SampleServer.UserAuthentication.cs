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
using System.Text;
using Microsoft.Extensions.Logging;
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
        /// Called when a client tries to change its user identity.
        /// </summary>
        private void SessionManager_ImpersonateUser(ISession session, ImpersonateEventArgs args)
        {
            // check for a WSS token.
            IssuedIdentityToken wssToken = args.UserIdentityTokenHandler?.Token as IssuedIdentityToken;

            // check for a user name token.
            UserNameIdentityToken userNameToken = args.UserIdentityTokenHandler?.Token as UserNameIdentityToken;

            if (userNameToken != null)
            {
                VerifyPassword(userNameToken.UserName, Encoding.UTF8.GetString(userNameToken.Password.Span.ToArray()));
                args.Identity = new UserIdentity(userNameToken);
                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.LogInformation("UserName Token Accepted: {DisplayName}", args.Identity.DisplayName);
                }
                return;
            }

            // check for x509 user token.
            X509IdentityToken x509Token = args.UserIdentityTokenHandler?.Token as X509IdentityToken;

            if (x509Token != null)
            {
                VerifyCertificate(x509Token.CertificateData);
                args.Identity = new UserIdentity(x509Token);
                if (m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.LogInformation("X509 Token Accepted: {DisplayName}", args.Identity.DisplayName);
                }
                return;
            }
        }

        /// <summary>
        /// Validates the password for a username token.
        /// </summary>
        private void VerifyPassword(string userName, string password)
        {
            if (String.IsNullOrEmpty(password))
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

        #region Private Fields
        private ICertificateValidatorEx m_certificateValidator;
        #endregion 
    }
}
