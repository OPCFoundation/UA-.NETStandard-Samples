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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Samples.Client;

namespace Quickstarts.AliasNames.Client.Model
{
    // The source generator emits a Quickstarts.AliasNames.BrowseNames for the model of the
    // server. This namespace is a child of that one, so it would win over the standard set of
    // the same name: both are named apart here.
    using ModelNames = Quickstarts.AliasNames.BrowseNames;
    using ObjectIds = Opc.Ua.ObjectIds;

    /// <summary>
    /// One category the client can search.
    /// </summary>
    /// <param name="Label">What the user sees.</param>
    /// <param name="CategoryName">
    /// The identifier of one of the server's own categories, or <c>null</c> for the
    /// standard TagVariables object.
    /// </param>
    public sealed record AliasCategoryChoice(string Label, string CategoryName)
    {
        /// <inheritdoc/>
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// One signal of the plant, found by structure, and the tag name the alias inventory
    /// knows it by.
    /// </summary>
    /// <param name="Path">The unit and the signal, as browsed.</param>
    /// <param name="NodeId">The node of the signal.</param>
    /// <param name="Value">Its value, or the status code of a refused read.</param>
    /// <param name="TagName">The alias name of the node, or empty when the inventory does not name it.</param>
    public sealed record PlantEntry(string Path, NodeId NodeId, string Value, string TagName);

    /// <summary>
    /// One search result: an alias name and the node it stands for.
    /// </summary>
    /// <param name="Name">The alias name.</param>
    /// <param name="Target">The node the alias stands for, or null when it has none.</param>
    /// <param name="ResolvesTo">The target rendered as a node id of this server, or empty.</param>
    /// <param name="Value">The value of the target, or the status code of a refused read, or empty.</param>
    /// <param name="Category">The category the entry came from; only a verbose search fills it.</param>
    /// <param name="ServerUris">The servers a remote target lives on; only a verbose search fills it.</param>
    public sealed record AliasEntry(
        string Name,
        ExpandedNodeId Target,
        string ResolvesTo,
        string Value,
        string Category,
        string ServerUris);

    /// <summary>
    /// What one operation of the model answered, ready for the status bar.
    /// </summary>
    /// <param name="What">What was attempted, for instance "Adding an alias".</param>
    /// <param name="Text">The whole line the status bar shows.</param>
    /// <param name="Failed">True when the line reports a refusal or a failure.</param>
    public sealed record Outcome(string What, string Text, bool Failed)
    {
        /// <summary>
        /// What the server answered to an operation.
        /// </summary>
        public static Outcome Of(string what, StatusCode status)
        {
            return new Outcome(what, $"{what} answered {status}", StatusCode.IsBad(status));
        }

        /// <summary>
        /// A plain message about an operation.
        /// </summary>
        public static Outcome Message(string what, string text)
        {
            return new Outcome(what, $"{what}: {text}", false);
        }

        /// <summary>
        /// Why the alias name client refused or could not complete a call.
        /// </summary>
        /// <remarks>
        /// <see cref="AliasNameClient"/> maps the Part 17 status codes onto ordinary .NET
        /// exceptions, so the refusals this sample is meant to show up arrive as
        /// <see cref="UnauthorizedAccessException"/> and
        /// <see cref="NotSupportedException"/> rather than as status codes. They are turned
        /// back into a line of text here, because the refusal is the outcome the sample is
        /// about, not an error.
        /// </remarks>
        public static Outcome Refusal(string what, Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            string text = exception switch {
                UnauthorizedAccessException => "refused - this needs the SecurityAdmin role on an encrypted channel",
                NotSupportedException => "this category does not expose that method",
                ServiceResultException service => service.StatusCode.ToString(),
                _ => exception.Message,
            };

            return new Outcome(what, $"{what}: {text}", true);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Text;
        }
    }

    /// <summary>
    /// What one search of a category found.
    /// </summary>
    /// <param name="Aliases">The entries which matched, with the value of each target.</param>
    /// <param name="LastChange">The VersionTime of the category, rendered, or why there is none.</param>
    /// <param name="Outcome">How many were found, or why the search was refused.</param>
    public sealed record AliasSearchResult(
        IReadOnlyList<AliasEntry> Aliases,
        string LastChange,
        Outcome Outcome);

