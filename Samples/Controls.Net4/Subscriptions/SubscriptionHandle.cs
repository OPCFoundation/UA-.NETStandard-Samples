/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Sample.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the
    // Opc.Ua.Client namespace this file imports, so the V2 types are pinned explicitly.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// Everything the sample controls keep about one subscription of the V2 engine.
    /// </summary>
    /// <remarks>
    /// A V2 subscription does not point back at its session, carries no display name and takes
    /// its notification handler when it is created, so the tree and the dialogs share this
    /// handle instead of the subscription itself: it pairs the subscription with the session it
    /// was created on, the options monitor which reconfigures it, the
    /// <see cref="SubscriptionCallbacks"/> every interested control attaches its delegates to
    /// and the <see cref="MonitoredItemHandle"/> bookkeeping of the items the controls added.
    /// </remarks>
    public sealed class SubscriptionHandle
    {
        private int m_nextItemId;

        /// <summary>
        /// Creates a handle for a subscription which has not been created on the server yet.
        /// </summary>
        /// <param name="session">The session the subscription belongs to.</param>
        /// <param name="displayName">The name the controls display for the subscription.</param>
        /// <param name="options">The options the subscription is created with.</param>
        public SubscriptionHandle(ISession session, string displayName, SubscriptionOptions options)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            DisplayName = displayName;
            Options = new OptionsMonitor<SubscriptionOptions>(options ?? ClientUtils.DefaultSubscriptionOptions);
            Callbacks = new SubscriptionCallbacks();
            Items = new List<MonitoredItemHandle>();
        }

        /// <summary>
        /// The session the subscription belongs to.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// The name the controls display for the subscription.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// The settings of the subscription. Reconfiguring the monitor is what modifies the
        /// subscription on the server.
        /// </summary>
        public OptionsMonitor<SubscriptionOptions> Options { get; }

        /// <summary>
        /// The current settings of the subscription.
        /// </summary>
        public SubscriptionOptions Settings => Options.CurrentValue;

        /// <summary>
        /// The handler the subscription is created with. Controls interested in the
        /// notifications combine their delegates into its callbacks.
        /// </summary>
        public SubscriptionCallbacks Callbacks { get; }

        /// <summary>
        /// The subscription once it has been created, null before that.
        /// </summary>
        public ISubscription Subscription { get; private set; }

        /// <summary>
        /// The monitored items of the subscription the controls keep track of.
        /// </summary>
        public IList<MonitoredItemHandle> Items { get; }

        /// <summary>
        /// Whether the subscription exists on the server.
        /// </summary>
        public bool Created => Subscription != null && Subscription.Created;

        /// <summary>
        /// Creates the subscription on the session with the V2 subscription engine.
        /// </summary>
        public ISubscription Create()
        {
            if (Subscription == null)
            {
                Subscription = ClientUtils.AddSubscription(Session, Callbacks, Options);
            }

            return Subscription;
        }

        /// <summary>
        /// Adopts a subscription which already runs in the engine, e.g. one restored from a
        /// file. The engine holds the options of such a subscription and its items, so the
        /// synthesized item handles only support display and removal, not reconfiguration.
        /// </summary>
        public void Attach(ISubscription subscription)
        {
            Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));

            foreach (IMonitoredItem monitoredItem in subscription.MonitoredItems.Items)
            {
                Items.Add(new MonitoredItemHandle(monitoredItem.Name, new MonitoredItemOptions()) {
                    DisplayName = monitoredItem.Name,
                    Item = monitoredItem,
                });
            }
        }

        /// <summary>
        /// Applies a change to the settings of the subscription.
        /// </summary>
        public void Configure(Func<SubscriptionOptions, SubscriptionOptions> configure)
        {
            Options.Configure(configure);
        }

        /// <summary>
        /// Adds a monitored item to the subscription.
        /// </summary>
        /// <remarks>
        /// Adding the item is the request: the engine creates it on the server from its own
        /// worker, and <see cref="WaitForPendingChangesAsync"/> or the state changed callback
        /// report when the revised values are available.
        /// </remarks>
        /// <param name="displayName">The name the controls display for the item.</param>
        /// <param name="nodeClass">The node class of the monitored node.</param>
        /// <param name="options">The settings the item is created with.</param>
        public MonitoredItemHandle AddItem(string displayName, NodeClass nodeClass, MonitoredItemOptions options)
        {
            MonitoredItemHandle handle = StageItem(displayName, nodeClass, options);
            Push(handle);
            return handle;
        }

        /// <summary>
        /// Registers a monitored item without handing it to the engine yet, so a wizard can
        /// collect several items and add them in one step with <see cref="ApplyChanges"/>.
        /// </summary>
        public MonitoredItemHandle StageItem(string displayName, NodeClass nodeClass, MonitoredItemOptions options)
        {
            // the name has to be unique within the subscription, and an item which was just
            // removed may not have been reaped yet, so every item gets its own name.
            MonitoredItemHandle handle = new MonitoredItemHandle(Utils.Format("Item{0}", ++m_nextItemId), options) {
                DisplayName = displayName,
                NodeClass = nodeClass,
            };

            Items.Add(handle);
            return handle;
        }

        /// <summary>
        /// Hands the staged monitored items to the engine, which creates them on the server
        /// from its own worker.
        /// </summary>
        public void ApplyChanges()
        {
            foreach (MonitoredItemHandle handle in Items)
            {
                Push(handle);
            }
        }

        /// <summary>
        /// Hands one monitored item to the engine unless it already has been.
        /// </summary>
        private void Push(MonitoredItemHandle handle)
        {
            if (Subscription != null && handle.Item == null)
            {
                Subscription.MonitoredItems.TryAdd(handle.Name, handle.Options, out IMonitoredItem item);
                handle.Item = item;
            }
        }

        /// <summary>
        /// Removes a monitored item from the subscription.
        /// </summary>
        public bool RemoveItem(MonitoredItemHandle handle)
        {
            if (handle == null)
            {
                return false;
            }

            if (handle.Item != null && Subscription != null)
            {
                Subscription.MonitoredItems.TryRemove(handle.Item.ClientHandle);
            }

            return Items.Remove(handle);
        }

        /// <summary>
        /// Finds the handle which owns the monitored item a notification came from.
        /// </summary>
        public MonitoredItemHandle FindItem(IMonitoredItem monitoredItem)
        {
            if (monitoredItem == null)
            {
                return null;
            }

            foreach (MonitoredItemHandle handle in Items)
            {
                if (Object.ReferenceEquals(handle.Item, monitoredItem))
                {
                    return handle;
                }
            }

            return null;
        }

        /// <summary>
        /// Waits until the engine has applied the pending monitored item changes.
        /// </summary>
        public Task WaitForPendingChangesAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            if (Subscription == null)
            {
                return Task.CompletedTask;
            }

            return ClientUtils.WaitForPendingChangesAsync(Subscription, timeout, ct);
        }

        /// <summary>
        /// Deletes the subscription on the server and drops it from the subscription manager
        /// of the session.
        /// </summary>
        public async Task DeleteAsync()
        {
            if (Subscription != null)
            {
                await Subscription.DisposeAsync();
                Subscription = null;
            }

            Items.Clear();
        }

        /// <summary>
        /// The event filter the sample subscribes event notifiers with, selecting the fields
        /// of the base event type the dialogs display.
        /// </summary>
        /// <remarks>
        /// The classic engine composed a default filter inside its MonitoredItem; the V2
        /// engine takes the filter through the options the item is created with, so the
        /// controls build it themselves. The first clause selects the node id of the event
        /// source, matching the layout the <see cref="Opc.Ua.Client.Controls.FilterDeclaration"/>
        /// helper produces.
        /// </remarks>
        public static EventFilter CreateDefaultEventFilter()
        {
            List<SimpleAttributeOperand> selectClauses = new List<SimpleAttributeOperand> {
                new SimpleAttributeOperand {
                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                    AttributeId = Attributes.NodeId,
                }
            };

            foreach (string browseName in new string[] {
                Opc.Ua.BrowseNames.EventId,
                Opc.Ua.BrowseNames.EventType,
                Opc.Ua.BrowseNames.SourceNode,
                Opc.Ua.BrowseNames.SourceName,
                Opc.Ua.BrowseNames.Time,
                Opc.Ua.BrowseNames.ReceiveTime,
                Opc.Ua.BrowseNames.Message,
                Opc.Ua.BrowseNames.Severity })
            {
                selectClauses.Add(new SimpleAttributeOperand {
                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                    AttributeId = Attributes.Value,
                    BrowsePath = new QualifiedName[] { new QualifiedName(browseName) }.ToArrayOf(),
                });
            }

            ContentFilter whereClause = new ContentFilter();
            whereClause.Push(FilterOperator.OfType, ObjectTypeIds.BaseEventType);

            return new EventFilter {
                SelectClauses = selectClauses.ToArrayOf(),
                WhereClause = whereClause,
            };
        }

        /// <summary>
        /// Returns the value of an event field selected by the filter the item was created
        /// with, or a null Variant if the filter does not select the field.
        /// </summary>
        /// <remarks>
        /// The fields of a V2 <see cref="EventNotification"/> align one to one with the select
        /// clauses of the event filter, which replaces the field lookup the classic
        /// MonitoredItem provided.
        /// </remarks>
        public static Variant GetEventFieldValue(MonitoredItemHandle handle, EventNotification notification, QualifiedName browseName)
        {
            if (handle?.Settings?.Filter is not EventFilter filter)
            {
                return Variant.Null;
            }

            for (int ii = 0; ii < filter.SelectClauses.Count && ii < notification.Fields.Count; ii++)
            {
                SimpleAttributeOperand clause = filter.SelectClauses[ii];

                if (clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == browseName)
                {
                    return notification.Fields[ii];
                }
            }

            return Variant.Null;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return DisplayName;
        }
    }
}
