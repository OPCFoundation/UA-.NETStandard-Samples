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
using System.Windows.Forms;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    // the V2 subscription engine reuses names the classic engine already has in the
    // Opc.Ua.Client namespace, so the V2 types are pinned explicitly.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;

    /// <summary>
    /// Chooses which monitored items of a subscription one triggering item reports for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Triggering (OPC UA Part 4 §5.13.5) links a triggering item to triggered items: when
    /// the triggering item fires, the queued notifications of the triggered items are
    /// reported in the same publish even though their monitoring mode is <c>Sampling</c>,
    /// which would otherwise suppress them. That is the "sample many, report on demand"
    /// pattern.
    /// </para>
    /// <para>
    /// The relationship is N:M - a triggered item may be linked to several triggering
    /// items - so this dialog only edits the links of the one item it was opened for and
    /// leaves the links other items hold alone.
    /// </para>
    /// </remarks>
    public partial class SetTriggeringDlg : Form
    {
        private MonitoredItemHandle[] m_candidates;

        /// <summary>
        /// Creates the dialog.
        /// </summary>
        public SetTriggeringDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Prompts the user for the items the triggering item should report for.
        /// </summary>
        /// <param name="subscription">The subscription both items belong to.</param>
        /// <param name="triggeringItem">The item whose links are edited.</param>
        /// <param name="linksToAdd">The items which were checked and were not linked.</param>
        /// <param name="linksToRemove">The items which were unchecked and were linked.</param>
        /// <returns>False when the user cancelled or nothing changed.</returns>
        public bool ShowDialog(
            SubscriptionHandle subscription,
            MonitoredItemHandle triggeringItem,
            out IList<MonitoredItemHandle> linksToAdd,
            out IList<MonitoredItemHandle> linksToRemove)
        {
            ArgumentNullException.ThrowIfNull(subscription);
            ArgumentNullException.ThrowIfNull(triggeringItem);

            linksToAdd = new List<MonitoredItemHandle>();
            linksToRemove = new List<MonitoredItemHandle>();

            TriggeringItemTB.Text = triggeringItem.DisplayName;

            // every other item of the subscription is a candidate; an item cannot trigger
            // itself.
            var candidates = new List<MonitoredItemHandle>();
            var linked = new List<bool>();

            foreach (MonitoredItemHandle candidate in subscription.Items)
            {
                if (Object.ReferenceEquals(candidate, triggeringItem))
                {
                    continue;
                }

                candidates.Add(candidate);
                linked.Add(IsTriggeredBy(candidate, triggeringItem));
            }

            m_candidates = candidates.ToArray();

            TriggeredItemsLV.Items.Clear();

            for (int ii = 0; ii < m_candidates.Length; ii++)
            {
                TriggeredItemsLV.Items.Add(m_candidates[ii].DisplayName, linked[ii]);
            }

            OkBTN.Enabled = m_candidates.Length > 0;

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            for (int ii = 0; ii < m_candidates.Length; ii++)
            {
                bool wanted = TriggeredItemsLV.GetItemChecked(ii);

                if (wanted && !linked[ii])
                {
                    linksToAdd.Add(m_candidates[ii]);
                }
                else if (!wanted && linked[ii])
                {
                    linksToRemove.Add(m_candidates[ii]);
                }
            }

            return linksToAdd.Count > 0 || linksToRemove.Count > 0;
        }

        /// <summary>
        /// Whether an item is currently linked to the triggering item.
        /// </summary>
        /// <remarks>
        /// The desired state is what both the declarative and the imperative path write,
        /// but they write it in different places: an item which the engine created carries
        /// it in <see cref="IMonitoredItem.TriggeringItems"/>, an item which is only staged
        /// carries it in the <c>TriggeredByNames</c> of the options it will be created with.
        /// </remarks>
        public static bool IsTriggeredBy(MonitoredItemHandle candidate, MonitoredItemHandle triggeringItem)
        {
            if (candidate.Item != null)
            {
                foreach (IMonitoredItem item in candidate.Item.TriggeringItems)
                {
                    if (item.Name == triggeringItem.Name)
                    {
                        return true;
                    }
                }

                return false;
            }

            IReadOnlyList<string> triggeredBy = candidate.Settings.TriggeredByNames;

            if (triggeredBy != null)
            {
                foreach (string name in triggeredBy)
                {
                    if (name == triggeringItem.Name)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The names of the items which trigger an item, for display in a grid.
        /// </summary>
        public static string GetTriggeredByDisplayText(SubscriptionHandle subscription, MonitoredItemHandle handle)
        {
            var names = new List<string>();

            if (handle.Item != null)
            {
                foreach (IMonitoredItem item in handle.Item.TriggeringItems)
                {
                    names.Add(DisplayNameOf(subscription, item.Name));
                }
            }
            else if (handle.Settings.TriggeredByNames != null)
            {
                foreach (string name in handle.Settings.TriggeredByNames)
                {
                    names.Add(DisplayNameOf(subscription, name));
                }
            }

            return String.Join(", ", names);
        }

        /// <summary>
        /// Maps the name the engine identifies an item by to the name the grids show.
        /// </summary>
        private static string DisplayNameOf(SubscriptionHandle subscription, string name)
        {
            if (subscription != null)
            {
                foreach (MonitoredItemHandle handle in subscription.Items)
                {
                    if (handle.Name == name)
                    {
                        return handle.DisplayName;
                    }
                }
            }

            return name;
        }
    }
}
