# DataTypes Server

This server is build using the UA-.NETStandard stack as example of how to define and use custom data types.

## Two ways to turn a model into code

The sample carries two information models, and each is built a different way, because
together they show both routes:

| Model | Built by | Where |
|-------|----------|-------|
| `Common/Types/ModelDesign1.xml` - the vehicle types | the **OPC UA source generator** at compile time | `AdditionalFiles` in [DataTypes Library.csproj](./Common/DataTypes%20Library.csproj) |
| `Server/Instances/ModelDesign2.xml` - the parking lot and the two wheelers | the **ModelCompiler**, checked in | [Server/BuildDesign.bat](./Server/BuildDesign.bat) |

The source generator is the route to prefer: the `.cs` files never enter the repository,
the model and the code cannot drift apart, and the generator emits a registration
extension (`AddQuickstartsDataTypesTypes`) instead of leaving a client to find the types
by reflection. Reference the analyzer package and hand the design to it:

```xml
<ItemGroup>
  <AdditionalFiles Include="Types\ModelDesign1.xml" />
  <AdditionalFiles Include="Types\ModelDesign1.csv" />
  <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.SourceGeneration"
                    Version="..." OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

The instance model cannot take that route yet: its `BicycleType` subtypes a structure
declared in the *other* design file, which the model source generator does not support
([UA-.NETStandard#4332](https://github.com/OPCFoundation/UA-.NETStandard/issues/4332)).
Writing those types by hand as `[DataType]` classes does not work around it either -
the attribute generator emits a `Clone()` which drops the inherited fields, and the
server clones node values while loading its address space
([UA-.NETStandard#4352](https://github.com/OPCFoundation/UA-.NETStandard/issues/4352)).
The instance model therefore still uses the ModelCompiler workflow described below; see
[#814](https://github.com/OPCFoundation/UA-.NETStandard-Samples/issues/814) for the
follow-up.

## How to integrate new information model into OPC Server

This documentation explains how to add a custom information model to OPC Server based on UA-.NETStandard stack. It will use the DataTypes server example as reference but the general steps are the same for every UA-.NETStandard stack based OPC Server.

### Preparation

1. Clone [Opc.Ua.ModelCompiler Repository](https://github.com/OPCFoundation/UA-ModelCompiler)
2. Build Opc.Ua.ModelCompiler solution
3. Copy the build result of Opc.Ua.ModelCompiler solution to [UA-.NETStandard/SampleApplications/bin](./../../bin)

### Add own information model

1. Create a Folder to to UA-.NETStandard/SampleApplications/Workshop/DataTypes/Common e.g. MyInformationModel
   1. Create a sub-folder named "Output"
2. Copy the model itself into this folder e.g. MyInformationModel.xml into UA-.NETStandard/SampleApplications/Workshop/DataTypes/Common/MyInformationModel
3. Modify [BuildDesign.bat](./Common/BuildDesign.bat) and add the following lines

```cmd
echo Building MyInformationModel
Opc.Ua.ModelCompiler.exe -version v104 -d2 ".\MyInformationModel\MyInformationModel.xml" -cg ".\MyInformationModel\Output\MyInformationModel.csv" -o2 ".\MyInformationModel\Output"
echo Success!
```

4. Run [BuildDesign.bat](./Common/BuildDesign.bat)

```cmd
.\Common\BuildDesign.bat
```

In case of an issue the Opc.Ua.ModelCompiler will show and error dialog, otherwise you will have different files in your output folder, that need to be added into the project. Either as source code or as embedded resource.

### Use information model

Extend the [DataTypesNodeManager](./Server/DataTypesNodeManager.cs):

```csharp
// in the constructor - add the namespaces of the new model to the base call,
// and register its encodeable types

public DataTypesNodeManager(IServerInternal server, ApplicationConfiguration configuration)
:
    base(server, configuration,
        Quickstarts.DataTypes.Namespaces.DataTypes,
        Quickstarts.DataTypes.Types.Namespaces.DataTypes,
        Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances,
        MyNamespace.DataTypes.Types.Namespaces.DataTypes)
{
    // a source generated model brings its own registration extension
    Server.Factory.Builder.AddMyNamespaceDataTypes().Commit();

    // a ModelCompiler built model is registered by reflection over its assembly
    Server.Factory.AddEncodeableTypes(typeof(MyNamespace.DataTypes.Types.MyDataType).Assembly);
    ...
}

// in LoadPredefinedNodesAsync

predefinedNodes.LoadFromBinaryResource(context,
      "MyNamespace.DataTypes.Types.MyNamespace.DataTypes.Types.PredefinedNodes.uanodes",
      typeof(MyNamespace.DataTypes.Types.MyDataType).Assembly,
      true);
```

The factory in the same file announces the namespaces the node manager serves, so
new namespaces are added to its `NamespacesUris` property as well.

Compile and run the DataTypes server, you should be able to connect with any OPC UA client (e.g. DataTypes Client) and to browse your own data types.

*Remark* you don't need to load schema files (*.xsd, *.bsd) because the *.uanodes files contains those information already. 
