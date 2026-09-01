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

// the model constants of the sample have the same names as the ones of the standard
// address space, and Opc.Ua wins inside a namespace below it, so they are aliased here
using BoilerBrowseNames = Quickstarts.Boiler.BrowseNames;
using BoilerObjectTypes = Quickstarts.Boiler.ObjectTypes;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the boiler sample does: one boiler comes from a node set and is upgraded to a
    /// typed object, a second one is built from the type model in code, and a simulation
    /// drives both.
    /// </summary>
    /// <remarks>
    /// The two boilers reach the address space along completely different routes, which is
    /// the point of the sample and the reason both are checked here. The simulation counts
    /// the drum level up modulo one hundred for the first boiler and modulo twenty for the
    /// second, so the range a client observes tells the two apart.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class BoilerWorkshopNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "Boiler";

        private const string BoilerNamespace = Quickstarts.Boiler.Namespaces.Boiler;
        private const string InstanceNamespace = Quickstarts.Boiler.Namespaces.Boiler + "/Instance";

        /// <summary>
        /// The boiler from the node set is a typed boiler with the components of its type.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task Boiler1FromTheNodeSetIsATypedBoiler(CancellationToken ct)
        {
            NodeId boilerId = await ResolveBoiler1Async(ct).ConfigureAwait(false);

            await AssertIsABoilerAsync(boilerId, "Boiler #1", ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The boiler built in code has the same shape as the one from the node set.
        /// </summary>
        /// <remarks>
        /// This one is created from the type model with node ids the node manager hands
        /// out, so it proves that the type model is complete enough to build an instance
        /// from, not just to read one back.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task Boiler2CreatedInCodeMirrorsBoiler1(CancellationToken ct)
        {
            NodeId boilerId = await ResolveBoiler2Async(ct).ConfigureAwait(false);

            await AssertIsABoilerAsync(boilerId, "Boiler #2", ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The simulation counts the drum level of the first boiler up below one hundred.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SimulationCountsBoiler1DrumLevelBelowOneHundred(CancellationToken ct)
        {
            NodeId boilerId = await ResolveBoiler1Async(ct).ConfigureAwait(false);

            await AssertDrumLevelCountsUpAsync(boilerId, 100, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The simulation counts the drum level of the second boiler up below twenty.
        /// </summary>
        /// <remarks>
        /// The lower ceiling is the only thing which distinguishes the second boiler's
        /// simulation from the first one's, so a migration which wires both to the same
        /// branch would be caught here and nowhere else.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SimulationCountsBoiler2DrumLevelBelowTwenty(CancellationToken ct)
        {
            NodeId boilerId = await ResolveBoiler2Async(ct).ConfigureAwait(false);

            await AssertDrumLevelCountsUpAsync(boilerId, 20, ct).ConfigureAwait(false);
        }

        private Task<NodeId> ResolveBoiler1Async(CancellationToken ct)
        {
            return ResolveAsync(ct, Name(BoilerNamespace, BoilerBrowseNames.Boiler1));
        }

        private Task<NodeId> ResolveBoiler2Async(CancellationToken ct)
        {
            return ResolveAsync(ct, Name(InstanceNamespace, "Boiler #2"));
        }

        private async Task AssertIsABoilerAsync(NodeId boilerId, string what, CancellationToken ct)
        {
            NodeId typeDefinition = await SessionOps
                .GetTypeDefinitionAsync(Session, boilerId, ct)
                .ConfigureAwait(false);

            Assert.That(
                typeDefinition,
                Is.EqualTo(new NodeId(BoilerObjectTypes.BoilerType, NamespaceIndex(BoilerNamespace))),
                $"{what} is not typed as a boiler, so the node manager did not upgrade it.");

            IReadOnlyList<string> children = await BrowseNamesAsync(boilerId, ct).ConfigureAwait(false);

            await ReportAsync(what, children).ConfigureAwait(false);

            Assert.That(
                children,
                Does.Contain(BoilerBrowseNames.InputPipe)
                    .And.Contain(BoilerBrowseNames.Drum)
                    .And.Contain(BoilerBrowseNames.OutputPipe),
                $"{what} does not carry the components its type declares.");

            // the drum level is the node the simulation drives, so it has to be reachable
            NodeId levelId = await ResolveDrumLevelAsync(boilerId, ct).ConfigureAwait(false);

            DataValue level = await SessionOps
                .ReadValueAsync(Session, levelId, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(level.StatusCode),
                Is.True,
                $"Reading the drum level of {what} failed: {level.StatusCode}");
        }

        private Task<NodeId> ResolveDrumLevelAsync(NodeId boilerId, CancellationToken ct)
        {
            return ResolveFromAsync(
                boilerId,
                ct,
                Name(BoilerNamespace, BoilerBrowseNames.Drum),
                Name(BoilerNamespace, BoilerBrowseNames.LevelIndicator),
                Name(BoilerNamespace, BoilerBrowseNames.Output));
        }

        private async Task AssertDrumLevelCountsUpAsync(NodeId boilerId, int ceiling, CancellationToken ct)
        {
            NodeId levelId = await ResolveDrumLevelAsync(boilerId, ct).ConfigureAwait(false);

            await using DataChangeCapture capture = await DataChangeCapture
                .CreateAsync(Session, levelId, ct)
                .ConfigureAwait(false);

            // the simulation ticks once a second, so three distinct values prove it runs
            IReadOnlyList<DataValue> values = await capture
                .CollectDistinctAsync(3, TimeSpan.FromSeconds(20), ct)
                .ConfigureAwait(false);

            double[] levels = values
                .Select(value => value.WrappedValue.ConvertTo(BuiltInType.Double).TryGetValue(out double level) ? level : double.NaN)
                .ToArray();

            await ReportAsync(
                $"Drum level (below {ceiling})",
                levels.Select(level => level.ToString(CultureInfo.InvariantCulture)))
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    levels,
                    Is.All.InRange(0d, ceiling - 1d),
                    $"The simulation has to keep the drum level below {ceiling}.");

                Assert.That(
                    levels.Distinct().Count(),
                    Is.GreaterThan(1),
                    "The simulation has to change the drum level.");
            });
        }
    }
}
