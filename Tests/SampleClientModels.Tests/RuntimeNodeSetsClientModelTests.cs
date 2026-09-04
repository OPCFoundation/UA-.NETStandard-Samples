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
using Quickstarts.RuntimeNodeSets.Client.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the RuntimeNodeSets client model does without its window: it drives the
    /// control model of the server and watches the vendor model come and go.
    /// </summary>
    /// <remarks>
    /// Every test leaves revision 1 published again, because the state under test is the
    /// server's own and the fixture keeps one server for the whole run.
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class RuntimeNodeSetsClientModelTests : ClientModelFixtureBase<RuntimeNodeSetsClientModel>
    {
        private static readonly TimeSpan s_notificationTimeout = TimeSpan.FromSeconds(30);

        /// <inheritdoc/>
        protected override string SampleName => "RuntimeNodeSets";

        /// <inheritdoc/>
        protected override RuntimeNodeSetsClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new RuntimeNodeSetsClientModel(telemetry);
        }

        [TearDown]
        public async Task RestoreRevision1Async()
        {
            if (!Model.IsConnected)
            {
                return;
            }

            ModelState state = await Model.ReadStateAsync().ConfigureAwait(false);

            if (state.LoadedRevision.Length == 0)
            {
                await Model.LoadAsync("Rev1").ConfigureAwait(false);
            }
            else if (state.LoadedRevision != "Rev1")
            {
                await Model.ReloadAsync("Rev1", ReloadMode.Reload).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Attaching finds the control model and reads what the server has documents for.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachingFindsTheControlModel(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(Model.IsControlModelAvailable, Is.True, "The control model was not found.");
                Assert.That(
                    Model.AvailableRevisions,
                    Is.EqualTo(new[] { "Rev1", "Rev2" }),
                    "The revisions the server advertises changed.");
            });
        }

        /// <summary>
        /// The model the server starts with is the one the browse reports.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheStartingModelIsRevision1(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            ModelState state = await Model.ReadStateAsync(ct).ConfigureAwait(false);
            IReadOnlyList<VendorNode> nodes = await Model.BrowseVendorModelAsync(ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync("Revision 1: " + string.Join(", ", nodes.Select(node => node.Name)))
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(state.LoadedRevision, Is.EqualTo("Rev1"), "The server did not start on revision 1.");
                Assert.That(state.Generation, Is.GreaterThan(0), "The registration has no generation.");
                Assert.That(
                    nodes.Select(node => node.Name),
                    Does.Contain("Conveyor1").And.Not.Contain("Conveyor2"),
                    "Revision 1 does not look the way it did.");
            });
        }

        /// <summary>
        /// A reload replaces the model under the session: the nodes revision 2 adds are
        /// browsable afterwards, without the client reconnecting.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReloadingPublishesTheOtherRevision(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            OperationResult result = await Model.ReloadAsync("Rev2", ReloadMode.Reload, ct).ConfigureAwait(false);

            Assert.That(result.Succeeded, Is.True, result.ToString());

            ModelState state = await Model.ReadStateAsync(ct).ConfigureAwait(false);
            IReadOnlyList<VendorNode> nodes = await Model.BrowseVendorModelAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(state.LoadedRevision, Is.EqualTo("Rev2"), "The control model reports the old revision.");
                Assert.That(
                    nodes.Select(node => node.Name),
                    Does.Contain("Conveyor2").And.Contain("Throughput"),
                    "What revision 2 adds is not browsable.");
            });
        }

        /// <summary>
        /// Removing takes the model off the running server, and loading puts it back.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task RemovingAndLoadingTakeTheModelOffAndPutItBack(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            OperationResult removed = await Model.RemoveAsync(ct).ConfigureAwait(false);

            Assert.That(removed.Succeeded, Is.True, removed.ToString());

            IReadOnlyList<VendorNode> gone = await Model.BrowseVendorModelAsync(ct).ConfigureAwait(false);
            ModelState empty = await Model.ReadStateAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(gone, Is.Empty, "The vendor model is still browsable after a Remove.");
                Assert.That(empty.LoadedRevision, Is.Empty, "The control model still reports a revision.");
            });

            OperationResult loaded = await Model.LoadAsync("Rev1", ct).ConfigureAwait(false);

            Assert.That(loaded.Succeeded, Is.True, loaded.ToString());

            IReadOnlyList<VendorNode> back = await Model.BrowseVendorModelAsync(ct).ConfigureAwait(false);

            Assert.That(
                back.Select(node => node.Name),
                Does.Contain("Conveyor1"),
                "Load did not put the model back.");
        }

        /// <summary>
        /// The refusals: a revision the server has no document for, and a Load over a
        /// model which is already published.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheServerRefusesWhatItCannotDo(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            OperationResult unknown = await Model.LoadAsync("Rev9", ct).ConfigureAwait(false);
            OperationResult duplicate = await Model.LoadAsync("Rev2", ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    unknown.Status,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument),
                    "A revision the sample does not ship was accepted.");

                Assert.That(
                    duplicate.Status,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState),
                    "A second Load over a published model was accepted.");
            });
        }

        /// <summary>
        /// A MonitoredItem on the conveyor speed delivers, and keeps delivering across a
        /// shadow reload - which is what that mode exists for.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AShadowReloadKeepsTheMonitoredItemAlive(CancellationToken ct)
        {
            var values = new EventSink<WatchedValueEventArgs>();

            await AttachAsync(ct).ConfigureAwait(false);

            Model.WatchedValueChanged += values.Handle;

            OperationResult watching = await Model.WatchSpeedAsync(ct).ConfigureAwait(false);

            Assert.That(watching.Succeeded, Is.True, watching.ToString());

            await values.WaitForAsync(
                value => StatusCode.IsGood(value.Value.StatusCode),
                "the MonitoredItem never delivered a value",
                s_notificationTimeout,
                ct).ConfigureAwait(false);

            OperationResult reloaded = await Model
                .ReloadAsync("Rev2", ReloadMode.ShadowReload, ct)
                .ConfigureAwait(false);

            Assert.That(reloaded.Succeeded, Is.True, reloaded.ToString());

            // the new generation is what a browse sees, and the item the old generation
            // still owns is not invalidated by the swap
            IReadOnlyList<VendorNode> nodes = await Model.BrowseVendorModelAsync(ct).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    nodes.Select(node => node.Name),
                    Does.Contain("Conveyor2"),
                    "The shadow reload did not publish revision 2.");

                Assert.That(
                    values.Events.Any(value => value.Value.StatusCode == StatusCodes.BadNodeIdUnknown),
                    Is.False,
                    "The shadow reload invalidated the MonitoredItem it was supposed to keep.");
            });

            await Model.StopWatchingAsync().ConfigureAwait(false);
        }
    }
}
