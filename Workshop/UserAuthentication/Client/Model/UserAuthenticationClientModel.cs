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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Samples.Client;

namespace Quickstarts.UserAuthenticationClient.Model
{
    /// <summary>
    /// The client model of the user authentication Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sample is about the user identity tokens of OPC UA: the same node looks different
    /// depending on who is asking. The model reads and writes the log file path of the
    /// sample server, and changes the identity of the session in between with
    /// <c>UpdateSession</c>, so that the same write can be tried as one identity after
    /// another. It does not subscribe to anything.
    /// </para>
    /// <para>
    /// A refusal is the interesting outcome here, not an error: the node manager answers
    /// the user access level per session, and an identity which may not write is told so
    /// by the write handler as well. The operations therefore answer an
    /// <see cref="OperationResult"/> with the status the server gave, and a failed
    /// impersonation is answered the same way, with the status code out of the exception
    /// the stack raised.
    /// </para>
    /// </remarks>
    public sealed class UserAuthenticationClientModel : SampleClientModel
    {
        /// <summary>
        /// The namespace the sample server publishes its process in, for a caller which
        /// cannot name the constants of the client.
        /// </summary>
        public const string LogFileNamespaceUri = Namespaces.UserAuthentication;

        /// <summary>
        /// The numeric identifier of the LogFilePath variable in the namespace of the
        /// sample server.
        /// </summary>
        /// <remarks>
        /// Built-in knowledge of the information model of the server, which is what this
        /// client has anyway: it exists to talk to that one server. Only the index of the
        /// namespace is taken from the session, because a server assigns it.
        /// </remarks>
        private const uint kLogFilePathIdentifier = 2;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public UserAuthenticationClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// The LogFilePath variable of the sample server, or null while detached or when the
        /// server does not serve the namespace.
        /// </summary>
        public NodeId LogFileNodeId { get; private set; } = NodeId.Null;

        /// <summary>
        /// The user token policies of the endpoint the session was opened on: which kinds of
        /// identity the server accepts.
        /// </summary>
        public IReadOnlyList<UserTokenPolicy> UserTokenPolicies { get; private set; } = Array.Empty<UserTokenPolicy>();

        /// <summary>
        /// Splits a comma separated list of locales the way the window takes it.
        /// </summary>
        /// <param name="text">For instance "de,es,en".</param>
        public static string[] ParseLocales(string text)
        {
            return (text ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Reads the log file path of the server.
        /// </summary>
        /// <remarks>
        /// A bad status is thrown here, with the diagnostics of the server attached, because
        /// a session which cannot even read the path has nothing to show.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task<string> ReadLogFilePathAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            // want to get error text for this call.
            session.ReturnDiagnostics = DiagnosticsMasks.All;

            try
            {
                var valuesToRead = new List<ReadValueId> {
                    new ReadValueId { NodeId = LogFileNodeId, AttributeId = Attributes.Value },
                };

                ReadResponse response = await session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    valuesToRead,
                    ct).ConfigureAwait(false);

                List<DataValue> results = response.Results.ToList();
                List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

                ClientBase.ValidateResponse(results, valuesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    throw ServiceResultException.Create(
                        results[0].StatusCode,
                        0,
                        diagnosticInfos,
                        response.ResponseHeader.StringTable);
                }

                return results[0].GetValue<string>(string.Empty);
            }
            finally
            {
                session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        /// <summary>
        /// Writes the log file path of the server.
        /// </summary>
        /// <param name="path">The path the server is to log to.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>What the server answered - BadUserAccessDenied for an identity which may not write.</returns>
        public async Task<OperationResult> WriteLogFilePathAsync(string path, CancellationToken ct = default)
        {
            const string what = "Writing the log file path";

            ISession session = RequireSession();

            // want to get error text for this call.
            session.ReturnDiagnostics = DiagnosticsMasks.All;

            try
            {
                var valuesToWrite = new List<WriteValue> {
                    new WriteValue {
                        NodeId = LogFileNodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(path)),
                    },
                };

                WriteResponse response = await session.WriteAsync(null, valuesToWrite, ct).ConfigureAwait(false);

                List<StatusCode> results = response.Results.ToList();
                List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

                ClientBase.ValidateResponse(results, valuesToWrite);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);

                return new OperationResult(what, results[0]);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Refused(what, exception);
            }
            finally
            {
                session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        /// <summary>
        /// Changes the identity of the session to a user name and password.
        /// </summary>
        /// <remarks>
        /// UserName/Password tokens can be used with any password based system including
        /// Windows. The main disadvantage is that the client must trust the server with its
        /// password, which is why the token must be encrypted when sent to the server.
        /// </remarks>
        /// <param name="userName">The user name.</param>
        /// <param name="password">The password.</param>
        /// <param name="preferredLocales">The locales to ask the server for.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<OperationResult> ImpersonateUserNameAsync(
            string userName,
            string password,
            string[] preferredLocales,
            CancellationToken ct = default)
        {
            return ImpersonateAsync(
                $"Impersonating '{userName}'",
                () => new UserIdentity(userName, Encoding.UTF8.GetBytes(password ?? string.Empty)),
                preferredLocales,
                ct);
        }

        /// <summary>
        /// Changes the identity of the session to a certificate.
        /// </summary>
        /// <remarks>
        /// Certificate tokens use a X509 certificate associated with a user. These could
        /// come from a smart card and identify a user account. The token must be signed
        /// when sent to the server.
        /// </remarks>
        /// <param name="certificatePath">The PKCS#12 file holding the certificate and its private key.</param>
        /// <param name="certificatePassword">The password of the file.</param>
        /// <param name="preferredLocales">The locales to ask the server for.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<OperationResult> ImpersonateWithCertificateAsync(
            string certificatePath,
            string certificatePassword,
            string[] preferredLocales,
            CancellationToken ct = default)
        {
            return ImpersonateAsync(
                "Impersonating with a certificate",
                () => {
                    using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
                        certificatePath,
                        certificatePassword,
                        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);

                    return new UserIdentity(new X509IdentityToken { CertificateData = certificate.RawData.ToByteString() });
                },
                preferredLocales,
                ct);
        }

