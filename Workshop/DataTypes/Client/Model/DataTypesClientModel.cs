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
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Samples.Client;
using Opc.Ua.Schema;
using Quickstarts.DataTypes.Types;
using DataTypeIds = Opc.Ua.DataTypeIds;

namespace Quickstarts.DataTypes.Model
{
    /// <summary>
    /// One data type of the server the client can produce a schema for.
    /// </summary>
    /// <param name="NodeId">The data type node.</param>
    /// <param name="Name">The name the schema gives the type.</param>
    /// <param name="Namespace">The namespace the type belongs to.</param>
    /// <param name="FromCompiledType">
    /// True when the definition came out of a class this client compiled, false when it
    /// came off the wire as the <c>DataTypeDefinition</c> Attribute of the node.
    /// </param>
    public sealed record SchemaDataType(
        NodeId NodeId,
        string Name,
        string Namespace,
        bool FromCompiledType)
    {
        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>
    /// The client model of the DataTypes client: loads the structured data types the
    /// server defines so that their values arrive decoded into their fields, and produces
    /// XSD, OPC Binary and JSON Schema documents for them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This sample exists to show a client making sense of structures it has no compiled
    /// knowledge of. The complex type system reads the data type dictionaries and the
    /// type definitions of the server when the session is attached and builds the types
    /// at run time; a value read after that is an object with named fields instead of the
    /// raw body of an extension object.
    /// </para>
    /// <para>
    /// The schemas are the other half of the same story. A <see cref="DataTypeDefinition"/>
    /// is all a schema is made from, and it reaches the client two ways: out of a class
    /// the client compiled from the shared model design, and off the wire as the
    /// <c>DataTypeDefinition</c> Attribute of a data type node. The
    /// <see cref="ISchemaProvider"/> does not care which - the same call produces the same
    /// document for a type the client has known since build time and for one it met a
    /// moment ago. On 2.0.0-preview.4 only the second route carries anything; see
    /// <see cref="s_compiledTypes"/>.
    /// </para>
    /// </remarks>
    public sealed class DataTypesClientModel : SampleClientModel
    {
        // The types this client compiled, from the shared DataTypes Library the source
        // generator built out of ModelDesign1.xml. A generated class which implements
        // IDataTypeDefinitionSource hands over the definition its model design declared,
        // and no browse is needed for it at all.
        //
        // On 2.0.0-preview.4 the generator does not emit that interface, so this list
        // registers nothing yet and every definition below comes off the wire instead
        // (UA-.NETStandard#4424). The branch is kept because the interface is the
        // supported way to reach a compiled definition, and because it is what makes the
        // difference visible: a type registered from here is marked as compiled in the
        // list the window shows.
        //
        // BicycleType is deliberately not here. It is declared in the model design of the
        // server alone, so the client can only ever meet it as a node.
        private static readonly IEncodeable[] s_compiledTypes = [
            new VehicleType(),
            new CarType(),
            new TruckType(),
        ];

        private readonly ServiceProvider m_schemaServices;
        private readonly ISchemaProvider m_schemas;
        private readonly DataTypeDefinitionRegistry m_registry;

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public DataTypesClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
            // the schema generators of the stack are internal and are reached through the
            // dependency injection registration alone. A client which is built around a
            // host container registers AddSchemaGeneration() there and takes an
            // ISchemaProvider in its constructor; the window of this sample creates its
            // model itself, so the model owns the registration.
            var services = new ServiceCollection();
            services.AddOpcUa().AddSchemaGeneration();

            m_schemaServices = services.BuildServiceProvider();
            m_schemas = m_schemaServices.GetRequiredService<ISchemaProvider>();
            m_registry = m_schemaServices.GetRequiredService<DataTypeDefinitionRegistry>();
        }

        /// <summary>
        /// True once the structured types of the server were loaded on the attached
        /// session.
        /// </summary>
        public bool TypeSystemLoaded { get; private set; }

        /// <summary>
        /// The data types a schema can be produced for, found when the session was
        /// attached.
        /// </summary>
        public IReadOnlyList<SchemaDataType> DataTypes { get; private set; } = Array.Empty<SchemaDataType>();

        /// <summary>
        /// Reads the value of a variable. A structure arrives decoded into its fields
        /// once the type system was loaded.
        /// </summary>
        /// <param name="nodeId">The variable.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<DataValue> ReadValueAsync(NodeId nodeId, CancellationToken ct = default)
        {
            return RequireSession().ReadValueAsync(nodeId, ct);
        }

        /// <summary>
        /// Produces the schema of a data type in one of the four encodings.
        /// </summary>
        /// <remarks>
        /// <see cref="UaSchemaScope.Type"/> produces a document for the one type and the
        /// closure of the types it depends on - the schema of <c>CarType</c> carries
        /// <c>VehicleType</c> and <c>EngineType</c> with it, because a document which
        /// referred to them without defining them would not validate anything.
        /// <see cref="UaSchemaScope.Namespace"/> produces the dictionary of every type of
        /// the namespace instead, which is what a server publishes as its type
        /// dictionary.
        /// </remarks>
        /// <param name="type">One of <see cref="DataTypes"/>.</param>
        /// <param name="format">XSD, OPC Binary, or JSON Schema in either flavour.</param>
        /// <param name="scope">The one type, or the whole namespace.</param>
        /// <exception cref="ServiceResultException">The type is not one the model
        /// registered.</exception>
        public string CreateSchema(SchemaDataType type, UaSchemaFormat format, UaSchemaScope scope)
        {
            ArgumentNullException.ThrowIfNull(type);

            if (!m_registry.TryResolve(type.NodeId, out UaTypeDescription description))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeDefinitionInvalid,
                    $"No definition was registered for {type.Name} ({type.NodeId}).");
            }

