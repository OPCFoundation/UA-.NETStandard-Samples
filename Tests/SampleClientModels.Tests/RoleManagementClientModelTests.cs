/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Samples.Client;
using Quickstarts.RoleManagement.Client.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the RoleManagement client exists to show, asked of its model without the
    /// window: the same machine looks different to each account, an Operator may reset it,
    /// an Observer may not, and the RoleSet is only changed by a SecurityAdmin over an
    /// encrypted channel.
    /// </summary>
    /// <remarks>
    /// The session of the fixture is anonymous. A test which needs an account opens a
    /// second managed session for it and attaches a model of its own, the way a second
    /// window would; the password of every sample account is its user name.
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class RoleManagementClientModelTests : ClientModelFixtureBase<RoleManagementClientModel>
    {
        /// <summary>
        /// The default the Reset method of the sample restores.
        /// </summary>
        private const double kDefaultSetPoint = 20.0;

        protected override string SampleName => "RoleManagement";

        protected override RoleManagementClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new RoleManagementClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnonymousSeesTheMachineButNoValues(CancellationToken ct)
        {
            Assert.That(Model.Snapshot.Nodes, Is.Empty, "A detached model already knows nodes.");

            await AttachAsync(ct).ConfigureAwait(false);

            Assert.That(Model.MachineId.IsNull, Is.False, "The model did not resolve the machine of the server.");

            RoleManagementSnapshot snapshot = await Model.RefreshAsync(ct).ConfigureAwait(false);

            Assert.That(snapshot, Is.SameAs(Model.Snapshot));

            string[] names = snapshot.Nodes.Select(node => node.Name).ToArray();

            await TestContext.Out
                .WriteLineAsync("Anonymous sees: " + string.Join(", ", snapshot.Nodes.Select(node => $"{node.Name}={node.Value} ({node.Status})")))
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(names, Does.Contain("Temperature"));
                Assert.That(names, Does.Contain("SetPoint"));
                Assert.That(names, Does.Contain("Reset"));
                Assert.That(names, Does.Not.Contain("Calibration"), "Only an Engineer may browse the calibration.");

                // the method was browsed, so the model knows it - even though the anonymous
                // session may not call it
                Assert.That(Model.ResetId.IsNull, Is.False, "Reset was listed but not picked up from the browse.");
                Assert.That(snapshot.Nodes.Single(node => node.Name == "Reset").IsMethod, Is.True);

                foreach (MachineNodeEntry node in snapshot.Nodes.Where(node => !node.IsMethod))
                {
                    Assert.That(node.Value, Is.Empty, $"An anonymous session read a value of {node.Name}.");
                    Assert.That(node.Status, Does.StartWith("Bad"), $"The read of {node.Name} was not refused.");
                }

                Assert.That(snapshot.Roles.Select(role => role.Name), Does.Contain("Operator"));
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task OperatorWritesTheSetPointAndResetsIt(CancellationToken ct)
        {
            await using SignedIn operator1 = await SignInAsync("operator1", encrypted: false, ct).ConfigureAwait(false);

            MachineNodeEntry setPoint = await SetPointAsync(operator1.Model, ct).ConfigureAwait(false);

            OperationResult written = await operator1.Model.WriteAsync(setPoint, "25", ct).ConfigureAwait(false);

            Assert.That(written.Succeeded, Is.True, $"An Operator may write the set point, but got: {written}");
            Assert.That(written.ToString(), Does.Contain("Good"), "The status line has to say Good.");

            setPoint = await SetPointAsync(operator1.Model, ct).ConfigureAwait(false);

            Assert.That(NumberOf(setPoint.Value), Is.EqualTo(25.0).Within(0.0001), "The write did not arrive.");

            // the regression the tier 2 fixture of this sample exists for: the client has
            // to find its own method and call it
            OperationResult reset = await operator1.Model.ResetAsync(ct).ConfigureAwait(false);

            Assert.That(
                reset.Succeeded,
                Is.True,
                $"Calling Reset as an Operator has to succeed, but got: {reset}. " +
                "BadNotFound means the model could not resolve its own method, " +
                "BadUserAccessDenied means the call was refused.");

            setPoint = await SetPointAsync(operator1.Model, ct).ConfigureAwait(false);

            Assert.That(
                NumberOf(setPoint.Value),
                Is.EqualTo(kDefaultSetPoint).Within(0.0001),
                $"After Reset the set point has to read {kDefaultSetPoint}.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task ObserverIsRefusedReset(CancellationToken ct)
        {
            await using SignedIn observer1 = await SignInAsync("observer1", encrypted: false, ct).ConfigureAwait(false);

            RoleManagementSnapshot snapshot = await observer1.Model.RefreshAsync(ct).ConfigureAwait(false);

            // the Observer reads what the Operator may write
            MachineNodeEntry temperature = snapshot.Nodes.Single(node => node.Name == "Temperature");

            Assert.That(temperature.Status, Is.EqualTo(StatusCodes.Good.ToString()), "An Observer may read the temperature.");

            OperationResult reset = await observer1.Model.ResetAsync(ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Reset as an Observer: {reset}").ConfigureAwait(false);

            Assert.That(
                reset.Status,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                "An Observer is not granted Call on Reset.");
            Assert.That(reset.ToString(), Does.Contain(nameof(StatusCodes.BadUserAccessDenied)));
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task SecurityAdminAddsARoleOnlyOverAnEncryptedChannel(CancellationToken ct)
        {
            const string roleName = "ModelTestRole";

            // the right Role on the wrong channel: Part 18 requires the role configuration
            // Methods to be called over an encrypted channel
            await using (SignedIn unsecured = await SignInAsync("secadmin", encrypted: false, ct).ConfigureAwait(false))
            {
                OperationResult refused = await unsecured.Model.AddRoleAsync(roleName, ct).ConfigureAwait(false);

                await TestContext.Out.WriteLineAsync($"AddRole without encryption: {refused}").ConfigureAwait(false);

                Assert.That(
                    refused.Status,
                    Is.EqualTo((StatusCode)StatusCodes.BadSecurityModeInsufficient),
                    "A SecurityAdmin on an unsecured channel has to be refused.");
            }

            await using SignedIn admin = await SignInAsync("secadmin", encrypted: true, ct).ConfigureAwait(false);

            OperationResult added = await admin.Model.AddRoleAsync(roleName, ct).ConfigureAwait(false);

            Assert.That(added.Succeeded, Is.True, $"AddRole over an encrypted channel failed: {added}");

            RoleManagementSnapshot snapshot = await admin.Model.RefreshAsync(ct).ConfigureAwait(false);

            RoleEntry role = snapshot.Roles.FirstOrDefault(candidate => candidate.Name == roleName);

            try
            {
                Assert.That(
                    role,
                    Is.Not.Null,
                    "The Role added through the model does not appear in its RoleSet. It lists: " +
                    string.Join(", ", snapshot.Roles.Select(candidate => candidate.Name)));

                // a SecurityAdmin also sees the identity rules of a Role, which everybody
                // else is told are not visible
                Assert.That(
                    snapshot.Roles.Single(candidate => candidate.Name == "Operator").Identities,
                    Does.Contain("operator1"),
                    "The Operator Role names its account, and a SecurityAdmin may read that.");
            }
            finally
            {
                // the server is per fixture, but the RoleSet is left as it was found anyway
                if (role != null)
                {
                    await SessionOps
                        .CallAsync(
                            admin.Client.Session,
                            ObjectIds.Server_ServerCapabilities_RoleSet,
                            MethodIds.Server_ServerCapabilities_RoleSet_RemoveRole,
                            ct,
                            Variant.From(role.NodeId))
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// The set point as the model sees it after a refresh.
        /// </summary>
        private static async Task<MachineNodeEntry> SetPointAsync(RoleManagementClientModel model, CancellationToken ct)
        {
            RoleManagementSnapshot snapshot = await model.RefreshAsync(ct).ConfigureAwait(false);

            MachineNodeEntry setPoint = snapshot.Nodes.FirstOrDefault(node => node.Name == "SetPoint");

            Assert.That(
                setPoint,
                Is.Not.Null,
                "The machine has no SetPoint. It has: " + string.Join(", ", snapshot.Nodes.Select(node => node.Name)));

            return setPoint;
        }

        /// <summary>
        /// A value the model rendered, read back as a number.
        /// </summary>
        /// <remarks>
        /// Tried with the current culture first, because that is what the window shows the
        /// user, and then invariant, so the test does not depend on the culture of the
        /// machine it runs on.
        /// </remarks>
        private static double NumberOf(string value)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double current))
            {
                return current;
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant))
            {
                return invariant;
            }

            Assert.Fail($"'{value}' is not a number.");

            return double.NaN;
        }

        /// <summary>
        /// Opens a session for one of the sample accounts and attaches a model to it.
        /// </summary>
        private async Task<SignedIn> SignInAsync(string account, bool encrypted, CancellationToken ct)
        {
            TestClient client = await ConnectAsync(
                RoleManagementClientModel.IdentityFor(account),
                encrypted,
                $"{account}{(encrypted ? " encrypted" : string.Empty)}",
                ct).ConfigureAwait(false);

            // the pair owns both halves from here on, so a failed attach disposes the
            // session together with the model instead of leaking it into the next test
            var signedIn = new SignedIn(client);

            try
            {
                await signedIn.Model.AttachAsync(client.Session, ct).ConfigureAwait(false);
            }
            catch
            {
                await signedIn.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            return signedIn;
        }

        /// <summary>
        /// A session for one account and the model attached to it.
        /// </summary>
        private sealed class SignedIn : IAsyncDisposable
        {
            public SignedIn(TestClient client)
            {
                Client = client;
                Model = new RoleManagementClientModel(NullTelemetry.Instance);
            }

            public TestClient Client { get; }

            public RoleManagementClientModel Model { get; }

            public async ValueTask DisposeAsync()
            {
                // the model first, the way the window does it: it releases what it holds
                // before the session is closed
                await Model.DisposeAsync().ConfigureAwait(false);
                await Client.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
