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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Configuration;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// One sample client, and how to tell that a notification reached its display.
    /// </summary>
    public sealed class SubscribingClient
    {
        /// <summary>
        /// The name of the sample in the catalog.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// What the sample has to do after connecting before anything can arrive, or null
        /// if it subscribes on its own.
        /// </summary>
        public Func<Form, CancellationToken, Task> Arrange { get; init; }

        /// <summary>
        /// Returns true once a notification has reached the display of the sample.
        /// </summary>
        public Func<Form, bool> HasNotification { get; init; }

        /// <summary>
        /// What is missing when it never does.
        /// </summary>
        public string Expectation { get; init; }

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>
    /// Tier 2: every Workshop client which subscribes has to receive its notifications through
    /// the V2 subscription engine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SampleClientTests"/> proves that a sample connects and finishes its post
    /// connect logic. That is not enough for the samples which exist to show a subscription:
    /// the whole notification path of the V2 engine - the handler which is fixed when the
    /// subscription is created, the item identified by name rather than by a mutable object,
    /// the callback which arrives on a publish worker - can be wired up wrongly and still
    /// connect perfectly, leaving nothing but an empty window.
    /// </para>
    /// <para>
    /// Each case therefore connects the real main form to its own sample server and waits for
    /// a notification to show up where the sample displays it. Both engine APIs are covered:
    /// the callback based <c>ISubscriptionNotificationHandler</c> (Boiler, Methods,
    /// DataAccess, AlarmCondition) and the streaming <c>IStreamingSubscription</c>
    /// (SimpleEvents, HistoricalEvents).
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class WorkshopClientSubscriptionTests
    {
        /// <summary>
        /// How long a sample gets to start its server, connect and report a notification.
        /// </summary>
        private const int kTimeout = 120_000;

        /// <summary>
        /// How long a single service call of a sample client may take.
        /// </summary>
        private const int kOperationTimeout = 30_000;

        /// <summary>
        /// How long a notification may take to arrive once the client is connected.
        /// </summary>
        /// <remarks>
        /// The samples which display events wait for their server to produce one, and those
        /// run on their own simulation timers, so this is generous by design.
        /// </remarks>
        private static readonly TimeSpan s_notificationTimeout = TimeSpan.FromSeconds(45);

        public static IEnumerable<SubscribingClient> Clients => new[] {
            new SubscribingClient {
                Name = "Boiler",
                HasNotification = form => HasValue(form, "DrumLevelTB"),
                Expectation = "the drum level of the selected boiler",
            },
            new SubscribingClient {
                Name = "Methods",
                HasNotification = form => HasValue(form, "CurrentStateTB"),
                Expectation = "the current state of the process",
            },
            new SubscribingClient {
                Name = "DataAccess",
                Arrange = MonitorAValueAsync,
                HasNotification = form => HasSubItem(form, "MonitoredItemsLV", kDataAccessValueColumn),
                Expectation = "a value for the monitored item",
            },
            new SubscribingClient {
                Name = "AlarmCondition",
                Arrange = OpenTheAuditWindowAsync,
                HasNotification = form => HasRows(form, "ConditionsLV"),
                Expectation = "a condition of the server",
            },
            new SubscribingClient {
                Name = "SimpleEvents",
                HasNotification = form => HasRows(form, "EventsLV"),
                Expectation = "an event of the server",
            },
            new SubscribingClient {
                Name = "StateMachines",
                Arrange = PowerOnTheMachineAsync,
                HasNotification = form => HasRows(form, "TransitionsLV"),
                Expectation = "a transition of the state machine it powered on",
            },
            new SubscribingClient {
                Name = "HistoricalEvents",
                HasNotification = form => HasRows(FindEventList(form), "EventsLV"),
                Expectation = "a live event of the server",
            },
        };

        /// <summary>
        /// The column of the monitored item list of the DataAccess client which shows the value.
        /// </summary>
        private const int kDataAccessValueColumn = 5;

        [Test]
        [TestCaseSource(nameof(Clients))]
        [CancelAfter(kTimeout)]
        public async Task ClientReceivesItsNotifications(SubscribingClient client, CancellationToken ct)
        {
            SampleClientUnderTest sample = SampleClientFactories.All
                .Single(entry => entry.Sample.Name == client.Name);

            SampleServerUnderTest server = SampleServerFactories.All
                .Single(entry => entry.Sample.Name == client.Name);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(client.Name, server.Sample.ServerConfig, server.CreateServer, ct)
                .ConfigureAwait(false);

            await WinFormsHarness.RunAsync(
                async _ => await DriveClientAsync(sample, client, host.EndpointUrl, ct).ConfigureAwait(true),
                TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task DriveClientAsync(
            SampleClientUnderTest sample,
            SubscribingClient client,
            string endpointUrl,
            CancellationToken ct)
        {
            using var pki = new TemporaryPki($"client-subscribe-{client.Name}");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(sample.Sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            configuration.TransportQuotas.OperationTimeout = kOperationTimeout;

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            bool certificateOk = await application
                .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                .ConfigureAwait(true);

            Assert.That(certificateOk, Is.True, "The client certificate of the sample could not be created.");

            using Form form = sample.CreateMainForm(configuration, NullTelemetry.Instance);

            // create the window handles without ever showing the form: the samples marshal
            // their notification callbacks back to the UI thread and need one to do so
            CreateHandles(form);

            ConnectServerCtrl connect = WinFormsHarness.GetConnectControl(form);

            ISession session = await connect
                .ConnectAsync(NullTelemetry.Instance, endpointUrl, false, 30_000, ct)
                .ConfigureAwait(true);

            try
            {
                Assert.That(
                    session,
                    Is.InstanceOf<ManagedSession>(),
                    "The sample no longer connects with a managed session, so it is back to " +
                    "driving the reconnect itself.");

                Assert.That(
                    session.TryGetSubscriptionManager(out ISubscriptionManager _),
                    Is.True,
                    "The session of the sample does not run the V2 subscription engine.");

                if (client.Arrange != null)
                {
                    await client.Arrange(form, ct).ConfigureAwait(true);
                }

                bool arrived = await WaitAsync(() => client.HasNotification(form), ct).ConfigureAwait(true);

                Assert.That(
                    arrived,
                    Is.True,
                    $"The {client.Name} client never displayed {client.Expectation}, so its " +
                    "notifications do not reach the display.");
            }
            finally
            {
                await connect.DisconnectAsync(ct).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Subscribes the DataAccess client to a value.
        /// </summary>
        /// <remarks>
        /// The sample creates its monitored item from the browse tree, which means expanding
        /// the tree down to a variable first. The step this test is interested in is the one
        /// after that, so it calls the same method the menu handler calls.
        /// </remarks>
        private static async Task MonitorAValueAsync(Form form, CancellationToken ct)
        {
            MethodInfo create = form.GetType().GetMethod(
                "CreateMonitoredItemAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(
                create,
                Is.Not.Null,
                "The DataAccess client no longer has a 'CreateMonitoredItemAsync'. Rename it here too.");

            var pending = (Task)create.Invoke(
                form,
                new object[] { VariableIds.Server_ServerStatus_CurrentTime, "CurrentTime", ct });

            await pending.ConfigureAwait(true);
        }

        /// <summary>
        /// Opens the audit event window of the AlarmCondition client.
        /// </summary>
        /// <remarks>
        /// That window is the sample's streaming subscription: it starts an enumeration when
        /// it opens and ends it when it closes, which the main form does on disconnect. Only
        /// opening it is asserted here - the audit trail of the sample server needs a
        /// condition method call to fill, which this test does not make - but a window which
        /// cannot open, or cannot close its stream again, fails the test.
        /// </remarks>
        private static async Task OpenTheAuditWindowAsync(Form form, CancellationToken ct)
        {
            MethodInfo open = form.GetType().GetMethod(
                "View_AuditEventsMI_ClickAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(
                open,
                Is.Not.Null,
                "The AlarmCondition client no longer has a 'View_AuditEventsMI_ClickAsync'. Rename it here too.");

            // the handler is an async void event handler, so there is nothing to await
            open.Invoke(form, new object[] { null, EventArgs.Empty });

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(true);
        }

        /// <summary>
        /// Powers the state machine of the StateMachines client on.
        /// </summary>
        /// <remarks>
        /// That sample streams the transitions of a state machine, and a state machine which
        /// nobody drives never has one: the client has to cause a transition before there is
        /// anything for the stream to report.
        /// </remarks>
        private static async Task PowerOnTheMachineAsync(Form form, CancellationToken ct)
        {
            var powerOn = (Button)WinFormsHarness.FindControl(form, "PowerOnBTN");

            Assert.That(
                powerOn,
                Is.Not.Null,
                "The StateMachines client no longer has a 'PowerOnBTN'. Rename it here too.");

            Assert.That(
                powerOn.Enabled,
                Is.True,
                "The StateMachines client did not enable its causes, so it never resolved " +
                "the state machines of its server.");

            // the click handler is an async void event handler, so there is nothing to await
            powerOn.PerformClick();

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(true);
        }

        /// <summary>
        /// Returns the event list control of the HistoricalEvents client, which holds the list
        /// view the events are written into.
        /// </summary>
        private static Control FindEventList(Form form)
        {
            return WinFormsHarness.FindControl(form, "EventsLV");
        }

        /// <summary>
        /// Returns true once a field shows something other than the placeholder.
        /// </summary>
        private static bool HasValue(Control parent, string name)
        {
            Control field = WinFormsHarness.FindControl(parent, name);

            Assert.That(field, Is.Not.Null, $"The sample no longer has a '{name}' field.");

            return !string.IsNullOrEmpty(field.Text) && field.Text != "---";
        }

        /// <summary>
        /// Creates the window handles of the whole form.
        /// </summary>
        /// <remarks>
        /// The samples marshal their notification callbacks to the UI thread and skip that
        /// while there is no window. A form which is never shown creates a handle only for
        /// itself, so the controls the notifications are written into need one of their own.
        /// </remarks>
        private static void CreateHandles(Control parent)
        {
            _ = parent.Handle;

            foreach (Control child in parent.Controls)
            {
                CreateHandles(child);
            }
        }

        /// <summary>
        /// Returns true once a list view has at least one entry.
        /// </summary>
        private static bool HasRows(Control parent, string name)
        {
            var list = (ListView)WinFormsHarness.FindControl(parent, name);

            Assert.That(list, Is.Not.Null, $"The sample no longer has a '{name}' list view.");

            return list.Items.Count > 0;
        }

        /// <summary>
        /// Returns true once a list view has an entry which carries a value in the column.
        /// </summary>
        private static bool HasSubItem(Control parent, string name, int column)
        {
            var list = (ListView)WinFormsHarness.FindControl(parent, name);

            Assert.That(list, Is.Not.Null, $"The sample no longer has a '{name}' list view.");

            return list.Items.Count > 0
                && list.Items[0].SubItems.Count > column
                && !string.IsNullOrEmpty(list.Items[0].SubItems[column].Text);
        }

        /// <summary>
        /// Waits for a condition while the message loop keeps pumping.
        /// </summary>
        private static async Task<bool> WaitAsync(Func<bool> condition, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.Add(s_notificationTimeout);

            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(100, ct).ConfigureAwait(true);
            }

            return condition();
        }
    }
}