    /// <summary>
    /// The client model of the OPC UA Part 17 alias names Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model puts the two ways of finding a node side by side, because Part 17 only
    /// makes sense as the difference between them.
    /// </para>
    /// <para>
    /// <see cref="LoadPlantAsync"/> is the plant as an ordinary client sees it: the result
    /// of browsing down from the Objects folder. Every entry is a node found by structure,
    /// and its tag name is what that node answers to - which the client did not browse for,
    /// because the name is not in the address space. It asked an
    /// <see cref="AliasNameResolver"/> to map the node back to a name.
    /// </para>
    /// <para>
    /// <see cref="SearchAsync"/> runs the other way, and is the reason a client would use
    /// Part 17 at all: given a name, or a wildcard over names, which nodes are meant? Pick a
    /// category, give a pattern, and the server answers from an index it keeps beside the
    /// address space. Nothing in the search knows how the plant is laid out.
    /// </para>
    /// <para>
    /// The two mutations show the other half of the specification: a tag list is
    /// configuration, not a compile time constant. They are offered to every account on
    /// purpose, because seeing the server refuse an anonymous session is as much a part of
    /// the sample as seeing it succeed for an administrator - which is why they answer an
    /// <see cref="Outcome"/> instead of throwing on a refusal.
    /// </para>
    /// </remarks>
    public sealed class AliasNamesClientModel : SampleClientModel
    {
        /// <summary>
        /// The account which opens an anonymous Session.
        /// </summary>
        public const string Anonymous = "Anonymous";

        /// <summary>
        /// The account of the sample server which holds the SecurityAdmin Role.
        /// </summary>
        public const string SecurityAdmin = "secadmin";

        /// <summary>
        /// The two accounts of the sample server. Which one is signed in decides only
        /// whether the mutation Methods are allowed - searching is open to everyone.
        /// </summary>
        public static IReadOnlyList<string> Accounts { get; } = new[] { Anonymous, SecurityAdmin };

        /// <summary>
        /// The categories this client can search. The standard one needs no prior
        /// knowledge of the server; the other three are this server's own.
        /// </summary>
        public static IReadOnlyList<AliasCategoryChoice> Categories { get; } = new[] {
            new AliasCategoryChoice("TagVariables (standard, i=23479)", null),
            new AliasCategoryChoice("PlantTags (application defined)", AliasCategories.PlantTags),
            new AliasCategoryChoice("PlantTags/Reactor", AliasCategories.Reactor),
            new AliasCategoryChoice("PlantTags/Boiler", AliasCategories.Boiler),
        };

        private AliasNameResolver m_resolver;
        private IReadOnlyList<PlantEntry> m_plant = Array.Empty<PlantEntry>();

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public AliasNamesClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// The plant as the last <see cref="LoadPlantAsync"/> found it.
        /// </summary>
        public IReadOnlyList<PlantEntry> Plant => m_plant;

        /// <summary>
        /// The identity token which signs in as one of the <see cref="Accounts"/>.
        /// </summary>
        /// <param name="account">One of the <see cref="Accounts"/>.</param>
        /// <returns>The identity, or null for an anonymous Session.</returns>
        public static IUserIdentity IdentityFor(string account)
        {
            return string.IsNullOrEmpty(account) || string.Equals(account, Anonymous, StringComparison.Ordinal)
                ? null
                : new UserIdentity(account, Encoding.UTF8.GetBytes(account));
        }

        /// <summary>
        /// Explains what an account may do.
        /// </summary>
        /// <param name="account">One of the <see cref="Accounts"/>.</param>
        public static string HintFor(string account)
        {
            return string.Equals(account, SecurityAdmin, StringComparison.Ordinal)
                ? "SecurityAdmin: may add and delete aliases, over an encrypted channel."
                : "Anonymous: may search the tag list, and is refused every change to it.";
        }

        /// <summary>
        /// Drops the cached reverse mapping, because the inventory may have changed.
        /// </summary>
        public void Invalidate()
        {
            m_resolver?.Invalidate();
        }

