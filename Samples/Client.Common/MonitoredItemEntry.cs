/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Samples.Client
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, so
    // the client types are aliased.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// Everything a client model keeps about one monitored item of the V2 engine.
    /// </summary>
    /// <remarks>
    /// The V2 engine takes the settings of a monitored item through an options monitor and
    /// hands back an <see cref="IMonitoredItem"/> which only reports the revised values.
    /// The pending settings live in <see cref="Options"/>, the item created from them in
    /// <see cref="Item"/>.
    /// </remarks>
    public sealed class MonitoredItemEntry
    {
        private string m_displayName;

        /// <summary>
        /// Creates an entry for an item which has not been added to a subscription yet.
        /// </summary>
        /// <param name="name">The name of the item, unique within its subscription.</param>
        /// <param name="options">The settings the item is created with.</param>
        public MonitoredItemEntry(string name, MonitoredItemOptions options)
        {
            Name = name;
            Options = new OptionsMonitor<MonitoredItemOptions>(options);
        }

        /// <summary>
        /// The name which identifies the item within its subscription.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The name a window displays for the item. Defaults to <see cref="Name"/>.
        /// </summary>
        public string DisplayName
        {
            get => m_displayName ?? Name;
            set => m_displayName = value;
        }

        /// <summary>
        /// The node class of the node the item monitors.
        /// </summary>
        public NodeClass NodeClass { get; set; } = NodeClass.Variable;

        /// <summary>
        /// The settings of the item. Reconfiguring the monitor is what modifies the item.
        /// </summary>
        public OptionsMonitor<MonitoredItemOptions> Options { get; }

        /// <summary>
        /// The current settings of the item.
        /// </summary>
        public MonitoredItemOptions Settings => Options.CurrentValue;

        /// <summary>
        /// The item once it has been added to the subscription, null before that.
        /// </summary>
        public IMonitoredItem Item { get; set; }

        /// <summary>
        /// Whether the item exists on the server.
        /// </summary>
        public bool Created => Item != null && Item.Created;

        /// <summary>
        /// Applies a change to the settings of the item.
        /// </summary>
        public void Configure(Func<MonitoredItemOptions, MonitoredItemOptions> configure)
        {
            Options.Configure(configure);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Name;
        }
    }
}
