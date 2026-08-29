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
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Historian;

namespace TestData
{
    /// <summary>
    /// The node manager factory for test data.
    /// </summary>
    /// <remarks>
    /// Hand-written because the node manager needs the application configuration
    /// and its own constructor; the source generator therefore only emits the
    /// node manager partial (<c>GenerateFactory = false</c>).
    /// </remarks>
    public class TestDataNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(IServerInternal server, ApplicationConfiguration configuration, CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: ownership of the node manager transfers to the caller.
            return new ValueTask<IAsyncNodeManager>(new TestDataNodeManager(
                server,
                configuration,
                server.Telemetry.CreateLogger<TestDataNodeManager>()));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
        {
            get
            {
                var nameSpaces = new List<string> {
                    Namespaces.TestData,
                    Namespaces.TestData + "Instance"
                };
                return nameSpaces;
            }
        }
    }

    /// <summary>
    /// A node manager for a variety of test data.
    /// </summary>
    /// <remarks>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to source
    /// generation: the generator emits a sibling partial which derives from
    /// <c>AsyncCustomNodeManager</c>, loads the predefined nodes generated from
    /// <c>TestDataDesign.xml</c> as typed node states - so the passive-node
    /// replacement the old node manager did by hand is no longer needed - and
    /// calls <see cref="Configure"/> once the address space is in place.
    /// </remarks>
    [NodeManager(NamespaceUri = "http://test.org/UA/Data/", GenerateFactory = false)]
    public partial class TestDataNodeManager : ITestDataSystemCallback
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        /// <remarks>
        /// The typed node states created from the model pull their initial values
        /// out of the test system through <see cref="ISystemContext.SystemHandle"/>,
        /// so the system has to exist before the base class loads the predefined
        /// nodes. The second namespace is the one the node id factory hands out
        /// for dynamically created nodes.
        /// </remarks>
        public TestDataNodeManager(IServerInternal server, ApplicationConfiguration configuration, ILogger<TestDataNodeManager> logger)
        :
            base(server, configuration, logger, Namespaces.TestData, Namespaces.TestData + "Instance")
        {
            SystemContext.NodeIdFactory = this;

            Server.Factory.AddEncodeableTypes(typeof(TestDataNodeManager).Assembly.GetExportedTypes().Where(t => t.FullName.StartsWith(typeof(TestDataNodeManager).Namespace, StringComparison.Ordinal)));

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<TestDataNodeManagerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new TestDataNodeManagerConfiguration();
            }

            m_lastUsedId = m_configuration.NextUnusedId - 1;

            // create the object used to access the test system.
            m_system = new TestDataSystem(this, server.NamespaceUris, server.ServerUris, server.Telemetry);

