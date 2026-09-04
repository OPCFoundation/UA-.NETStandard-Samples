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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the role management sample does: the same address space looks and behaves
    /// differently depending on which OPC UA Part 18 Roles the Session was granted.
    /// </summary>
    /// <remarks>
    /// Nothing here calls into the node manager. Every assertion is what an ordinary client
    /// observes over a session opened for one of the sample accounts, which is the only thing
    /// the sample promises: an Observer may read but not write, an Operator may write the set
    /// point and call the method, an Engineer sees a node an Observer cannot even browse, and
    /// a SecurityAdmin can change all of that at runtime over an encrypted channel.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class RoleManagementNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "RoleManagement";

        /// <summary>
        /// The namespace the sample serves its nodes in.
        /// </summary>
        /// <remarks>
        /// Spelled out rather than imported, because Opc.Ua defines a Namespaces class too
        /// and the tests live in a namespace below Opc.Ua.
        /// </remarks>
        private const string RoleManagementNamespace =
            Quickstarts.RoleManagement.Namespaces.RoleManagement;

        private QualifiedName Machine => Name(RoleManagementNamespace, "Machine");
        private QualifiedName Temperature => Name(RoleManagementNamespace, "Temperature");
        private QualifiedName SetPoint => Name(RoleManagementNamespace, "SetPoint");
        private QualifiedName Calibration => Name(RoleManagementNamespace, "Calibration");
        private QualifiedName MaintenanceNote => Name(RoleManagementNamespace, "MaintenanceNote");
        private QualifiedName ServiceCode => Name(RoleManagementNamespace, "ServiceCode");
        private QualifiedName Reset => Name(RoleManagementNamespace, "Reset");

        /// <summary>
        /// The subject name the maintenance workstation of the sample creates its application
        /// instance certificate with, as the configuration file of the sample client spells
        /// it. The stack replaces DC=localhost with the host name.
        /// </summary>
        private const string WorkstationSubject =
            "CN=Quickstart RoleManagement Client, C=US, S=Arizona, O=OPC Foundation, DC=localhost";

        /// <summary>
        /// The Role the sample grants for the certificate of the maintenance workstation.
        /// </summary>
        private static NodeId WorkstationRole => ObjectIds.WellKnownRole_ConfigureAdmin;

        /// <summary>
        /// The identity mapping rule the server adds for the workstation certificate at
        /// startup, which two of the fixtures below take away and put back.
        /// </summary>
        private static IdentityMappingRuleType WorkstationRule => new IdentityMappingRuleType {
            CriteriaType = IdentityCriteriaType.X509Subject,
            Criteria = Quickstarts.RoleManagement.Server.RoleManagementServer.WorkstationCertificateSubject,
        };

        #region Tests
        /// <summary>
        /// An anonymous Session finds the machine, but is refused every value.
        /// </summary>
        /// <remarks>
        /// The Anonymous Role is granted Browse and nothing else, so the address space is
        /// there but empty of data. The two nodes which do not name Anonymous at all are not
        /// part of the address space this Session sees: Browse is a permission like any other.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnonymousSessionSeesTheMachineButNoValues(CancellationToken ct)
        {
            NodeId machineId = await ResolveAsync(ct, Machine).ConfigureAwait(false);

            IReadOnlyList<string> children = await SessionOps
                .BrowseNamesAsync(Session, machineId, ct)
                .ConfigureAwait(false);

            await ReportAsync("An anonymous session browses", children).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(children, Does.Contain("Temperature"), "Anonymous is granted Browse on the measurement.");
                Assert.That(children, Does.Contain("SetPoint"), "Anonymous is granted Browse on the set point.");
                Assert.That(children, Does.Contain("Reset"), "Anonymous is granted Browse on the method.");
                Assert.That(children, Does.Not.Contain("Calibration"), "Anonymous is not granted Browse on the calibration.");
                Assert.That(children, Does.Not.Contain("MaintenanceNote"), "Anonymous is not granted Browse on the maintenance note.");
            });

            NodeId temperatureId = await ResolveAsync(ct, Machine, Temperature).ConfigureAwait(false);

            DataValue value = await SessionOps
                .ReadValueAsync(Session, temperatureId, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"An anonymous read of the temperature answered {value.StatusCode}")
                .ConfigureAwait(false);

            Assert.That(
                value.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                "The Anonymous Role carries Browse but not Read on the temperature.");
        }

        /// <summary>
        /// An Observer reads the machine, but may neither write nor call.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ObserverMayReadButNeitherWriteNorCall(CancellationToken ct)
        {
            await using TestClient observer = await ConnectAsAsync("observer1", ct).ConfigureAwait(false);

            NodeId machineId = await ResolveAsync(ct, Machine).ConfigureAwait(false);
            NodeId setPointId = await ResolveAsync(ct, Machine, SetPoint).ConfigureAwait(false);
            NodeId resetId = await ResolveAsync(ct, Machine, Reset).ConfigureAwait(false);

            DataValue read = await SessionOps
                .ReadValueAsync(observer.Session, setPointId, ct)
                .ConfigureAwait(false);

            StatusCode write = await SessionOps
                .WriteValueAsync(observer.Session, setPointId, Variant.From(42.0), ct)
                .ConfigureAwait(false);

            CallMethodResult call = await SessionOps
                .CallAsync(observer.Session, machineId, resetId, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"An Observer read {read.StatusCode}, wrote {write}, called Reset -> {call.StatusCode}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    StatusCode.IsGood(read.StatusCode),
                    Is.True,
                    $"The Observer Role carries Read on the set point: {read.StatusCode}");

                Assert.That(
                    write,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                    "The Observer Role does not carry Write on the set point.");

                Assert.That(
                    call.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                    "The Observer Role does not carry Call on the method.");
            });
        }

        /// <summary>
        /// An Operator writes the set point, and the method puts it back.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task OperatorMayWriteTheSetPointAndCallReset(CancellationToken ct)
        {
            await using TestClient client = await ConnectAsAsync("operator1", ct).ConfigureAwait(false);

            NodeId machineId = await ResolveAsync(ct, Machine).ConfigureAwait(false);
            NodeId setPointId = await ResolveAsync(ct, Machine, SetPoint).ConfigureAwait(false);
            NodeId resetId = await ResolveAsync(ct, Machine, Reset).ConfigureAwait(false);

            StatusCode write = await SessionOps
                .WriteValueAsync(client.Session, setPointId, Variant.From(42.0), ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(write),
                Is.True,
                $"The Operator Role carries Write on the set point: {write}");

            Assert.That(
                await ReadDoubleAsync(client, setPointId, ct).ConfigureAwait(false),
                Is.EqualTo(42.0),
                "The write has to take effect.");

            CallMethodResult call = await SessionOps
                .CallAsync(client.Session, machineId, resetId, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(call.StatusCode),
                Is.True,
                $"The Operator Role carries Call on the method: {call.StatusCode}");

            Assert.That(
                await ReadDoubleAsync(client, setPointId, ct).ConfigureAwait(false),
                Is.EqualTo(20.0),
                "Reset has to restore the default set point.");
        }

        /// <summary>
        /// An Engineer browses a node which is not part of the address space of an Observer.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task EngineerSeesTheCalibrationAnObserverDoesNot(CancellationToken ct)
        {
            NodeId machineId = await ResolveAsync(ct, Machine).ConfigureAwait(false);

            await using TestClient engineer = await ConnectAsAsync("engineer1", ct).ConfigureAwait(false);
            await using TestClient observer = await ConnectAsAsync("observer1", ct).ConfigureAwait(false);

            IReadOnlyList<string> byEngineer = await SessionOps
                .BrowseNamesAsync(engineer.Session, machineId, ct)
                .ConfigureAwait(false);

            IReadOnlyList<string> byObserver = await SessionOps
                .BrowseNamesAsync(observer.Session, machineId, ct)
                .ConfigureAwait(false);

            await ReportAsync("An Engineer browses", byEngineer).ConfigureAwait(false);
            await ReportAsync("An Observer browses", byObserver).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(byEngineer, Does.Contain("Calibration"), "The Engineer Role carries Browse on the calibration.");
                Assert.That(byObserver, Does.Not.Contain("Calibration"), "The Observer Role does not.");
            });

            // resolved through the Engineer's own session: the node is not in the address
            // space the anonymous session of the fixture sees
            NodeId calibrationId = await SessionOps
                .ResolveFromAsync(engineer.Session, machineId, ct, Calibration)
                .ConfigureAwait(false);

            Assert.That(calibrationId.IsNull, Is.False, "The Engineer has to be able to resolve the calibration.");

            // What the Engineer may then do with the value is a separate question, because
            // the calibration also carries an AccessRestrictions attribute which the two
            // sessions here cannot satisfy: both of them are on the unsecured endpoint.
            // AnEncryptedChannelIsRequiredWhateverTheRole is where the value is read.
        }

        /// <summary>
        /// The UserRolePermissions attribute tells a Session what it may do before it tries.
        /// </summary>
        /// <remarks>
        /// RolePermissions is the whole table and reads the same for everyone;
        /// UserRolePermissions is the part of it the reading Session earns. The two together
        /// are how a client greys out a control instead of letting the user run into
        /// BadUserAccessDenied.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task UserRolePermissionsReportWhatTheSessionMayDo(CancellationToken ct)
        {
            NodeId setPointId = await ResolveAsync(ct, Machine, SetPoint).ConfigureAwait(false);

            await using TestClient client = await ConnectAsAsync("operator1", ct).ConfigureAwait(false);

            RolePermissionType[] all = await ReadPermissionsAsync(
                client, setPointId, Attributes.RolePermissions, ct).ConfigureAwait(false);

            RolePermissionType[] mine = await ReadPermissionsAsync(
                client, setPointId, Attributes.UserRolePermissions, ct).ConfigureAwait(false);

            await ReportAsync(
                "The set point grants",
                all.Select(entry => $"{entry.RoleId} -> {(PermissionType)entry.Permissions}"))
                .ConfigureAwait(false);

            await ReportAsync(
                "An Operator earns",
                mine.Select(entry => $"{entry.RoleId} -> {(PermissionType)entry.Permissions}"))
                .ConfigureAwait(false);

            Assert.That(all, Has.Length.GreaterThan(mine.Length), "Not every Role of the table is granted to one user.");

            // Part 18 4.3 gives the Anonymous Role both the Anonymous and the
            // AuthenticatedUser criteria, so every Session holds it as a baseline, and a
            // signed in one holds AuthenticatedUser on top of it
            Assert.That(
                mine.Select(entry => entry.RoleId),
                Is.EquivalentTo(new[] {
                    ObjectIds.WellKnownRole_Anonymous,
                    ObjectIds.WellKnownRole_AuthenticatedUser,
                    ObjectIds.WellKnownRole_Operator,
                }),
                "An Operator earns the three entries of the table its Roles name.");

            RolePermissionType asOperator = mine
                .Single(entry => entry.RoleId == ObjectIds.WellKnownRole_Operator);

            Assert.That(
                (PermissionType)asOperator.Permissions,
                Is.EqualTo(
                    PermissionType.Browse |
                    PermissionType.ReadRolePermissions |
                    PermissionType.Read |
                    PermissionType.Write),
                "The Operator entry of the set point carries Read and Write.");
        }

        /// <summary>
        /// A Session which is not a SecurityAdmin cannot change the role configuration.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task OnlyASecurityAdminMayChangeTheRoleConfiguration(CancellationToken ct)
        {
            var rule = new IdentityMappingRuleType {
                CriteriaType = IdentityCriteriaType.UserName,
                Criteria = "guest",
            };

            // the standard address space protects the Role nodes themselves, so only a
            // SecurityAdmin can browse to the Method. The two Sessions below are refused, so
            // the node id is resolved once through an admin Session and then reused.
            await using TestClient admin = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "admin resolves the method", UserOf("secadmin"), ct)
                .ConfigureAwait(false);

            NodeId addIdentityId = await SessionOps
                .ResolveFromAsync(admin.Session, ObjectIds.WellKnownRole_Operator, ct, new QualifiedName("AddIdentity"))
                .ConfigureAwait(false);

            Assert.That(addIdentityId.IsNull, Is.False, "The Operator Role has to have an AddIdentity method.");

            // an Engineer over an encrypted channel: authenticated, but the wrong Role
            await using TestClient engineer = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "engineer adds an identity", UserOf("engineer1"), ct)
                .ConfigureAwait(false);

            CallMethodResult byEngineer = await CallAsync(engineer, ObjectIds.WellKnownRole_Operator, addIdentityId, rule, ct).ConfigureAwait(false);

            // a SecurityAdmin, but on the unsecured endpoint: the right Role, the wrong channel
            await using TestClient unsecured = await TestClient
                .ConnectWithIdentityAsync(EndpointUrl, "admin without encryption", UserOf("secadmin"), ct)
                .ConfigureAwait(false);

            CallMethodResult unencrypted = await CallAsync(unsecured, ObjectIds.WellKnownRole_Operator, addIdentityId, rule, ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"AddIdentity as an Engineer -> {byEngineer.StatusCode}, " +
                    $"as a SecurityAdmin without encryption -> {unencrypted.StatusCode}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    byEngineer.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                    "Part 18 4.4 reserves the role configuration for the SecurityAdmin Role.");

                Assert.That(
                    unencrypted.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadSecurityModeInsufficient),
                    "Part 18 4.4 requires an encrypted channel for the role configuration.");
            });
        }

        /// <summary>
        /// A SecurityAdmin grants a Role at runtime, and an open Session picks it up.
        /// </summary>
        /// <remarks>
        /// This is the Part 18 4.4.1 requirement that a change to the configuration of a Role
        /// is applied to the Sessions which are already active. The guest session is opened
        /// before the identity rule exists and is never reconnected: the write which is
        /// refused at the start succeeds after the rule is added, and is refused again once
        /// it is removed.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task GrantingARoleReachesAnAlreadyOpenSession(CancellationToken ct)
        {
            NodeId setPointId = await ResolveAsync(ct, Machine, SetPoint).ConfigureAwait(false);

            var rule = new IdentityMappingRuleType {
                CriteriaType = IdentityCriteriaType.UserName,
                Criteria = "guest",
            };

            await using TestClient guest = await ConnectAsAsync("guest", ct).ConfigureAwait(false);

            StatusCode before = await SessionOps
                .WriteValueAsync(guest.Session, setPointId, Variant.From(33.0), ct)
                .ConfigureAwait(false);

            Assert.That(
                before,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                "An account with no Role beyond AuthenticatedUser may not write the set point.");

            await using TestClient admin = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "security admin", UserOf("secadmin"), ct)
                .ConfigureAwait(false);

            CallMethodResult added = await AddIdentityAsync(admin, ObjectIds.WellKnownRole_Operator, rule, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(added.StatusCode),
                Is.True,
                $"A SecurityAdmin on an encrypted channel may add an identity: {added.StatusCode}");

            try
            {
                StatusCode granted = await SessionOps
                    .WriteValueAsync(guest.Session, setPointId, Variant.From(33.0), ct)
                    .ConfigureAwait(false);

                await TestContext.Out
                    .WriteLineAsync($"The write of the open guest session answered {granted} after the grant")
                    .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(granted),
                    Is.True,
                    "Part 18 4.4.1: the Roles of an active Session are re-evaluated when the " +
                    $"configuration of a Role changes. The write still answered {granted}.");
            }
            finally
            {
                CallMethodResult removed = await RemoveIdentityAsync(
                    admin, ObjectIds.WellKnownRole_Operator, rule, ct).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(removed.StatusCode),
                    Is.True,
                    $"Removing the identity rule again failed: {removed.StatusCode}");
            }

            StatusCode after = await SessionOps
                .WriteValueAsync(guest.Session, setPointId, Variant.From(34.0), ct)
                .ConfigureAwait(false);

            Assert.That(
                after,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                "Removing the rule has to take the Role away from the open Session again.");
        }

        /// <summary>
        /// A SecurityAdmin adds a Role of its own, and it turns up in the RoleSet.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SecurityAdminMayAddARoleToTheRoleSet(CancellationToken ct)
        {
            await using TestClient admin = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "security admin adds a role", UserOf("secadmin"), ct)
                .ConfigureAwait(false);

            IReadOnlyList<string> before = await SessionOps
                .BrowseNamesAsync(admin.Session, ObjectIds.Server_ServerCapabilities_RoleSet, ct)
                .ConfigureAwait(false);

            Assert.That(before, Does.Contain("Observer"), "The RoleSet has to carry the well known Roles.");
            Assert.That(before, Does.Not.Contain("Maintenance"), "The sample must not ship the Role it creates here.");

            CallMethodResult added = await SessionOps
                .CallAsync(
                    admin.Session,
                    ObjectIds.Server_ServerCapabilities_RoleSet,
                    MethodIds.Server_ServerCapabilities_RoleSet_AddRole,
                    ct,
                    Variant.From("Maintenance"),

                    // no namespace uri: the server puts the new Role in the namespace it
                    // allocates dynamic Roles in, rather than into the namespace of the
                    // sample model, where it would take the node id of the machine
                    // (UA-.NETStandard#4361)
                    Variant.From(string.Empty))
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(added.StatusCode),
                Is.True,
                $"AddRole failed: {added.StatusCode}");

            IReadOnlyList<string> after = await SessionOps
                .BrowseNamesAsync(admin.Session, ObjectIds.Server_ServerCapabilities_RoleSet, ct)
                .ConfigureAwait(false);

            await ReportAsync("The RoleSet now carries", after).ConfigureAwait(false);

            Assert.That(after, Does.Contain("Maintenance"), "A Role added through AddRole has to appear in the RoleSet.");

            added.OutputArguments.ToArray()[0].TryGetValue(out NodeId roleId);

            CallMethodResult removed = await SessionOps
                .CallAsync(
                    admin.Session,
                    ObjectIds.Server_ServerCapabilities_RoleSet,
                    MethodIds.Server_ServerCapabilities_RoleSet_RemoveRole,
                    ct,
                    Variant.From(roleId))
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(removed.StatusCode),
                Is.True,
                $"Removing the Role again failed: {removed.StatusCode}");
        }

        /// <summary>
        /// AccessRestrictions refuse an unencrypted channel whatever Roles the Session holds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Part 3 5.2.11 AccessRestrictions is the other half of the access story, and the
        /// master node manager checks it after the role permissions. It says nothing about
        /// who the Session is: the Engineer below holds Browse, Read and Write on the
        /// calibration in both halves of this test, and is refused on the first one purely
        /// because the channel is not encrypted.
        /// </para>
        /// <para>
        /// The two nodes differ in one bit. The calibration carries EncryptionRequired only,
        /// so a Browse still finds it and the value is refused. The maintenance note carries
        /// ApplyRestrictionsToBrowse on top, so browsing the node itself is refused too.
        /// </para>
        /// <para>
        /// What ApplyRestrictionsToBrowse does <b>not</b> do in 2.0.0-preview.4 is take the
        /// node out of the reference list of its parent: the per reference filter of a Browse
        /// applies role permissions only, so the maintenance note is still listed below the
        /// machine on an unencrypted channel and only the browse of the node itself is
        /// refused. The assertion below is written for what the stack does today.
        /// </para>
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnEncryptedChannelIsRequiredWhateverTheRole(CancellationToken ct)
        {
            NodeId machineId = await ResolveAsync(ct, Machine).ConfigureAwait(false);

            await using TestClient plain = await TestClient
                .ConnectWithIdentityAsync(EndpointUrl, "engineer without encryption", UserOf("engineer1"), ct)
                .ConfigureAwait(false);

            IReadOnlyList<string> unencrypted = await SessionOps
                .BrowseNamesAsync(plain.Session, machineId, ct)
                .ConfigureAwait(false);

            await ReportAsync("An Engineer on the unsecured endpoint browses", unencrypted)
                .ConfigureAwait(false);

            Assert.That(
                unencrypted,
                Does.Contain("Calibration"),
                "EncryptionRequired on its own does not apply to Browse.");

            NodeId maintenanceNoteId = await SessionOps
                .ResolveFromAsync(plain.Session, machineId, ct, MaintenanceNote)
                .ConfigureAwait(false);

            StatusCode browsedUnencrypted = await BrowseStatusAsync(plain, maintenanceNoteId, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Unencrypted, browsing the maintenance note answered {browsedUnencrypted}")
                .ConfigureAwait(false);

            Assert.That(
                browsedUnencrypted,
                Is.EqualTo((StatusCode)StatusCodes.BadSecurityModeInsufficient),
                "ApplyRestrictionsToBrowse extends the restriction to the Browse service, for a " +
                "Role which holds Browse on the node exactly as for one which does not.");

            NodeId calibrationId = await SessionOps
                .ResolveFromAsync(plain.Session, machineId, ct, Calibration)
                .ConfigureAwait(false);

            DataValue refusedRead = await SessionOps
                .ReadValueAsync(plain.Session, calibrationId, ct)
                .ConfigureAwait(false);

            StatusCode refusedWrite = await SessionOps
                .WriteValueAsync(plain.Session, calibrationId, Variant.From(0.5), ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"Unencrypted, an Engineer read the calibration {refusedRead.StatusCode} " +
                    $"and wrote it {refusedWrite}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    refusedRead.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadSecurityModeInsufficient),
                    "The Engineer holds Read on the calibration; the channel is what is wrong, " +
                    "and BadUserAccessDenied would send a client looking for the wrong fix.");

                Assert.That(
                    refusedWrite,
                    Is.EqualTo((StatusCode)StatusCodes.BadSecurityModeInsufficient),
                    "The same restriction covers the write.");
            });

            // the same account on the encrypted endpoint: nothing about the Roles changed
            await using TestClient secure = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "engineer with encryption", UserOf("engineer1"), ct)
                .ConfigureAwait(false);

            IReadOnlyList<string> encrypted = await SessionOps
                .BrowseNamesAsync(secure.Session, machineId, ct)
                .ConfigureAwait(false);

            await ReportAsync("An Engineer on the encrypted endpoint browses", encrypted)
                .ConfigureAwait(false);

            Assert.That(
                encrypted,
                Does.Contain("MaintenanceNote"),
                "The maintenance note is part of the address space of an encrypted channel.");

            NodeId secureNoteId = await SessionOps
                .ResolveFromAsync(secure.Session, machineId, ct, MaintenanceNote)
                .ConfigureAwait(false);

            Assert.That(
                await BrowseStatusAsync(secure, secureNoteId, ct).ConfigureAwait(false),
                Is.EqualTo((StatusCode)StatusCodes.Good),
                "Over an encrypted channel the same node browses normally.");

            NodeId secureCalibrationId = await SessionOps
                .ResolveFromAsync(secure.Session, machineId, ct, Calibration)
                .ConfigureAwait(false);

            StatusCode write = await SessionOps
                .WriteValueAsync(secure.Session, secureCalibrationId, Variant.From(0.5), ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(write),
                Is.True,
                $"The Engineer Role carries Write on the calibration: {write}");
        }

        /// <summary>
        /// A Role earned by the certificate of the client application, on one endpoint only.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two Part 18 features at once, because the sample configures them on the same Role
        /// and each one is the negative case of the other. The X509Subject identity criteria
        /// of 4.4.3 matches the subject of the application instance certificate the client
        /// sent in CreateSession - so the Role belongs to the software on that workstation
        /// and an anonymous Session from it holds the Role. The Endpoints filter of 4.4.1 is
        /// evaluated before any identity rule is, so the same certificate on the unsecured
        /// endpoint earns nothing.
        /// </para>
        /// <para>
        /// Note which certificate this is. The criteria is named after X.509 and Part 18
        /// allows reading it as a user certificate, but the stack matches it against the
        /// client's <b>application instance</b> certificate, which is also the only one it
        /// has on a Session opened with an anonymous or a user name token.
        /// </para>
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheWorkstationCertificateEarnsARoleOnTheEncryptedEndpointOnly(CancellationToken ct)
        {
            NodeId machineId = await ResolveAsync(ct, Machine).ConfigureAwait(false);

            await using TestClient workstation = await TestClient
                .ConnectWithCertificateAsync(
                    EndpointUrl, "maintenance workstation", null, WorkstationSubject, encrypted: true, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"The workstation certificate says {workstation.ApplicationCertificateSubject}, " +
                    $"and the server was configured with {WorkstationRule.Criteria}")
                .ConfigureAwait(false);

            IReadOnlyList<string> asWorkstation = await SessionOps
                .BrowseNamesAsync(workstation.Session, machineId, ct)
                .ConfigureAwait(false);

            await ReportAsync("The maintenance workstation browses", asWorkstation).ConfigureAwait(false);

            Assert.That(
                asWorkstation,
                Does.Contain("ServiceCode"),
                "An anonymous Session whose client certificate matches the X509Subject rule of " +
                "the ConfigureAdmin Role has to hold that Role. Compare the two subjects above: " +
                "the criteria is a normalised subject and has to match the certificate exactly.");

            NodeId serviceCodeId = await SessionOps
                .ResolveFromAsync(workstation.Session, machineId, ct, ServiceCode)
                .ConfigureAwait(false);

            StatusCode write = await SessionOps
                .WriteValueAsync(workstation.Session, serviceCodeId, Variant.From("SVC-0815"), ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(write),
                Is.True,
                $"The ConfigureAdmin Role carries Write on the service code: {write}");

            // a different client on the same encrypted endpoint: right endpoint, wrong subject
            await using TestClient stranger = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "another client", null, ct)
                .ConfigureAwait(false);

            Assert.That(
                await SessionOps.BrowseNamesAsync(stranger.Session, machineId, ct).ConfigureAwait(false),
                Does.Not.Contain("ServiceCode"),
                "A client whose certificate carries a different subject earns nothing.");

            // the workstation certificate on the unsecured endpoint: right subject, wrong
            // endpoint - and on an unsecured channel the server has no client certificate to
            // match in the first place
            await using TestClient offEndpoint = await TestClient
                .ConnectWithCertificateAsync(
                    EndpointUrl, "workstation without encryption", null, WorkstationSubject, encrypted: false, ct)
                .ConfigureAwait(false);

            Assert.That(
                await SessionOps.BrowseNamesAsync(offEndpoint.Session, machineId, ct).ConfigureAwait(false),
                Does.Not.Contain("ServiceCode"),
                "The Endpoints filter of the ConfigureAdmin Role names the encrypted endpoints only.");
        }

        /// <summary>
        /// A Thumbprint criteria names one certificate rather than a family of them.
        /// </summary>
        /// <remarks>
        /// The counterpart of the X509Subject rule the server configures at startup: both
        /// clients below carry the same subject, and only the one whose thumbprint the
        /// SecurityAdmin registered earns the Role. This also exercises the criteria on the
        /// write path, because the rule is added over OPC UA through the AddIdentity Method
        /// of the Role rather than in the configuration of the server.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AThumbprintCriteriaGrantsTheRoleToOneCertificate(CancellationToken ct)
        {
            NodeId machineId = await ResolveAsync(ct, Machine).ConfigureAwait(false);

            await using TestClient admin = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "security admin registers a certificate", UserOf("secadmin"), ct)
                .ConfigureAwait(false);

            await using TestClient registered = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "the registered client", null, ct)
                .ConfigureAwait(false);

            await using TestClient sibling = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "a client with the same subject", null, ct)
                .ConfigureAwait(false);

            Assert.That(
                registered.ApplicationCertificateThumbprint,
                Is.Not.EqualTo(sibling.ApplicationCertificateThumbprint),
                "The two clients have to differ by certificate for this test to say anything.");

            var rule = new IdentityMappingRuleType {
                CriteriaType = IdentityCriteriaType.Thumbprint,
                Criteria = registered.ApplicationCertificateThumbprint,
            };

            CallMethodResult added = await AddIdentityAsync(admin, WorkstationRole, rule, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(added.StatusCode),
                Is.True,
                $"Adding a Thumbprint rule failed: {added.StatusCode}. " +
                $"The criteria was '{rule.Criteria}', which Part 18 4.4.3 requires to be " +
                "upper case hexadecimal with no separators.");

            try
            {
                Assert.Multiple(async () => {
                    Assert.That(
                        await SessionOps.BrowseNamesAsync(registered.Session, machineId, ct).ConfigureAwait(false),
                        Does.Contain("ServiceCode"),
                        "The Session whose certificate was registered has to hold the Role, and " +
                        "Part 18 4.4.1 requires it to pick the change up without reconnecting.");

                    Assert.That(
                        await SessionOps.BrowseNamesAsync(sibling.Session, machineId, ct).ConfigureAwait(false),
                        Does.Not.Contain("ServiceCode"),
                        "A Thumbprint names one certificate, not every certificate with that subject.");
                });
            }
            finally
            {
                CallMethodResult removed = await RemoveIdentityAsync(admin, WorkstationRole, rule, ct)
                    .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(removed.StatusCode),
                    Is.True,
                    $"Removing the Thumbprint rule again failed: {removed.StatusCode}");
            }

            Assert.That(
                await SessionOps.BrowseNamesAsync(registered.Session, machineId, ct).ConfigureAwait(false),
                Does.Not.Contain("ServiceCode"),
                "Removing the rule has to take the Role away from the open Session again.");
        }

        /// <summary>
        /// CustomConfiguration is what lets a Role with no identity rules be granted at all.
        /// </summary>
        /// <remarks>
        /// Part 18 4.4.1: "If this Property is an empty array and CustomConfiguration is not
        /// TRUE, then the Role cannot be granted to any Session." Taking the one identity
        /// rule of the ConfigureAdmin Role away therefore switches the Role off entirely -
        /// until the flag is set, at which point what is left of its configuration, the
        /// Endpoints filter, is enough on its own and every Session on an encrypted endpoint
        /// holds it.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task CustomConfigurationGrantsARoleWhichHasNoIdentities(CancellationToken ct)
        {
            NodeId machineId = await ResolveAsync(ct, Machine).ConfigureAwait(false);

            await using TestClient admin = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "security admin sets CustomConfiguration", UserOf("secadmin"), ct)
                .ConfigureAwait(false);

            NodeId flagId = await SessionOps
                .ResolveFromAsync(admin.Session, WorkstationRole, ct, new QualifiedName("CustomConfiguration"))
                .ConfigureAwait(false);

            Assert.That(
                flagId.IsNull,
                Is.False,
                "The ConfigureAdmin Role has to expose a CustomConfiguration property.");

            await using TestClient plain = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "an ordinary encrypted client", null, ct)
                .ConfigureAwait(false);

            Assert.That(
                await SessionOps.BrowseNamesAsync(plain.Session, machineId, ct).ConfigureAwait(false),
                Does.Not.Contain("ServiceCode"),
                "Before the flag is set the Role is granted by its identity rule only.");

            CallMethodResult removed = await RemoveIdentityAsync(admin, WorkstationRole, WorkstationRule, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(removed.StatusCode),
                Is.True,
                $"Removing the X509Subject rule the server configured failed: {removed.StatusCode}. " +
                $"The rule was '{WorkstationRule.Criteria}'.");

            try
            {
                StatusCode set = await SessionOps
                    .WriteValueAsync(admin.Session, flagId, Variant.From(true), ct)
                    .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(set),
                    Is.True,
                    $"A SecurityAdmin on an encrypted channel may write CustomConfiguration: {set}");

                try
                {
                    Assert.That(
                        await SessionOps.BrowseNamesAsync(plain.Session, machineId, ct).ConfigureAwait(false),
                        Does.Contain("ServiceCode"),
                        "With CustomConfiguration set and no identity rules left, the Endpoints " +
                        "filter is the whole of the Role configuration, so every Session on an " +
                        "encrypted endpoint holds it.");
                }
                finally
                {
                    StatusCode reset = await SessionOps
                        .WriteValueAsync(admin.Session, flagId, Variant.From(false), ct)
                        .ConfigureAwait(false);

                    Assert.That(
                        StatusCode.IsGood(reset),
                        Is.True,
                        $"Clearing CustomConfiguration again failed: {reset}");
                }
            }
            finally
            {
                CallMethodResult restored = await AddIdentityAsync(admin, WorkstationRole, WorkstationRule, ct)
                    .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(restored.StatusCode),
                    Is.True,
                    $"Putting the X509Subject rule of the server back failed: {restored.StatusCode}");
            }

            Assert.That(
                await SessionOps.BrowseNamesAsync(plain.Session, machineId, ct).ConfigureAwait(false),
                Does.Not.Contain("ServiceCode"),
                "Clearing the flag has to close the Role again.");
        }

        /// <summary>
        /// Every change to the role configuration is reported as an audit event.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The stack reports a RoleMappingRuleChangedAuditEventType from the RoleSet binding
        /// for each of the Part 18 4.4 Methods, successful or not - but only when the server
        /// has AuditingEnabled, which is the one line this sample adds to its configuration
        /// file. Without it a change which should be audited looks exactly like one which is
        /// not, so the flag and this fixture belong together.
        /// </para>
        /// <para>
        /// This is recorded as a known issue because no audit event of any type reaches a
        /// client in 2.0.0-preview.4, on this sample or on any other. Measured: the server
        /// reports Server.Auditing as true, AddIdentity answers Good, and a subscription on
        /// the Server object receives a GeneralModelChangeEvent from that same object - so
        /// the event path itself works - while neither the RoleMappingRuleChanged event of
        /// this fixture nor an AuditCreateSessionEvent from opening a session arrives.
        /// </para>
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ChangingTheRoleConfigurationIsAudited(CancellationToken ct)
        {
            var rule = new IdentityMappingRuleType {
                CriteriaType = IdentityCriteriaType.UserName,
                Criteria = "guest",
            };

            await using TestClient admin = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "security admin under audit", UserOf("secadmin"), ct)
                .ConfigureAwait(false);

            await using EventCapture audit = await EventCapture
                .CreateAsync(
                    admin.Session,
                    ObjectIds.Server,
                    ct,
                    ObjectTypeIds.RoleMappingRuleChangedAuditEventType,
                    [new QualifiedName("Status")],
                    [new QualifiedName("MethodId")])
                .ConfigureAwait(false);

            DataValue auditing = await SessionOps
                .ReadValueAsync(admin.Session, VariableIds.Server_Auditing, ct)
                .ConfigureAwait(false);

            Assert.That(
                auditing.WrappedValue.TryGetValue(out bool enabled) && enabled,
                Is.True,
                $"The sample configures AuditingEnabled, so Server.Auditing has to read true: " +
                $"{auditing.WrappedValue} ({auditing.StatusCode})");

            CallMethodResult added = await AddIdentityAsync(admin, ObjectIds.WellKnownRole_Operator, rule, ct)
                .ConfigureAwait(false);

            try
            {
                Assert.That(
                    StatusCode.IsGood(added.StatusCode),
                    Is.True,
                    $"AddIdentity failed: {added.StatusCode}");

                await KnownIssueAsync(
                    async () => {
                        CapturedEvent reported = await audit
                            .WaitAsync(
                                candidate => candidate.Field(BrowseNames.SourceNode)
                                    .TryGetValue(out NodeId source) &&
                                    source == ObjectIds.WellKnownRole_Operator,
                                TimeSpan.FromSeconds(20),
                                "the server has to audit a change to the role configuration",
                                ct)
                            .ConfigureAwait(false);

                        await TestContext.Out
                            .WriteLineAsync($"The server audited: {reported}")
                            .ConfigureAwait(false);

                        Assert.Multiple(() => {
                            Assert.That(
                                reported.EventType,
                                Is.EqualTo(ObjectTypeIds.RoleMappingRuleChangedAuditEventType),
                                "The filter asked for that type and its subtypes.");

                            Assert.That(
                                reported.Field("Status").TryGetValue(out bool succeeded) && succeeded,
                                Is.True,
                                "The audit event of a change which was applied reports Status true.");

                            Assert.That(
                                reported.Field("MethodId").TryGetValue(out NodeId methodId) && !methodId.IsNull,
                                Is.True,
                                "An AuditUpdateMethodEventType names the Method which was called.");
                        });
                    },
                    "2.0.0-preview.4 delivers no audit event to a subscription on the Server " +
                    "object. Server.Auditing reads true and the Method answered Good, and a " +
                    "GeneralModelChangeEvent from the same object does arrive, so this is not " +
                    "the configuration of the sample and not the event path.")
                    .ConfigureAwait(false);
            }
            finally
            {
                CallMethodResult removed = await RemoveIdentityAsync(
                    admin, ObjectIds.WellKnownRole_Operator, rule, ct).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(removed.StatusCode),
                    Is.True,
                    $"Removing the identity rule again failed: {removed.StatusCode}");
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// The identity of one of the sample accounts, whose password is its user name.
        /// </summary>
        private static IUserIdentity UserOf(string userName)
        {
            return new UserIdentity(userName, Encoding.UTF8.GetBytes(userName));
        }

        /// <summary>
        /// Opens a session for one of the sample accounts.
        /// </summary>
        private Task<TestClient> ConnectAsAsync(string userName, CancellationToken ct)
        {
            return TestClient.ConnectAsync(EndpointUrl, userName, UserOf(userName), ct);
        }

        /// <summary>
        /// Browses a node and returns what the server answered, good or bad.
        /// </summary>
        /// <remarks>
        /// SessionOps.BrowseAsync throws on a bad browse result, and the bad result is the
        /// thing an AccessRestrictions fixture is looking at.
        /// </remarks>
        private static async Task<StatusCode> BrowseStatusAsync(
            TestClient client,
            NodeId nodeId,
            CancellationToken ct)
        {
            var nodesToBrowse = new List<BrowseDescription> {
                new BrowseDescription {
                    NodeId = nodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All,
                },
            };

            BrowseResponse response = await client.Session
                .BrowseAsync(null, null, 0, nodesToBrowse, ct)
                .ConfigureAwait(false);

            return response.Results.ToArray()[0].StatusCode;
        }

        private static async Task<double> ReadDoubleAsync(TestClient client, NodeId nodeId, CancellationToken ct)
        {
            DataValue value = await SessionOps
                .ReadValueAsync(client.Session, nodeId, ct)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, $"Reading failed: {value.StatusCode}");

            value.WrappedValue.TryGetValue(out double number);

            return number;
        }

        private static async Task<RolePermissionType[]> ReadPermissionsAsync(
            TestClient client,
            NodeId nodeId,
            uint attributeId,
            CancellationToken ct)
        {
            DataValue value = await SessionOps
                .ReadAttributeAsync(client.Session, nodeId, attributeId, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading attribute {attributeId} failed: {value.StatusCode}");

            if (!value.WrappedValue.TryGetStructure(out ArrayOf<RolePermissionType> permissions))
            {
                throw new InvalidOperationException(
                    $"Attribute {attributeId} is not an array of RolePermissionType but a {value.WrappedValue.TypeInfo}.");
            }

            return permissions.ToArray();
        }

        private static Task<CallMethodResult> AddIdentityAsync(
            TestClient client,
            NodeId roleId,
            IdentityMappingRuleType rule,
            CancellationToken ct)
        {
            return CallRoleMethodAsync(client, roleId, "AddIdentity", rule, ct);
        }

        private static Task<CallMethodResult> RemoveIdentityAsync(
            TestClient client,
            NodeId roleId,
            IdentityMappingRuleType rule,
            CancellationToken ct)
        {
            return CallRoleMethodAsync(client, roleId, "RemoveIdentity", rule, ct);
        }

        /// <summary>
        /// Calls one of the identity mapping Methods of a Role node.
        /// </summary>
        /// <remarks>
        /// The method node is resolved by browse path rather than written down, because a
        /// Role added at runtime gets its Methods with node ids the server allocates.
        /// </remarks>
        private static async Task<CallMethodResult> CallRoleMethodAsync(
            TestClient client,
            NodeId roleId,
            string methodName,
            IdentityMappingRuleType rule,
            CancellationToken ct)
        {
            NodeId methodId = await SessionOps
                .ResolveFromAsync(client.Session, roleId, ct, new QualifiedName(methodName))
                .ConfigureAwait(false);

            if (methodId.IsNull)
            {
                IReadOnlyList<string> children = await SessionOps
                    .BrowseNamesAsync(client.Session, roleId, ct)
                    .ConfigureAwait(false);

                Assert.Fail(
                    $"The Role {roleId} has no {methodName} method. It has: " +
                    string.Join(", ", children));
            }

            return await CallAsync(client, roleId, methodId, rule, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls an identity mapping Method whose node id is already known, on the Role node
        /// which owns it.
        /// </summary>
        private static Task<CallMethodResult> CallAsync(
            TestClient client,
            NodeId roleId,
            NodeId methodId,
            IdentityMappingRuleType rule,
            CancellationToken ct)
        {
            return SessionOps.CallAsync(
                client.Session,
                roleId,
                methodId,
                ct,
                Variant.From(new ExtensionObject(rule)));
        }
        #endregion
    }
}
