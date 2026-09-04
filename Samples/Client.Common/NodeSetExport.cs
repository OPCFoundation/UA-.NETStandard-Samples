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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// What a NodeSet2 export collected and wrote.
    /// </summary>
    /// <param name="FilePath">The file the NodeSet2 XML was written to.</param>
    /// <param name="NodeCount">How many nodes the export contains.</param>
    /// <param name="NamespaceUris">The namespace URIs the exported nodes belong to,
    /// without the OPC UA namespace, which a NodeSet2 file never declares.</param>
    public sealed record NodeSetExportResult(
        string FilePath,
        int NodeCount,
        IReadOnlyList<string> NamespaceUris);

    /// <summary>
    /// How much of the address space an export walks and what it writes for each node.
    /// </summary>
    /// <remarks>
    /// The two switches which matter to a user of a sample client are how far the browse
    /// reaches and whether the nodes of the OPC UA namespace come along; the rest of the
    /// knobs belong to the stack and are passed through as
    /// <see cref="NodeSetExportOptions"/>.
    /// </remarks>
    public sealed record NodeSetExportSettings
    {
        /// <summary>
        /// The node the browse starts at. Defaults to the Objects folder.
        /// </summary>
        public NodeId StartNodeId { get; init; } = ObjectIds.ObjectsFolder;

        /// <summary>
        /// Whether the start node itself is part of the export.
        /// </summary>
        public bool IncludeStartNode { get; init; } = true;

        /// <summary>
        /// Whether the browse follows the hierarchy below the start node, or stops after
        /// its immediate children.
        /// </summary>
        public bool FetchTree { get; init; } = true;

        /// <summary>
        /// Whether the nodes of the OPC UA namespace are skipped.
        /// </summary>
        /// <remarks>
        /// A NodeSet2 file never declares namespace 0 - every consumer already has the
        /// standard address space - so the nodes of the server, and only those, are what
        /// an export is normally after.
        /// </remarks>
        public bool ExcludeStandardNamespace { get; init; } = true;

        /// <summary>
        /// How many nodes the browse collects at most, which bounds an export of a server
        /// whose address space is larger than a sample wants to walk. Zero means no bound.
        /// </summary>
        public int MaxNodeCount { get; init; } = 100000;

        /// <summary>
        /// What is written for each node. Defaults to
        /// <see cref="NodeSetExportOptions.Default"/>, which leaves out the values.
        /// </summary>
        public NodeSetExportOptions NodeOptions { get; init; } = NodeSetExportOptions.Default;

        /// <summary>
        /// The version written into the NodeSet2 model header, or null for none.
        /// </summary>
        public string Version { get; init; }
    }

    /// <summary>
    /// Exports the address space of a connected server to a NodeSet2 XML file.
    /// </summary>
    /// <remarks>
    /// The stack does the writing: <c>CoreClientUtils.ExportNodesToNodeSet2</c>
    /// turns the client side <see cref="INode"/> representations a browse produced into
    /// the server side <c>NodeState</c> representations the NodeSet2 encoder wants. What
    /// a client has to bring is the list of nodes, which is what this class collects - a
    /// hierarchical browse from a start node, served out of the node cache of the session
    /// so that a node reachable over more than one path is fetched once.
    /// </remarks>
    public static class NodeSetExport
    {
        /// <summary>
        /// Collects the nodes below a start node and writes them to a NodeSet2 file.
        /// </summary>
        /// <param name="session">The connected session to export from.</param>
        /// <param name="filePath">The file to write the NodeSet2 XML to.</param>
        /// <param name="settings">What to collect and what to write; the defaults when null.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<NodeSetExportResult> ExportToFileAsync(
            ISession session,
            string filePath,
            NodeSetExportSettings settings = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(filePath);

            settings ??= new NodeSetExportSettings();

            IList<INode> nodes = await CollectNodesAsync(session, settings, ct).ConfigureAwait(false);

            // the file is opened only once the browse succeeded, so a failed export does
            // not leave a truncated file behind where one already existed.
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Export(session, nodes, stream, settings);
            }

            return new NodeSetExportResult(filePath, nodes.Count, CollectNamespaceUris(session, nodes));
        }

        /// <summary>
        /// Writes an already collected set of nodes to a stream in NodeSet2 XML.
        /// </summary>
        /// <param name="session">The session the nodes were read from, which carries the
        /// namespace and server tables the node ids are resolved against.</param>
        /// <param name="nodes">The nodes to write.</param>
        /// <param name="outputStream">The stream to write to.</param>
        /// <param name="settings">What to write for each node; the defaults when null.</param>
        public static void Export(
            ISession session,
            IList<INode> nodes,
            Stream outputStream,
            NodeSetExportSettings settings = null)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(nodes);
            ArgumentNullException.ThrowIfNull(outputStream);

            settings ??= new NodeSetExportSettings();

            // the node ids of the collected nodes are indexes into the tables of the
            // session, so the context the encoder resolves them against has to be the
            // tables of that session and not the default ones.
            var context = new SystemContext(session.MessageContext?.Telemetry) {
                NamespaceUris = session.NamespaceUris,
                ServerUris = session.ServerUris,
            };

            CoreClientUtils.ExportNodesToNodeSet2(
                context,
                nodes,
                outputStream,
                settings.NodeOptions ?? NodeSetExportOptions.Default,
                settings.Version,
                DateTime.UtcNow);
        }

        /// <summary>
        /// Browses the hierarchy below the start node and returns the nodes it reaches.
        /// </summary>
        /// <remarks>
        /// The walk follows every hierarchical reference and their subtypes, which is what
        /// spans the instances and the types a server declares below the start node. It is
        /// breadth first over the node cache: the cache is what turns the second path to a
        /// node into a lookup instead of another read, and the visited set is what keeps a
        /// cyclic hierarchy - which the specification allows - from looping.
        /// </remarks>
        public static async Task<IList<INode>> CollectNodesAsync(
            ISession session,
            NodeSetExportSettings settings = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);

            settings ??= new NodeSetExportSettings();

            NodeId startNodeId = settings.StartNodeId.IsNull ? ObjectIds.ObjectsFolder : settings.StartNodeId;

            var nodes = new List<INode>();
            var visited = new HashSet<NodeId>();
            var pending = new Queue<NodeId>();

            visited.Add(startNodeId);

            if (settings.IncludeStartNode)
            {
                INode startNode = await session.NodeCache.FindAsync(startNodeId, ct).ConfigureAwait(false);

                if (startNode != null && Accept(startNode, settings))
                {
                    nodes.Add(startNode);
                }
            }

            pending.Enqueue(startNodeId);

            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                NodeId nodeId = pending.Dequeue();

                ArrayOf<INode> children = await session.NodeCache.GetReferencesAsync(
                    nodeId,
                    ReferenceTypeIds.HierarchicalReferences,
                    false,
                    true,
                    ct).ConfigureAwait(false);

                foreach (INode child in children)
                {
                    // a node of another server is outside the address space this export
                    // describes, and its node id cannot be resolved against this session.
                    NodeId childId = ExpandedNodeId.ToNodeId(child.NodeId, session.NamespaceUris);

                    if (childId.IsNull || !visited.Add(childId))
                    {
                        continue;
                    }

                    if (Accept(child, settings))
                    {
                        nodes.Add(child);

                        if (settings.MaxNodeCount > 0 && nodes.Count >= settings.MaxNodeCount)
                        {
                            return nodes;
                        }
                    }

                    // the hierarchy below a node of the standard namespace is walked even
                    // when the node itself is skipped: a server may organize its own nodes
                    // below a standard folder.
                    if (settings.FetchTree)
                    {
                        pending.Enqueue(childId);
                    }
                }
            }

            return nodes;
        }

        /// <summary>
        /// Whether a browsed node is part of the export.
        /// </summary>
        private static bool Accept(INode node, NodeSetExportSettings settings)
        {
            if (node == null)
            {
                return false;
            }

            return !settings.ExcludeStandardNamespace || node.NodeId.NamespaceIndex != 0;
        }

        /// <summary>
        /// The namespace URIs the exported nodes belong to, which is what a caller reports
        /// so that a user can tell an export of a server model from an empty one.
        /// </summary>
        private static IReadOnlyList<string> CollectNamespaceUris(ISession session, IList<INode> nodes)
        {
            var uris = new List<string>();

            foreach (INode node in nodes)
            {
                ushort namespaceIndex = node.NodeId.NamespaceIndex;

                if (namespaceIndex == 0)
                {
                    continue;
                }

                string uri = session.NamespaceUris.GetString(namespaceIndex);

                if (uri != null && !uris.Contains(uri))
                {
                    uris.Add(uri);
                }
            }

            return uris;
        }
    }
}
