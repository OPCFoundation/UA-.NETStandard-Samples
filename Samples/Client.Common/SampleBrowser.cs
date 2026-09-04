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
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// What a browse tree asks the server, without the tree.
    /// </summary>
    /// <remarks>
    /// A tree which shows the address space of a server does two things a window cannot do
    /// for it: it browses the children of a node under the reference types it follows, and
    /// it reads the icon a type definition may publish. Turning what comes back into tree
    /// nodes and images is the window's half.
    /// </remarks>
    public static class SampleBrowser
    {
        /// <summary>
        /// Browses the children of a node under a set of reference types.
        /// </summary>
        /// <remarks>
        /// References which leave the server are skipped - a tree cannot follow them without
        /// a second session - and so is a node which more than one of the reference types
        /// leads to, so that a child appears once however many ways there are to reach it.
        /// A browse which fails returns nothing rather than throwing: a node the server will
        /// not let a client browse is a normal thing to run into while expanding a tree.
        /// </remarks>
        /// <param name="session">The session to browse on.</param>
        /// <param name="view">The view to browse in, or null for the whole address space.</param>
        /// <param name="nodeId">The node whose children to browse.</param>
        /// <param name="referenceTypeIds">The reference types to follow, subtypes included.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<List<ReferenceDescription>> BrowseChildrenAsync(
            ISession session,
            ViewDescription view,
            NodeId nodeId,
            IReadOnlyList<NodeId> referenceTypeIds,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(referenceTypeIds);

            var nodesToBrowse = new List<BrowseDescription>(referenceTypeIds.Count);

            foreach (NodeId referenceTypeId in referenceTypeIds)
            {
                nodesToBrowse.Add(new BrowseDescription {
                    NodeId = nodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All,
                });
            }

            List<ReferenceDescription> references = await SampleSession
                .BrowseAsync(session, view, nodesToBrowse, false, ct)
                .ConfigureAwait(false);

            var children = new List<ReferenceDescription>();

            if (references == null)
            {
                return children;
            }

            var seen = new HashSet<ExpandedNodeId>();

            foreach (ReferenceDescription reference in references)
            {
                if (reference.NodeId.IsAbsolute || !seen.Add(reference.NodeId))
                {
                    continue;
                }

                children.Add(reference);
            }

            return children;
        }

        /// <summary>
        /// Reads the icon a type definition publishes, or null when it publishes none.
        /// </summary>
        /// <remarks>
        /// Part 5 lets a type definition carry an <c>Icon</c> property holding an image, so
        /// that a client can show the symbol the vendor intended rather than the generic one
        /// for the node class. This returns the bytes; decoding them into an image is the
        /// caller's business, and so is caching the result - the read is one round trip per
        /// type and a tree runs into the same types over and over.
        /// </remarks>
        /// <param name="session">The session to read on.</param>
        /// <param name="typeDefinitionId">The type definition to read the icon of.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<ByteString> ReadTypeIconAsync(
            ISession session,
            NodeId typeDefinitionId,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);

            if (typeDefinitionId.IsNull)
            {
                return default;
            }

            List<NodeId> nodeIds = await SampleSession
                .TranslateBrowsePathsAsync(session, typeDefinitionId, session.NamespaceUris, ct, Opc.Ua.BrowseNames.Icon)
                .ConfigureAwait(false);

            if (nodeIds.Count == 0 || nodeIds[0].IsNull)
            {
                return default;
            }

            DataValue value = await session.ReadValueAsync(nodeIds[0], ct).ConfigureAwait(false);

            return value.WrappedValue.TryGetValue(out ByteString bytes) ? bytes : default;
        }
    }
}