            return m_schemas.CreateSchema(description, format, scope).ToSchemaString();
        }

        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            // the type system is loaded for the whole server, including the types no
            // node references yet, so that a value of any structure the server defines
            // can be decoded.
            var typeSystem = ComplexTypeSystemClientExtensions.Create(session, Telemetry);
            await typeSystem.LoadAsync(true, true, ct).ConfigureAwait(false);

            TypeSystemLoaded = true;

            DataTypes = await RegisterDataTypesAsync(session, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override Task OnDetachingAsync()
        {
            // the types were built on the session, so they go with it.
            TypeSystemLoaded = false;
            DataTypes = Array.Empty<SchemaDataType>();

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            await base.DisposeAsyncCore().ConfigureAwait(false);
            await m_schemaServices.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Fills the registry the schema provider resolves from, out of both sources, and
        /// returns what a schema can be produced for.
        /// </summary>
        private async Task<IReadOnlyList<SchemaDataType>> RegisterDataTypesAsync(
            ISession session,
            CancellationToken ct)
        {
            var types = new List<SchemaDataType>();
            var compiled = new HashSet<NodeId>();

            // the types the client compiled. The generated class carries the definition
            // its model design declared, and hands it over already resolved against the
            // namespace table of this session.
            foreach (IEncodeable value in s_compiledTypes)
            {
                if (value is not IDataTypeDefinitionSource source)
                {
                    continue;
                }

                NodeId nodeId = ExpandedNodeId.ToNodeId(value.TypeId, session.NamespaceUris);

                if (nodeId.IsNull)
                {
                    continue;
                }

                string name = value.GetType().Name;
                string namespaceUri = session.NamespaceUris.GetString(nodeId.NamespaceIndex);

                m_registry.Add(new UaTypeDescription(
                    nodeId,
                    new QualifiedName(name, nodeId.NamespaceIndex),
                    source.GetDataTypeDefinition(session.NamespaceUris),
                    namespaceUri));

                compiled.Add(nodeId);
                types.Add(new SchemaDataType(nodeId, name, namespaceUri, true));
            }

            // and every data type the server declares below Structure and Enumeration.
            // The DataTypeDefinition Attribute is what the registry needs, and it is
            // there whether the server generated the type from a model design or read it
            // out of a NodeSet2 document at run time.
            foreach (NodeId nodeId in await FindDataTypesAsync(session, ct).ConfigureAwait(false))
            {
                if (compiled.Contains(nodeId))
                {
                    continue;
                }

                DataTypeNode node = await ReadDataTypeAsync(session, nodeId, ct).ConfigureAwait(false);

                if (node == null || !m_registry.TryAddDataType(node, session.NamespaceUris))
                {
                    continue;
                }

                types.Add(new SchemaDataType(
                    nodeId,
                    node.BrowseName.Name,
                    session.NamespaceUris.GetString(nodeId.NamespaceIndex),
                    false));
            }

            return types;
        }

        /// <summary>
        /// The data types of the server which are not in the standard address space.
        /// </summary>
        /// <remarks>
        /// Walked from Structure and Enumeration down the HasSubtype hierarchy, because
        /// those two are what the schema generators can describe. Types in namespace zero
        /// are skipped: the standard types have their schemas published already.
        /// </remarks>
        private static async Task<IReadOnlyList<NodeId>> FindDataTypesAsync(
            ISession session,
            CancellationToken ct)
        {
            var found = new List<NodeId>();
            var visited = new HashSet<NodeId>();

            await AppendSubtypesAsync(session, DataTypeIds.Structure, found, visited, ct)
                .ConfigureAwait(false);
            await AppendSubtypesAsync(session, DataTypeIds.Enumeration, found, visited, ct)
                .ConfigureAwait(false);

            return found;
        }

        /// <summary>
        /// Appends the subtypes of a data type, and their subtypes.
        /// </summary>
        private static async Task AppendSubtypesAsync(
            ISession session,
            NodeId parentId,
            List<NodeId> found,
            HashSet<NodeId> visited,
            CancellationToken ct)
        {
            var nodeToBrowse = new BrowseDescription {
                NodeId = parentId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                IncludeSubtypes = false,
                NodeClassMask = (uint)NodeClass.DataType,
                ResultMask = (uint)BrowseResultMask.All,
            };

            List<ReferenceDescription> references = await SampleSession
                .BrowseAsync(session, nodeToBrowse, false, ct)
                .ConfigureAwait(false);

            if (references == null)
            {
                return;
            }

            foreach (ReferenceDescription reference in references)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);

                if (nodeId.IsNull || !visited.Add(nodeId))
                {
                    continue;
                }

                if (nodeId.NamespaceIndex != 0)
                {
                    found.Add(nodeId);
                }

                await AppendSubtypesAsync(session, nodeId, found, visited, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads the browse name and the definition of a data type node, which is what
        /// <see cref="DataTypeDefinitionRegistryExtensions.TryAddDataType"/> needs.
        /// </summary>
        private static async Task<DataTypeNode> ReadDataTypeAsync(
            ISession session,
            NodeId nodeId,
            CancellationToken ct)
        {
            var valuesToRead = new List<ReadValueId> {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.BrowseName },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.DataTypeDefinition },
            };

            ReadResponse response = await session
                .ReadAsync(null, 0, TimestampsToReturn.Neither, valuesToRead, ct)
                .ConfigureAwait(false);

            DataValue[] results = response.Results.ToArray();

            if (results.Length < 2 ||
                !results[0].WrappedValue.TryGetValue(out QualifiedName browseName) ||
                !results[1].WrappedValue.TryGetValue(out ExtensionObject definition))
            {
                // an abstract type, or one whose server does not publish a definition
                return null;
            }

            return new DataTypeNode {
                NodeId = nodeId,
                BrowseName = browseName,
                DataTypeDefinition = definition,
            };
        }
    }
}