        /// <summary>
        /// Browses the plant, and names each signal from the alias inventory.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The signals, which are also kept in <see cref="Plant"/>.</returns>
        public async Task<IReadOnlyList<PlantEntry>> LoadPlantAsync(CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var plant = new List<PlantEntry>();

            NodeId plantId = await ResolveModelNodeAsync(session, ModelNames.Plant, ct).ConfigureAwait(false);

            if (plantId.IsNull)
            {
                m_plant = plant;
                return plant;
            }

            foreach (ReferenceDescription unit in
                await BrowseAsync(session, plantId, NodeClass.Object, ct).ConfigureAwait(false))
            {
                NodeId unitId = ExpandedNodeId.ToNodeId(unit.NodeId, session.NamespaceUris);

                foreach (ReferenceDescription signal in
                    await BrowseAsync(session, unitId, NodeClass.Variable, ct).ConfigureAwait(false))
                {
                    NodeId signalId = ExpandedNodeId.ToNodeId(signal.NodeId, session.NamespaceUris);

                    DataValue value = await ReadAsync(session, signalId, ct).ConfigureAwait(false);

                    // the column the sample is about: the node was found by structure, and
                    // this is the name the alias inventory knows it by. Nothing was browsed
                    // to get it - the name does not exist in the address space.
                    string tagName = await ResolveAliasNameAsync(session, signalId, ct).ConfigureAwait(false);

                    plant.Add(new PlantEntry(
                        $"{unit.BrowseName.Name}/{signal.BrowseName.Name}",
                        signalId,
                        Render(value),
                        tagName));
                }
            }

            m_plant = plant;

            return plant;
        }

        /// <summary>
        /// Searches a category for a pattern.
        /// </summary>
        /// <remarks>
        /// A refused search is an outcome rather than an error: the result carries an
        /// <see cref="Outcome"/> with <see cref="Outcome.Failed"/> set and no entries.
        /// </remarks>
        /// <param name="category">The category to search.</param>
        /// <param name="pattern">The pattern, with the wildcards of Part 17 6.3.2; empty for everything.</param>
        /// <param name="verbose">Whether to use FindAliasVerbose, which also names the category and the server of each entry.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<AliasSearchResult> SearchAsync(
            AliasCategoryChoice category,
            string pattern,
            bool verbose,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(category);

            ISession session = RequireSession();

            if (string.IsNullOrEmpty(pattern))
            {
                pattern = "%";
            }

            var aliases = new List<AliasEntry>();
            string what = verbose ? $"FindAliasVerbose('{pattern}')" : $"FindAlias('{pattern}')";

            try
            {
                AliasNameClient client = OpenCategory(session, category);

                string lastChange = await ReadLastChangeAsync(client, ct).ConfigureAwait(false);

                if (verbose)
                {
                    IReadOnlyList<AliasNameVerboseDataType> found = await client
                        .FindAliasVerboseAsync(pattern, null, ct)
                        .ConfigureAwait(false);

                    foreach (AliasNameVerboseDataType alias in found)
                    {
                        aliases.Add(await DescribeAsync(
                            session,
                            alias.AliasName.Name,
                            alias.ReferencedNodes.ToArray(),
                            NameOfCategory(alias.AliasNameCategoryId),
                            alias.ServerUris.ToArray(),
                            ct).ConfigureAwait(false));
                    }
                }
                else
                {
                    IReadOnlyList<AliasNameDataType> found = await client
                        .FindAliasAsync(pattern, null, ct)
                        .ConfigureAwait(false);

                    foreach (AliasNameDataType alias in found)
                    {
                        aliases.Add(await DescribeAsync(
                            session,
                            alias.AliasName.Name,
                            alias.ReferencedNodes.ToArray(),

                            // FindAlias does not say which category an entry came from, nor
                            // whether its target is remote. That is what the verbose variant
                            // is for, and the two empty columns are the difference.
                            string.Empty,
                            Array.Empty<string>(),
                            ct).ConfigureAwait(false));
                    }
                }

                return new AliasSearchResult(
                    aliases,
                    lastChange,
                    Outcome.Message(what, $"{aliases.Count} alias(es)"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new AliasSearchResult(aliases, string.Empty, Outcome.Refusal("Searching", exception));
            }
        }

        /// <summary>
        /// Gives a node of the plant another tag name.
        /// </summary>
        /// <remarks>
        /// The target is one of the plant entries rather than typed, because that is the
        /// realistic direction: an engineer looking at a signal decides what the control
        /// system calls it. The name goes into whichever category was picked.
        /// </remarks>
        /// <param name="node">The signal the name is for.</param>
        /// <param name="category">The category to put the name in.</param>
        /// <param name="name">The tag name.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<Outcome> AddAliasAsync(
            PlantEntry node,
            AliasCategoryChoice category,
            string name,
            CancellationToken ct = default)
        {
            const string what = "Adding an alias";

            ISession session = RequireSession();

            if (node == null)
            {
                return Outcome.Message(what, "select a node in the plant first");
            }

            name = name?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                return Outcome.Message(what, "type a tag name first");
            }

