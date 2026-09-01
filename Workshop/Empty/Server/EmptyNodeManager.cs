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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.EmptyServer
{
    /// <summary>
    /// The factory the server registers to create the node manager.
    /// </summary>
    public class EmptyNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: ownership of the node manager transfers to the caller.
            return new ValueTask<IAsyncNodeManager>(
                new EmptyNodeManager(server, configuration));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [Namespaces.Empty];
    }

    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class EmptyNodeManager : AsyncCustomNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public EmptyNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Namespaces.Empty)
        {
        }
        #endregion

        #region IAsyncNodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.
        /// </remarks>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

#pragma warning disable CA2000 // Justification: Node ownership is transferred to the server address space.
            BaseObjectState trigger = new BaseObjectState(null);
#pragma warning restore CA2000

            trigger.NodeId = new NodeId(1, NamespaceIndex);
            trigger.BrowseName = new QualifiedName("Trigger", NamespaceIndex);
            trigger.DisplayName = new LocalizedText(trigger.BrowseName.Name);
            trigger.TypeDefinitionId = ObjectTypeIds.BaseObjectType;

            // ensure trigger can be found via the server object.
            IList<IReference> references = null;

            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }

            trigger.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, trigger.NodeId));

#pragma warning disable CA2000 // Justification: Node ownership is transferred to the server address space.
            PropertyState property = new PropertyState(trigger);
#pragma warning restore CA2000

            property.NodeId = new NodeId(2, NamespaceIndex);
            property.BrowseName = new QualifiedName("Matrix", NamespaceIndex);
            property.DisplayName = new LocalizedText(property.BrowseName.Name);
            property.TypeDefinitionId = VariableTypeIds.PropertyType;
            property.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            property.DataType = DataTypeIds.Int32;
            property.ValueRank = ValueRanks.TwoDimensions;
            property.ArrayDimensions = new uint[] { 2, 2 }.ToArrayOf();

            trigger.AddChild(property);

            // save in dictionary.
            await AddPredefinedNodeAsync(SystemContext, trigger, cancellationToken).ConfigureAwait(false);

#pragma warning disable CA2000 // Justification: Node ownership is transferred to the server address space.
            ReferenceTypeState referenceType = new ReferenceTypeState();
#pragma warning restore CA2000

            referenceType.NodeId = new NodeId(3, NamespaceIndex);
            referenceType.BrowseName = new QualifiedName("IsTriggerSource", NamespaceIndex);
            referenceType.DisplayName = new LocalizedText(referenceType.BrowseName.Name);
            referenceType.InverseName = new LocalizedText("IsSourceOfTrigger");
            referenceType.SuperTypeId = ReferenceTypeIds.NonHierarchicalReferences;

            if (!externalReferences.TryGetValue(ObjectIds.Server, out references))
            {
                externalReferences[ObjectIds.Server] = references = new List<IReference>();
            }

            trigger.AddReference(referenceType.NodeId, false, ObjectIds.Server);
            references.Add(new NodeStateReference(referenceType.NodeId, true, trigger.NodeId));

            // save in dictionary.
            await AddPredefinedNodeAsync(SystemContext, referenceType, cancellationToken).ConfigureAwait(false);
        }
        #endregion
    }
}