        /// <summary>
        /// Drops the identity of the session.
        /// </summary>
        /// <remarks>
        /// Anonymous tokens mean no user is associated with the session. They are used by
        /// servers which do not require user authentication, and they can also be used to
        /// log out while keeping a session active.
        /// </remarks>
        /// <param name="preferredLocales">The locales to ask the server for.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<OperationResult> ImpersonateAnonymouslyAsync(string[] preferredLocales, CancellationToken ct = default)
        {
            return ImpersonateAsync(
                "Impersonating anonymously",
                () => new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales,
                ct);
        }

        /// <inheritdoc/>
        protected override Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            int index = session.NamespaceUris.GetIndex(LogFileNamespaceUri);

            LogFileNodeId = index < 0
                ? NodeId.Null
                : new NodeId(kLogFilePathIdentifier, (ushort)index);

            UserTokenPolicies = session.ConfiguredEndpoint?.Description?.UserIdentityTokens.ToArray()
                ?? Array.Empty<UserTokenPolicy>();

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override Task OnDetachingAsync()
        {
            LogFileNodeId = NodeId.Null;
            UserTokenPolicies = Array.Empty<UserTokenPolicy>();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Changes the identity of the session, answering a refusal as a status rather than
        /// throwing it.
        /// </summary>
        /// <remarks>
        /// The token is created inside the call rather than passed in, so that a token
        /// which cannot be created - a certificate file which does not open, for instance -
        /// is reported the same way as one the server refuses.
        /// </remarks>
        private async Task<OperationResult> ImpersonateAsync(
            string what,
            Func<IUserIdentity> createIdentity,
            string[] preferredLocales,
            CancellationToken ct)
        {
            ISession session = RequireSession();

            // want to get error text for this call.
            session.ReturnDiagnostics = DiagnosticsMasks.All;

            try
            {
                IUserIdentity identity = createIdentity();

                await session.UpdateSessionAsync(identity, preferredLocales.ToArrayOf(), ct).ConfigureAwait(false);

                return new OperationResult(what, StatusCodes.Good);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Refused(what, exception);
            }
            finally
            {
                session.ReturnDiagnostics = DiagnosticsMasks.None;
            }
        }

        /// <summary>
        /// An operation which did not get through, with the status code it failed with
        /// where the stack gave one.
        /// </summary>
        private OperationResult Refused(string what, Exception exception)
        {
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation(exception, "{What} did not get through.", what);
            }

            return new OperationResult(
                what,
                exception is ServiceResultException refused
                    ? refused.StatusCode
                    : (StatusCode)StatusCodes.Bad);
        }
    }
}
