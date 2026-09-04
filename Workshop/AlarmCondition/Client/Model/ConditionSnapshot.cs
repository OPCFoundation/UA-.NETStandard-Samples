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

namespace Quickstarts.AlarmConditionClient.Model
{
    /// <summary>
    /// What identifies one row of the condition list: a condition, and the branch of it
    /// when the server reports a previous state which still needs attention.
    /// </summary>
    /// <remarks>
    /// Part 9 lets a condition carry branches - snapshots of earlier states which have
    /// not been acknowledged yet - and every one of them is shown on its own row, so the
    /// condition id alone does not identify a row.
    /// </remarks>
    /// <param name="ConditionId">The node of the condition instance.</param>
    /// <param name="BranchId">The branch, or a null node id for the current state.</param>
    public sealed record ConditionKey(NodeId ConditionId, NodeId BranchId)
    {
        /// <summary>
        /// True when the key names a branch rather than the current state.
        /// </summary>
        public bool IsBranch => !BranchId.IsNull;

        /// <inheritdoc/>
        public override string ToString()
        {
            return IsBranch ? Utils.Format("{0} [{1}]", ConditionId, BranchId) : Utils.Format("{0}", ConditionId);
        }
    }

    /// <summary>
    /// Whether a condition is new to the list or an existing one changed.
    /// </summary>
    public enum ConditionChange
    {
        /// <summary>The condition was not in the list before.</summary>
        Added,

        /// <summary>A later event of a condition which is already listed.</summary>
        Updated,
    }

    /// <summary>
    /// What the client shows for one condition: one field per column of the list, taken
    /// from the last event of the condition.
    /// </summary>
    /// <remarks>
    /// The window renders these as they are. The texts are already formatted the way the
    /// list shows them, which keeps the formatting of a column in one place and lets a
    /// test read the same text a user sees; only the time is left as a value, because
    /// the window formats it in local time.
    /// </remarks>
    /// <param name="Key">The condition and branch this describes.</param>
    /// <param name="EventId">The id of the event, which the Part 9 Methods which take a comment need.</param>
    /// <param name="SourceName">The name of the source which raised the condition.</param>
    /// <param name="ConditionName">The name of the condition.</param>
    /// <param name="BranchText">The branch id as text, or null for the current state.</param>
    /// <param name="TypeName">The name of the event type.</param>
    /// <param name="Severity">The severity of the last event.</param>
    /// <param name="Time">The time of the last event, in UTC.</param>
    /// <param name="StateText">The effective display name of the enabled state.</param>
    /// <param name="Flags">The Part 9 states which are set, as a short list.</param>
    /// <param name="Message">The message of the last event.</param>
    /// <param name="Comment">The last comment an operator attached.</param>
    /// <param name="Retain">Whether the condition still needs attention.</param>
    /// <param name="IsDialog">Whether the condition is a dialog which waits for a response.</param>
    /// <param name="DialogPrompt">The prompt of a dialog, or null.</param>
    /// <param name="DialogResponses">The responses a dialog offers, in the order the server lists them.</param>
    /// <param name="CanSilence">Whether the condition is an alarm with a silence state which is not silenced.</param>
    public sealed record ConditionSnapshot(
        ConditionKey Key,
        ByteString EventId,
        string SourceName,
        string ConditionName,
        string BranchText,
        string TypeName,
        ushort Severity,
        DateTimeUtc? Time,
        string StateText,
        string Flags,
        string Message,
        string Comment,
        bool Retain,
        bool IsDialog,
        string DialogPrompt,
        IReadOnlyList<string> DialogResponses,
        bool CanSilence)
    {
        /// <summary>
        /// True when the snapshot describes a branch of a condition.
        /// </summary>
        public bool IsBranch => Key.IsBranch;

        /// <summary>
        /// The severity the way the list shows it: the name of the level when the value
        /// is one, the number otherwise.
        /// </summary>
        public string SeverityText => Utils.Format("{0}", (EventSeverity)Severity);

        /// <inheritdoc/>
        public override string ToString()
        {
            return Utils.Format("{0}/{1} {2} [{3}]", SourceName, ConditionName, SeverityText, Flags);
        }
    }

