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
        /// How long the post connect logic of a sample gets to run before it is inspected.
        /// </summary>
        /// <remarks>
        /// The samples handle ConnectComplete in an async void handler, so there is nothing
        /// to await. The message loop keeps pumping during this delay, which is also what
        /// gives the watchdog the chance to see a late error dialog.
        /// </remarks>
        private static readonly TimeSpan s_postConnectGrace = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Clients whose sample server does not start, so the client cannot be tested either.
        /// </summary>
        /// <remarks>
        /// These mirror the server side known issues in <c>SampleServerTests</c>. As with
        /// those, the test fails when a listed sample starts working, so the list cannot rot.
        /// </remarks>
        private static readonly IReadOnlyDictionary<string, string> s_knownIssues =
            new Dictionary<string, string>(StringComparer.Ordinal) {
                ["HistoricalAccess"] = "the HistoricalAccess sample server does not start",
                ["HistoricalEvents"] = "the HistoricalEvents sample server does not start",
            };

        // DataAccess is deliberately not listed: its server defect only shows when the
        // Objects folder is browsed, the client connects and enables its tree just fine.

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
            // the sample client discovers its server instead of connecting to a fixed
            // endpoint, and the GDS and aggregation clients need more than one server;
            // both follow in phase 4 of docs/TESTING.md
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
                    _ => {
                        using var dialog = new Form { Text = "Sample complaint" };

                        dialog.Controls.Add(new Label { Text = "something went wrong" });

                        // blocks the message loop until the watchdog closes it, which is
                        // exactly what a sample error dialog does in a test run
                        dialog.ShowDialog();

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

        private static async Task SmokeTestAsync(SampleClientUnderTest client, CancellationToken ct)
        {
            SampleServerUnderTest server = SampleServerFactories.All
                .Single(entry => entry.Sample.Name == client.Sample.Name);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(client.Sample.Name, server.Sample.ServerConfig, server.CreateServer, ct)
                .ConfigureAwait(false);

            await WinFormsHarness.RunAsync(
                async _ => await DriveClientAsync(client, host.EndpointUrl, ct).ConfigureAwait(true),
                TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task DriveClientAsync(SampleClientUnderTest client, string endpointUrl, CancellationToken ct)
        {
            using var pki = new TemporaryPki($"client-{client.Sample.Name}");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(client.Sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            bool certificateOk = await application
                .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                .ConfigureAwait(true);

            Assert.That(certificateOk, Is.True, "The client certificate of the sample could not be created.");

            using Form form = client.CreateMainForm(configuration, NullTelemetry.Instance);

            // create the window handle without ever showing the form: the sample marshals its
            // connect callbacks back to the UI thread and needs a handle to do so
            _ = form.Handle;

            ConnectServerCtrl connect = WinFormsHarness.GetConnectControl(form);

            ISession session = await connect
                .ConnectAsync(NullTelemetry.Instance, endpointUrl, false, 30_000, ct)
                .ConfigureAwait(true);

            Assert.That(session, Is.Not.Null, "The sample client did not create a session.");
            Assert.That(session.Connected, Is.True, "The session of the sample client is not connected.");
            Assert.That(connect.Session, Is.SameAs(session), "The connect control did not keep the session.");

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

            await connect.DisconnectAsync(ct).ConfigureAwait(true);

            Assert.That(connect.Session, Is.Null, "The sample client did not release its session on disconnect.");
        }
    }
}