            ArgumentNullException.ThrowIfNull(category);

            try
            {
                var request = new AliasNameAddRequest(
                    name,
                    NodeId.ToExpandedNodeId(node.NodeId, session.NamespaceUris),

                    // no server uri: the target is a node of the server being talked to
                    null,

                    // the Part 17 8.2 reference type which says "this name stands for that
                    // node". A store may hold other reference types, but only this one is an
                    // alias association.
                    ReferenceTypeIds.AliasFor);

                IReadOnlyList<StatusCode> results = await OpenCategory(session, category)
                    .AddAliasesToCategoryAsync(new[] { request }, ct)
                    .ConfigureAwait(false);

                return Outcome.Of($"Adding '{name}' for {node.Path}", results[0]);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Outcome.Refusal(what, exception);
            }
        }

        /// <summary>
        /// Removes an alias from a category.
        /// </summary>
        /// <param name="alias">The search result to remove.</param>
        /// <param name="category">The category it is in.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<Outcome> DeleteAliasAsync(
            AliasEntry alias,
            AliasCategoryChoice category,
            CancellationToken ct = default)
        {
            const string what = "Deleting an alias";

            ISession session = RequireSession();

            if (alias == null)
            {
                return Outcome.Message(what, "select a search result first");
            }

            ArgumentNullException.ThrowIfNull(category);

            try
            {
                IReadOnlyList<StatusCode> results = await OpenCategory(session, category)
                    .DeleteAliasesFromCategoryAsync(
                        new[] { new AliasNameDeleteRequest(alias.Name, alias.Target) },
                        ct)
                    .ConfigureAwait(false);

                return Outcome.Of($"Deleting '{alias.Name}'", results[0]);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Outcome.Refusal(what, exception);
            }
        }

