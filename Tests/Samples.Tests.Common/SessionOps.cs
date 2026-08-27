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
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The OPC UA services a node manager test needs, as one call each.
    /// </summary>
    /// <remarks>
    /// These tests exist to survive a rewrite of the node managers, so they may only ever
    /// look at a sample through the services a real client would use. That rules out the
    /// convenience helpers of the client library where they hide the result of an
    /// operation: a test which asserts that a write is refused needs the status code, not
    /// an exception, and a test which asserts view filtering needs a Browse which actually
    /// carries a ViewDescription.
    /// </remarks>
    public static class SessionOps
    {
        /// <summary>
        /// Follows a browse path of hierarchical references from the Objects folder.
        /// </summary>
        public static Task<NodeId> ResolveAsync(
            ISession session,
            CancellationToken ct,
            params QualifiedName[] path)
        {
            return ResolveFromAsync(session, ObjectIds.ObjectsFolder, ct, path);
        }

        /// <summary>
        /// Follows a browse path of hierarchical references from the given node.
        /// </summary>
        /// <remarks>
        /// The path is given as browse names rather than as a string, because the sample
        /// address spaces contain names a relative path parser would choke on: "Boiler #1"
        /// and "My Process" both carry characters the syntax gives a meaning to.
        /// </remarks>
        /// <returns>The resolved node, or NodeId.Null when the path does not lead anywhere.</returns>
        public static async Task<NodeId> ResolveFromAsync(
            ISession session,
            NodeId startingNode,
            CancellationToken ct,
            params QualifiedName[] path)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(path);

            if (path.Length == 0)
            {
                throw new ArgumentException("A browse path needs at least one element.", nameof(path));
            }

            RelativePathElement[] elements = path
                .Select(name => new RelativePathElement {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = name,
                })
                .ToArray();

            var relativePath = new RelativePath { Elements = elements.ToArrayOf() };

            var browsePath = new BrowsePath {
                StartingNode = startingNode,
                RelativePath = relativePath,
            };

            var browsePaths = new List<BrowsePath> { browsePath };

            TranslateBrowsePathsToNodeIdsResponse response = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, browsePaths, ct)
                .ConfigureAwait(false);

            List<BrowsePathResult> results = response.Results.ToList();

            ClientBase.ValidateResponse(results, browsePaths);

            if (StatusCode.IsBad(results[0].StatusCode) || results[0].Targets.Count == 0)
            {
                return NodeId.Null;
            }

            BrowsePathTarget target = results[0].Targets[0];

            // a target which still has a remaining path is a partial match: the server got
            // part of the way and then ran out of nodes, which is not the node asked for
            if (target.RemainingPathIndex != uint.MaxValue)
            {
                return NodeId.Null;
            }

            return ExpandedNodeId.ToNodeId(target.TargetId, session.NamespaceUris);
        }

        /// <summary>
        /// Browses a node, optionally through a view.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodeId">The node to browse.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <param name="view">The view to browse through, or null for the whole address space.</param>
        /// <param name="referenceTypeId">The references to follow. Defaults to all hierarchical ones.</param>
        /// <param name="includeSubtypes">Whether subtypes of the reference type count too.</param>
        /// <param name="direction">The direction to browse in.</param>
        public static async Task<IReadOnlyList<ReferenceDescription>> BrowseAsync(
            ISession session,
            NodeId nodeId,
            CancellationToken ct,
            ViewDescription view = null,
            NodeId referenceTypeId = default,
            bool includeSubtypes = true,
            BrowseDirection direction = BrowseDirection.Forward)
        {
            ArgumentNullException.ThrowIfNull(session);

            var nodeToBrowse = new BrowseDescription {
                NodeId = nodeId,
                BrowseDirection = direction,
                ReferenceTypeId = referenceTypeId.IsNull ? ReferenceTypeIds.HierarchicalReferences : referenceTypeId,
                IncludeSubtypes = includeSubtypes,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All,
            };

            var nodesToBrowse = new List<BrowseDescription> { nodeToBrowse };

            BrowseResponse response = await session
                .BrowseAsync(null, view, 0, nodesToBrowse, ct)
                .ConfigureAwait(false);

            List<BrowseResult> results = response.Results.ToList();

            ClientBase.ValidateResponse(results, nodesToBrowse);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            var references = new List<ReferenceDescription>(results[0].References.ToArray());

            // follow continuation points, so a caller never has to wonder whether the list
            // it compares against is the whole answer
            ByteString continuationPoint = results[0].ContinuationPoint;

            while (!continuationPoint.IsNull && continuationPoint.Length > 0)
            {
                BrowseNextResponse next = await session
                    .BrowseNextAsync(null, false, new List<ByteString> { continuationPoint }, ct)
                    .ConfigureAwait(false);

                List<BrowseResult> nextResults = next.Results.ToList();

                if (StatusCode.IsBad(nextResults[0].StatusCode))
                {
                    throw new ServiceResultException(nextResults[0].StatusCode);
                }

                references.AddRange(nextResults[0].References.ToArray());
                continuationPoint = nextResults[0].ContinuationPoint;
            }

            return references;
        }

        /// <summary>
        /// The browse names of the children of a node.
        /// </summary>
        public static async Task<IReadOnlyList<string>> BrowseNamesAsync(
            ISession session,
            NodeId nodeId,
            CancellationToken ct,
            ViewDescription view = null)
        {
            IReadOnlyList<ReferenceDescription> references =
                await BrowseAsync(session, nodeId, ct, view).ConfigureAwait(false);

            return references.Select(reference => reference.BrowseName.Name).ToArray();
        }

        /// <summary>
        /// Reads one attribute of a node and returns the result, good or bad.
        /// </summary>
        public static async Task<DataValue> ReadAttributeAsync(
            ISession session,
            NodeId nodeId,
            uint attributeId,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(session);

            var valuesToRead = new List<ReadValueId> {
                new() { NodeId = nodeId, AttributeId = attributeId },
            };

            ReadResponse response = await session
                .ReadAsync(null, 0, TimestampsToReturn.Both, valuesToRead, ct)
                .ConfigureAwait(false);

            List<DataValue> results = response.Results.ToList();

            ClientBase.ValidateResponse(results, valuesToRead);

            return results[0];
        }

        /// <summary>
        /// Reads the value of a node and returns the result, good or bad.
        /// </summary>
        /// <remarks>
        /// Unlike ISession.ReadValueAsync this does not throw on a bad status code: a test
        /// which asserts that a node id is rejected needs to see BadNodeIdUnknown as a
        /// value it can compare, not as an exception it has to catch and unwrap.
        /// </remarks>
        public static Task<DataValue> ReadValueAsync(ISession session, NodeId nodeId, CancellationToken ct)
        {
            return ReadAttributeAsync(session, nodeId, Attributes.Value, ct);
        }

        /// <summary>
        /// Writes the value of a node and returns the status code the server answered with.
        /// </summary>
        public static async Task<StatusCode> WriteValueAsync(
            ISession session,
            NodeId nodeId,
            Variant value,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(session);

            var valuesToWrite = new List<WriteValue> {
                new() {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(value),
                },
            };

            WriteResponse response = await session
                .WriteAsync(null, valuesToWrite, ct)
                .ConfigureAwait(false);

            List<StatusCode> results = response.Results.ToList();

            ClientBase.ValidateResponse(results, valuesToWrite);

            return results[0];
        }

        /// <summary>
        /// Calls a method and returns the whole result.
        /// </summary>
        /// <remarks>
        /// The full result is returned rather than just the output arguments, because the
        /// argument validation of a sample method manager is visible in the status code and
        /// in the per argument results, which is exactly what these tests pin down.
        /// </remarks>
        public static async Task<CallMethodResult> CallAsync(
            ISession session,
            NodeId objectId,
            NodeId methodId,
            CancellationToken ct,
            params Variant[] inputArguments)
        {
            ArgumentNullException.ThrowIfNull(session);

            var request = new CallMethodRequest {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = (inputArguments ?? []).ToArrayOf(),
            };

            var requests = new List<CallMethodRequest> { request };

            CallResponse response = await session
                .CallAsync(null, requests, ct)
                .ConfigureAwait(false);

            List<CallMethodResult> results = response.Results.ToList();

            ClientBase.ValidateResponse(results, requests);

            return results[0];
        }

        /// <summary>
        /// The type definition of a node, or NodeId.Null when it has none.
        /// </summary>
        public static async Task<NodeId> GetTypeDefinitionAsync(
            ISession session,
            NodeId nodeId,
            CancellationToken ct)
        {
            IReadOnlyList<ReferenceDescription> references = await BrowseAsync(
                session,
                nodeId,
                ct,
                referenceTypeId: ReferenceTypeIds.HasTypeDefinition,
                includeSubtypes: false).ConfigureAwait(false);

            if (references.Count == 0)
            {
                return NodeId.Null;
            }

            return ExpandedNodeId.ToNodeId(references[0].NodeId, session.NamespaceUris);
        }
    }
}