    /// <summary>
    /// The payload of <see cref="AlarmConditionClientModel.ConditionChanged"/>.
    /// </summary>
    public sealed class ConditionChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public ConditionChangedEventArgs(ConditionChange change, ConditionSnapshot snapshot)
        {
            Change = change;
            Snapshot = snapshot;
        }

        /// <summary>
        /// Whether the condition is new to the list.
        /// </summary>
        public ConditionChange Change { get; }

        /// <summary>
        /// What the condition looks like now.
        /// </summary>
        public ConditionSnapshot Snapshot { get; }
    }

    /// <summary>
    /// What one Part 9 Method call on one condition answered.
    /// </summary>
    /// <remarks>
    /// The generated proxies call one condition at a time and throw on a bad status, so
    /// the per condition result which used to accompany a batched Call request is
    /// reconstructed here: the window writes a bad status into the row it belongs to.
    /// </remarks>
    /// <param name="Key">The condition the Method was called on.</param>
    /// <param name="Status">What the server answered.</param>
    public sealed record ConditionCallResult(ConditionKey Key, StatusCode Status)
    {
        /// <summary>
        /// True when the server accepted the call.
        /// </summary>
        public bool Succeeded => StatusCode.IsGood(Status);

        /// <inheritdoc/>
        public override string ToString()
        {
            return Utils.Format("{0}: {1}", Key, Status);
        }
    }

    /// <summary>
    /// The shelving Methods of Part 9.
    /// </summary>
    public enum ShelveAction
    {
        /// <summary>Puts the alarm back into the Unshelved state.</summary>
        Unshelve,

        /// <summary>Shelves the alarm until an operator unshelves it.</summary>
        Manual,

        /// <summary>Shelves the alarm until it goes inactive once.</summary>
        OneShot,

        /// <summary>Shelves the alarm for a given time.</summary>
        Timed,
    }

    /// <summary>
    /// Which shelving Method to call, and for how long when it is a timed shelve.
    /// </summary>
    /// <param name="Action">The Method.</param>
    /// <param name="ShelvingTime">The shelving time in milliseconds, for <see cref="ShelveAction.Timed"/>.</param>
    public sealed record ShelveRequest(ShelveAction Action, double ShelvingTime = 0)
    {
        /// <summary>Unshelves the alarm.</summary>
        public static ShelveRequest Unshelve { get; } = new ShelveRequest(ShelveAction.Unshelve);

        /// <summary>Shelves the alarm until an operator unshelves it.</summary>
        public static ShelveRequest Manual { get; } = new ShelveRequest(ShelveAction.Manual);

        /// <summary>Shelves the alarm until it goes inactive once.</summary>
        public static ShelveRequest OneShot { get; } = new ShelveRequest(ShelveAction.OneShot);

        /// <summary>Shelves the alarm for the given number of milliseconds.</summary>
        public static ShelveRequest Timed(double shelvingTime)
        {
            return new ShelveRequest(ShelveAction.Timed, shelvingTime);
        }
    }

    /// <summary>
    /// What toggling the maintenance flag of a source came to.
    /// </summary>
    public enum MaintenanceModeOutcome
    {
        /// <summary>The event did not name a source node.</summary>
        NoSource,

        /// <summary>The source does not offer a maintenance flag.</summary>
        NoFlag,

        /// <summary>The flag was written.</summary>
        Written,

        /// <summary>The server refused the write.</summary>
        Failed,
    }

    /// <summary>
    /// The result of <see cref="AlarmConditionClientModel.ToggleMaintenanceModeAsync"/>.
    /// </summary>
    /// <param name="Outcome">What happened.</param>
    /// <param name="MaintenanceMode">The value of the flag after the call, when it was written.</param>
    /// <param name="Status">The status of the write, when the server refused it.</param>
    public sealed record MaintenanceModeResult(MaintenanceModeOutcome Outcome, bool MaintenanceMode, StatusCode Status);

    /// <summary>
    /// The raw fields of the last event of a condition together with the filter which
    /// says what each field is, for the details dialog.
    /// </summary>
    /// <param name="Filter">The filter the monitored item was created with.</param>
    /// <param name="Fields">The fields of the event, one per select clause of the filter.</param>
    public sealed record ConditionDetails(EventFilter Filter, EventFieldList Fields);
}
