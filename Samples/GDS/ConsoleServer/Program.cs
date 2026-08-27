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

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mono.Options;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Gds.Server
{
    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string m_message = string.Empty;
        private bool m_ask = false;

        public override void Message(string text, bool ask)
        {
            this.m_message = text;
            this.m_ask = ask;
        }

        public override async Task<bool> ShowAsync()
        {
            if (m_ask)
            {
                m_message += " (y/n, default y): ";
                Console.Write(m_message);
            }
            else
            {
                Console.WriteLine(m_message);
            }
            if (m_ask)
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

    public enum ExitCode : int
    {
        Ok = 0,
        ErrorServerNotStarted = 0x80,
        ErrorServerRunning = 0x81,
        ErrorServerException = 0x82,
        ErrorInvalidCommandLine = 0x100
    };

    public static class Program
    {

        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine(".Net Core OPC UA Global Discovery Server");

            // command line options
            bool showHelp = false;

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                { "h|help", "show this message and exit", h => showHelp = h != null },
            };

            try
            {
                IList<string> extraArgs = options.Parse(args);
                foreach (string extraArg in extraArgs)
                {
                    Console.WriteLine("Error: Unknown option: {0}", extraArg);
                    showHelp = true;
                }
            }
            catch (OptionException e)
            {
                Console.WriteLine(e.Message);
                showHelp = true;
            }

            if (showHelp)
            {
                Console.WriteLine("Usage: dotnet NetCoreGlobalDiscoveryServer.dll [OPTIONS]");
                Console.WriteLine();

                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return (int)ExitCode.ErrorInvalidCommandLine;
            }

            var server = new NetCoreGlobalDiscoveryServer();
            await server.RunAsync().ConfigureAwait(false);

            return (int)NetCoreGlobalDiscoveryServer.ExitCode;
        }
    }

    public sealed class ConsoleTelemetry : TelemetryContextBase
    {
        public ConsoleTelemetry()
        : base(
            #pragma warning disable CA2000 // Justification: WinForms/sample ownership or lifetime is managed outside the local scope.
            Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            #pragma warning restore CA2000
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddConsole();
            })
            )
        {
        }
    }

    #pragma warning disable CA1001, CA1708 // Justification: Public sample API compatibility is preserved.
    public class NetCoreGlobalDiscoveryServer
    #pragma warning restore CA1001, CA1708
    {
        private GlobalDiscoverySampleServer server;
        private readonly ITelemetryContext m_telemetry = new ConsoleTelemetry();
        private Task status;
        private DateTime lastEventTime;
        private ApplicationConfiguration m_configuration;
        private IApplicationsDatabase m_database;
        private LdsScannerConfiguration m_ldsScannerConfiguration;
        private GlobalDiscoveryServerAliasMerger m_aliasMerger;
        private ApplicationInstance m_application;
        #pragma warning disable CA2211 // Justification: Public sample API compatibility is preserved.
        public static ExitCode exitCode;
        #pragma warning restore CA2211

        public NetCoreGlobalDiscoveryServer()
        {
        }

        public async Task RunAsync()
        {
            try
            {
                exitCode = ExitCode.ErrorServerNotStarted;
                await ConsoleGlobalDiscoveryServerAsync(m_telemetry).ConfigureAwait(false);
                Console.WriteLine("Server started.");
                PrintCommandHelp();
                exitCode = ExitCode.ErrorServerRunning;
            }
            catch (Exception ex)
            {
                m_telemetry.CreateLogger<NetCoreGlobalDiscoveryServer>()
                    #pragma warning disable CA2254 // Justification: Public sample API compatibility is preserved.
                    .LogError("ServiceResultException:" + ex.Message);
                    #pragma warning restore CA2254
                Console.WriteLine("Exception: {0}", ex.Message);
                exitCode = ExitCode.ErrorServerException;
                return;
            }

            using (var quitEvent = new ManualResetEventSlim(false))
            {
                try
                {
                    Console.CancelKeyPress += (sender, eArgs) =>
                    {
                        quitEvent.Set();
                        eArgs.Cancel = true;
                    };
                }
                catch
                {
                }

                // interactive command loop: read commands until quit or Ctrl-C.
                while (!quitEvent.IsSet)
                {
                    string command = await ReadCommandAsync(quitEvent).ConfigureAwait(false);
                    if (command == null)
                    {
                        break;
                    }

                    switch (command.Trim().ToUpperInvariant())
                    {
                        case "":
                            break;
                        case "Q":
                        case "QUIT":
                        case "EXIT":
                            quitEvent.Set();
                            break;
                        case "S":
                        case "SCAN":
                            await RunLdsScanWorkflowAsync().ConfigureAwait(false);
                            break;
                        case "H":
                        case "HELP":
                        case "?":
                            PrintCommandHelp();
                            break;
                        default:
                            Console.WriteLine("Unknown command '{0}'. Type 'help' for options.", command.Trim());
                            break;
                    }
                }
            }

            if (server != null)
            {
                Console.WriteLine("Server stopped. Waiting for exit...");

                m_aliasMerger?.Dispose();
                m_aliasMerger = null;

                using (GlobalDiscoverySampleServer _server = server)
                {
                    // Stop status thread
                    server = null;
                    await status.ConfigureAwait(false);
                    // Stop server and dispose
                    await _server.StopAsync();
                }

                if (m_application != null)
                {
                    await m_application.DisposeAsync().ConfigureAwait(false);
                    m_application = null;
                }
            }

            exitCode = ExitCode.Ok;
        }

        public static ExitCode ExitCode => exitCode;

        private static void PrintCommandHelp()
        {
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  scan  - scan the configured LDS(es) for servers and stage them for approval");
            Console.WriteLine("  help  - show this message");
            Console.WriteLine("  quit  - stop the server and exit (Ctrl-C also works)");
            Console.WriteLine();
        }

        private static async Task<string> ReadCommandAsync(ManualResetEventSlim quitEvent)
        {
            Console.Write("gds> ");

            // read a line on a background thread so Ctrl-C can still interrupt.
            Task<string> readTask = Task.Run(() => Console.ReadLine());

            while (!readTask.IsCompleted)
            {
                if (quitEvent.IsSet)
                {
                    return null;
                }
                await Task.WhenAny(readTask, Task.Delay(200)).ConfigureAwait(false);
            }

            return await readTask.ConfigureAwait(false);
        }

        private async Task RunLdsScanWorkflowAsync()
        {
            if (m_database == null || m_configuration == null || m_ldsScannerConfiguration == null)
            {
                Console.WriteLine("The server is not ready to scan yet.");
                return;
            }

            IList<string> ldsUrls = m_ldsScannerConfiguration.LdsDiscoveryUrls;
            Console.WriteLine("Scanning {0} LDS discovery URL(s): {1}",
                ldsUrls.Count, string.Join(", ", ldsUrls));

            var scanner = new LdsGdsScanner(m_configuration, m_database, m_telemetry);

            IList<LdsScanCandidate> candidates;
            try
            {
                candidates = await scanner.ScanAsync(ldsUrls).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Scan failed: {0}", ex.Message);
                return;
            }

            if (candidates.Count == 0)
            {
                Console.WriteLine("No new servers found on the configured LDS(es).");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Found {0} server(s) not yet registered in the GDS.", candidates.Count);
            Console.WriteLine("Approval represents the DiscoveryAdmin role - only approved servers are added.");
            Console.WriteLine();

            var approved = new List<LdsScanCandidate>();

            foreach (LdsScanCandidate candidate in candidates)
            {
                ApplicationDescription app = candidate.Application;
                Console.WriteLine("  Server name     : {0}", candidate.Network?.ServerName);
                Console.WriteLine("  Application URI : {0}", app?.ApplicationUri ?? "(unresolved)");
                Console.WriteLine("  Application type: {0}", app?.ApplicationType.ToString() ?? "(unresolved)");
                Console.WriteLine("  Product URI     : {0}", app?.ProductUri);
                Console.WriteLine("  Discovery URL   : {0}", candidate.Network?.DiscoveryUrl);
                Console.WriteLine("  Found via LDS   : {0}", candidate.SourceLdsUrl);
                Console.Write("  Register this application? (y/n, default n): ");

                string answer = Console.ReadLine();
                if (!string.IsNullOrEmpty(answer) &&
                    (answer.StartsWith('y') || answer.StartsWith('Y')))
                {
                    approved.Add(candidate);
                }

                Console.WriteLine();
            }

            if (approved.Count == 0)
            {
                Console.WriteLine("No servers approved. Nothing registered.");
                return;
            }

            IList<NodeId> registered = scanner.RegisterApproved(approved);
            Console.WriteLine("Registered {0} of {1} approved server(s) in the GDS.",
                registered.Count, approved.Count);
        }

        private async Task ConsoleGlobalDiscoveryServerAsync(ITelemetryContext telemetry)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            m_application = new ApplicationInstance(telemetry)
            {
                ApplicationName = "Global Discovery Server",
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "Opc.Ua.GlobalDiscoveryServer"
            };

            // load the application configuration.
            ApplicationConfiguration config = await m_application.LoadApplicationConfigurationAsync(false).ConfigureAwait(false);

            // check the application certificate.
            bool haveAppCertificate = await m_application.CheckApplicationInstanceCertificatesAsync(false).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                #pragma warning disable CA2201 // Justification: Public sample API compatibility is preserved.
                throw new Exception("Application instance certificate invalid!");
                #pragma warning restore CA2201
            }

            if (!config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                config.CertificateManager.AcceptError = (certificate, error) =>
                {
                    bool accept = error.StatusCode == StatusCodes.BadCertificateUntrusted;
                    if (accept)
                    {
                        Console.WriteLine("Accepted Certificate: {0}", certificate.AsX509Certificate2().Subject);
                    }
                    return accept;
                };
            }

            // get the DatabaseStorePath configuration parameter.
            GlobalDiscoveryServerConfiguration gdsConfiguration = config.ParseExtension<GlobalDiscoveryServerConfiguration>();
            string databaseStorePath = Utils.ReplaceSpecialFolderNames(gdsConfiguration.DatabaseStorePath);
            string userdatabaseStorePath = Utils.ReplaceSpecialFolderNames(gdsConfiguration.UsersDatabaseStorePath);

            var database = JsonApplicationsDatabase.Load(databaseStorePath);
            var userDatabase = JsonUserDatabase.Load(userdatabaseStorePath, telemetry);

            bool createStandardUsers = ConfigureUsers(userDatabase);

            // GDS master AliasNames list (issue #274): merge each registered
            // server's AliasNames into a master list served by this GDS.
            m_aliasMerger = new GlobalDiscoveryServerAliasMerger(telemetry);

            // start the server.
            server = new AliasMergingGlobalDiscoverySampleServer(
                database,
                database,
                #pragma warning disable CA2000 // Justification: Certificate group ownership is transferred to the server.
                new CertificateGroup(telemetry),
                #pragma warning restore CA2000
                userDatabase,
                telemetry,
                m_aliasMerger,
                true);
            await m_application.StartAsync(server).ConfigureAwait(false);

            // keep the configuration and database so the interactive LDS scan
            // can register approved servers after start-up.
            m_configuration = config;
            m_database = database;
            m_ldsScannerConfiguration = LdsScannerConfiguration.Parse(config);

            // start periodically refreshing the GDS master AliasNames list
            // from every registered server.
            m_aliasMerger.Start(config, database);

            // print endpoint info
            IEnumerable<string> endpoints = m_application.Server.GetEndpoints().ToArray().Select(e => e.EndpointUrl).Distinct();
            foreach (string endpoint in endpoints)
            {
                Console.WriteLine(endpoint);
            }

            // start the status thread
            status = Task.Run(new Action(StatusThreadAsync));

            // print notification on session events
            server.CurrentInstance.SessionManager.SessionActivated += EventStatus;
            server.CurrentInstance.SessionManager.SessionClosing += EventStatus;
            server.CurrentInstance.SessionManager.SessionCreated += EventStatus;

        }

        private bool ConfigureUsers(IUserDatabase userDatabase)
        {
            ApplicationInstance.MessageDlg.Message("Use default users?", true);
            bool createStandardUsers = ApplicationInstance.MessageDlg.ShowAsync().GetAwaiter().GetResult();

            if (!createStandardUsers)
            {
                //delete existing standard users
                userDatabase.DeleteUser("appadmin");
                userDatabase.DeleteUser("appuser");
                userDatabase.DeleteUser("sysadmin");
                userDatabase.DeleteUser("DiscoveryAdmin");
                userDatabase.DeleteUser("CertificateAuthorityAdmin");

                //Create new admin user
                Console.Write("Please specify user name of the application admin user:");
                string username = Console.ReadLine();
                #pragma warning disable CA2208 // Justification: Public sample API compatibility is preserved.
                _ = username ?? throw new ArgumentNullException("User name is not allowed to be empty");
                #pragma warning restore CA2208

                Console.Write($"Please specify the password of {username}:");

                //string password = Console.ReadLine();
                string password = GetPassword();
                #pragma warning disable CA2208 // Justification: Public sample API compatibility is preserved.
                _ = password ?? throw new ArgumentNullException("Password is not allowed to be empty");
                #pragma warning restore CA2208

                //create User, if User exists delete & recreate
                if (!userDatabase.CreateUser(username, Encoding.UTF8.GetBytes(password), new List<Role>() { Role.AuthenticatedUser, GdsRole.CertificateAuthorityAdmin, GdsRole.DiscoveryAdmin }))
                {
                    userDatabase.DeleteUser(username);
                    userDatabase.CreateUser(username, Encoding.UTF8.GetBytes(password), new List<Role>() { Role.AuthenticatedUser, GdsRole.CertificateAuthorityAdmin, GdsRole.DiscoveryAdmin });
                }
            }
            else
            {
                //delete existing standard users
                userDatabase.DeleteUser("appadmin");
                userDatabase.DeleteUser("appuser");
                userDatabase.DeleteUser("sysadmin");
                userDatabase.DeleteUser("DiscoveryAdmin");
                userDatabase.DeleteUser("CertificateAuthorityAdmin");

                //create standard users
                userDatabase.CreateUser(
                    "sysadmin",
                    Encoding.UTF8.GetBytes("demo"),
                    [GdsRole.CertificateAuthorityAdmin, GdsRole.DiscoveryAdmin, Role.SecurityAdmin, Role
                    .ConfigureAdmin]);
                userDatabase.CreateUser(
                    "appadmin",
                    Encoding.UTF8.GetBytes("demo"),
                    [Role.AuthenticatedUser, GdsRole.CertificateAuthorityAdmin, GdsRole
                    .DiscoveryAdmin]);
                userDatabase.CreateUser("appuser", Encoding.UTF8.GetBytes("demo"), [Role.AuthenticatedUser]);

                userDatabase.CreateUser(
                    "DiscoveryAdmin",
                    Encoding.UTF8.GetBytes("demo"),
                    [Role.AuthenticatedUser, GdsRole.DiscoveryAdmin]);
                userDatabase.CreateUser(
                    "CertificateAuthorityAdmin",
                    Encoding.UTF8.GetBytes("demo"),
                    [Role.AuthenticatedUser, GdsRole.CertificateAuthorityAdmin]);
            }

            return createStandardUsers;
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
            string item = Utils.Format("{0,9}:{1,20}:", reason, session.ReadDiagnostics(d => d.SessionName));
            if (lastContact)
            {
                item += Utils.Format("Last Event:{0:HH:mm:ss}", session.ReadDiagnostics(d => d.ClientLastContactTime).ToLocalTime());
            }
            else
            {
                if (session.Identity != null)
                {
                    item += Utils.Format(":{0,20}", session.Identity.DisplayName);
                }
                item += Utils.Format(":{0}", session.Id);
            }
            Console.WriteLine(item);
        }

        private async void StatusThreadAsync()
        {
            while (server != null)
            {
                if (DateTime.UtcNow - lastEventTime > TimeSpan.FromMilliseconds(6000))
                {
                    IList<ISession> sessions = server.CurrentInstance.SessionManager.GetSessions();
                    for (int ii = 0; ii < sessions.Count; ii++)
                    {
                        ISession session = sessions[ii];
                        PrintSessionStatus(session, "-Status-", true);
                    }
                    lastEventTime = DateTime.UtcNow;
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        private static string GetPassword()
        {
            StringBuilder input = new StringBuilder();
            while (true)
            {
                int x = Console.CursorLeft;
                int y = Console.CursorTop;
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                if (key.Key == ConsoleKey.Backspace && input.Length > 0)
                {
                    input.Remove(input.Length - 1, 1);
                    Console.SetCursorPosition(x - 1, y);
                    Console.Write(" ");
                    Console.SetCursorPosition(x - 1, y);
                }
                else if (key.KeyChar < 32 || key.KeyChar > 126)
                {
                    Trace.WriteLine("Output suppressed: no key char"); //catch non-printable chars, e.g F1, CursorUp and so ...
                }
                else if (key.Key != ConsoleKey.Backspace)
                {
                    input.Append(key.KeyChar);
                    Console.Write("*");
                }
            }
            return input.ToString();
        }
    }
}
