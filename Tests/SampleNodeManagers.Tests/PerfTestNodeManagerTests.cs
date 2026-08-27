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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the performance sample does: it serves fifty thousand variables without ever
    /// building a node for them, by encoding the address in the node id.
    /// </summary>
    /// <remarks>
    /// The register number lives in the top byte of the numeric identifier and the index
    /// of the variable within the register in the lower three. Nothing is stored: the node
    /// manager takes a node id apart, asks the register for that slot and builds a node for
    /// the duration of the operation. That arithmetic is the whole sample, so it is what
    /// the tests here pin down.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class PerfTestNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "PerfTest";

        private const string PerfTestNamespace = Quickstarts.PerfTestServer.Namespaces.PerfTest;

        /// <summary>
        /// The register the sample configures is organized under the Objects folder.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task RegisterIsOrganizedUnderObjects(CancellationToken ct)
        {
            IReadOnlyList<ReferenceDescription> children = await SessionOps
                .BrowseAsync(Session, ObjectIds.ObjectsFolder, ct)
                .ConfigureAwait(false);

            ushort ns = NamespaceIndex(PerfTestNamespace);

            ReferenceDescription[] registers = children
                .Where(child => child.BrowseName.NamespaceIndex == ns)
                .ToArray();

            await ReportAsync("Registers", registers.Select(register => register.BrowseName.Name))
                .ConfigureAwait(false);

            Assert.That(registers, Is.Not.Empty, "The sample serves no register at all.");

            Assert.That(
                registers.Select(register => register.BrowseName.Name),
                Does.Contain("R1"),
                "The register the sample configures is called R1.");
        }

        /// <summary>
        /// A variable of the register can be read without ever having been browsed to.
        /// </summary>
        /// <remarks>
        /// The node id is put together here the way the sample encodes it, and the server
        /// answers for a node which does not exist until it is asked for. Its browse name
        /// is the index padded to six digits, which is what proves the right slot was
        /// decoded rather than just any node.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task RegisterVariableIsSynthesizedOnDemand(CancellationToken ct)
        {
            NodeId variableId = RegisterVariable(1, 5);

            DataValue value = await SessionOps
                .ReadValueAsync(Session, variableId, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading the synthesized variable {variableId} failed: {value.StatusCode}");

            DataValue browseName = await SessionOps
                .ReadAttributeAsync(Session, variableId, Attributes.BrowseName, ct)
                .ConfigureAwait(false);

            Assert.That(
                ((QualifiedName)browseName.WrappedValue.AsBoxedObject()).Name,
                Is.EqualTo("000005"),
                "The browse name of a register variable is its index padded to six digits.");

            DataValue dataType = await SessionOps
                .ReadAttributeAsync(Session, variableId, Attributes.DataType, ct)
                .ConfigureAwait(false);

            Assert.That(
                dataType.WrappedValue.AsBoxedObject(),
                Is.EqualTo(DataTypeIds.Int32),
                "A register holds integers.");
        }

        /// <summary>
        /// An index past the end of the register is not a node.
        /// </summary>
        /// <remarks>
        /// This is the bounds check in the sample: without it the arithmetic would happily
        /// hand out a node for any number a client cares to send.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task IndexPastTheEndOfTheRegisterIsUnknown(CancellationToken ct)
        {
            DataValue beyondTheEnd = await SessionOps
                .ReadValueAsync(Session, RegisterVariable(1, 0xFFFFFE), ct)
                .ConfigureAwait(false);

            DataValue unknownRegister = await SessionOps
                .ReadValueAsync(Session, RegisterVariable(200, 1), ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    beyondTheEnd.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown),
                    "An index past the end of the register is not a node.");

                Assert.That(
                    unknownRegister.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown),
                    "A register which does not exist has no variables.");
            });
        }

        /// <summary>
        /// Subscribing to a register variable makes the register push its values.
        /// </summary>
        /// <remarks>
        /// The sample bypasses the sampling machinery of the server: the register keeps the
        /// monitored item itself and writes into it from its own thread. Values therefore
        /// only move while somebody is subscribed, which is what makes this sample fast and
        /// what a migration has to preserve.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SubscribingMakesTheRegisterPushValues(CancellationToken ct)
        {
            NodeId variableId = RegisterVariable(1, 7);

            await using DataChangeCapture capture = await DataChangeCapture
                .CreateAsync(Session, variableId, ct)
                .ConfigureAwait(false);

            IReadOnlyList<DataValue> values = await capture
                .CollectDistinctAsync(3, TimeSpan.FromSeconds(20), ct)
                .ConfigureAwait(false);

            await ReportAsync(
                "Register values",
                values.Select(value => string.Format(CultureInfo.InvariantCulture, "{0}", value.WrappedValue)))
                .ConfigureAwait(false);

            Assert.That(
                values.Select(value => value.StatusCode),
                Is.All.Matches<StatusCode>(StatusCode.IsGood),
                "The register has to push good values.");
        }

        /// <summary>
        /// The node id of a variable within a register, encoded the way the sample does it.
        /// </summary>
        private NodeId RegisterVariable(uint registerId, uint index)
        {
            return new NodeId((registerId << 24) + index, NamespaceIndex(PerfTestNamespace));
        }
    }
}
