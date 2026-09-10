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
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Quickstarts.RuntimeNodeSets.Server;

namespace Quickstarts.RuntimeNodeSets.Site
{
    /// <summary>
    /// A source-generated node manager which overlays two NodeSet2 documents onto the
    /// model it was generated from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the other half of the sample. The vendor model next door is a document
    /// the server publishes as a node manager of its own
    /// (<c>AddRuntimeNodeSet</c>): the two address spaces never share a node. Here the
    /// model is compiled in - <c>ModelDesign.xml</c>, generated <c>StationState</c>,
    /// generated identifiers - and the documents in <c>NodeSets/Site.*.NodeSet2.xml</c>
    /// are written <em>into that model's namespace</em>, filling in placeholder slots
    /// the OEM left for the integrator.
    /// </para>
    /// <para>
    /// The <c>[NodeManager]</c> attribute is what opts the class in to source
    /// generation. The emitted partial derives from <c>FluentNodeManagerBase</c>, loads
    /// the model, calls <see cref="Configure"/>, and - the part which matters here -
    /// implements <c>INodeSetImportFactoryProvider</c> by delegating to the generated
    /// <c>QuickstartsRuntimeNodeSetsSiteNodeSetImportFactoryProvider</c>. That is how an
    /// imported node whose TypeDefinition is <c>StationType</c> comes out of the
    /// importer as a <see cref="StationState"/> and not as a bare
    /// <c>BaseObjectState</c>: one factory per model type, each calling a concrete
    /// constructor, nothing looked up by reflection. <c>Import</c> also takes a provider
    /// directly, for the factories of a model this manager does not own.
    /// </para>
    /// <para>
    /// The class is declared in the generated model namespace rather than in
    /// <c>Quickstarts.RuntimeNodeSets.Server</c> so that <see cref="StationState"/>,
    /// <see cref="Objects"/> and the other generated names are in scope unqualified.
    /// </para>
    /// </remarks>
    [NodeManager]
    public partial class SiteNodeManager
    {
        private readonly RuntimeNodeSetLibrary m_library;
        private StationState m_station1;
        private StationState m_station3;
        private BaseDataVariableState[] m_throughput;
        private BaseDataVariableState m_station3Temperature;
        private double m_parts;

        /// <summary>
        /// Initializes the node manager over the directory of NodeSet2 documents the
        /// sample ships.
        /// </summary>
        /// <remarks>
        /// The generated constructor takes the server and the configuration only. This
        /// one adds the library, and <see cref="SiteOverlayNodeManagerFactory"/> is what
        /// lets the container hand it over - the same library object the runtime half of
        /// the sample loads its documents through.
        /// </remarks>
        /// <param name="server">The server the node manager belongs to.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="library">The NodeSet2 documents the sample ships.</param>
        /// <param name="namespaceUris">
        /// The namespaces the manager owns, or null for the ones the model declares. The
        /// parameter exists so that this constructor is not a second three argument
        /// candidate next to the generated one, whose last parameter is a
        /// <c>string[]</c>: the generated parameterless overload chains with a literal
        /// <c>null</c>, which would match both.
        /// </param>
        public SiteNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            RuntimeNodeSetLibrary library,
            string[] namespaceUris = null)
            : this(server, configuration, namespaceUris)
        {
            m_library = library ?? throw new ArgumentNullException(nameof(library));
        }

        /// <summary>
        /// The station the overlay put in the first slot of the generated model,
        /// replacing the placeholder.
        /// </summary>
        public StationState ReplacedStation => m_station1;

        /// <summary>
        /// The station the overlay added below <c>Site</c>, which the model does not
        /// declare at all.
        /// </summary>
        public StationState AddedStation => m_station3;

