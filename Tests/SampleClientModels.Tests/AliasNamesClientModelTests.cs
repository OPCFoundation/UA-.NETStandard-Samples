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
using Quickstarts.AliasNames.Client.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the AliasNames client exists to show, asked of its model without the window:
    /// every signal of the plant found by structure answers to a tag name, the search runs
    /// the other way and finds them all, and only a SecurityAdmin over an encrypted channel
    /// may change the tag list.
    /// </summary>
    /// <remarks>
    /// The session of the fixture is anonymous. The mutation test opens a second managed
    /// session for the administrator and attaches a model of its own, the way a second
    /// window would.
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class AliasNamesClientModelTests : ClientModelFixtureBase<AliasNamesClientModel>
    {
        /// <summary>
        /// The signals the plant of the sample server has.
        /// </summary>
        private const int kPlantSignals = 7;

        protected override string SampleName => "AliasNames";

        protected override AliasNamesClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new AliasNamesClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task EveryPlantTagHasAName(CancellationToken ct)
        {
            Assert.That(Model.Plant, Is.Empty, "A detached model already knows the plant.");

            await AttachAsync(ct).ConfigureAwait(false);

            IReadOnlyList<PlantEntry> plant = await Model.LoadPlantAsync(ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync("The plant: " + string.Join(", ", plant.Select(entry => $"{entry.Path}={entry.TagName}")))
                .ConfigureAwait(false);

            Assert.That(plant, Is.SameAs(Model.Plant));
            Assert.That(plant, Has.Count.EqualTo(kPlantSignals), "The plant does not have the signals of the sample.");

            Assert.Multiple(() => {
                foreach (PlantEntry entry in plant)
                {
                    Assert.That(entry.NodeId.IsNull, Is.False, $"{entry.Path} has no node.");
                    Assert.That(entry.Value, Does.Not.StartWith("Bad"), $"Reading {entry.Path} was refused: {entry.Value}");

                    // the column the sample is about: the node was found by browsing, and
                    // the inventory knows it by a name the address space does not carry
                    Assert.That(entry.TagName, Is.Not.Empty, $"{entry.Path} has no tag name in the inventory.");
                }

                Assert.That(plant.Select(entry => entry.TagName), Is.Unique, "Two signals answer to the same tag.");
            });
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task StandardSearchFindsEveryTagAndVerboseNamesTheCategory(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            IReadOnlyList<PlantEntry> plant = await Model.LoadPlantAsync(ct).ConfigureAwait(false);

            AliasCategoryChoice standard = AliasNamesClientModel.Categories[0];

            Assume.That(standard.CategoryName, Is.Null, "The first category is the standard TagVariables.");

            // an empty pattern searches for everything
            AliasSearchResult found = await Model.SearchAsync(standard, string.Empty, verbose: false, ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"FindAlias: {found.Outcome.Text}; {found.LastChange}").ConfigureAwait(false);

            Assert.That(found.Outcome.Failed, Is.False, $"The search was refused: {found.Outcome.Text}");
            Assert.That(found.Outcome.Text, Does.Contain($"{found.Aliases.Count} alias(es)"));
            Assert.That(found.LastChange, Is.Not.Empty);

            string[] names = found.Aliases.Select(alias => alias.Name).ToArray();

            Assert.Multiple(() => {
                foreach (PlantEntry entry in plant)
                {
                    Assert.That(names, Does.Contain(entry.TagName), $"The search did not find {entry.TagName}.");
                }

                foreach (AliasEntry alias in found.Aliases)
                {
                    Assert.That(alias.ResolvesTo, Is.Not.Empty, $"{alias.Name} stands for nothing.");
                    Assert.That(alias.Value, Does.Not.StartWith("Bad"), $"The target of {alias.Name} could not be read: {alias.Value}");

                    // FindAlias does not say which category an entry came from
                    Assert.That(alias.Category, Is.Empty);
                }
            });

            AliasSearchResult verbose = await Model.SearchAsync(standard, "%", verbose: true, ct).ConfigureAwait(false);

            Assert.That(verbose.Outcome.Failed, Is.False, $"The verbose search was refused: {verbose.Outcome.Text}");
            Assert.That(verbose.Aliases.Select(alias => alias.Name), Is.EquivalentTo(names));
            Assert.That(
                verbose.Aliases.Select(alias => alias.Category),
                Has.All.Not.Empty,
                "The verbose record names the category the entry was found in.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnonymousAddIsRefusedWithTheSecurityAdminText(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            IReadOnlyList<PlantEntry> plant = await Model.LoadPlantAsync(ct).ConfigureAwait(false);

            AliasCategoryChoice plantTags = AliasNamesClientModel.Categories[1];

            Outcome refused = await Model.AddAliasAsync(plant[0], plantTags, "XX999_PV", ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Adding anonymously: {refused.Text}").ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(refused.Failed, Is.True, "An anonymous session must not be allowed to add a tag.");
                Assert.That(refused.Text, Does.Contain("SecurityAdmin"), "The refusal has to say what is needed.");
            });

            AliasSearchResult after = await Model.SearchAsync(plantTags, "XX%", verbose: false, ct).ConfigureAwait(false);

            Assert.That(after.Aliases, Is.Empty, "The refused call changed something.");

            // and the checks the window relies on before anything is sent
            Outcome noNode = await Model.AddAliasAsync(null, plantTags, "XX999_PV", ct).ConfigureAwait(false);
            Outcome noName = await Model.AddAliasAsync(plant[0], plantTags, "  ", ct).ConfigureAwait(false);

            Assert.That(noNode.Text, Does.Contain("select a node"));
            Assert.That(noName.Text, Does.Contain("type a tag name"));
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task SecurityAdminAddsFindsAndDeletes(CancellationToken ct)
        {
            const string tagName = "LIC104_PV";

            await using TestClient admin = await ConnectAsync(
                AliasNamesClientModel.IdentityFor(AliasNamesClientModel.SecurityAdmin),
                useSecurity: true,
                "secadmin encrypted",
                ct).ConfigureAwait(false);

            await using var model = new AliasNamesClientModel(NullTelemetry.Instance);

            await model.AttachAsync(admin.Session, ct).ConfigureAwait(false);

            IReadOnlyList<PlantEntry> plant = await model.LoadPlantAsync(ct).ConfigureAwait(false);

            PlantEntry pressure = plant.FirstOrDefault(entry => entry.Path.EndsWith("/PressureMeasurement", StringComparison.Ordinal));

            Assert.That(pressure, Is.Not.Null, "The reactor has no pressure measurement.");

            AliasCategoryChoice plantTags = AliasNamesClientModel.Categories[1];

            AliasSearchResult before = await model.SearchAsync(plantTags, "LIC%", verbose: false, ct).ConfigureAwait(false);

            Assume.That(before.Aliases, Is.Empty, "A previous run left the tag behind.");

            Outcome added = await model.AddAliasAsync(pressure, plantTags, tagName, ct).ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"Adding as a SecurityAdmin: {added.Text}").ConfigureAwait(false);

            Assert.That(added.Failed, Is.False, $"A SecurityAdmin on an encrypted channel may add a tag: {added.Text}");

            try
            {
                // the inventory may have changed, which is what the window does before it
                // re-reads the plant
                model.Invalidate();

                AliasSearchResult found = await model.SearchAsync(plantTags, "LIC%", verbose: true, ct).ConfigureAwait(false);

                AliasEntry alias = found.Aliases.FirstOrDefault(candidate => candidate.Name == tagName);

                Assert.That(alias, Is.Not.Null, "The new tag is not findable straight away.");
                Assert.That(alias.ResolvesTo, Is.EqualTo(pressure.NodeId.ToString()), "The tag stands for the wrong node.");
                Assert.That(found.LastChange, Is.Not.EqualTo(before.LastChange), "Changing the tag list has to advance LastChange.");

                // and the plant names the node by it now
                IReadOnlyList<PlantEntry> renamed = await model.LoadPlantAsync(ct).ConfigureAwait(false);

                await TestContext.Out
                    .WriteLineAsync("The pressure measurement now answers to: " + renamed.Single(entry => entry.NodeId == pressure.NodeId).TagName)
                    .ConfigureAwait(false);

                Outcome deleted = await model.DeleteAliasAsync(alias, plantTags, ct).ConfigureAwait(false);

                Assert.That(deleted.Failed, Is.False, $"Deleting the tag again was refused: {deleted.Text}");
            }
            finally
            {
                // the fixture is left as it was found, whatever the assertions said
                AliasSearchResult leftover = await model.SearchAsync(plantTags, "LIC%", verbose: false, ct).ConfigureAwait(false);

                foreach (AliasEntry alias in leftover.Aliases)
                {
                    await model.DeleteAliasAsync(alias, plantTags, ct).ConfigureAwait(false);
                }
            }

            AliasSearchResult after = await model.SearchAsync(plantTags, "LIC%", verbose: false, ct).ConfigureAwait(false);

            Assert.That(after.Aliases, Is.Empty, "The tag is gone again.");
        }
    }
}
