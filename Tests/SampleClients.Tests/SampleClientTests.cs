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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 2: every WinForms sample client has to build its main form, connect to its own
    /// sample server and run its post connect logic without complaining.
    /// </summary>
    /// <remarks>
    /// No window is ever shown. The form is created on an STA thread with a running message
    /// loop, the shared connect control is driven directly, and a watchdog turns any modal
    /// dialog the sample opens into a test failure instead of a hang.
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class SampleClientTests
    {
        /// <summary>
        /// How long a sample gets to start its server, connect and disconnect again.
        /// </summary>
        private const int kTimeout = 90_000;

        /// <summary>
        /// How long a single service call of a sample client may take.
        /// </summary>
        /// <remarks>
        /// The sample configurations allow ten minutes, which is longer than this whole
        /// test may run: a request which never came back used to surface as a bare harness
        /// timeout with nothing to point at. This cap is far above what any of these
        /// operations need against a server on loopback - the slowest single call measured
        /// is a few hundred milliseconds - so it only ever fires for a call which is stuck,
        /// and then the test reports that call instead of the clock.
        /// </remarks>
        private const int kOperationTimeout = 30_000;

        /// <summary>
        /// How long the post connect logic of a sample gets to run before it is inspected.
        /// </summary>
        /// <remarks>
        /// The samples handle ConnectComplete in an async void handler, so there is nothing
        /// to await. The message loop keeps pumping during this delay, which is also what
        /// gives the watchdog the chance to see a late error dialog.
        /// </remarks>
        private static readonly TimeSpan s_postConnectGrace = TimeSpan.FromSeconds(2);

        /// <summary>
        /// How long the user interface of a sample gets to react after a disconnect.
        /// </summary>
        /// <remarks>
        /// Generous on purpose: this tells a message loop which still runs apart from one
        /// which is deadlocked, it does not measure how quick a sample is. A sample which
        /// handles ConnectComplete with an await of its own needs a few messages to get
        /// through it.
        /// </remarks>
        private static readonly TimeSpan s_responsiveWithin = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Clients whose sample server does not start, so the client cannot be tested either.
        /// </summary>
        /// <remarks>
        /// These mirror the server side known issues in <c>SampleServerTests</c>. As with
        /// those, the test fails when a listed sample starts working, so the list cannot rot.
        /// </remarks>
        private static readonly IReadOnlyDictionary<string, string> s_knownIssues =
            new Dictionary<string, string>(StringComparer.Ordinal) {
                // empty on purpose: every sample client this tier covers works. The two
                // entries this list ever had - the HistoricalEvents client and the
                // AlarmCondition client - were both fixed rather than parked here.
            };

        public static IEnumerable<SampleClientUnderTest> Clients => SampleClientFactories.All;

        [Test]
        [TestCaseSource(nameof(Clients))]
        [CancelAfter(kTimeout)]
        public async Task ClientConnectsToItsSampleServer(SampleClientUnderTest client, CancellationToken ct)
        {
            Exception failure = null;

            try
            {
                await SmokeTestAsync(client, ct).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (s_knownIssues.TryGetValue(client.Sample.Name, out string issue))
            {
                Assert.That(
                    failure,
                    Is.Not.Null,
                    $"{client.Sample.Name} is listed as a known issue, but the smoke test passed. " +
                    "Remove the entry from s_knownIssues and from docs/TESTING.md.");

                Assert.Ignore($"{client.Sample.Name}: known issue - {issue}. The test reported: {failure.Message}");
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        /// <summary>
        /// A sample client which no test drives has to be declared, so it cannot drop out
        /// unnoticed.
        /// </summary>
        [Test]
        public void UncoveredClientSamplesAreDeclared()
        {
            // the UA Sample Client has no shared connect control and opens its session
            // through a modal dialog, so it cannot be driven by the loop above - it has its
            // own fixture in SampleClientFormTests instead. The GDS client hosts no connect
            // control either and needs two servers, so it has one in GdsClientTests. The
            // aggregation client also needs more than one server and is still open.
            string[] expectedGaps = ["Aggregation", "Gds", "Sample"];

            IEnumerable<string> uncovered = SampleCatalog.Clients
                .Select(sample => sample.Name)
                .Except(SampleClientFactories.CoveredSamples);

            Assert.That(
                uncovered,
                Is.EquivalentTo(expectedGaps),
                "A sample client without a factory is not driven by any test. Add it to " +
                "SampleClientFactories, or to the list of known gaps here.");
        }

        /// <summary>
        /// Guards the guard: a modal dialog has to be closed and reported, otherwise every
        /// client test above would hang instead of failing the day a sample starts
        /// complaining about something.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public void WatchdogTurnsAModalDialogIntoAFailure()
        {
            Assert.That(
                async () => await WinFormsHarness.RunAsync(
                    watchdog => {
                        using var dialog = new Form { Text = "Sample complaint" };

                        dialog.Controls.Add(new Label { Text = "something went wrong" });

                        // blocks the message loop until the watchdog closes it, which is
                        // exactly what a sample error dialog does in a test run
#pragma warning disable CA1849 // Justification: blocking the message loop is what this test exercises.
                        DialogResult ignored = dialog.ShowDialog();
#pragma warning restore CA1849

                        return Task.CompletedTask;
                    },
                    TimeSpan.FromSeconds(20)).ConfigureAwait(false),
                Throws.InstanceOf<InvalidOperationException>()
                    .With.Message.Contains("something went wrong"),
                "The watchdog did not close and report a modal dialog.");
        }

        [Test]
        public void KnownIssuesReferenceCoveredSamples()
        {
            Assert.That(
                s_knownIssues.Keys.Except(SampleClientFactories.CoveredSamples),
                Is.Empty,
                "A known issue for a sample which no test drives is dead weight.");
        }

        /// <summary>
        /// What a user does: click Server, Disconnect.
        /// </summary>
        /// <remarks>
        /// The loop above drives the connect control directly, which is the part every sample
        /// shares. This goes through the menu item of one of them instead, so that the click
        /// handler of the sample - which disposes its subscription first and only then asks
        /// the control to disconnect - is on the path as well.
        ///
        /// The window is not shown here either. What froze was the message loop, not the
        /// painting: a callback posted to a loop which is blocked is never dispatched whether
        /// there is a window on screen or not, and AssertMessageLoopRunsAsync asks exactly
        /// that. Unlike Button.PerformClick, ToolStripItem.PerformClick is not gated on
        /// CanSelect, so the menu item can be clicked on a form which was never shown.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DisconnectMenuItemLeavesTheWindowReacting(CancellationToken ct)
        {
            SampleClientUnderTest client = SampleClientFactories.All
                .Single(entry => entry.Sample.Name == "Boiler");

            SampleServerUnderTest server = SampleServerFactories.All
                .Single(entry => entry.Sample.Name == client.Sample.Name);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(client.Sample.Name, server.Sample.ServerConfig, server.CreateServer, ct)
                .ConfigureAwait(false);

            await WinFormsHarness.RunAsync(
                _ => ClickDisconnectAsync(client, host.EndpointUrl, ct),
                TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task ClickDisconnectAsync(
            SampleClientUnderTest client,
            string endpointUrl,
            CancellationToken ct)
        {
            using var pki = new TemporaryPki($"menu-{client.Sample.Name}");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(client.Sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            configuration.TransportQuotas.OperationTimeout = kOperationTimeout;

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            Assert.That(
                await application.CheckApplicationInstanceCertificatesAsync(true, null, ct).ConfigureAwait(true),
                Is.True,
                "The client certificate of the sample could not be created.");

            using Form form = client.CreateMainForm(configuration, NullTelemetry.Instance);

            // as in the loop above: a handle without a window, so the sample can marshal its
            // callbacks back and the test can post to the message loop
            _ = form.Handle;

            ConnectServerCtrl connect = WinFormsHarness.GetConnectControl(form);

            ISession session = await connect
                .ConnectAsync(NullTelemetry.Instance, endpointUrl, false, 30_000, ct)
                .ConfigureAwait(true);

            Assert.That(session, Is.Not.Null, "The sample client did not create a session.");

            await Task.Delay(s_postConnectGrace, ct).ConfigureAwait(true);

            var disconnect = WinFormsHarness.FindField<ToolStripMenuItem>(form, "Server_DisconnectMI");

            Assert.That(disconnect, Is.Not.Null, "The sample has no 'Server_DisconnectMI' menu item to click.");

            // what the menu does. The handler of the sample is async void, so this comes back
            // at its first await and everything below is what the click actually led to
            disconnect.PerformClick();

            await AssertMessageLoopRunsAsync(form, ct).ConfigureAwait(true);

            Assert.That(
                await WaitUntilAsync(() => connect.Session == null, ct).ConfigureAwait(true),
                Is.True,
                "Clicking Disconnect did not release the session of the sample client.");

            Control display = WinFormsHarness.FindControl(form, client.EnabledAfterConnect);

            Assert.That(
                await WaitUntilAsync(() => !display.Enabled, ct).ConfigureAwait(true),
                Is.True,
                $"Clicking Disconnect left '{client.EnabledAfterConnect}' enabled, so the sample never " +
                "cleared its display: the connect control did not report the disconnect.");

            form.Close();
        }

        private static async Task SmokeTestAsync(SampleClientUnderTest client, CancellationToken ct)
        {
            SampleServerUnderTest server = SampleServerFactories.All
                .Single(entry => entry.Sample.Name == client.Sample.Name);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(client.Sample.Name, server.Sample.ServerConfig, server.CreateServer, ct)
                .ConfigureAwait(false);

            DialogWatchdog watchdog = null;
            var phase = new ClientPhase();

            try
            {
                await WinFormsHarness.RunAsync(
                    async dialogs => {
                        watchdog = dialogs;

                        await DriveClientAsync(client, host.EndpointUrl, phase, ct).ConfigureAwait(true);
                    },
                    TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(15))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException expired)
            {
                throw phase.Explain(expired);
            }

            if (watchdog?.DuringTeardown.Count > 0)
            {
                await TestContext.Out.WriteLineAsync(
                    $"{client.Sample.Name}: while the form was being disposed - " +
                    string.Join("; ", watchdog.DuringTeardown))
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task DriveClientAsync(
            SampleClientUnderTest client,
            string endpointUrl,
            ClientPhase phase,
            CancellationToken ct)
        {
            phase.Enter("loading the configuration of the sample");

            using var pki = new TemporaryPki($"client-{client.Sample.Name}");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(client.Sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            configuration.TransportQuotas.OperationTimeout = kOperationTimeout;

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            phase.Enter("creating its client certificate");

            bool certificateOk = await application
                .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                .ConfigureAwait(true);

            Assert.That(certificateOk, Is.True, "The client certificate of the sample could not be created.");

            phase.Enter("building its main form");

            using Form form = client.CreateMainForm(configuration, NullTelemetry.Instance);

            // create the window handle without ever showing the form: the sample marshals its
            // connect callbacks back to the UI thread and needs a handle to do so
            _ = form.Handle;

            ConnectServerCtrl connect = WinFormsHarness.GetConnectControl(form);

            phase.Enter($"connecting to {endpointUrl}");

            ISession session = await connect
                .ConnectAsync(NullTelemetry.Instance, endpointUrl, false, 30_000, ct)
                .ConfigureAwait(true);

            Assert.That(session, Is.Not.Null, "The sample client did not create a session.");
            Assert.That(session.Connected, Is.True, "The session of the sample client is not connected.");
            Assert.That(connect.Session, Is.SameAs(session), "The connect control did not keep the session.");

            phase.Enter("running its post connect logic");

            // let the post connect logic of the sample run, and give the watchdog a chance to
            // catch a dialog it opens while doing so
            await Task.Delay(s_postConnectGrace, ct).ConfigureAwait(true);

            if (client.EnabledAfterConnect != null)
            {
                Control control = WinFormsHarness.FindControl(form, client.EnabledAfterConnect);

                Assert.That(
                    control,
                    Is.Not.Null,
                    $"The form has no control '{client.EnabledAfterConnect}'. Update the entry in SampleClientFactories.");

                Assert.That(
                    control.Enabled,
                    Is.True,
                    $"The sample did not enable '{client.EnabledAfterConnect}' after connecting, " +
                    "so its post connect logic did not complete.");
            }

            phase.Enter("disconnecting");

            // the synchronous entry point on purpose. It is the one the Disconnect menu item
            // and the FormClosing handler of every sample use, and waiting there for a close
            // which needs this very thread to finish deadlocked the message loop: the window
            // disconnected and then froze. The asynchronous entry point never had that
            // problem and is covered by the fixtures which drive a single client.
            connect.Disconnect();

            Assert.That(connect.Session, Is.Null, "The sample client did not release its session on disconnect.");

            phase.Enter("checking that its window still reacts");

            await AssertMessageLoopRunsAsync(form, ct).ConfigureAwait(true);

            if (client.EnabledAfterConnect != null)
            {
                Control control = WinFormsHarness.FindControl(form, client.EnabledAfterConnect);

                // the samples clear their display from ConnectComplete, so the control going
                // disabled again is what proves the disconnect got all the way through and
                // did not stop at the close. A sample which handles the event with an await
                // of its own gets there a message later, hence the wait rather than a look.
                Assert.That(
                    await WaitUntilAsync(() => !control.Enabled, ct).ConfigureAwait(true),
                    Is.True,
                    $"The sample did not disable '{client.EnabledAfterConnect}' again after disconnecting, " +
                    "so the connect control never reported the disconnect and the display still shows a " +
                    "session which is gone.");
            }

            phase.Enter("closing its main form");

            // the other caller of the synchronous disconnect, and the one a user reaches by
            // closing the window. FormClosing cannot await, so everything a sample tears down
            // there has to come back without blocking this thread. If it does not, this call
            // never returns and the harness reports the timeout against this phase.
            form.Close();

            phase.Enter("shutting down");
        }

        /// <summary>
        /// Checks that the message loop of the sample still dispatches what is posted to it,
        /// which is what a user sees as a window that still reacts.
        /// </summary>
        private static async Task AssertMessageLoopRunsAsync(Form form, CancellationToken ct)
        {
            var pumped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            form.BeginInvoke(new Action(() => pumped.TrySetResult(true)));

            Task finished = await Task
                .WhenAny(pumped.Task, Task.Delay(s_responsiveWithin, ct))
                .ConfigureAwait(true);

            Assert.That(
                finished,
                Is.SameAs(pumped.Task),
                "The message loop of the sample did not dispatch a posted callback within " +
                $"{s_responsiveWithin.TotalSeconds:F0} seconds, so its window is frozen.");
        }

        /// <summary>
        /// Waits for a condition of the user interface, letting the message loop run while
        /// it does.
        /// </summary>
        private static async Task<bool> WaitUntilAsync(Func<bool> condition, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + s_responsiveWithin;

            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    return false;
                }

                await Task.Delay(50, ct).ConfigureAwait(true);
            }

            return true;
        }
    }
}
