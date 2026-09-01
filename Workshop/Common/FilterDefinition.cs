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

namespace Quickstarts
{
    // the V2 subscription engine reuses a name the classic engine has in Opc.Ua.Client.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// Defines a event filter for a subscription.
    /// </summary>
    public class FilterDefinition
    {
        #region Public Interface
        #pragma warning disable CA1051 // Justification: sample API intentionally exposes fields for simple filter definitions.
        /// <summary>
        /// The NodeId for the Area that is subscribed to (the entire Server if not specified).
        /// </summary>
        public NodeId AreaId;

        /// <summary>
        /// The minimum severity for the events of interest.
        /// </summary>
        public EventSeverity Severity;

        /// <summary>
        /// The types for the events of interest.
        /// </summary>
        public IList<NodeId> EventTypes;

        /// <summary>
        /// The select clauses to use with the filter.
        /// </summary>
        public IList<SimpleAttributeOperand> SelectClauses;
        #pragma warning restore CA1051

        /// <summary>
        /// Creates the settings of a monitored item based on the current definition.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns>The settings the item is created with.</returns>
        /// <remarks>
        /// The V2 subscription engine takes the settings of an item as options instead of a
        /// mutable monitored item, and the event filter has to be part of the options the item
        /// is created with: it cannot be pushed in afterwards.
        /// </remarks>
        public MonitoredItemOptions CreateMonitoredItemOptions(ISession session)
        {
            // choose the server object by default.
            if (AreaId.IsNull)
            {
                AreaId = ObjectIds.Server;
            }

            return new MonitoredItemOptions {
                StartNodeId = AreaId,
                AttributeId = Attributes.EventNotifier,
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = TimeSpan.Zero,
                QueueSize = UInt32.MaxValue,
                DiscardOldest = true,
                Filter = ConstructFilter(session),
            };
        }

        /// <summary>
        /// Constructs the select clauses for a set of event types.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="ct">A token to cancel the operation with</param>
        /// <param name="eventTypeIds">The event type ids.</param>
        /// <returns>The select clauses for all fields discovered.</returns>
        /// <remarks>
        /// Each event type is an ObjectType in the address space. The fields supported by the
        /// server are defined as children of the ObjectType. Many of the fields are manadatory
        /// and are defined by the UA information model, however, indiviudual servers many not
        /// support all of the optional fields.
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

            // add the fields for the selected EventTypes.
            if (eventTypeIds != null)
            {
                for (int ii = 0; ii < eventTypeIds.Length; ii++)
                {
                    await CollectFieldsAsync(session, eventTypeIds[ii], selectClauses, ct);
                }
            }

            // use BaseEventType as the default if no EventTypes specified.
            else
            {
                await CollectFieldsAsync(session, ObjectTypeIds.BaseEventType, selectClauses, ct);
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
        /// <param name="ct">A token to cancel the operation with</param>
        private async Task CollectFieldsAsync(ISession session, NodeId eventTypeId, List<SimpleAttributeOperand> eventFields, CancellationToken ct = default)
        {
            // get the supertypes.
            List<ReferenceDescription> supertypes = await FormUtils.BrowseSuperTypesAsync(session, eventTypeId, false, ct);

            if (supertypes == null)
            {
                return;
            }

            // process the types starting from the top of the tree.
            Dictionary<NodeId, List<QualifiedName>> foundNodes = new Dictionary<NodeId, List<QualifiedName>>();
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
        /// <param name="ct">A token to cancel the operation with</param>
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
