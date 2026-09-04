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
using Quickstarts.Boiler.Client.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the Boiler client exists to show, asked of its model without the window: the
    /// boilers of the server are found, the four variables of the selected one arrive,
    /// and selecting another one moves the subscription.
    /// </summary>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class BoilerClientModelTests : ClientModelFixtureBase<BoilerClientModel>
    {
        private static readonly TimeSpan kValueTimeout = TimeSpan.FromSeconds(30);

        protected override string SampleName => "Boiler";

        protected override BoilerClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new BoilerClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachListsTheBoilersOfTheServer(CancellationToken ct)
        {
            Assert.That(Model.Boilers, Is.Empty, "A detached model already knows boilers.");

            await AttachAsync(ct).ConfigureAwait(false);

            // the sample server has one boiler from its node set and builds a second one
            // in code
            Assert.That(
                Model.Boilers.Count,
                Is.GreaterThanOrEqualTo(2),
                "The model did not find the boilers of the sample server. It found: " +
                string.Join(", ", Model.Boilers.Select(boiler => boiler.DisplayName)));

            Assert.That(Model.Boilers.Select(boiler => boiler.DisplayName), Has.All.Not.Empty);
            Assert.That(Model.Boilers.Select(boiler => boiler.NodeId.IsNull), Has.All.False);
            Assert.That(Model.SelectedBoiler, Is.Null, "Nothing is watched until a boiler is selected.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task SelectingABoilerDeliversAllFourVariables(CancellationToken ct)
        {
            var values = new EventSink<BoilerValueChangedEventArgs>();
            Model.ValueChanged += values.Handle;

            await AttachAsync(ct).ConfigureAwait(false);
            await Model.SelectBoilerAsync(Model.Boilers[0], ct).ConfigureAwait(false);

            Assert.That(Model.SelectedBoiler, Is.EqualTo(Model.Boilers[0]));

            // the simulation of the server changes the drum level about once a second, so
            // every variable reports at least its initial value and the level keeps moving
            foreach (BoilerVariable variable in Enum.GetValues<BoilerVariable>())
            {
                await values
                    .WaitForAsync(value => value.Variable == variable, $"no value for {variable} arrived", kValueTimeout, ct)
                    .ConfigureAwait(false);
            }

            await WaitUntilAsync(
                () => DistinctLevels(values).Count >= 3,
                "the drum level did not change three times, so the simulation is not being reported",
                kValueTimeout,
                ct).ConfigureAwait(false);

            Assert.That(
                values.Events.Select(value => value.Value.StatusCode).Where(StatusCode.IsBad),
                Is.Empty,
                "A variable of the boiler reported a bad status.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task SelectingAnotherBoilerReplacesTheSubscription(CancellationToken ct)
        {
            var values = new EventSink<BoilerValueChangedEventArgs>();
            Model.ValueChanged += values.Handle;

            await AttachAsync(ct).ConfigureAwait(false);

            Assume.That(Model.Boilers.Count, Is.GreaterThanOrEqualTo(2), "The test needs two boilers to switch between.");

            await Model.SelectBoilerAsync(Model.Boilers[0], ct).ConfigureAwait(false);
            await values.WaitForAsync(_ => true, "no value arrived for the first boiler", kValueTimeout, ct).ConfigureAwait(false);

            await Model.SelectBoilerAsync(Model.Boilers[1], ct).ConfigureAwait(false);

            Assert.That(Model.SelectedBoiler, Is.EqualTo(Model.Boilers[1]));

            int seenBefore = values.Count;

            // the second boiler reports through the new subscription
            await values
                .WaitForCountAsync(seenBefore + 4, "no values arrived after switching to the second boiler", kValueTimeout, ct)
                .ConfigureAwait(false);

            // and selecting nothing stops the values
            await Model.SelectBoilerAsync(null, ct).ConfigureAwait(false);

            Assert.That(Model.SelectedBoiler, Is.Null);

            int seenAfter = values.Count;
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);

            Assert.That(values.Count, Is.EqualTo(seenAfter), "Values kept arriving after the selection was cleared.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task DetachStopsTheValuesAndIsIdempotent(CancellationToken ct)
        {
            var values = new EventSink<BoilerValueChangedEventArgs>();
            var changes = new EventSink<ConnectionChangedEventArgs>();
            Model.ValueChanged += values.Handle;
            Model.ConnectionChanged += changes.Handle;

            await AttachAsync(ct).ConfigureAwait(false);
            await Model.SelectBoilerAsync(Model.Boilers[0], ct).ConfigureAwait(false);
            await values.WaitForAsync(_ => true, "no value arrived", kValueTimeout, ct).ConfigureAwait(false);

            await Model.DetachAsync().ConfigureAwait(false);
            await Model.DetachAsync().ConfigureAwait(false);

            Assert.That(Model.IsConnected, Is.False);
            Assert.That(Model.Boilers, Is.Empty, "A detached model still lists boilers.");
            Assert.That(Model.SelectedBoiler, Is.Null);

            int seen = values.Count;
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);

            Assert.That(values.Count, Is.EqualTo(seen), "Values kept arriving after the model was detached.");
            Assert.That(
                changes.Events.Select(change => change.Change),
                Is.EqualTo(new[] { ConnectionChange.Attached, ConnectionChange.Detached }));
        }

        private static HashSet<double> DistinctLevels(EventSink<BoilerValueChangedEventArgs> values)
        {
            var levels = new HashSet<double>();

            foreach (BoilerValueChangedEventArgs value in values.Events)
            {
                if (value.Variable == BoilerVariable.DrumLevel && value.Value.WrappedValue.TryGetValue(out double level))
                {
                    levels.Add(level);
                }
            }

            return levels;
        }
    }
}
