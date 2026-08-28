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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using CarType = Quickstarts.DataTypes.Types.CarType;
using EngineType = Quickstarts.DataTypes.Types.EngineType;
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
        /// A property of the sample can be subscribed to and reports its current value.
        /// </summary>
        /// <remarks>
        /// Monitored items go through the monitored-item manager of the node manager,
        /// which is a part the migration to <c>AsyncCustomNodeManager</c> replaces
        /// wholesale, so the subscription path gets its own pin.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SubscribingToTheLotTypeDeliversTheCurrentValue(CancellationToken ct)
        {
            var lotType = new NodeId(
                InstanceVariables.ParkingLot_LotType,
                NamespaceIndex(InstancesNamespace));

            DataValue read = await SessionOps.ReadValueAsync(Session, lotType, ct).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(read.StatusCode),
                Is.True,
                $"Reading the lot type failed: {read.StatusCode}");

            await using DataChangeCapture capture = await DataChangeCapture
                .CreateAsync(Session, lotType, ct)
                .ConfigureAwait(false);

            DataValue reported = await capture.NextAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"LotType: read {read.WrappedValue}, subscription reported {reported.WrappedValue}")
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(reported.StatusCode),
                Is.True,
                $"The subscription reported a bad status: {reported.StatusCode}");

            Assert.That(
                reported.WrappedValue,
                Is.EqualTo(read.WrappedValue),
                "The subscription has to report the same value a read returns.");
        }

        /// <summary>
        /// A variable with a custom structure type accepts a write and serves the new
        /// value back.
        /// </summary>
        /// <remarks>
        /// The original value is written back at the end, so the other tests of the
        /// fixture see the address space the sample shipped no matter in which order
        /// NUnit runs them.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task WritingAStructuredValueRoundTrips(CancellationToken ct)
        {
            // decode structures on the client for this test: the session factory does
            // not know the types of the sample by default, and the round trip is only
            // provable on the decoded value.
            Session.MessageContext.Factory.AddEncodeableTypes(typeof(CarType).Assembly);

            var primaryVehicle = new NodeId(
                InstanceVariables.ParkingLot_DriverOfTheMonth_PrimaryVehicle,
                NamespaceIndex(InstancesNamespace));

            DataValue before = await SessionOps
                .ReadValueAsync(Session, primaryVehicle, ct)
                .ConfigureAwait(false);

            var written = new CarType {
                Make = "Rimac",
                Model = "Nevera",
                Engine = EngineType.Electric,
                NoOfPassengers = 2,
            };

            StatusCode result = await SessionOps
                .WriteValueAsync(Session, primaryVehicle, Variant.From(new ExtensionObject(written)), ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result),
                Is.True,
                $"Writing the primary vehicle failed: {result}");

            DataValue after = await SessionOps
                .ReadValueAsync(Session, primaryVehicle, ct)
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"PrimaryVehicle after write: {after.WrappedValue}")
                .ConfigureAwait(false);

            Assert.That(
                after.WrappedValue.AsBoxedObject(),
                Is.InstanceOf<ExtensionObject>(),
                "The written structure has to come back as an extension object.");

            var encoded = (ExtensionObject)after.WrappedValue.AsBoxedObject();

            Assert.That(
                encoded.Body,
                Is.InstanceOf<CarType>(),
                "The value has to decode into the structure that was written.");

            var car = (CarType)encoded.Body;

            Assert.That(car.Make, Is.EqualTo("Rimac"), "The write lost the content of the structure.");
            Assert.That(car.NoOfPassengers, Is.EqualTo(2u), "The write lost the content of the structure.");

            // restore the value the sample shipped.
            StatusCode restored = await SessionOps
                .WriteValueAsync(Session, primaryVehicle, before.WrappedValue, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(restored),
                Is.True,
                $"Restoring the primary vehicle failed: {restored}");
        }
    }
}
