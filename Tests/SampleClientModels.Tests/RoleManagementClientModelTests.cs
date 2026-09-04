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
using Opc.Ua.Configuration;
using Opc.Ua.Samples.Client;
using Quickstarts.RoleManagement.Client.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the RoleManagement client exists to show, asked of its model without the
    /// window: the same machine looks different to each account and to each channel, an
    /// Operator may reset it, an Observer may not, and the RoleSet is only changed by a
    /// SecurityAdmin over an encrypted channel.
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
        /// The criteria the model offers for each identity criteria type are what a Part 18
        /// 4.4.3 rule accepts: an account, an upper case thumbprint, a normalised subject.
        /// </summary>
        /// <remarks>
        /// The certificate criteria are read off an application instance certificate, so a
        /// client application with one is built the way the sample client builds its own.
        /// No session is involved: the window fills its text box before it connects.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task CriteriaStringsAreWellFormedForEveryType(CancellationToken ct)
        {
            Assert.That(
                RoleManagementClientModel.CriteriaTypes,
                Is.EqualTo(new[] {
                    IdentityCriteriaType.UserName,
                    IdentityCriteriaType.Thumbprint,
                    IdentityCriteriaType.X509Subject,
                }),
                "The model offers the three criteria types the sample server can match.");

            // the normalisation on its own, against the subject the sample client's
            // configuration file spells out - in the order Part 18 4.4.3 requires, not the
            // order the certificate carries the parts in
            Assert.That(
                RoleManagementClientModel.Part18Subject(
                    "CN=Quickstart RoleManagement Client, C=US, S=Arizona, O=OPC Foundation, DC=host"),
                Is.EqualTo("CN=\"Quickstart RoleManagement Client\"/O=\"OPC Foundation\"/DC=\"host\"/S=\"Arizona\"/C=\"US\""));

            using var pki = new TemporaryPki("client-RoleManagement-criteria");

            (ApplicationInstance application, ApplicationConfiguration configuration) =
                await TestClient.CreateApplicationAsync(pki, ct).ConfigureAwait(false);

            await using (application.ConfigureAwait(false))
            {
                SecurityConfiguration security = configuration.SecurityConfiguration;

                string userName = await Model
                    .CriteriaOfAsync(IdentityCriteriaType.UserName, security, ct)
                    .ConfigureAwait(false);

                string thumbprint = await Model
                    .CriteriaOfAsync(IdentityCriteriaType.Thumbprint, security, ct)
                    .ConfigureAwait(false);

                string subject = await Model
                    .CriteriaOfAsync(IdentityCriteriaType.X509Subject, security, ct)
                    .ConfigureAwait(false);

                await TestContext.Out
                    .WriteLineAsync($"UserName='{userName}', Thumbprint='{thumbprint}', X509Subject='{subject}'")
                    .ConfigureAwait(false);

                Assert.Multiple(() => {
                    Assert.That(userName, Is.EqualTo(RoleManagementClientModel.DefaultUserCriteria));

                    Assert.That(
                        thumbprint,
                        Does.Match("^[0-9A-F]{40,}$"),
                        "Part 18 4.4.3 requires a Thumbprint to be upper case hexadecimal with no separators.");

                    Assert.That(
                        subject,
                        Does.StartWith("CN=\"Sample Test Client\"/O=\"OPC Foundation\"/DC=\""),
                        "An X509Subject has to be the normalised Name=\"Value\" form, CN first.");

                    Assert.That(subject, Does.EndWith("/S=\"Arizona\"/C=\"US\""));

                    Assert.That(
                        subject,
                        Does.Match("^([A-Za-z]+=\"[^\"]*\")(/[A-Za-z]+=\"[^\"]*\")*$"),
                        "An X509Subject is a sequence of Name=\"Value\" pairs separated by slashes.");
                });
            }
        }

        /// <summary>
        /// A node which demands an encrypted channel says so - with its status on the wrong
        /// channel and with its AccessRestrictions on the right one.
        /// </summary>
        /// <remarks>
        /// The Operator holds Browse and Read on the maintenance note in both sessions. What
        /// changes is the channel, and the two entries have to make the difference visible:
        /// BadSecurityModeInsufficient means reconnect with security, where
        /// BadUserAccessDenied would mean sign in as somebody else. The restrictions text
        /// stays empty on the unencrypted channel, because reading the attribute is checked
        /// against the restrictions too.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheRestrictedNodesSayWhyTheyRefuse(CancellationToken ct)
        {
            await using (SignedIn unsecured = await SignInAsync("operator1", encrypted: false, ct).ConfigureAwait(false))
            {
                MachineNodeEntry note = await MaintenanceNoteAsync(unsecured.Model, ct).ConfigureAwait(false);

                await TestContext.Out
                    .WriteLineAsync($"Unencrypted: status '{note.Status}', restrictions '{note.Restrictions}'")
                    .ConfigureAwait(false);

                Assert.Multiple(() => {
                    Assert.That(
                        note.Status,
                        Is.EqualTo(((StatusCode)StatusCodes.BadSecurityModeInsufficient).ToString()),
                        "An Operator holds Read on the maintenance note, so the refusal has to " +
                        "name the channel rather than the user.");

                    Assert.That(
                        note.Restrictions,
                        Is.Empty,
                        "A Session on an unencrypted channel may not read the AccessRestrictions either.");
                });
            }

            await using SignedIn encrypted = await SignInAsync("operator1", encrypted: true, ct).ConfigureAwait(false);

            MachineNodeEntry readable = await MaintenanceNoteAsync(encrypted.Model, ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Encrypted: status '{readable.Status}', restrictions '{readable.Restrictions}'")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    readable.Status,
                    Is.EqualTo(((StatusCode)StatusCodes.Good).ToString()),
                    "The same account reads the note over an encrypted channel.");

                Assert.That(
                    readable.Restrictions,
                    Does.Contain(nameof(AccessRestrictionType.EncryptionRequired)),
                    "A Session which may touch the node at all has to be able to read the " +
                    "AccessRestrictions which apply to it.");
            });
        }

        /// <summary>
        /// A SecurityAdmin flips the CustomConfiguration flag of a Role, and the role list
        /// reflects it; the same account on the unsecured channel is refused.
        /// </summary>
        /// <remarks>
        /// The ConfigureAdmin Role is the one the sample restricts to its encrypted
        /// endpoints, so its entry also shows what the Endpoints filter of a Role looks
        /// like next to the "any" of the well known Roles.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SecurityAdminFlipsCustomConfigurationOverAnEncryptedChannel(CancellationToken ct)
        {
            const string roleName = "ConfigureAdmin";

            await using (SignedIn unsecured = await SignInAsync("secadmin", encrypted: false, ct).ConfigureAwait(false))
            {
                RoleManagementSnapshot seen = await unsecured.Model.RefreshAsync(ct).ConfigureAwait(false);

                RoleEntry role = seen.Roles.Single(candidate => candidate.Name == roleName);

                OperationResult refused = await unsecured.Model
                    .SetCustomConfigurationAsync(role, true, ct)
                    .ConfigureAwait(false);

                await TestContext.Out
                    .WriteLineAsync($"CustomConfiguration without encryption: {refused}")
                    .ConfigureAwait(false);

                Assert.That(
                    refused.Status,
                    Is.EqualTo((StatusCode)StatusCodes.BadSecurityModeInsufficient),
                    "Part 18 4.4 reserves the role configuration for an encrypted channel, and " +
                    "the refusal has to name the channel rather than the user.");
            }

            await using SignedIn admin = await SignInAsync("secadmin", encrypted: true, ct).ConfigureAwait(false);

            RoleManagementSnapshot before = await admin.Model.RefreshAsync(ct).ConfigureAwait(false);

            RoleEntry configureAdmin = before.Roles.Single(candidate => candidate.Name == roleName);
            RoleEntry operatorRole = before.Roles.Single(candidate => candidate.Name == "Operator");

            await TestContext.Out
                .WriteLineAsync(
                    $"{roleName}: endpoints '{configureAdmin.Endpoints}', custom {configureAdmin.CustomConfiguration}, " +
                    $"identities '{configureAdmin.Identities}'; Operator: endpoints '{operatorRole.Endpoints}'")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(configureAdmin.CustomConfiguration, Is.False, "The sample does not ship the flag set.");

                Assert.That(
                    configureAdmin.Endpoints,
                    Is.Not.EqualTo("any").And.Not.EqualTo("(not visible)").And.Not.Empty,
                    "The sample restricts ConfigureAdmin to its encrypted endpoints, and a " +
                    "SecurityAdmin may read that filter.");

                Assert.That(operatorRole.Endpoints, Is.EqualTo("any"), "The well known Roles are granted on every endpoint.");

                Assert.That(
                    configureAdmin.Identities,
                    Does.Contain(nameof(IdentityCriteriaType.X509Subject)),
                    "The server maps the workstation certificate onto ConfigureAdmin.");
            });

            OperationResult set = await admin.Model
                .SetCustomConfigurationAsync(configureAdmin, true, ct)
                .ConfigureAwait(false);

            try
            {
                Assert.That(set.Succeeded, Is.True, $"Setting the flag over an encrypted channel failed: {set}");
                Assert.That(set.ToString(), Does.Contain($"CustomConfiguration of {roleName} := True"));

                RoleManagementSnapshot after = await admin.Model.RefreshAsync(ct).ConfigureAwait(false);

                Assert.That(
                    after.Roles.Single(candidate => candidate.Name == roleName).CustomConfiguration,
                    Is.True,
                    "The role list has to reflect the flag after a refresh.");
            }
            finally
            {
                // the server is per fixture, so the Role is left as it was found
                OperationResult cleared = await admin.Model
                    .SetCustomConfigurationAsync(configureAdmin, false, ct)
                    .ConfigureAwait(false);

                Assert.That(cleared.Succeeded, Is.True, $"Clearing the flag again failed: {cleared}");
            }

            RoleManagementSnapshot restored = await admin.Model.RefreshAsync(ct).ConfigureAwait(false);

            Assert.That(
                restored.Roles.Single(candidate => candidate.Name == roleName).CustomConfiguration,
                Is.False,
                "Clearing the flag has to show in the role list again.");
        }

        /// <summary>
        /// The maintenance note as the model sees it after a refresh.
        /// </summary>
        private static async Task<MachineNodeEntry> MaintenanceNoteAsync(RoleManagementClientModel model, CancellationToken ct)
        {
            RoleManagementSnapshot snapshot = await model.RefreshAsync(ct).ConfigureAwait(false);

            MachineNodeEntry note = snapshot.Nodes.FirstOrDefault(node => node.Name == "MaintenanceNote");

            Assert.That(
                note,
                Is.Not.Null,
                "The machine has no MaintenanceNote. It has: " + string.Join(", ", snapshot.Nodes.Select(node => node.Name)));

            return note;
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
