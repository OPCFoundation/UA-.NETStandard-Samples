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
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;
using Opc.Ua.Sample.Controls;
using Opc.Ua.Samples.Client;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 2: the UA Sample Client controls have to connect with a managed session and receive
    /// data changes through the V2 subscription engine.
    /// </summary>
    /// <remarks>
    /// The client smoke tests only prove that a sample connects. This drives the subscription
    /// dialog of the sample controls the same way a user would - create the subscription, add
    /// an item through the config grid and apply - so a notification which never arrives at the
    /// data change grid fails a test instead of showing up as an empty window.
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class SampleControlsSubscribeTests
    {
        private const int kTimeout = 90_000;

        /// <summary>
        /// The sample whose server this drives the controls against.
        /// </summary>
        private const string kSample = "Reference";

        [Test]
        [CancelAfter(kTimeout)]
        public async Task SampleControlsSubscribeAndReceiveDataChanges(CancellationToken ct)
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
            using var pki = new TemporaryPki("client-sample-controls");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            bool certificateOk = await application
                .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                .ConfigureAwait(true);

            Assert.That(certificateOk, Is.True, "The client certificate of the sample could not be created.");

            // create the managed session the way SessionOpenDlg does, without the dialog.
            EndpointDescription endpointDescription = await CoreClientUtils
                .SelectEndpointAsync(configuration, endpointUrl, false, 15_000, NullTelemetry.Instance, ct)
                .ConfigureAwait(true);

            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, EndpointConfiguration.Create(configuration));

            ISession session = await new ManagedSessionFactory(NullTelemetry.Instance)
                .CreateAsync(configuration, endpoint, false, false, "SampleControlsTest", 60_000, null, (string[])null, ct)
                .ConfigureAwait(true);

            // the dialog is created the way the sample creates it: out of a container.
            var services = new ServiceCollection();

            services.AddSingleton(configuration);
            services.AddSingleton<ITelemetryContext>(NullTelemetry.Instance);
            services.AddSampleWindows();

            await using ServiceProvider provider = services.BuildServiceProvider();

            using var dialog = provider.GetRequiredService<IWindowFactory>().Create<SubscriptionDlg>();

            try
            {
                Assert.That(
                    session,
                    Is.InstanceOf<ManagedSession>(),
                    "The session open dialog path no longer creates a managed session, so the " +
                    "sample controls are back to the classic client surface.");

                // create the subscription the way SubscriptionDlg.New does, without the modal
                // edit dialog.
                var subscription = new SubscriptionHandle(session, "Test Subscription", ClientUtils.DefaultSubscriptionOptions);

                Assert.That(subscription.Create(), Is.Not.Null, "The subscription was not registered with the V2 engine.");

                // showing the dialog wires the monitored item grid and the notification lists
                // to the callbacks of the subscription.
                #pragma warning disable CA1849 // Justification: Show displays a modeless window, there is nothing to await.
                dialog.Show(subscription);
                #pragma warning restore CA1849

                var monitoredItems = (MonitoredItemConfigCtrl)WinFormsHarness.FindControl(dialog, "MonitoredItemsCTRL");

                Assert.That(monitoredItems, Is.Not.Null, "The dialog no longer has a 'MonitoredItemsCTRL' grid.");

                await monitoredItems.AddItemAsync(
                    new ReferenceDescription {
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                        NodeClass = NodeClass.Variable,
                        BrowseName = new QualifiedName(Opc.Ua.BrowseNames.CurrentTime),
                        DisplayName = new LocalizedText("CurrentTime"),
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IsForward = true,
                    },
                    ct).ConfigureAwait(true);

                await monitoredItems.ApplyChangesAsync(true, ct).ConfigureAwait(true);

                MonitoredItemHandle handle = subscription.Items.SingleOrDefault();

                Assert.That(handle, Is.Not.Null, "The control did not add a monitored item.");
                Assert.That(handle.Item, Is.Not.Null, "The control did not hand the monitored item to the engine.");
                Assert.That(
                    ServiceResult.IsGood(handle.Item.Error),
                    Is.True,
                    $"The server refused the monitored item: {handle.Item.Error}");

                // a data change has to reach the grid of the dialog through the notification
                // handler the subscription was created with.
                ListView dataChanges = await WaitForDataChangeAsync(dialog, ct).ConfigureAwait(true);

                Assert.That(
                    dataChanges,
                    Is.Not.Null,
                    "No data change reached the grid, so the notification handler of the dialog " +
                    "is not wired up to the subscription engine.");
            }
            finally
            {
                dialog.Close();
                await session.CloseAsync(ct).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Waits until a data change has been written into the grid of the dialog.
        /// </summary>
        /// <remarks>
        /// The designer fields are private, and reflection is used rather than widening the
        /// controls just for this test.
        /// </remarks>
        private static async Task<ListView> WaitForDataChangeAsync(SubscriptionDlg dialog, CancellationToken ct)
        {
            var dataChangesCtrl = (DataChangeNotificationListCtrl)WinFormsHarness.FindControl(dialog, "DataChangesCTRL");

            Assert.That(dataChangesCtrl, Is.Not.Null, "The dialog no longer has a 'DataChangesCTRL' list.");

            var listView = (ListView)WinFormsHarness.FindControl(dataChangesCtrl, "ItemsLV");

            Assert.That(listView, Is.Not.Null, "The list control no longer has an 'ItemsLV' view.");

            DateTime deadline = DateTime.UtcNow.AddSeconds(30);

            while (DateTime.UtcNow < deadline)
            {
                if (listView.Items.Count > 0)
                {
                    return listView;
                }

                await Task.Delay(100, ct).ConfigureAwait(true);
            }

            return null;
        }
    }
}
