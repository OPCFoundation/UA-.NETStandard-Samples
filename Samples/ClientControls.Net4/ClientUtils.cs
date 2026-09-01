/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;


[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1016:Mark assemblies with assembly version", Justification = "Sample project keeps existing assembly version metadata.")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1824:Mark assemblies with NeutralResourcesLanguageAttribute", Justification = "Sample project keeps existing resource metadata.")]
[assembly: System.Resources.NeutralResourcesLanguage("en", System.Resources.UltimateResourceFallbackLocation.MainAssembly)]

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Defines numerous re-useable utility functions.
    /// </summary>
    public partial class ClientUtils
    {
        /// <summary>
        /// Handles an exception.
        /// </summary>
        public static void HandleException(ITelemetryContext telemetry, string caption, Exception e)
        {
            ExceptionDlg.Show(telemetry, caption, e);
        }

        /// <summary>
        /// Handles an exception.
        /// </summary>
        public static void HandleException(ILogger logger, string caption, Exception e)
        {
            ExceptionDlg.Show(logger, caption, e);
        }

        /// <summary>
        /// Returns the application icon.
        /// </summary>
        #pragma warning disable CA1024 // Justification: sample public API shape is preserved by design.
        public static System.Drawing.Icon GetAppIcon()
        #pragma warning restore CA1024
        {
            try
            {
                return new Icon("App.ico");
            }
            catch (Exception)
            {
                return null;
            }
        }

        #region DisplayText Lookup
        /// <summary>
        /// Gets the display text for the access level attribute.
        /// </summary>
        /// <param name="accessLevel">The access level.</param>
        /// <returns>The access level formatted as a string.</returns>
        public static string GetAccessLevelDisplayText(byte accessLevel)
        {
            StringBuilder buffer = new StringBuilder();

            if (accessLevel == AccessLevels.None)
            {
                buffer.Append("None");
            }

            if ((accessLevel & AccessLevels.CurrentRead) == AccessLevels.CurrentRead)
            {
                buffer.Append("Read");
            }

            if ((accessLevel & AccessLevels.CurrentWrite) == AccessLevels.CurrentWrite)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append(" | ");
                }

                buffer.Append("Write");
            }

            if ((accessLevel & AccessLevels.HistoryRead) == AccessLevels.HistoryRead)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append(" | ");
                }

                buffer.Append("HistoryRead");
            }

            if ((accessLevel & AccessLevels.HistoryWrite) == AccessLevels.HistoryWrite)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append(" | ");
                }

                buffer.Append("HistoryWrite");
            }

            if ((accessLevel & AccessLevels.SemanticChange) == AccessLevels.SemanticChange)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append(" | ");
                }

                buffer.Append("SemanticChange");
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Gets the display text for the event notifier attribute.
        /// </summary>
        /// <param name="eventNotifier">The event notifier.</param>
        /// <returns>The event notifier formatted as a string.</returns>
        public static string GetEventNotifierDisplayText(byte eventNotifier)
        {
            StringBuilder buffer = new StringBuilder();

            if (eventNotifier == EventNotifiers.None)
            {
                buffer.Append("None");
            }

            if ((eventNotifier & EventNotifiers.SubscribeToEvents) == EventNotifiers.SubscribeToEvents)
            {
                buffer.Append("Subscribe");
            }

            if ((eventNotifier & EventNotifiers.HistoryRead) == EventNotifiers.HistoryRead)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append(" | ");
                }

                buffer.Append("HistoryRead");
            }

            if ((eventNotifier & EventNotifiers.HistoryWrite) == EventNotifiers.HistoryWrite)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append(" | ");
                }

                buffer.Append("HistoryWrite");
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Gets the display text for the value rank attribute.
        /// </summary>
        /// <param name="valueRank">The value rank.</param>
        /// <returns>The value rank formatted as a string.</returns>
        public static string GetValueRankDisplayText(int valueRank)
        {
            switch (valueRank)
            {
                case ValueRanks.Any: return "Any";
                case ValueRanks.Scalar: return "Scalar";
                case ValueRanks.ScalarOrOneDimension: return "ScalarOrOneDimension";
                case ValueRanks.OneOrMoreDimensions: return "OneOrMoreDimensions";
                case ValueRanks.OneDimension: return "OneDimension";
                case ValueRanks.TwoDimensions: return "TwoDimensions";
            }

            return valueRank.ToString();
        }

        /// <summary>
        /// Gets the display text for the specified attribute.
        /// </summary>
        /// <param name="session">The currently active session.</param>
        /// <param name="attributeId">The id of the attribute.</param>
        /// <param name="value">The value of the attribute.</param>
        /// <returns>The attribute formatted as a string.</returns>
        public static async Task<string> GetAttributeDisplayTextAsync(ISession session, uint attributeId, Variant value, CancellationToken ct = default)
        {
            if (value == Variant.Null)
            {
                return String.Empty;
            }

            switch (attributeId)
            {
                case Attributes.AccessLevel:
                case Attributes.UserAccessLevel:
                {
                    if (value.TryGetValue(out byte accessLevel))
                    {
                        return GetAccessLevelDisplayText(accessLevel);
                    }

                    break;
                }

                case Attributes.EventNotifier:
                {
                    if (value.TryGetValue(out byte eventNotifier))
                    {
                        return GetEventNotifierDisplayText(eventNotifier);
                    }

                    break;
                }

                case Attributes.DataType:
                {
                    NodeId dataTypeId = value.TryGetValue(out NodeId dt) ? dt : NodeId.Null;
                    return await session.NodeCache.GetDisplayTextAsync(dataTypeId, ct);
                }

                case Attributes.ValueRank:
                {
                    if (value.TryGetValue(out int valueRank))
                    {
                        return GetValueRankDisplayText(valueRank);
                    }

                    break;
                }

                case Attributes.NodeClass:
                {
                    if (value.TryGetValue(out int nodeClass))
                    {
                        return ((NodeClass)nodeClass).ToString();
                    }

                    break;
                }

                case Attributes.NodeId:
                {
                    if (value.TryGetValue(out NodeId field) && !field.IsNull)
                    {
                        return field.ToString();
                    }

                    return "Null";
                }

                case Attributes.DataTypeDefinition:
                {
                    if (value.TryGetValue(out ExtensionObject field))
                    {
                        return field.ToString();
                    }
                    break;
                }
            }

            // check for byte strings.
            if (value.TryGetValue(out ByteString byteString))
            {
                return Utils.ToHexString(byteString.Span);
            }

            // use default format.
            return value.ToString();
        }
        #endregion

        #region Browse
        /// <summary>
        /// Browses the address space and returns the references found.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodesToBrowse">The set of browse operations to perform.</param>
        /// <param name="throwOnError">if set to <c>true</c> a exception will be thrown on an error.</param>
        /// <param name="ct">A cancellation token to use to cancel the operation.</param>
        /// <returns>
        /// The references found. Null if an error occurred.
        /// </returns>
        public static Task<List<ReferenceDescription>> BrowseAsync(ISession session, IReadOnlyList<BrowseDescription> nodesToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            return BrowseAsync(session, null, nodesToBrowse, throwOnError, ct);
        }

        /// <summary>
        /// Browses the address space and returns the references found.
        /// </summary>
        public static async Task<List<ReferenceDescription>> BrowseAsync(ISession session, ViewDescription view, IReadOnlyList<BrowseDescription> nodesToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            try
            {
                List<ReferenceDescription> references = new List<ReferenceDescription>();

                while (nodesToBrowse.Count > 0)
                {
                    // start the browse operation.
                    BrowseResponse response = await session.BrowseAsync(
                        null,
                        view,
                        0,
                        nodesToBrowse.ToArrayOf(),
                        ct);

                    var results = response.Results.ToList();
                    var diagnosticInfos = response.DiagnosticInfos.ToList();

                    ClientBase.ValidateResponse(results, nodesToBrowse);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                    List<ByteString> continuationPoints = new List<ByteString>();
                    List<BrowseDescription> unprocessedOperations = new List<BrowseDescription>();

                    for (int ii = 0; ii < nodesToBrowse.Count; ii++)
                    {
                        // check for error.
                        if (StatusCode.IsBad(results[ii].StatusCode))
                        {
                            // this error indicates that the server does not have enough simultaneously active
                            // continuation points. This request will need to be resent after the other operations
                            // have been completed and their continuation points released.
                            if (results[ii].StatusCode == StatusCodes.BadNoContinuationPoints)
                            {
                                unprocessedOperations.Add(nodesToBrowse[ii]);
                            }

                            continue;
                        }

                        // check if all references have been fetched.
                        if (results[ii].References.Count == 0)
                        {
                            continue;
                        }

                        // save results.
                        references.AddRange(results[ii].References);

                        // check for continuation point.
                        if (!results[ii].ContinuationPoint.IsNull)
                        {
                            continuationPoints.Add(results[ii].ContinuationPoint);
                        }
                    }

                    // process continuation points.
                    while (continuationPoints.Count > 0)
                    {
                        // continue browse operation.
                        BrowseNextResponse response2 = await session.BrowseNextAsync(
                            null,
                            false,
                            continuationPoints,
                            ct);

                        results = response2.Results.ToList();
                        diagnosticInfos = response2.DiagnosticInfos.ToList();

                        ClientBase.ValidateResponse(results, continuationPoints);
                        ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

                        List<ByteString> revisedContinuationPoints = new List<ByteString>();
                        for (int ii = 0; ii < continuationPoints.Count; ii++)
                        {
                            // check for error.
                            if (StatusCode.IsBad(results[ii].StatusCode))
                            {
                                continue;
                            }

                            // check if all references have been fetched.
                            if (results[ii].References.Count == 0)
                            {
                                continue;
                            }

                            // save results.
                            references.AddRange(results[ii].References);

                            // check for continuation point.
                            if (!results[ii].ContinuationPoint.IsNull)
                            {
                                revisedContinuationPoints.Add(results[ii].ContinuationPoint);
                            }
                        }

                        // check if browsing must continue;
                        continuationPoints = revisedContinuationPoints;
                    }

                    // check if unprocessed results exist.
                    nodesToBrowse = unprocessedOperations;
                }

                // return complete list.
                return references;
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
        /// Browses the address space and returns the references found.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodeToBrowse">The NodeId for the starting node.</param>
        /// <param name="throwOnError">if set to <c>true</c> a exception will be thrown on an error.</param>
        /// <param name="ct">The cancellation token to cancel the operation</param>
        /// <returns>
        /// The references found. Null if an error occurred.
        /// </returns>
        public static Task<List<ReferenceDescription>> BrowseAsync(ISession session, BrowseDescription nodeToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            return BrowseAsync(session, null, nodeToBrowse, throwOnError, ct);
        }

        /// <summary>
        /// Browses the address space and returns the references found.
        /// </summary>
        public static Task<List<ReferenceDescription>> BrowseAsync(ISession session, ViewDescription view, BrowseDescription nodeToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            // construct browse request.
            List<BrowseDescription> nodesToBrowse = new List<BrowseDescription> {
                nodeToBrowse
            };

            return BrowseAsync(session, view, nodesToBrowse, throwOnError, ct);
        }

        /// <summary>
        /// Browses the address space and returns all of the supertypes of the specified type node.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="typeId">The NodeId for a type node in the address space.</param>
        /// <param name="throwOnError">if set to <c>true</c> a exception will be thrown on an error.</param>
        /// <param name="ct">A cancellation token to use to cancel the operation.</param>
        /// <returns>
        /// The references found. Null if an error occurred.
        /// </returns>
        public static async Task<List<ReferenceDescription>> BrowseSuperTypesAsync(ISession session, NodeId typeId, bool throwOnError, CancellationToken ct = default)
        {
            List<ReferenceDescription> supertypes = new List<ReferenceDescription>();

            try
            {
                // find all of the children of the field.
                BrowseDescription nodeToBrowse = new BrowseDescription();

                nodeToBrowse.NodeId = typeId;
                nodeToBrowse.BrowseDirection = BrowseDirection.Inverse;
                nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasSubtype;
                nodeToBrowse.IncludeSubtypes = false; // more efficient to use IncludeSubtypes=False when possible.
                nodeToBrowse.NodeClassMask = 0; // the HasSubtype reference already restricts the targets to Types.
                nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

                List<ReferenceDescription> references = await BrowseAsync(session, nodeToBrowse, throwOnError, ct);

                while (references != null && references.Count > 0)
                {
                    // should never be more than one supertype.
                    supertypes.Add(references[0]);

                    // only follow references within this server.
                    if (references[0].NodeId.IsAbsolute)
                    {
                        break;
                    }

                    // get the references for the next level up.
                    nodeToBrowse.NodeId = (NodeId)references[0].NodeId;
                    references = await BrowseAsync(session, nodeToBrowse, throwOnError, ct);
                }

                // return complete list.
                return supertypes;
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
        /// Returns the node ids for a set of relative paths.
        /// </summary>
        /// <param name="session">An open session with the server to use.</param>
        /// <param name="startNodeId">The starting node for the relative paths.</param>
        /// <param name="namespacesUris">The namespace URIs referenced by the relative paths.</param>
        /// <param name="ct">A cancellation token to use to cancel the operation.</param>
        /// <param name="relativePaths">The relative paths.</param>
        /// <returns>A collection of local nodes.</returns>
        public static async Task<List<NodeId>> TranslateBrowsePathsAsync(
            ISession session,
            NodeId startNodeId,
            NamespaceTable namespacesUris,
            CancellationToken ct,
            params string[] relativePaths)
        {
            // build the list of browse paths to follow by parsing the relative paths.
            List<BrowsePath> browsePaths = new List<BrowsePath>();

            if (relativePaths != null)
            {
                for (int ii = 0; ii < relativePaths.Length; ii++)
                {
                    BrowsePath browsePath = new BrowsePath();

                    // The relative paths used indexes in the namespacesUris table. These must be
                    // converted to indexes used by the server. An error occurs if the relative path
                    // refers to a namespaceUri that the server does not recognize.

                    // The relative paths may refer to ReferenceType by their BrowseName. The TypeTree object
                    // allows the parser to look up the server's NodeId for the ReferenceType.

                    browsePath.RelativePath = RelativePath.Parse(
                        relativePaths[ii],
                        session.TypeTree,
                        namespacesUris,
                        session.NamespaceUris);

                    browsePath.StartingNode = startNodeId;

                    browsePaths.Add(browsePath);
                }
            }

            // make the call to the server.
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                browsePaths,
                ct);

            ResponseHeader responseHeader = response.ResponseHeader;
            var results = response.Results.ToList();
            var diagnosticInfos = response.DiagnosticInfos.ToList();

            // ensure that the server returned valid results.
            Session.ValidateResponse(results, browsePaths);
            Session.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);

            // collect the list of node ids found.
            List<NodeId> nodes = new List<NodeId>();

            for (int ii = 0; ii < results.Count; ii++)
            {
                // check if the start node actually exists.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    nodes.Add(NodeId.Null);
                    continue;
                }

                // an empty list is returned if no node was found.
                if (results[ii].Targets.Count == 0)
                {
                    nodes.Add(NodeId.Null);
                    continue;
                }

                // Multiple matches are possible, however, the node that matches the type model is the
                // one we are interested in here. The rest can be ignored.
                BrowsePathTarget target = results[ii].Targets[0];

                if (target.RemainingPathIndex != UInt32.MaxValue)
                {
                    nodes.Add(NodeId.Null);
                    continue;
                }

                // The targetId is an ExpandedNodeId because it could be node in another server.
                // The ToNodeId function is used to convert a local NodeId stored in a ExpandedNodeId to a NodeId.
                nodes.Add(ExpandedNodeId.ToNodeId(target.TargetId, session.NamespaceUris));
            }

            // return whatever was found.
            return nodes;
        }
        #endregion

        #region Events
        /// <summary>
        /// Finds the type of the event for the notification.
        /// </summary>
        /// <param name="filter">The filter the notification was produced with.</param>
        /// <param name="notification">The notification.</param>
        /// <returns>The NodeId of the EventType.</returns>
        /// <remarks>
        /// The V2 subscription engine reports revised values but not the filter a monitored
        /// item was created with, so the caller which owns the filter passes it in. The fields
        /// of a notification line up one to one with its select clauses.
        /// </remarks>
        public static NodeId FindEventType(EventFilter filter, EventFieldList notification)
        {
            if (filter != null)
            {
                for (int ii = 0; ii < filter.SelectClauses.Count; ii++)
                {
                    SimpleAttributeOperand clause = filter.SelectClauses[ii];

                    if (clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == BrowseNames.EventType)
                    {
                        return notification.EventFields[ii].TryGetValue(out NodeId nodeId) ? nodeId : NodeId.Null;
                    }
                }
            }

            return NodeId.Null;
        }

        /// <summary>
        /// Constructs an event object from a notification.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="filter">The filter the notification was produced with.</param>
        /// <param name="notification">The notification.</param>
        /// <param name="knownEventTypes">The known event types.</param>
        /// <param name="eventTypeMappings">Mapping between event types and known event types.</param>
        /// <param name="ct">Cancellation token to use to cancel operation</param>
        /// <returns>
        /// The event object. Null if the notification is not a valid event type.
        /// </returns>
        public static async Task<BaseEventState> ConstructEventAsync(
            ISession session,
            EventFilter filter,
            EventFieldList notification,
            Dictionary<NodeId, Type> knownEventTypes,
            Dictionary<NodeId, NodeId> eventTypeMappings,
            CancellationToken ct = default)
        {
            // find the event type.
            NodeId eventTypeId = FindEventType(filter, notification);

            if (eventTypeId.IsNull)
            {
                return null;
            }

            // look up the known event type.
            Type knownType = null;
            NodeId knownTypeId = NodeId.Null;

            if (eventTypeMappings.TryGetValue(eventTypeId, out knownTypeId))
            {
                knownType = knownEventTypes[knownTypeId];
            }

            // try again.
            if (knownType == null)
            {
                if (knownEventTypes.TryGetValue(eventTypeId, out knownType))
                {
                    knownTypeId = eventTypeId;
                    eventTypeMappings.TryAdd(eventTypeId, eventTypeId);
                }
            }

            // try mapping it to a known type.
            if (knownType == null)
            {
                // browse for the supertypes of the event type.
                List<ReferenceDescription> supertypes = await ClientUtils.BrowseSuperTypesAsync(session, eventTypeId, false, ct);

                // can't do anything with unknown types.
                if (supertypes == null)
                {
                    return null;
                }

                // find the first supertype that matches a known event type.
                for (int ii = 0; ii < supertypes.Count; ii++)
                {
                    NodeId superTypeId = (NodeId)supertypes[ii].NodeId;

                    if (knownEventTypes.TryGetValue(superTypeId, out knownType))
                    {
                        knownTypeId = superTypeId;
                        eventTypeMappings.TryAdd(eventTypeId, superTypeId);
                    }

                    if (!knownTypeId.IsNull)
                    {
                        break;
                    }
                }

                // can't do anything with unknown types.
                if (knownTypeId.IsNull)
                {
                    return null;
                }
            }

            // construct the event based on the known event type.
            BaseEventState e = (BaseEventState)Activator.CreateInstance(knownType, new object[] { (NodeState)null });

            // initialize the event with the values in the notification.
            e.Update(session.SystemContext, filter.SelectClauses, notification);

            // save the orginal notification.
            e.Handle = notification;

            return e;
        }
        #endregion


        #region Type Model Browsing
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
                List<ReferenceDescription> supertypes = await ClientUtils.BrowseSuperTypesAsync(session, typeId, false, ct);

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
            List<ReferenceDescription> references = await ClientUtils.BrowseAsync(session, nodeToBrowse, false, ct);

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
        #endregion

        #region Private Methods
        /// <summary>
        /// Collects the fields for the type.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="typeId">The type id.</param>
        /// <param name="fields">The fields.</param>
        /// <param name="fieldNodeIds">The node id for the declaration of the field.</param>
        private static async Task CollectFieldsForTypeAsync(Session session, NodeId typeId, List<SimpleAttributeOperand> fields, List<NodeId> fieldNodeIds, CancellationToken ct = default)
        {
            // get the supertypes.
            List<ReferenceDescription> supertypes = await ClientUtils.BrowseSuperTypesAsync(session, typeId, false, ct);

            if (supertypes == null)
            {
                return;
            }

            // process the types starting from the top of the tree.
            Dictionary<NodeId, List<QualifiedName>> foundNodes = new Dictionary<NodeId, List<QualifiedName>>();
            List<QualifiedName> parentPath = new List<QualifiedName>();

            for (int ii = supertypes.Count - 1; ii >= 0; ii--)
            {
                await CollectFieldsAsync(session, (NodeId)supertypes[ii].NodeId, parentPath, fields, fieldNodeIds, foundNodes, ct);
            }

            // collect the fields for the selected type.
            await CollectFieldsAsync(session, typeId, parentPath, fields, fieldNodeIds, foundNodes, ct);
        }

        /// <summary>
        /// Collects the fields for the instance.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="fields">The fields.</param>
        /// <param name="fieldNodeIds">The node id for the declaration of the field.</param>
        /// <param name="ct">Canceellation token to cancel the operation</param>
        private static Task CollectFieldsForInstanceAsync(Session session, NodeId instanceId, List<SimpleAttributeOperand> fields, List<NodeId> fieldNodeIds, CancellationToken ct = default)
        {
            Dictionary<NodeId, List<QualifiedName>> foundNodes = new Dictionary<NodeId, List<QualifiedName>>();
            List<QualifiedName> parentPath = new List<QualifiedName>();
            return CollectFieldsAsync(session, instanceId, parentPath, fields, fieldNodeIds, foundNodes, ct);
        }

        /// <summary>
        /// Collects the fields for the instance node.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodeId">The node id.</param>
        /// <param name="parentPath">The parent path.</param>
        /// <param name="fields">The event fields.</param>
        /// <param name="fieldNodeIds">The node id for the declaration of the field.</param>
        /// <param name="foundNodes">The table of found nodes.</param>
        /// <param name="ct">Canceellation token to cancel the operation</param>
        private static async Task CollectFieldsAsync(
            Session session,
            NodeId nodeId,
            List<QualifiedName> parentPath,
            List<SimpleAttributeOperand> fields,
            List<NodeId> fieldNodeIds,
            Dictionary<NodeId, List<QualifiedName>> foundNodes,
            CancellationToken ct = default)
        {
            // find all of the children of the field.
            BrowseDescription nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = nodeId;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.Aggregates;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable);
            nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

            List<ReferenceDescription> children = await ClientUtils.BrowseAsync(session, nodeToBrowse, false, ct);

            if (children == null)
            {
                return;
            }

            // process the children.
            for (int ii = 0; ii < children.Count; ii++)
            {
                ReferenceDescription child = children[ii];

                if (child.NodeId.IsAbsolute)
                {
                    continue;
                }

                // construct browse path.
                List<QualifiedName> browsePath = new List<QualifiedName>(parentPath);
                browsePath.Add(child.BrowseName);

                // check if the browse path is already in the list.
                int index = ContainsPath(fields, browsePath);

                if (index < 0)
                {
                    SimpleAttributeOperand field = new SimpleAttributeOperand();

                    field.TypeDefinitionId = ObjectTypeIds.BaseEventType;
                    field.BrowsePath = browsePath;
                    field.AttributeId = (child.NodeClass == NodeClass.Variable) ? Attributes.Value : Attributes.NodeId;

                    fields.Add(field);
                    fieldNodeIds.Add((NodeId)child.NodeId);
                }

                // recusively find all of the children.
                NodeId targetId = (NodeId)child.NodeId;

                // need to guard against loops.
                if (foundNodes.TryAdd(targetId, browsePath))
                {
                    await CollectFieldsAsync(session, (NodeId)child.NodeId, browsePath, fields, fieldNodeIds, foundNodes, ct);
                }
            }
        }

        /// <summary>
        /// Determines whether the specified select clause contains the browse path.
        /// </summary>
        /// <param name="selectClause">The select clause.</param>
        /// <param name="browsePath">The browse path.</param>
        /// <returns>
        /// 	<c>true</c> if the specified select clause contains path; otherwise, <c>false</c>.
        /// </returns>
        private static int ContainsPath(List<SimpleAttributeOperand> selectClause, List<QualifiedName> browsePath)
        {
            for (int ii = 0; ii < selectClause.Count; ii++)
            {
                SimpleAttributeOperand field = selectClause[ii];

                if (field.BrowsePath.Count != browsePath.Count)
                {
                    continue;
                }

                bool match = true;

                for (int jj = 0; jj < field.BrowsePath.Count; jj++)
                {
                    if (field.BrowsePath[jj] != browsePath[jj])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return ii;
                }
            }

            return -1;
        }
        #endregion

        #region Sessions
        /// <summary>
        /// How long a sample waits for a session to close before it stops waiting and tears
        /// the session down instead.
        /// </summary>
        public static readonly TimeSpan DefaultCloseTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Closes a session and disposes it, without letting a server which is no longer
        /// reachable hold the sample up.
        /// </summary>
        /// <remarks>
        /// A managed session cannot close while its connection state machine is inside a
        /// reconnect attempt: requesting the close only moves the state machine to Closing,
        /// and the attempt in flight runs to its own end first. Against a host which accepts
        /// the connection but never answers - a server which was stopped while its machine
        /// stayed up, a firewall which swallows the traffic - that end is the OperationTimeout
        /// of the endpoint, which the sample configurations set to ten minutes. Closing the
        /// window of a sample would then block for those ten minutes.
        ///
        /// So the wait is bounded and, when it expires, the session is disposed instead:
        /// disposing cancels the state machine, which does cancel the attempt in flight. The
        /// session is disposed either way, which is also what stops its background workers.
        /// </remarks>
        /// <param name="session">The session to close. A null session is ignored.</param>
        /// <param name="ct">The cancellation token.</param>
        public static Task CloseAndDisposeAsync(ISession session, CancellationToken ct = default)
        {
            return CloseAndDisposeAsync(session, DefaultCloseTimeout, ct);
        }

        /// <summary>
        /// Closes a session and disposes it, waiting no longer than the given timeout.
        /// </summary>
        /// <param name="session">The session to close. A null session is ignored.</param>
        /// <param name="timeout">How long to wait for the close before disposing instead.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task CloseAndDisposeAsync(
            ISession session,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            if (session == null)
            {
                return;
            }

            try
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
                bounded.CancelAfter(timeout);

                await session.CloseAsync((int)timeout.TotalMilliseconds, bounded.Token);
            }
            catch (OperationCanceledException)
            {
                // the server did not answer in time, or the caller gave up: the dispose below
                // is what actually tears the session down
            }
            catch (ServiceResultException)
            {
                // closing a session on a server which is already gone is not an error a
                // sample has anything to do about
            }
            finally
            {
                await session.DisposeAsync();
            }
        }
        #endregion

        #region Subscriptions
        /// <summary>
        /// Adds a subscription driven by the V2 subscription engine to the session.
        /// </summary>
        /// <param name="session">The session, which has to run the V2 subscription engine.</param>
        /// <param name="handler">The handler which receives the notifications.</param>
        /// <param name="options">The options of the subscription. The caller keeps the monitor
        /// so it can reconfigure the subscription later on.</param>
        public static ISubscription AddSubscription(
            ISession session,
            ISubscriptionNotificationHandler handler,
            IOptionsMonitor<Opc.Ua.Client.Subscriptions.SubscriptionOptions> options)
        {
            ArgumentNullException.ThrowIfNull(session);

            if (!session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine. Create it with a " +
                    "ManagedSessionFactory or a session factory configured with the " +
                    "DefaultSubscriptionEngineFactory.");
            }

            return manager.Add(handler, options);
        }

        /// <summary>
        /// The options a control uses for a subscription it creates itself.
        /// </summary>
        public static Opc.Ua.Client.Subscriptions.SubscriptionOptions DefaultSubscriptionOptions => new Opc.Ua.Client.Subscriptions.SubscriptionOptions {
            PublishingInterval = TimeSpan.FromSeconds(1),
            KeepAliveCount = 10,
            LifetimeCount = 100,
            MaxNotificationsPerPublish = 1000,
            PublishingEnabled = true,
        };

        /// <summary>
        /// Waits until the V2 engine has applied the pending monitored item changes.
        /// </summary>
        /// <remarks>
        /// The V2 engine applies added, modified and removed items on its own worker instead of
        /// on an explicit ApplyChanges call, so a wizard which wants to show the operation
        /// results of a step has to wait for that worker to catch up first.
        /// </remarks>
        /// <param name="subscription">The subscription to wait for.</param>
        /// <param name="timeout">How long to wait before giving up and showing what there is.</param>
        /// <param name="ct">Cancellation token to use to cancel operation.</param>
        public static async Task WaitForPendingChangesAsync(
            ISubscription subscription,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(subscription);

            DateTime deadline = DateTime.UtcNow.Add(timeout);

            while (HasPendingChanges(subscription))
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return;
                }

                await Task.Delay(50, ct).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Returns true while the V2 engine still has monitored item changes to apply.
        /// </summary>
        private static bool HasPendingChanges(ISubscription subscription)
        {
            foreach (IMonitoredItem monitoredItem in subscription.MonitoredItems.Items)
            {
                if (monitoredItem is IMonitoredItemApplyState applyState && applyState.HasPendingChanges)
                {
                    return true;
                }
            }

            return false;
        }
        #endregion
    }
}
