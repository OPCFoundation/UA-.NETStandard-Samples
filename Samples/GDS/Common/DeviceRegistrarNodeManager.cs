/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Exposes an OPC 10000-21 (Onboarding) <c>DeviceRegistrarAdminType</c> instance on the
    /// GDS and routes its <c>RegisterTickets</c> / <c>UnregisterTickets</c> Methods through
    /// an <see cref="ITicketStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Part 21 onboarding lets a manufacturer hand a customer a set of *tickets* - signed
    /// blobs that identify the devices that are allowed to provision themselves. The
    /// customer loads the tickets into the registrar; a device presenting a matching
    /// identity later is accepted without an administrator having to register it by hand.
    /// The registrar administration surface - loading and removing those tickets - is the
    /// half the SDK implements, through
    /// <see cref="DeviceRegistrarAdminExtensions.BindToTicketStore"/> plus a ticket store
    /// (<see cref="MemoryTicketStore"/> here), and it is the half this node manager exposes.
    /// The device-facing <c>ProvideIdentities</c> flow is not part of the sample.
    /// </para>
    /// <para>
    /// The nodes are built by hand rather than generated from the companion model: the
    /// Onboarding model ships as a design file in the SDK repository but its generated node
    /// classes are not part of the <c>Opc.Ua.Gds.Common</c> NuGet package, so there is no
    /// <c>DeviceRegistrarAdminState</c> to instantiate. The Object therefore carries
    /// <c>BaseObjectType</c> as its TypeDefinition instead of <c>DeviceRegistrarAdminType</c>,
    /// and lives in this sample's own namespace. Everything a client calls - the two Method
    /// nodes, their BrowseNames and their <c>Tickets</c> argument - matches Part 21.
    /// </para>
    /// </remarks>
    public class DeviceRegistrarNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Namespace of the registrar nodes this sample creates. It is deliberately a
        /// sample namespace and not <c>http://opcfoundation.org/UA/Onboarding/</c>, because
        /// these are hand-built stand-ins rather than the companion model's nodes.
        /// </summary>
        public const string NamespaceUri =
            "http://opcfoundation.org/UA-.NETStandard-Samples/GDS/Onboarding/";

        /// <summary>
        /// BrowseName of the registrar administration Object, the node a client passes to
        /// <c>OnboardingClient</c>.
        /// </summary>
        public const string RegistrarBrowseName = "DeviceRegistrarAdmin";

        private readonly ITicketStore m_ticketStore;

        /// <summary>
        /// Creates the node manager over the supplied ticket store. The Onboarding
        /// companion model namespace is the manager's second namespace, because the two
        /// Method nodes carry the model's type-declaration NodeIds - see
        /// <see cref="CreateTicketMethod"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="ticketStore"/> is <c>null</c>.</exception>
        public DeviceRegistrarNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ITicketStore ticketStore)
            : base(server, configuration, NamespaceUri, Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding)
        {
            m_ticketStore = ticketStore ?? throw new ArgumentNullException(nameof(ticketStore));
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            #pragma warning disable CA2000 // Justification: Node ownership is transferred to the server address space.
            var registrar = new BaseObjectState(null) {
                NodeId = new NodeId(RegistrarBrowseName, NamespaceIndex),
                BrowseName = new QualifiedName(RegistrarBrowseName, NamespaceIndex),
                DisplayName = new LocalizedText(RegistrarBrowseName),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            #pragma warning restore CA2000

            MethodState register = CreateTicketMethod(
                registrar,
                "RegisterTickets",
                Opc.Ua.Onboarding.MethodIds.DeviceRegistrarAdminType_RegisterTickets);
            MethodState unregister = CreateTicketMethod(
                registrar,
                "UnregisterTickets",
                Opc.Ua.Onboarding.MethodIds.DeviceRegistrarAdminType_UnregisterTickets);

            registrar.AddChild(register);
            registrar.AddChild(unregister);

            // one call wires both Methods to the store: the SDK matches the two children by
            // BrowseName and installs its own handlers.
            registrar.BindToTicketStore(m_ticketStore);

            ResetOutputArgumentsBeforeCall(register);
            ResetOutputArgumentsBeforeCall(unregister);

            if (!externalReferences.TryGetValue(Opc.Ua.ObjectIds.ObjectsFolder, out IList<IReference> references))
            {
                externalReferences[Opc.Ua.ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }

            registrar.AddReference(ReferenceTypeIds.Organizes, true, Opc.Ua.ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, registrar.NodeId));

            await AddPredefinedNodeAsync(SystemContext, registrar, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds one of the two ticket Methods, together with its argument Properties.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three details are dictated by the SDK rather than by Part 21, and they let both
        /// client generations call the Methods. The NodeId is the type-declaration MethodId
        /// of the Onboarding companion model, because the current generated
        /// <c>OnboardingClient</c> calls that id on the wrapped instance directly - which
        /// OPC UA Part 4 permits - and this hand-built registrar has no instantiated type to
        /// answer for it otherwise. The BrowseName stays in namespace 0, because the earlier
        /// <c>OnboardingClient</c> generation resolves the Methods with an ns=0 browse path.
        /// And the <c>Tickets</c>
        /// argument is declared as a <c>ByteString</c> array rather than as the Part 21
        /// <c>EncodedTicket</c> alias: <c>MethodState</c> type-checks every input argument
        /// against the declared DataType, and ns=0 models <c>EncodedTicket</c> with
        /// <c>String</c> as its base type, so the ByteString array both halves of the SDK
        /// actually exchange would be rejected with <c>Bad_TypeMismatch</c>.
        /// </para>
        /// </remarks>
        private MethodState CreateTicketMethod(
            NodeState parent,
            string name,
            ExpandedNodeId typeMethodId)
        {
            #pragma warning disable CA2000 // Justification: Node ownership is transferred to the server address space.
            var method = new MethodState(parent) {
                NodeId = ExpandedNodeId.ToNodeId(typeMethodId, SystemContext.NamespaceUris),
                BrowseName = new QualifiedName(name),
                DisplayName = new LocalizedText(name),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                Executable = true,
                UserExecutable = true
            };
            #pragma warning restore CA2000

            PropertyState<ArrayOf<Argument>> inputArguments =
                method.CreateOrReplaceInputArguments(SystemContext, null, assignInstanceNodeIds: false);
            inputArguments.NodeId = new NodeId(name + "_InputArguments", NamespaceIndex);
            inputArguments.Value = new[] {
                new Argument {
                    Name = "Tickets",
                    DataType = Opc.Ua.DataTypeIds.ByteString,
                    ValueRank = ValueRanks.OneDimension,
                    Description = new LocalizedText("The encoded onboarding tickets.")
                }
            }.ToArrayOf();

            PropertyState<ArrayOf<Argument>> outputArguments =
                method.CreateOrReplaceOutputArguments(SystemContext, null, assignInstanceNodeIds: false);
            outputArguments.NodeId = new NodeId(name + "_OutputArguments", NamespaceIndex);
            outputArguments.Value = new[] {
                new Argument {
                    Name = "Results",
                    DataType = Opc.Ua.DataTypeIds.StatusCode,
                    ValueRank = ValueRanks.OneDimension,
                    Description = new LocalizedText("The result for each supplied ticket.")
                }
            }.ToArrayOf();

            return method;
        }

        /// <summary>
        /// Drops the default output values <c>MethodState</c> seeds before the call.
        /// </summary>
        /// <remarks>
        /// <c>MethodState</c> pre-fills the output list with one default value per declared
        /// output Argument and then hands that list to the handler, but the SDK's
        /// <c>BindToTicketStore</c> handler <em>appends</em> its result instead of assigning
        /// it into the seeded slot. Left alone, the call would return two values and a client
        /// reading <c>OutputArguments[0]</c> - which is what <c>OnboardingClient</c> does -
        /// would get the default rather than the per-ticket results. Clearing the list first
        /// leaves exactly the handler's own output.
        /// </remarks>
        private static void ResetOutputArgumentsBeforeCall(MethodState method)
        {
            GenericMethodCalledEventHandler2 inner = method.OnCallMethod2;

            if (inner == null)
            {
                return;
            }

            method.OnCallMethod2 = (context, m, objectId, inputArguments, outputArguments) => {
                outputArguments.Clear();
                return inner(context, m, objectId, inputArguments, outputArguments);
            };
        }
    }
}
