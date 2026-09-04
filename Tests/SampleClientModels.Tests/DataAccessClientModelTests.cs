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
using Quickstarts.DataAccessClient.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the Data Access client exists to show, asked of its model without the window:
    /// the address space is browsed, a variable is monitored and reports values, its
    /// monitoring settings are changed and revised by the server, and a value is written.
    /// </summary>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class DataAccessClientModelTests : ClientModelFixtureBase<DataAccessClientModel>
    {
        private static readonly TimeSpan kValueTimeout = TimeSpan.FromSeconds(10);

        protected override string SampleName => "DataAccess";

        protected override DataAccessClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new DataAccessClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task BrowsingTheObjectsFolderFindsTheServerAndThePlant(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            IReadOnlyList<BrowseNode> children = await Model
                .BrowseChildrenAsync(ObjectIds.ObjectsFolder, ct)
                .ConfigureAwait(false);

            string[] texts = children.Select(child => child.Text).ToArray();

            await TestContext.Out
                .WriteLineAsync("Objects: " + string.Join(", ", texts))
                .ConfigureAwait(false);

            Assert.That(texts, Has.Some.Contains("Server"), "The Server object is missing from the Objects folder.");
            Assert.That(texts, Has.Some.Contains("Factory"), "The plant of the underlying system is missing from the Objects folder.");
            Assert.That(children.Select(child => child.IsLocal), Has.All.True, "A child of the Objects folder lives on another server.");
            Assert.That(children.Select(child => child.NodeId.IsNull), Has.All.False);

            // and the attributes of a node are readable in the same way the window shows them
            IReadOnlyList<AttributeRow> attributes = await Model
                .ReadAttributesAsync(ObjectIds.Server, ct)
                .ConfigureAwait(false);

            Assert.That(attributes.Select(row => row.Name), Does.Contain("BrowseName").And.Contain("DisplayName"));
            Assert.That(attributes.Select(row => row.Name), Does.Not.Contain("Value"), "An object has no Value attribute.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task MonitoringAVariableReportsItsValues(CancellationToken ct)
        {
            var values = new EventSink<MonitoredItemValueChangedEventArgs>();
            Model.ValueChanged += values.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            Assert.That(Model.MonitoredItems, Is.Empty, "A fresh model already monitors something.");

            MonitoredItemRow row = await Model
                .MonitorAsync(VariableIds.Server_ServerStatus_CurrentTime, "CurrentTime", ct)
                .ConfigureAwait(false);

            Assert.That(row.Name, Is.Not.Empty);
            Assert.That(row.DisplayName, Is.EqualTo("CurrentTime"));
            Assert.That(row.Error, Is.Empty, $"The server refused the item: {row.Error}");
            Assert.That(row.ClientHandle, Is.Not.Null, "The engine did not create the item before MonitorAsync returned.");
            Assert.That(Model.MonitoredItems.Select(item => item.Name), Is.EqualTo(new[] { row.Name }));

            // the current time changes with every sample, so a value follows within a
            // couple of publishing intervals
            MonitoredItemValueChangedEventArgs value = await values
                .WaitForAsync(candidate => candidate.Name == row.Name, "no value arrived for the monitored item", kValueTimeout, ct)
                .ConfigureAwait(false);

            // a DateTime travels as a DateTimeUtc, which is what the Variant hands out
            Assert.That(value.Value.WrappedValue.TryGetValue(out DateTimeUtc _), Is.True, "The current time did not arrive as a DateTimeUtc.");
            Assert.That(
                Model.MonitoredItems.Single().Value,
                Is.Not.Null,
                "The model does not keep the last value, so a row added late would show nothing.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task MonitoringSettingsAreRevisedByTheServer(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            MonitoredItemRow row = await Model
                .MonitorAsync(VariableIds.Server_ServerStatus_CurrentTime, "CurrentTime", ct)
                .ConfigureAwait(false);

            string[] names = { row.Name };

            IReadOnlyList<MonitoredItemRow> revised = await Model
                .SetSamplingIntervalAsync(names, 2500, ct)
                .ConfigureAwait(false);

            Assert.That(revised.Select(item => item.Name), Is.EqualTo(names));
            Assert.That(revised[0].SamplingIntervalMs, Is.EqualTo(2500), "The sampling interval the server revised is not reported.");
            Assert.That(revised[0].Error, Is.Empty);

            revised = await Model
                .SetMonitoringModeAsync(names, MonitoringMode.Sampling, ct)
                .ConfigureAwait(false);

            Assert.That(revised[0].MonitoringMode, Is.EqualTo(MonitoringMode.Sampling));

            // a deadband makes no sense on a DateTime, so the server refuses the filter and
            // the model drops it again: the list shows what the server applies
            revised = await Model
                .SetDeadbandAsync(names, DeadbandType.Absolute, 5.0, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"After the refused deadband: filter '{revised[0].DeadbandText}', error '{revised[0].Error}'")
                .ConfigureAwait(false);

            Assert.That(revised[0].DeadbandText, Is.EqualTo("None"), "A deadband the server refused is still shown.");

            await Model.RemoveAsync(names, ct).ConfigureAwait(false);

            Assert.That(Model.MonitoredItems, Is.Empty, "The removed item is still listed.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task WritingTheSetPointRoundTrips(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            // the controller of the first boiler, found the way the window finds it: by
            // browsing down from the Objects folder
            NodeId setPointId = await PathAsync(ct, "Factory", "East", "Boiler1", "FC1001", "SetPoint").ConfigureAwait(false);

            DataValue original = await Model.ReadAttributeAsync(setPointId, Attributes.Value, ct).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(original.StatusCode), Is.True, $"Reading the set point failed: {original.StatusCode}");
            Assert.That(original.WrappedValue.TryGetValue(out float setPoint), Is.True, "The set point is not a float.");

            float written = setPoint + 10f;

            try
            {
                StatusCode result = await Model
                    .WriteAsync(setPointId, Attributes.Value, Variant.From(written), ct)
                    .ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(result), Is.True, $"Writing the set point failed: {result}");

                DataValue readBack = await Model.ReadAttributeAsync(setPointId, Attributes.Value, ct).ConfigureAwait(false);

                Assert.That(
                    readBack.WrappedValue.TryGetValue(out float readBackSetPoint) ? readBackSetPoint : float.NaN,
                    Is.EqualTo(written),
                    "The set point has to come back with the written value.");
            }
            finally
            {
                await Model
                    .WriteAsync(setPointId, Attributes.Value, original.WrappedValue, ct)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task DetachStopsTheValues(CancellationToken ct)
        {
            var values = new EventSink<MonitoredItemValueChangedEventArgs>();
            var changes = new EventSink<ConnectionChangedEventArgs>();
            Model.ValueChanged += values.Handle;
            Model.ConnectionChanged += changes.Handle;

            await AttachAsync(ct).ConfigureAwait(false);
            await Model.MonitorAsync(VariableIds.Server_ServerStatus_CurrentTime, "CurrentTime", ct).ConfigureAwait(false);
            await values.WaitForAsync(_ => true, "no value arrived", kValueTimeout, ct).ConfigureAwait(false);

            await Model.DetachAsync().ConfigureAwait(false);
            await Model.DetachAsync().ConfigureAwait(false);

            Assert.That(Model.IsConnected, Is.False);
            Assert.That(Model.MonitoredItems, Is.Empty, "A detached model still lists monitored items.");

            int seen = values.Count;
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);

            Assert.That(values.Count, Is.EqualTo(seen), "Values kept arriving after the model was detached.");
            Assert.That(
                changes.Events.Select(change => change.Change),
                Is.EqualTo(new[] { ConnectionChange.Attached, ConnectionChange.Detached }));
        }
    }
}
