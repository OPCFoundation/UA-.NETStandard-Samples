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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Server;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Registers the server of a sample with a service collection: the server class,
    /// its node managers and the configuration file to load. Every sample server has
    /// one of these next to its entry point, and the tests host the sample through
    /// the same registration its <c>Program.Main</c> uses.
    /// </summary>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configurationFile">The configuration file to load.</param>
    /// <param name="configure">Applied to the configuration right after it has been
    /// read.</param>
    public delegate void SampleServerRegistration(
        IServiceCollection services,
        string configurationFile,
        Action<ApplicationConfiguration> configure);

    /// <summary>
    /// Hosts one sample server for a test, the way the sample hosts itself: a generic
    /// host with the server of the sample registered as the hosted OPC UA server of
    /// the stack. The certificate stores are redirected into a temporary PKI, the
    /// server is restricted to its opc.tcp endpoint, and the host is started with a
    /// retry while the port of the previous run is still bound.
    /// </summary>
    public sealed class SampleServerHost : IAsyncDisposable
    {
        private readonly string m_name;
        private readonly string m_configPath;
        private readonly SampleServerRegistration m_register;
        private readonly Action<ApplicationConfiguration> m_configure;
        private readonly TemporaryPki m_pki;
        private IHost m_host;

        private SampleServerHost(
            string name,
            string configPath,
            SampleServerRegistration register,
            Action<ApplicationConfiguration> configure,
            TemporaryPki pki)
        {
            m_name = name;
            m_configPath = configPath;
            m_register = register;
            m_configure = configure;
            m_pki = pki;
        }

        /// <summary>
        /// The opc.tcp endpoint the server listens on.
        /// </summary>
        public string EndpointUrl { get; private set; }

        /// <summary>
        /// The configuration the running server was started with.
        /// </summary>
        public ApplicationConfiguration Configuration { get; private set; }

        /// <summary>
        /// The running server.
        /// </summary>
        public StandardServer Server { get; private set; }

        /// <summary>
        /// Whether the server is running.
        /// </summary>
        public bool IsRunning => m_host != null;

        /// <summary>
        /// Starts the sample server.
        /// </summary>
        /// <param name="name">The name of the sample, for the messages and the PKI.</param>
        /// <param name="configPath">The configuration file, relative to the repository.</param>
        /// <param name="register">Registers the server of the sample with the host.</param>
        /// <param name="ct">Cancels the start.</param>
        /// <param name="configure">Applied to the configuration before the server starts,
        /// after the certificate stores were redirected.</param>
        public static async Task<SampleServerHost> StartAsync(
            string name,
            string configPath,
            SampleServerRegistration register,
            CancellationToken ct = default,
            Action<ApplicationConfiguration> configure = null)
        {
            if (register == null)
            {
                throw new ArgumentNullException(nameof(register));
            }

            var pki = new TemporaryPki(name);
            var host = new SampleServerHost(name, configPath, register, configure, pki);

            try
            {
                await host.StartServerAsync(ct).ConfigureAwait(false);
                return host;
            }
            catch
            {
                pki.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Stops the server and disposes the host it ran in.
        /// </summary>
        public async Task StopAsync()
        {
            IHost host = m_host;
            m_host = null;
            Server = null;

            if (host == null)
            {
                return;
            }

            await StopAndDisposeAsync(host).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts the server again after <see cref="StopAsync"/>, in a fresh host
        /// with the same configuration and PKI.
        /// </summary>
        public Task StartAgainAsync(CancellationToken ct = default)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException(
                    $"{m_name}: the server is still running. Stop it before starting it again.");
            }

            return StartServerAsync(ct);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            m_pki.Dispose();
        }

        private static readonly TimeSpan kBindRetryWindow = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan kBindRetryDelay = TimeSpan.FromMilliseconds(500);

        private async Task StartServerAsync(CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + kBindRetryWindow;

            while (true)
            {
                try
                {
                    await StartServerOnceAsync(ct).ConfigureAwait(false);
                    return;
                }
                catch (Exception e) when (IsAddressAlreadyInUse(e) && DateTime.UtcNow < deadline)
                {
                    await TestContext.Progress.WriteLineAsync(
                        $"{m_name}: {EndpointUrl} is still bound, retrying ...").ConfigureAwait(false);
                    await Task.Delay(kBindRetryDelay, ct).ConfigureAwait(false);
                }
            }
        }

        private static bool IsAddressAlreadyInUse(Exception e)
        {
            while (e != null)
            {
                if (e is SocketException socketError &&
                    socketError.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    return true;
                }

                if (e is AggregateException aggregate)
                {
                    return aggregate.InnerExceptions.Any(IsAddressAlreadyInUse);
                }

                e = e.InnerException;
            }

            return false;
        }

        private async Task StartServerOnceAsync(CancellationToken ct)
        {
            // the empty builder registers no logging, which the telemetry of the
            // stack resolves its loggers from; the tests log nothing from the server.
            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(
                new HostApplicationBuilderSettings());
            builder.Services.AddLogging(logging => { logging.AddProvider(new ProbeFileLoggerProvider()); logging.SetMinimumLevel(LogLevel.Trace); });

            string endpointUrl = null;

            m_register(
                builder.Services,
                RepositoryLayout.PathOf(m_configPath),
                configuration => {
                    // read from the repository, so the certificate stores have to be
                    // redirected before the server sets up its certificate
                    m_pki.Redirect(configuration);
                    endpointUrl = KeepOpcTcpEndpointsOnly(configuration);
                    KeepTheServerFromDiallingOut(configuration);
                    m_configure?.Invoke(configuration);
                });

            IHost host = builder.Build();

            try
            {
                // the hosts of the samples return from the start with the server
                // listening, which the tests rely on
                await host.StartAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // a half started server holds its listener, and a second attempt on
                // the same port would fail for a reason which has nothing to do with
                // the first failure
                await StopAndDisposeAsync(host).ConfigureAwait(false);
                throw;
            }

            m_host = host;
            EndpointUrl = endpointUrl;
            Configuration = host.Services.GetRequiredService<ApplicationConfiguration>();
            Server = host.Services.GetRequiredService<StandardServer>();
        }

        private static async Task StopAndDisposeAsync(IHost host)
        {
            try
            {
                await host.StopAsync().ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // a server which is already down must not fail the test
            }
            catch (OperationCanceledException)
            {
                // as above
            }

            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host.Dispose();
            }
        }

        /// <summary>
        /// Drops the reverse connect clients a sample server ships with, so a server
        /// hosted by a test never opens a connection of its own.
        /// </summary>
        /// <remarks>
        /// The Reference server dials a client every <c>ConnectInterval</c> so its own
        /// sample pairs with the Reference Client out of the box. In a test nothing owns
        /// that port, so the dial is a failing outbound connect every fifteen seconds,
        /// with the logging that goes with it, in the same process as timing sensitive
        /// data change tests. A test which wants a server to dial sets its own
        /// <see cref="ServerConfiguration.ReverseConnect"/> in the <c>configure</c>
        /// callback, which runs after this.
        /// </remarks>
        private static void KeepTheServerFromDiallingOut(ApplicationConfiguration configuration)
        {
            if (configuration.ServerConfiguration != null)
            {
                configuration.ServerConfiguration.ReverseConnect = null;
            }
        }

        private static string KeepOpcTcpEndpointsOnly(ApplicationConfiguration configuration)
        {
            ServerBaseConfiguration server = configuration.ServerConfiguration;
            if (server == null)
            {
                throw new InvalidOperationException("The configuration does not describe a server.");
            }

            string[] opcTcp = server.BaseAddresses
                .Filter(address => address.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            for (int ii = 0; ii < opcTcp.Length; ii++)
            {
                opcTcp[ii] = PreferLocalhost(opcTcp[ii]);
            }

            if (opcTcp.Length == 0)
            {
                throw new InvalidOperationException("The server does not offer an opc.tcp endpoint.");
            }

            server.BaseAddresses = new ArrayOf<string>(opcTcp);
            server.AlternateBaseAddresses = ArrayOf<string>.Empty;
            return opcTcp[0];
        }

        /// <summary>
        /// Puts <c>localhost</c> back into an address the stack rewrote to the name of
        /// the machine while it loaded the configuration: the tests connect to the
        /// endpoint the catalog names, and the samples listen on localhost as their
        /// configuration files say.
        /// </summary>
        private static string PreferLocalhost(string address)
        {
            var uri = new Uri(address);

            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                (!string.Equals(uri.Host, Environment.MachineName, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(uri.Host, System.Net.Dns.GetHostName(), StringComparison.OrdinalIgnoreCase)))
            {
                return address;
            }

            int hostStart = address.IndexOf("://", StringComparison.Ordinal) + 3;
            return string.Concat(address.AsSpan(0, hostStart), "localhost", address.AsSpan(hostStart + uri.Host.Length));
        }
    }
}

// TEMPORARY diagnostic logger - delete with the probe.
namespace Opc.Ua.Samples.Tests
{
    internal sealed class ProbeFileLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
    {
        internal static readonly string Path =
            System.Environment.GetEnvironmentVariable("PROBE_LOG") ?? "probe-server.log";

        private static readonly object s_gate = new();

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
            => new ProbeLogger(categoryName);

        public void Dispose()
        {
        }

        private sealed class ProbeLogger : Microsoft.Extensions.Logging.ILogger
        {
            private readonly string m_category;

            public ProbeLogger(string category) => m_category = category;

            public System.IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel,
                Microsoft.Extensions.Logging.EventId eventId,
                TState state,
                System.Exception exception,
                System.Func<TState, System.Exception, string> formatter)
            {
                string line =
                    $"[{logLevel}] {m_category} ({eventId.Id}) {formatter(state, exception)}" +
                    (exception == null ? string.Empty : $" EX: {exception}");

                lock (s_gate)
                {
                    System.IO.File.AppendAllText(Path, line + System.Environment.NewLine);
                }
            }
        }
    }
}
