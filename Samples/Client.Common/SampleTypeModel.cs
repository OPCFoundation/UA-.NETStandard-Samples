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

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// Reads the type model of a server: the instance declarations of a type, with the
    /// declarations it inherits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An instance declaration is a child of a type definition which carries a modelling
    /// rule - the Mandatory and Optional members every instance of the type has. Reading
    /// them is what an event filter editor, a field list and a history event view are
    /// built on: the fields a client may select from an event of a given type are exactly
    /// the instance declarations of that event type.
    /// </para>
    /// <para>
    /// This used to exist twice, in <c>ClientUtils</c> of the shared control library and
    /// in <c>ModelUtils</c> of the Quickstart library, and neither copy needed a window.
    /// It lives here so that a client model can read a type model without pulling in
    /// Windows Forms; <c>ClientUtils</c> keeps forwarders under the old names.
    /// </para>
    /// </remarks>
    public static class SampleTypeModel
    {
        /// <summary>
        /// Collects the instance declarations for a type.
        /// </summary>
        public static Task<List<InstanceDeclaration>> CollectInstanceDeclarationsForTypeAsync(ISession session, NodeId typeId, CancellationToken ct = default)
        {
            return CollectInstanceDeclarationsForTypeAsync(session, typeId, true, ct);
        }

        /// <summary>
        /// Collects the instance declarations for a type.
        /// </summary>
        public static async Task<List<InstanceDeclaration>> CollectInstanceDeclarationsForTypeAsync(ISession session, NodeId typeId, bool includeSupertypes, CancellationToken ct = default)
        {
            // process the types starting from the top of the tree.
            List<InstanceDeclaration> instances = new List<InstanceDeclaration>();
            Dictionary<string, InstanceDeclaration> map = new Dictionary<string, InstanceDeclaration>();

            // get the supertypes.
            if (includeSupertypes)
            {
                List<ReferenceDescription> supertypes = await SampleSession.BrowseSuperTypesAsync(session, typeId, false, ct);

                if (supertypes != null)
                {
                    for (int ii = supertypes.Count - 1; ii >= 0; ii--)
                    {
                        await CollectInstanceDeclarationsAsync(session, (NodeId)supertypes[ii].NodeId, null, instances, map, ct);
                    }
                }
            }

            // collect the fields for the selected type.
            await CollectInstanceDeclarationsAsync(session, typeId, null, instances, map, ct);

            // return the complete list.
            return instances;
        }

        /// <summary>
        /// Collects the fields for the instance node.
        /// </summary>
        private static async Task CollectInstanceDeclarationsAsync(
            ISession session,
            NodeId typeId,
            InstanceDeclaration parent,
            List<InstanceDeclaration> instances,
            IDictionary<string, InstanceDeclaration> map,
            CancellationToken ct = default)
        {
            // find the children.
            BrowseDescription nodeToBrowse = new BrowseDescription();

            if (parent == null)
            {
                nodeToBrowse.NodeId = typeId;
            }
            else
            {
                nodeToBrowse.NodeId = parent.NodeId;
            }

            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasChild;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.Method);
            nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

            // ignore any browsing errors.
            List<ReferenceDescription> references = await SampleSession.BrowseAsync(session, nodeToBrowse, false, ct);

            if (references == null)
            {
                return;
            }

            // process the children.
            List<NodeId> nodeIds = new List<NodeId>();
            List<InstanceDeclaration> children = new List<InstanceDeclaration>();

            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];

                if (reference.NodeId.IsAbsolute)
                {
                    continue;
                }

                // create a new declaration.
                InstanceDeclaration child = new InstanceDeclaration();

                child.RootTypeId = typeId;
                child.NodeId = (NodeId)reference.NodeId;
                child.BrowseName = reference.BrowseName;
                child.NodeClass = reference.NodeClass;

                if (!(reference.DisplayName).IsNullOrEmpty)
                {
                    child.DisplayName = reference.DisplayName.Text;
                }
                else
                {
                    child.DisplayName = reference.BrowseName.Name;
                }

                if (parent != null)
                {
                    child.BrowsePath = new List<QualifiedName>(parent.BrowsePath);
                    child.BrowsePathDisplayText = Utils.Format("{0}/{1}", parent.BrowsePathDisplayText, reference.BrowseName);
                    child.DisplayPath = Utils.Format("{0}/{1}", parent.DisplayPath, reference.DisplayName);
                }
                else
                {
                    child.BrowsePath = new List<QualifiedName>();
                    child.BrowsePathDisplayText = Utils.Format("{0}", reference.BrowseName);
                    child.DisplayPath = Utils.Format("{0}", reference.DisplayName);
                }

                child.BrowsePath.Add(reference.BrowseName);

                // check if reading an overridden declaration.
                InstanceDeclaration overriden = null;

                if (map.TryGetValue(child.BrowsePathDisplayText, out overriden))
                {
                    child.OverriddenDeclaration = overriden;
                }

                map[child.BrowsePathDisplayText] = child;

                // add to list.
                children.Add(child);
                nodeIds.Add(child.NodeId);
            }

            // check if nothing more to do.
            if (children.Count == 0)
            {
                return;
            }

            // find the modelling rules.
            List<NodeId> modellingRules = await FindTargetOfReferenceAsync(session, nodeIds, Opc.Ua.ReferenceTypeIds.HasModellingRule, false, ct);

            if (modellingRules != null)
            {
                for (int ii = 0; ii < nodeIds.Count; ii++)
                {
                    children[ii].ModellingRule = modellingRules[ii];

                    // if the modelling rule is null then the instance is not part of the type declaration.
                    if ((modellingRules[ii]).IsNull)
                    {
                        map.Remove(children[ii].BrowsePathDisplayText);
                    }
                }
            }

            // update the descriptions.
            await UpdateInstanceDescriptionsAsync(session, children, false, ct);

            // recusively collect instance declarations for the tree below.
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (!(children[ii].ModellingRule).IsNull)
                {
                    instances.Add(children[ii]);
                    await CollectInstanceDeclarationsAsync(session, typeId, children[ii], instances, map, ct);
                }
            }
        }

        /// <summary>
        /// Finds the targets for the specified reference.
        /// </summary>
        private static async Task<List<NodeId>> FindTargetOfReferenceAsync(ISession session, List<NodeId> nodeIds, NodeId referenceTypeId, bool throwOnError, CancellationToken ct = default)
        {
            try
            {
                // construct browse request.
                List<BrowseDescription> nodesToBrowse = new List<BrowseDescription>();

                for (int ii = 0; ii < nodeIds.Count; ii++)
                {
                    BrowseDescription nodeToBrowse = new BrowseDescription();
                    nodeToBrowse.NodeId = nodeIds[ii];
                    nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
                    nodeToBrowse.ReferenceTypeId = referenceTypeId;
                    nodeToBrowse.IncludeSubtypes = false;
                    nodeToBrowse.NodeClassMask = 0;
                    nodeToBrowse.ResultMask = (uint)BrowseResultMask.None;
                    nodesToBrowse.Add(nodeToBrowse);
                }

                // start the browse operation.
                BrowseResponse response = await session.BrowseAsync(
                    null,
                    null,
                    1,
                    nodesToBrowse,
                    ct);

                var results = response.Results.ToList();
                var diagnosticInfos = response.DiagnosticInfos.ToList();

                ClientBase.ValidateResponse(results, nodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                List<NodeId> targetIds = new List<NodeId>();
                List<ByteString> continuationPoints = new List<ByteString>();

                for (int ii = 0; ii < nodeIds.Count; ii++)
                {
                    targetIds.Add(NodeId.Null);

                    // check for error.
                    if (StatusCode.IsBad(results[ii].StatusCode))
                    {
                        continue;
                    }

                    // check for continuation point.
                    if (!results[ii].ContinuationPoint.IsNull && results[ii].ContinuationPoint.Length > 0)
                    {
                        continuationPoints.Add(results[ii].ContinuationPoint);
                    }

                    // get the node id.
                    if (results[ii].References.Count > 0)
                    {
                        if ((results[ii].References[0].NodeId).IsNull || results[ii].References[0].NodeId.IsAbsolute)
                        {
                            continue;
                        }

                        targetIds[ii] = (NodeId)results[ii].References[0].NodeId;
                    }
                }

                // release continuation points.
                if (continuationPoints.Count > 0)
                {
                    BrowseNextResponse response2 = await session.BrowseNextAsync(
                        null,
                        true,
                        continuationPoints,
                        ct);

                    results = response2.Results.ToList();
                    diagnosticInfos = response2.DiagnosticInfos.ToList();

                    ClientBase.ValidateResponse(results, continuationPoints);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);
                }

                //return complete list.
                return targetIds;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }

        /// <summary>
        /// Finds the targets for the specified reference.
        /// </summary>
        private static async Task UpdateInstanceDescriptionsAsync(ISession session, List<InstanceDeclaration> instances, bool throwOnError, CancellationToken ct = default)
        {
            try
            {
                List<ReadValueId> nodesToRead = new List<ReadValueId>();

                for (int ii = 0; ii < instances.Count; ii++)
                {
                    ReadValueId nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = instances[ii].NodeId;
                    nodeToRead.AttributeId = Attributes.Description;
                    nodesToRead.Add(nodeToRead);

                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = instances[ii].NodeId;
                    nodeToRead.AttributeId = Attributes.DataType;
                    nodesToRead.Add(nodeToRead);

                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = instances[ii].NodeId;
                    nodeToRead.AttributeId = Attributes.ValueRank;
                    nodesToRead.Add(nodeToRead);
                }

                // start the browse operation.
                ReadResponse response = await session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    ct);

                var results = response.Results.ToList();
                var diagnosticInfos = response.DiagnosticInfos.ToList();

                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                // update the instances.
                for (int ii = 0; ii < nodesToRead.Count; ii += 3)
                {
                    InstanceDeclaration instance = instances[ii / 3];

                    instance.Description = results[ii].GetValue<LocalizedText>(LocalizedText.Null).Text;
                    instance.DataType = results[ii + 1].GetValue<NodeId>(NodeId.Null);
                    instance.ValueRank = results[ii + 2].GetValue<int>(ValueRanks.Any);

                    if (!(instance.DataType).IsNull)
                    {
                        instance.BuiltInType = await TypeInfo.GetBuiltInTypeAsync(instance.DataType, session.TypeTree, ct);
                        instance.DataTypeDisplayText = await session.NodeCache.GetDisplayTextAsync(instance.DataType, ct);

                        if (instance.ValueRank >= 0)
                        {
                            instance.DataTypeDisplayText += "[]";
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }
            }
        }
    }
}
