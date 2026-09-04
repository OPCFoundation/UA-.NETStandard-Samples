/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Schema;
using Quickstarts.DataTypes.Model;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the DataTypes client exists to show, asked of its model without the window: a
    /// value of a structure the client has no compiled knowledge of arrives decoded into
    /// its fields once the type system of the server was loaded.
    /// </summary>
    [TestFixture]
    [Category("ClientModel")]
    [NonParallelizable]
    public class DataTypesClientModelTests : ClientModelFixtureBase<DataTypesClientModel>
    {
        protected override string SampleName => "DataTypes";

        protected override DataTypesClientModel CreateModel(ITelemetryContext telemetry)
        {
            return new DataTypesClientModel(telemetry);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task AttachLoadsTheTypeSystem(CancellationToken ct)
        {
            Assert.That(Model.TypeSystemLoaded, Is.False, "A detached model claims to have loaded types.");

            await AttachAsync(ct).ConfigureAwait(false);

            Assert.That(Model.TypeSystemLoaded, Is.True, "Attaching did not load the type system of the server.");

            await Model.DetachAsync().ConfigureAwait(false);

            Assert.That(Model.TypeSystemLoaded, Is.False, "The types were built on the session and have to go with it.");
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task ReadsTheStructuredPrimaryVehicle(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            // the vehicle of the driver of the month is a structure of the second node set,
            // derived from the vehicle type of the first
            NodeId vehicle = await PathAsync(ct, "ParkingLot", "DriverOfTheMonth", "PrimaryVehicle").ConfigureAwait(false);

            DataValue value = await Model.ReadValueAsync(vehicle, ct).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, $"Reading the primary vehicle failed: {value.StatusCode}");

            string text = value.WrappedValue.ToString();

            await TestContext.Out.WriteLineAsync($"PrimaryVehicle: {text}").ConfigureAwait(false);

            Assert.Multiple(() => {
                // Make and Model come from the vehicle type of the first node set, the
                // manufacturer and the gears from the bicycle type of the second. A value
                // decoded only as its declared base type would be missing the last two
                Assert.That(
                    text,
                    Does.Contain("Trek"),
                    "The value has to arrive decoded into its fields, which is the whole point of the sample.");

                Assert.That(
                    text,
                    Does.Contain("Cube").And.Contain("10"),
                    "The value has to keep the fields the derived structure adds to the one the property is declared as.");
            });

            // the type id of the structure has to name one of the encodings the sample
            // serves, otherwise a client has nothing to decode it with. The source
            // generator of 2.0.0-preview.4 emits the design's default value as bare XML and
            // the XML decoder leaves an extension object without a TypeId undecoded, so the
            // value arrives with a null TypeId until the stack fixes it.
            await KnownIssueAsync(
                () => {
                    Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject encoded), Is.True, "The primary vehicle is a structure, so it arrives as an extension object.");
                    Assert.That(encoded.TypeId.IsNull, Is.False, "The structure has to name the encoding it was written with.");
                    return Task.CompletedTask;
                },
                "OPCFoundation/UA-.NETStandard#4401: a structure default value generated from a " +
                "ModelDesign is served as an ExtensionObject with a null TypeId.")
                .ConfigureAwait(false);
        }

        [Test]
        public void ReadBeforeAttachThrows()
        {
            Assert.That(
                async () => await Model.ReadValueAsync(Opc.Ua.VariableIds.Server_ServerStatus).ConfigureAwait(false),
                Throws.InstanceOf<InvalidOperationException>(),
                "A detached model has no session to read from.");
        }

        /// <summary>
        /// The model finds every data type of the server, including the ones only the
        /// model design of the server declares.
        /// </summary>
        /// <remarks>
        /// <c>CarType</c> is in the model design the client shares with the server;
        /// <c>BicycleType</c> is in the model design of the server alone, and the client
        /// has no class for it at all. Both have to end up in the list, because both have
        /// a <c>DataTypeDefinition</c> the server publishes.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task EveryDataTypeOfTheServerIsFound(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(string.Join(
                    ", ",
                    Model.DataTypes.Select(type => $"{type.Name} ({(type.FromCompiledType ? "compiled" : "browsed")})")))
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    Model.DataTypes.Select(type => type.Name),
                    Does.Contain("CarType").And.Contain("BicycleType").And.Contain("EngineType"),
                    "The model did not register every data type the server declares.");

                Assert.That(
                    Find("BicycleType").FromCompiledType,
                    Is.False,
                    "BicycleType is only in the model of the server, so it can only come off the wire.");
            });
        }

        /// <summary>
        /// A type the client compiled should not have to be browsed for at all: the
        /// generated class carries the definition its model design declared.
        /// </summary>
        /// <remarks>
        /// The source generator of 2.0.0-preview.4 does not emit
        /// <c>IDataTypeDefinitionSource</c> on the structures it generates, so the model's
        /// compiled branch registers nothing and <c>CarType</c> arrives off the wire like
        /// every other type. This is recorded rather than asserted, and starts failing the
        /// moment the generator emits the interface - which is when the branch starts
        /// doing its job and this wrapper comes off.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ACompiledTypeCarriesItsOwnDefinition(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            await KnownIssueAsync(
                () => {
                    Assert.That(
                        Find("CarType").FromCompiledType,
                        Is.True,
                        "CarType is compiled into the client, so its definition should come from the class.");

                    return Task.CompletedTask;
                },
                "OPCFoundation/UA-.NETStandard#4424: " +
                "the ModelDesign source generator of 2.0.0-preview.4 does not implement " +
                "IDataTypeDefinitionSource on the structures it generates, so a compiled type " +
                "cannot hand over its own DataTypeDefinition.")
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Every format produces a document, and it names the type it was asked for.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        [TestCase("CarType", UaSchemaFormat.Xsd)]
        [TestCase("CarType", UaSchemaFormat.Bsd)]
        [TestCase("CarType", UaSchemaFormat.JsonCompact)]
        [TestCase("CarType", UaSchemaFormat.JsonVerbose)]
        [TestCase("BicycleType", UaSchemaFormat.Xsd)]
        [TestCase("BicycleType", UaSchemaFormat.Bsd)]
        [TestCase("BicycleType", UaSchemaFormat.JsonCompact)]
        public async Task EveryFormatProducesASchema(string name, UaSchemaFormat format, CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            string schema = Model.CreateSchema(Find(name), format, UaSchemaScope.Type);

            await TestContext.Out.WriteLineAsync(schema).ConfigureAwait(false);

            Assert.That(schema, Does.Contain(name), $"The {format} schema does not name {name}.");
        }

        /// <summary>
        /// A type scoped schema carries the types the one it describes depends on, because
        /// a document which referred to them without defining them would validate nothing.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ASchemaCarriesTheTypesItDependsOn(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            string schema = Model.CreateSchema(Find("CarType"), UaSchemaFormat.Bsd, UaSchemaScope.Type);

            await TestContext.Out.WriteLineAsync(schema).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(schema, Does.Contain("CarType"), "The schema does not describe CarType.");
                Assert.That(schema, Does.Contain("EngineType"), "The enumeration CarType uses is missing.");
            });
        }

        /// <summary>
        /// The namespace scope produces the dictionary of the whole namespace, which is
        /// more than the closure of one type.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheNamespaceScopeProducesTheWholeDictionary(CancellationToken ct)
        {
            await AttachAsync(ct).ConfigureAwait(false);

            SchemaDataType car = Find("CarType");

            string one = Model.CreateSchema(car, UaSchemaFormat.Bsd, UaSchemaScope.Type);
            string all = Model.CreateSchema(car, UaSchemaFormat.Bsd, UaSchemaScope.Namespace);

            Assert.Multiple(() => {
                Assert.That(all, Does.Contain("TruckType"), "The dictionary does not carry every type of the namespace.");
                Assert.That(one, Does.Not.Contain("TruckType"), "The type scope pulled in a type CarType does not use.");
            });
        }

        /// <summary>
        /// The data type of the fixture, by name.
        /// </summary>
        private SchemaDataType Find(string name)
        {
            SchemaDataType type = Model.DataTypes.FirstOrDefault(candidate => candidate.Name == name);

            Assert.That(
                type,
                Is.Not.Null,
                $"The model did not register '{name}'. It registered: " +
                string.Join(", ", Model.DataTypes.Select(candidate => candidate.Name)));

            return type;
        }

        /// <summary>
        /// Runs an assertion which is expected to fail, and reports the test as ignored.
        /// </summary>
        /// <remarks>
        /// The same helper the node manager tier has; it lives in that test project, which
        /// this one does not reference.
        /// </remarks>
        private static async Task KnownIssueAsync(Func<Task> check, string issue)
        {
            try
            {
                await check().ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not SuccessException)
            {
                Assert.Ignore($"Known issue: {issue}{Environment.NewLine}The test reported: {failure.Message}");
                return;
            }

            Assert.Fail(
                $"This is recorded as a known issue, but it passed: {issue}{Environment.NewLine}" +
                "Remove the KnownIssueAsync wrapper and let the assertion stand on its own.");
        }
    }
}
