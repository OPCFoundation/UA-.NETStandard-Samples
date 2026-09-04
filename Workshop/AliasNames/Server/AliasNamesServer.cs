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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Samples.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.AliasNames;

namespace Quickstarts.AliasNames.Server
{
    /// <summary>
    /// A Quickstart server which demonstrates OPC UA Part 17, Alias Names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Part 17 answers one question: a client knows a name a human gave a signal, and needs
    /// the NodeId that name stands for. Browsing cannot answer it, because the name is not in
    /// the address space - the plant is laid out by structure, and the tag names come from the
    /// control system. So the server publishes a second, searchable index of names beside the
    /// address space, and a client searches it by wildcard.
    /// </para>
    /// <para>
    /// The alias inventory lives in an <see cref="IAliasNameStore"/>. This server builds two
    /// of them, because Part 17 offers two ways to publish one and they behave differently:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///   <b>The standard well known categories.</b> <see cref="ConfigureStandardTagVariables"/>
    ///   seeds a store against <see cref="Opc.Ua.ObjectIds.TagVariables"/> - the <c>TagVariables</c>
    ///   object (<c>i=23479</c>) every server already has, from Part 17 §9.3 - and registers
    ///   it with the server-wide <see cref="IAliasNameStoreRegistry"/>. The
    ///   <c>DiagnosticsNodeManager</c> already binds <c>TagVariables.FindAlias</c>, so from
    ///   that one call the standard node starts answering. A client needs no prior knowledge
    ///   of this server at all: <c>AliasNameClient.OpenStandardTagVariables</c> knows the
    ///   NodeId from the specification. What the published NodeSet does not contain is the
    ///   optional Methods and an <c>AliasNameType</c> node per alias, so
    ///   <see cref="AliasNamesConfigurationNodeManager"/> materializes them from the
    ///   registered store while the address space is built.
    ///   </item>
    ///   <item>
    ///   <b>An application defined category tree.</b>
    ///   <see cref="ConfigurePlantCategories"/> builds a store whose root is a category of
    ///   this sample's own, with one nested sub-category per unit of the plant, and hands it
    ///   to an <see cref="AliasNameNodeManager"/>. That node manager owns a namespace and
    ///   creates the <c>AliasNameCategoryType</c> nodes for the tree - browsable, nested, and
    ///   carrying the optional Part 17 Methods <c>FindAliasVerbose</c>,
    ///   <c>AddAliasesToCategory</c> and <c>DeleteAliasesFromCategory</c> - without any
    ///   materialization call of the server's own.
    ///   </item>
    /// </list>
    /// <para>
    /// Both stores serve the same tags, so the sample client can put the two side by side and
    /// show what each way can and cannot do.
    /// </para>
    /// <para>
    /// This is the one workshop sample with a server class of its own. The plant categories,
    /// the SecurityAdmin Role of the sample account and the way that account logs in are all
    /// registered on the server builder of the stack, in <c>AliasNamesServerHosting</c>. The
    /// standard category is the exception: its alias nodes are materialized by the
    /// configuration node manager while the address space is built, so its store has to be
    /// registered before that - and the store registrations of the stack
    /// (<c>AddAliasNameStore</c>) are made after the node managers have started. Until the
    /// stack materializes registered stores itself (UA-.NETStandard #4410), the two hooks
    /// below are what it takes.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Sample server type name intentionally mirrors the namespace.")]
    public class AliasNamesServer : SampleServer
    {
        #region Constants
        /// <summary>
        /// The namespace the application defined alias categories live in.
        /// </summary>
        /// <remarks>
        /// <see cref="AliasNameNodeManager"/> refuses to create a category whose NodeId lies
        /// outside the namespace it owns - it will not mint node ids in a namespace another
        /// node manager or the standard NodeSet is responsible for - so the descriptors below
        /// and <see cref="AliasNameNodeManagerOptions.NamespaceUri"/> have to agree.
        /// </remarks>
        public const string CategoryNamespaceUri = "http://opcfoundation.org/Quickstarts/AliasNames/Categories";

        /// <summary>
        /// The browse name and identifier of the root category of the plant.
        /// </summary>
        public const string PlantTagsCategoryName = "PlantTags";

