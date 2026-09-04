/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Samples.Client;

namespace Quickstarts.AlarmConditionClient.Model
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, and
    // Opc.Ua itself has a server side IMonitoredItem, so the client types are aliased.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// The client model of the Alarm Condition client: subscribes to the condition events
    /// of an area, keeps the current state of every condition it hears about, and calls
    /// the Part 9 Methods of the conditions the operator picks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window hands the session over with <see cref="SampleClientModel.AttachAsync"/>.
    /// The model then creates one subscription on the V2 engine with one monitored item
    /// for the current <see cref="FilterDefinition"/>, asks the server for a condition
    /// refresh so the list starts out with everything the server retains, and reports
    /// every condition it decodes through <see cref="ConditionChanged"/> as a
    /// <see cref="ConditionSnapshot"/>. Changing the filter replaces the item - the event
    /// filter of an item cannot be modified after it was created - and clears the list,
    /// which <see cref="ConditionsCleared"/> announces.
    /// </para>
    /// <para>
    /// <b>Threading.</b> The engine delivers a batch of notifications on a publish worker
    /// and expects the callback to return quickly, but decoding one event awaits: the
    /// supertypes of an unknown event type are browsed, the type node is looked up in the
    /// node cache. Doing that in a fire and forget continuation lets the next batch
    /// overtake the first at every await, which is how the list of this sample used to
    /// end up with rows without a condition and duplicate keys in the type mapping. So the
    /// callback only queues each notification into a <see cref="SerialNotificationPump{T}"/>
    /// and one consumer does the awaiting, in arrival order, one event at a time. That
    /// consumer is also the only writer of the condition table.
    /// </para>
    /// <para>
    /// The Part 9 Methods go through the <see cref="AlarmClient"/> of the SDK, a facade
    /// over the generated proxies of the types which declare them: the model never has to
    /// know a Method NodeId, and the facade picks the "2" variant of a Method by itself
    /// when a comment is supplied. Each call answers per condition, so that the window can
    /// write a refusal into the row it belongs to.
    /// </para>
    /// </remarks>
    public sealed class AlarmConditionClientModel : SampleClientModel
    {
        /// <summary>
        /// How long the model waits for the subscription engine to apply the item changes.
        /// </summary>
        private static readonly TimeSpan kApplyTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// The browse name the sample server gives the flag which suppresses a source.
        /// </summary>
        private const string kMaintenanceMode = "MaintenanceMode";

        // the V2 engine takes the notification handler when the subscription is created,
        // so the model owns one for its whole lifetime and points it at its own method.
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();

        // the fields of a notification line up with the select clauses of the filter the
        // item was created with, and the engine does not report that filter back. The
        // model keeps it per item, keyed by the client handle the notification names, so
        // that a notification of an item which a filter change already removed is dropped
        // instead of being decoded with the select clauses of its successor.
        private readonly ConcurrentDictionary<uint, EventFilter> m_filtersByHandle = new ConcurrentDictionary<uint, EventFilter>();

        // only the consumer of the pump touches these two tables
        private readonly Dictionary<NodeId, Type> m_knownEventTypes = ConditionEventTypes.CreateKnownTypes();
        private readonly Dictionary<NodeId, NodeId> m_eventTypeMappings = new Dictionary<NodeId, NodeId>();

        // written by the consumer of the pump, read by the Method calls of the window
        private readonly Dictionary<ConditionKey, ConditionEntry> m_conditions = new Dictionary<ConditionKey, ConditionEntry>();
        private readonly object m_lock = new object();

        private readonly FilterDefinition m_filter;
        private AlarmClient m_alarms;
        private ISubscription m_subscription;
        private MonitoredItemEntry m_monitoredItem;
        private SerialNotificationPump<EventNotification> m_pump;
        private int m_nextItemId;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public AlarmConditionClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
            m_filter = new FilterDefinition {
                AreaId = ObjectIds.Server,
                Severity = EventSeverity.Min,
                IgnoreSuppressedOrShelved = true,
                EventTypes = new NodeId[] { ObjectTypeIds.ConditionType },
            };

            m_callbacks.EventCallback = OnEvents;
        }

        /// <summary>
        /// The area whose conditions are listed. The Server object by default, which
        /// delivers the conditions of every area.
        /// </summary>
        public NodeId AreaId => m_filter.AreaId;

        /// <summary>
        /// The lowest severity which is listed.
        /// </summary>
        public EventSeverity Severity => m_filter.Severity;

        /// <summary>
        /// The event types which are listed, as an OfType clause of the filter.
        /// </summary>
        public IReadOnlyList<NodeId> EventTypes => m_filter.EventTypes.ToArray();

        /// <summary>
        /// The current state of every condition the model has heard about since the list
        /// was last cleared.
        /// </summary>
        public IReadOnlyList<ConditionSnapshot> Conditions
        {
            get
            {
                lock (m_lock)
                {
                    return m_conditions.Values.Select(entry => entry.Snapshot).ToArray();
                }
            }
        }

        /// <summary>
        /// Raised for every condition event which was decoded: the snapshot carries the
        /// state of the condition after the event.
        /// </summary>
        public event EventHandler<ConditionChangedEventArgs> ConditionChanged;

        /// <summary>
        /// Raised when the list starts over: at the start of a condition refresh, after
        /// the filter changed, and when the model detaches.
        /// </summary>
        public event EventHandler<EventArgs> ConditionsCleared;

        #region Filter
        /// <summary>
        /// Lists the conditions of another area.
        /// </summary>
        /// <param name="areaId">The area, or a null node id for the whole server.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task SetAreaAsync(NodeId areaId, CancellationToken ct = default)
        {
            m_filter.AreaId = areaId.IsNull ? ObjectIds.Server : areaId;
            return UpdateFilterAsync(ct);
        }

        /// <summary>
        /// Lists only the conditions of at least the given severity.
        /// </summary>
        /// <param name="severity">The lowest severity of interest.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task SetSeverityAsync(EventSeverity severity, CancellationToken ct = default)
        {
            m_filter.Severity = severity;
            return UpdateFilterAsync(ct);
        }

        /// <summary>
        /// Lists only the conditions of the given event types, subtypes included.
        /// </summary>
        /// <param name="eventTypes">The event types.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task SetEventTypesAsync(IReadOnlyList<NodeId> eventTypes, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(eventTypes);

            m_filter.EventTypes = eventTypes.ToList();
            return UpdateFilterAsync(ct);
        }

        /// <summary>
        /// Asks the server to replay every condition it retains.
        /// </summary>
        /// <remarks>
        /// The server announces the replay with a RefreshStart event, which clears the
        /// list, and ends it with a RefreshEnd event; the conditions arrive in between,
        /// through <see cref="ConditionChanged"/> like any other event.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public Task RefreshAsync(CancellationToken ct = default)
        {
            RequireSession();

            return RefreshItemAsync(ct);
        }
        #endregion

        #region Part 9 Methods
        /// <summary>
        /// Enables the conditions.
        /// </summary>
        public Task<IReadOnlyList<ConditionCallResult>> EnableAsync(IEnumerable<ConditionKey> keys, CancellationToken ct = default)
        {
            return CallEachAsync(keys, (alarms, condition, token) => alarms.EnableAsync(condition.NodeId, token), ct);
        }

        /// <summary>
        /// Disables the conditions.
        /// </summary>
        public Task<IReadOnlyList<ConditionCallResult>> DisableAsync(IEnumerable<ConditionKey> keys, CancellationToken ct = default)
        {
            return CallEachAsync(keys, (alarms, condition, token) => alarms.DisableAsync(condition.NodeId, token), ct);
        }

        /// <summary>
        /// Attaches a comment to the last event of each condition.
        /// </summary>
        public Task<IReadOnlyList<ConditionCallResult>> AddCommentAsync(IEnumerable<ConditionKey> keys, LocalizedText comment, CancellationToken ct = default)
        {
            return CallEachAsync(
                keys,
                (alarms, condition, token) => alarms.AddCommentAsync(condition.NodeId, EventIdOf(condition), comment, token),
                ct);
        }

        /// <summary>
        /// Acknowledges the last event of each condition.
        /// </summary>
        public Task<IReadOnlyList<ConditionCallResult>> AcknowledgeAsync(IEnumerable<ConditionKey> keys, LocalizedText comment, CancellationToken ct = default)
        {
            return CallEachAsync(
                keys,
                (alarms, condition, token) => alarms.AcknowledgeAsync(condition.NodeId, EventIdOf(condition), comment, token),
                ct);
        }

        /// <summary>
        /// Confirms the last event of each condition.
        /// </summary>
        public Task<IReadOnlyList<ConditionCallResult>> ConfirmAsync(IEnumerable<ConditionKey> keys, LocalizedText comment, CancellationToken ct = default)
        {
            return CallEachAsync(
                keys,
                (alarms, condition, token) => alarms.ConfirmAsync(condition.NodeId, EventIdOf(condition), comment, token),
                ct);
        }

        /// <summary>
        /// Shelves or unshelves the alarms.
        /// </summary>
        /// <remarks>
        /// The shelving Methods live on the ShelvingState object of an alarm, but Part 9
        /// 5.8.10.4 lets a client call them with the ConditionId instead, which is what the
        /// facade does. Nothing has to be browsed to find the state machine.
        /// </remarks>
        public Task<IReadOnlyList<ConditionCallResult>> ShelveAsync(IEnumerable<ConditionKey> keys, ShelveRequest request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            return CallEachAsync(
                keys,
                (alarms, condition, token) => request.Action switch {
                    ShelveAction.Unshelve => alarms.UnshelveAsync(condition.NodeId, token),
                    ShelveAction.OneShot => alarms.OneShotShelveAsync(condition.NodeId, token),
                    ShelveAction.Timed => alarms.TimedShelveAsync(condition.NodeId, request.ShelvingTime, token),
                    _ => alarms.TimedShelveAsync(condition.NodeId, 0, token),
                },
                ct);
        }

        /// <summary>
        /// Silences the audible annunciation of the alarms.
        /// </summary>
        public Task<IReadOnlyList<ConditionCallResult>> SilenceAsync(IEnumerable<ConditionKey> keys, CancellationToken ct = default)
        {
            return CallEachAsync(keys, (alarms, condition, token) => alarms.SilenceAsync(condition.NodeId, token), ct);
        }

        /// <summary>
        /// Suppresses or unsuppresses the alarms.
        /// </summary>
        /// <remarks>
        /// A suppressed alarm keeps following its process condition but stops asking for
        /// attention. The comment picks the Suppress2 / Unsuppress2 variant of the Method
        /// by itself when the operator supplied one.
        /// </remarks>
        public Task<IReadOnlyList<ConditionCallResult>> SuppressAsync(IEnumerable<ConditionKey> keys, bool suppress, LocalizedText comment, CancellationToken ct = default)
        {
            return CallEachAsync(
                keys,
                (alarms, condition, token) => suppress
                    ? alarms.SuppressAsync(condition.NodeId, comment, token)
                    : alarms.UnsuppressAsync(condition.NodeId, comment, token),
                ct);
        }

        /// <summary>
        /// Takes the alarms out of service or places them back in service.
        /// </summary>
        public Task<IReadOnlyList<ConditionCallResult>> SetOutOfServiceAsync(IEnumerable<ConditionKey> keys, bool outOfService, LocalizedText comment, CancellationToken ct = default)
        {
            return CallEachAsync(
                keys,
                (alarms, condition, token) => outOfService
                    ? alarms.RemoveFromServiceAsync(condition.NodeId, comment, token)
                    : alarms.PlaceInServiceAsync(condition.NodeId, comment, token),
                ct);
        }

        /// <summary>
        /// Clears the latch of the alarms.
        /// </summary>
        /// <remarks>
        /// A latching alarm keeps asking for attention after the process condition which
        /// raised it is gone. The server refuses the reset until the alarm is inactive,
        /// acknowledged and confirmed, and the refusal is the status of that key.
        /// </remarks>
        public Task<IReadOnlyList<ConditionCallResult>> ResetAsync(IEnumerable<ConditionKey> keys, LocalizedText comment, CancellationToken ct = default)
        {
            return CallEachAsync(keys, (alarms, condition, token) => alarms.ResetAsync(condition.NodeId, comment, token), ct);
        }

        /// <summary>
        /// Answers a dialog condition.
        /// </summary>
        /// <param name="key">The dialog.</param>
        /// <param name="selectedResponse">The index of the response in <see cref="ConditionSnapshot.DialogResponses"/>.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<ConditionCallResult> RespondAsync(ConditionKey key, int selectedResponse, CancellationToken ct = default)
        {
            IReadOnlyList<ConditionCallResult> results = await CallEachAsync(
                new[] { key },
                (alarms, condition, token) => condition is DialogConditionState dialog
                    ? alarms.RespondAsync(dialog.NodeId, selectedResponse, token)
                    : throw new ServiceResultException(StatusCodes.BadNotSupported, "The condition is not a dialog."),
                ct).ConfigureAwait(false);

            return results[0];
        }

        /// <summary>
        /// The names of the alarm groups a condition belongs to.
        /// </summary>
        public async Task<IReadOnlyList<string>> GetGroupMembershipNamesAsync(ConditionKey key, CancellationToken ct = default)
        {
            ISession session = RequireSession();
            ConditionState condition = RequireCondition(key);

            ArrayOf<NodeId> groups = await m_alarms.GetGroupMembershipsAsync(condition.NodeId, ct).ConfigureAwait(false);

            var names = new List<string>();

            // the array is a span based collection, which cannot be enumerated across an
            // await, so the node ids are taken out of it before anything is looked up.
            foreach (NodeId group in groups.ToArray())
            {
                INode node = await session.NodeCache.FindAsync(group, ct).ConfigureAwait(false);
                names.Add(node != null ? Utils.Format("{0}", node) : Utils.Format("{0}", group));
            }

            return names;
        }

        /// <summary>
        /// Turns the maintenance flag of the source which reported a condition on or off.
        /// </summary>
        /// <remarks>
        /// The flag is an ordinary variable next to the alarms of the source, and the
        /// server watches it with an alarm suppression group: while it is set, every alarm
        /// of the source reports itself as suppressed. The default filter of this client
        /// leaves suppressed conditions out, so the alarms of the source disappear from the
        /// list until the flag is cleared again - which is the whole point of the pattern.
        /// </remarks>
        public async Task<MaintenanceModeResult> ToggleMaintenanceModeAsync(ConditionKey key, CancellationToken ct = default)
        {
            ISession session = RequireSession();
            ConditionState condition = RequireCondition(key);

            NodeId sourceId = condition.SourceNode?.Value ?? NodeId.Null;

            if (sourceId.IsNull)
            {
                return new MaintenanceModeResult(MaintenanceModeOutcome.NoSource, false, StatusCodes.Good);
            }

            // the flag sits in the same namespace as the source which owns it, so the
            // relative path is built against a table which has that namespace at index one.
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(session.NamespaceUris.GetString(sourceId.NamespaceIndex));

            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                sourceId,
                namespaceUris,
                ct,
                Utils.Format("1:{0}", kMaintenanceMode)).ConfigureAwait(false);

            if (nodes.Count == 0 || nodes[0].IsNull)
            {
                return new MaintenanceModeResult(MaintenanceModeOutcome.NoFlag, false, StatusCodes.Good);
            }

            DataValue current = await session.ReadValueAsync(nodes[0], ct).ConfigureAwait(false);

            bool maintenance = current.WrappedValue.TryGetValue(out bool flag) && flag;

            var valuesToWrite = new List<WriteValue> {
                new WriteValue {
                    NodeId = nodes[0],
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(!maintenance)),
                },
            };

            WriteResponse response = await session.WriteAsync(null, valuesToWrite, ct).ConfigureAwait(false);

            StatusCode result = response.Results.ToArray()[0];

            return StatusCode.IsBad(result)
                ? new MaintenanceModeResult(MaintenanceModeOutcome.Failed, maintenance, result)
                : new MaintenanceModeResult(MaintenanceModeOutcome.Written, !maintenance, result);
        }
        #endregion

        #region Lookups
        /// <summary>
        /// The areas and sources below a node of the area tree, for the area filter dialog.
        /// </summary>
        /// <remarks>
        /// HasEventSource is browsed rather than HasNotifier so that the sources show up
        /// below their areas too; only a target of a HasNotifier reference can be picked
        /// as the area to subscribe to.
        /// </remarks>
        /// <param name="parent">The node to browse, or a null node id for the Server object.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<IReadOnlyList<ReferenceDescription>> BrowseAreasAsync(NodeId parent, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var nodeToBrowse = new BrowseDescription {
                NodeId = parent.IsNull ? ObjectIds.Server : parent,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasEventSource,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All,
            };

            List<ReferenceDescription> references = await SampleSession.BrowseAsync(session, nodeToBrowse, false, ct).ConfigureAwait(false);

            if (references == null)
            {
                return Array.Empty<ReferenceDescription>();
            }

            // out of server references cannot be subscribed to
            return references.Where(reference => !reference.NodeId.IsAbsolute).ToArray();
        }

        /// <summary>
        /// The raw fields of the last event of a condition, for the details dialog.
        /// </summary>
        /// <returns>The details, or null when the model does not know the condition.</returns>
        public ConditionDetails GetDetails(ConditionKey key)
        {
            ConditionEntry entry = Find(key);

            return entry == null ? null : new ConditionDetails(entry.Filter, entry.Fields);
        }

        /// <summary>
        /// Creates the model of an audit trail window on the attached session. The caller
        /// starts and disposes it.
        /// </summary>
        public AuditTrailModel CreateAuditTrail()
        {
            return new AuditTrailModel(RequireSession(), Telemetry);
        }

        /// <summary>
        /// Renders the Part 9 states of a condition as a short list of flags.
        /// </summary>
        /// <remarks>
        /// The state variables are optional, so a condition which does not carry one
        /// simply has nothing to say about it. Only the states which are set are listed,
        /// which keeps the column short enough to read at a glance.
        /// </remarks>
        public static string FormatConditionFlags(ConditionState condition)
        {
            ArgumentNullException.ThrowIfNull(condition);

            var flags = new List<string>();

            void Add(TwoStateVariableState state, string name)
            {
                if (state != null && state.Id != null && state.Id.Value)
                {
                    flags.Add(name);
                }
            }

            if (condition is AlarmConditionState alarm)
            {
                Add(alarm.ActiveState, "Active");
                Add(alarm.LatchedState, "Latched");
                Add(alarm.SilenceState, "Silenced");
                Add(alarm.SuppressedState, "Suppressed");
                Add(alarm.OutOfServiceState, "OutOfService");

                if (alarm.ShelvingState?.CurrentState?.Id != null &&
                    alarm.ShelvingState.CurrentState.Id.Value != ObjectIds.ShelvedStateMachineType_Unshelved)
                {
                    flags.Add(Utils.Format("{0}", alarm.ShelvingState.CurrentState.Value));
                }
            }

            if (condition is AcknowledgeableConditionState acknowledgeable)
            {
                if (acknowledgeable.AckedState?.Id?.Value == false)
                {
                    flags.Add("Unacked");
                }

                if (acknowledgeable.ConfirmedState?.Id?.Value == false)
                {
                    flags.Add("Unconfirmed");
                }
            }

            return String.Join(", ", flags);
        }
        #endregion

        #region Lifecycle
        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            try
            {
                m_alarms = session.GetAlarmClient(Telemetry);

                // the pump runs before the subscription exists, so that nothing the server
                // sends during the refresh below can arrive with nobody to take it
                m_pump = new SerialNotificationPump<EventNotification>(
                    ProcessNotificationAsync,
                    (_, exception) => ReportError("Processing a condition event", exception));
                m_pump.Start();

                // the V2 engine takes the settings through an options monitor and creates
                // the subscription on the server on its own worker.
                var options = new OptionsMonitor<SubscriptionOptions>(SampleSession.DefaultSubscriptionOptions);

                m_subscription = SampleSession.AddSubscription(session, m_callbacks, options);

                // must specify the fields that the client is interested in.
                m_filter.SelectClauses = await m_filter.ConstructSelectClausesAsync(
                    session,
                    ct,
                    NodeId.Parse("ns=2;s=4:2"),
                    NodeId.Parse("ns=2;s=4:1"),
                    ObjectTypeIds.DialogConditionType,
                    ObjectTypeIds.ExclusiveLimitAlarmType,
                    ObjectTypeIds.NonExclusiveLimitAlarmType).ConfigureAwait(false);

                // create a monitored item based on the current filter settings.
                m_monitoredItem = AddEventItem(session, m_subscription);

                await SampleSession.WaitForPendingChangesAsync(m_subscription, kApplyTimeout, ct).ConfigureAwait(false);

                // Send an initial refresh so the list starts out with everything the server
                // retains rather than with whatever happens to change next.
                await RefreshItemAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // the base class releases the session; what was created on it goes with it
                await ReleaseAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        protected override Task OnDetachingAsync()
        {
            // done before the session is closed: closing a session which still carries a
            // subscription waits for the publish pipeline to drain.
            return ReleaseAsync();
        }

        /// <inheritdoc/>
        protected override Task OnReconnectCompletedAsync(CancellationToken ct)
        {
            // a V2 subscription belongs to the subscription manager of the session and
            // survives the reconnect together with its monitored items, and so does the
            // alarm client of a managed session. What may have changed while the
            // connection was down is the state of the conditions, so they are replayed.
            return RefreshItemAsync(ct);
        }

        /// <summary>
        /// Asks the server to replay the conditions of the event item.
        /// </summary>
        /// <remarks>
        /// The refresh is addressed to the monitored item (ConditionRefresh2) rather than
        /// to the subscription: the refresh of a whole V2 subscription replays nothing on
        /// this stack, while the refresh of the item replays everything the server
        /// retains, bracketed by its RefreshStart and RefreshEnd events. Nothing to
        /// refresh - the model is detached, or the item is still being created - is not
        /// an error; the next event fills the list.
        /// </remarks>
        private async Task RefreshItemAsync(CancellationToken ct)
        {
            IMonitoredItem item = m_monitoredItem?.Item;

            if (item != null)
            {
                await item.ConditionRefreshAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Deletes the subscription on the server, stops the pump and forgets the conditions.
        /// </summary>
        private async Task ReleaseAsync()
        {
            ISubscription subscription = m_subscription;
            SerialNotificationPump<EventNotification> pump = m_pump;

            m_subscription = null;
            m_monitoredItem = null;
            m_pump = null;
            m_alarms = null;
            m_filtersByHandle.Clear();

            try
            {
                if (subscription != null)
                {
                    // disposing it deletes the subscription on the server and removes it
                    // from the manager
                    await subscription.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                if (pump != null)
                {
                    await pump.DisposeAsync().ConfigureAwait(false);
                }

                // the pump has stopped, so nobody else touches the tables any more; the
                // mappings belong to the server they were learned from
                m_eventTypeMappings.Clear();
                ClearConditions();
            }
        }
        #endregion

        #region Subscription
        /// <summary>
        /// Adds a monitored item for the current filter settings to the subscription.
        /// </summary>
        /// <remarks>
        /// The V2 engine identifies an item by a name which is unique within its
        /// subscription, and adding it to the collection is the create request.
        /// </remarks>
        private MonitoredItemEntry AddEventItem(ISession session, ISubscription subscription)
        {
            MonitoredItemOptions options = m_filter.CreateMonitoredItemOptions(session);

            var entry = new MonitoredItemEntry(Utils.Format("Events{0}", ++m_nextItemId), options) {
                NodeClass = NodeClass.Object,
            };

            subscription.MonitoredItems.TryAdd(entry.Name, entry.Options, out IMonitoredItem item);
            entry.Item = item;

            if (item != null)
            {
                m_filtersByHandle[item.ClientHandle] = (EventFilter)options.Filter;
            }

            return entry;
        }

        /// <summary>
        /// Replaces the monitored item after the filter changed.
        /// </summary>
        /// <remarks>
        /// Changing the filter changes the fields requested, which makes it impossible to
        /// process notifications sent before the change; and the event filter of an item
        /// cannot be modified after it was created anyway. So a new item is created and
        /// the old one removed. A detached model only remembers the setting: the item is
        /// created with it when a session is attached.
        /// </remarks>
        private async Task UpdateFilterAsync(CancellationToken ct)
        {
            ISession session = Session;
            ISubscription subscription = m_subscription;

            if (session == null || subscription == null)
            {
                return;
            }

            MonitoredItemEntry previous = m_monitoredItem;

            if (previous?.Item != null)
            {
                // the filter of the old item is forgotten before the item is: whatever the
                // server still delivers for it while it is being removed can no longer be
                // decoded with the right select clauses, and is dropped by the consumer
                m_filtersByHandle.TryRemove(previous.Item.ClientHandle, out _);
                subscription.MonitoredItems.TryRemove(previous.Item.ClientHandle);

                // The old item goes before the new one is created. Created first, the new
                // item would serve the refresh below and then never report a live event
                // again: on this stack, deleting an event item after another was created
                // on the same notifier leaves the new one without live events, and only
                // an item created after the deletion receives them.
                m_monitoredItem = null;
                await SampleSession.WaitForPendingChangesAsync(subscription, kApplyTimeout, ct).ConfigureAwait(false);
            }

            m_monitoredItem = AddEventItem(session, subscription);

            await SampleSession.WaitForPendingChangesAsync(subscription, kApplyTimeout, ct).ConfigureAwait(false);

            // The conditions which are listed belong to the filter which was just replaced.
            // The refresh below announces itself with a RefreshStart event, which clears
            // them too - the filter asks for that event explicitly - but the refresh only
            // arrives once the server has processed the call, and until then the old rows
            // would stay up and make the new filter look as if it did nothing.
            ClearConditions();

            // Send a refresh since previously filtered conditions may now be available.
            await RefreshItemAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Queues the events the server reported for the consumer of the pump.
        /// </summary>
        /// <remarks>
        /// The V2 engine calls this on a publish worker and reports the whole notification
        /// instead of one event at a time. Nothing is decoded here: the events of a batch
        /// are queued in order, and the one consumer of the pump takes them from there.
        /// </remarks>
        private void OnEvents(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            EventNotification[] notifications,
            PublishState publishState)
        {
            SerialNotificationPump<EventNotification> pump = m_pump;

            if (pump == null)
            {
                return;
            }

            foreach (EventNotification notification in notifications)
            {
                pump.Post(notification);
            }
        }

        /// <summary>
        /// Decodes one event and reports the condition it belongs to.
        /// </summary>
        /// <remarks>
        /// Runs on the consumer of the pump, one event at a time. Everything which awaits
        /// happens before the condition table is touched, so an entry is complete - with
        /// its snapshot and its state - before the next event is looked at.
        /// </remarks>
        private async Task ProcessNotificationAsync(EventNotification notification, CancellationToken ct)
        {
            ISession session = Session;

            if (session == null)
            {
                return;
            }

            // an item which a filter change removed has no filter any more, and its
            // events cannot be decoded with the select clauses of its successor
            uint clientHandle = notification.MonitoredItem?.ClientHandle ?? 0;

            if (!m_filtersByHandle.TryGetValue(clientHandle, out EventFilter filter))
            {
                return;
            }

            EventFieldList fields = ConditionEventTypes.ToFieldList(notification);

            // check the type of event.
            NodeId eventTypeId = SampleSession.FindEventType(filter, fields);

            // ignore unknown events.
            if (eventTypeId.IsNull)
            {
                return;
            }

            // a refresh starts the list over and ends without anything to show
            if (eventTypeId == ObjectTypeIds.RefreshStartEventType)
            {
                ClearConditions();
                return;
            }

            if (eventTypeId == ObjectTypeIds.RefreshEndEventType)
            {
                return;
            }

            // construct the condition object.
            ConditionState condition = await SampleSession.ConstructEventAsync(
                session,
                filter,
                fields,
                m_knownEventTypes,
                m_eventTypeMappings,
                ct).ConfigureAwait(false) as ConditionState;

            if (condition == null)
            {
                return;
            }

            // look up the condition type metadata in the local cache.
            INode type = await session.NodeCache.FindAsync(condition.TypeDefinitionId, ct).ConfigureAwait(false);

            // the combination of a condition and branch id uniquely identify a row.
            var key = new ConditionKey(condition.NodeId, condition.BranchId?.Value ?? NodeId.Null);

            ConditionChange change;
            ConditionSnapshot snapshot;

            lock (m_lock)
            {
                // the filter may have changed while this event was being decoded; then
                // the event belongs to a list which was cleared in the meantime
                if (!m_filtersByHandle.ContainsKey(clientHandle))
                {
                    return;
                }

                if (m_conditions.TryGetValue(key, out ConditionEntry existing))
                {
                    // an older event of a condition which is already listed has nothing
                    // to add. The server does not reorder events, but the guard costs
                    // nothing and the rule is easy to state: a row only moves forward.
                    if (existing.State.Time != null && condition.Time != null &&
                        existing.State.Time.Value > condition.Time.Value)
                    {
                        return;
                    }

                    change = ConditionChange.Updated;
                }
                else
                {
                    change = ConditionChange.Added;
                }

                snapshot = CreateSnapshot(key, condition, type);
                m_conditions[key] = new ConditionEntry(condition, snapshot, filter, fields);
            }

            Raise(ConditionChanged, new ConditionChangedEventArgs(change, snapshot));
        }

        /// <summary>
        /// Forgets every condition and says so.
        /// </summary>
        private void ClearConditions()
        {
            lock (m_lock)
            {
                m_conditions.Clear();
            }

            Raise(ConditionsCleared, EventArgs.Empty);
        }

        /// <summary>
        /// One field per column of the list, from the state of the condition.
        /// </summary>
        private static ConditionSnapshot CreateSnapshot(ConditionKey key, ConditionState condition, INode type)
        {
            var dialog = condition as DialogConditionState;
            IReadOnlyList<string> responses = Array.Empty<string>();

            if (dialog?.ResponseOptionSet != null)
            {
                responses = dialog.ResponseOptionSet.Value
                    .ToArray()
                    .Select(option => Utils.Format("{0}", option))
                    .ToArray();
            }

            return new ConditionSnapshot(
                key,
                EventIdOf(condition),
                Text(condition.SourceName?.Value),
                Text(condition.ConditionName?.Value),
                key.IsBranch ? Utils.Format("{0}", key.BranchId) : null,
                type != null ? Utils.Format("{0}", type) : null,
                condition.Severity?.Value ?? 0,
                condition.Time?.Value,
                Text(condition.EnabledState?.EffectiveDisplayName?.Value),
                FormatConditionFlags(condition),
                Text(condition.Message?.Value),
                Text(condition.Comment?.Value),
                condition.Retain?.Value ?? false,
                dialog != null,
                Text(dialog?.Prompt?.Value),
                responses,
                condition is AlarmConditionState alarm && alarm.SilenceState?.Id?.Value == false);
        }

        /// <summary>
        /// A value the way the list shows it, or null when the event did not carry it.
        /// </summary>
        private static string Text(object value)
        {
            return value == null ? null : Utils.Format("{0}", value);
        }

        /// <summary>
        /// The id of the last event of a condition, which the Methods taking a comment need.
        /// </summary>
        private static ByteString EventIdOf(ConditionState condition)
        {
            return condition.EventId?.Value ?? default;
        }
        #endregion

        #region Condition table
        /// <summary>
        /// Everything the model keeps about one row: the decoded state, what the window
        /// shows for it, and the raw event with the filter it was decoded with.
        /// </summary>
        private sealed record ConditionEntry(
            ConditionState State,
            ConditionSnapshot Snapshot,
            EventFilter Filter,
            EventFieldList Fields);

        /// <summary>
        /// The entry of a key, or null.
        /// </summary>
        private ConditionEntry Find(ConditionKey key)
        {
            ArgumentNullException.ThrowIfNull(key);

            lock (m_lock)
            {
                return m_conditions.TryGetValue(key, out ConditionEntry entry) ? entry : null;
            }
        }

        /// <summary>
        /// The state of a condition the model knows, or an exception for one it does not.
        /// </summary>
        private ConditionState RequireCondition(ConditionKey key)
        {
            return Find(key)?.State
                ?? throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, Utils.Format("The condition {0} is not listed.", key));
        }

        /// <summary>
        /// Calls a Part 9 Method for every condition and answers per condition.
        /// </summary>
        /// <remarks>
        /// A refusal of the server arrives as a <see cref="ServiceResultException"/> from
        /// the generated proxy and becomes the status of that key; the calls for the other
        /// keys go ahead. Anything else - a session which is gone, a cancellation - is not
        /// a per condition answer and reaches the caller.
        /// </remarks>
        private async Task<IReadOnlyList<ConditionCallResult>> CallEachAsync(
            IEnumerable<ConditionKey> keys,
            Func<AlarmClient, ConditionState, CancellationToken, ValueTask> callAsync,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(keys);

            RequireSession();

            AlarmClient alarms = m_alarms;
            var results = new List<ConditionCallResult>();

            foreach (ConditionKey key in keys)
            {
                ConditionState condition = Find(key)?.State;

                if (condition == null)
                {
                    results.Add(new ConditionCallResult(key, StatusCodes.BadNodeIdUnknown));
                    continue;
                }

                try
                {
                    await callAsync(alarms, condition, ct).ConfigureAwait(false);
                    results.Add(new ConditionCallResult(key, StatusCodes.Good));
                }
                catch (ServiceResultException exception)
                {
                    results.Add(new ConditionCallResult(key, exception.StatusCode));
                }
            }

            return results;
        }
        #endregion
    }
}
