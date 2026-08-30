/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace Quickstarts.AlarmConditionClient
{
    /// <summary>
    /// Defines a event filter for a subscription.
    /// </summary>
    public class FilterDefinition
    {
        #region Public Interface
        /// <summary>
        /// The NodeId for the Area that is subscribed to (the entire Server if not specified).
        /// </summary>
#pragma warning disable CA1051 // Justification: Sample code exposes fields by design.
        public NodeId AreaId;
#pragma warning restore CA1051

        /// <summary>
        /// The minimum severity for the events of interest.
        /// </summary>
#pragma warning disable CA1051 // Justification: Sample code exposes fields by design.
        public EventSeverity Severity;
#pragma warning restore CA1051

        /// <summary>
        /// The types for the events of interest.
        /// </summary>
#pragma warning disable CA1051 // Justification: Sample code exposes fields by design.
        public IList<NodeId> EventTypes;
#pragma warning restore CA1051

        /// <summary>
        /// Whether suppressed or shelved condition events are of interest.
        /// </summary>
#pragma warning disable CA1051 // Justification: Sample code exposes fields by design.
        public bool IgnoreSuppressedOrShelved;
#pragma warning restore CA1051

        /// <summary>
        /// The select clauses to use with the filter.
        /// </summary>
#pragma warning disable CA1051 // Justification: Sample code exposes fields by design.
        public IList<SimpleAttributeOperand> SelectClauses;
#pragma warning restore CA1051

        /// <summary>
        /// Creates the monitored item based on the current definition.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns>The monitored item.</returns>
        public MonitoredItem CreateMonitoredItem(ISession session, ITelemetryContext telemetry)
        {
            // choose the server object by default.
            if (AreaId.IsNull)
            {
                AreaId = ObjectIds.Server;
            }

            // create the item with the filter.
            MonitoredItem monitoredItem = new MonitoredItem(telemetry);

            monitoredItem.DisplayName = null;
            monitoredItem.StartNodeId = AreaId;
            monitoredItem.RelativePath = null;
            monitoredItem.NodeClass = NodeClass.Object;
            monitoredItem.AttributeId = Attributes.EventNotifier;
            monitoredItem.IndexRange = null;
            monitoredItem.Encoding = QualifiedName.Null;
            monitoredItem.MonitoringMode = MonitoringMode.Reporting;
            monitoredItem.SamplingInterval = 0;
            monitoredItem.QueueSize = UInt32.MaxValue;
            monitoredItem.DiscardOldest = true;
            monitoredItem.Filter = ConstructFilter(session);

            // save the definition as the handle.
            monitoredItem.Handle = this;

            return monitoredItem;
        }

        /// <summary>
        /// Constructs the select clauses for a set of event types.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="eventTypeIds">The event type ids.</param>
        /// <returns>The select clauses for all fields discovered.</returns>
        /// <remarks>
        /// Each event type is an ObjectType in the address space. The fields supported by the
        /// server are defined as children of the ObjectType. Many of the fields are manadatory
        /// and are defined by the UA information model, however, indiviudual servers many not
        /// support all of the optional fields.
        ///
        /// This method browses the type model and
        /// </remarks>
        public async Task<List<SimpleAttributeOperand>> ConstructSelectClausesAsync(
            ISession session,
            CancellationToken ct,
            params NodeId[] eventTypeIds)
        {
            // browse the type model in the server address space to find the fields available for the event type.
            List<SimpleAttributeOperand> selectClauses = new List<SimpleAttributeOperand>();

            // must always request the NodeId for the condition instances.
            // this can be done by specifying an operand with an empty browse path.
            SimpleAttributeOperand operand = new SimpleAttributeOperand();

            operand.TypeDefinitionId = ObjectTypeIds.ConditionType;
            operand.AttributeId = Attributes.NodeId;
            operand.BrowsePath = new List<QualifiedName>();

            selectClauses.Add(operand);

            // the requested event types share most of their supertype chain - every
            // condition type descends from BaseEventType through ConditionType - and each
            // type in that chain carries a subtree of its own. One table of visited nodes
            // for the whole call therefore browses each of those subtrees once instead of
            // once per requested type. The fields are unaffected: they are keyed by browse
            // path, and a node reached again through the same path adds nothing.
            var foundNodes = new Dictionary<NodeId, List<QualifiedName>>();

            // add the fields for the selected EventTypes.
            if (eventTypeIds != null)
            {
                for (int ii = 0; ii < eventTypeIds.Length; ii++)
                {
                    await CollectFieldsAsync(session, eventTypeIds[ii], selectClauses, foundNodes, ct);
                }
            }

            // use BaseEventType as the default if no EventTypes specified.
            else
            {
                await CollectFieldsAsync(session, ObjectTypeIds.BaseEventType, selectClauses, foundNodes, ct);
            }

            return selectClauses;
        }

        /// <summary>
        /// Constructs the event filter for the subscription.
        /// </summary>
        /// <returns>The event filter.</returns>
        public EventFilter ConstructFilter(ISession session)
        {
            EventFilter filter = new EventFilter();

            // the select clauses specify the values returned with each event notification.
            filter.SelectClauses = SelectClauses.ToArrayOf();

            // the where clause restricts the events returned by the server.
            // it works a lot like the WHERE clause in a SQL statement and supports
            // arbitrary expession trees where the operands are literals or event fields.
            ContentFilter whereClause = new ContentFilter();

            // the code below constructs a filter that looks like this:
            // (Severity >= X OR LastSeverity >= X) AND (SuppressedOrShelved == False) AND (OfType(A) OR OfType(B))

            // add the severity.
            ContentFilterElement element1 = null;
            ContentFilterElement element2 = null;

            if (Severity > EventSeverity.Min)
            {
                // select the Severity property of the event.
                SimpleAttributeOperand operand1 = new SimpleAttributeOperand();
                operand1.TypeDefinitionId = ObjectTypeIds.BaseEventType;
                operand1.BrowsePath = new List<QualifiedName> { new QualifiedName(BrowseNames.Severity) }.ToArrayOf();
                operand1.AttributeId = Attributes.Value;

                // specify the value to compare the Severity property with.
                LiteralOperand operand2 = new LiteralOperand();
                operand2.Value = new Variant((ushort)Severity);

                // specify that the Severity property must be GreaterThanOrEqual the value specified.
                element1 = whereClause.Push(FilterOperator.GreaterThanOrEqual, new Variant(new ExtensionObject(operand1)), new Variant(new ExtensionObject(operand2)));
            }

            // add the suppressed or shelved.
            if (!IgnoreSuppressedOrShelved)
            {
                // select the SuppressedOrShelved property of the event.
                SimpleAttributeOperand operand1 = new SimpleAttributeOperand();
                operand1.TypeDefinitionId = ObjectTypeIds.BaseEventType;
                operand1.BrowsePath = new List<QualifiedName> { new QualifiedName(BrowseNames.SuppressedOrShelved) }.ToArrayOf();
                operand1.AttributeId = Attributes.Value;

                // specify the value to compare the Severity property with.
                LiteralOperand operand2 = new LiteralOperand();
                operand2.Value = new Variant(false);

                // specify that the Severity property must Equal the value specified.
                element2 = whereClause.Push(FilterOperator.Equals, new Variant(new ExtensionObject(operand1)), new Variant(new ExtensionObject(operand2)));

                // chain multiple elements together with an AND clause.
                if (element1 != null)
                {
                    element1 = whereClause.Push(FilterOperator.And, new Variant(new ExtensionObject(element1)), new Variant(new ExtensionObject(element2)));
                }
                else
                {
                    element1 = element2;
                }
            }

            // add the event types.
            if (EventTypes != null && EventTypes.Count > 0)
            {
                element2 = null;

                // save the last element.
                for (int ii = 0; ii < EventTypes.Count; ii++)
                {
                    // for this example uses the 'OfType' operator to limit events to thoses with specified event type.
                    LiteralOperand operand1 = new LiteralOperand();
                    operand1.Value = new Variant(EventTypes[ii]);
                    ContentFilterElement element3 = whereClause.Push(FilterOperator.OfType, new Variant(new ExtensionObject(operand1)));

                    // need to chain multiple types together with an OR clause.
                    if (element2 != null)
                    {
                        element2 = whereClause.Push(FilterOperator.Or, new Variant(new ExtensionObject(element2)), new Variant(new ExtensionObject(element3)));
                    }
                    else
                    {
                        element2 = element3;
                    }
                }

                // need to link the set of event types with the previous filters.
                if (element1 != null)
                {
                    whereClause.Push(FilterOperator.And, new Variant(new ExtensionObject(element1)), new Variant(new ExtensionObject(element2)));
                }
            }

            filter.WhereClause = whereClause;

            // return filter.
            return filter;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Collects the fields for the event type.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="eventTypeId">The event type id.</param>
        /// <param name="eventFields">The event fields.</param>
        /// <param name="foundNodes">The table of nodes already browsed, shared by the whole call.</param>
        /// <param name="ct">The token to cancel the request</param>
        private async Task CollectFieldsAsync(
            ISession session,
            NodeId eventTypeId,
            List<SimpleAttributeOperand> eventFields,
            Dictionary<NodeId, List<QualifiedName>> foundNodes,
            CancellationToken ct = default)
        {
            // get the supertypes.
            List<ReferenceDescription> supertypes = await FormUtils.BrowseSuperTypesAsync(session, eventTypeId, false, ct);

            if (supertypes == null)
            {
                return;
            }

            // process the types starting from the top of the tree.
            List<QualifiedName> parentPath = new List<QualifiedName>();

            for (int ii = supertypes.Count - 1; ii >= 0; ii--)
            {
                await CollectFieldsAsync(session, (NodeId)supertypes[ii].NodeId, parentPath, eventFields, foundNodes, ct);
            }

            // collect the fields for the selected type.
            await CollectFieldsAsync(session, eventTypeId, parentPath, eventFields, foundNodes, ct);
        }

        /// <summary>
        /// Collects the fields for the instance node.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodeId">The node id.</param>
        /// <param name="parentPath">The parent path.</param>
        /// <param name="eventFields">The event fields.</param>
        /// <param name="foundNodes">The table of found nodes.</param>
        /// <param name="ct">The token to cancel the request</param>
        private async Task CollectFieldsAsync(
            ISession session,
            NodeId nodeId,
            List<QualifiedName> parentPath,
            List<SimpleAttributeOperand> eventFields,
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

            List<ReferenceDescription> children = await FormUtils.BrowseAsync(session, nodeToBrowse, false, ct);

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
                if (!ContainsPath(eventFields, browsePath))
                {
                    SimpleAttributeOperand field = new SimpleAttributeOperand();

                    field.TypeDefinitionId = ObjectTypeIds.BaseEventType;
                    field.BrowsePath = browsePath;
                    field.AttributeId = (child.NodeClass == NodeClass.Variable) ? Attributes.Value : Attributes.NodeId;

                    eventFields.Add(field);
                }

                // recusively find all of the children.
                NodeId targetId = (NodeId)child.NodeId;

                // need to guard against loops.
                if (foundNodes.TryAdd(targetId, browsePath))
                {
                    await CollectFieldsAsync(session, (NodeId)child.NodeId, browsePath, eventFields, foundNodes, ct);
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
        private bool ContainsPath(List<SimpleAttributeOperand> selectClause, List<QualifiedName> browsePath)
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
                    return true;
                }
            }

            return false;
        }
        #endregion
    }
}
