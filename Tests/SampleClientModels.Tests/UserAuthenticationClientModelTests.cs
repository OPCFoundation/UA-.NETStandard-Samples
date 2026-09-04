/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Samples.Client;
using Quickstarts.UserAuthenticationClient.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the UserAuthentication client exists to show, asked of its model without the
    /// window: the same node looks different depending on who is asking. An anonymous
    /// session reads the log file path and is refused the write, an account the server
    /// cannot verify is refused and leaves the session as it was, and a real account may
    /// write.
    /// </summary>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class UserAuthenticationClientModelTests : ClientModelFixtureBase<UserAuthenticationClientModel>
    {
        private const string UserVariable = "OPCUA_SAMPLES_TEST_USER";
        private const string PasswordVariable = "OPCUA_SAMPLES_TEST_PASSWORD";

        private static readonly string[] s_locales = { "de", "es", "en" };

        protected override string SampleName => "UserAuthentication";

        protected override UserAuthenticationClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new UserAuthenticationClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachResolvesTheLogFilePathAndTheTokenPolicies(CancellationToken ct)
        {
            Assert.That(Model.LogFileNodeId.IsNull, Is.True, "A detached model already knows the node.");
            Assert.That(Model.UserTokenPolicies, Is.Empty);

            await AttachAsync(ct).ConfigureAwait(false);

            // the sample server accepts anonymous sessions and user name tokens, which is
            // what the two tabs the window enables are for
            UserTokenType[] policies = Model.UserTokenPolicies.Select(policy => policy.TokenType).ToArray();

            await TestContext.Out
                .WriteLineAsync("The endpoint accepts: " + string.Join(", ", policies))
                .ConfigureAwait(false);

            Assert.That(policies, Does.Contain(UserTokenType.Anonymous));
            Assert.That(policies, Does.Contain(UserTokenType.UserName));

            // the node the model addresses is the one a browse finds
            NodeId browsed = await PathAsync(ct, "My Process", "LogFilePath").ConfigureAwait(false);

            Assert.That(Model.LogFileNodeId, Is.EqualTo(browsed), "The model addresses another node than the browse finds.");
            Assert.That(
                Model.LogFileNodeId.NamespaceIndex,
                Is.EqualTo(NamespaceIndex(UserAuthenticationClientModel.LogFileNamespaceUri)));

            string path = await Model.ReadLogFilePathAsync(ct).ConfigureAwait(false);

            DataValue plain = await SessionOps.ReadValueAsync(Session, browsed, ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"The log file path is '{path}'").ConfigureAwait(false);

            Assert.That(path, Is.Not.Empty, "The sample server has to publish a log file path.");
            Assert.That(path, Is.EqualTo(plain.GetValue<string>(string.Empty)), "The model does not read what a plain read reads.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnonymousWriteIsRefused(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            string before = await Model.ReadLogFilePathAsync(ct).ConfigureAwait(false);

            OperationResult written = await Model.WriteLogFilePathAsync(@".\NotAllowed.txt", ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"The write reported: {written}").ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    written.Status,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                    "The write handler has to refuse an anonymous session.");

                // the line the window puts on its status bar, which tier 2 reads
                Assert.That(written.ToString(), Does.Contain(nameof(StatusCodes.BadUserAccessDenied)));
            });

            Assert.That(
                await Model.ReadLogFilePathAsync(ct).ConfigureAwait(false),
                Is.EqualTo(before),
                "The refused write changed the value.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task UnknownUserIsRefusedAndTheSessionStaysAnonymous(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            OperationResult refused = await Model
                .ImpersonateUserNameAsync("no-such-user-8f2c", "not the password", s_locales, ct)
                .ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Impersonating reported: {refused}").ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(refused.Succeeded, Is.False, "An account the server cannot verify must not be reported as a success.");
                Assert.That(refused.ToString(), Does.Not.Contain("Good"));
                Assert.That(
                    Session.Identity?.TokenType,
                    Is.EqualTo(UserTokenType.Anonymous),
                    "A refused UpdateSession has to leave the session with the identity it had.");
            });

            // the model is still usable afterwards: the diagnostics mask was restored and
            // the session still answers
            Assert.That(await Model.ReadLogFilePathAsync(ct).ConfigureAwait(false), Is.Not.Empty);
            Assert.That(Session.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.None));
        }

        /// <summary>
        /// The one path which needs a password the server accepts.
        /// </summary>
        /// <remarks>
        /// The sample server verifies the password with LogonUser, so this only runs when
        /// the two environment variables the tier 1.5 fixture uses name a Windows account.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AuthenticatedUserMayWriteTheLogFilePath(CancellationToken ct)
        {
            string user = Environment.GetEnvironmentVariable(UserVariable);
            string password = Environment.GetEnvironmentVariable(PasswordVariable);

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
            {
                Assert.Ignore(
                    $"Set {UserVariable} and {PasswordVariable} to a local Windows account to run this. " +
                    "The server verifies the password with LogonUser, so there is no way to " +
                    "authenticate without one.");
            }

            await AttachAsync(ct).ConfigureAwait(false);

            OperationResult impersonated = await Model
                .ImpersonateUserNameAsync(user, password, s_locales, ct)
                .ConfigureAwait(false);

            Assert.That(impersonated.Succeeded, Is.True, $"Impersonating {user} failed: {impersonated}");
            Assert.That(Session.Identity?.TokenType, Is.EqualTo(UserTokenType.UserName));

            string logFile = Path.Combine(TestContext.CurrentContext.WorkDirectory, "user-authentication-model.log");

            OperationResult written = await Model.WriteLogFilePathAsync(logFile, ct).ConfigureAwait(false);

            Assert.That(written.Succeeded, Is.True, $"An authenticated session has to be allowed to write: {written}");

            Assert.That(
                await Model.ReadLogFilePathAsync(ct).ConfigureAwait(false),
                Is.EqualTo(logFile),
                "The written path does not read back.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task DetachForgetsTheNodeAndThePolicies(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);
            await Model.DetachAsync().ConfigureAwait(false);

            Assert.That(Model.LogFileNodeId.IsNull, Is.True, "A detached model still knows the node.");
            Assert.That(Model.UserTokenPolicies, Is.Empty);

            // reading before an attach is a programming error of the window, not a refusal
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await Model.ReadLogFilePathAsync(ct).ConfigureAwait(false));

            // and the session it was on is still usable by whoever holds it
            await AttachAsync(ct).ConfigureAwait(false);

            Assert.That(await Model.ReadLogFilePathAsync(ct).ConfigureAwait(false), Is.Not.Empty);
        }
    }
}
