/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Configuration;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 2: the shared client controls have to connect with a managed session and receive
    /// data changes through the V2 subscription engine.
    /// </summary>
    /// <remarks>
    /// The client smoke tests only prove that a sample connects. This drives the subscription
    /// wizard of the shared control the same way a user would - create the subscription, add an
    /// item, step to apply and then to view - so a notification which never arrives at the grid
    /// fails a test instead of showing up as an empty window.
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class SubscribeControlTests
    {
        private const int kTimeout = 90_000;

        /// <summary>
        /// The sample whose server this drives the controls against.
        /// </summary>
        private const string kSample = "Reference";

        [Test]
        [CancelAfter(kTimeout)]
        public async Task ControlsSubscribeAndReceiveDataChanges(CancellationToken ct)
        {
            SampleDefinition sample = SampleCatalog.All.Single(entry => entry.Name == kSample);
            SampleServerUnderTest server = SampleServerFactories.All.Single(entry => entry.Sample.Name == kSample);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(kSample, server.Sample.ServerConfig, server.ConfigureServices, ct)
                .ConfigureAwait(false);

            await WinFormsHarness.RunAsync(
                async _ => await DriveSubscriptionAsync(sample, host.EndpointUrl, ct).ConfigureAwait(true),
                TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task DriveSubscriptionAsync(SampleDefinition sample, string endpointUrl, CancellationToken ct)
        {
            using var pki = new TemporaryPki("client-subscribe-control");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            bool certificateOk = await application
                .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                .ConfigureAwait(true);

            Assert.That(certificateOk, Is.True, "The client certificate of the sample could not be created.");

            using var form = new Form();
            using var connect = new ConnectServerCtrl { Configuration = configuration };
            using var subscribe = new SubscribeDataListViewCtrl();

            form.Controls.Add(connect);
            form.Controls.Add(subscribe);

            // create the window handle without ever showing the form: the controls marshal their
            // notification callbacks back to the UI thread and need a handle to do so
            _ = form.Handle;

            ISession session = await connect
                .ConnectAsync(NullTelemetry.Instance, endpointUrl, false, 30_000, ct)
                .ConfigureAwait(true);

            try
            {
                Assert.That(
                    session,
                    Is.InstanceOf<ManagedSession>(),
                    "The connect control no longer creates a managed session, so it is back to " +
                    "driving the reconnect itself.");

                ISubscription subscription = subscribe.CreateSubscription(session);

                Assert.That(subscription, Is.Not.Null, "The control did not create a subscription.");

                await subscribe.AddItemsAsync(
                    ct,
                    new ReadValueId {
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                        AttributeId = Attributes.Value,
                    })
                    .ConfigureAwait(true);

                // step the wizard: edit items -> apply changes -> view updates
                await subscribe.NextAsync(ct).ConfigureAwait(true);
                await subscribe.NextAsync(ct).ConfigureAwait(true);

                IMonitoredItem monitoredItem = subscription.MonitoredItems.Items.SingleOrDefault();

                Assert.That(monitoredItem, Is.Not.Null, "The control did not add a monitored item.");
                Assert.That(
                    ServiceResult.IsGood(monitoredItem.Error),
                    Is.True,
                    $"The server refused the monitored item: {monitoredItem.Error}");

                DataValue? value = await WaitForValueAsync(subscribe, ct).ConfigureAwait(true);

                Assert.That(
                    value,
                    Is.Not.Null,
                    "No data change reached the grid, so the notification handler of the control " +
                    "is not wired up to the subscription engine.");

                Assert.That(
                    StatusCode.IsGood(value.Value.StatusCode),
                    Is.True,
                    $"The data change reported {value.Value.StatusCode}.");
            }
            finally
            {
                await connect.DisconnectAsync(ct).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Waits until a data change has been written into the grid of the control.
        /// </summary>
        /// <remarks>
        /// The grid is bound to a table the control fills from its notification callback. The
        /// designer field is private, and reflection is used rather than widening the control
        /// just for this test.
        /// </remarks>
        private static async Task<DataValue?> WaitForValueAsync(SubscribeDataListViewCtrl control, CancellationToken ct)
        {
            var grid = (DataGridView)WinFormsHarness.FindControl(control, "ResultsDV");

            Assert.That(grid, Is.Not.Null, "The control no longer has a 'ResultsDV' grid.");

            var table = (DataTable)grid.DataSource;
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);

            while (DateTime.UtcNow < deadline)
            {
                if (table.Rows.Count > 0 && table.Rows[0]["DataValue"] is DataValue value)
                {
                    return value;
                }

                await Task.Delay(100, ct).ConfigureAwait(true);
            }

            return null;
        }
    }
}
