/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Linq;
using AggregationClient.Model;
using NUnit.Framework;
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// A client model which no fixture drives has to be declared, so it cannot drop out
    /// unnoticed.
    /// </summary>
    [TestFixture]
    [Category("ClientModel")]
    public class UncoveredClientModelsTests
    {
        /// <summary>
        /// The Aggregation client has a model like every other client, but no fixture:
        /// its server aggregates other servers and there is no factory which starts it
        /// in process, so the model tier cannot attach to it. This is the same gap the
        /// form tier declares.
        /// </summary>
        [Test]
        public void AggregationIsTheDeclaredGap()
        {
            Assert.Multiple(() => {
                Assert.That(
                    typeof(SampleClientModel).IsAssignableFrom(typeof(AggregationClientModel)),
                    Is.True,
                    "The Aggregation client has to keep its logic in a model, fixture or not.");

                Assert.That(
                    SampleCatalog.Clients.Select(sample => sample.Name),
                    Does.Contain("Aggregation"),
                    "The catalog no longer lists the Aggregation client; drop this test with it.");

                Assert.That(
                    SampleServerFactories.All.Select(server => server.Sample.Name),
                    Does.Not.Contain("Aggregation"),
                    "There is a server factory for Aggregation now, so its model can be tested: " +
                    "add an AggregationClientModelTests fixture and remove this declaration.");
            });
        }
    }
}
