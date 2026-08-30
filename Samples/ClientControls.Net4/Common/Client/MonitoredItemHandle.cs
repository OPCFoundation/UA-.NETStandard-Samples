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
using System.Data;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the enclosing
    // Opc.Ua.Client namespace, which wins over a using directive at the top of the file.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// Everything a control needs to keep about one monitored item of the V2 engine.
    /// </summary>
    /// <remarks>
    /// The V2 engine takes the settings of a monitored item through an
    /// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> and hands back an
    /// <see cref="IMonitoredItem"/> which only reports the revised values. This replaces the
    /// mutable classic <c>MonitoredItem</c> the grids used to edit in place and hang their
    /// <c>Handle</c> off: the pending settings live in <see cref="Options"/>, the item created
    /// from them in <see cref="Item"/> and the grid row in <see cref="Row"/>.
    /// </remarks>
    public sealed class MonitoredItemHandle
    {
        /// <summary>
        /// Creates a handle for an item which has not been added to a subscription yet.
        /// </summary>
        /// <param name="name">The name of the item, unique within its subscription.</param>
        /// <param name="options">The settings the item is created with.</param>
        public MonitoredItemHandle(string name, MonitoredItemOptions options)
        {
            Name = name;
            Options = new OptionsMonitor<MonitoredItemOptions>(options);
        }

        /// <summary>
        /// The name which identifies the item within its subscription.
        /// </summary>
        public string Name { get; }

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
        /// The grid row which displays the item.
        /// </summary>
        public DataRow Row { get; set; }

        /// <summary>
        /// Whether the item exists on the server, which is what decides if the settings that
        /// cannot be modified afterwards are still editable.
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
