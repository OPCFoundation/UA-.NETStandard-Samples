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

// the generated partial of the node manager loads the instance model through the
// AddQuickstartsDataTypesInstances extension, which the generator emits into the
// namespace of the model design (Quickstarts.DataTypes.Instances). It calls the
// extension unqualified, and that only resolves when the namespace of the model
// encloses the one of the node manager - here it is the other way round, so the
// namespace is imported for the whole compilation.
global using Quickstarts.DataTypes.Instances;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server.Fluent;
using Quickstarts.DataTypes.Types;

namespace Quickstarts.DataTypes
{
    /// <summary>
    /// A node manager for a server that exposes custom data types.
    /// </summary>
    /// <remarks>
    /// The sample serves two models from one node manager: the vehicle type
    /// model compiled into the shared DataTypes Library, and the parking lot
    /// instances compiled into this assembly. Both are built by the OPC UA
    /// source generator from their model designs; the instance model subtypes
    /// the vehicle structures and uses the driver type across the project
    /// boundary, and the generated code refers to the types of the library.
    /// <para>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to source
    /// generation: the generator emits a sibling partial which loads the
    /// predefined nodes generated from <c>ModelDesign2.xml</c> and calls
    /// <c>Configure</c> once the address space is in place, and it
    /// emits the <c>DataTypesNodeManagerFactory</c> the server registers to
    /// create this node manager. The attribute selects the instance model by
    /// its namespace URI - spelled out because the generated Namespaces
    /// constants are produced by the same generator that reads this
    /// attribute - and names the two further namespaces the node manager
    /// serves: the type model of the library, and the namespace of the
    /// server itself. Loading both models into the same manager is what
    /// links the instances to their types.
    /// </para>
    /// </remarks>
    [NodeManager(
        NamespaceUri = "http://opcfoundation.org/UA/Quickstarts/DataTypes/Instances",
        AdditionalNamespaceUris = new[] {
            Quickstarts.DataTypes.Types.Namespaces.DataTypes,
            Quickstarts.DataTypes.Namespaces.DataTypes
        })]
    public partial class DataTypesNodeManager
    {
        #region Overridden Methods
        /// <summary>
        /// Registers the encodeable types of both models and adds the nodes of
        /// the vehicle type model before the instance model generated into this
        /// assembly is loaded.
        /// </summary>
        /// <remarks>
        /// The generated partial only loads the model it was generated from,
        /// so the nodes of the type model the instances depend on are added
        /// here, in dependency order, from the extension the generator emitted
        /// into the library. The base call then loads the instance model and
        /// links both to the rest of the address space. The sample has nothing
        /// to wire in <c>Configure</c>: the values of the parking lot are what
        /// the model declares, and reading and writing them is what the base
        /// node manager does on its own.
        /// </remarks>
        protected override async ValueTask LoadPredefinedNodesAsync(
            ISystemContext context,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            // register the encodeable types of both models so the values of the
            // custom structures can be decoded and encoded by the server. both
            // models are source generated from their model designs and come with
            // a registration extension of their own. the generated node sets keep
            // the default values of the model as encoded XML which is decoded when
            // the nodes are created, so the types have to be known before that.
            Server.Factory.Builder
                .AddQuickstartsDataTypesTypes()
                .AddQuickstartsDataTypesInstances()
                .Commit();

            NodeStateCollection typeNodes = new NodeStateCollection()
                .AddQuickstartsDataTypesTypes(context);

            foreach (NodeState node in typeNodes)
            {
                await AddPredefinedNodeAsync(context, node, cancellationToken).ConfigureAwait(false);
            }

            await base.LoadPredefinedNodesAsync(context, externalReferences, cancellationToken).ConfigureAwait(false);
        }
        #endregion
    }
}
