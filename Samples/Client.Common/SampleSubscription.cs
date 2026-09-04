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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Samples.Client
{
    // the V2 subscription engine reuses names the classic engine already has in the
    // Opc.Ua.Client namespace, which wins over a using directive at the top of the file.
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// A subscription of the V2 engine and the monitored items a caller is editing on it,
    /// without the grid which displays them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The subscribe wizards of the shared control library - the data one and the event
    /// one - each owned a copy of this: create or adopt a subscription, hand out item
    /// names, add the items which are new, wait for the engine to apply them, read back
    /// what the server revised, change a monitoring mode, describe the subscription for a
    /// status field. None of it needs a window, and the two copies had already drifted.
    /// </para>
    /// <para>
    /// <b>Adding an item is the request.</b> The V2 engine has no <c>ApplyChanges</c>:
    /// adding an item to the collection, or reconfiguring the options of one which is
    /// already there, is what asks the server, and the engine carries it out on its own
    /// worker. A caller which wants to show what the server answered therefore waits for
    /// that worker to settle first, which is what <see cref="ApplyAsync"/> does.
    /// </para>
    /// <para>
    /// <b>A subscription belongs to its session.</b> It survives a reconnect together with
    /// its monitored items, because the managed session keeps its identity; it has to be
    /// let go only when the caller is handed a <em>different</em> session, which is what
    /// <see cref="ChangeSession"/> decides.
    /// </para>
    /// </remarks>
    public sealed class SampleSubscription
    {
        /// <summary>
        /// How long a caller waits by default for the engine to apply its item changes.
        /// </summary>
        public static readonly TimeSpan DefaultApplyTimeout = TimeSpan.FromSeconds(10);

        private readonly List<MonitoredItemHandle> m_items = new List<MonitoredItemHandle>();
        private int m_nextItemId;

        /// <summary>
        /// The callbacks the subscription is created with. A caller points them at its own
        /// methods before creating the subscription.
        /// </summary>
        public SubscriptionCallbacks Callbacks { get; } = new SubscriptionCallbacks();

        /// <summary>
        /// The handler to pass when the subscription is created on the caller's behalf.
        /// </summary>
        /// <remarks>
        /// The V2 engine takes the notification handler when the subscription is created,
        /// so a caller which creates the subscription itself has to pass this one.
        /// </remarks>
        public ISubscriptionNotificationHandler NotificationHandler => Callbacks;

        /// <summary>
        /// The session the subscription lives on, or null.
        /// </summary>
        public ISession Session { get; private set; }

        /// <summary>
        /// The subscription, or null while there is none.
        /// </summary>
        public ISubscription Subscription { get; private set; }

        /// <summary>
        /// The options monitor the subscription was created with, or null when the
        /// subscription was adopted without one and cannot be reconfigured.
        /// </summary>
        public OptionsMonitor<SubscriptionOptions> Options { get; private set; }

        /// <summary>
        /// True while there is a subscription.
        /// </summary>
        public bool HasSubscription => Subscription != null;

        /// <summary>
        /// True while the subscription itself can be reconfigured, which needs the options
        /// monitor it was created with.
        /// </summary>
        public bool CanEditSubscription => Subscription != null && Options != null;

        /// <summary>
        /// The items the caller is editing, in the order they were added.
        /// </summary>
        public IReadOnlyList<MonitoredItemHandle> Items => m_items;

        /// <summary>
        /// Takes a new session, and lets the subscription go when it does not belong to it.
        /// </summary>
        /// <param name="session">The new session, which may be null.</param>
        /// <returns>True when the subscription was let go and the caller has to clear what
        /// it displays.</returns>
        public bool ChangeSession(ISession session)
        {
            if (ReferenceEquals(session, Session))
            {
                return false;
            }

            Session = session;

            if (Subscription == null || IsOwnedBy(session, Subscription))
            {
                return false;
            }

            Subscription = null;
            Options = null;
            m_items.Clear();

            return true;
        }

        /// <summary>
        /// Creates the subscription on the session.
        /// </summary>
        /// <param name="session">The session, which has to run the V2 subscription engine.</param>
        /// <param name="options">The options of the subscription, or null for the defaults
        /// the samples use.</param>
        public ISubscription Create(ISession session, SubscriptionOptions options = null)
        {
            var monitor = new OptionsMonitor<SubscriptionOptions>(options ?? SampleSession.DefaultSubscriptionOptions);

            Adopt(SampleSession.AddSubscription(session, Callbacks, monitor), session, monitor);

            return Subscription;
        }

        /// <summary>
        /// Takes over a subscription somebody else created.
        /// </summary>
        /// <param name="subscription">The subscription, created with <see cref="NotificationHandler"/>.</param>
        /// <param name="session">The session the subscription was created on. A V2
        /// subscription does not point back at its session, so it has to be told.</param>
        /// <param name="options">The options monitor the subscription was created with, so
        /// that it can be reconfigured. Optional.</param>
        public void Adopt(
            ISubscription subscription,
            ISession session,
            OptionsMonitor<SubscriptionOptions> options = null)
        {
            Session = session;
            Subscription = subscription;
            Options = options;

            m_items.Clear();
        }

        /// <summary>
        /// Adds a handle for an item which has not reached the server yet.
        /// </summary>
        /// <remarks>
        /// The item is created on the server by the next <see cref="ApplyAsync"/>, which is
        /// what lets a wizard collect a page of items and send them in one go.
        /// </remarks>
        /// <param name="options">The settings of the item.</param>
        public MonitoredItemHandle Add(MonitoredItemOptions options)
        {
            var handle = new MonitoredItemHandle(Utils.Format("Item{0}", ++m_nextItemId), options);

            m_items.Add(handle);

            return handle;
        }

        /// <summary>
        /// Removes an item, from the subscription as well when it reached the server.
        /// </summary>
        /// <param name="handle">The item to remove.</param>
        public void Remove(MonitoredItemHandle handle)
        {
            ArgumentNullException.ThrowIfNull(handle);

            if (handle.Item != null && Subscription != null)
            {
                Subscription.MonitoredItems.TryRemove(handle.Item.ClientHandle);
            }

            m_items.Remove(handle);
        }

        /// <summary>
        /// Forgets every item without touching the server.
        /// </summary>
        public void Clear()
        {
            m_items.Clear();
        }

        /// <summary>
        /// Creates the items which are still pending and waits for the engine to apply
        /// everything that is outstanding.
        /// </summary>
        /// <remarks>
        /// Returns once the engine has settled, so the revised values of
        /// <see cref="MonitoredItemHandle.Item"/> are the ones the server answered with.
        /// </remarks>
        /// <param name="timeout">How long to wait for the engine, or null for
        /// <see cref="DefaultApplyTimeout"/>.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task ApplyAsync(TimeSpan? timeout = null, CancellationToken ct = default)
        {
            if (Subscription == null)
            {
                return;
            }

            foreach (MonitoredItemHandle handle in m_items)
            {
                if (handle.Item == null)
                {
                    Subscription.MonitoredItems.TryAdd(handle.Name, handle.Options, out IMonitoredItem item);
                    handle.Item = item;
                }
            }

            await SampleSession
                .WaitForPendingChangesAsync(Subscription, timeout ?? DefaultApplyTimeout, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Waits for the engine to apply what is outstanding, without adding anything.
        /// </summary>
        /// <remarks>
        /// This is what follows a change to the options of the subscription or of an item:
        /// reconfiguring the monitor is the request, and the revised values are only there
        /// once the engine has caught up.
        /// </remarks>
        /// <param name="timeout">How long to wait, or null for <see cref="DefaultApplyTimeout"/>.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task WaitForChangesAsync(TimeSpan? timeout = null, CancellationToken ct = default)
        {
            if (Subscription == null)
            {
                return Task.CompletedTask;
            }

            return SampleSession.WaitForPendingChangesAsync(Subscription, timeout ?? DefaultApplyTimeout, ct);
        }

        /// <summary>
        /// Puts a set of items into a monitoring mode and waits for the engine.
        /// </summary>
        /// <remarks>
        /// Reconfiguring the options is the request: the engine sends SetMonitoringMode for
        /// the items which already exist and creates the rest with the new mode.
        /// </remarks>
        /// <param name="handles">The items to change.</param>
        /// <param name="monitoringMode">The mode to put them in.</param>
        /// <param name="timeout">How long to wait for the engine, or null for the default.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task SetMonitoringModeAsync(
            IEnumerable<MonitoredItemHandle> handles,
            MonitoringMode monitoringMode,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(handles);

            foreach (MonitoredItemHandle handle in handles)
            {
                handle.Configure(options => options with { MonitoringMode = monitoringMode });
            }

            return WaitForChangesAsync(timeout, ct);
        }

        /// <summary>
        /// The handle of a monitored item the engine reported a notification for.
        /// </summary>
        /// <param name="monitoredItem">The item, which may be null.</param>
        public MonitoredItemHandle Find(IMonitoredItem monitoredItem)
        {
            if (monitoredItem == null)
            {
                return null;
            }

            foreach (MonitoredItemHandle handle in m_items)
            {
                if (ReferenceEquals(handle.Item, monitoredItem))
                {
                    return handle;
                }
            }

            return null;
        }

        /// <summary>
        /// True when the subscription belongs to the subscription manager of the session.
        /// </summary>
        /// <param name="session">The session, which may be null.</param>
        /// <param name="subscription">The subscription.</param>
        public static bool IsOwnedBy(ISession session, ISubscription subscription)
        {
            if (session == null || !session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                return false;
            }

            foreach (ISubscription item in manager.Items)
            {
                if (ReferenceEquals(item, subscription))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The revised settings of a subscription, as one line for a status field:
        /// whether publishing is on, the publishing interval, and the keep alive and
        /// lifetime the counts work out to.
        /// </summary>
        /// <param name="subscription">The subscription to describe.</param>
        public static string Describe(ISubscription subscription)
        {
            ArgumentNullException.ThrowIfNull(subscription);

            var buffer = new StringBuilder();
            double publishingInterval = subscription.CurrentPublishingInterval.TotalMilliseconds;

            buffer.Append(subscription.CurrentPublishingEnabled ? "Enabled" : "Disabled");
            buffer.Append(" (");
            buffer.Append(publishingInterval);
            buffer.Append("ms/");
            buffer.Append(publishingInterval * subscription.CurrentKeepAliveCount / 1000);
            buffer.Append("s/");
            buffer.Append(publishingInterval * subscription.CurrentLifetimeCount / 1000);
            buffer.Append("s}");

            return buffer.ToString();
        }
    }
}
