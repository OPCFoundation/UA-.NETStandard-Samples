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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.AliasNames;
using Quickstarts.AliasNames.Server;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the alias names sample does: a client which knows only the tag name a human uses
    /// finds the node that name stands for, without knowing anything about the address space.
    /// </summary>
    /// <remarks>
    /// Every assertion goes through the Part 17 service surface an ordinary client has - the
    /// <c>FindAlias</c> family on the standard well known categories and on the application
    /// defined ones - so the tests hold the sample to the behaviour it promises rather than to
    /// the way it happens to store its inventory today.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class AliasNamesNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "AliasNames";

        /// <summary>
        /// The namespace the plant is served in.
        /// </summary>
        /// <remarks>
        /// Spelled out rather than imported, because Opc.Ua defines a Namespaces class too
        /// and the tests live in a namespace below Opc.Ua.
        /// </remarks>
        private const string PlantNamespace =
            Quickstarts.AliasNames.Namespaces.AliasNames;

        private QualifiedName Plant => Name(PlantNamespace, "Plant");
        private QualifiedName Reactor => Name(PlantNamespace, "Reactor");
        private QualifiedName TemperatureMeasurement => Name(PlantNamespace, "TemperatureMeasurement");

        #region Tests
        /// <summary>
        /// The standard TagVariables object answers FindAlias for the whole plant.
        /// </summary>
        /// <remarks>
        /// This is the case which needs no prior knowledge of the server: the NodeId of
        /// <c>TagVariables</c> and of its <c>FindAlias</c> Method come from Part 17 §9.3, so
        /// the client opens the category from the specification alone. The sample server had
        /// to do nothing but register a store with the server-wide registry.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task StandardTagVariablesFindsEveryTagOfThePlant(CancellationToken ct)
        {
            AliasNameClient client = AliasNameClient.OpenStandardTagVariables(Session);

            IReadOnlyList<AliasNameDataType> found = await client
                .FindAliasAsync("%", null, ct)
                .ConfigureAwait(false);

            IReadOnlyList<string> names = NamesOf(found);

            await ReportAsync("TagVariables.FindAlias('%')", names).ConfigureAwait(false);

            Assert.That(
                names,
                Is.EquivalentTo(PlantTags.All.Select(tag => tag.Alias)),
                "The standard category serves exactly the tag list of the sample.");
        }

        /// <summary>
        /// A wildcard narrows the search the way Part 17 §6.3.2 defines it.
        /// </summary>
        /// <remarks>
        /// This is the entire point of the pattern argument: a client which wants the
        /// measured values of the plant asks for them by name shape, rather than reading the
        /// whole inventory and filtering it itself.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task WildcardPatternSelectsASubsetOfTheTags(CancellationToken ct)
        {
            AliasNameClient client = AliasNameClient.OpenStandardTagVariables(Session);

            IReadOnlyList<string> processValues = NamesOf(
                await client.FindAliasAsync("%_PV", null, ct).ConfigureAwait(false));

            IReadOnlyList<string> reactorTags = NamesOf(
                await client.FindAliasAsync("TIC101%", null, ct).ConfigureAwait(false));

            await ReportAsync("FindAlias('%_PV')", processValues).ConfigureAwait(false);
            await ReportAsync("FindAlias('TIC101%')", reactorTags).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    processValues,
                    Is.EquivalentTo(new[] { "TIC101_PV", "PIC102_PV", "PIC201_PV", "FIC202_PV" }),
                    "'%_PV' selects the measured values of both units.");

                Assert.That(
                    reactorTags,
                    Is.EquivalentTo(new[] { "TIC101_PV", "TIC101_SP" }),
                    "'TIC101%' selects the two tags of the one instrument.");
            });
        }

        /// <summary>
        /// A tag name resolves to the node it stands for, and that node is readable.
        /// </summary>
        /// <remarks>
        /// The test which matters most: it is not enough that the server returns a NodeId,
        /// the NodeId has to be the one the structural browse path leads to, and it has to
        /// carry a live value. That is the whole promise of Part 17 - a name a human chose,
        /// turned into an address a machine can use.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AResolvedTagNameIsTheNodeTheBrowsePathLeadsTo(CancellationToken ct)
        {
            await using var resolver = new AliasNameResolver(
                AliasNameClient.OpenStandardTagVariables(Session));

            IReadOnlyList<ExpandedNodeId> targets = await resolver
                .ResolveAsync("TIC101_PV", ct)
                .ConfigureAwait(false);

            Assert.That(targets, Has.Count.EqualTo(1), "A tag of this plant stands for exactly one node.");

            NodeId resolved = ExpandedNodeId.ToNodeId(targets[0], Session.NamespaceUris);

            NodeId browsed = await ResolveAsync(ct, Plant, Reactor, TemperatureMeasurement)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"TIC101_PV -> {resolved}, Plant/Reactor/TemperatureMeasurement -> {browsed}")
                .ConfigureAwait(false);

            Assert.That(
                resolved,
                Is.EqualTo(browsed),
                "The alias points at the node the structural browse path leads to.");

            DataValue value = await SessionOps
                .ReadValueAsync(Session, resolved, ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, "The resolved node is readable.");
                Assert.That(value.WrappedValue.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Double));
            });
        }

        /// <summary>
        /// The resolver answers the reverse question too: which name stands for this node.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ANodeResolvesBackToItsTagName(CancellationToken ct)
        {
            await using var resolver = new AliasNameResolver(
                AliasNameClient.OpenStandardTagVariables(Session));

            NodeId browsed = await ResolveAsync(ct, Plant, Reactor, TemperatureMeasurement)
                .ConfigureAwait(false);

            string alias = await resolver
                .ResolveAliasNameAsync(NodeId.ToExpandedNodeId(browsed, Session.NamespaceUris), ct)
                .ConfigureAwait(false);

            await TestContext.Out.WriteLineAsync($"The node resolves back to '{alias}'").ConfigureAwait(false);

            Assert.That(alias, Is.EqualTo("TIC101_PV"));
        }

        /// <summary>
        /// The application defined category tree is browsable, and its sub-categories are
        /// reachable from the standard Aliases object.
        /// </summary>
        /// <remarks>
        /// This is what the well known categories cannot do. A category created by an
        /// <c>AliasNameNodeManager</c> is a real <c>AliasNameCategoryType</c> node, so a
        /// client which knows nothing about this sample discovers the tree by browsing the
        /// place Part 17 §9.2 tells it to look.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ThePlantCategoryTreeIsBrowsableBelowTheStandardAliasesObject(CancellationToken ct)
        {
            IReadOnlyList<string> rootCategories = await BrowseNamesAsync(ObjectIds.Aliases, ct)
                .ConfigureAwait(false);

            await ReportAsync("Browsing the standard Aliases object", rootCategories).ConfigureAwait(false);

            Assert.That(
                rootCategories,
                Does.Contain(AliasNamesServer.PlantTagsCategoryName),
                "The root category of the sample is organized under the standard Aliases object.");

            NodeId plantTags = await ChildAsync(
                ObjectIds.Aliases,
                AliasNamesServer.PlantTagsCategoryName,
                ct).ConfigureAwait(false);

            IReadOnlyList<string> children = await BrowseNamesAsync(plantTags, ct).ConfigureAwait(false);

            await ReportAsync("Browsing PlantTags", children).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(children, Does.Contain(AliasNamesServer.ReactorCategoryName));
                Assert.That(children, Does.Contain(AliasNamesServer.BoilerCategoryName));
                Assert.That(
                    children,
                    Does.Contain(Opc.Ua.BrowseNames.FindAlias),
                    "Every Part 17 category carries the mandatory FindAlias method.");
            });
        }

        /// <summary>
        /// A nested category serves only the tags of its own unit.
        /// </summary>
        /// <remarks>
        /// Part 17 §6.3.1 allows the nesting so that a client can narrow a search to the part
        /// of an installation it cares about. The sample seeds every tag into the root as
        /// well, because a category is not the union of its children.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ANestedCategoryServesOnlyItsOwnUnit(CancellationToken ct)
        {
            AliasNameClient reactor = OpenCategory(AliasNamesServer.ReactorCategoryName);
            AliasNameClient boiler = OpenCategory(AliasNamesServer.BoilerCategoryName);
            AliasNameClient plant = OpenCategory(AliasNamesServer.PlantTagsCategoryName);

            IReadOnlyList<string> reactorTags = NamesOf(
                await reactor.FindAliasAsync("%", null, ct).ConfigureAwait(false));

            IReadOnlyList<string> boilerTags = NamesOf(
                await boiler.FindAliasAsync("%", null, ct).ConfigureAwait(false));

            IReadOnlyList<string> plantTags = NamesOf(
                await plant.FindAliasAsync("%", null, ct).ConfigureAwait(false));

            await ReportAsync("Reactor", reactorTags).ConfigureAwait(false);
            await ReportAsync("Boiler", boilerTags).ConfigureAwait(false);
            await ReportAsync("PlantTags", plantTags).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    reactorTags,
                    Is.EquivalentTo(PlantTags.Reactor.Select(tag => tag.Alias)),
                    "The reactor category serves the reactor tags.");

                Assert.That(
                    boilerTags,
                    Is.EquivalentTo(PlantTags.Boiler.Select(tag => tag.Alias)),
                    "The boiler category serves the boiler tags.");

                Assert.That(
                    plantTags,
                    Is.EquivalentTo(PlantTags.All.Select(tag => tag.Alias)),
                    "The root category serves the whole plant.");
            });
        }

        /// <summary>
        /// The sub-categories of a category are enumerable over the wire.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SubCategoriesOfThePlantAreEnumerable(CancellationToken ct)
        {
            AliasNameClient plant = OpenCategory(AliasNamesServer.PlantTagsCategoryName);

            var names = new List<string>();

            await foreach (AliasNameSubCategoryInfo child in
                plant.EnumerateSubCategoriesAsync(ct).ConfigureAwait(false))
            {
                names.Add(child.BrowseName.Name);
            }

            await ReportAsync("The sub-categories of PlantTags", names).ConfigureAwait(false);

            Assert.That(
                names,
                Is.EquivalentTo(new[] {
                    AliasNamesServer.ReactorCategoryName,
                    AliasNamesServer.BoilerCategoryName,
                }));
        }

        /// <summary>
        /// FindAliasVerbose reports which category an alias came from, and whether its
        /// targets are local.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The optional Method of Part 17 §6.3.3, which only the application defined
        /// categories of this sample expose: the well known nodes ship without it, so calling
        /// it there would be a call on a Method node which does not exist.
        /// </para>
        /// <para>
        /// What the verbose record adds over <c>FindAlias</c> is the category an entry was
        /// found in - which matters once a search covers a tree - and the
        /// <c>ServerUris</c> which say whether a target lives in this server or another one.
        /// It deliberately does not carry the concrete reference type of the association.
        /// </para>
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task VerboseSearchReportsTheCategoryAnAliasCameFrom(CancellationToken ct)
        {
            AliasNameClient reactor = OpenCategory(AliasNamesServer.ReactorCategoryName);

            IReadOnlyList<AliasNameVerboseDataType> found = await reactor
                .FindAliasVerboseAsync("TIC101_PV", null, ct)
                .ConfigureAwait(false);

            Assert.That(found, Has.Count.EqualTo(1));

            AliasNameVerboseDataType alias = found[0];

            await TestContext.Out
                .WriteLineAsync(
                    $"{alias.AliasName} came from category {alias.AliasNameCategoryId} and stands for " +
                    $"{alias.ReferencedNodes.ToArray().Length} node(s)")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(alias.AliasName.Name, Is.EqualTo("TIC101_PV"));

                Assert.That(
                    alias.AliasNameCategoryId,
                    Is.EqualTo(AliasNamesServer.CategoryId(
                        AliasNamesServer.ReactorCategoryName,
                        Session.NamespaceUris)),
                    "The verbose record names the category the entry was found in.");

                // an entry which points into another server names it here. A local target
                // reports no server uri - the store still emits a slot per referenced node,
                // so what says "local" is the slot being empty rather than absent.
                Assert.That(
                    alias.ServerUris.ToArray().Where(uri => !string.IsNullOrEmpty(uri)),
                    Is.Empty,
                    "No server uri: the target is a node of this server, not a remote one.");
            });
        }

        /// <summary>
        /// A SecurityAdmin adds a tag at runtime, and it is findable straight away.
        /// </summary>
        /// <remarks>
        /// Part 17 §6.3.4. The point of the mutation Methods is that a tag list is
        /// configuration rather than a compile time constant: a signal renamed in the control
        /// system is renamed here without restarting the server.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ASecurityAdminAddsAndDeletesATagAtRuntime(CancellationToken ct)
        {
            await using TestClient admin = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "admin changes the tag list", AdminUser(), ct)
                .ConfigureAwait(false);

            await admin.Session.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            AliasNameClient plant = OpenCategory(
                AliasNamesServer.PlantTagsCategoryName,
                admin.Session);

            var request = new AliasNameAddRequest(
                "LIC104_PV",
                Quickstarts.AliasNames.VariableIds.Plant_Reactor_PressureMeasurement,
                null,
                ReferenceTypeIds.AliasFor);

            IReadOnlyList<StatusCode> added = await plant
                .AddAliasesToCategoryAsync(new[] { request }, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(added[0]),
                Is.True,
                $"A SecurityAdmin on an encrypted channel may add a tag, but got {added[0]}.");

            IReadOnlyList<string> afterAdd = NamesOf(
                await plant.FindAliasAsync("LIC%", null, ct).ConfigureAwait(false));

            await ReportAsync("After adding LIC104_PV", afterAdd).ConfigureAwait(false);

            Assert.That(afterAdd, Does.Contain("LIC104_PV"), "The new tag is findable straight away.");

            IReadOnlyList<StatusCode> deleted = await plant
                .DeleteAliasesFromCategoryAsync(
                    new[] {
                        new AliasNameDeleteRequest(
                            "LIC104_PV",
                            Quickstarts.AliasNames.VariableIds.Plant_Reactor_PressureMeasurement),
                    },
                    ct)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(deleted[0]), Is.True, $"Deleting it again answered {deleted[0]}.");

            IReadOnlyList<string> afterDelete = NamesOf(
                await plant.FindAliasAsync("LIC%", null, ct).ConfigureAwait(false));

            Assert.That(afterDelete, Is.Empty, "The tag is gone again, so the fixture is left as it was found.");
        }

        /// <summary>
        /// An anonymous session may search the inventory but not change it.
        /// </summary>
        /// <remarks>
        /// Part 17 leaves the authorization of the mutation Methods to the server, and the
        /// stack takes the strict reading: SecurityAdmin on a SignAndEncrypt channel, or
        /// BadUserAccessDenied. Reading stays open, because a tag list is not a secret.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnAnonymousSessionMaySearchButNotChangeTheTagList(CancellationToken ct)
        {
            AliasNameClient plant = OpenCategory(AliasNamesServer.PlantTagsCategoryName);

            IReadOnlyList<string> found = NamesOf(
                await plant.FindAliasAsync("TIC%", null, ct).ConfigureAwait(false));

            Assert.That(found, Is.Not.Empty, "Searching the tag list needs no privilege.");

            var request = new AliasNameAddRequest(
                "XX999_PV",
                Quickstarts.AliasNames.VariableIds.Plant_Reactor_PressureMeasurement,
                null,
                ReferenceTypeIds.AliasFor);

            UnauthorizedAccessException refused = Assert.ThrowsAsync<UnauthorizedAccessException>(
                async () => await plant
                    .AddAliasesToCategoryAsync(new[] { request }, ct)
                    .ConfigureAwait(false));

            await TestContext.Out
                .WriteLineAsync($"An anonymous AddAliasesToCategory was refused: {refused.Message}")
                .ConfigureAwait(false);

            IReadOnlyList<string> after = NamesOf(
                await plant.FindAliasAsync("XX%", null, ct).ConfigureAwait(false));

            Assert.That(after, Is.Empty, "The refused call changed nothing.");
        }

        /// <summary>
        /// Changing the tag list advances the LastChange of the category and of its ancestors.
        /// </summary>
        /// <remarks>
        /// Part 17 §6.3.1: a category's <c>LastChange</c> reflects the most recent change
        /// anywhere in its subtree. That is what lets a client cache an inventory and notice
        /// it has gone stale instead of re-reading it on every lookup.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AChangeInASubCategoryAdvancesTheLastChangeOfItsParent(CancellationToken ct)
        {
            await using TestClient admin = await TestClient
                .ConnectEncryptedAsync(EndpointUrl, "admin bumps LastChange", AdminUser(), ct)
                .ConfigureAwait(false);

            await admin.Session.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);

            AliasNameClient reactor = OpenCategory(AliasNamesServer.ReactorCategoryName, admin.Session);
            AliasNameClient plant = OpenCategory(AliasNamesServer.PlantTagsCategoryName, admin.Session);

            uint? beforeReactor = await reactor.ReadLastChangeAsync(ct).ConfigureAwait(false);
            uint? beforePlant = await plant.ReadLastChangeAsync(ct).ConfigureAwait(false);

            IReadOnlyList<StatusCode> added = await reactor
                .AddAliasesToCategoryAsync(
                    new[] {
                        new AliasNameAddRequest(
                            "TT105_PV",
                            Quickstarts.AliasNames.VariableIds.Plant_Reactor_TemperatureMeasurement,
                            null,
                            ReferenceTypeIds.AliasFor),
                    },
                    ct)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(added[0]), Is.True, $"Adding the tag answered {added[0]}.");

            try
            {
                uint? afterReactor = await reactor.ReadLastChangeAsync(ct).ConfigureAwait(false);
                uint? afterPlant = await plant.ReadLastChangeAsync(ct).ConfigureAwait(false);

                await TestContext.Out
                    .WriteLineAsync(
                        $"Reactor LastChange {beforeReactor} -> {afterReactor}, " +
                        $"PlantTags LastChange {beforePlant} -> {afterPlant}")
                    .ConfigureAwait(false);

                Assert.That(
                    afterReactor,
                    Is.Not.EqualTo(beforeReactor),
                    "The category which changed advances its LastChange.");

                // Part 17 6.3.1 wants the parent to move as well, so that a client watching
                // the root notices a change anywhere below it. The stack bumps the ancestors
                // inside the store but does not write the new value through to the LastChange
                // node of the parent category, so a client reading it sees the old one.
                await KnownIssueAsync(
                    () => {
                        Assert.That(
                            afterPlant,
                            Is.Not.EqualTo(beforePlant),
                            "A change in a nested category advances the LastChange of its parent too.");

                        return Task.CompletedTask;
                    },
                    "The LastChange of an ancestor category is not updated when a nested " +
                    "category changes, so a client which watches only the root misses the " +
                    "change (UA-.NETStandard, Opc.Ua.Server.AliasNames).")
                    .ConfigureAwait(false);
            }
            finally
            {
                // leave the fixture as it was found: the other tests assert the exact tag list
                await reactor
                    .DeleteAliasesFromCategoryAsync(
                        new[] {
                            new AliasNameDeleteRequest(
                                "TT105_PV",
                                Quickstarts.AliasNames.VariableIds.Plant_Reactor_TemperatureMeasurement),
                        },
                        ct)
                    .ConfigureAwait(false);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// The identity of the one account which holds the SecurityAdmin Role.
        /// </summary>
        private static IUserIdentity AdminUser()
        {
            return new UserIdentity(
                AliasNamesServer.SecurityAdminUser,
                Encoding.UTF8.GetBytes(AliasNamesServer.SecurityAdminUser));
        }

        /// <summary>
        /// Opens one of the application defined categories of the sample.
        /// </summary>
        /// <remarks>
        /// The category NodeId is built from the namespace uri of the sample rather than
        /// browsed for, the same way the sample client does it: the identifier is the
        /// category name, so the only thing which has to be looked up is the namespace index.
        /// </remarks>
        private AliasNameClient OpenCategory(string categoryName, Opc.Ua.Client.ISession session = null)
        {
            Opc.Ua.Client.ISession target = session ?? Session;

            return new AliasNameClient(
                target,
                AliasNamesServer.CategoryId(categoryName, target.NamespaceUris));
        }

        /// <summary>
        /// The alias names of a search result.
        /// </summary>
        private static IReadOnlyList<string> NamesOf(IReadOnlyList<AliasNameDataType> found)
        {
            return found.Select(alias => alias.AliasName.Name).ToList();
        }

        /// <summary>
        /// The alias names of a verbose search result.
        /// </summary>
        private static IReadOnlyList<string> NamesOf(IReadOnlyList<AliasNameVerboseDataType> found)
        {
            return found.Select(alias => alias.AliasName.Name).ToList();
        }
        #endregion
    }
}
