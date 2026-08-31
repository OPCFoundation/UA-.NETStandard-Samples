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
    /// What the boiler of the sample server does: it creates a boiler from the type model,
    /// renames its parts after the unit it belongs to, and starts the boiler's own state
    /// machine without being asked.
    /// </summary>
    /// <remarks>
    /// This is the other node manager built on the local SampleNodeManager fork, and it is
    /// a different sample from the boiler in the workshop, which is built on the SDK base
    /// class. The two are worth telling apart: this one runs a state machine which the
    /// node manager starts by calling a method on itself.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class BoilerSampleNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "Sample";

        /// <summary>
        /// The boiler the node manager creates is under the boilers folder.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task BoilersFolderHoldsTheCreatedBoiler(CancellationToken ct)
        {
            NodeId boilers = await PathAsync(ct, "Boilers").ConfigureAwait(false);

            IReadOnlyList<string> names = await BrowseNamesAsync(boilers, ct).ConfigureAwait(false);

            await ReportAsync("Boilers", names).ConfigureAwait(false);

            Assert.That(
                names,
                Does.Contain("Boiler #2"),
                "The node manager creates boiler number two while it builds the address space.");
        }

        /// <summary>
        /// The parts of the boiler are renamed after the unit they belong to.
        /// </summary>
        /// <remarks>
        /// The type model calls them PipeX001 and so on, with an X where the unit number
        /// goes. The node manager substitutes the unit label into the display name and
        /// leaves the browse name alone, so a client sees Pipe2001 but addresses PipeX001.
        /// That split is easy to lose in a rewrite and impossible to notice without looking.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DisplayNamesCarryTheUnitLabelWhileBrowseNamesDoNot(CancellationToken ct)
        {
            NodeId boiler = await PathAsync(ct, "Boilers", "Boiler #2").ConfigureAwait(false);

            IReadOnlyList<ReferenceDescription> parts = await SessionOps
                .BrowseAsync(Session, boiler, ct)
                .ConfigureAwait(false);

            var renamed = new List<string>();

            foreach (ReferenceDescription part in parts)
            {
                NodeId partId = ExpandedNodeId.ToNodeId(part.NodeId, Session.NamespaceUris);

                DataValue displayName = await SessionOps
                    .ReadAttributeAsync(Session, partId, Attributes.DisplayName, ct)
                    .ConfigureAwait(false);

                string shown = displayName.WrappedValue.TryGetValue(out LocalizedText text) ? text.Text : null;

                renamed.Add($"{part.BrowseName.Name} shown as {shown}");
            }

            await ReportAsync("Boiler #2", renamed).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    parts.Select(part => part.BrowseName.Name),
                    Has.Some.Contains("X0"),
                    "The browse names keep the placeholder the type model gives them.");

                Assert.That(
                    renamed,
                    Has.Some.Contains("shown as Pipe20"),
                    "The display names have the unit label substituted into them.");

                Assert.That(
                    renamed.Where(part => part.Contains("shown as", StringComparison.Ordinal)),
                    Has.None.Contains("shown as PipeX0"),
                    "A display name must not be left with the placeholder in it.");
            });
        }

        /// <summary>
        /// The simulations of both boilers are started by the node manager itself.
        /// </summary>
        /// <remarks>
        /// Nobody calls the start method from outside: the node manager calls it on each
        /// boiler's state machine while it builds the address space, so a client which
        /// connects and simply watches sees the values move. Boiler #1 comes out of the
        /// type model and Boiler #2 is created dynamically, so the two reach their state
        /// machines over different construction paths.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SimulationIsRunningWithoutAClientStartingIt(CancellationToken ct)
        {
            foreach (string name in new[] { "Boiler #1", "Boiler #2" })
            {
                NodeId boiler = await PathAsync(ct, "Boilers", name).ConfigureAwait(false);

                IReadOnlyList<string> parts = await BrowseNamesAsync(boiler, ct).ConfigureAwait(false);

                Assert.That(
                    parts,
                    Does.Contain("Simulation"),
                    $"{name} carries the state machine which drives it.");

                NodeId simulation = await ChildAsync(boiler, "Simulation", ct).ConfigureAwait(false);
                NodeId currentState = await ChildAsync(simulation, "CurrentState", ct).ConfigureAwait(false);

                DataValue state = await SessionOps
                    .ReadValueAsync(Session, currentState, ct)
                    .ConfigureAwait(false);

                await TestContext.Out
                    .WriteLineAsync($"The simulation of {name} reports itself as {state.WrappedValue} ({state.StatusCode})")
                    .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(state.StatusCode),
                    Is.True,
                    $"Reading the state of the simulation of {name} failed: {state.StatusCode}");

                Assert.That(
                    state.WrappedValue.TryGetValue(out LocalizedText simulationState) ? simulationState.Text : null,
                    Is.EqualTo("Running"),
                    $"The node manager starts the simulation of {name} itself, so it has to be running.");
            }
        }
    }
}
