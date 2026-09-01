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

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;
using Quickstarts.DataTypes.Types;

namespace Quickstarts.DataTypes
{
    /// <summary>
    /// The factory the server registers to create the node manager.
    /// </summary>
    public class DataTypesNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: ownership of the node manager transfers to the caller.
            return new ValueTask<IAsyncNodeManager>(
                new DataTypesNodeManager(server, configuration));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
        [
            Quickstarts.DataTypes.Namespaces.DataTypes,
            Quickstarts.DataTypes.Types.Namespaces.DataTypes,
            Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances
        ];
    }

    /// <summary>
    /// A node manager for a server that exposes custom data types.
    /// </summary>
    /// <remarks>
    /// The sample serves two models from one node manager: the vehicle type
    /// model compiled into the shared DataTypes Library, and the parking lot
    /// instances compiled into this assembly. Loading both node sets into the
    /// same manager is what links the instances to their types.
    /// </remarks>
    public class DataTypesNodeManager : AsyncCustomNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public DataTypesNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration,
                Quickstarts.DataTypes.Namespaces.DataTypes,
                Quickstarts.DataTypes.Types.Namespaces.DataTypes,
                Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances)
        {
            // register the encodeable types of both models so the values of the
            // custom structures can be decoded and encoded by the server. the
            // vehicle types of the library are source generated from its model
            // design and come with a registration extension of their own; the
            // instance model is still built by the ModelCompiler and its types
            // are picked up by reflection.
            Server.Factory.Builder.AddQuickstartsDataTypesTypes().Commit();
            Server.Factory.AddEncodeableTypes(typeof(DataTypesNodeManager).GetTypeInfo().Assembly);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Loads the node sets of both models from their embedded resources and
        /// adds them to the set of predefined nodes.
        /// </summary>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();

            predefinedNodes.LoadFromBinaryResource(context,
                "Quickstarts.DataTypes.Types.Quickstarts.DataTypes.Types.PredefinedNodes.uanodes",
                typeof(Quickstarts.DataTypes.Types.VehicleType).Assembly,
                true);
            predefinedNodes.LoadFromBinaryResource(context,
                "Quickstarts.DataTypes.Instances.Quickstarts.DataTypes.Instances.PredefinedNodes.uanodes",
                typeof(DataTypesNodeManager).GetTypeInfo().Assembly,
                true);

            return new ValueTask<NodeStateCollection>(predefinedNodes);
        }
        #endregion
    }
}
