/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Samples.Hosting;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AggregationServer
{
    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string message = string.Empty;
        private bool ask = false;

        public override void Message(string text, bool ask)
        {
            this.message = text;
            this.ask = ask;
        }

        public override async Task<bool> ShowAsync()
        {
            if (ask)
            {
                message += " (y/n, default y): ";
                Console.Write(message);
            }
            else
            {
                Console.WriteLine(message);
            }
            if (ask)
            {
                try
                {
                    ConsoleKeyInfo result = Console.ReadKey();
                    Console.WriteLine();
                    return await Task.FromResult((result.KeyChar == 'y') || (result.KeyChar == 'Y') || (result.KeyChar == '\r')).ConfigureAwait(false);
                }
                catch
                {
                    // intentionally fall through
                }
            }
            return await Task.FromResult(true).ConfigureAwait(false);
        }
    }

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var server = new MyServer();
            await server.RunAsync(args).ConfigureAwait(false);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Sample server disposes the field during shutdown.")]
    public class MyServer
    {
        AggregationServer server;
        IHost host;
        // a completed task until the status thread is started, so a failure between
        // the server being created and the thread starting still shuts down cleanly.
        Task status = Task.CompletedTask;
        DateTime lastEventTime;
        private ITelemetryContext m_telemetry;

        public async Task RunAsync(string[] args)
        {
            try
            {
                await ConsoleAggregationServerAsync(args).ConfigureAwait(false);
                Console.WriteLine("Server started. Press any key to exit...");
            }
            catch (Exception ex)
            {
                m_telemetry?.CreateLogger<MyServer>()
                    .LogError(ex, "ServiceResultException:");
                Console.WriteLine("Exception: {0}", ex.Message);
            }

            try
            {
                Console.ReadKey(true);
            }
            catch
            {
                // wait forever if there is no console
                await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
            }

            if (server != null)
            {
                Console.WriteLine("Server stopped. Waiting for exit...");

                // let the status thread run out before the host stops the server.
                server = null;
                await status.ConfigureAwait(false);
            }

            if (host != null)
            {
                IHost stopping = host;
                host = null;

                await stopping.StopAsync().ConfigureAwait(false);
                if (stopping is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    stopping.Dispose();
                }
            }

            SampleLogging.CloseAndFlush();
        }

        private static bool CertificateManager_AcceptError(Opc.Ua.Security.Certificates.Certificate certificate, ServiceResult error)
        {
            if (error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                Console.WriteLine("Rejected Certificate: {0}", certificate.Subject);
            }

            return false;
        }

        /// <summary>
        /// Reports an untrusted certificate before rejecting it, which the
        /// configuration file cannot express.
        /// </summary>
        private static void RejectUntrustedCertificatesLoudly(ApplicationConfiguration config)
        {
            if (!config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                config.CertificateManager.AcceptError = CertificateManager_AcceptError;
            }
        }

        private async Task ConsoleAggregationServerAsync(string[] args)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();

            // the generic host owns the logging and the lifetime of the sample; the
            // server is hosted by the stack, its configuration is loaded straight from
            // the configuration file. A console sample logs to the console as well as
            // to the file the configuration names.
            HostApplicationBuilder builder = SampleHost.CreateBuilder(args);

            builder.Logging.AddSampleConsole();

            builder.Services
                .AddSampleServer<AggregationServer>(
                    "Quickstarts.AggregationServer.Config.xml",
                    RejectUntrustedCertificatesLoudly);

            host = builder.Build();
            m_telemetry = host.Services.GetRequiredService<ITelemetryContext>();

            await host.StartAsync().ConfigureAwait(false);

            server = host.Services.GetRequiredService<AggregationServer>();

            ApplicationConfiguration config = host.Services.GetRequiredService<ApplicationConfiguration>();

            // print reverse connect info
            Console.WriteLine("Reverse Connect Clients:");
            var reverseConnections = server.GetReverseConnections();
            foreach (var connection in reverseConnections)
            {
                Console.WriteLine(connection.Key);
            }

            // print endpoint info
            Console.WriteLine("Server Endpoints:");
            HashSet<string> endpoints = new HashSet<string>();
            foreach (EndpointDescription endpointDescription in server.GetEndpoints())
            {
                endpoints.Add(endpointDescription.EndpointUrl);
            }
            foreach (var endpoint in endpoints)
            {
                Console.WriteLine(endpoint);
            }

            // print the reverse connect host
            if (config.ClientConfiguration?.ReverseConnect != null)
            {
                var reverseConnect = config.ClientConfiguration.ReverseConnect;
                Console.WriteLine("Reverse Connect Server Endpoints:");
                foreach (var endpoint in reverseConnect.ClientEndpoints)
                {
                    Console.WriteLine(endpoint.EndpointUrl);
                }
                Console.WriteLine("HoldTime: {0}", reverseConnect.HoldTime);
                Console.WriteLine("WaitTimeout: {0}", reverseConnect.WaitTimeout);
            }

            // start the status thread
            status = Task.Run(async () => await StatusThreadAsync().ConfigureAwait(false));

            // print notification on session events
            server.CurrentInstance.SessionManager.SessionActivated += EventStatus;
            server.CurrentInstance.SessionManager.SessionClosing += EventStatus;
            server.CurrentInstance.SessionManager.SessionCreated += EventStatus;
        }

        private void EventStatus(ISession session, SessionEventReason reason)
        {
            lastEventTime = DateTime.UtcNow;
            PrintSessionStatus(session, reason.ToString());
        }

        private void PrintSessionStatus(ISession session, string reason, bool lastContact = false)
        {
            // The session owns its diagnostics lock and no longer exposes it;
            // ReadDiagnostics applies each projection while holding that lock.
            string item = String.Format("{0,9}:{1,20}:", reason, session.ReadDiagnostics(d => d.SessionName));
            if (lastContact)
            {
                item += String.Format(":{0:HH:mm:ss}", session.ReadDiagnostics(d => d.ClientLastContactTime).ToLocalTime());
            }
            else
            {
                if (session.Identity != null)
                {
                    item += String.Format(":{0,20}", session.Identity.DisplayName);
                }
                item += String.Format(":{0}", session.Id);
            }
            Console.WriteLine(item);
        }

        private async Task StatusThreadAsync()
        {
            AggregationServer serverStatus = server;
            while (serverStatus != null)
            {
                if (DateTime.UtcNow - lastEventTime > TimeSpan.FromMilliseconds(6000))
                {
                    IList<ISession> sessions = serverStatus.CurrentInstance.SessionManager.GetSessions();
                    for (int ii = 0; ii < sessions.Count; ii++)
                    {
                        ISession session = sessions[ii];
                        PrintSessionStatus(session, "-Status-", true);
                    }
                    lastEventTime = DateTime.UtcNow;
                }
                await Task.Delay(1000).ConfigureAwait(false);
                serverStatus = server;
            }
        }
    }
}
