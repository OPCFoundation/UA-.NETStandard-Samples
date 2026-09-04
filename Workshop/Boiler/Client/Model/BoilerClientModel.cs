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

namespace Quickstarts.Boiler.Client.Model
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, and
    // Opc.Ua itself has a server side IMonitoredItem, so the client types are aliased.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
    using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

    /// <summary>
    /// One boiler the server offers.
    /// </summary>
    /// <param name="NodeId">The node of the boiler object.</param>
    /// <param name="DisplayName">The name the server gives it.</param>
    public sealed record BoilerInfo(NodeId NodeId, string DisplayName)
    {
        /// <inheritdoc/>
        public override string ToString()
        {
            return DisplayName;
        }
    }

    /// <summary>
    /// The four variables of a boiler the client watches.
    /// </summary>
    public enum BoilerVariable
    {
        /// <summary>The flow through the input pipe (PipeX001/FTX001/Output).</summary>
        InputPipeFlow,

        /// <summary>The level of the drum (DrumX001/LIX001/Output).</summary>
        DrumLevel,

        /// <summary>The flow through the output pipe (PipeX002/FTX002/Output).</summary>
        OutputPipeFlow,

        /// <summary>The set point of the level controller (LCX001/SetPoint).</summary>
        DrumLevelSetPoint,
    }

    /// <summary>
    /// The payload of <see cref="BoilerClientModel.ValueChanged"/>.
    /// </summary>
    public sealed class BoilerValueChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public BoilerValueChangedEventArgs(BoilerVariable variable, DataValue value)
        {
            Variable = variable;
            Value = value;
        }

        /// <summary>
        /// Which variable changed.
        /// </summary>
        public BoilerVariable Variable { get; }

        /// <summary>
        /// Its new value.
        /// </summary>
        public DataValue Value { get; }
    }

    /// <summary>
    /// The client model of the Boiler client: finds the boilers of the server and watches
    /// the four variables of the one which is selected.
    /// </summary>
    /// <remarks>
    /// The window hands the session over with <see cref="SampleClientModel.AttachAsync"/>,
    /// which browses the Objects folder for boilers; <see cref="SelectBoilerAsync"/> then
    /// creates one subscription on the V2 engine with a monitored item per variable, and
    /// every data change is reported through <see cref="ValueChanged"/> on the thread the
    /// model was created on. Selecting another boiler replaces the subscription; detaching
    /// deletes it.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The handles below are taken, cleared and released by OnDetachingAsync, which the detach of the base class runs - on a detach as well as on a dispose. The analyzer does not follow an asynchronous release through a virtual hook.")]
    public sealed class BoilerClientModel : SampleClientModel
    {
        /// <summary>
        /// The namespace of the boiler model, for a caller which cannot name the generated
        /// constants (they exist in the server assembly as well).
        /// </summary>
        public const string BoilerNamespaceUri = Namespaces.Boiler;

        /// <summary>
        /// The browse path of each variable below a boiler, in the namespace of the model.
        /// </summary>
        private static readonly (BoilerVariable Variable, string BrowsePath)[] s_variables =
        {
            (BoilerVariable.InputPipeFlow, "1:PipeX001/1:FTX001/1:Output"),
            (BoilerVariable.DrumLevel, "1:DrumX001/1:LIX001/1:Output"),
            (BoilerVariable.OutputPipeFlow, "1:PipeX002/1:FTX002/1:Output"),
            (BoilerVariable.DrumLevelSetPoint, "1:LCX001/1:SetPoint"),
        };

        // the V2 engine takes the notification handler when the subscription is created,
        // so the model owns one for its whole lifetime and points it at its own method.
        private readonly SubscriptionCallbacks m_callbacks = new SubscriptionCallbacks();

        // the V2 engine identifies an item by a name which is unique within its
        // subscription and reports that name with every notification. Replaced as a whole
        // when the selection changes, so the callback never sees a half built table.
        private IReadOnlyDictionary<string, BoilerVariable> m_variablesByItem = new Dictionary<string, BoilerVariable>();

        private ISubscription m_subscription;
        private IReadOnlyList<BoilerInfo> m_boilers = Array.Empty<BoilerInfo>();

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public BoilerClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
            m_callbacks.DataChangeCallback = OnDataChanges;
        }

        /// <summary>
        /// The boilers the server offers, found when the session was attached.
        /// </summary>
        public IReadOnlyList<BoilerInfo> Boilers => m_boilers;

        /// <summary>
        /// The boiler whose variables are being watched, or null.
        /// </summary>
        public BoilerInfo SelectedBoiler { get; private set; }

        /// <summary>
        /// Raised for every change of a variable of the selected boiler.
        /// </summary>
        public event EventHandler<BoilerValueChangedEventArgs> ValueChanged;

        /// <summary>
        /// Watches the variables of a boiler, dropping the subscription of the previous one.
        /// </summary>
        /// <param name="boiler">The boiler, or null to stop watching.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task SelectBoilerAsync(BoilerInfo boiler, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            // the previous boiler is dropped with its subscription: disposing it deletes
            // the subscription on the server and removes it from the manager.
            await DeleteSubscriptionAsync().ConfigureAwait(false);

            SelectedBoiler = boiler;

            if (boiler == null)
            {
                return;
            }

            // the V2 engine takes the settings through an options monitor and creates the
            // subscription on the server on its own worker.
            var options = new OptionsMonitor<SubscriptionOptions>(
                SampleSession.DefaultSubscriptionOptions with { Priority = 1, LifetimeCount = 20 });

            ISubscription subscription = SampleSession.AddSubscription(session, m_callbacks, options);
            m_subscription = subscription;

            // this client has built-in knowledge of the boiler model: the browse paths of
            // the four variables, in the namespace of the model.
            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(Namespaces.Boiler);

            var browsePaths = new string[s_variables.Length];

            for (int ii = 0; ii < s_variables.Length; ii++)
            {
                browsePaths[ii] = s_variables[ii].BrowsePath;
            }

            List<NodeId> nodes = await SampleSession.TranslateBrowsePathsAsync(
                session,
                boiler.NodeId,
                wellKnownNamespaceUris,
                ct,
                browsePaths).ConfigureAwait(false);

            var variablesByItem = new Dictionary<string, BoilerVariable>(StringComparer.Ordinal);

            for (int ii = 0; ii < nodes.Count && ii < s_variables.Length; ii++)
            {
                if (nodes[ii].IsNull)
                {
                    continue;
                }

                // adding the item to the collection is the create request: the engine
                // applies it on its own worker, there is no ApplyChanges to call.
                string name = s_variables[ii].BrowsePath;

                variablesByItem[name] = s_variables[ii].Variable;

                subscription.MonitoredItems.TryAdd(
                    name,
                    new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions {
                        StartNodeId = nodes[ii],
                        AttributeId = Attributes.Value,
                    }),
                    out IMonitoredItem _);
            }

            m_variablesByItem = variablesByItem;
        }

        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            m_boilers = await FindBoilersAsync(RequireSession(), ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override async Task OnDetachingAsync()
        {
            // done before the session is closed: closing a session which still carries a
            // subscription waits for the publish pipeline to drain.
            await DeleteSubscriptionAsync().ConfigureAwait(false);

            SelectedBoiler = null;
            m_boilers = Array.Empty<BoilerInfo>();
        }

        // a V2 subscription belongs to the subscription manager of the session and survives
        // a reconnect together with its monitored items, so there is nothing to re-attach:
        // the reconnect hooks of the base class are not overridden.

        /// <summary>
        /// Browses the Objects folder for the objects of the boiler type.
        /// </summary>
        private static async Task<IReadOnlyList<BoilerInfo>> FindBoilersAsync(ISession session, CancellationToken ct)
        {
            var nodeToBrowse = new BrowseDescription {
                NodeId = Opc.Ua.ObjectIds.ObjectsFolder,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All,
            };

            List<ReferenceDescription> references = await SampleSession.BrowseAsync(
                session,
                nodeToBrowse,
                false,
                ct).ConfigureAwait(false);

            var boilers = new List<BoilerInfo>();

            if (references == null)
            {
                return boilers;
            }

            NodeId boilerTypeId = ExpandedNodeId.ToNodeId(ObjectTypeIds.BoilerType, session.NamespaceUris);

            foreach (ReferenceDescription reference in references)
            {
                if (boilerTypeId == reference.TypeDefinition && !reference.NodeId.IsAbsolute)
                {
                    boilers.Add(new BoilerInfo(
                        ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris),
                        reference.DisplayName.IsNullOrEmpty ? reference.BrowseName.Name : reference.DisplayName.Text));
                }
            }

            return boilers;
        }

        /// <summary>
        /// Deletes the subscription on the server and drops it from the subscription manager.
        /// </summary>
        private async Task DeleteSubscriptionAsync()
        {
            ISubscription subscription = m_subscription;

            m_subscription = null;
            m_variablesByItem = new Dictionary<string, BoilerVariable>();

            if (subscription != null)
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reports the new values of the monitored variables.
        /// </summary>
        /// <remarks>
        /// The V2 engine calls this on a publish worker and reports the whole notification
        /// instead of one value per item. Each change is turned into one event, which the
        /// base class posts to the thread the model was created on.
        /// </remarks>
        private void OnDataChanges(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            DataValueChange[] notifications,
            PublishState publishState)
        {
            IReadOnlyDictionary<string, BoilerVariable> variablesByItem = m_variablesByItem;

            foreach (DataValueChange change in notifications)
            {
                if (change.MonitoredItem == null
                    || !variablesByItem.TryGetValue(change.MonitoredItem.Name, out BoilerVariable variable))
                {
                    continue;
                }

                Raise(ValueChanged, new BoilerValueChangedEventArgs(variable, change.Value));
            }
        }
    }
}
