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
using Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Forwards the callbacks of the V2 subscription engine to delegates.
    /// </summary>
    /// <remarks>
    /// The engine takes the handler when the subscription is created and never hands it back,
    /// so a control creates one of these up front, keeps it for the lifetime of the control and
    /// points the delegates at its own methods. Those methods run on a publish worker, not on
    /// the UI thread, and marshal themselves with <see cref="System.Windows.Forms.Control.BeginInvoke(Delegate, object[])"/>.
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
        /// Called whenever the state of the subscription changes, which is also what reports
        /// that pending monitored item changes have been applied.
        /// </summary>
        public Action<ISubscription, Opc.Ua.Client.Subscriptions.SubscriptionState, PublishState> StateChangedCallback { get; set; }

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
            // notifications are copied out before they are handed to the control.
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
            Opc.Ua.Client.Subscriptions.SubscriptionState state,
            PublishState publishStateMask,
            CancellationToken ct)
        {
            StateChangedCallback?.Invoke(subscription, state, publishStateMask);
            return default;
        }
    }
}
