/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the empty sample does: the smallest address space a node manager can build by
    /// hand, including a reference type of its own.
    /// </summary>
    /// <remarks>
    /// Everything here is written out in CreateAddressSpace rather than loaded from a node
    /// set, which makes this the sample where a migration is most likely to change
    /// something by accident.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class EmptyNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "Empty";

        private const string EmptyNamespace = Quickstarts.EmptyServer.Namespaces.Empty;

        private QualifiedName Trigger => Name(EmptyNamespace, "Trigger");
        private QualifiedName Matrix => Name(EmptyNamespace, "Matrix");

        /// <summary>
        /// The trigger object hangs under the Objects folder and is a plain base object.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TriggerObjectIsOrganizedUnderObjects(CancellationToken ct)
        {
            NodeId triggerId = await ResolveAsync(ct, Trigger).ConfigureAwait(false);

            Assert.That(
                triggerId,
                Is.EqualTo(new NodeId(1u, NamespaceIndex(EmptyNamespace))),
                "The trigger object moved.");

            NodeId typeDefinition = await SessionOps
                .GetTypeDefinitionAsync(Session, triggerId, ct)
                .ConfigureAwait(false);

            Assert.That(
                typeDefinition,
                Is.EqualTo(ObjectTypeIds.BaseObjectType),
                "The trigger is a plain base object.");

            IReadOnlyList<string> children = await BrowseNamesAsync(triggerId, ct).ConfigureAwait(false);

            await ReportAsync("Trigger", children).ConfigureAwait(false);

            Assert.That(children, Does.Contain("Matrix"), "The trigger carries the matrix property.");
        }

        /// <summary>
        /// The matrix property is a two by two array of integers.
        /// </summary>
        /// <remarks>
        /// The value rank and the array dimensions are the whole point of this node: it is
        /// the sample's demonstration that a property can be a matrix.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task MatrixPropertyIsTwoByTwoInt32(CancellationToken ct)
        {
            NodeId matrixId = await ResolveAsync(ct, Trigger, Matrix).ConfigureAwait(false);

            Assert.That(
                matrixId,
                Is.EqualTo(new NodeId(2u, NamespaceIndex(EmptyNamespace))),
                "The matrix property moved.");

            DataValue dataType = await SessionOps
                .ReadAttributeAsync(Session, matrixId, Attributes.DataType, ct)
                .ConfigureAwait(false);

            DataValue valueRank = await SessionOps
                .ReadAttributeAsync(Session, matrixId, Attributes.ValueRank, ct)
                .ConfigureAwait(false);

            DataValue arrayDimensions = await SessionOps
                .ReadAttributeAsync(Session, matrixId, Attributes.ArrayDimensions, ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    dataType.WrappedValue.TryGetValue(out NodeId matrixDataType) ? matrixDataType : NodeId.Null,
                    Is.EqualTo(DataTypeIds.Int32),
                    "The matrix holds integers.");

                Assert.That(
                    valueRank.WrappedValue.TryGetValue(out int matrixValueRank) ? matrixValueRank : ValueRanks.Any,
                    Is.EqualTo(ValueRanks.TwoDimensions),
                    "The matrix is two dimensional.");

                Assert.That(
                    AsUInt32Array(arrayDimensions.WrappedValue),
                    Is.EqualTo(new uint[] { 2, 2 }),
                    "The matrix is two by two.");
            });

            // the property is attached with HasProperty, not with a plain component
            // reference, which is what makes it a property rather than a variable
            IReadOnlyList<ReferenceDescription> properties = await SessionOps.BrowseAsync(
                Session,
                await ResolveAsync(ct, Trigger).ConfigureAwait(false),
                ct,
                referenceTypeId: ReferenceTypeIds.HasProperty,
                includeSubtypes: false).ConfigureAwait(false);

            Assert.That(
                properties.Select(reference => reference.BrowseName.Name),
                Is.EqualTo(new[] { "Matrix" }),
                "The matrix is the only property of the trigger.");
        }

        /// <summary>
        /// The sample's own reference type links the trigger to the server object, and the
        /// server object back to the trigger.
        /// </summary>
        /// <remarks>
        /// The inverse half of this lives in another node manager, so it is the external
        /// reference the sample hands out in CreateAddressSpace which is under test here.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task IsTriggerSourceLinksTriggerToServerObject(CancellationToken ct)
        {
            ushort ns = NamespaceIndex(EmptyNamespace);
            var referenceTypeId = new NodeId(3u, ns);

            NodeId triggerId = await ResolveAsync(ct, Trigger).ConfigureAwait(false);

            IReadOnlyList<ReferenceDescription> forward = await SessionOps.BrowseAsync(
                Session,
                triggerId,
                ct,
                referenceTypeId: referenceTypeId,
                includeSubtypes: false).ConfigureAwait(false);

            Assert.That(
                forward.Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris)),
                Is.EqualTo(new[] { ObjectIds.Server }),
                "The trigger points at the server object through IsTriggerSource.");

            // the sample hands the server object the inverse half of the reference, so it
            // is found by browsing the server object backwards rather than forwards
            IReadOnlyList<ReferenceDescription> inverse = await SessionOps.BrowseAsync(
                Session,
                ObjectIds.Server,
                ct,
                referenceTypeId: referenceTypeId,
                includeSubtypes: false,
                direction: BrowseDirection.Inverse).ConfigureAwait(false);

            Assert.That(
                inverse.Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris)),
                Does.Contain(triggerId),
                "The server object points back at the trigger, which is the external reference the sample adds.");

            DataValue browseName = await SessionOps
                .ReadAttributeAsync(Session, referenceTypeId, Attributes.BrowseName, ct)
                .ConfigureAwait(false);

            DataValue inverseName = await SessionOps
                .ReadAttributeAsync(Session, referenceTypeId, Attributes.InverseName, ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    browseName.WrappedValue.TryGetValue(out QualifiedName name) ? name.Name : null,
                    Is.EqualTo("IsTriggerSource"));

                Assert.That(
                    inverseName.WrappedValue.TryGetValue(out LocalizedText inverse) ? inverse.Text : null,
                    Is.EqualTo("IsSourceOfTrigger"));
            });

            // the reference type is a non hierarchical one, which is why browsing the
            // trigger the ordinary way does not turn up the server object
            IReadOnlyList<ReferenceDescription> supertypes = await SessionOps.BrowseAsync(
                Session,
                referenceTypeId,
                ct,
                referenceTypeId: ReferenceTypeIds.HasSubtype,
                includeSubtypes: false,
                direction: BrowseDirection.Inverse).ConfigureAwait(false);

            Assert.That(
                supertypes.Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris)),
                Is.EqualTo(new[] { ReferenceTypeIds.NonHierarchicalReferences }),
                "IsTriggerSource is a non hierarchical reference type.");
        }

        private static uint[] AsUInt32Array(Variant value)
        {
            return value.TryGetValue(out ArrayOf<uint> array) ? array.ToArray() : [];
        }
    }
}
