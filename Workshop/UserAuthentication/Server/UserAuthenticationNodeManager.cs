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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.UserAuthenticationServer
{
    /// <summary>
    /// The factory the server registers to create the node manager.
    /// </summary>
    public class UserAuthenticationNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Justification: ownership of the node manager transfers to the caller.
            return new ValueTask<IAsyncNodeManager>(
                new UserAuthenticationNodeManager(server, configuration));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [Namespaces.UserAuthentication];
    }

    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class UserAuthenticationNodeManager : AsyncCustomNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public UserAuthenticationNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Namespaces.UserAuthentication)
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

            // create a object to represent the process being controlled.
#pragma warning disable CA2000 // Justification: Node ownership is transferred to the server address space.
            BaseObjectState process = new BaseObjectState(null);
#pragma warning restore CA2000

            process.NodeId = new NodeId(1, NamespaceIndex);
            process.BrowseName = new QualifiedName("My Process", NamespaceIndex);
            process.DisplayName = new LocalizedText(process.BrowseName.Name);
            process.TypeDefinitionId = ObjectTypeIds.BaseObjectType;

            // ensure the process object can be found via the server object.
            IList<IReference> references = null;

            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }

            process.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, process.NodeId));

            // a property to report the process state.
#pragma warning disable CA2000 // Justification: Node ownership is transferred to the server address space.
            PropertyState<string> state = PropertyState<string>.With<VariantBuilder>(process);
#pragma warning restore CA2000

            state.NodeId = new NodeId(2, NamespaceIndex);
            state.BrowseName = new QualifiedName("LogFilePath", NamespaceIndex);
            state.DisplayName = new LocalizedText(state.BrowseName.Name);
            state.TypeDefinitionId = VariableTypeIds.PropertyType;
            state.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            state.DataType = DataTypeIds.String;
            state.ValueRank = ValueRanks.Scalar;
            state.AccessLevel = AccessLevels.CurrentReadOrWrite;
            state.UserAccessLevel = AccessLevels.CurrentRead;
            state.Value = ".\\Log.txt";

            process.AddChild(state);

            // the user access level is answered per session, the write callback
            // enforces the same identity check again before it touches the file system.
            state.OnReadUserAccessLevel = OnReadUserAccessLevel;
            state.OnSimpleWriteValueAsync = OnWriteValueAsync;

            // save in dictionary.
            await AddPredefinedNodeAsync(SystemContext, process, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<AttributeWriteResult> OnWriteValueAsync(
            ISystemContext context,
            NodeState node,
            Variant value,
            CancellationToken cancellationToken)
        {
            UserTokenType? tokenType = (context as ISessionSystemContext)?.UserIdentity?.TokenType;

            if (tokenType is null or UserTokenType.Anonymous)
            {
                TranslationInfo info = new TranslationInfo(
                    "BadUserAccessDenied",
                    "en-US",
                    "User cannot change value.");

                return new AttributeWriteResult(
                    new ServiceResult(StatusCodes.BadUserAccessDenied, new LocalizedText(info)));
            }

            // attempt to update file system. on success the framework stores the
            // written value in the variable, mirroring the synchronous write path.
            try
            {
                value.TryGetValue(out string filePath);
                PropertyState<string> variable = node as PropertyState<string>;

                if (!String.IsNullOrEmpty(variable.Value))
                {
                    FileInfo file = new FileInfo(variable.Value);

                    if (file.Exists)
                    {
                        file.Delete();
                    }
                }

                if (!String.IsNullOrEmpty(filePath))
                {
                    FileInfo file = new FileInfo(filePath);

                    StreamWriter writer = file.CreateText();

                    await using (writer.ConfigureAwait(false))
                    {
                        await writer.WriteLineAsync(
                            System.Security.Principal.WindowsIdentity.GetCurrent().Name.AsMemory(),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                return new AttributeWriteResult(
                    ServiceResult.Create(e, StatusCodes.BadUserAccessDenied, "Could not update file system."));
            }

            return new AttributeWriteResult(ServiceResult.Good);
        }

        public ServiceResult OnReadUserAccessLevel(ISystemContext context, NodeState node, ref byte value)
        {
            UserTokenType? tokenType = (context as ISessionSystemContext)?.UserIdentity?.TokenType;

            if (tokenType is null or UserTokenType.Anonymous)
            {
                value = AccessLevels.CurrentRead;
            }
            else
            {
                value = AccessLevels.CurrentReadOrWrite;
            }

            return ServiceResult.Good;
        }
        #endregion
    }
}
