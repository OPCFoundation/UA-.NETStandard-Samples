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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the user authentication sample does: the same node looks different depending
    /// on who is asking.
    /// </summary>
    /// <remarks>
    /// The node manager answers the user access level per session rather than storing it,
    /// so an anonymous client is told the node is read only while an authenticated one is
    /// told it may write. The write handler enforces the same rule again, which is the
    /// part that matters: a client which ignores the access level still gets refused.
    ///
    /// The successful write is only reachable with a real Windows account, because the
    /// server checks the password with LogonUser. That test is therefore gated on two
    /// environment variables and skips when they are not set, which is what happens on a
    /// build server. The two refusals need no account and run everywhere.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class UserAuthenticationNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "UserAuthentication";

        private const string UserAuthenticationNamespace =
            "http://opcfoundation.org/Quickstarts/UserAuthentication";

        private const string UserVariable = "OPCUA_SAMPLES_TEST_USER";
        private const string PasswordVariable = "OPCUA_SAMPLES_TEST_PASSWORD";

        /// <summary>
        /// The process object and its log file path are where the sample puts them.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task LogFilePathIsExposedUnderTheProcess(CancellationToken ct)
        {
            NodeId process = await ResolveAsync(ct, Name(UserAuthenticationNamespace, "My Process"))
                .ConfigureAwait(false);

            NodeId logFilePath = await ResolveLogFilePathAsync(ct).ConfigureAwait(false);

            ushort ns = NamespaceIndex(UserAuthenticationNamespace);

            Assert.Multiple(() => {
                Assert.That(process, Is.EqualTo(new NodeId(1u, ns)), "The process object moved.");
                Assert.That(logFilePath, Is.EqualTo(new NodeId(2u, ns)), "The log file path moved.");
            });

            DataValue value = await SessionOps
                .ReadValueAsync(Session, logFilePath, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading the log file path failed: {value.StatusCode}");

            await TestContext.Out
                .WriteLineAsync($"The log file path is {value.WrappedValue}")
                .ConfigureAwait(false);
        }

        /// <summary>
        /// An anonymous client is told the node is read only, while the node itself is not.
        /// </summary>
        /// <remarks>
        /// The difference between the two attributes is the whole point: AccessLevel says
        /// what the node can do, UserAccessLevel says what this session may do with it, and
        /// the sample computes the second one per session.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnonymousSessionIsToldTheNodeIsReadOnly(CancellationToken ct)
        {
            NodeId logFilePath = await ResolveLogFilePathAsync(ct).ConfigureAwait(false);

            DataValue accessLevel = await SessionOps
                .ReadAttributeAsync(Session, logFilePath, Attributes.AccessLevel, ct)
                .ConfigureAwait(false);

            DataValue userAccessLevel = await SessionOps
                .ReadAttributeAsync(Session, logFilePath, Attributes.UserAccessLevel, ct)
                .ConfigureAwait(false);

            accessLevel.WrappedValue.TryGetValue(out byte forTheNode);
            userAccessLevel.WrappedValue.TryGetValue(out byte forThisUser);

            await TestContext.Out
                .WriteLineAsync($"AccessLevel {forTheNode}, UserAccessLevel {forThisUser} for an anonymous session")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    forTheNode,
                    Is.EqualTo(AccessLevels.CurrentReadOrWrite),
                    "The node itself can be read and written.");

                Assert.That(
                    forThisUser,
                    Is.EqualTo(AccessLevels.CurrentRead),
                    "An anonymous session may only read it.");
            });
        }

        /// <summary>
        /// An anonymous client which writes anyway is refused, and nothing changes.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnonymousWriteIsDeniedAndLeavesTheValueAlone(CancellationToken ct)
        {
            NodeId logFilePath = await ResolveLogFilePathAsync(ct).ConfigureAwait(false);

            DataValue before = await SessionOps
                .ReadValueAsync(Session, logFilePath, ct)
                .ConfigureAwait(false);

            StatusCode result = await SessionOps
                .WriteValueAsync(Session, logFilePath, Variant.From(@".\NotAllowed.txt"), ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"An anonymous write answered {result}")
                .ConfigureAwait(false);

            Assert.That(
                result,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                "The write handler has to refuse an anonymous session.");

            DataValue after = await SessionOps
                .ReadValueAsync(Session, logFilePath, ct)
                .ConfigureAwait(false);

            Assert.That(
                after.WrappedValue.ToString(),
                Is.EqualTo(before.WrappedValue.ToString()),
                "A write which was refused must not have changed anything.");
        }

        /// <summary>
        /// A user name the server cannot verify does not get a session.
        /// </summary>
        /// <remarks>
        /// The user name is one no machine is going to have, so this needs no account of
        /// its own and runs anywhere.
        ///
        /// What is asserted is that no session comes back, not which status code says so.
        /// The server verifies the password with LogonUser, and how Windows refuses an
        /// account which does not exist is not the sample's behaviour to pin down - it
        /// differs between a developer machine and a build agent. The code is reported so a
        /// failing run still shows it.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public void UnknownUserIsRejected()
        {
            var identity = new UserIdentity("no-such-user-8f2c", Encoding.UTF8.GetBytes("not the password"));

            Exception refused = Assert.CatchAsync(
                async () => {
                    await using TestClient client = await TestClient
                        .ConnectWithIdentityAsync(EndpointUrl, "rejected user", identity)
                        .ConfigureAwait(false);
                },
                "A user the server cannot verify must not get a session.");

            string reported = refused is ServiceResultException serviceResult
                ? serviceResult.StatusCode.ToString()
                : refused.GetType().Name;

            TestContext.Out.WriteLine($"An unknown user was refused with {reported}: {refused.Message}");

            Assert.That(
                refused,
                Is.Not.Null,
                "Opening a session for a user the server cannot verify has to fail.");
        }

        /// <summary>
        /// An authenticated client may write, and the sample writes the log file.
        /// </summary>
        /// <remarks>
        /// The value which is written is the path of the log file, so pointing it into the
        /// working directory of the test run is what keeps the side effect out of the way.
        /// Needs a real local account, because the server checks the password with
        /// LogonUser; skips when the two environment variables which name one are not set.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AuthenticatedUserMayWriteAndTheLogFileAppears(CancellationToken ct)
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

            await using TestClient client = await TestClient
                .ConnectWithIdentityAsync(EndpointUrl, "authenticated user", new UserIdentity(user, Encoding.UTF8.GetBytes(password)), ct)
                .ConfigureAwait(false);

            NodeId logFilePath = await ResolveLogFilePathAsync(ct).ConfigureAwait(false);

            DataValue userAccessLevel = await SessionOps
                .ReadAttributeAsync(client.Session, logFilePath, Attributes.UserAccessLevel, ct)
                .ConfigureAwait(false);

            Assert.That(
                userAccessLevel.WrappedValue.TryGetValue(out byte writable) ? writable : (byte)0,
                Is.EqualTo(AccessLevels.CurrentReadOrWrite),
                "An authenticated session has to be told it may write.");

            string logFile = Path.Combine(TestContext.CurrentContext.WorkDirectory, "user-authentication.log");

            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }

            StatusCode result = await SessionOps
                .WriteValueAsync(client.Session, logFilePath, Variant.From(logFile), ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result),
                Is.True,
                $"An authenticated session has to be allowed to write: {result}");

            Assert.That(
                File.Exists(logFile),
                Is.True,
                "Writing the path has to create the log file it names.");

            string contents = await File.ReadAllTextAsync(logFile, ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"The log file holds: {contents}").ConfigureAwait(false);

            Assert.That(
                contents,
                Is.Not.Empty,
                "The sample writes the identity it is running as into the log file.");
        }

        private Task<NodeId> ResolveLogFilePathAsync(CancellationToken ct)
        {
            return ResolveAsync(
                ct,
                Name(UserAuthenticationNamespace, "My Process"),
                Name(UserAuthenticationNamespace, "LogFilePath"));
        }
    }
}
