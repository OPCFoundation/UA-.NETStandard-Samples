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
    /// What the memory buffer sample does: it serves a block of raw memory as tags, and
    /// takes the monitored item machinery of the server out of the loop entirely.
    /// </summary>
    /// <remarks>
    /// The tags do not exist as nodes: a node id names a buffer and an offset into it, and
    /// the node manager builds a tag for the operation. Because the buffer publishes into
    /// the monitored item itself, it refuses everything the standard machinery would have
    /// handled - filters, index ranges and data encodings - and those refusals are part of
    /// what the sample promises.
    ///
    /// This node manager is one of the two built on the local SampleNodeManager fork, so
    /// these tests are also the coverage for that base class.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class MemoryBufferNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "Sample";

        private const string MemoryBufferNamespace = "http://samples.org/UA/MemoryBuffer";
        private const string InstanceNamespace = "http://samples.org/UA/MemoryBuffer/Instance";

        /// <summary>
        /// The buffers the configuration declares are served.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ConfiguredBuffersAreExposed(CancellationToken ct)
        {
            NodeId buffers = await ResolveAsync(ct, Name(MemoryBufferNamespace, "MemoryBuffers"))
                .ConfigureAwait(false);

            IReadOnlyList<string> names = await BrowseNamesAsync(buffers, ct).ConfigureAwait(false);

            await ReportAsync("MemoryBuffers", names).ConfigureAwait(false);

            Assert.That(
                names,
                Does.Contain("UInt32").And.Contain("Double"),
                "The sample configuration declares an unsigned integer and a double buffer.");
        }

        /// <summary>
        /// A tag is built from its node id, without ever having been browsed to.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TagIsSynthesizedFromItsNodeId(CancellationToken ct)
        {
            DataValue value = await SessionOps
                .ReadValueAsync(Session, Tag("UInt32", 0), ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"{Tag("UInt32", 0)} = {value.WrappedValue} ({value.StatusCode})")
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading a tag of the buffer failed: {value.StatusCode}");
        }

        /// <summary>
        /// A node id which does not name a slot of the buffer is not a node.
        /// </summary>
        /// <remarks>
        /// The buffer is a hundred tags of four bytes each, so an offset past the end of it
        /// has to be refused rather than read off the end of the memory.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task OffsetPastTheEndOfTheBufferIsUnknown(CancellationToken ct)
        {
            DataValue pastTheEnd = await SessionOps
                .ReadValueAsync(Session, Tag("UInt32", 100_000), ct)
                .ConfigureAwait(false);

            DataValue malformed = await SessionOps
                .ReadValueAsync(Session, new NodeId("UInt32[not a number]", NamespaceIndex(InstanceNamespace)), ct)
                .ConfigureAwait(false);

            DataValue unknownBuffer = await SessionOps
                .ReadValueAsync(Session, Tag("NoSuchBuffer", 0), ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"past the end: {pastTheEnd.StatusCode}, malformed: {malformed.StatusCode}, " +
                    $"unknown buffer: {unknownBuffer.StatusCode}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    pastTheEnd.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown),
                    "An offset past the end of the buffer is not a node.");

                Assert.That(
                    malformed.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown),
                    "A node id which does not parse as an offset is not a node.");

                Assert.That(
                    unknownBuffer.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown),
                    "A buffer which was not configured has no tags.");
            });
        }

        /// <summary>
        /// The buffer refuses everything its own publishing cannot honour.
        /// </summary>
        /// <remarks>
        /// These three refusals are the sample's, not the server's: because the buffer
        /// writes into the monitored item itself, there is nothing left to apply a filter,
        /// an index range or an encoding, so asking for one has to fail at creation rather
        /// than be quietly ignored.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task MonitoringATagRefusesFilterIndexRangeAndEncoding(CancellationToken ct)
        {
            NodeId tag = Tag("UInt32", 4);

            ServiceResult withFilter = await TryMonitorAsync(
                tag,
                ct,
                filter: new DataChangeFilter {
                    Trigger = DataChangeTrigger.StatusValue,
                    DeadbandType = (uint)DeadbandType.None,
                }).ConfigureAwait(false);

            ServiceResult withIndexRange = await TryMonitorAsync(tag, ct, indexRange: "0:1")
                .ConfigureAwait(false);

            ServiceResult withEncoding = await TryMonitorAsync(
                tag,
                ct,
                dataEncoding: new QualifiedName(BrowseNames.DefaultXml)).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"filter: {withFilter}, index range: {withIndexRange}, encoding: {withEncoding}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    withFilter?.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadFilterNotAllowed),
                    "A tag of the buffer takes no monitoring filter.");

                Assert.That(
                    withIndexRange?.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadIndexRangeInvalid),
                    "A tag of the buffer is a scalar, so it takes no index range.");

                Assert.That(
                    withEncoding?.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadDataEncodingUnsupported),
                    "A tag of the buffer takes no data encoding.");
            });
        }

        /// <summary>
        /// A plain monitored item on a tag is served by the buffer's own publishing.
        /// </summary>
        /// <remarks>
        /// This is the path the refusals above protect. The buffer scans itself and writes
        /// straight into the monitored item, so a subscriber sees a stream of values
        /// without the server ever sampling the node.
        ///
        /// What a client writes is not what it reads back here: the buffer keeps counting
        /// over the whole block, so a written value is overwritten again within the scan
        /// interval. The write is checked for being accepted, not for being kept.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TagIsWritableAndTheBufferPublishesItsOwnValues(CancellationToken ct)
        {
            NodeId tag = Tag("UInt32", 8);

            StatusCode written = await SessionOps
                .WriteValueAsync(Session, tag, Variant.From(1234u), ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(written),
                Is.True,
                $"Writing into the buffer failed: {written}");

            await using DataChangeCapture capture = await DataChangeCapture
                .CreateAsync(Session, tag, ct)
                .ConfigureAwait(false);

            IReadOnlyList<DataValue> reported = await capture
                .CollectDistinctAsync(3, TimeSpan.FromSeconds(20), ct)
                .ConfigureAwait(false);

            await ReportAsync(
                "Buffer reported",
                reported.Select(value => string.Format(CultureInfo.InvariantCulture, "{0}", value.WrappedValue)))
                .ConfigureAwait(false);

            Assert.That(
                reported.Select(value => value.StatusCode),
                Is.All.Matches<StatusCode>(StatusCode.IsGood),
                "The buffer has to publish good values into a monitored item.");
        }

        /// <summary>
        /// The node id of a tag, built the way the sample encodes it.
        /// </summary>
        private NodeId Tag(string buffer, int offset)
        {
            return new NodeId(
                string.Format(CultureInfo.InvariantCulture, "{0}[{1}]", buffer, offset),
                NamespaceIndex(InstanceNamespace));
        }

        /// <summary>
        /// Tries to monitor a node and reports what the server made of it.
        /// </summary>
        private async Task<ServiceResult> TryMonitorAsync(
            NodeId nodeId,
            CancellationToken ct,
            MonitoringFilter filter = null,
            string indexRange = null,
            QualifiedName dataEncoding = default)
        {
            await using DataChangeCapture capture = await DataChangeCapture
                .CreateAsync(
                    Session,
                    nodeId,
                    ct,
                    filter: filter,
                    indexRange: indexRange,
                    dataEncoding: dataEncoding,
                    throwOnItemError: false)
                .ConfigureAwait(false);

            return capture.ItemError;
        }
    }
}
