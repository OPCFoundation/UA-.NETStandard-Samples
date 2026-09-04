/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.NodeManagement.Server
{
    /// <summary>
    /// A Quickstart server whose address space is built by its clients, over the OPC UA
    /// NodeManagement service set (OPC 10000-4 5.8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is nothing to see in this class, and that is the first thing the sample has to
    /// say. AddNodes, DeleteNodes, AddReferences and DeleteReferences are implemented by
    /// <see cref="StandardServer"/> and dispatched per item by the master node manager; a
    /// server does not override them, and neither does a node manager. The whole of the
    /// server side opt-in is one property on
    /// <see cref="NodeManagementNodeManager.AllowNodeManagement"/>.
    /// </para>
    /// <para>
    /// What the server does contribute is its configuration: the operation limit
    /// MaxNodesPerNodeManagement, which bounds how many items one request may carry, and
    /// AuditingEnabled, which turns the audit events the four services raise into events a
    /// client can subscribe to.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Sample server type name intentionally mirrors the namespace.")]
    public partial class NodeManagementServer : StandardServer
    {
        /// <summary>
        /// Creates the server. Its node manager is registered with the host by the hosting composition root of the sample.
        /// </summary>
        public NodeManagementServer(ITelemetryContext telemetry) : base(telemetry)
        {
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        protected override ServerProperties LoadServerProperties()
        {
            var properties = new ServerProperties {
                ManufacturerName = "OPC Foundation",
                ProductName = "Quickstart NodeManagement Server",
                ProductUri = "http://opcfoundation.org/Quickstart/NodeManagementServer/v1.0",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp(),
            };

            return properties;
        }
    }
}