        /// <summary>
        /// The browse name and identifier of the reactor sub-category.
        /// </summary>
        public const string ReactorCategoryName = "Reactor";

        /// <summary>
        /// The browse name and identifier of the boiler sub-category.
        /// </summary>
        public const string BoilerCategoryName = "Boiler";

        /// <summary>
        /// The account which is granted the SecurityAdmin Role.
        /// </summary>
        /// <remarks>
        /// Part 17 §6.3.4/§6.3.5 leave the authorization of the mutation Methods to the
        /// server, and the stack takes the strict reading: <c>AddAliasesToCategory</c> and
        /// <c>DeleteAliasesFromCategory</c> are refused unless the caller holds
        /// <c>WellKnownRole_SecurityAdmin</c> on a <c>SignAndEncrypt</c> channel. One account
        /// which passes that test, and an anonymous session which does not, is all the sample
        /// needs to show both answers.
        /// </remarks>
        public const string SecurityAdminUser = "secadmin";
        #endregion

        #region Constructors
        /// <summary>
        /// Creates the server. Its node managers, roles and authenticators are registered
        /// with the host by the composition root of the sample.
        /// </summary>
        public AliasNamesServer(
            IServiceProvider services,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
            : base(services, telemetry, timeProvider)
        {
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// The NodeId of one of the categories of the sample, in the category namespace.
        /// </summary>
        /// <remarks>
        /// The categories carry a string identifier rather than a numeric one, so the
        /// identifier a client sees is the category name and a reader can recognise it in a
        /// log. Only the namespace index has to be looked up, which is why the id is built
        /// from an <see cref="ExpandedNodeId"/> carrying the uri.
        /// </remarks>
        public static NodeId CategoryId(string categoryName, NamespaceTable namespaceUris)
        {
            return ExpandedNodeId.ToNodeId(
                new ExpandedNodeId(categoryName, 0, CategoryNamespaceUri, 0),
                namespaceUris);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Registers the store which serves the standard TagVariables category, and hands the
        /// server a configuration node manager which materializes it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the first hook which sees the running server, and it runs before any node
        /// manager builds its address space - which is what the standard categories need. The
        /// binder of <c>DiagnosticsNodeManager</c> queries the registry at every call, so a
        /// store registered later would still answer <c>FindAlias</c>; but the nodes of §6.2
        /// are created once, while the address space is built, so the store has to be there
        /// by then.
        /// </para>
        /// </remarks>
        protected override IMainNodeManagerFactory CreateMainNodeManagerFactory(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            m_standardTags = ConfigureStandardTagVariables();

            ((IAliasNameStoreRegistryProvider)server)
                .AliasNameStoreRegistry
                .Register(m_standardTags);

            return new AliasNamesMainNodeManagerFactory(
                base.CreateMainNodeManagerFactory(server, configuration),
                server,
                configuration);
        }

        /// <summary>
        /// Disposes the alias stores.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // the store of the application defined categories belongs to its node
                // manager, which the master node manager disposes. This one has no owner but
                // the server, because it was registered rather than handed to a node manager.
                m_standardTags?.Dispose();
                m_standardTags = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Builds the store which answers FindAlias on the standard TagVariables object.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Part 17 §9.3 reserves <c>TagVariables (i=23479)</c> for exactly this: the aliases
        /// of a server which name its process variables. The node is part of the standard
        /// address space, so it is already there and already has a <c>FindAlias</c> Method -
        /// registering a store is the whole of the server side work.
        /// </para>
        /// <para>
        /// What the published NodeSet does not instantiate is the rest: the optional Methods
        /// and the <c>AliasNameType</c> nodes §6.2 clients browse for. Those are created by
        /// the materialization pass which <see cref="AliasNamesConfigurationNodeManager"/>
        /// runs, and the capabilities declared here are what that pass creates - so the
        /// standard category ends up with the same repertoire as the application defined ones
        /// below.
        /// </para>
        /// </remarks>
        private static InMemoryAliasNameStore ConfigureStandardTagVariables()
        {
            var tagVariables = new AliasNameCategoryDescriptor(
                Opc.Ua.ObjectIds.TagVariables,
                new QualifiedName(Opc.Ua.BrowseNames.TagVariables),
                AliasNameCapabilities.All);

            var store = new InMemoryAliasNameStore(new[] { tagVariables });

            foreach (PlantTag tag in PlantTags.All)
            {
                Seed(store, Opc.Ua.ObjectIds.TagVariables, tag);
            }

            return store;
        }

        /// <summary>
        /// Builds the store behind the application defined category tree of the plant.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The tree mirrors the plant: a <c>PlantTags</c> root with a <c>Reactor</c> and a
        /// <c>Boiler</c> sub-category below it. Part 17 §6.3.1 allows that nesting precisely
        /// so a client can narrow a search to the part of an installation it cares about
        /// instead of filtering thousands of names itself.
        /// </para>
        /// <para>
        /// Every category declares <see cref="AliasNameCapabilities.All"/>, so the node
        /// manager creates the optional Methods and the <c>LastChange</c> property on each of
        /// them. Mutating any of the three still needs a SecurityAdmin on an encrypted
        /// channel - declaring a capability says the Method exists, not that anyone may call
        /// it.
        /// </para>
        /// <para>
        /// Each tag is seeded into its unit only. A search of the <c>PlantTags</c> root still
        /// finds all of them, because the store searches a category together with everything
        /// below it - so a client which knows how the plant is divided narrows the search, and
        /// one which does not asks the root and gets the whole inventory.
        /// </para>
        /// </remarks>
        internal static InMemoryAliasNameStore ConfigurePlantCategories(ushort namespaceIndex)
        {
            var reactor = new AliasNameCategoryDescriptor(
                new NodeId(ReactorCategoryName, namespaceIndex),
                new QualifiedName(ReactorCategoryName, namespaceIndex),
                AliasNameCapabilities.All);

            var boiler = new AliasNameCategoryDescriptor(
                new NodeId(BoilerCategoryName, namespaceIndex),
                new QualifiedName(BoilerCategoryName, namespaceIndex),
                AliasNameCapabilities.All);

            var plant = new AliasNameCategoryDescriptor(
                new NodeId(PlantTagsCategoryName, namespaceIndex),
                new QualifiedName(PlantTagsCategoryName, namespaceIndex),
                AliasNameCapabilities.All,
                new[] { reactor, boiler });

            var store = new InMemoryAliasNameStore(new[] { plant });

            foreach (PlantTag tag in PlantTags.Reactor)
            {
                Seed(store, reactor.NodeId, tag);
            }

            foreach (PlantTag tag in PlantTags.Boiler)
            {
                Seed(store, boiler.NodeId, tag);
            }

            return store;
        }

        /// <summary>
        /// Puts one tag of the plant into a category of a store.
        /// </summary>
        /// <remarks>
        /// <c>AliasFor</c> (Part 17 §8.2) is the reference type which says "this name stands
        /// for that node". A store may hold an entry under any reference type - a client can
        /// filter <c>FindAlias</c> by one - but only <c>AliasFor</c> and its subtypes are the
        /// alias association of §6.2, so that is what a tag list uses.
        /// </remarks>
        private static void Seed(InMemoryAliasNameStore store, NodeId categoryId, PlantTag tag)
        {
            store.Seed(
                categoryId,
                tag.Alias,
                tag.Target,

                // no server uri: the targets are nodes of this server. Part 17 allows an
                // alias to point into another server, and the verbose record carries the uri
                // which says so.
                serverUri: null,
                referenceTypeId: ReferenceTypeIds.AliasFor);
        }
        #endregion

        #region Private Fields
        private InMemoryAliasNameStore m_standardTags;
        #endregion
    }

    /// <summary>
    /// Hands the server a <see cref="AliasNamesConfigurationNodeManager"/> instead of the
    /// standard one, and leaves everything else to the factory the base server built.
    /// </summary>
    internal sealed class AliasNamesMainNodeManagerFactory : IMainNodeManagerFactory
    {
        private readonly IMainNodeManagerFactory m_inner;
        private readonly IServerInternal m_server;
        private readonly ApplicationConfiguration m_configuration;

        public AliasNamesMainNodeManagerFactory(
            IMainNodeManagerFactory inner,
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            m_inner = inner;
            m_server = server;
            m_configuration = configuration;
        }

        /// <inheritdoc/>
        public IConfigurationNodeManager CreateConfigurationNodeManager()
            => new AliasNamesConfigurationNodeManager(m_server, m_configuration);

        /// <inheritdoc/>
        public ICoreNodeManager CreateCoreNodeManager(ushort namespaceIndex)
            => m_inner.CreateCoreNodeManager(namespaceIndex);
    }

    /// <summary>
    /// The node manager which owns the standard address space, asked to publish the aliases
    /// of the registered stores as nodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ConfigurationNodeManager</c> loads <c>Opc.Ua.NodeSet2.xml</c>, which brings the
    /// three well known categories of Part 17 §9 with their mandatory <c>FindAlias</c> and
    /// nothing else. <c>MaterializeRegisteredAliasNameNodesAsync</c> is the opt-in pass which
    /// creates the rest: the optional Methods a category's
    /// <see cref="AliasNameCapabilities"/> declare, and one <c>AliasNameType</c> node per
    /// alias, which is how a §6.2 client - the OPC Foundation CTT among them - discovers an
    /// alias by browsing rather than by calling <c>FindAlias</c>.
    /// </para>
    /// <para>
    /// The nodes are a snapshot taken here. A tag added through
    /// <c>AddAliasesToCategory</c> afterwards is found by <c>FindAlias</c> and advances
    /// <c>LastChange</c>, but gets no node until the server is restarted.
    /// </para>
    /// </remarks>
    internal sealed class AliasNamesConfigurationNodeManager : ConfigurationNodeManager
    {
        public AliasNamesConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(server, configuration)
        {
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            // the standard categories have to exist before they can be filled in
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            await MaterializeRegisteredAliasNameNodesAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates the <see cref="AliasNameNodeManager"/> which publishes the application defined
    /// category tree of the sample.
    /// </summary>
    /// <remarks>
    /// The alias node manager ships with the stack, so the sample has nothing to implement -
    /// only to say which store it serves and which namespace it owns.
    /// </remarks>
    public sealed class AliasNameCategoryNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
            => new ArrayOf<string>(new string[] { AliasNamesServer.CategoryNamespaceUri });

        /// <inheritdoc/>
        /// <remarks>
        /// The store is seeded here because a category descriptor carries the NodeId and the
        /// BrowseName the category will really have, and both need the index the server gave
        /// the category namespace. Part 17 clients compare alias names ignoring the namespace,
        /// but a BrowseName left in namespace zero would claim a name in the one reserved for
        /// OPC Foundation defined names, so the descriptors take the sample's own.
        /// </remarks>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            ushort namespaceIndex = server.NamespaceUris.GetIndexOrAppend(
                AliasNamesServer.CategoryNamespaceUri);

#pragma warning disable CA2000 // Justification: store and node manager ownership is transferred to the master node manager.
            return new ValueTask<IAsyncNodeManager>(new AliasNameNodeManager(
                server,
                configuration,
                AliasNamesServer.ConfigurePlantCategories(namespaceIndex),
                new AliasNameNodeManagerOptions {
                    // the categories of this sample live in a namespace of its own, so their
                    // node ids can never collide with the model's or the standard NodeSet's
                    NamespaceUri = AliasNamesServer.CategoryNamespaceUri,

                    // organizes the PlantTags root under the standard Aliases object
                    // (i=23470), so a client which does not know this sample still finds the
                    // tree by browsing the place Part 17 §9.2 tells it to look
                    LinkToStandardAliasesObject = true,

                    // the mutation Methods answer BadUserAccessDenied unless the caller holds
                    // SecurityAdmin on an encrypted channel. This is the default; the sample
                    // sets it to say out loud that it is not turning the check off.
                    RequireSecurityAdminForMutations = true,

                    // the standard TagVariables store is registered separately by the server
                    // class; this store serves categories of the sample's own, and
                    // registering it as well is what lets a client reach them through the
                    // server-wide registry rather than only through this node manager.
                    RegisterWithServerRegistry = true,
                }));
#pragma warning restore CA2000
        }
    }
}
