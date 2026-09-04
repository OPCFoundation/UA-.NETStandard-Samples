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
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Configuration;
using Opc.Ua.Sample;
using Opc.Ua.Sample.Controls;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 2: the UA Sample Client has to open its session with a managed session running the
    /// V2 subscription engine, and fill its browse tree from it.
    /// </summary>
    /// <remarks>
    /// The sample is the one client the generic smoke test cannot drive: it does not host the
    /// shared connect control, and it opens its session through a modal dialog rather than from
    /// a url in a toolbar. That is why "Sample" is a declared gap in <see cref="SampleClientTests"/>.
    /// This fixture drives it the way a user would instead - pick an endpoint, let the session
    /// dialog come up and click OK - with the watchdog answering the dialog.
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class SampleClientFormTests
    {
        private const int kTimeout = 120_000;

        /// <summary>
        /// The sample. Its client is the UA Sample Client and its server the UA Sample Server.
        /// </summary>
        private const string kSample = "Sample";

        [Test]
        [CancelAfter(kTimeout)]
        public async Task SampleClientConnectsWithAManagedSessionAndBrowses(CancellationToken ct)
        {
            SampleDefinition sample = SampleCatalog.All.Single(entry => entry.Name == kSample);
            SampleServerUnderTest server = SampleServerFactories.All.Single(entry => entry.Sample.Name == kSample);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(kSample, server.Sample.ServerConfig, server.ConfigureServices, ct)
                .ConfigureAwait(false);

            var phase = new ClientPhase();

            try
            {
                await WinFormsHarness.RunAsync(
                    async watchdog => await DriveClientAsync(sample, host.EndpointUrl, watchdog, phase, ct)
                        .ConfigureAwait(true),
                    TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(20))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException expired)
            {
                throw phase.Explain(expired);
            }
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task DriveClientAsync(
            SampleDefinition sample,
            string endpointUrl,
            DialogWatchdog watchdog,
            ClientPhase phase,
            CancellationToken ct)
        {
            phase.Enter("loading the configuration of the sample");

            using var pki = new TemporaryPki("client-sample-form");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            phase.Enter("creating its client certificate");

            bool certificateOk = await application
                .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                .ConfigureAwait(true);

            Assert.That(certificateOk, Is.True, "The client certificate of the sample could not be created.");

            phase.Enter("building its main form");

            // the form is composed the way the entry point of the sample composes it, so the
            // dialogs it opens come out of the same container.
            var services = new ServiceCollection();

            services.AddSingleton(application);
            services.AddSingleton(configuration);
            services.AddSingleton<ITelemetryContext>(NullTelemetry.Instance);
            services.AddSampleWindows();

            await using ServiceProvider provider = services.BuildServiceProvider();

            using var form = provider.GetRequiredService<IWindowFactory>().Create<SampleClientForm>();

            // create the window handle without ever showing the form: the sample marshals its
            // connect callbacks back to the UI thread and needs a handle to do so
            _ = form.Handle;

            phase.Enter("selecting the endpoint");

            EndpointDescription description = await CoreClientUtils
                .SelectEndpointAsync(configuration, endpointUrl, false, 15_000, NullTelemetry.Instance, ct)
                .ConfigureAwait(true);

            var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(configuration)) {
                // the sample would otherwise open its endpoint configuration dialog first,
                // which is a step the user has already done when they pick a cached endpoint
                UpdateBeforeConnect = false,
            };

            // the sample opens its session through a modal dialog, and the harness would
            // otherwise cancel it and report it as an error dialog the sample opened
            watchdog.Accept<SessionOpenDlg>("OkBTN");

            phase.Enter($"connecting to {endpointUrl}");

            await form.ConnectAsync(endpoint, NullTelemetry.Instance, ct).ConfigureAwait(true);

            try
            {
                phase.Enter("checking the session it opened");

                var session = WinFormsHarness.FindField<ISession>(form, "m_session");

                Assert.That(
                    session,
                    Is.Not.Null,
                    "The sample did not keep the session its session dialog opened.");

                Assert.That(
                    session,
                    Is.InstanceOf<ManagedSession>(),
                    "The sample no longer opens a managed session, so nothing drives its reconnect.");

                Assert.That(session.Connected, Is.True, "The session of the sample is not connected.");

                Assert.That(
                    session.TryGetSubscriptionManager(out ISubscriptionManager manager),
                    Is.True,
                    "The session of the sample does not run the V2 subscription engine, so its " +
                    "subscription dialogs have nothing to add monitored items to.");

                Assert.That(manager, Is.Not.Null, "The session reported a V2 engine without a manager.");

                phase.Enter("checking its browse tree");

                var nodes = (TreeView)WinFormsHarness.FindControl(
                    WinFormsHarness.FindControl(form, "BrowseCTRL"),
                    "NodesTV");

                Assert.That(nodes, Is.Not.Null, "The browse control of the sample has no 'NodesTV' tree.");

                Assert.That(
                    nodes.Nodes.Count,
                    Is.GreaterThan(0),
                    "The sample connected but its browse tree stayed empty, so the post connect " +
                    "logic did not read the address space of the server.");
            }
            finally
            {
                phase.Enter("disconnecting");

                await form.DisconnectAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }
    }
}
