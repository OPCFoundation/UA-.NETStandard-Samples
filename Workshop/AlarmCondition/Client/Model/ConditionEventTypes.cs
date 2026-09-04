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
using Opc.Ua;
using Opc.Ua.Client.Subscriptions;

namespace Quickstarts.AlarmConditionClient.Model
{
    /// <summary>
    /// The event types this client knows how to decode, and the shape it decodes a
    /// notification of the V2 engine into.
    /// </summary>
    /// <remarks>
    /// <see cref="Opc.Ua.Samples.Client.SampleSession.ConstructEventAsync"/> turns a
    /// notification into the state object of the closest known supertype of its event
    /// type, and looks that supertype up in this table. Every type which can be the target
    /// of such a mapping has to be in it - <c>BaseEventType</c> included, because it is
    /// where the supertype chain of an event nobody expected ends.
    /// </remarks>
    public static class ConditionEventTypes
    {
        /// <summary>
        /// A fresh table of the known event types. Each model keeps its own, because the
        /// helper which uses it also fills a mapping table next to it.
        /// </summary>
        public static Dictionary<NodeId, Type> CreateKnownTypes()
        {
            return new Dictionary<NodeId, Type> {
                [ObjectTypeIds.BaseEventType] = typeof(BaseEventState),
                [ObjectTypeIds.ConditionType] = typeof(ConditionState),
                [ObjectTypeIds.DialogConditionType] = typeof(DialogConditionState),
                [ObjectTypeIds.AlarmConditionType] = typeof(AlarmConditionState),
                [ObjectTypeIds.ExclusiveLimitAlarmType] = typeof(ExclusiveLimitAlarmState),
                [ObjectTypeIds.NonExclusiveLimitAlarmType] = typeof(NonExclusiveLimitAlarmState),
                [ObjectTypeIds.AuditEventType] = typeof(AuditEventState),
                [ObjectTypeIds.AuditUpdateMethodEventType] = typeof(AuditUpdateMethodEventState),
            };
        }

        /// <summary>
        /// The fields of a notification of the V2 engine in the shape the event helpers
        /// take: the fields line up with the select clauses of the filter the monitored
        /// item was created with.
        /// </summary>
        public static EventFieldList ToFieldList(EventNotification notification)
        {
            return new EventFieldList {
                ClientHandle = notification.MonitoredItem?.ClientHandle ?? 0,
                EventFields = notification.Fields,
            };
        }
    }
}
