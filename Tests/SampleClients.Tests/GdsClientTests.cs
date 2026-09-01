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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Configuration;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Client.Controls;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 2: the Global Discovery Client has to talk to the GDS and to a managed server over
    /// managed sessions, and to show the status of the managed server through the V2
    /// subscription engine.
    /// </summary>
    /// <remarks>
    /// This is the client the generic smoke test cannot drive: it hosts no shared connect
    /// control and it needs two servers - the global discovery server it registers with, and
    /// the server whose certificates it pushes or pulls. "Gds" is therefore a declared gap in
    /// <see cref="SampleClientTests"/> and is covered here instead.
    /// <para>
    /// The two clients the form keeps are driven directly rather than through the dialogs the
    /// user would open, which is what <c>SelectGdsDialog</c> and <c>SelectServerDialog</c> do
    /// with the very same objects: set the credentials, then connect. What is asserted is what
    /// the sample owns - which kind of session its clients opened, that the directory answers
    /// over it, and that its status panel fills itself from a subscription.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class GdsClientTests
    {
        private const int kTimeout = 180_000;

        /// <summary>
        /// The console variant of the global discovery server is the one which starts without
        /// a user interface, and is what tier 1 covers as well.
        /// </summary>
        private const string kGdsSample = "GdsConsole";

        /// <summary>
        /// The catalog entry of the client under test.
        /// </summary>
        private const string kClientSample = "Gds";

        /// <summary>
        /// The server the client points at as the one it manages. Any sample server serves
        /// what the status panel reads; the reference server is the one a user would pick.
        /// </summary>
        private const string kManagedServerSample = "Reference";

        /// <summary>
        /// One of the standard users the sample global discovery server creates on first
        /// start, and the one its own README tells a user to log in with.
        /// </summary>
        private const string kGdsAdminUser = "appadmin";

        private const string kGdsAdminPassword = "demo";

        private static readonly TimeSpan s_startupTimeout = TimeSpan.FromSeconds(60);

        [Test]
        [CancelAfter(kTimeout)]
        public async Task GdsClientConnectsWithManagedSessionsAndShowsTheServerStatus(CancellationToken ct)
        {
            SampleDefinition gdsSample = SampleCatalog.All.Single(entry => entry.Name == kGdsSample);
            SampleDefinition clientSample = SampleCatalog.All.Single(entry => entry.Name == kClientSample);
            SampleServerUnderTest managed = SampleServerFactories.All
                .Single(entry => entry.Sample.Name == kManagedServerSample);

            // create the certificate of the global discovery server before it starts. On a
            // machine which has never run it the server would otherwise create it while
            // starting up, and the first run would differ from every later one.
            await SampleCertificates
                .EnsureApplicationCertificateAsync(gdsSample.ServerConfig, ct)
                .ConfigureAwait(false);

            await using ConsoleSampleProcess gds = await ConsoleSampleProcess.StartAsync(
                gdsSample.ServerProject,
                "NetCoreGlobalDiscoveryServer",
                "Server started",
                s_startupTimeout,
                quitCommand: "quit",
                ct: ct).ConfigureAwait(false);

            await using SampleServerHost managedServer = await SampleServerHost
                .StartAsync(kManagedServerSample, managed.Sample.ServerConfig, managed.CreateServer, ct)
                .ConfigureAwait(false);

            var phase = new ClientPhase();

            try
            {
                await WinFormsHarness.RunAsync(
                    async watchdog => await DriveClientAsync(
                        clientSample,
                        gdsSample.ServerUrl,
                        managedServer.EndpointUrl,
                        watchdog,
                        phase,
                        ct).ConfigureAwait(true),
                    TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException expired)
            {
                await ReportServerOutputAsync(gds).ConfigureAwait(false);

                throw phase.Explain(expired);
            }
            catch (Exception)
            {
                await ReportServerOutputAsync(gds).ConfigureAwait(false);

                throw;
            }
        }

        /// <summary>
        /// Writes what the global discovery server made of the client, which is only visible
        /// in its own console output.
        /// </summary>
        private static Task ReportServerOutputAsync(ConsoleSampleProcess gds)
        {
            return TestContext.Out.WriteLineAsync($"GdsConsole output:{Environment.NewLine}{gds.Output}");
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task DriveClientAsync(
            SampleDefinition clientSample,
            string gdsUrl,
            string managedServerUrl,
            DialogWatchdog watchdog,
            ClientPhase phase,
            CancellationToken ct)
        {
            phase.Enter("loading the configuration of the sample");

            using var pki = new TemporaryPki("client-gds");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(clientSample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            phase.Enter("creating its client certificate");

            bool certificateOk = await application
                .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                .ConfigureAwait(true);

            Assert.That(certificateOk, Is.True, "The client certificate of the sample could not be created.");

            phase.Enter("composing the sample");

            // the sample does not construct its clients: Program.cs registers them through
            // GdsClientServiceCollectionExtensions and lets the container build the form. The
            // test composes it the same way, so what is under test is the sample's own wiring
            // and not a second copy of it written here.
            var services = new ServiceCollection();

            services.AddSingleton<ITelemetryContext>(NullTelemetry.Instance);
            services.AddSingleton(application);
            services.AddSingleton(configuration);
            services.AddGlobalDiscoveryClient();

            await using ServiceProvider provider = services.BuildServiceProvider();

            phase.Enter("building its main form");

            using var form = ActivatorUtilities.CreateInstance<Opc.Ua.Gds.Client.MainForm>(provider);

            // create the window handle without ever showing the form: the sample marshals its
            // notifications back to the UI thread and needs a window to do so
            _ = form.Handle;

            var gdsClient = provider.GetRequiredService<GlobalDiscoveryServerClient>();
            var pushClient = provider.GetRequiredService<ServerPushConfigurationClient>();
            var statusPanel = (ServerStatusControl)WinFormsHarness.FindControl(form, "ServerStatusPanel");

            Assert.That(
                WinFormsHarness.FindField<GlobalDiscoveryServerClient>(form, "m_gds"),
                Is.SameAs(gdsClient),
                "The form did not take the global discovery client the container composed.");

            Assert.That(
                WinFormsHarness.FindField<ServerPushConfigurationClient>(form, "m_server"),
                Is.SameAs(pushClient),
                "The form did not take the push client the container composed.");

            Assert.That(statusPanel, Is.Not.Null, "The form has no 'ServerStatusPanel'.");

            // the GDS client manages servers it has no trust relationship with yet, so it
            // shows the certificate of every server it meets and asks the user to accept it.
            // Its own hook replaces the AutoAcceptUntrustedCertificates of the test PKI - the
            // callback wins over the flag - so the dialog has to be answered rather than
            // suppressed. Everything else the sample opens is still a failure.
            watchdog.Accept<UntrustedCertificateDialog>("OkButton");

            try
            {
                await ConnectToTheDirectoryAsync(gdsClient, gdsUrl, configuration, phase, ct).ConfigureAwait(true);
                await ShowTheStatusOfTheManagedServerAsync(pushClient, statusPanel, managedServerUrl, phase, ct)
                    .ConfigureAwait(true);
            }
            finally
            {
                phase.Enter("disconnecting");

                await statusPanel.InitializeAsync(null, NullTelemetry.Instance, CancellationToken.None)
                    .ConfigureAwait(true);
                await pushClient.DisconnectAsync(CancellationToken.None).ConfigureAwait(true);
                await gdsClient.DisconnectAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Connects to the global discovery server and registers, finds and unregisters the
        /// client itself, which is the pull registration the sample offers for a client.
        /// </summary>
        private static async Task ConnectToTheDirectoryAsync(
            GlobalDiscoveryServerClient gdsClient,
            string gdsUrl,
            ApplicationConfiguration configuration,
            ClientPhase phase,
            CancellationToken ct)
        {
            phase.Enter($"connecting to the global discovery server at {gdsUrl}");

            // the same two steps the select GDS dialog of the sample performs
            gdsClient.AdminCredentials = new UserIdentity(kGdsAdminUser, Encoding.UTF8.GetBytes(kGdsAdminPassword));

            await gdsClient.ConnectAsync(gdsUrl, ct).ConfigureAwait(true);

            Assert.That(gdsClient.IsConnected, Is.True, "The client did not connect to the global discovery server.");

            Assert.That(
                gdsClient.Session,
                Is.InstanceOf<ManagedSession>(),
                "The client no longer opens a managed session to the global discovery server, " +
                "so nothing drives its reconnect.");

            Assert.That(
                gdsClient.Session.TryGetSubscriptionManager(out ISubscriptionManager manager),
                Is.True,
                "The session to the global discovery server does not run the V2 subscription engine.");

            Assert.That(manager, Is.Not.Null, "The session reported a V2 engine without a manager.");

            phase.Enter("registering itself with the global discovery server");

            // the sample global discovery server keeps its registrations in a database under
            // %LocalApplicationData%, so a run which failed before it could unregister would
            // otherwise leave this test looking at the record of the previous one.
            await UnregisterEveryRecordOfAsync(gdsClient, configuration.ApplicationUri, ct).ConfigureAwait(true);

            var record = new ApplicationRecordDataType {
                ApplicationUri = configuration.ApplicationUri,
                ApplicationType = ApplicationType.Client,
                ApplicationNames = new LocalizedText[] { new LocalizedText(configuration.ApplicationName) },
                ProductUri = configuration.ProductUri,
            };

            NodeId applicationId = await gdsClient.RegisterApplicationAsync(record, ct).ConfigureAwait(true);

            Assert.That(
                applicationId,
                Is.Not.EqualTo(NodeId.Null),
                "The global discovery server did not return an application id for the registration.");

            try
            {
                phase.Enter("reading its own registration back");

                ArrayOf<ApplicationRecordDataType> found = await gdsClient
                    .FindApplicationAsync(configuration.ApplicationUri, ct)
                    .ConfigureAwait(true);

                Assert.That(
                    found.ToArray().Select(entry => entry.ApplicationUri).ToArray(),
                    Has.Some.EqualTo(configuration.ApplicationUri),
                    "The directory does not report the application the client just registered.");
            }
            finally
            {
                phase.Enter("unregistering itself again");

                await gdsClient.UnregisterApplicationAsync(applicationId, CancellationToken.None)
                    .ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Removes every registration the directory still holds for the application uri.
        /// </summary>
        private static async Task UnregisterEveryRecordOfAsync(
            GlobalDiscoveryServerClient gdsClient,
            string applicationUri,
            CancellationToken ct)
        {
            ArrayOf<ApplicationRecordDataType> existing = await gdsClient
                .FindApplicationAsync(applicationUri, ct)
                .ConfigureAwait(true);

            foreach (ApplicationRecordDataType stale in existing.ToArray())
            {
                await gdsClient.UnregisterApplicationAsync(stale.ApplicationId, ct).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Connects the push client to the managed server and waits for the status panel to
        /// fill itself, which only a working subscription can do.
        /// </summary>
        private static async Task ShowTheStatusOfTheManagedServerAsync(
            ServerPushConfigurationClient pushClient,
            ServerStatusControl statusPanel,
            string managedServerUrl,
            ClientPhase phase,
            CancellationToken ct)
        {
            phase.Enter($"connecting to the managed server at {managedServerUrl}");

            await pushClient.ConnectAsync(managedServerUrl, ct).ConfigureAwait(true);

            Assert.That(pushClient.IsConnected, Is.True, "The client did not connect to the managed server.");

            Assert.That(
                pushClient.Session,
                Is.InstanceOf<ManagedSession>(),
                "The client no longer opens a managed session to the managed server, so nothing " +
                "drives its reconnect.");

            Assert.That(
                pushClient.Session.TryGetSubscriptionManager(out ISubscriptionManager manager),
                Is.True,
                "The session to the managed server does not run the V2 subscription engine, so " +
                "the status panel has nothing to monitor with.");

            Assert.That(manager, Is.Not.Null, "The session reported a V2 engine without a manager.");

            phase.Enter("showing the status of the managed server");

            await statusPanel.InitializeAsync(pushClient, NullTelemetry.Instance, ct).ConfigureAwait(true);

            var state = (Label)WinFormsHarness.FindControl(statusPanel, "StateTextBox");

            Assert.That(state, Is.Not.Null, "The status panel has no 'StateTextBox'.");

            // the panel starts at its placeholder and is filled by the first data change of the
            // subscription it created, so this is the assertion the migration is about: before
            // it, the status arrived through an event the SDK declares but never raises.
            string reported = await Poll.UntilAsync(
                _ => Task.FromResult(state.Text),
                text => !string.IsNullOrEmpty(text) && text != "---",
                "the status panel to be filled by a notification of the V2 subscription engine",
                TimeSpan.FromSeconds(30),
                ct: ct).ConfigureAwait(true);

            await TestContext.Out.WriteLineAsync($"Gds: the managed server reports {reported}")
                .ConfigureAwait(true);

            Assert.That(
                reported,
                Is.EqualTo(ServerState.Running.ToString()),
                "The status panel does not show the state the managed server is in.");
        }
    }
}