        /// <inheritdoc/>
        protected override Task OnAttachedAsync(CancellationToken ct)
        {
            // the reverse mapping of the plant. The standard category holds every tag of
            // the plant, so one resolver over it answers for the whole address space.
            //
            // The refresh mode is left at its default, Manual: the resolver then loads the
            // inventory once and reloads it when this model asks. The automatic modes are
            // worth having when a server's tag list changes under a long lived client, but
            // they cost a poll or a subscription, and this model re-reads on every refresh
            // anyway.
            m_resolver = new AliasNameResolver(AliasNameClient.OpenStandardTagVariables(RequireSession()));

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override async Task OnDetachingAsync()
        {
            AliasNameResolver resolver = m_resolver;

            m_resolver = null;
            m_plant = Array.Empty<PlantEntry>();

            if (resolver != null)
            {
                // disposal of a resolver in the Manual refresh mode completes without doing
                // any I/O - there is no timer and no subscription to unwind - so it is safe
                // against a session which is already gone
                await resolver.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Describes one search result, with the value of its target.
        /// </summary>
        private static async Task<AliasEntry> DescribeAsync(
            ISession session,
            string name,
            IReadOnlyList<ExpandedNodeId> targets,
            string category,
            IReadOnlyList<string> serverUris,
            CancellationToken ct)
        {
            ExpandedNodeId target = targets.Count > 0 ? targets[0] : ExpandedNodeId.Null;

            string resolvesTo = string.Empty;
            string value = string.Empty;

            if (!target.IsNull)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(target, session.NamespaceUris);

                resolvesTo = nodeId.IsNull ? target.ToString() : nodeId.ToString();

                // reading the target is what proves the search answered with a usable
                // address rather than just a matching string
                value = Render(nodeId.IsNull
                    ? DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown)
                    : await ReadAsync(session, nodeId, ct).ConfigureAwait(false));
            }

            return new AliasEntry(
                name,
                target,
                resolvesTo,
                value,
                category,
                string.Join(", ", serverUris.Where(uri => !string.IsNullOrEmpty(uri))));
        }

        /// <summary>
        /// The VersionTime of a category, rendered, or why there is none.
        /// </summary>
        /// <remarks>
        /// Part 17 6.3.1. A client which caches an inventory watches this instead of
        /// re-reading the whole list: it changes whenever the category does. A category
        /// which does not expose the property at all - the well known ones of a server
        /// which does not materialize its store - reports none, which is itself worth
        /// seeing in the field.
        /// </remarks>
        private static async Task<string> ReadLastChangeAsync(AliasNameClient category, CancellationToken ct)
        {
            try
            {
                uint? lastChange = await category.ReadLastChangeAsync(ct).ConfigureAwait(false);

                return lastChange.HasValue
                    ? $"LastChange {lastChange.Value}"
                    : "no LastChange";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return "no LastChange";
            }
        }

        /// <summary>
        /// The alias name of a node, or an empty string when the inventory does not name it.
        /// </summary>
        private async Task<string> ResolveAliasNameAsync(ISession session, NodeId nodeId, CancellationToken ct)
        {
            AliasNameResolver resolver = m_resolver;

            if (resolver == null)
            {
                return string.Empty;
            }

            try
            {
                return await resolver
                    .ResolveAliasNameAsync(NodeId.ToExpandedNodeId(nodeId, session.NamespaceUris), ct)
                    .ConfigureAwait(false)
                    ?? string.Empty;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // a node the inventory does not cover is an ordinary outcome, not an error
                return string.Empty;
            }
        }

        /// <summary>
        /// Opens a category.
        /// </summary>
        /// <remarks>
        /// The standard entry costs no round trip at all - <c>OpenStandardTagVariables</c>
        /// knows the NodeId of the category and of its FindAlias Method from Part 17 9.3.
        /// An application defined category is addressed by the namespace uri and identifier
        /// the server publishes it under.
        /// </remarks>
        private static AliasNameClient OpenCategory(ISession session, AliasCategoryChoice choice)
        {
            if (choice.CategoryName == null)
            {
                return AliasNameClient.OpenStandardTagVariables(session);
            }

            return new AliasNameClient(
                session,
                AliasCategories.NodeIdOf(choice.CategoryName, session.NamespaceUris));
        }

        /// <summary>
        /// The readable name of a category NodeId reported by a verbose search.
        /// </summary>
        private static string NameOfCategory(NodeId categoryId)
        {
            if (categoryId.IsNull)
            {
                return string.Empty;
            }

            // the categories of this server carry their name as a string identifier, which
            // reads better than the whole node id
            return categoryId.IdentifierAsString;
        }

        /// <summary>
        /// Follows one browse name of the model from the Objects folder.
        /// </summary>
        private static async Task<NodeId> ResolveModelNodeAsync(ISession session, string browseName, CancellationToken ct)
        {
            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(Quickstarts.AliasNames.Namespaces.AliasNames);

            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                ObjectIds.ObjectsFolder,
                wellKnownNamespaceUris,
                ct,
                "1:" + browseName).ConfigureAwait(false);

            return nodes.Count > 0 ? nodes[0] : NodeId.Null;
        }

        /// <summary>
        /// Browses the hierarchical children of a node of one node class.
        /// </summary>
        private static async Task<IReadOnlyList<ReferenceDescription>> BrowseAsync(
            ISession session,
            NodeId nodeId,
            NodeClass nodeClass,
            CancellationToken ct)
        {
            var browse = new BrowseDescription {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)nodeClass,
                ResultMask = (uint)BrowseResultMask.All,
            };

            BrowseResponse response = await session
                .BrowseAsync(null, null, 0, new List<BrowseDescription> { browse }, ct)
                .ConfigureAwait(false);

            return response.Results.ToArray()[0].References.ToArray();
        }

        /// <summary>
        /// Reads the value of a node without throwing on a bad status code.
        /// </summary>
        private static async Task<DataValue> ReadAsync(ISession session, NodeId nodeId, CancellationToken ct)
        {
            var valuesToRead = new List<ReadValueId> {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
            };

            ReadResponse response = await session
                .ReadAsync(null, 0, TimestampsToReturn.Both, valuesToRead, ct)
                .ConfigureAwait(false);

            return response.Results.ToArray()[0];
        }

        /// <summary>
        /// A value for a column: the value itself, or the status code of a refused read.
        /// </summary>
        private static string Render(DataValue value)
        {
            return StatusCode.IsGood(value.StatusCode)
                ? value.WrappedValue.ToString()
                : value.StatusCode.ToString();
        }
    }
}
