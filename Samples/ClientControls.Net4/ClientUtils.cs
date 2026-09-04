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
        /// Browses the address space and returns the references found. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.BrowseAsync(ISession, IReadOnlyList{BrowseDescription}, bool, CancellationToken)"/>.
        /// </summary>
        public static Task<List<ReferenceDescription>> BrowseAsync(ISession session, IReadOnlyList<BrowseDescription> nodesToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            return Opc.Ua.Samples.Client.SampleSession.BrowseAsync(session, nodesToBrowse, throwOnError, ct);
        }

        /// <summary>
        /// Browses the address space and returns the references found. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.BrowseAsync(ISession, ViewDescription, IReadOnlyList{BrowseDescription}, bool, CancellationToken)"/>.
        /// </summary>
        public static Task<List<ReferenceDescription>> BrowseAsync(ISession session, ViewDescription view, IReadOnlyList<BrowseDescription> nodesToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            return Opc.Ua.Samples.Client.SampleSession.BrowseAsync(session, view, nodesToBrowse, throwOnError, ct);
        }

        /// <summary>
        /// Browses the address space and returns the references found. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.BrowseAsync(ISession, BrowseDescription, bool, CancellationToken)"/>.
        /// </summary>
        public static Task<List<ReferenceDescription>> BrowseAsync(ISession session, BrowseDescription nodeToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            return Opc.Ua.Samples.Client.SampleSession.BrowseAsync(session, nodeToBrowse, throwOnError, ct);
        }

        /// <summary>
        /// Browses the address space and returns the references found. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.BrowseAsync(ISession, ViewDescription, BrowseDescription, bool, CancellationToken)"/>.
        /// </summary>
        public static Task<List<ReferenceDescription>> BrowseAsync(ISession session, ViewDescription view, BrowseDescription nodeToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            return Opc.Ua.Samples.Client.SampleSession.BrowseAsync(session, view, nodeToBrowse, throwOnError, ct);
        }

        /// <summary>
        /// Browses the address space and returns all of the supertypes of the specified type node. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.BrowseSuperTypesAsync"/>.
        /// </summary>
        public static Task<List<ReferenceDescription>> BrowseSuperTypesAsync(ISession session, NodeId typeId, bool throwOnError, CancellationToken ct = default)
        {
            return Opc.Ua.Samples.Client.SampleSession.BrowseSuperTypesAsync(session, typeId, throwOnError, ct);
        }

        /// <summary>
        /// Returns the node ids for a set of relative paths. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.TranslateBrowsePathsAsync"/>.
        /// </summary>
        public static Task<List<NodeId>> TranslateBrowsePathsAsync(
            ISession session,
            NodeId startNodeId,
            NamespaceTable namespacesUris,
            CancellationToken ct,
            params string[] relativePaths)
        {
            return Opc.Ua.Samples.Client.SampleSession.TranslateBrowsePathsAsync(session, startNodeId, namespacesUris, ct, relativePaths);
        }
        #endregion

        #region Events
        /// <summary>
        /// Finds the type of the event for the notification. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.FindEventType"/>.
        /// </summary>
        public static NodeId FindEventType(EventFilter filter, EventFieldList notification)
        {
            return Opc.Ua.Samples.Client.SampleSession.FindEventType(filter, notification);
        }

        /// <summary>
        /// Constructs an event object from a notification. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.ConstructEventAsync"/>.
        /// </summary>
        public static Task<BaseEventState> ConstructEventAsync(
            ISession session,
            EventFilter filter,
            EventFieldList notification,
            Dictionary<NodeId, Type> knownEventTypes,
            Dictionary<NodeId, NodeId> eventTypeMappings,
            CancellationToken ct = default)
        {
            return Opc.Ua.Samples.Client.SampleSession.ConstructEventAsync(session, filter, notification, knownEventTypes, eventTypeMappings, ct);
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
        /// the session down instead. See <see cref="Opc.Ua.Samples.Client.SampleSession.DefaultCloseTimeout"/>.
        /// </summary>
        public static readonly TimeSpan DefaultCloseTimeout = Opc.Ua.Samples.Client.SampleSession.DefaultCloseTimeout;

        /// <summary>
        /// Closes a session and disposes it. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.CloseAndDisposeAsync(ISession, CancellationToken)"/>.
        /// </summary>
        public static Task CloseAndDisposeAsync(ISession session, CancellationToken ct = default)
        {
            return Opc.Ua.Samples.Client.SampleSession.CloseAndDisposeAsync(session, ct);
        }

        /// <summary>
        /// Closes a session and disposes it, waiting no longer than the given timeout. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.CloseAndDisposeAsync(ISession, TimeSpan, CancellationToken)"/>.
        /// </summary>
        public static Task CloseAndDisposeAsync(ISession session, TimeSpan timeout, CancellationToken ct = default)
        {
            return Opc.Ua.Samples.Client.SampleSession.CloseAndDisposeAsync(session, timeout, ct);
        }

        /// <summary>
        /// Runs an asynchronous teardown from a synchronous callback and waits for it.
        /// </summary>
        /// <remarks>
        /// FormClosing and Dispose cannot await, so a sample which releases something
        /// asynchronously on its way out has to wait for it. Awaiting it on the UI thread
        /// would deadlock: the continuation is posted back to the message loop which the
        /// wait is blocking, and neither side moves again. Running the teardown on a thread
        /// pool thread, where there is no synchronization context to post back to, is what
        /// lets the wait complete.
        ///
        /// The teardown therefore runs off the UI thread and must not touch any control.
        /// </remarks>
        /// <param name="teardown">The teardown to run.</param>
        public static void WaitForTeardown(Func<Task> teardown)
        {
            if (teardown == null)
            {
                throw new ArgumentNullException(nameof(teardown));
            }

            Task.Run(teardown).GetAwaiter().GetResult();
        }
        #endregion

        #region Subscriptions
        /// <summary>
        /// Adds a subscription driven by the V2 subscription engine to the session. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.AddSubscription"/>.
        /// </summary>
        public static ISubscription AddSubscription(
            ISession session,
            ISubscriptionNotificationHandler handler,
            IOptionsMonitor<Opc.Ua.Client.Subscriptions.SubscriptionOptions> options)
        {
            return Opc.Ua.Samples.Client.SampleSession.AddSubscription(session, handler, options);
        }

        /// <summary>
        /// The options a control uses for a subscription it creates itself. See <see cref="Opc.Ua.Samples.Client.SampleSession.DefaultSubscriptionOptions"/>.
        /// </summary>
        public static Opc.Ua.Client.Subscriptions.SubscriptionOptions DefaultSubscriptionOptions => Opc.Ua.Samples.Client.SampleSession.DefaultSubscriptionOptions;

        /// <summary>
        /// Waits until the V2 engine has applied the pending monitored item changes. Forwards to <see cref="Opc.Ua.Samples.Client.SampleSession.WaitForPendingChangesAsync"/>.
        /// </summary>
        public static Task WaitForPendingChangesAsync(
            ISubscription subscription,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            return Opc.Ua.Samples.Client.SampleSession.WaitForPendingChangesAsync(subscription, timeout, ct);
        }
        #endregion
    }
}