        /// <summary>
        /// Imports the overlay documents and wires the behaviour of the nodes they
        /// brought in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Order is the one rule of this method: <b>import, then wire</b>. Every
        /// document imported during one <c>Configure</c> pass is a single batch which
        /// the manager links exactly once after the pass returns, and a child which
        /// lands in a slot the generated parent declares replaces the placeholder that
        /// sat there. Wiring a placeholder first and importing over it afterwards would
        /// silently throw the wiring away, so the builder refuses it instead:
        /// </para>
        /// <code>
        /// // don't: Station1 is about to be displaced by the overlay
        /// builder.Variable&lt;double&gt;("Site/Station1/Throughput").OnRead(...);
        /// builder.Import(layout);
        /// // BadInvalidState, from CreateAddressSpaceAsync:
        /// // "Imported node '3:Throughput' replaces configured node 'ns=3;i=102'.
        /// //  Import the NodeSet before wiring that node."
        /// </code>
        /// <para>
        /// The imported nodes resolve through <c>Node(...)</c> immediately, even though
        /// they are only registered with the manager once this method returns, so
        /// everything below the import call reads as ordinary fluent wiring. They are
        /// resolved <em>by NodeId</em> rather than by browse path for the same reason the
        /// order matters: until the batch is linked, the browse path of a replaced node
        /// still leads to the placeholder.
        /// </para>
        /// <para>
        /// Nothing here asserts that the import factories did their work, because the
        /// wiring already does. <c>Node&lt;StationState&gt;</c> answers
        /// <c>BadTypeMismatch</c> when the node is something else, so a server whose
        /// imported stations came out as bare <c>BaseObjectState</c>s would not start.
        /// </para>
        /// </remarks>
        /// <param name="builder">The builder for this node manager.</param>
        partial void Configure(INodeManagerBuilder builder)
        {
            // 1. the overlay. Two documents, one batch: Site.Instrumentation names a
            //    parent which only exists in Site.Layout, and a parent - Site itself -
            //    which this manager already owns. Both resolve because linking waits
            //    for the whole batch.
            foreach (UANodeSet document in m_library.ReadOverlayDocuments())
            {
                builder.Import(document);
            }

            // 2. the imported nodes, as the generated types the import factories made
            //    them. Station1 is the replacement for the placeholder the model
            //    declares at ns;i=101; Station3 has no counterpart in the model.
            m_station1 = builder.Node<StationState>(SiteId(SiteOverlayIds.Station1)).Node;
            m_station3 = builder.Node<StationState>(SiteId(SiteOverlayIds.Station3)).Node;

            // the variables below them are ordinary BaseDataVariableStates: an import
            // factory matches an Object or a Variable by its TypeDefinition, and the
            // documents give these the base BaseDataVariableType. Only the stations,
            // whose TypeDefinition is the model's StationType, are matched - which is
            // also why StationState.Throughput stays null on an imported station and
            // the variables are held here instead.
            m_throughput =
            [
                builder.Node<BaseDataVariableState>(SiteId(SiteOverlayIds.Station1_Throughput)).Node,
                builder.Node<BaseDataVariableState>(SiteId(SiteOverlayIds.Station3_Throughput)).Node,
                builder.Node<BaseDataVariableState>(SiteId(Variables.Site_Station2_Throughput)).Node,
            ];
            m_station3Temperature = builder
                .Node<BaseDataVariableState>(SiteId(SiteOverlayIds.Station3_Temperature))
                .Node;

            // 3. behaviour on an imported node and on a generated one, wired the same
            //    way. Station3/Reset carries a MethodDeclarationId of StationType/Reset,
            //    so the importer built it as the generated ResetMethodState.
            builder.Node<ResetMethodState>(SiteId(SiteOverlayIds.Station3_Reset)).OnCall(OnResetAsync);
            builder.Node<ResetMethodState>(SiteId(Methods.Site_Station2_Reset)).OnCall(OnResetAsync);

            // 4. something to watch. An imported variable is an ordinary node of this
            //    manager once the batch is linked.
            builder.Simulation(TimeSpan.FromSeconds(1)).OnTick((context, elapsed) => Simulate());
        }

        /// <summary>
        /// Moves the values of the site so a Client has something to subscribe to.
        /// </summary>
        private void Simulate()
        {
            m_parts += 1;

            for (int ii = 0; ii < m_throughput.Length; ii++)
            {
                Publish(m_throughput[ii], 40.0 + ((m_parts + ii) % 5));
            }

            Publish(m_station3Temperature, 120.0 + (m_parts % 7));
        }

        /// <summary>
        /// Writes a value to a variable of this manager, imported or generated.
        /// </summary>
        private void Publish(BaseVariableState variable, double value)
        {
            variable.WrappedValue = new Variant(value);
            variable.ClearChangeMasks(SystemContext, false);
        }

        /// <summary>
        /// Answers <c>Reset</c> on any station, imported or generated.
        /// </summary>
        private async ValueTask<ServiceResult> OnResetAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            if (inputArguments.Count != 1 ||
                !inputArguments[0].TryGetValue(out bool clearFaults))
            {
                return StatusCodes.BadArgumentsMissing;
            }

            // the station a Reset belongs to is its parent, whether the model declared
            // that station or a document brought it in: both are StationState.
            if (method.Parent is StationState station &&
                station.FindChild(SystemContext, BrowseNameOf("Status"))
                    is BaseVariableState status)
            {
                status.WrappedValue = new Variant(clearFaults ? "Idle" : "Idle (faults latched)");
                await status
                    .ClearChangeMasksAsync(SystemContext, false, cancellationToken)
                    .ConfigureAwait(false);
            }

            // the SDK sizes the list from the OutputArguments the Method declares, so a
            // handler assigns rather than appends.
            outputArguments[0] = Variant.From(true);

            return ServiceResult.Good;
        }

        /// <summary>
        /// A NodeId in the namespace of the site model.
        /// </summary>
        private NodeId SiteId(uint identifier)
        {
            return new NodeId(identifier, NamespaceIndexes[0]);
        }

        /// <summary>
        /// A BrowseName in the namespace of the site model.
        /// </summary>
        private QualifiedName BrowseNameOf(string name)
        {
            return new QualifiedName(name, NamespaceIndexes[0]);
        }
    }

    /// <summary>
    /// Hands the node manager the NodeSet2 documents of the sample.
    /// </summary>
    /// <remarks>
    /// The generator emits <see cref="SiteNodeManagerFactory"/> itself, and its
    /// <c>CreateAsync</c> calls the two-argument constructor. Deriving from it is the
    /// supported way to give a generated node manager a dependency: the composition root
    /// registers this one instead, and the container resolves the library the runtime
    /// half of the sample already put there.
    /// </remarks>
    public sealed class SiteOverlayNodeManagerFactory : SiteNodeManagerFactory
    {
        private readonly RuntimeNodeSetLibrary m_library;

        /// <summary>
        /// Initializes the factory.
        /// </summary>
        /// <param name="library">The NodeSet2 documents the sample ships.</param>
        public SiteOverlayNodeManagerFactory(RuntimeNodeSetLibrary library)
        {
            m_library = library ?? throw new ArgumentNullException(nameof(library));
        }

        /// <inheritdoc/>
        public override ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: node manager ownership is transferred to the server.
            return ValueTask.FromResult<IAsyncNodeManager>(
                new SiteNodeManager(server, configuration, m_library));
#pragma warning restore CA2000
        }
    }
}
