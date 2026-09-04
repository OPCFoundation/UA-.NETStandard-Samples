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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// Forwards the callbacks of the V2 subscription engine to delegates.
    /// </summary>
    /// <remarks>
    /// The engine takes the handler when the subscription is created and never hands it
    /// back, so a caller creates one of these up front, keeps it for its whole lifetime and
    /// points the delegates at its own methods. Those methods run on a publish worker of
    /// the engine, not on the thread of a window: a model turns what they deliver into
    /// events, which its base class posts to the thread the model was created on, and a
    /// control marshals them itself.
    /// </remarks>
    public sealed class SubscriptionCallbacks : ISubscriptionNotificationHandler
    {
        /// <summary>
        /// Called for every data change notification.
        /// </summary>
        public Action<ISubscription, uint, DateTime, DataValueChange[], PublishState> DataChangeCallback { get; set; }

        /// <summary>
        /// Called for every event notification.
        /// </summary>
        public Action<ISubscription, uint, DateTime, EventNotification[], PublishState> EventCallback { get; set; }

        /// <summary>
        /// Called for every keep alive notification.
        /// </summary>
        public Action<ISubscription, uint, DateTime, PublishState> KeepAliveCallback { get; set; }

        /// <summary>
        /// Called whenever the state of the subscription changes, which is also what
        /// reports that pending monitored item changes have been applied.
        /// </summary>
        public Action<ISubscription, SubscriptionState, PublishState> StateChangedCallback { get; set; }

        /// <inheritdoc/>
        ValueTask ISubscriptionNotificationHandler.OnDataChangeNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<DataValueChange> notifications,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            // the buffer belongs to the engine and may be recycled once this returns, so the
            // notifications are copied out before they are handed on
            DataChangeCallback?.Invoke(subscription, sequenceNumber, publishTime, notifications.ToArray(), publishStateMask);
            return default;
        }

        /// <inheritdoc/>
        ValueTask ISubscriptionNotificationHandler.OnEventDataNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<EventNotification> notifications,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            EventCallback?.Invoke(subscription, sequenceNumber, publishTime, notifications.ToArray(), publishStateMask);
            return default;
        }

        /// <inheritdoc/>
        ValueTask ISubscriptionNotificationHandler.OnKeepAliveNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            PublishState publishStateMask)
        {
            KeepAliveCallback?.Invoke(subscription, sequenceNumber, publishTime, publishStateMask);
            return default;
        }

        /// <inheritdoc/>
        ValueTask ISubscriptionNotificationHandler.OnSubscriptionStateChangedAsync(
            ISubscription subscription,
            SubscriptionState state,
            PublishState publishStateMask,
            CancellationToken ct)
        {
            StateChangedCallback?.Invoke(subscription, state, publishStateMask);
            return default;
        }
    }
}
