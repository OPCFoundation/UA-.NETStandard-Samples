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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Samples.Client;
using Quickstarts.NodeManagement.Client.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The model of the NodeManagement client, driven the way its window drives it.
    /// </summary>
    /// <remarks>
    /// The server keeps what a client adds until it is deleted, so every node a test creates
    /// carries the name of the fixture and is deleted again when the test ends, whether it
    /// passed or not.
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class NodeManagementClientModelTests : ClientModelFixtureBase<NodeManagementClientModel>
    {
        private static readonly TimeSpan kEventTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The prefix of every node the fixture creates, so that a leftover from a previous
        /// run is recognisable rather than confusing.
        /// </summary>
        private const string kPrefix = "ModelTest";

        protected override string SampleName => "NodeManagement";

        protected override NodeManagementClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new NodeManagementClientModel(telemetry);
        }

        [TearDown]
        public async Task DeleteLeftoversAsync()
        {
            // runs before the base class detaches the model, so the session is still open
            if (Model == null || !Model.IsConnected)
            {
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            AddressSpace addressSpace = await Model.ReadAddressSpaceAsync(timeout.Token).ConfigureAwait(false);

            foreach (NodeEntry entry in addressSpace.Devices.Where(entry => entry.Depth == 0 && entry.Name.StartsWith(kPrefix, StringComparison.Ordinal)))
            {
                await Model.DeleteNodeAsync(entry, timeout.Token).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachResolvesTheFoldersAndTheNamespace(CancellationToken ct)
        {
            Assert.Multiple(() => {
                Assert.That(Model.IsModelAvailable, Is.False, "A detached model already claims the namespace.");
                Assert.That(Model.DevicesId.IsNull, Is.True);
            });

            await AttachAsync(ct).ConfigureAwait(false);

            NodeId devicesId = await PathAsync(ct, "Plant", "Devices").ConfigureAwait(false);
            NodeId commissionedId = await PathAsync(ct, "Plant", "Commissioned").ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(Model.IsModelAvailable, Is.True, "The sample server serves the namespace of its model.");
                Assert.That(Model.DevicesId, Is.EqualTo(devicesId), "The Devices folder was not resolved.");
                Assert.That(Model.CommissionedId, Is.EqualTo(commissionedId), "The Commissioned group was not resolved.");
                Assert.That(
                    Model.NamespaceIndex,
                    Is.EqualTo(NamespaceIndex(NodeManagementClientModel.NodeManagementNamespaceUri)),
                    "The browse names the model sends have to carry the index of the model namespace.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheFourServicesRoundTripThroughTheFolders(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            string pump = $"{kPrefix}Pump";

            OperationResult added = await Model.AddNodeAsync(NodeClass.Object, pump, Model.DevicesId, ct).ConfigureAwait(false);

            Assert.That(added.Succeeded, Is.True, $"AddNodes below the Devices folder answered {added}.");
            Assert.That(added.What, Does.StartWith($"Adding Object '{pump}' as "), "A success names the node the server assigned.");

            AddressSpace addressSpace = await Model.ReadAddressSpaceAsync(ct).ConfigureAwait(false);
            NodeEntry pumpEntry = addressSpace.Devices.SingleOrDefault(entry => entry.Name == pump);

            Assert.That(pumpEntry, Is.Not.Null, "AddNodes did not put the object below Devices; " + Seen(addressSpace));
            Assert.That(pumpEntry.Depth, Is.Zero, "A child of the folder sits at depth 0.");

            // a variable of the device the client just created
            OperationResult pressure = await Model.AddNodeAsync(NodeClass.Variable, "Pressure", pumpEntry.NodeId, ct).ConfigureAwait(false);

            Assert.That(pressure.Succeeded, Is.True, $"AddNodes below the new object answered {pressure}.");

            addressSpace = await Model.ReadAddressSpaceAsync(ct).ConfigureAwait(false);
            NodeEntry pressureEntry = addressSpace.Devices.SingleOrDefault(entry => entry.Name == "Pressure");

            Assert.Multiple(() => {
                Assert.That(pressureEntry, Is.Not.Null, "The variable is not listed below its object; " + Seen(addressSpace));
                Assert.That(pressureEntry?.Depth, Is.EqualTo(1), "The variable of a device is one level below it.");
                Assert.That(pressureEntry?.NodeClass, Is.EqualTo(NodeClass.Variable));
                Assert.That(pressureEntry?.Value, Is.EqualTo("0"), "A new variable starts with the value the client sent.");
            });

            // the very same node reachable from the group as well
            OperationResult referenced = await Model.AddReferenceAsync(pumpEntry, ct).ConfigureAwait(false);

            Assert.That(referenced.Succeeded, Is.True, $"AddReferences answered {referenced}.");

            addressSpace = await Model.ReadAddressSpaceAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(addressSpace.Commissioned.Select(entry => entry.Name), Does.Contain(pump), "The group does not list the node; " + Seen(addressSpace));
                Assert.That(addressSpace.Devices.Select(entry => entry.Name), Does.Contain(pump), "A reference must not move the node out of its folder.");
            });

            // dropping the reference leaves the node alone
            OperationResult dropped = await Model.DeleteReferenceAsync(pumpEntry, ct).ConfigureAwait(false);

            Assert.That(dropped.Succeeded, Is.True, $"DeleteReferences answered {dropped}.");

            addressSpace = await Model.ReadAddressSpaceAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(addressSpace.Commissioned.Select(entry => entry.Name), Does.Not.Contain(pump), "DeleteReferences left the node in the group.");
                Assert.That(addressSpace.Devices.Select(entry => entry.Name), Does.Contain(pump), "Deleting a reference to a node must not delete the node.");
            });

            // and deleting the node takes its variable with it
            OperationResult deleted = await Model.DeleteNodeAsync(pumpEntry, ct).ConfigureAwait(false);

            Assert.That(deleted.Succeeded, Is.True, $"DeleteNodes answered {deleted}.");
            Assert.That(deleted.ToString(), Does.Contain("Good"), "The status bar of the window greps for the status name.");

            addressSpace = await Model.ReadAddressSpaceAsync(ct).ConfigureAwait(false);

            Assert.That(
                addressSpace.Devices.Select(entry => entry.Name),
                Does.Not.Contain(pump).And.Not.Contain("Pressure"),
                "DeleteNodes left something behind; " + Seen(addressSpace));
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnotherClientChangingTheModelIsReported(CancellationToken ct)
        {
            var changes = new EventSink<EventArgs>();
            Model.ModelChanged += changes.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            // the situation the service set creates: somebody else adds a node while this
            // client is looking at the folder
            await using TestClient other = await ConnectAsync(null, false, "another client", ct).ConfigureAwait(false);

            string name = $"{kPrefix}Elsewhere";

            var item = new AddNodesItem {
                ParentNodeId = Model.DevicesId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName(name, Model.NamespaceIndex),
                NodeClass = NodeClass.Object,
                TypeDefinition = ObjectTypeIds.BaseObjectType,
            };

            AddNodesResponse response = await other.Session
                .AddNodesAsync(null, new List<AddNodesItem> { item }, ct)
                .ConfigureAwait(false);

            AddNodesResult result = response.Results.ToArray()[0];

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True, $"The other client could not add a node: {result.StatusCode}");

            await changes
                .WaitForAsync(_ => true, "the model change event of the other client's AddNodes did not arrive", kEventTimeout, ct)
                .ConfigureAwait(false);

            AddressSpace addressSpace = await Model.ReadAddressSpaceAsync(ct).ConfigureAwait(false);

            Assert.That(
                addressSpace.Devices.Select(entry => entry.Name),
                Does.Contain(name),
                "Reading again after the event shows what the other client did.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheCoreNodeManagerRefusesADelete(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            OperationResult refused = await Model.DeleteStandardNodeAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    refused.Status,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                    "ServerCapabilities belongs to a node manager which has not opted in.");
                Assert.That(refused.ToString(), Does.Contain("BadUserAccessDenied"));
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheInputChecksAnswerWithoutARoundTrip(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            OperationResult noName = await Model.AddNodeAsync(NodeClass.Object, "  ", Model.DevicesId, ct).ConfigureAwait(false);
            OperationResult noParent = await Model.AddNodeAsync(NodeClass.Object, $"{kPrefix}Orphan", NodeId.Null, ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(noName.Status, Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameInvalid));
                Assert.That(noParent.Status, Is.EqualTo((StatusCode)StatusCodes.BadParentNodeIdInvalid));
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task DetachStopsTheEventsAndIsIdempotent(CancellationToken ct)
        {
            var changes = new EventSink<EventArgs>();
            Model.ModelChanged += changes.Handle;

            await AttachAsync(ct).ConfigureAwait(false);
            await Model.DetachAsync().ConfigureAwait(false);
            await Model.DetachAsync().ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(Model.IsConnected, Is.False);
                Assert.That(Model.IsModelAvailable, Is.False, "A detached model still claims the namespace.");
                Assert.That(Model.DevicesId.IsNull, Is.True, "A detached model still holds a folder.");
            });

            Assert.ThrowsAsync<InvalidOperationException>(
                () => Model.ReadAddressSpaceAsync(ct),
                "A detached model has no session to browse on.");
        }

        private static string Seen(AddressSpace addressSpace)
        {
            return $"Devices holds [{string.Join(", ", addressSpace.Devices.Select(entry => entry.Name))}], " +
                $"the group holds [{string.Join(", ", addressSpace.Commissioned.Select(entry => entry.Name))}]";
        }
    }
}
