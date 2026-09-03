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

        /// <summary>
        /// The user the sample global discovery server gives the SecurityAdmin and
        /// ConfigureAdmin Roles to, and the only one its <c>ServerConfiguration</c> nodes are
        /// visible to.
        /// </summary>
        private const string kGdsSystemAdminUser = "sysadmin";

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
                .StartAsync(kManagedServerSample, managed.Sample.ServerConfig, managed.ConfigureServices, ct)
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

            phase.Enter("building the dialogs the sample opens on demand");

            // the two v1.05.07 dialogs are not reached by the walk-through below - they are
            // modal and would stall the harness - so they are at least built here. That runs
            // their InitializeComponent, which is where a designer mistake shows up.
            using (var certificates = new CertificateManagementDialog())
            using (var onboarding = new DeviceOnboardingDialog())
            {
                _ = certificates.Handle;
                _ = onboarding.Handle;
            }

            // the GDS client manages servers it has no trust relationship with yet, so it
            // shows the certificate of every server it meets and asks the user to accept it.
            // Its own hook replaces the AutoAcceptUntrustedCertificates of the test PKI - the
            // callback wins over the flag - so the dialog has to be answered rather than
            // suppressed. Everything else the sample opens is still a failure.
            watchdog.Accept<UntrustedCertificateDialog>("OkButton");

            try
            {
                await ConnectToTheDirectoryAsync(gdsClient, gdsUrl, configuration, phase, ct).ConfigureAwait(true);
                await ReadTheOptionalServerConfigurationSurfaceAsync(gdsClient, gdsUrl, phase, ct)
                    .ConfigureAwait(true);
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

                phase.Enter("listing the certificates the directory holds for the registration");

                // OPC 10000-12 §7.9.8: the pull model gained a GetCertificates Method, so a
                // client no longer has to guess what the GDS has issued for an application.
                // A fresh registration has none yet - what is under test is that the sample
                // reaches the Method at all and that the two arrays come back aligned.
                (ArrayOf<NodeId> certificateTypeIds, ArrayOf<ByteString> certificates) =
                    await gdsClient.GetCertificatesAsync(applicationId, NodeId.Null, ct)
                        .ConfigureAwait(true);

                Assert.That(
                    certificateTypeIds.Count,
                    Is.EqualTo(certificates.Count),
                    "GetCertificates returned the certificate types and the certificates ragged.");

                await RegisterAnOnboardingTicketAsync(gdsClient, phase, ct).ConfigureAwait(true);
            }
            finally
            {
                phase.Enter("unregistering itself again");

                await gdsClient.UnregisterApplicationAsync(applicationId, CancellationToken.None)
                    .ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Reads the Optional OPC 10000-12 §7.10.3 / §7.10.13 / §7.10.20
        /// <c>ServerConfiguration</c> members off the sample global discovery server.
        /// </summary>
        /// <remarks>
        /// <c>ConfigurationNodeManager</c> suppresses every one of these unless the host hands
        /// it a <c>ServerConfigurationOptions</c>, so finding them in the address space is
        /// what proves the sample server configures them - and nothing else in the sample
        /// would notice if that wiring were dropped.
        /// </remarks>
        private static async Task ReadTheOptionalServerConfigurationSurfaceAsync(
            GlobalDiscoveryServerClient gdsClient,
            string gdsUrl,
            ClientPhase phase,
            CancellationToken ct)
        {
            phase.Enter("reconnecting to the directory as the system administrator");

            // the ServerConfiguration nodes of the sample GDS are only visible to the
            // SecurityAdmin Role - the sample's own README points a user at 'sysadmin' for
            // exactly this - so the push-management surface is read over a second session
            // rather than over the registration one.
            await gdsClient.DisconnectAsync(ct).ConfigureAwait(true);

            gdsClient.AdminCredentials = new UserIdentity(
                kGdsSystemAdminUser,
                Encoding.UTF8.GetBytes(kGdsAdminPassword));

            await gdsClient.ConnectAsync(gdsUrl, ct).ConfigureAwait(true);

            phase.Enter("reading the Optional ServerConfiguration surface of the directory");

            // the Optional members are looked up by BrowseName rather than by their
            // well-known NodeId: several of them - SupportsTransactions and
            // InApplicationSetup among them - have no well-known singleton-instance NodeId in
            // the standard model, so browsing is the only way a client finds all of them, and
            // it is what the sample's own status panel does.
            var nodeToBrowse = new BrowseDescription {
                NodeId = Opc.Ua.ObjectIds.ServerConfiguration,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };

            var children = await Opc.Ua.Client.Controls.ClientUtils
                .BrowseAsync(gdsClient.Session, nodeToBrowse, true, ct)
                .ConfigureAwait(true);

            string[] browseNames = children.Select(child => child.BrowseName.Name).ToArray();

            await TestContext.Out
                .WriteLineAsync($"Gds: ServerConfiguration exposes {string.Join(", ", browseNames)}")
                .ConfigureAwait(true);

            Assert.Multiple(() => {
                Assert.That(
                    browseNames,
                    Has.Some.EqualTo("HasSecureElement"),
                    "ServerConfiguration.HasSecureElement is not exposed, so the sample no " +
                    "longer configures the Optional §7.10.3 surface.");

                Assert.That(
                    browseNames,
                    Has.Some.EqualTo("InApplicationSetup"),
                    "ServerConfiguration.InApplicationSetup is not exposed.");

                Assert.That(
                    browseNames,
                    Has.Some.EqualTo("ResetToServerDefaults"),
                    "ServerConfiguration.ResetToServerDefaults is not exposed, so the sample " +
                    "no longer supplies an IServerConfigurationResetProvider.");

                Assert.That(
                    browseNames,
                    Has.Some.EqualTo("ConfigurationFile"),
                    "ServerConfiguration.ConfigurationFile is not exposed, so the sample no " +
                    "longer supplies an IApplicationConfigurationFileProvider.");

                Assert.That(
                    browseNames,
                    Has.Some.EqualTo("SupportsTransactions"),
                    "ServerConfiguration.SupportsTransactions is not exposed, so the server " +
                    "does not implement the §7.10.2 transaction model.");

                Assert.That(
                    browseNames,
                    Has.Some.EqualTo("TransactionDiagnostics"),
                    "ServerConfiguration.TransactionDiagnostics is not exposed.");
            });

            phase.Enter("browsing the ManagedApplications folder of the directory");

            // §7.10.16: the sample fills this folder from its applications database while the
            // address space is built, so on a directory that has never had a registration it
            // is legitimately empty. What is asserted is that it is reachable at all - a
            // ManagedApplications node manager that failed would leave the folder unreadable
            // or the references dangling.
            ReadResponse folder = await gdsClient.Session
                .ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    [new ReadValueId {
                        NodeId = Opc.Ua.ObjectIds.ManagedApplications,
                        AttributeId = Attributes.BrowseName
                    }],
                    ct)
                .ConfigureAwait(true);

            Assert.That(
                StatusCode.IsGood(folder.Results[0].StatusCode),
                Is.True,
                "The ManagedApplications folder is not readable on the directory.");

            var managedApplications = new BrowseDescription {
                NodeId = Opc.Ua.ObjectIds.ManagedApplications,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            };

            var managed = await Opc.Ua.Client.Controls.ClientUtils
                .BrowseAsync(gdsClient.Session, managedApplications, true, ct)
                .ConfigureAwait(true);

            await TestContext.Out
                .WriteLineAsync($"Gds: ManagedApplications holds {managed.Count} application(s)")
                .ConfigureAwait(true);
        }

        /// <summary>
        /// Registers and removes an OPC 10000-21 onboarding ticket on the registrar the sample
        /// global discovery server exposes.
        /// </summary>
        /// <remarks>
        /// The registrar administration Object is found by BrowseName because the Onboarding
        /// companion model is not part of the shipped GDS packages, so the sample server
        /// builds the node in a namespace of its own - which is exactly what the client-side
        /// dialog does.
        /// </remarks>
        private static async Task RegisterAnOnboardingTicketAsync(
            GlobalDiscoveryServerClient gdsClient,
            ClientPhase phase,
            CancellationToken ct)
        {
            phase.Enter("finding the onboarding registrar of the directory");

            var nodeToBrowse = new BrowseDescription {
                NodeId = Opc.Ua.ObjectIds.ObjectsFolder,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            };

            var references = await Opc.Ua.Client.Controls.ClientUtils
                .BrowseAsync(gdsClient.Session, nodeToBrowse, true, ct)
                .ConfigureAwait(true);

            ReferenceDescription registrar = references
                .Find(reference => reference.BrowseName.Name == "DeviceRegistrarAdmin");

            Assert.That(
                registrar,
                Is.Not.Null,
                "The sample global discovery server no longer exposes the OPC 10000-21 " +
                "registrar administration Object.");

            phase.Enter("registering an onboarding ticket");

            var client = new OnboardingClient(
                gdsClient.Session,
                ExpandedNodeId.ToNodeId(registrar.NodeId, gdsClient.Session.NamespaceUris),
                NullTelemetry.Instance);

            byte[][] tickets = [[1, 2, 3, 4]];

            int[] registered = await client.RegisterTicketsAsync(tickets, ct).ConfigureAwait(true);

            Assert.That(registered, Has.Length.EqualTo(1), "RegisterTickets did not report a result per ticket.");
            Assert.That(
                StatusCode.IsGood(new StatusCode((uint)registered[0])),
                Is.True,
                "The registrar rejected the ticket.");

            phase.Enter("removing the onboarding ticket again");

            int[] removed = await client.UnregisterTicketsAsync(tickets, CancellationToken.None)
                .ConfigureAwait(true);

            Assert.That(removed, Has.Length.EqualTo(1), "UnregisterTickets did not report a result per ticket.");
            Assert.That(
                StatusCode.IsGood(new StatusCode((uint)removed[0])),
                Is.True,
                "The registrar did not remove the ticket it had just accepted.");
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

            // OPC 10000-12 §7.10 gates the ServerConfiguration Methods on the SecurityAdmin
            // Role, so the push client is given the credentials it elevates with before every
            // such call - which is what SelectPushServerDialog asks the user for.
            pushClient.AdminCredentials = new UserIdentity(
                kGdsSystemAdminUser,
                Encoding.UTF8.GetBytes(kGdsAdminPassword));

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

            await ShowThePushTransactionStateAsync(pushClient, statusPanel, phase, ct).ConfigureAwait(true);
        }

        /// <summary>
        /// Checks that the status panel reports the OPC 10000-12 v1.05.07 PushManagement
        /// transaction state of the managed server, and that the sample can drive the Methods
        /// that came with it.
        /// </summary>
        /// <remarks>
        /// <c>SupportsTransactions</c> has no well-known singleton-instance NodeId, so the
        /// panel resolves it by browse path - a step that silently yields nothing if the
        /// server does not expose the Property, which is why the rendered text is asserted
        /// rather than the read alone.
        /// </remarks>
        private static async Task ShowThePushTransactionStateAsync(
            ServerPushConfigurationClient pushClient,
            ServerStatusControl statusPanel,
            ClientPhase phase,
            CancellationToken ct)
        {
            phase.Enter("showing the PushManagement transaction state of the managed server");

            var supportsTransactions = (Label)WinFormsHarness.FindControl(statusPanel, "SupportsTransactionsTextBox");

            Assert.That(supportsTransactions, Is.Not.Null, "The status panel has no 'SupportsTransactionsTextBox'.");

            Assert.That(
                supportsTransactions.Text,
                Does.StartWith("True"),
                "The status panel does not report ServerConfiguration.SupportsTransactions of the managed server.");

            var transactionResult = (Label)WinFormsHarness.FindControl(statusPanel, "TransactionResultTextBox");

            Assert.That(transactionResult, Is.Not.Null, "The status panel has no 'TransactionResultTextBox'.");

            // §7.10.17: before the first transaction the server reports Bad_OutOfService on
            // the diagnostics children, which the panel shows instead of a value.
            Assert.That(
                transactionResult.Text,
                Is.Not.EqualTo("---"),
                "The status panel did not read ServerConfiguration.TransactionDiagnostics.");

            phase.Enter("listing the certificates the managed server holds");

            // §7.10.8: the push model's own GetCertificates, which the certificate dialog of
            // the sample lists.
            (ArrayOf<NodeId> certificateTypeIds, ArrayOf<ByteString> certificates) =
                await pushClient.GetCertificatesAsync(pushClient.DefaultApplicationGroup, ct).ConfigureAwait(true);

            Assert.That(
                certificateTypeIds.Count,
                Is.EqualTo(certificates.Count),
                "GetCertificates returned the certificate types and the certificates ragged.");

            phase.Enter("cancelling the changes the session has staged");

            // §7.10.11: nothing was staged, so the server answers Bad_NothingToDo - which is
            // the answer the Cancel Changes button of the panel swallows on purpose. The
            // exception is caught by hand rather than with Assert.ThrowsAsync, which pumps a
            // message loop of its own and cannot run on the harness thread.
            StatusCode cancelled = StatusCodes.Good;

            try
            {
                await pushClient.CancelChangesAsync(ct).ConfigureAwait(true);
            }
            catch (ServiceResultException refused)
            {
                cancelled = refused.StatusCode;
            }

            Assert.That(
                cancelled,
                Is.EqualTo(StatusCodes.BadNothingToDo),
                "CancelChanges on a session with no open transaction did not answer Bad_NothingToDo.");
        }
    }
}
