# DataTypes Server

This server is build using the UA-.NETStandard stack as example of how to define and use custom data types.

## Two models, one generator

The sample carries two information models, both compiled by the **OPC UA source
generator** at build time:

| Model | Assembly | Where |
|-------|----------|-------|
| `Common/Types/ModelDesign1.xml` - the vehicle types | [DataTypes Library](./Common/DataTypes%20Library.csproj), shared by the client and the server | `AdditionalFiles` in the library project |
| `Server/Instances/ModelDesign2.xml` - the parking lot and the two wheelers | [DataTypes Server](./Server/DataTypes%20Server.csproj) | `AdditionalFiles` in the server project |

The `.cs` files never enter the repository, the model and the code cannot drift apart,
and the generator emits a registration extension per model (`AddQuickstartsDataTypesTypes`,
`AddQuickstartsDataTypesInstances`) instead of leaving a client to find the types by
reflection. Reference the analyzer package and hand the design to it:

```xml
<ItemGroup>
  <AdditionalFiles Include="Types\ModelDesign1.xml" />
  <AdditionalFiles Include="Types\ModelDesign1.csv" />
  <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.SourceGeneration"
                    Version="..." OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

The instance model reaches across the project boundary: its `BicycleType` subtypes a
structure of the type model, and its `DriverOfTheMonth` is an instance of the `DriverType`
declared there. The generated code refers to the classes of the library assembly for
both. The generator resolves such references from the model dependency metadata every
generated assembly carries; the server project hands it the type model's design file as
well (a second pair of `AdditionalFiles`), because the metadata does not carry the access
levels and default values of the children a type declares, and the design file does. No
code is generated for that design in the server - the library already supplies the same
C# namespace.

The type model is built with `ModelSourceGeneratorOmitFluentApi=true`: the library is
shared with the client and must not depend on `Opc.Ua.Server`.

### Known gap on 2.0.0-preview.4

The generated node sets keep the default values of a model as the XML the design
declares, decoded when the nodes are created. An `ExtensionObject` written that way has
no `TypeId`, and the XML decoder of the SDK only resolves a body by its type id, never by
its element name (`XmlDecoder.ReadExtensionObjectBody`). The structured default values
of the sample - the driver's `PrimaryVehicle`, the `VehiclesInLot` - therefore load as raw
XML with a null type id, which a client cannot decode; values a client writes round-trip
as before. Two of the node manager tests of the sample record this as a known issue
([UA-.NETStandard#4401](https://github.com/OPCFoundation/UA-.NETStandard/issues/4401))
and start failing the moment the SDK resolves such bodies, which is when the wrappers come
off.

## How to integrate new information model into OPC Server

This documentation explains how to add a custom information model to OPC Server based on UA-.NETStandard stack. It will use the DataTypes server example as reference but the general steps are the same for every UA-.NETStandard stack based OPC Server.

### Add own information model

1. Create a folder for the model, e.g. `Workshop/DataTypes/Server/MyInformationModel`
2. Copy the model design into it, e.g. `MyInformationModel.xml`, together with the `.csv`
   file that pins the numeric node ids (the generator assigns ids to nodes the file does
   not list, so the file may start empty)
3. Hand both files to the source generator in the project file:

```xml
<ItemGroup>
  <AdditionalFiles Include="MyInformationModel\MyInformationModel.xml" />
  <AdditionalFiles Include="MyInformationModel\MyInformationModel.csv" />
</ItemGroup>
```

The generator emits the node states, the data types, the constants (`Objects`,
`Variables`, `DataTypes`, ... and `Namespaces`) and the registration extensions of the
model into the C# namespace the `Prefix` of the model's `<opc:Namespace>` names. When the
design refers to a model of another project - a base type, a type definition - reference
that project and, as this sample does for the type model, add its design as
`AdditionalFiles` too so the inherited children come out complete.

### Use information model

Extend the [DataTypesNodeManager](./Server/DataTypesNodeManager.cs). A node manager
serves one generated model directly: the `[NodeManager]` attribute selects it by its
namespace URI, and the generated partial loads it. Every further model is named in
`AdditionalNamespaceUris` and loaded in front of it:

```csharp
[NodeManager(
    NamespaceUri = "http://opcfoundation.org/UA/Quickstarts/DataTypes/Instances",
    AdditionalNamespaceUris = new[] {
        Quickstarts.DataTypes.Types.Namespaces.DataTypes,
        Quickstarts.DataTypes.Namespaces.DataTypes,
        MyNamespace.Namespaces.MyInformationModel
    })]
public partial class DataTypesNodeManager
{
    protected override async ValueTask LoadPredefinedNodesAsync(
        ISystemContext context,
        IDictionary<NodeId, IList<IReference>> externalReferences,
        CancellationToken cancellationToken = default)
    {
        // every generated model brings its own registration extension
        Server.Factory.Builder
            .AddQuickstartsDataTypesTypes()
            .AddQuickstartsDataTypesInstances()
            .AddMyNamespaceMyInformationModel()
            .Commit();

        // add the nodes of the models the generated partial does not load itself
        NodeStateCollection nodes = new NodeStateCollection()
            .AddQuickstartsDataTypesTypes(context)
            .AddMyNamespaceMyInformationModel(context);

        foreach (NodeState node in nodes)
        {
            await AddPredefinedNodeAsync(context, node, cancellationToken).ConfigureAwait(false);
        }

        await base.LoadPredefinedNodesAsync(context, externalReferences, cancellationToken).ConfigureAwait(false);
    }
}
```

The generated `DataTypesNodeManagerFactory` announces every namespace the attribute
names, so nothing else has to change for the server to route requests for the new
namespace to this node manager.

Compile and run the DataTypes server, you should be able to connect with any OPC UA client (e.g. DataTypes Client) and to browse your own data types.

*Remark* the type dictionaries (`*.xsd`, `*.bsd`) and the node set files are no longer needed: the generated code carries the nodes, the data type definitions and the schemas.
