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

namespace Quickstarts.FileTransferServer
{
    /// <summary>
    /// A Quickstart server which publishes a directory of the host as an OPC UA file system.
    /// </summary>
    /// <remarks>
    /// The sample writes no node manager of its own: the
    /// <see cref="FileTransferNodeManagerFactory"/> the host registers creates the file
    /// system node manager the SDK ships for the directory the configuration names. The
    /// server class itself only names the product.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Sample server type name intentionally mirrors the namespace.")]
    public partial class FileTransferServer : StandardServer
    {
        public FileTransferServer(ITelemetryContext telemetry) : base(telemetry)
        {
        }

        #region Overridden Methods
        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            var properties = new ServerProperties {
                ManufacturerName = "OPC Foundation",
                ProductName = "Quickstart File Transfer Server",
                ProductUri = "http://opcfoundation.org/Quickstart/FileTransferServer/v1.0",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp(),
            };

            return properties;
        }
        #endregion
    }
}
