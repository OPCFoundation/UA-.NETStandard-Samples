/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Quickstarts.DataTypes.Instances;
using Quickstarts.DataTypes.Types;

using InstanceObjects = Quickstarts.DataTypes.Instances.Objects;
using InstanceVariables = Quickstarts.DataTypes.Instances.Variables;
using TypeDataTypes = Quickstarts.DataTypes.Types.DataTypes;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the data types sample does: it serves structured data types of its own, from
    /// two node sets which are loaded from two different assemblies.
    /// </summary>
    /// <remarks>
    /// The type model and the instances that use it are built separately, and the node
    /// manager loads both into the same namespace table. A client can only make sense of a
    /// custom structure if the data type, its encodings and the instance which carries a
    /// value all arrive intact, so that whole chain is what the tests follow.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class DataTypesNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "DataTypes";

        private const string TypesNamespace = Quickstarts.DataTypes.Types.Namespaces.DataTypes;
        private const string InstancesNamespace = Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances;

        /// <summary>
        /// The custom data types are served with both of their encodings.
        /// </summary>
        /// <remarks>
        /// Without the encodings a client cannot decode a value of the type, so a data type
        /// node on its own would not be enough.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task CustomDataTypesAreExposedWithTheirEncodings(CancellationToken ct)
        {
            var vehicleType = new NodeId(TypeDataTypes.VehicleType, NamespaceIndex(TypesNamespace));

            DataValue browseName = await SessionOps
                .ReadAttributeAsync(Session, vehicleType, Attributes.BrowseName, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(browseName.StatusCode),
                Is.True,
                $"The vehicle data type is not served: {browseName.StatusCode}");

            IReadOnlyList<ReferenceDescription> encodings = await SessionOps.BrowseAsync(
                Session,
                vehicleType,
                ct,
                referenceTypeId: ReferenceTypeIds.HasEncoding,
                includeSubtypes: false).ConfigureAwait(false);

            await ReportAsync("VehicleType encodings", encodings.Select(encoding => encoding.BrowseName.Name))
                .ConfigureAwait(false);

            Assert.That(
                encodings.Select(encoding => encoding.BrowseName.Name),
                Does.Contain(BrowseNames.DefaultBinary).And.Contain(BrowseNames.DefaultXml),
                "A custom structure has to offer both standard encodings.");
        }

        /// <summary>
        /// An instance from the second node set carries a value of a type from the first.
        /// </summary>
        /// <remarks>
        /// This is the test which proves that loading two node sets into one node manager
        /// worked: the instance is only usable if the type it refers to came along.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DriverOfTheMonthCarriesAStructuredValue(CancellationToken ct)
        {
            var primaryVehicle = new NodeId(
                InstanceVariables.ParkingLot_DriverOfTheMonth_PrimaryVehicle,
                NamespaceIndex(InstancesNamespace));

            DataValue value = await SessionOps
                .ReadValueAsync(Session, primaryVehicle, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading the primary vehicle failed: {value.StatusCode}");

            await TestContext.Out
                .WriteLineAsync($"PrimaryVehicle: {value.WrappedValue}")
                .ConfigureAwait(false);

            Assert.That(
                value.WrappedValue.AsBoxedObject(),
                Is.InstanceOf<ExtensionObject>(),
                "The primary vehicle is a structure, so it arrives as an extension object.");

            // the type id of the structure has to name one of the encodings the sample
            // serves, otherwise a client has nothing to decode it with
            var encoded = (ExtensionObject)value.WrappedValue.AsBoxedObject();

            Assert.That(
                encoded.TypeId.IsNull,
                Is.False,
                "The structure has to name the encoding it was written with.");
        }

        /// <summary>
        /// The instance hierarchy of the second node set is browsable.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task ParkingLotHierarchyIsBrowsable(CancellationToken ct)
        {
            var parkingLot = new NodeId(InstanceObjects.ParkingLot, NamespaceIndex(InstancesNamespace));

            IReadOnlyList<string> children = await BrowseNamesAsync(parkingLot, ct).ConfigureAwait(false);

            await ReportAsync("ParkingLot", children).ConfigureAwait(false);

            Assert.That(
                children,
                Does.Contain("DriverOfTheMonth").And.Contain("VehiclesInLot"),
                "The parking lot of the instance node set lost its contents.");
        }

        /// <summary>
        /// A client which registers the data types decodes the structure the server serves
        /// into the derived type it was written as, inherited fields included.
        /// </summary>
        /// <remarks>
        /// This is the chain the sample exists for: the bicycle comes from the instance
        /// model, its make and model from the vehicle type of the other model, which the
        /// source generator builds from a model design. If the generated encodings did not
        /// line up with the node set, the value would either stay an undecoded extension
        /// object or come back short of its inherited fields.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task GeneratedActivatorsDecodeTheSamplesStructures(CancellationToken ct)
        {
            RegisterGeneratedDataTypes();

            DataValue value = await SessionOps
                .ReadValueAsync(Session, PrimaryVehicle, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading the primary vehicle failed: {value.StatusCode}");

            var decoded = value.WrappedValue.AsBoxedObject() as ExtensionObject?;

            Assert.That(decoded, Is.Not.Null, "The primary vehicle is a structure.");

            decoded.Value.TryGetValue(out IEncodeable body);

            await TestContext.Out
                .WriteLineAsync($"PrimaryVehicle: {body?.GetType().Name}")
                .ConfigureAwait(false);

            Assert.That(
                body,
                Is.InstanceOf<BicycleType>(),
                "The driver of the month rides the bicycle the instance model gives them, " +
                "and the registered activator has to turn it back into one.");

            var bicycle = (BicycleType)body;

            Assert.Multiple(() => {
                Assert.That(
                    bicycle.Make,
                    Is.EqualTo("Trek"),
                    "Make is inherited from the generated vehicle type of the other model.");

                Assert.That(
                    bicycle.NoOfGears,
                    Is.EqualTo(10u),
                    "The number of gears is the bicycle's own field.");
            });
        }

        /// <summary>
        /// A structure written by a client comes back with every field intact.
        /// </summary>
        /// <remarks>
        /// The whole point of the sample is that a client and a server agree on a custom
        /// structure, so the round trip is the test that matters. The bicycle is the
        /// interesting case: its make, model and engine come from the vehicle type of the
        /// other model, which the generator builds from a model design, while the bicycle
        /// itself is a <c>[DataType]</c> class. Both halves of the encoding have to line up.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task GeneratedStructuresRoundTrip(CancellationToken ct)
        {
            RegisterGeneratedDataTypes();

            var written = new BicycleType {
                Make = "Trek",
                Model = "Compact",
                Engine = EngineType.Manual,
                ManufacturerName = "Cube",
                NoOfGears = 10,
            };

            StatusCode writeResult = await SessionOps
                .WriteValueAsync(
                    Session,
                    PrimaryVehicle,
                    new Variant(new ExtensionObject(written)),
                    ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(writeResult),
                Is.True,
                $"Writing a bicycle to the primary vehicle failed: {writeResult}");

            DataValue value = await SessionOps
                .ReadValueAsync(Session, PrimaryVehicle, ct)
                .ConfigureAwait(false);

            var decoded = value.WrappedValue.AsBoxedObject() as ExtensionObject?;

            Assert.That(decoded, Is.Not.Null, "The primary vehicle is a structure.");

            Assert.That(
                decoded.Value.TryGetValue(out BicycleType read),
                Is.True,
                "What went in as a bicycle has to come back as one.");

            await TestContext.Out
                .WriteLineAsync(
                    $"Round trip: {read.Make} {read.Model}, {read.Engine}, " +
                    $"{read.ManufacturerName}, {read.NoOfGears} gears")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(read.Make, Is.EqualTo("Trek"), "Make comes from the vehicle type.");
                Assert.That(read.Model, Is.EqualTo("Compact"), "Model comes from the vehicle type.");
                Assert.That(read.Engine, Is.EqualTo(EngineType.Manual), "Engine is an enumeration of the vehicle type.");
                Assert.That(read.ManufacturerName, Is.EqualTo("Cube"), "ManufacturerName comes from the two wheeler.");
                Assert.That(read.NoOfGears, Is.EqualTo(10u), "NoOfGears is the bicycle's own field.");
            });
        }

        private NodeId PrimaryVehicle => new(
            InstanceVariables.ParkingLot_DriverOfTheMonth_PrimaryVehicle,
            NamespaceIndex(InstancesNamespace));

        /// <summary>
        /// Registers the data types of both models of the sample, which is what a client
        /// has to do to decode their structures.
        /// </summary>
        /// <remarks>
        /// The vehicle types come with the registration extension the source generator
        /// emitted from the model design of the library; the instance model is still
        /// ModelCompiler output and is registered by reflection.
        /// </remarks>
        private void RegisterGeneratedDataTypes()
        {
            Session.Factory.Builder.AddQuickstartsDataTypesTypes().Commit();
            Session.Factory.AddEncodeableTypes(typeof(BicycleType).Assembly);
        }
    }
}
