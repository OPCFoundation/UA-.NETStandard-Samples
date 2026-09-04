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

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the node management sample does: a client builds the address space of the server
    /// with the four services of OPC 10000-4 5.8, and the server decides how far it may go.
    /// </summary>
    /// <remarks>
    /// Every assertion is what an ordinary client observes over a session. The fixture shares
    /// one server across its tests and every test adds nodes to it, so each one works under a
    /// name of its own rather than assuming the folder is empty.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class NodeManagementNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "NodeManagement";

        /// <summary>
        /// The namespace the sample serves its nodes in.
        /// </summary>
        /// <remarks>
        /// Spelled out rather than imported, because Opc.Ua defines a Namespaces class too
        /// and the tests live in a namespace below Opc.Ua.
        /// </remarks>
        private const string NodeManagementNamespace =
            Quickstarts.NodeManagement.Namespaces.NodeManagement;

        private QualifiedName Plant => Name(NodeManagementNamespace, "Plant");
        private QualifiedName Devices => Name(NodeManagementNamespace, "Devices");
        private QualifiedName Commissioned => Name(NodeManagementNamespace, "Commissioned");
        private QualifiedName DeviceCount => Name(NodeManagementNamespace, "DeviceCount");

        #region AddNodes
        /// <summary>
        /// A client creates an object and gives it a variable, and both are then part of the
        /// address space like any other node.
        /// </summary>
        /// <remarks>
        /// The variable carries VariableAttributes, so this also pins down that the data
        /// type, the access level and the initial value a client asks for arrive: a node
        /// added without them would read as a null BaseDataType and the sample would have
        /// nothing to show.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AddNodesCreatesAnObjectAndAVariable(CancellationToken ct)
        {
            NodeId devicesId = await ResolveAsync(ct, Plant, Devices).ConfigureAwait(false);

            NodeId pumpId = await AddObjectAsync(devicesId, "Pump101", ct).ConfigureAwait(false);

            NodeId pressureId = await AddVariableAsync(pumpId, "Pressure", 4.25, ct).ConfigureAwait(false);

            IReadOnlyList<string> devices = await BrowseNamesAsync(devicesId, ct).ConfigureAwait(false);
            IReadOnlyList<string> children = await BrowseNamesAsync(pumpId, ct).ConfigureAwait(false);

            await ReportAsync("Devices holds", devices).ConfigureAwait(false);
            await ReportAsync("Pump101 holds", children).ConfigureAwait(false);

            DataValue value = await SessionOps
                .ReadValueAsync(Session, pressureId, ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(devices, Does.Contain("Pump101"), "AddNodes did not attach the object to its parent.");
                Assert.That(children, Does.Contain("Pressure"), "AddNodes did not attach the variable to the object.");
                Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, $"Reading the new variable answered {value.StatusCode}.");
                Assert.That(
                    value.WrappedValue.TryGetValue(out double pressure) ? pressure : double.NaN,
                    Is.EqualTo(4.25),
                    "The Value attribute of the request did not reach the node.");
            });
        }

        /// <summary>
        /// A client which does not name a NodeId gets the one the node manager mints.
        /// </summary>
        /// <remarks>
        /// <para>
        /// With an empty RequestedNewNodeId the master node manager routes the item by the
        /// namespace of the <b>BrowseName</b>, which is the trap of this service: a browse
        /// name in namespace zero would be routed to the core node manager and refused. The
        /// identifier itself comes from <c>INodeIdFactory.New</c>, which the sample overrides
        /// to build a readable string identifier.
        /// </para>
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AddNodesLetsTheServerChooseTheNodeId(CancellationToken ct)
        {
            NodeId devicesId = await ResolveAsync(ct, Plant, Devices).ConfigureAwait(false);

            AddNodesResult result = await AddAsync(
                NewObject(devicesId, "Valve201"),
                ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The server assigned {result.AddedNodeId} to Valve201")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True, $"AddNodes answered {result.StatusCode}.");

                Assert.That(
                    result.AddedNodeId.NamespaceIndex,
                    Is.EqualTo(NamespaceIndex(NodeManagementNamespace)),
                    "The new node has to land in the namespace of the node manager which owns it.");

                Assert.That(
                    result.AddedNodeId.IdentifierAsString,
                    Does.Match("^Valve201-[0-9]+$"),
                    "The sample overrides INodeIdFactory.New to build the identifier from the browse name.");
            });
        }

        /// <summary>
        /// The node id a client asks for is used, and asking for one which exists is refused.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AddNodesHonoursAndThenRefusesARequestedNodeId(CancellationToken ct)
        {
            NodeId devicesId = await ResolveAsync(ct, Plant, Devices).ConfigureAwait(false);

            var requested = new NodeId("Motor301", NamespaceIndex(NodeManagementNamespace));

            AddNodesItem item = NewObject(devicesId, "Motor301");
            item.RequestedNewNodeId = requested;

            AddNodesResult first = await AddAsync(item, ct).ConfigureAwait(false);

            // the same node id under a different browse name, so that the duplicate browse
            // name is not what refuses the second attempt
            AddNodesItem again = NewObject(devicesId, "Motor302");
            again.RequestedNewNodeId = requested;

            AddNodesResult second = await AddAsync(again, ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The requested node id answered {first.StatusCode}, and a second time {second.StatusCode}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(StatusCode.IsGood(first.StatusCode), Is.True, $"AddNodes answered {first.StatusCode}.");
                Assert.That(first.AddedNodeId, Is.EqualTo(requested), "A requested node id which is free has to be used as it is.");

                Assert.That(
                    second.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNodeIdExists),
                    "A requested node id which is taken has to be refused.");
            });
        }

        /// <summary>
        /// Two siblings may not share a browse name.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AddNodesRefusesADuplicateBrowseName(CancellationToken ct)
        {
            NodeId devicesId = await ResolveAsync(ct, Plant, Devices).ConfigureAwait(false);

            AddNodesResult first = await AddAsync(NewObject(devicesId, "Fan401"), ct).ConfigureAwait(false);
            AddNodesResult second = await AddAsync(NewObject(devicesId, "Fan401"), ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(StatusCode.IsGood(first.StatusCode), Is.True, $"AddNodes answered {first.StatusCode}.");

                Assert.That(
                    second.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadBrowseNameDuplicated),
                    "A browse name a sibling already uses has to be refused.");
            });
        }

        /// <summary>
        /// A new node has to be attached with a hierarchical reference.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AddNodesRefusesANonHierarchicalReference(CancellationToken ct)
        {
            NodeId devicesId = await ResolveAsync(ct, Plant, Devices).ConfigureAwait(false);

            AddNodesItem item = NewObject(devicesId, "Loose501");
            item.ReferenceTypeId = ReferenceTypeIds.HasDescription;

            AddNodesResult result = await AddAsync(item, ct).ConfigureAwait(false);

            Assert.That(
                result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadReferenceNotAllowed),
                "Only a subtype of HierarchicalReferences may attach a new node to its parent.");
        }

        /// <summary>
        /// The sample opens the Devices folder to its clients and nothing else.
        /// </summary>
        /// <remarks>
        /// This is the node manager's own rule rather than the SDK's - the override of
        /// AddNodeAsync - and it is what a server which opts in has to decide for itself.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AddNodesRefusesAParentOutsideTheOpenFolder(CancellationToken ct)
        {
            NodeId plantId = await ResolveAsync(ct, Plant).ConfigureAwait(false);
            NodeId commissionedId = await ResolveAsync(ct, Plant, Commissioned).ConfigureAwait(false);

            AddNodesResult onThePlant = await AddAsync(NewObject(plantId, "Stray601"), ct).ConfigureAwait(false);
            AddNodesResult inTheGroup = await AddAsync(NewObject(commissionedId, "Stray602"), ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    onThePlant.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadParentNodeIdInvalid),
                    "The sample does not let a client hang nodes off the plant itself.");

                Assert.That(
                    inTheGroup.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadParentNodeIdInvalid),
                    "The commissioned group is filled with AddReferences, not with AddNodes.");
            });
        }
        #endregion

        #region DeleteNodes
        /// <summary>
        /// A node a client added can be deleted again, and takes the reference to it with it.
        /// </summary>
        /// <remarks>
        /// DeleteTargetReferences is what removes the reference the parent holds. Without it
        /// the node is gone but the parent still points at it, which a browse reports as a
        /// reference to a node that cannot be read.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DeleteNodesRemovesTheNodeAndTheReferenceToIt(CancellationToken ct)
        {
            NodeId devicesId = await ResolveAsync(ct, Plant, Devices).ConfigureAwait(false);

            NodeId pumpId = await AddObjectAsync(devicesId, "Pump701", ct).ConfigureAwait(false);

            StatusCode deleted = await DeleteAsync(pumpId, deleteTargetReferences: true, ct)
                .ConfigureAwait(false);

            IReadOnlyList<string> devices = await BrowseNamesAsync(devicesId, ct).ConfigureAwait(false);

            DataValue read = await SessionOps
                .ReadAttributeAsync(Session, pumpId, Attributes.BrowseName, ct)
                .ConfigureAwait(false);

            await ReportAsync("After the delete, Devices holds", devices).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(StatusCode.IsGood(deleted), Is.True, $"DeleteNodes answered {deleted}.");
                Assert.That(devices, Does.Not.Contain("Pump701"), "The parent still references the deleted node.");

                Assert.That(
                    read.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown),
                    "The deleted node is still in the address space.");
            });
        }

        /// <summary>
        /// The model the sample ships cannot be deleted by a client.
        /// </summary>
        /// <remarks>
        /// A server which opts in to NodeManagement and protects nothing can be emptied by
        /// its first client. The refusal is the node manager's override of DeleteNodeAsync.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DeleteNodesRefusesTheModelOfTheServer(CancellationToken ct)
        {
            NodeId plantId = await ResolveAsync(ct, Plant).ConfigureAwait(false);
            NodeId devicesId = await ResolveAsync(ct, Plant, Devices).ConfigureAwait(false);

            StatusCode plant = await DeleteAsync(plantId, deleteTargetReferences: true, ct).ConfigureAwait(false);
            StatusCode devices = await DeleteAsync(devicesId, deleteTargetReferences: true, ct).ConfigureAwait(false);

            IReadOnlyList<string> stillThere = await BrowseNamesAsync(plantId, ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(plant, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied), "The plant has to survive a client.");
                Assert.That(devices, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied), "The open folder itself is part of the model.");
                Assert.That(stillThere, Does.Contain("Devices"), "The refused delete changed the address space anyway.");
            });
        }

        /// <summary>
        /// A node manager which has not opted in refuses every one of the four services.
        /// </summary>
        /// <remarks>
        /// The opt-in is per node manager, so the answer depends on who owns the node rather
        /// than on what the server supports. The standard address space is owned by the core
        /// node manager, which has not opted in - which is why NodeManagement is safe to have
        /// implemented in the SDK for every node manager there is.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task NodeManagementIsRefusedForANodeManagerWhichHasNotOptedIn(CancellationToken ct)
        {
            StatusCode deleted = await DeleteAsync(
                ObjectIds.Server_ServerCapabilities,
                deleteTargetReferences: false,
                ct).ConfigureAwait(false);

            StatusCode added = await AddReferenceAsync(
                ObjectIds.Server,
                ReferenceTypeIds.Organizes,
                await ResolveAsync(ct, Plant).ConfigureAwait(false),
                ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"On the standard address space, DeleteNodes answered {deleted} and AddReferences {added}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    deleted,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                    "The core node manager does not accept DeleteNodes.");

                Assert.That(
                    added,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied),
                    "The core node manager does not accept AddReferences either.");
            });
        }
        #endregion

        #region References
        /// <summary>
        /// A client points a second folder at a node without copying or moving it.
        /// </summary>
        /// <remarks>
        /// This is the difference between the two pairs of services: AddNodes creates a node,
        /// AddReferences only creates an edge. The device is browsed under both folders and
        /// is the same node under each.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AddAndDeleteReferencesMoveANodeInAndOutOfAGroup(CancellationToken ct)
        {
            NodeId devicesId = await ResolveAsync(ct, Plant, Devices).ConfigureAwait(false);
            NodeId commissionedId = await ResolveAsync(ct, Plant, Commissioned).ConfigureAwait(false);

            NodeId pumpId = await AddObjectAsync(devicesId, "Pump801", ct).ConfigureAwait(false);

            StatusCode added = await AddReferenceAsync(
                commissionedId,
                ReferenceTypeIds.Organizes,
                pumpId,
                ct).ConfigureAwait(false);

            IReadOnlyList<ReferenceDescription> grouped = await SessionOps
                .BrowseAsync(Session, commissionedId, ct)
                .ConfigureAwait(false);

            StatusCode removed = await DeleteReferenceAsync(
                commissionedId,
                ReferenceTypeIds.Organizes,
                pumpId,
                ct).ConfigureAwait(false);

            IReadOnlyList<string> afterwards = await BrowseNamesAsync(commissionedId, ct).ConfigureAwait(false);
            IReadOnlyList<string> devices = await BrowseNamesAsync(devicesId, ct).ConfigureAwait(false);

            await ReportAsync("The group held", grouped.Select(reference => reference.BrowseName.Name)).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(StatusCode.IsGood(added), Is.True, $"AddReferences answered {added}.");

                Assert.That(
                    grouped.Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris)),
                    Does.Contain(pumpId),
                    "The reference has to make the very same node browsable under the group.");

                Assert.That(StatusCode.IsGood(removed), Is.True, $"DeleteReferences answered {removed}.");
                Assert.That(afterwards, Does.Not.Contain("Pump801"), "DeleteReferences left the edge behind.");

                Assert.That(
                    devices,
                    Does.Contain("Pump801"),
                    "Deleting a reference to a node must not delete the node.");
            });
        }

        /// <summary>
        /// Adding the same edge twice, and deleting one which is not there, are both refused.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReferencesAreRefusedWhenTheyDuplicateOrDoNotExist(CancellationToken ct)
        {
            NodeId devicesId = await ResolveAsync(ct, Plant, Devices).ConfigureAwait(false);
            NodeId commissionedId = await ResolveAsync(ct, Plant, Commissioned).ConfigureAwait(false);

            NodeId pumpId = await AddObjectAsync(devicesId, "Pump901", ct).ConfigureAwait(false);

            StatusCode first = await AddReferenceAsync(commissionedId, ReferenceTypeIds.Organizes, pumpId, ct)
                .ConfigureAwait(false);

            StatusCode duplicate = await AddReferenceAsync(commissionedId, ReferenceTypeIds.Organizes, pumpId, ct)
                .ConfigureAwait(false);

            StatusCode missing = await DeleteReferenceAsync(commissionedId, ReferenceTypeIds.HasNotifier, pumpId, ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(StatusCode.IsGood(first), Is.True, $"AddReferences answered {first}.");

                Assert.That(
                    duplicate,
                    Is.EqualTo((StatusCode)StatusCodes.BadDuplicateReferenceNotAllowed),
                    "The same reference type, direction and target may only be added once.");

                Assert.That(
                    missing,
                    Is.EqualTo((StatusCode)StatusCodes.BadNoMatch),
                    "Deleting a reference which is not there has to say so.");
            });
        }
        #endregion

        #region The Server Side
        /// <summary>
        /// The counter the node manager keeps follows what clients add and delete.
        /// </summary>
        /// <remarks>
        /// The one piece of server state in the sample which is derived from an address space
        /// the server does not control, and the reason a node manager still has work to do
        /// after opting in.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DeviceCountFollowsTheNodesClientsAdd(CancellationToken ct)
        {
            NodeId devicesId = await ResolveAsync(ct, Plant, Devices).ConfigureAwait(false);
            NodeId countId = await ResolveAsync(ct, Plant, DeviceCount).ConfigureAwait(false);

            uint before = await ReadCountAsync(countId, ct).ConfigureAwait(false);

            NodeId pumpId = await AddObjectAsync(devicesId, "PumpA01", ct).ConfigureAwait(false);

            uint afterAdd = await ReadCountAsync(countId, ct).ConfigureAwait(false);

            await DeleteAsync(pumpId, deleteTargetReferences: true, ct).ConfigureAwait(false);

            uint afterDelete = await ReadCountAsync(countId, ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"DeviceCount went {before} -> {afterAdd} -> {afterDelete}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(afterAdd, Is.EqualTo(before + 1), "The node manager did not see the AddNodes.");
                Assert.That(afterDelete, Is.EqualTo(before), "The node manager did not see the DeleteNodes.");
            });
        }

        /// <summary>
        /// A client which is not the one making the change still hears about it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// AddNodes and DeleteNodes raise a GeneralModelChangeEvent by themselves once the
        /// affected node carries a NodeVersion property, which is what the node manager's
        /// call to EnableModelChangeTrackingFor attaches. Without the event a second client
        /// would have to poll to notice that the address space it is looking at has changed.
        /// </para>
        /// <para>
        /// The change the event names is the <b>folder</b>, not the new node: Part 5 9.32.2
        /// only reports a node which carries a NodeVersion, and a node a client just created
        /// has none. So a client learns that something below Devices changed and browses
        /// again, which is what the sample client does.
        /// </para>
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AddNodesReportsAGeneralModelChangeEvent(CancellationToken ct)
        {
            NodeId devicesId = await ResolveAsync(ct, Plant, Devices).ConfigureAwait(false);

            await using EventCapture capture = await EventCapture
                .CreateAsync(
                    Session,
                    ObjectIds.Server,
                    ct,
                    ObjectTypeIds.GeneralModelChangeEventType,
                    [new QualifiedName(Opc.Ua.BrowseNames.Changes)])
                .ConfigureAwait(false);

            await AddObjectAsync(devicesId, "PumpB01", ct).ConfigureAwait(false);

            CapturedEvent reported = await capture.WaitAsync(
                candidate => Changed(candidate).Contains(devicesId),
                TimeSpan.FromSeconds(20),
                "a model change event which names the folder the node was added to",
                ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"The model change event reported {string.Join(", ", Changed(reported))}")
                .ConfigureAwait(false);

            Assert.That(
                reported.EventType,
                Is.EqualTo(ObjectTypeIds.GeneralModelChangeEventType),
                "AddNodes has to report the change as a GeneralModelChangeEvent.");
        }

        /// <summary>
        /// A server which supports the service set says how many items one request may carry.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheServerPublishesItsNodeManagementOperationLimit(CancellationToken ct)
        {
            DataValue value = await SessionOps
                .ReadValueAsync(
                    Session,
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement,
                    ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"MaxNodesPerNodeManagement reads {value.WrappedValue} ({value.StatusCode})")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, $"Reading the operation limit answered {value.StatusCode}.");
                Assert.That(
                    value.WrappedValue.TryGetValue(out uint limit) ? limit : 0u,
                    Is.EqualTo(100u),
                    "The configuration of the sample declares 100.");
            });
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// An AddNodesItem for an object below a parent, with a server assigned node id.
        /// </summary>
        private AddNodesItem NewObject(NodeId parentId, string name)
        {
            return new AddNodesItem {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = Name(NodeManagementNamespace, name),
                NodeClass = NodeClass.Object,
                TypeDefinition = ObjectTypeIds.BaseObjectType,
            };
        }

        /// <summary>
        /// Adds an object and asserts that the server accepted it.
        /// </summary>
        private async Task<NodeId> AddObjectAsync(NodeId parentId, string name, CancellationToken ct)
        {
            AddNodesResult result = await AddAsync(NewObject(parentId, name), ct).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result.StatusCode),
                Is.True,
                $"Adding '{name}' below {parentId} answered {result.StatusCode}.");

            return result.AddedNodeId;
        }

        /// <summary>
        /// Adds a double variable with attributes and asserts that the server accepted it.
        /// </summary>
        private async Task<NodeId> AddVariableAsync(
            NodeId parentId,
            string name,
            double value,
            CancellationToken ct)
        {
            var attributes = new VariableAttributes {
                SpecifiedAttributes = (uint)(
                    NodeAttributesMask.DisplayName |
                    NodeAttributesMask.DataType |
                    NodeAttributesMask.ValueRank |
                    NodeAttributesMask.AccessLevel |
                    NodeAttributesMask.UserAccessLevel |
                    NodeAttributesMask.Value),
                DisplayName = new LocalizedText(name),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = Variant.From(value),
            };

            var item = new AddNodesItem {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                BrowseName = Name(NodeManagementNamespace, name),
                NodeClass = NodeClass.Variable,
                TypeDefinition = VariableTypeIds.BaseDataVariableType,
                NodeAttributes = new ExtensionObject(attributes),
            };

            AddNodesResult result = await AddAsync(item, ct).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result.StatusCode),
                Is.True,
                $"Adding '{name}' below {parentId} answered {result.StatusCode}.");

            return result.AddedNodeId;
        }

        /// <summary>
        /// Sends one AddNodesItem and returns its result, good or bad.
        /// </summary>
        private async Task<AddNodesResult> AddAsync(AddNodesItem item, CancellationToken ct)
        {
            AddNodesResponse response = await Session
                .AddNodesAsync(null, new List<AddNodesItem> { item }, ct)
                .ConfigureAwait(false);

            return response.Results.ToArray()[0];
        }

        /// <summary>
        /// Sends one DeleteNodesItem and returns its result, good or bad.
        /// </summary>
        private async Task<StatusCode> DeleteAsync(
            NodeId nodeId,
            bool deleteTargetReferences,
            CancellationToken ct)
        {
            var item = new DeleteNodesItem {
                NodeId = nodeId,
                DeleteTargetReferences = deleteTargetReferences,
            };

            DeleteNodesResponse response = await Session
                .DeleteNodesAsync(null, new List<DeleteNodesItem> { item }, ct)
                .ConfigureAwait(false);

            return response.Results.ToArray()[0];
        }

        /// <summary>
        /// Adds one forward reference and returns its result, good or bad.
        /// </summary>
        private async Task<StatusCode> AddReferenceAsync(
            NodeId sourceId,
            NodeId referenceTypeId,
            NodeId targetId,
            CancellationToken ct)
        {
            var item = new AddReferencesItem {
                SourceNodeId = sourceId,
                ReferenceTypeId = referenceTypeId,
                IsForward = true,
                TargetServerUri = string.Empty,
                TargetNodeId = targetId,
                TargetNodeClass = NodeClass.Object,
            };

            AddReferencesResponse response = await Session
                .AddReferencesAsync(null, new List<AddReferencesItem> { item }, ct)
                .ConfigureAwait(false);

            return response.Results.ToArray()[0];
        }

        /// <summary>
        /// Deletes one forward reference and returns its result, good or bad.
        /// </summary>
        private async Task<StatusCode> DeleteReferenceAsync(
            NodeId sourceId,
            NodeId referenceTypeId,
            NodeId targetId,
            CancellationToken ct)
        {
            var item = new DeleteReferencesItem {
                SourceNodeId = sourceId,
                ReferenceTypeId = referenceTypeId,
                IsForward = true,
                TargetNodeId = targetId,
                DeleteBidirectional = false,
            };

            DeleteReferencesResponse response = await Session
                .DeleteReferencesAsync(null, new List<DeleteReferencesItem> { item }, ct)
                .ConfigureAwait(false);

            return response.Results.ToArray()[0];
        }

        /// <summary>
        /// The nodes a GeneralModelChangeEvent reports as affected.
        /// </summary>
        private IReadOnlyList<NodeId> Changed(CapturedEvent reported)
        {
            if (!reported.Field(Opc.Ua.BrowseNames.Changes)
                .TryGetStructure(out ArrayOf<ModelChangeStructureDataType> changes))
            {
                return [];
            }

            return changes
                .ToArray()
                .Select(change => ExpandedNodeId.ToNodeId(change.Affected, Session.NamespaceUris))
                .ToArray();
        }

        /// <summary>
        /// Reads the DeviceCount variable.
        /// </summary>
        private async Task<uint> ReadCountAsync(NodeId countId, CancellationToken ct)
        {
            DataValue value = await SessionOps.ReadValueAsync(Session, countId, ct).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading DeviceCount answered {value.StatusCode}.");

            return value.WrappedValue.TryGetValue(out uint count) ? count : 0u;
        }
        #endregion
    }
}