            // update the default context.
            SystemContext.SystemHandle = m_system;
        }
        #endregion

        #region ITestDataSystemCallback Members
        /// <summary>
        /// Updates the variable after receiving a notification that it has changed in the underlying system.
        /// </summary>
        public async ValueTask OnDataChangeAsync(
            BaseVariableState variable,
            object value,
            StatusCode statusCode,
            DateTime timestamp,
            CancellationToken cancellationToken = default)
        {
            variable.Value = ToVariant(value);
            variable.StatusCode = statusCode;
            variable.Timestamp = timestamp;

            // notifies any monitored items that the value has changed.
            await variable.ClearChangeMasksAsync(SystemContext, false, cancellationToken).ConfigureAwait(false);
        }

        private static Variant ToVariant(object value)
        {
            switch (value)
            {
                case null: return Variant.Null;
                case Variant v: return v;
                case bool v: return Variant.From(v);
                case sbyte v: return Variant.From(v);
                case byte v: return Variant.From(v);
                case short v: return Variant.From(v);
                case ushort v: return Variant.From(v);
                case int v: return Variant.From(v);
                case uint v: return Variant.From(v);
                case long v: return Variant.From(v);
                case ulong v: return Variant.From(v);
                case float v: return Variant.From(v);
                case double v: return Variant.From(v);
                case string v: return Variant.From(v);
                case DateTime v: return Variant.From(new DateTimeUtc(v));
                case Guid v: return Variant.From(new Uuid(v));
                case ByteString v: return Variant.From(v);
                case byte[] v: return Variant.From(new ArrayOf<byte>(v));
                case XmlElement v: return Variant.From(v);
                case NodeId v: return Variant.From(v);
                case ExpandedNodeId v: return Variant.From(v);
                case StatusCode v: return Variant.From(v);
                case QualifiedName v: return Variant.From(v);
                case LocalizedText v: return Variant.From(v);
                case ExtensionObject v: return Variant.From(v);
                case bool[] v: return Variant.From(new ArrayOf<bool>(v));
                case sbyte[] v: return Variant.From(new ArrayOf<sbyte>(v));
                case short[] v: return Variant.From(new ArrayOf<short>(v));
                case ushort[] v: return Variant.From(new ArrayOf<ushort>(v));
                case int[] v: return Variant.From(new ArrayOf<int>(v));
                case uint[] v: return Variant.From(new ArrayOf<uint>(v));
                case long[] v: return Variant.From(new ArrayOf<long>(v));
                case ulong[] v: return Variant.From(new ArrayOf<ulong>(v));
                case float[] v: return Variant.From(new ArrayOf<float>(v));
                case double[] v: return Variant.From(new ArrayOf<double>(v));
                case string[] v: return Variant.From(new ArrayOf<string>(v));
                case DateTime[] v: return Variant.From(new ArrayOf<DateTimeUtc>(Array.ConvertAll(v, x => new DateTimeUtc(x))));
                case Guid[] v: return Variant.From(new ArrayOf<Uuid>(Array.ConvertAll(v, x => new Uuid(x))));
                case ByteString[] v: return Variant.From(new ArrayOf<ByteString>(v));
                case XmlElement[] v: return Variant.From(new ArrayOf<XmlElement>(v));
                case NodeId[] v: return Variant.From(new ArrayOf<NodeId>(v));
                case ExpandedNodeId[] v: return Variant.From(new ArrayOf<ExpandedNodeId>(v));
                case StatusCode[] v: return Variant.From(new ArrayOf<StatusCode>(v));
                case QualifiedName[] v: return Variant.From(new ArrayOf<QualifiedName>(v));
                case LocalizedText[] v: return Variant.From(new ArrayOf<LocalizedText>(v));
                case ExtensionObject[] v: return Variant.From(new ArrayOf<ExtensionObject>(v));
                case IEncodeable v: return Variant.From(new ExtensionObject(v, false));
                case Variant[] v: return Variant.From(new ArrayOf<Variant>(v));
                default: return Variant.Null;
            }
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            uint id = Utils.IncrementIdentifier(ref m_lastUsedId);
            return new NodeId(id, NamespaceIndexes[1]);
        }
        #endregion

        #region Configure
        /// <summary>
        /// Wires the behaviour of the sample once the predefined nodes are in place.
        /// </summary>
        partial void Configure(INodeManagerBuilder builder)
        {
            ushort typeNamespaceIndex = NamespaceIndexes[0];

            // link all conditions to the conditions folder.
            NodeState conditionsFolder = FindPredefinedNode<NodeState>(
                new NodeId(Objects.Data_Conditions, typeNamespaceIndex));

            foreach (NodeState node in PredefinedNodes.Values)
            {
                if (node is ConditionState condition && !Object.ReferenceEquals(condition.Parent, conditionsFolder))
                {
                    condition.AddNotifier(SystemContext, NodeId.Null, true, conditionsFolder);
                    conditionsFolder.AddNotifier(SystemContext, NodeId.Null, false, condition);
                }
            }

            // enable history for the simulated Int32 value. the user access level
            // carries the history bit too: the history services check it per
            // session, and without it every read of the archive is refused.
            ScalarValueObjectState scalarValues = FindPredefinedNode<ScalarValueObjectState>(
                new NodeId(Objects.Data_Dynamic_Scalar, typeNamespaceIndex));

            scalarValues.Int32Value.Historizing = true;
            scalarValues.Int32Value.AccessLevel = (byte)(scalarValues.Int32Value.AccessLevel | AccessLevels.HistoryRead);
            scalarValues.Int32Value.UserAccessLevel = (byte)(scalarValues.Int32Value.UserAccessLevel | AccessLevels.HistoryRead);

            m_system.EnableHistoryArchiving(scalarValues.Int32Value);

            // serve the archive through the SDK's native historian: the dispatcher
            // routes every HistoryRead through the provider and owns continuation
            // points, timestamps to return, index ranges and data encodings. The
            // registration also keeps the advertisement reconcile from stripping
            // the history access bits set above.
            Server.UseHistorian()
                .UseProvider(new TestDataHistorianProvider(m_system, Server))
                .RegisterForNamespace(Namespaces.TestData);
        }
        #endregion

        #region Monitored Item Handling
        /// <summary>
        /// Returns true if the system must be scanning to provide updates for the monitored item.
        /// </summary>
        private static bool SystemScanRequired(NodeHandle handle, ISampledDataChangeMonitoredItem monitoredItem)
        {
            // ignore other types of monitored items.
            if (monitoredItem == null)
            {
                return false;
            }

            // only care about variables.
            if (handle?.Node is not BaseDataVariableState source)
            {
                return false;
            }

            // check for variables that need to be scanned.
            if (monitoredItem.AttributeId == Attributes.Value)
            {
                if (source.Parent is TestDataObjectState test && test.SimulationActive.Value)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Called after a batch of monitored items was created.
        /// </summary>
        /// <remarks>
        /// The generated partial already overrides the per-item
        /// <c>OnMonitoredItemCreated</c> callback to feed the fluent dispatcher,
        /// so the sample observes creations through the batch callback instead.
        /// The list only carries the items this node manager created.
        /// </remarks>
        protected override void OnCreateMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            base.OnCreateMonitoredItemsComplete(context, monitoredItems);

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                var monitoredItem = monitoredItems[ii] as ISampledDataChangeMonitoredItem;
                var handle = monitoredItems[ii].ManagerHandle as NodeHandle;

                if (SystemScanRequired(handle, monitoredItem))
                {
                    if (monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                    {
                        m_system.StartMonitoringValue(
                            monitoredItem.Id,
                            monitoredItem.SamplingInterval,
                            handle.Node as BaseVariableState);
                    }
                }
            }
        }

        /// <summary>
        /// Called after modifying a MonitoredItem.
        /// </summary>
        protected override ValueTask OnMonitoredItemModifiedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            if (SystemScanRequired(handle, monitoredItem))
            {
                if (monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                {
                    BaseVariableState source = handle.Node as BaseVariableState;
                    m_system.StopMonitoringValue(monitoredItem.Id);
                    m_system.StartMonitoringValue(monitoredItem.Id, monitoredItem.SamplingInterval, source);
                }
            }

            return base.OnMonitoredItemModifiedAsync(context, handle, monitoredItem, cancellationToken);
        }

        /// <summary>
        /// Called after deleting a MonitoredItem.
        /// </summary>
        protected override ValueTask OnMonitoredItemDeletedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            // check for variables that need to be scanned.
            if (SystemScanRequired(handle, monitoredItem))
            {
                m_system.StopMonitoringValue(monitoredItem.Id);
            }

            return base.OnMonitoredItemDeletedAsync(context, handle, monitoredItem, cancellationToken);
        }

        /// <summary>
        /// Called after changing the MonitoringMode for a MonitoredItem.
        /// </summary>
        protected override ValueTask OnMonitoringModeChangedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode monitoringMode,
            CancellationToken cancellationToken = default)
        {
            if (SystemScanRequired(handle, monitoredItem))
            {
                BaseVariableState source = handle.Node as BaseVariableState;

                if (previousMode != MonitoringMode.Disabled && monitoredItem.MonitoringMode == MonitoringMode.Disabled)
                {
                    m_system.StopMonitoringValue(monitoredItem.Id);
                }

                if (previousMode == MonitoringMode.Disabled && monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                {
                    m_system.StartMonitoringValue(monitoredItem.Id, monitoredItem.SamplingInterval, source);
                }
            }

            return base.OnMonitoringModeChangedAsync(context, handle, monitoredItem, previousMode, monitoringMode, cancellationToken);
        }
        #endregion

        #region Private Fields
        private TestDataNodeManagerConfiguration m_configuration;
        private TestDataSystem m_system;
        private uint m_lastUsedId;
        #endregion
    }
}
