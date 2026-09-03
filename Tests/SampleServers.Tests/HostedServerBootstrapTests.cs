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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Server;
using Quickstarts.Boiler.Server;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 1, the bootstrap of the hosted samples: the server samples hand their
    /// configuration file to the hosted server of the stack through
    /// <c>services.AddSampleServer&lt;TServer&gt;(configurationFile)</c>.
    /// </summary>
    /// <remarks>
    /// The samples rely on promises the shared bootstrap makes on top of the stack:
    /// the start of the host returns with the server listening, the running server
    /// and the loaded configuration are resolvable - which is what the main forms
    /// inject - and a startup failure surfaces from the start instead of hanging it.
    /// This fixture starts the boiler sample the exact way its Program.Main does,
    /// minus the user interface, and asserts those promises.
    /// </remarks>
    [TestFixture]
    [Category("ServerSmoke")]
    [NonParallelizable]
    public class HostedServerBootstrapTests
    {
        /// <summary>
        /// How long the sample gets to start, answer and shut down again.
        /// </summary>
        private const int kTimeout = 60_000;

        /// <summary>
        /// The start of the host returns with the server started, and the container
        /// resolves what the main form of a sample injects: the running server, the
        /// shared <see cref="StandardServer"/> and the loaded configuration.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task StartReturnsWithTheServerListening(CancellationToken ct)
        {
            using var pki = new TemporaryPki("BoilerHostedBootstrap");

            IHost host = BuildHost(
                RepositoryLayout.PathOf("Workshop/Boiler/Server/BoilerServer.Config.xml"),
                pki);

            try
            {
                await host.StartAsync(ct).ConfigureAwait(false);

                BoilerServer server = host.Services.GetRequiredService<BoilerServer>();

                Assert.That(
                    host.Services.GetRequiredService<StandardServer>(),
                    Is.SameAs(server),
                    "The form of a sample takes the shared StandardServer, which must be the running server.");

                ApplicationConfiguration configuration =
                    host.Services.GetRequiredService<ApplicationConfiguration>();

                Assert.That(
                    configuration.ApplicationName,
                    Is.Not.Null.And.Not.Empty,
                    "The configuration was not loaded from the configuration file.");

                // the server is up when the start returns - the samples read their
                // endpoints and session managers right after it.
                string[] endpoints = server.GetEndpoints()
                    .ToArray()
                    .Select(endpoint => endpoint.EndpointUrl)
                    .Distinct()
                    .ToArray();

                await TestContext.Out.WriteLineAsync(
                    $"BoilerHostedBootstrap: endpoints -> {string.Join(", ", endpoints)}")
                    .ConfigureAwait(false);

                Assert.That(endpoints, Is.Not.Empty, "The started server offers no endpoints.");

                // and it answers on the opc.tcp endpoint the configuration names
                string endpointUrl = configuration.ServerConfiguration.BaseAddresses
                    .Filter(address => address.StartsWith(
                        Utils.UriSchemeOpcTcp,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray()[0];

                await using (TestClient client = await TestClient
                    .ConnectAsync(endpointUrl, "hosted bootstrap test", ct)
                    .ConfigureAwait(false))
                {
                    Assert.That(client.Session.Connected, Is.True, "The session is not connected.");
                }

                await host.StopAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                await DisposeAsync(host).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A configuration file which cannot be loaded fails the start of the host
        /// with the original error, instead of hanging the readiness gate forever.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AStartupFailureSurfacesInsteadOfHanging(CancellationToken ct)
        {
            IHost host = BuildHost("DoesNotExist.Config.xml", pki: null);

            try
            {
                Exception failure = Assert.CatchAsync(
                    () => host.StartAsync(ct),
                    "Starting without a configuration file must fail the start of the host.");

                await TestContext.Out.WriteLineAsync(
                    $"BoilerHostedBootstrap: startup failure -> {failure.Message}")
                    .ConfigureAwait(false);
            }
            finally
            {
                await DisposeAsync(host).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Builds the host of the boiler sample the way its Program.Main does, with
        /// the certificate stores redirected into the temporary PKI of the test.
        /// </summary>
        private static IHost BuildHost(string configurationFile, TemporaryPki pki)
        {
            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(
                new HostApplicationBuilderSettings());

            // the empty builder registers no logging, which the telemetry of the
            // stack resolves its loggers from
            builder.Services.AddLogging();

            builder.Services.AddSampleServer<BoilerServer>(
                configurationFile,
                configuration => pki?.Redirect(configuration));

            return builder.Build();
        }

        /// <summary>
        /// Disposes the host, which stops a server the test left running.
        /// </summary>
        private static async ValueTask DisposeAsync(IHost host)
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host.Dispose();
            }
        }
    }
}
