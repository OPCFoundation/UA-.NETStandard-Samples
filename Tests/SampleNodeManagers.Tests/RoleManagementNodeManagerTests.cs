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
        private QualifiedName Reset => Name(RoleManagementNamespace, "Reset");

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

            StatusCode write = await SessionOps
                .WriteValueAsync(engineer.Session, calibrationId, Variant.From(0.5), ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(write),
                Is.True,
                $"The Engineer Role carries Write on the calibration: {write}");
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

            CallMethodResult byEngineer = await CallAsync(engineer, addIdentityId, rule, ct).ConfigureAwait(false);

            // a SecurityAdmin, but on the unsecured endpoint: the right Role, the wrong channel
            await using TestClient unsecured = await TestClient
                .ConnectWithIdentityAsync(EndpointUrl, "admin without encryption", UserOf("secadmin"), ct)
                .ConfigureAwait(false);

            CallMethodResult unencrypted = await CallAsync(unsecured, addIdentityId, rule, ct).ConfigureAwait(false);

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

            return await CallAsync(client, methodId, rule, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls an identity mapping Method whose node id is already known, on the Role node
        /// which owns it.
        /// </summary>
        private static Task<CallMethodResult> CallAsync(
            TestClient client,
            NodeId methodId,
            IdentityMappingRuleType rule,
            CancellationToken ct)
        {
            return SessionOps.CallAsync(
                client.Session,
                ObjectIds.WellKnownRole_Operator,
                methodId,
                ct,
                Variant.From(new ExtensionObject(rule)));
        }
        #endregion
    }
}
