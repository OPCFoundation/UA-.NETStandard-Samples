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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server.Alarms;

namespace Quickstarts.AlarmConditionServer
{
    /// <summary>
    /// Maps an alarm source to a UA object node.
    /// </summary>
    public partial class SourceState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the area.
        /// </summary>
        public SourceState(
            AlarmConditionServerNodeManager nodeManager,
            NodeId nodeId,
            string sourcePath)
        :
            base(null)
        {
            Initialize(nodeManager.SystemContext);

            // save the node manager that owns the source.
            m_nodeManager = nodeManager;
            m_logger = nodeManager.Server.Telemetry.CreateLogger<SourceState>();

            // create the source with the underlying system.
            m_source = ((UnderlyingSystem)nodeManager.SystemContext.SystemHandle).CreateSource(sourcePath, OnAlarmChanged);

            // initialize the area with the fixed metadata.
            this.SymbolicName = m_source.Name;
            this.NodeId = nodeId;
            this.BrowseName = new QualifiedName(Utils.Format("{0}", m_source.Name), nodeId.NamespaceIndex);
            this.DisplayName = new LocalizedText(BrowseName.Name);
            this.Description = LocalizedText.Null;
            this.ReferenceTypeId = NodeId.Null;
            this.TypeDefinitionId = ObjectTypeIds.BaseObjectType;
            this.EventNotifier = EventNotifiers.None;

            // create a dialog.
            m_dialog = CreateDialog("OnlineState");

            // create the table of conditions.
            m_alarms = new Dictionary<string, AlarmConditionState>();
            m_events = new Dictionary<string, AlarmConditionState>();
            m_branches = new Dictionary<NodeId, AlarmConditionState>();

            // the Part 9 group model. The group has to exist before the alarms are created
            // because every alarm adds itself to it, and the alarms are created by the
            // refresh below.
            m_group = CreateAlarmGroup(kAlarmGroupName);
            m_metrics = CreateAlarmMetrics(kAlarmMetricsName);
            m_maintenanceMode = CreateMaintenanceMode(kMaintenanceModeName);
            m_rateTracker = new AlarmRateTracker(kAlarmRateWindow);

            // request an updated for all alarms.
            m_source.Refresh();

            // the suppression engine belongs to the node manager: it is the piece which
            // decides, and it decides for every source of the server.
            RegisterWithSuppressionEngine(nodeManager.SuppressionEngine);
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Drives everything the source owes the simulation cycle: the re-alarm reminders
        /// and the alarm metrics.
        /// </summary>
        /// <remarks>
        /// Neither is driven by the state type itself. Part 9 leaves the re-alarm schedule
        /// to the application - the stack only offers <c>ProcessReAlarm</c> - and the alarm
        /// metrics are ordinary variables which somebody has to fill in.
        /// </remarks>
        public async Task OnSimulationTickAsync(CancellationToken cancellationToken = default)
        {
            List<AlarmConditionState> reAlarmed = CollectReAlarms();

            foreach (AlarmConditionState alarm in reAlarmed)
            {
                // the stack withdraws the acknowledgement, clears the silence and counts
                // the repetition; the underlying system is told so that the two agree and
                // so that the event which announces the repetition carries a reason.
                alarm.ProcessReAlarm(m_nodeManager.SystemContext);
                m_source.ReAlarm(alarm.SymbolicName);
            }

            UpdateMetrics();

            await m_metrics
                .ClearChangeMasksAsync(m_nodeManager.SystemContext, true, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the last event produced for any conditions belonging to the node or its chilren.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="events">The list of condition events to return.</param>
        /// <param name="includeChildren">Whether to recursively report events for the children.</param>
        public override void ConditionRefresh(ISystemContext context, List<IFilterTarget> events, bool includeChildren)
        {
            // need to check if this source has already been processed during this refresh operation.
            for (int ii = 0; ii < events.Count; ii++)
            {
                InstanceStateSnapshot e = events[ii] as InstanceStateSnapshot;

                if (e != null && Object.ReferenceEquals(e.Handle, this))
                {
                    return;
                }
            }

            // the refresh can run concurrently with alarm changes reported by the underlying system.
            lock (m_lock)
            {
                // report the dialog.
                if (m_dialog != null)
                {
                    // do not refresh dialogs that are not active.
                    if (m_dialog.Retain.Value)
                    {
                        // create a snapshot.
                        InstanceStateSnapshot e = new InstanceStateSnapshot();
                        e.Initialize(context, m_dialog);

                        // set the handle of the snapshot to check for duplicates.
                        e.Handle = this;

                        events.Add(e);
                    }
                }

                // the alarm objects act as a cache for the last known state and are used to generate refresh events.
                foreach (AlarmConditionState alarm in m_alarms.Values)
                {
                    // do not refresh alarms that are not in an interesting state.
                    if (!alarm.Retain.Value)
                    {
                        continue;
                    }

                    // create a snapshot.
                    InstanceStateSnapshot e = new InstanceStateSnapshot();
                    e.Initialize(context, alarm);

                    // set the handle of the snapshot to check for duplicates.
                    e.Handle = this;

                    events.Add(e);
                }

                // report any active branches.
                foreach (AlarmConditionState alarm in m_branches.Values)
                {
                    // create a snapshot.
                    InstanceStateSnapshot e = new InstanceStateSnapshot();
                    e.Initialize(context, alarm);

                    // set the handle of the snapshot to check for duplicates.
                    e.Handle = this;

                    events.Add(e);
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Called when the state of an alarm for the source has changed.
        /// </summary>
        /// <remarks>
        /// The underlying system reports the change on its own thread. The alarm state is
        /// updated under the source lock and the resulting event is reported through the
        /// asynchronous event path of the node state.
        /// </remarks>
        private async void OnAlarmChanged(UnderlyingSystemAlarm alarm)
        {
            try
            {
                AlarmConditionState node = null;
                var alsoChanged = new List<AlarmConditionState>();

                lock (m_lock)
                {
                    // ignore archived alarms for now.
                    if (alarm.RecordNumber != 0)
                    {
                        NodeId branchId = new NodeId(alarm.RecordNumber, this.NodeId.NamespaceIndex);

                        // find the alarm branch.
                        AlarmConditionState branch = null;

                        if (!m_branches.TryGetValue(branchId, out branch))
                        {
                            m_branches[branchId] = branch = CreateAlarm(alarm, branchId);
                        }

                        // map the system information to the UA defined alarm.
                        UpdateAlarm(branch, alarm, alsoChanged);

                        // delete the branch.
                        if ((alarm.State & UnderlyingSystemAlarmStates.Deleted) != 0)
                        {
                            m_branches.Remove(branchId);
                        }

                        node = branch;
                    }
                    else
                    {
                        // find the alarm node.
                        if (!m_alarms.TryGetValue(alarm.Name, out node))
                        {
                            m_alarms[alarm.Name] = node = CreateAlarm(alarm, NodeId.Null);
                        }

                        // map the system information to the UA defined alarm.
                        UpdateAlarm(node, alarm, alsoChanged);
                    }
                }

                await ReportChangesAsync(node).ConfigureAwait(false);

                // a trip alarm which changed its active state also changed the suppression
                // of the alarms which follow it, and each of those owes its own event.
                foreach (AlarmConditionState follower in alsoChanged)
                {
                    await ReportChangesAsync(follower).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error reporting a change to an alarm for source {SourceName}.", SymbolicName);
            }
        }

        /// <summary>
        /// Creates a new dialog condition
        /// </summary>
        private DialogConditionState CreateDialog(string dialogName)
        {
            ISystemContext context = m_nodeManager.SystemContext;

            DialogConditionState node = new DialogConditionState(this);

            node.SymbolicName = dialogName;

            // specify optional fields.
            node.EnabledState = new TwoStateVariableState(node);
            node.EnabledState.TransitionTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(node.EnabledState);
            node.EnabledState.EffectiveDisplayName = PropertyState<LocalizedText>.With<VariantBuilder>(node.EnabledState);
            node.EnabledState.Create(context, NodeId.Null, new QualifiedName(BrowseNames.EnabledState), LocalizedText.Null, false);

            // This call initializes the condition from the type model (i.e. creates all of the objects
            // and variables requried to store its state). The information about the type model was 
            // incorporated into the class when the class was created.
            node.Create(
                context,
                NodeId.Null,
                new QualifiedName(dialogName, this.BrowseName.NamespaceIndex),
                LocalizedText.Null,
                true);

            AttachToSource(node);

            // initialize event information.
            node.EventId.Value = Guid.NewGuid().ToByteArray().ToByteString();
            node.EventType.Value = node.TypeDefinitionId;
            node.SourceNode.Value = this.NodeId;
            node.SourceName.Value = this.SymbolicName;
            node.ConditionName.Value = node.SymbolicName;
            node.Time.Value = DateTime.UtcNow;
            node.ReceiveTime.Value = node.Time.Value;
            node.Message.Value = new LocalizedText("The dialog was activated");

            node.SetEnableState(context, true);
            node.SetSeverity(context, EventSeverity.Low);

            // enabling re-evaluates the retain state, and the dialog condition of the SDK
            // does not consider an active dialog interesting on its own. the dialog has to
            // stay retained until it is answered, so that a condition refresh replays it,
            // which is how a client which connects later learns that a response is wanted.
            node.Retain.Value = true;

            // initialize the dialog information.
            node.Prompt.Value = new LocalizedText("Please specify a new state for the source.");
            node.ResponseOptionSet.Value = s_ResponseOptions;
            node.DefaultResponse.Value = 2;
            node.CancelResponse.Value = 2;
            node.OkResponse.Value = 0;

            // set up method handlers.
            node.OnRespond = OnRespond;

            // this flag needs to be set because the underlying system does not produce these events.
            node.AutoReportStateChanges = true;

            // activate the dialog.
            node.Activate(context);

            // return the new node.
            return node;
        }

        /// <summary>
        /// The responses used with the dialog condition.
        /// </summary>
        private LocalizedText[] s_ResponseOptions = new LocalizedText[]
        {
            new LocalizedText("Online"),
            new LocalizedText("Offline"),
            new LocalizedText("No Change")
        };

        /// <summary>
        /// Adds a node which was created from the type model to the source, so that a
        /// client can browse to it.
        /// </summary>
        /// <remarks>
        /// The reference type has to be set after <c>Create</c> rather than before it:
        /// creating a node initializes it from its type model, and that copies the
        /// reference type of the template over whatever the caller had set. A child whose
        /// reference type ends up null is not a hierarchical child of anything, and a
        /// browse of the source would not return it.
        /// </remarks>
        private void AttachToSource(BaseInstanceState child)
        {
            child.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            this.AddChild(child);
        }

        /// <summary>
        /// Creates the <see cref="AlarmGroupState"/> which collects the alarms of the source.
        /// </summary>
        /// <remarks>
        /// The group is what makes the alarms of a source addressable as a set: it carries
        /// an AlarmGroupMember reference to each of them, which is what
        /// <c>GetGroupMemberships</c> reads back and what the suppression engine works on.
        /// </remarks>
        private AlarmGroup CreateAlarmGroup(string groupName)
        {
            ISystemContext context = m_nodeManager.SystemContext;

            var group = new AlarmGroupState(this) {
                SymbolicName = groupName,
                TypeDefinitionId = ObjectTypeIds.AlarmGroupType
            };

            group.Create(
                context,
                NodeId.Null,
                new QualifiedName(groupName, this.BrowseName.NamespaceIndex),
                LocalizedText.Null,
                true);

            AttachToSource(group);

            return new AlarmGroup(group);
        }

        /// <summary>
        /// Creates the <see cref="AlarmMetricsState"/> which reports the alarm rate of the source.
        /// </summary>
        private AlarmMetricsState CreateAlarmMetrics(string metricsName)
        {
            ISystemContext context = m_nodeManager.SystemContext;

            var metrics = new AlarmMetricsState(this) {
                SymbolicName = metricsName,
                TypeDefinitionId = ObjectTypeIds.AlarmMetricsType
            };

            metrics.Create(
                context,
                NodeId.Null,
                new QualifiedName(metricsName, this.BrowseName.NamespaceIndex),
                LocalizedText.Null,
                true);

            AttachToSource(metrics);

            metrics.StartTime.Value = DateTime.UtcNow;

            // the Rate property states the period the rate is expressed in, so a client
            // knows that a value of 3 means three activations per minute.
            metrics.CurrentAlarmRate.Rate.Value = (ushort)kAlarmRateWindow.TotalSeconds;
            metrics.MaximumAlarmRate.Rate.Value = (ushort)kAlarmRateWindow.TotalSeconds;

            return metrics;
        }

        /// <summary>
        /// Creates the writable flag which suppresses every alarm of the source.
        /// </summary>
        /// <remarks>
        /// This is the process value the suppression engine watches. Writing it is how a
        /// client tells the server that the source is being serviced and that its alarms
        /// are not worth annunciating for the time being (OPC UA Part 9 5.8.4).
        /// </remarks>
        private BaseDataVariableState CreateMaintenanceMode(string variableName)
        {
            ISystemContext context = m_nodeManager.SystemContext;

            var variable = new BaseDataVariableState(this) {
                SymbolicName = variableName,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = DataTypeIds.Boolean,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Value = false
            };

            variable.Create(
                context,
                NodeId.Null,
                new QualifiedName(variableName, this.BrowseName.NamespaceIndex),
                LocalizedText.Null,
                true);

            variable.OnWriteValue = OnMaintenanceModeWritten;

            AttachToSource(variable);

            return variable;
        }

        /// <summary>
        /// Registers the two suppression patterns of Part 9 with the engine of the server.
        /// </summary>
        /// <remarks>
        /// The engine owns the decision and the edge detection for both patterns, but a
        /// single <c>SuppressedState</c> carries all of the causes an alarm can have. The
        /// sample therefore records which cause the engine just decided on and re-applies
        /// the union of the causes to the members, so that clearing one cause does not
        /// clear a suppression another cause still asks for.
        /// </remarks>
        private void RegisterWithSuppressionEngine(AlarmSuppressionEngine engine)
        {
            m_engine = engine;

            List<AlarmConditionState> members = new List<AlarmConditionState>(m_alarms.Values);

            // AlarmSuppressionGroup: MaintenanceMode suppresses every alarm of the source.
            engine.RegisterSuppressionGroup(
                m_group.State,
                () => IsMaintenanceMode,
                members);

            // FirstInGroup: while the trip alarm of the source is active the level alarms
            // it drags along are not worth reporting on their own.
            //
            // Only the metal sources run this pattern. The trip alarm of the simulation is
            // active most of the time, so a server which ran it everywhere would leave a
            // client with the default "hide suppressed conditions" filter looking at an
            // almost empty list. Half of the sources showing every alarm and half of them
            // showing only the leader is what makes the difference visible.
            m_firstInGroupSuppression = m_tripAlarm != null
                && m_source.SourceType == kFirstInGroupSourceType;

            if (m_firstInGroupSuppression)
            {
                engine.RegisterFirstInGroupAlarm(
                    m_tripAlarm,
                    m_group.State,
                    members.Where(member => !Object.ReferenceEquals(member, m_tripAlarm)).ToList());
            }
        }

        /// <summary>
        /// Whether the maintenance flag of the source is set.
        /// </summary>
        private bool IsMaintenanceMode
            => m_maintenanceMode != null &&
               m_maintenanceMode.Value.TryGetValue(out bool enabled) &&
               enabled;

        /// <summary>
        /// Called when a client writes the MaintenanceMode flag of the source.
        /// </summary>
        private ServiceResult OnMaintenanceModeWritten(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            if (!value.TryGetValue(out bool maintenance))
            {
                return StatusCodes.BadTypeMismatch;
            }

            var changed = new List<AlarmConditionState>();

            lock (m_lock)
            {
                ((BaseDataVariableState)node).Value = value;

                // the engine reads the flag back through the delegate it was registered
                // with, decides whether this is an edge, and suppresses the members.
                m_engine.Evaluate(context);
                m_maintenanceSuppressed = maintenance;

                foreach (AlarmConditionState alarm in m_alarms.Values)
                {
                    ApplySuppression(alarm);
                    UpdateAlarm(alarm, null);
                    changed.Add(alarm);
                }
            }

            foreach (AlarmConditionState alarm in changed)
            {
                ReportChanges(alarm);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Applies the union of the suppression causes to an alarm. The caller must hold
        /// the source lock.
        /// </summary>
        private void ApplySuppression(AlarmConditionState node)
        {
            string alarmName = node.SymbolicName;

            node.SetSuppressedState(
                m_nodeManager.SystemContext,
                m_systemSuppressed.Contains(alarmName)
                    || m_maintenanceSuppressed
                    || m_firstInGroupSuppressed.Contains(alarmName));
        }

        /// <summary>
        /// Tells the engine that the trip alarm of the source changed its active state and
        /// re-applies the suppression of the alarms which follow it. The caller must hold
        /// the source lock.
        /// </summary>
        private void ApplyFirstInGroup(bool tripActive, List<AlarmConditionState> alsoChanged)
        {
            // the flag is the Part 9 information that this alarm leads its group; it says
            // nothing about suppression and is therefore set on every source.
            m_tripAlarm.FirstInGroupFlag.Value = tripActive;

            if (!m_firstInGroupSuppression)
            {
                return;
            }

            m_engine.OnFirstInGroupActiveChanged(
                m_nodeManager.SystemContext,
                m_tripAlarm,
                m_group.State,
                tripActive);

            foreach (AlarmConditionState member in m_alarms.Values)
            {
                if (Object.ReferenceEquals(member, m_tripAlarm))
                {
                    continue;
                }

                if (tripActive)
                {
                    m_firstInGroupSuppressed.Add(member.SymbolicName);
                }
                else
                {
                    m_firstInGroupSuppressed.Remove(member.SymbolicName);
                }

                ApplySuppression(member);
                UpdateAlarm(member, null);
                alsoChanged?.Add(member);
            }
        }

        /// <summary>
        /// Returns the alarms which have been active and unacknowledged long enough to
        /// deserve a reminder.
        /// </summary>
        private List<AlarmConditionState> CollectReAlarms()
        {
            var due = new List<AlarmConditionState>();
            DateTime now = DateTime.UtcNow;

            lock (m_lock)
            {
                foreach (AlarmConditionState alarm in m_alarms.Values)
                {
                    if (!m_reAlarmDue.TryGetValue(alarm.SymbolicName, out DateTime deadline) || now < deadline)
                    {
                        continue;
                    }

                    // the next reminder is one period away; the schedule is kept here
                    // rather than being derived from ReAlarmRepeatCount, because an
                    // acknowledgement resets the count and would move the deadline back.
                    m_reAlarmDue[alarm.SymbolicName] = now.AddMilliseconds(alarm.ReAlarmTime.Value);
                    due.Add(alarm);
                }
            }

            return due;
        }

        /// <summary>
        /// Copies the tracked alarm rate into the AlarmMetrics object of the source.
        /// </summary>
        private void UpdateMetrics()
        {
            lock (m_lock)
            {
                short maximumRepeats = 0;

                foreach (AlarmConditionState alarm in m_alarms.Values)
                {
                    if (alarm.ReAlarmRepeatCount != null && alarm.ReAlarmRepeatCount.Value > maximumRepeats)
                    {
                        maximumRepeats = alarm.ReAlarmRepeatCount.Value;
                    }
                }

                m_metrics.AlarmCount.Value = (uint)m_alarms.Count;
                m_metrics.CurrentAlarmRate.Value = m_rateTracker.CurrentAlarmRate;
                m_metrics.MaximumAlarmRate.Value = m_rateTracker.MaximumAlarmRate;

                // MaximumReAlarmCount is optional in AlarmMetricsType, so the type model
                // only materializes it on a server which asks for it.
                if (m_metrics.MaximumReAlarmCount != null)
                {
                    m_metrics.MaximumReAlarmCount.Value = (uint)maximumRepeats;
                }
            }
        }

        /// <summary>
        /// Creates a new alarm for the source.
        /// </summary>
        /// <param name="alarm">The alarm.</param>
        /// <param name="branchId">The branch id.</param>
        /// <returns>The new alarm.</returns>
        private AlarmConditionState CreateAlarm(UnderlyingSystemAlarm alarm, NodeId branchId)
        {
            ISystemContext context = m_nodeManager.SystemContext;

            AlarmConditionState node = null;

            // need to map the alarm type to a UA defined alarm type.
            switch (alarm.AlarmType)
            {
                case "HighAlarm":
                {
                    ExclusiveDeviationAlarmState node2 = new ExclusiveDeviationAlarmState(this);
                    node = node2;
                    node2.HighLimit = PropertyState<double>.With<VariantBuilder>(node2);
                    break;
                }

                case "HighLowAlarm":
                {
                    NonExclusiveLevelAlarmState node2 = new NonExclusiveLevelAlarmState(this);
                    node = node2;

                    node2.HighHighLimit = PropertyState<double>.With<VariantBuilder>(node2);
                    node2.HighLimit = PropertyState<double>.With<VariantBuilder>(node2);
                    node2.LowLimit = PropertyState<double>.With<VariantBuilder>(node2);
                    node2.LowLowLimit = PropertyState<double>.With<VariantBuilder>(node2);

                    node2.HighHighState = new TwoStateVariableState(node2);
                    node2.HighState = new TwoStateVariableState(node2);
                    node2.LowState = new TwoStateVariableState(node2);
                    node2.LowLowState = new TwoStateVariableState(node2);

                    break;
                }

                case "TripAlarm":
                {
                    node = new TripAlarmState(this);
                    break;
                }

                default:
                {
                    node = new AlarmConditionState(this);
                    break;
                }
            }

            node.SymbolicName = alarm.Name;

            // add optional components.
            node.Comment = ConditionVariableState<LocalizedText>.With<VariantBuilder>(node);
            node.ClientUserId = PropertyState<string>.With<VariantBuilder>(node);
            node.AddComment = new AddCommentMethodState(node);
            node.ConfirmedState = new TwoStateVariableState(node);
            node.Confirm = new AddCommentMethodState(node);

            if ((branchId).IsNull)
            {
                node.SuppressedState = new TwoStateVariableState(node);
                node.ShelvingState = new ShelvedStateMachineState(node);

                // The Part 9 states and Methods which the stack implements for an alarm.
                // Creating the optional nodes is all that is required: AlarmConditionState
                // wires a handler onto every one of these Methods in OnAfterCreate, and
                // each handler validates the transition, raises the audit event the
                // specification asks for and consults the veto delegate wired below.
                node.AddSilenceState(context)
                    .AddOutOfServiceState(context)
                    .AddSilence(context)
                    .AddSuppress(context)
                    .AddSuppress2(context)
                    .AddUnsuppress(context)
                    .AddUnsuppress2(context)
                    .AddRemoveFromService(context)
                    .AddRemoveFromService2(context)
                    .AddPlaceInService(context)
                    .AddPlaceInService2(context)
                    .AddGetGroupMemberships(context)
                    .AddFirstInGroupFlag(context);

                // Audible annunciation. AudibleSound carries the sound a client is meant
                // to play, and the stack clears the silence when the alarm activates again.
                node.AddAudibleEnabled(context).AddAudibleSound(context);

                // Re-alarming. A ReAlarmTime greater than zero is what turns the reminder
                // on; the schedule itself belongs to the application.
                node.AddReAlarmTime(context).AddReAlarmRepeatCount(context);

                // Only the trip alarm latches: a trip stays in the operator's face until
                // it is reset, while a level alarm follows the process value.
                if (alarm.AlarmType == kTripAlarmType)
                {
                    node.AddLatchedState(context).AddReset(context).AddReset2(context);
                }
            }

            // adding optional components to children is a little more complicated since the 
            // necessary initilization strings defined by the class that represents the child.
            // in this case we pre-create the child, add the optional components
            // and call create without assigning NodeIds. The NodeIds will be assigned when the
            // parent object is created.
            node.EnabledState = new TwoStateVariableState(node);
            node.EnabledState.TransitionTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(node.EnabledState);
            node.EnabledState.EffectiveDisplayName = PropertyState<LocalizedText>.With<VariantBuilder>(node.EnabledState);
            node.EnabledState.Create(context, NodeId.Null, new QualifiedName(BrowseNames.EnabledState), LocalizedText.Null, false);

            // same procedure add optional components to the ActiveState component.
            node.ActiveState = new TwoStateVariableState(node);
            node.ActiveState.TransitionTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(node.ActiveState);
            node.ActiveState.EffectiveDisplayName = PropertyState<LocalizedText>.With<VariantBuilder>(node.ActiveState);
            node.ActiveState.Create(context, NodeId.Null, new QualifiedName(BrowseNames.ActiveState), LocalizedText.Null, false);

            // This call initializes the condition from the type model (i.e. creates all of the objects
            // and variables requried to store its state). The information about the type model was 
            // incorporated into the class when the class was created.
            //
            // This method also assigns new NodeIds to all of the components by calling the INodeIdFactory.New
            // method on the INodeIdFactory object which is part of the system context. The NodeManager provides
            // the INodeIdFactory implementation used here.
            node.Create(
                context,
                NodeId.Null,
                new QualifiedName(alarm.Name, this.BrowseName.NamespaceIndex),
                LocalizedText.Null,
                true);

            // don't add branches to the address space.
            if ((branchId).IsNull)
            {
                AttachToSource(node);
            }

            // initialize event information.node
            node.EventType.Value = node.TypeDefinitionId;
            node.SourceNode.Value = this.NodeId;
            node.SourceName.Value = this.SymbolicName;
            node.ConditionName.Value = node.SymbolicName;
            node.Time.Value = DateTime.UtcNow;
            node.ReceiveTime.Value = node.Time.Value;
            node.BranchId.Value = branchId;

            // set up method handlers.
            node.OnEnableDisable = OnEnableDisableAlarm;
            node.OnAcknowledge = OnAcknowledge;
            node.OnAddComment = OnAddComment;
            node.OnConfirm = OnConfirm;
            node.OnShelve = OnShelve;
            node.OnTimedUnshelve = OnTimedUnshelve;

            if ((branchId).IsNull)
            {
                // the veto delegates of the Part 9 Methods. They run before the stack
                // applies the transition, so this is where the underlying system is told
                // about the change and where an operation can be refused.
                node.OnSilenceRequested = OnSilenceRequested;
                node.OnSuppressRequested = OnSuppressRequested;
                node.OnOutOfServiceRequested = OnOutOfServiceRequested;
                node.OnResetRequested = OnResetRequested;

                node.AudibleEnabled.Value = true;
                node.AudibleSound.Value = s_alarmSound;
                node.ReAlarmTime.Value = kReAlarmTime.TotalMilliseconds;
                node.ReAlarmRepeatCount.Value = 0;
                node.FirstInGroupFlag.Value = false;

                // membership is a pair of AlarmGroupMember references, so the group has to
                // know the node id the alarm was just given.
                m_group.AddMember(node);

                if (alarm.AlarmType == kTripAlarmType)
                {
                    m_tripAlarm = node;
                }
            }

            // return the new node.
            return node;
        }

        /// <summary>
        /// Updates the alarm with a new state. The caller must hold the source lock.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="alarm">The alarm.</param>
        /// <param name="alsoChanged">
        /// Collects the other alarms of the source which this update changed as well; a
        /// trip alarm which activates suppresses the alarms which follow it.
        /// </param>
        private void UpdateAlarm(
            AlarmConditionState node,
            UnderlyingSystemAlarm alarm,
            List<AlarmConditionState> alsoChanged = null)
        {
            ISystemContext context = m_nodeManager.SystemContext;

            // remove old event.
            if (!node.EventId.Value.IsNull)
            {
                m_events.Remove(Utils.ToHexString(node.EventId.Value.ToArray()));
            }

            // update the basic event information (include generating a unique id for the event).
            node.EventId.Value = Guid.NewGuid().ToByteArray().ToByteString();
            node.Time.Value = DateTime.UtcNow;
            node.ReceiveTime.Value = node.Time.Value;

            // save the event for later lookup.
            m_events[Utils.ToHexString(node.EventId.Value.ToArray())] = node;

            // determine the retain state.
            node.Retain.Value = true;

            if (alarm != null)
            {
                node.Time.Value = alarm.Time;
                node.Message.Value = new LocalizedText(alarm.Reason);

                // update the states.
                node.SetEnableState(context, (alarm.State & UnderlyingSystemAlarmStates.Enabled) != 0);
                node.SetAcknowledgedState(context, (alarm.State & UnderlyingSystemAlarmStates.Acknowledged) != 0);
                node.SetConfirmedState(context, (alarm.State & UnderlyingSystemAlarmStates.Confirmed) != 0);

                // the active state is only pushed when it actually changed. SetActiveState
                // latches the alarm and lifts the silence every time it is called with
                // true, and an alarm which merely gets a comment has not activated again.
                bool active = (alarm.State & UnderlyingSystemAlarmStates.Active) != 0;
                bool isBranch = !(node.BranchId.Value).IsNull;

                if (node.ActiveState.Id.Value != active)
                {
                    node.SetActiveState(context, active);

                    if (active && !isBranch)
                    {
                        m_rateTracker.RecordActivation();
                    }

                    // an activation is audible again; the stack lifts SilenceState and
                    // hands the client the sound to play.
                    node.UpdateAudibleState(context, active, s_alarmSound);

                    if (Object.ReferenceEquals(node, m_tripAlarm))
                    {
                        ApplyFirstInGroup(active, alsoChanged);
                    }
                }

                // update other information.
                node.SetComment(context, new LocalizedText(alarm.Comment), alarm.UserName);
                node.SetSeverity(context, alarm.Severity);

                node.EnabledState.TransitionTime.Value = alarm.EnableTime;
                node.ActiveState.TransitionTime.Value = alarm.ActiveTime;

                // check for deleted items.
                if ((alarm.State & UnderlyingSystemAlarmStates.Deleted) != 0)
                {
                    node.Retain.Value = false;
                }

                // handle high alarms.
                ExclusiveLimitAlarmState highAlarm = node as ExclusiveLimitAlarmState;

                if (highAlarm != null)
                {
                    highAlarm.HighLimit.Value = alarm.Limits[0];

                    if ((alarm.State & UnderlyingSystemAlarmStates.High) != 0)
                    {
                        highAlarm.SetLimitState(context, LimitAlarmStates.High);
                    }
                }

                // handle high-low alarms.
                NonExclusiveLimitAlarmState highLowAlarm = node as NonExclusiveLimitAlarmState;

                if (highLowAlarm != null)
                {
                    highLowAlarm.HighHighLimit.Value = alarm.Limits[0];
                    highLowAlarm.HighLimit.Value = alarm.Limits[1];
                    highLowAlarm.LowLimit.Value = alarm.Limits[2];
                    highLowAlarm.LowLowLimit.Value = alarm.Limits[3];

                    LimitAlarmStates limit = LimitAlarmStates.Inactive;

                    if ((alarm.State & UnderlyingSystemAlarmStates.HighHigh) != 0)
                    {
                        limit |= LimitAlarmStates.HighHigh;
                    }

                    if ((alarm.State & UnderlyingSystemAlarmStates.High) != 0)
                    {
                        limit |= LimitAlarmStates.High;
                    }

                    if ((alarm.State & UnderlyingSystemAlarmStates.Low) != 0)
                    {
                        limit |= LimitAlarmStates.Low;
                    }

                    if ((alarm.State & UnderlyingSystemAlarmStates.LowLow) != 0)
                    {
                        limit |= LimitAlarmStates.LowLow;
                    }

                    highLowAlarm.SetLimitState(context, limit);
                }

                // The states which the limit handling above would undo. SetLimitState
                // re-applies the active state, and activating an alarm lifts its silence
                // and sets its latch, so the states the underlying system owns have to be
                // pushed after it rather than before it.
                node.SetLatchedState(context, (alarm.State & UnderlyingSystemAlarmStates.Latched) != 0);
                node.SetSilenceState(context, (alarm.State & UnderlyingSystemAlarmStates.Silenced) != 0);
                node.SetOutOfServiceState(context, (alarm.State & UnderlyingSystemAlarmStates.OutOfService) != 0);

                // the suppression causes are tracked per alarm of the source, so a branch -
                // which shares its name with the alarm it was archived from and carries no
                // SuppressedState of its own - stays out of the bookkeeping.
                if (!isBranch)
                {
                    if ((alarm.State & UnderlyingSystemAlarmStates.Suppressed) != 0)
                    {
                        m_systemSuppressed.Add(node.SymbolicName);
                    }
                    else
                    {
                        m_systemSuppressed.Remove(node.SymbolicName);
                    }

                    ApplySuppression(node);
                    UpdateReAlarmSchedule(node);
                }
            }

            // not interested in disabled or inactive alarms - unless the alarm latched:
            // a latching alarm stays retained until it is reset, which is what makes a
            // client which connects afterwards see it in a condition refresh
            // (OPC UA Part 9 4.8).
            bool latched = node.LatchedState != null && node.LatchedState.Id.Value;

            if (!node.EnabledState.Id.Value || (!node.ActiveState.Id.Value && !latched))
            {
                node.Retain.Value = false;
            }
        }

        /// <summary>
        /// Keeps the re-alarm deadline of an alarm in step with its state. The caller must
        /// hold the source lock.
        /// </summary>
        /// <remarks>
        /// A reminder is owed while the alarm is enabled, active and unacknowledged; every
        /// other state cancels it and resets the repeat count.
        /// </remarks>
        private void UpdateReAlarmSchedule(AlarmConditionState node)
        {
            if (!node.IsReAlarmEnabled)
            {
                return;
            }

            bool pending = node.EnabledState.Id.Value
                && node.ActiveState.Id.Value
                && node.AckedState != null
                && !node.AckedState.Id.Value;

            if (pending)
            {
                if (!m_reAlarmDue.ContainsKey(node.SymbolicName))
                {
                    m_reAlarmDue[node.SymbolicName] = DateTime.UtcNow.Add(kReAlarmTime);
                }

                return;
            }

            m_reAlarmDue.Remove(node.SymbolicName);
            node.ResetReAlarmRepeatCount(m_nodeManager.SystemContext);
        }

        /// <summary>
        /// Called when the alarm is enabled or disabled.
        /// </summary>
        private ServiceResult OnEnableDisableAlarm(
            ISystemContext context,
            ConditionState condition,
            bool enabling)
        {
            m_source.EnableAlarm(condition.SymbolicName, enabling);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the alarm has a comment added.
        /// </summary>
        private ServiceResult OnAddComment(
            ISystemContext context,
            ConditionState condition,
            ByteString eventId,
            LocalizedText comment)
        {
            AlarmConditionState alarm = FindAlarmByEventId(eventId);

            if (alarm == null)
            {
                return StatusCodes.BadEventIdUnknown;
            }

            m_source.CommentAlarm(alarm.SymbolicName, GetRecordNumber(alarm), comment, GetUserName(context));

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the alarm is acknowledged.
        /// </summary>
        private ServiceResult OnAcknowledge(
            ISystemContext context,
            ConditionState condition,
            ByteString eventId,
            LocalizedText comment)
        {
            AlarmConditionState alarm = FindAlarmByEventId(eventId);

            if (alarm == null)
            {
                return StatusCodes.BadEventIdUnknown;
            }

            m_source.AcknowledgeAlarm(alarm.SymbolicName, GetRecordNumber(alarm), comment, GetUserName(context));

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the alarm is confirmed.
        /// </summary>
        private ServiceResult OnConfirm(
            ISystemContext context,
            ConditionState condition,
            ByteString eventId,
            LocalizedText comment)
        {
            AlarmConditionState alarm = FindAlarmByEventId(eventId);

            if (alarm == null)
            {
                return StatusCodes.BadEventIdUnknown;
            }

            m_source.ConfirmAlarm(alarm.SymbolicName, GetRecordNumber(alarm), comment, GetUserName(context));

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called before the alarm is silenced.
        /// </summary>
        private ServiceResult OnSilenceRequested(ISystemContext context, AlarmConditionState alarm)
        {
            m_source.SilenceAlarm(alarm.SymbolicName, GetRecordNumber(alarm));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Called before the alarm is suppressed or unsuppressed by a client.
        /// </summary>
        private ServiceResult OnSuppressRequested(
            ISystemContext context,
            AlarmConditionState alarm,
            bool suppressing)
        {
            m_source.SuppressAlarm(alarm.SymbolicName, GetRecordNumber(alarm), suppressing);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Called before the alarm is removed from or placed back in service.
        /// </summary>
        private ServiceResult OnOutOfServiceRequested(
            ISystemContext context,
            AlarmConditionState alarm,
            bool outOfService)
        {
            m_source.SetAlarmOutOfService(alarm.SymbolicName, GetRecordNumber(alarm), outOfService);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Called before the latch of the alarm is cleared.
        /// </summary>
        /// <remarks>
        /// The stack has already checked the preconditions Part 9 puts on Reset - enabled,
        /// inactive, acknowledged and confirmed - by the time this runs. What is left is
        /// the policy of the application, and refusing here is what makes the Method
        /// answer the client with an error instead of clearing the latch.
        /// </remarks>
        private ServiceResult OnResetRequested(ISystemContext context, AlarmConditionState alarm)
        {
            if (m_source.IsOffline)
            {
                return new ServiceResult(
                    StatusCodes.BadUserAccessDenied,
                    new LocalizedText(
                        "The source is offline and cannot confirm that the trip is gone. " +
                        "Put it back online before resetting the alarm."));
            }

            m_source.ResetAlarm(alarm.SymbolicName, GetRecordNumber(alarm));
            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the alarm is shelved.
        /// </summary>
        private ServiceResult OnShelve(
            ISystemContext context,
            AlarmConditionState alarm,
            bool shelving,
            bool oneShot,
            double shelvingTime)
        {
            lock (m_lock)
            {
                alarm.SetShelvingState(context, shelving, oneShot, shelvingTime);
                alarm.Message.Value = new LocalizedText("The alarm shelved.");

                UpdateAlarm(alarm, null);
            }

            ReportChanges(alarm);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the alarm is shelved.
        /// </summary>
        private ServiceResult OnTimedUnshelve(
            ISystemContext context,
            AlarmConditionState alarm)
        {
            lock (m_lock)
            {
                // update the alarm state and produce and event.
                alarm.SetShelvingState(context, false, false, 0);
                alarm.Message.Value = new LocalizedText("The timed shelving period expired.");

                UpdateAlarm(alarm, null);
            }

            ReportChanges(alarm);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the dialog receives a response.
        /// </summary>
        private ServiceResult OnRespond(
            ISystemContext context,
            DialogConditionState dialog,
            int selectedResponse)
        {
            // response 0 means set the source online.
            if (selectedResponse == 0)
            {
                m_source.SetOfflineState(false);
            }

            // response 1 means set the source offine.
            if (selectedResponse == 1)
            {
                m_source.SetOfflineState(true);
            }

            lock (m_lock)
            {
                // other responses mean do nothing.
                dialog.SetResponse(context, selectedResponse);

                // the dialog is the only way an operator has of taking the source offline
                // and of putting it back online, so it is armed again rather than retired
                // after one answer. A deactivated dialog answers BadDialogNotActive.
                dialog.Activate(context);
                dialog.Message.Value = new LocalizedText(
                    Utils.Format(
                        "The source is now {0}. Respond again to change it.",
                        m_source.IsOffline ? "offline" : "online"));

                dialog.Retain.Value = true;
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Reports the changes to the alarm through the asynchronous event path.
        /// </summary>
        private async Task ReportChangesAsync(AlarmConditionState alarm, CancellationToken cancellationToken = default)
        {
            // report changes to node attributes.
            await alarm.ClearChangeMasksAsync(m_nodeManager.SystemContext, true, cancellationToken).ConfigureAwait(false);

            // check if events are being monitored for the source.
            if (this.AreEventsMonitored)
            {
                // create a snapshot.
                InstanceStateSnapshot e = new InstanceStateSnapshot();
                e.Initialize(m_nodeManager.SystemContext, alarm);

                // report the event.
                await alarm.ReportEventAsync(m_nodeManager.SystemContext, e, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reports the changes to the alarm.
        /// </summary>
        /// <remarks>
        /// The shelving handlers are synchronous delegates, so they use this synchronous
        /// bridge; it still drives the asynchronous notification sinks of the node state.
        /// </remarks>
        private void ReportChanges(AlarmConditionState alarm)
        {
            // report changes to node attributes.
            alarm.ClearChangeMasks(m_nodeManager.SystemContext, true);

            // check if events are being monitored for the source.
            if (this.AreEventsMonitored)
            {
                // create a snapshot.
                InstanceStateSnapshot e = new InstanceStateSnapshot();
                e.Initialize(m_nodeManager.SystemContext, alarm);

                // report the event.
                alarm.ReportEvent(m_nodeManager.SystemContext, e);
            }
        }

        /// <summary>
        /// Finds the alarm by event id.
        /// </summary>
        /// <param name="eventId">The event id.</param>
        /// <returns>The alarm. Null if not found.</returns>
        private AlarmConditionState FindAlarmByEventId(ByteString eventId)
        {
            if (eventId.IsNull)
            {
                return null;
            }

            AlarmConditionState alarm = null;

            lock (m_lock)
            {
                if (!m_events.TryGetValue(Utils.ToHexString(eventId.ToArray()), out alarm))
                {
                    return null;
                }
            }

            return alarm;
        }

        /// <summary>
        /// Gets the record number associated with tge alarm.
        /// </summary>
        /// <param name="alarm">The alarm.</param>
        /// <returns>The record number; 0 if the alarm is not an archived alarm.</returns>
        private uint GetRecordNumber(AlarmConditionState alarm)
        {
            if (alarm == null)
            {
                return 0;
            }

            if (alarm.BranchId == null || alarm.BranchId.Value.IsNull)
            {
                return 0;
            }

            uint? recordNumber = alarm.BranchId.Value.TryGetValue(out uint id) ? id : null;

            if (recordNumber != null)
            {
                return recordNumber.Value;
            }

            return 0;
        }

        /// <summary>
        /// Gets the user name associated with the context.
        /// </summary>
        private string GetUserName(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.UserIdentity?.DisplayName;
        }

        /// <summary>
        /// Builds the wave file an audible alarm hands to its clients: a fifth of a second
        /// of an 880 Hz tone, mono, eight bit, eight kilohertz.
        /// </summary>
        private static ByteString CreateAlarmSound()
        {
            const int sampleRate = 8000;
            const int sampleCount = sampleRate / 5;
            const double frequency = 880.0;

            var buffer = new byte[44 + sampleCount];

            void WriteAscii(int offset, string text)
            {
                for (int ii = 0; ii < text.Length; ii++)
                {
                    buffer[offset + ii] = (byte)text[ii];
                }
            }

            WriteAscii(0, "RIFF");
            BitConverter.TryWriteBytes(buffer.AsSpan(4), 36 + sampleCount);
            WriteAscii(8, "WAVEfmt ");
            BitConverter.TryWriteBytes(buffer.AsSpan(16), 16);              // subchunk size
            BitConverter.TryWriteBytes(buffer.AsSpan(20), (short)1);        // PCM
            BitConverter.TryWriteBytes(buffer.AsSpan(22), (short)1);        // mono
            BitConverter.TryWriteBytes(buffer.AsSpan(24), sampleRate);
            BitConverter.TryWriteBytes(buffer.AsSpan(28), sampleRate);      // byte rate
            BitConverter.TryWriteBytes(buffer.AsSpan(32), (short)1);        // block align
            BitConverter.TryWriteBytes(buffer.AsSpan(34), (short)8);        // bits per sample
            WriteAscii(36, "data");
            BitConverter.TryWriteBytes(buffer.AsSpan(40), sampleCount);

            for (int ii = 0; ii < sampleCount; ii++)
            {
                double angle = 2.0 * Math.PI * frequency * ii / sampleRate;
                buffer[44 + ii] = (byte)(128 + (127 * Math.Sin(angle)));
            }

            return buffer.ToByteString();
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The type the underlying system gives to an alarm which trips rather than
        /// following a level; only this one latches and only this one leads its group.
        /// </summary>
        private const string kTripAlarmType = "TripAlarm";

        /// <summary>
        /// The type of source whose group is led by its trip alarm.
        /// </summary>
        private const string kFirstInGroupSourceType = "Metals";

        private const string kAlarmGroupName = "Alarms";
        private const string kAlarmMetricsName = "AlarmMetrics";
        private const string kMaintenanceModeName = "MaintenanceMode";

        /// <summary>
        /// How long an alarm may stay active and unacknowledged before it asks again.
        /// </summary>
        private static readonly TimeSpan kReAlarmTime = TimeSpan.FromSeconds(15);

        /// <summary>
        /// The window the alarm rate of a source is measured over.
        /// </summary>
        private static readonly TimeSpan kAlarmRateWindow = TimeSpan.FromMinutes(1);

        /// <summary>
        /// The sound an audible alarm hands to its clients.
        /// </summary>
        /// <remarks>
        /// A real server would load a wave file here. The sample builds one so that it
        /// stays a single source file, and because what matters for the sample is that
        /// AudibleSound carries something a client can play.
        /// </remarks>
        private static readonly ByteString s_alarmSound = CreateAlarmSound();

        private readonly Lock m_lock = new();
        private AlarmConditionServerNodeManager m_nodeManager;
        private ILogger m_logger;
        private UnderlyingSystemSource m_source;
        private Dictionary<string, AlarmConditionState> m_alarms;
        private Dictionary<string, AlarmConditionState> m_events;
        private Dictionary<NodeId, AlarmConditionState> m_branches;
#pragma warning disable CA2213 // Justification: Dialog condition state is part of the source node model lifetime.
        private DialogConditionState m_dialog;
#pragma warning restore CA2213
        private AlarmGroup m_group;
        private AlarmConditionState m_tripAlarm;
        private AlarmMetricsState m_metrics;
        private BaseDataVariableState m_maintenanceMode;
        private AlarmRateTracker m_rateTracker;
        private AlarmSuppressionEngine m_engine;

        /// <summary>
        /// The suppression causes. A single SuppressedState carries all of them, so the
        /// alarm is suppressed while any of these says so.
        /// </summary>
        private readonly HashSet<string> m_systemSuppressed = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_firstInGroupSuppressed = new(StringComparer.Ordinal);
        private bool m_maintenanceSuppressed;
        private bool m_firstInGroupSuppression;

        /// <summary>
        /// When each alarm which is waiting for an acknowledgement asks again.
        /// </summary>
        private readonly Dictionary<string, DateTime> m_reAlarmDue = new(StringComparer.Ordinal);
        #endregion
    }
}
