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

using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Quickstarts.Views.Server
{
    /// <summary>
    /// A node manager for a server that exposes the same address space through
    /// an engineering and an operations view.
    /// </summary>
    /// <remarks>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to source
    /// generation: the generator emits a sibling partial which derives from
    /// <c>AsyncCustomNodeManager</c>, loads the predefined nodes generated from
    /// <c>ModelDesign.xml</c> - the plant folder, the controller and boiler types
    /// and the two views come out of it - and calls <see cref="Configure"/> once
    /// the address space is in place. It also emits the
    /// <c>ViewsNodeManagerFactory</c> the server registers to create this node
    /// manager. The project carries two more designs (<c>EngineeringDesign.xml</c>
    /// and <c>OperationsDesign.xml</c>) which only contribute the browse names the
    /// view filtering keys on, so the attribute selects the Views design by its
    /// namespace URI - spelled out because the generated Namespaces constants are
    /// produced by the same generator that reads this attribute.
    /// </remarks>
    [NodeManager(NamespaceUri = "http://opcfoundation.org/UA/Quickstarts/Views")]
    public partial class ViewsNodeManager
    {
        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <remarks>
        /// The boilers the sample creates from the type model get parsed node ids:
        /// a root instance's id is built from its own name, and every child derives
        /// its id from its parent's, so the whole subtree carries stable, readable
        /// string ids. The predefined nodes of the model never pass through here -
        /// they keep the ids the model assigns.
        /// </remarks>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            BaseInstanceState instance = node as BaseInstanceState;

            if (instance != null)
            {
                if (instance.Parent == null)
                {
                    return new ParsedNodeId() { NamespaceIndex = NamespaceIndex, RootId = instance.SymbolicName }.Construct();
                }

                ParsedNodeId pnd = ParsedNodeId.Parse(instance.Parent.NodeId);

                if (pnd != null)
                {
                    return pnd.Construct(instance.SymbolicName);
                }
            }

            return node.NodeId;
        }
        #endregion

        #region Configure
        /// <summary>
        /// Builds the dynamic part of the address space once the predefined nodes
        /// are in place.
        /// </summary>
        partial void Configure(INodeManagerBuilder builder)
        {
            // the model gives the controller properties browse names in the engineering
            // and operations namespaces; loading it appended those namespaces to the
            // server namespace table. the view filtering below tells the disciplines
            // apart by those browse name namespaces, so resolve their indexes once.
            m_engineeringNamespaceIndex =
                (ushort)Server.NamespaceUris.GetIndex(Quickstarts.Views.Namespaces.Engineering);
            m_operationsNamespaceIndex =
                (ushort)Server.NamespaceUris.GetIndex(Quickstarts.Views.Namespaces.Operations);

            // create the two boilers below the plant folder the model declares.
            NodeState root = FindPredefinedNode<NodeState>(
                new NodeId(Quickstarts.Views.Objects.Plant, NamespaceIndex));

            CreateBoiler(root, "Boiler #1");
            CreateBoiler(root, "Boiler #2");
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Checks if the node is in the view.
        /// </summary>
        protected override bool IsNodeInView(ServerSystemContext context, ContinuationPoint continuationPoint, NodeState node)
        {
            if (continuationPoint.View != null)
            {
                if (continuationPoint.View.ViewId == new NodeId(Quickstarts.Views.Views.Engineering, NamespaceIndex))
                {
                    // suppress operations properties.
                    if (node != null && node.BrowseName.NamespaceIndex == m_operationsNamespaceIndex)
                    {
                        return false;
                    }
                }

                if (continuationPoint.View.ViewId == new NodeId(Quickstarts.Views.Views.Operations, NamespaceIndex))
                {
                    // suppress engineering properties.
                    if (node != null && node.BrowseName.NamespaceIndex == m_engineeringNamespaceIndex)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if the reference is in the view.
        /// </summary>
        protected override bool IsReferenceInView(ServerSystemContext context, ContinuationPoint continuationPoint, IReference reference)
        {
            if (continuationPoint.View != null)
            {
                // guard against absolute node ids.
                if (reference.TargetId.IsAbsolute)
                {
                    return true;
                }

                // find the node.
                NodeState node = FindPredefinedNode<NodeState>((NodeId)reference.TargetId);

                if (node != null)
                {
                    return IsNodeInView(context, continuationPoint, node);
                }
            }

            return true;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates a boiler below the plant folder.
        /// </summary>
        /// <remarks>
        /// The generated factory instantiates the subtree the type model declares
        /// and lets the <see cref="New"/> override above assign the parsed node ids.
        /// </remarks>
        private void CreateBoiler(NodeState root, string name)
        {
            Quickstarts.Views.BoilerState boiler = SystemContext.CreateInstanceOfBoilerType(
                browseName: new QualifiedName(name, NamespaceIndex));

            boiler.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, true, root.NodeId);
            root.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, false, boiler.NodeId);

            AddPredefinedNodeSynchronously(boiler);
        }
        #endregion

        #region Private Fields
        private ushort m_engineeringNamespaceIndex;
        private ushort m_operationsNamespaceIndex;
        #endregion
    }
}
