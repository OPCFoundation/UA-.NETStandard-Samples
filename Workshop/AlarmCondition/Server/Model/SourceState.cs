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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

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

            // request an updated for all alarms.
            m_source.Refresh();
        }
        #endregion

        #region Public Interface
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
                        UpdateAlarm(branch, alarm);

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
                        UpdateAlarm(node, alarm);
                    }
                }

                await ReportChangesAsync(node).ConfigureAwait(false);
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

            // specify reference type between the source and the alarm.
            node.ReferenceTypeId = ReferenceTypeIds.HasComponent;

            // This call initializes the condition from the type model (i.e. creates all of the objects
            // and variables requried to store its state). The information about the type model was 
            // incorporated into the class when the class was created.
            node.Create(
                context,
                NodeId.Null,
                new QualifiedName(dialogName, this.BrowseName.NamespaceIndex),
                LocalizedText.Null,
                true);

            this.AddChild(node);

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

            // specify reference type between the source and the alarm.
            node.ReferenceTypeId = ReferenceTypeIds.HasComponent;

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
                this.AddChild(node);
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

            // return the new node.
            return node;
        }

        /// <summary>
        /// Updates the alarm with a new state. The caller must hold the source lock.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="alarm">The alarm.</param>
        private void UpdateAlarm(AlarmConditionState node, UnderlyingSystemAlarm alarm)
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
                node.SetActiveState(context, (alarm.State & UnderlyingSystemAlarmStates.Active) != 0);
                node.SetSuppressedState(context, (alarm.State & UnderlyingSystemAlarmStates.Suppressed) != 0);

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
            }

            // not interested in disabled or inactive alarms.
            if (!node.EnabledState.Id.Value || !node.ActiveState.Id.Value)
            {
                node.Retain.Value = false;
            }
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

                // dialog no longer interesting once it is deactivated.
                dialog.Message.Value = new LocalizedText("The dialog was deactivated");
                dialog.Retain.Value = false;
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
        #endregion    

        #region Private Fields
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
        #endregion
    }
}
