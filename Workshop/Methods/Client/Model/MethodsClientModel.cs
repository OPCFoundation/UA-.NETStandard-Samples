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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Samples.Client;

namespace Quickstarts.MethodsClient.Model
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, and
    // Opc.Ua itself has a server side IMonitoredItem, so the client types are aliased.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// What the Start method answered: the states the server accepted, which it echoes
    /// back as its output arguments.
    /// </summary>
    /// <param name="RevisedInitialState">The initial state the process was started with.</param>
    /// <param name="RevisedFinalState">The final state the process runs towards.</param>
    public sealed record StartResult(uint RevisedInitialState, uint RevisedFinalState);

    /// <summary>
    /// The payload of <see cref="MethodsClientModel.StateChanged"/>.
    /// </summary>
    public sealed class StateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public StateChangedEventArgs(DataValue value)
        {
            Value = value;
        }

        /// <summary>
        /// The new value of the state variable.
        /// </summary>
        public DataValue Value { get; }
    }

    /// <summary>
    /// The client model of the Methods client: finds the process the server offers,
    /// starts it through its Start method and watches its state.
    /// </summary>
    /// <remarks>
    /// This client has built-in knowledge of the information model of its server: the
    /// process, its state variable and its Start method are found by browse path when the
    /// session is attached, and the state is watched through one subscription on the V2
    /// engine. <see cref="StartAsync"/> calls the method; every change of the state is
    /// reported through <see cref="StateChanged"/> on the thread the model was created on.
    /// </remarks>
    public sealed class MethodsClientModel : SampleClientModel
    {
        /// <summary>
        /// The namespace of the nodes provided by the server.
        /// </summary>
        public const string MethodsNamespaceUri = Namespaces.Methods;

        /// <summary>
        /// The name which identifies the state item within its subscription.
        /// </summary>
        private const string kStateItemName = "State";

        // the V2 engine takes the notification handler when the subscription is created,
        // so the model owns one for its whole lifetime and points it at its own method.
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();

        private ISubscription m_subscription;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public MethodsClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
            m_callbacks.DataChangeCallback = OnDataChanges;
        }

        /// <summary>
        /// The process object, or a null node id while the model is detached.
        /// </summary>
        public NodeId ProcessNodeId { get; private set; } = NodeId.Null;

        /// <summary>
        /// The Start method of the process, or a null node id while the model is detached.
        /// </summary>
        public NodeId StartMethodNodeId { get; private set; } = NodeId.Null;

        /// <summary>
        /// The state variable of the process, or a null node id while the model is detached.
        /// </summary>
        public NodeId StateNodeId { get; private set; } = NodeId.Null;

        /// <summary>
        /// Raised for every change of the state of the process.
        /// </summary>
        public event EventHandler<StateChangedEventArgs> StateChanged;

        /// <summary>
        /// Starts the process, which then ramps its state from the initial to the final
        /// value one step per second.
        /// </summary>
        /// <param name="initialState">The state to start from.</param>
        /// <param name="finalState">The state to stop at.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>
        /// The states the server accepted, or null when it answered with fewer than the
        /// two output arguments the method declares.
        /// </returns>
        public async Task<StartResult> StartAsync(uint initialState, uint finalState, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            if (ProcessNodeId.IsNull || StartMethodNodeId.IsNull)
            {
                throw new InvalidOperationException("The server does not offer the process this client knows.");
            }

            ArrayOf<Variant> outputArguments = await session.CallAsync(
                ProcessNodeId,
                StartMethodNodeId,
                ct,
                initialState,
                finalState).ConfigureAwait(false);

            // the method declares two UInt32 outputs; anything else is not the process
            // this client knows and is reported as no result rather than guessed at.
            if (outputArguments == null
                || outputArguments.Count < 2
                || !outputArguments[0].TryGetValue(out uint revisedInitialState)
                || !outputArguments[1].TryGetValue(out uint revisedFinalState))
            {
                return null;
            }

            return new StartResult(revisedInitialState, revisedFinalState);
        }

        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            // this client has built-in knowledge of the information model used by the server.
            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(Namespaces.Methods);

            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                Opc.Ua.ObjectIds.ObjectsFolder,
                wellKnownNamespaceUris,
                ct,
                "1:My Process/1:State",
                "1:My Process",
                "1:My Process/1:Start").ConfigureAwait(false);

            // subscribe to the state if available.
            if (nodes.Count > 0 && !nodes[0].IsNull)
            {
                StateNodeId = nodes[0];

                await DeleteSubscriptionAsync().ConfigureAwait(false);

                // the V2 engine takes the settings through an options monitor and creates
                // the subscription on the server on its own worker.
                var options = new OptionsMonitor<SubscriptionOptions>(
                    SampleSession.DefaultSubscriptionOptions with { Priority = 1, LifetimeCount = 20 });

                ISubscription subscription = SampleSession.AddSubscription(session, m_callbacks, options);
                m_subscription = subscription;

                // adding the item to the collection is the create request: the engine
                // applies it on its own worker, there is no ApplyChanges to call.
                subscription.MonitoredItems.TryAdd(
                    kStateItemName,
                    new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions {
                        StartNodeId = nodes[0],
                        AttributeId = Attributes.Value,
                    }),
                    out IMonitoredItem _);
            }

            // save the object/method
            if (nodes.Count > 2)
            {
                ProcessNodeId = nodes[1];
                StartMethodNodeId = nodes[2];
            }
        }

        /// <inheritdoc/>
        protected override async Task OnDetachingAsync()
        {
            // done before the session is closed: closing a session which still carries a
            // subscription waits for the publish pipeline to drain.
            await DeleteSubscriptionAsync().ConfigureAwait(false);

            ProcessNodeId = NodeId.Null;
            StartMethodNodeId = NodeId.Null;
            StateNodeId = NodeId.Null;
        }

        // a V2 subscription belongs to the subscription manager of the session and survives
        // a reconnect together with its monitored items, so there is nothing to re-attach:
        // the reconnect hooks of the base class are not overridden. Restarting the process
        // after a reconnect is a choice of the window, which knows what the user typed.

        /// <summary>
        /// Deletes the subscription on the server and drops it from the subscription manager.
        /// </summary>
        private async Task DeleteSubscriptionAsync()
        {
            ISubscription subscription = m_subscription;

            m_subscription = null;

            if (subscription != null)
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reports the new value of the state variable.
        /// </summary>
        /// <remarks>
        /// The V2 engine calls this on a publish worker and reports the whole notification
        /// instead of one value per item. Each change of the state is turned into one
        /// event, which the base class posts to the thread the model was created on.
        /// </remarks>
        private void OnDataChanges(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            DataValueChange[] notifications,
            PublishState publishState)
        {
            foreach (DataValueChange change in notifications)
            {
                if (change.MonitoredItem?.Name == kStateItemName)
                {
                    Raise(StateChanged, new StateChangedEventArgs(change.Value));
                }
            }
        }
    }
}
