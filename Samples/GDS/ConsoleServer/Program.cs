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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Samples.Hosting;
using Mono.Options;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Onboarding;
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
            await server.RunAsync(args).ConfigureAwait(false);

            return (int)NetCoreGlobalDiscoveryServer.ExitCode;
        }
    }

    #pragma warning disable CA1001, CA1708 // Justification: Public sample API compatibility is preserved.
    public class NetCoreGlobalDiscoveryServer
    #pragma warning restore CA1001, CA1708
    {
        private SampleGlobalDiscoveryServer server;
        private IHost m_host;
        private ITelemetryContext m_telemetry;
        // a completed task until the status thread is started, so a failure between
        // the server being created and the thread starting still shuts down cleanly.
        private Task status = Task.CompletedTask;
        private DateTime lastEventTime;
        private ApplicationConfiguration m_configuration;
        private IApplicationsDatabase m_database;
        private LdsScannerConfiguration m_ldsScannerConfiguration;
        private GlobalDiscoveryServerAliasMerger m_aliasMerger;
        // OPC 10000-12 §7.2 ApplicationAdmin grants, §7.10.13 ResetToServerDefaults and the
        // OPC 10000-21 ticket store, kept so the interactive commands can reach them.
        private GdsApplicationAdminUserDatabase m_userDatabase;
        private SampleServerConfigurationResetProvider m_resetProvider;
        private ITicketStore m_ticketStore;
        #pragma warning disable CA2211 // Justification: Public sample API compatibility is preserved.
        public static ExitCode exitCode;
        #pragma warning restore CA2211

        public NetCoreGlobalDiscoveryServer()
        {
        }

        public async Task RunAsync(string[] args)
        {
            try
            {
                exitCode = ExitCode.ErrorServerNotStarted;
                await ConsoleGlobalDiscoveryServerAsync(args).ConfigureAwait(false);
                Console.WriteLine("Server started.");
                PrintCommandHelp();
                exitCode = ExitCode.ErrorServerRunning;
            }
            catch (Exception ex)
            {
                #pragma warning disable CA2254 // Justification: Public sample API compatibility is preserved.
                m_telemetry?.CreateLogger<NetCoreGlobalDiscoveryServer>()
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
                        case "T":
                        case "TICKETS":
                            await ListOnboardingTicketsAsync().ConfigureAwait(false);
                            break;
                        case "A":
                        case "ADMINS":
                            RunApplicationAdminWorkflow();
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

                // stop the status thread, then let the host stop and dispose the
                // server it started.
                server = null;
                await status.ConfigureAwait(false);
            }

            if (m_host != null)
            {
                IHost host = m_host;
                m_host = null;

                await host.StopAsync().ConfigureAwait(false);
                if (host is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    host.Dispose();
                }
            }

            SampleLogging.CloseAndFlush();

            exitCode = ExitCode.Ok;
        }

        public static ExitCode ExitCode => exitCode;

        private static void PrintCommandHelp()
        {
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  scan    - scan the configured LDS(es) for servers and stage them for approval");
            Console.WriteLine("  admins  - list and edit the OPC 10000-12 ApplicationAdmin grants");
            Console.WriteLine("  tickets - list the OPC 10000-21 onboarding tickets the registrar holds");
            Console.WriteLine("  help    - show this message");
            Console.WriteLine("  quit    - stop the server and exit (Ctrl-C also works)");
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

        /// <summary>
        /// Prints the OPC 10000-21 onboarding tickets currently loaded into the registrar.
        /// </summary>
        /// <remarks>
        /// Tickets get here through the <c>RegisterTickets</c> Method on the registrar
        /// administration Object - the GDS client's <b>Onboarding</b> panel drives it, and so
        /// does any client using the SDK's <c>OnboardingClient</c>.
        /// </remarks>
        private async Task ListOnboardingTicketsAsync()
        {
            if (m_ticketStore == null)
            {
                Console.WriteLine("The onboarding registrar is not configured.");
                return;
            }

            int count = 0;

            await foreach (TicketRecord ticket in m_ticketStore.ListAsync().ConfigureAwait(false))
            {
                count++;
                Console.WriteLine("  Ticket id      : {0}", ticket.TicketId);
                Console.WriteLine("  Kind           : {0}", ticket.Metadata.Kind);
                Console.WriteLine("  Manufacturer   : {0}", ticket.Metadata.ManufacturerName);
                Console.WriteLine("  Model          : {0}", ticket.Metadata.ModelName);
                Console.WriteLine("  Serial number  : {0}", ticket.Metadata.SerialNumber);
                Console.WriteLine("  Product URI    : {0}", ticket.Metadata.ProductInstanceUri);
                Console.WriteLine("  Registered at  : {0:u}", ticket.CreatedAt);
                Console.WriteLine("  Encoded length : {0} byte(s)", ticket.EncodedTicket?.Length ?? 0);
                Console.WriteLine();
            }

            if (count == 0)
            {
                Console.WriteLine("The registrar holds no onboarding tickets.");
            }
        }

        /// <summary>
        /// Lists and edits the OPC 10000-12 §7.2 <c>ApplicationAdmin</c> grants.
        /// </summary>
        /// <remarks>
        /// A user holding <c>ApplicationAdmin</c> may administer exactly the applications
        /// granted here - unlike <c>DiscoveryAdmin</c>, which may administer all of them. The
        /// grants are matched against the target application of every GDS Method call by the
        /// stack's <c>AuthorizationHelper</c>.
        /// </remarks>
        private void RunApplicationAdminWorkflow()
        {
            if (m_userDatabase == null || m_database == null)
            {
                Console.WriteLine("The server is not ready yet.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Current ApplicationAdmin grants:");

            IReadOnlyDictionary<string, IReadOnlyList<string>> grants = m_userDatabase.Grants;

            if (grants.Count == 0)
            {
                Console.WriteLine("  (none)");
            }
            else
            {
                foreach (KeyValuePair<string, IReadOnlyList<string>> grant in grants)
                {
                    Console.WriteLine("  {0}: {1}", grant.Key, string.Join(", ", grant.Value));
                }
            }

            Console.WriteLine();
            Console.Write("User to grant ApplicationAdmin to (empty to cancel): ");
            string userName = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(userName))
            {
                return;
            }

            userName = userName.Trim();

            ApplicationDescription[] applications = m_database.QueryApplications(
                startingRecordId: 0,
                maxRecordsToReturn: 0,
                applicationName: string.Empty,
                applicationUri: string.Empty,
                applicationType: 0,
                productUri: string.Empty,
                serverCapabilities: ArrayOf<string>.Empty,
                lastCounterResetTime: out _,
                nextRecordId: out _);

            var selected = new List<NodeId>();

            foreach (ApplicationDescription application in applications ?? Array.Empty<ApplicationDescription>())
            {
                ApplicationRecordDataType[] records = m_database.FindApplications(application.ApplicationUri);

                if (records == null || records.Length == 0)
                {
                    continue;
                }

                foreach (ApplicationRecordDataType record in records)
                {
                    Console.Write(
                        "  Administer '{0}' ({1})? (y/n, default n): ",
                        application.ApplicationUri, record.ApplicationId);

                    string answer = Console.ReadLine();

                    if (!string.IsNullOrEmpty(answer) &&
                        (answer.StartsWith('y') || answer.StartsWith('Y')))
                    {
                        selected.Add(record.ApplicationId);
                    }
                }
            }

            if (selected.Count == 0)
            {
                if (m_userDatabase.RevokeApplicationAdmin(userName))
                {
                    Console.WriteLine("Revoked every ApplicationAdmin grant of '{0}'.", userName);
                }
                else
                {
                    Console.WriteLine("Nothing selected, '{0}' is unchanged.", userName);
                }

                return;
            }

            m_userDatabase.GrantApplicationAdmin(userName, selected);

            Console.WriteLine(
                "Granted '{0}' the ApplicationAdmin privilege for {1} application(s).",
                userName, selected.Count);
            Console.WriteLine(
                "Assign the ApplicationAdmin role to the user as well for the grant to take effect.");
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

        private async Task ConsoleGlobalDiscoveryServerAsync(string[] args)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();

            // the generic host owns the logging and the lifetime of the sample; the
            // server is hosted by the stack, its configuration is loaded straight from
            // the configuration file. A console sample logs to the console as well as
            // to the file the configuration names.
            HostApplicationBuilder builder = SampleHost.CreateBuilder(args);

            builder.Logging.AddSampleConsole();

            builder.Services
                .AddSampleServer(
                    "Opc.Ua.GlobalDiscoveryServer.Config.xml",
                    CreateServer,
                    AcceptUntrustedCertificatesInteractively);

            m_host = builder.Build();
            m_telemetry = m_host.Services.GetRequiredService<ITelemetryContext>();

            await m_host.StartAsync().ConfigureAwait(false);

            server = m_host.Services.GetRequiredService<SampleGlobalDiscoveryServer>();

            ApplicationConfiguration config = m_host.Services.GetRequiredService<ApplicationConfiguration>();

            // keep the configuration and database so the interactive LDS scan
            // can register approved servers after start-up.
            m_configuration = config;
            m_ldsScannerConfiguration = LdsScannerConfiguration.Parse(config);

            // start periodically refreshing the GDS master AliasNames list
            // from every registered server.
            m_aliasMerger.Start(config, m_database);

            // remember what "the server defaults" are before the first client can change
            // them, so OPC 10000-12 §7.10.13 ResetToServerDefaults has something to restore.
            await m_resetProvider.CaptureDefaultsAsync().ConfigureAwait(false);

            // print endpoint info
            IEnumerable<string> endpoints = server.GetEndpoints().ToArray().Select(e => e.EndpointUrl).Distinct();
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

        /// <summary>
        /// Creates the server, together with the databases, the alias merger, the
        /// ManagedApplications store, the onboarding ticket store and the Optional
        /// ServerConfiguration surface it is built on. Called by the host once the
        /// configuration has been read.
        /// </summary>
        private SampleGlobalDiscoveryServer CreateServer(IServiceProvider provider)
        {
            ITelemetryContext telemetry = provider.GetRequiredService<ITelemetryContext>();
            ApplicationConfiguration config = provider.GetRequiredService<ApplicationConfiguration>();

            // get the DatabaseStorePath configuration parameter.
            GlobalDiscoveryServerConfiguration gdsConfiguration = config.ParseExtension<GlobalDiscoveryServerConfiguration>();
            string databaseStorePath = Utils.ReplaceSpecialFolderNames(gdsConfiguration.DatabaseStorePath);
            string userdatabaseStorePath = Utils.ReplaceSpecialFolderNames(gdsConfiguration.UsersDatabaseStorePath);

            var database = JsonApplicationsDatabase.Load(databaseStorePath);

            // the users database is wrapped so the GDS can also answer "which applications
            // may this user administer?" - the OPC 10000-12 §7.2 ApplicationAdmin privilege.
            m_userDatabase = new GdsApplicationAdminUserDatabase(
                JsonUserDatabase.Load(userdatabaseStorePath, telemetry),
                Path.Combine(
                    Path.GetDirectoryName(userdatabaseStorePath) ?? string.Empty,
                    "gdsapplicationadmins.json"));

            ConfigureUsers(m_userDatabase);

            m_database = database;

            // GDS master AliasNames list (issue #274): merge each registered
            // server's AliasNames into a master list served by this GDS.
            m_aliasMerger = new GlobalDiscoveryServerAliasMerger(telemetry);

            // OPC 10000-21: the onboarding tickets the registrar accepts. In memory here -
            // a production registrar persists them.
            m_ticketStore = new MemoryTicketStore();

            m_resetProvider = new SampleServerConfigurationResetProvider(config, telemetry);

            return new SampleGlobalDiscoveryServer(
                database,
                database,
                #pragma warning disable CA2000 // Justification: Certificate group ownership is transferred to the server.
                new CertificateGroup(telemetry),
                #pragma warning restore CA2000
                m_userDatabase,
                telemetry,
                m_aliasMerger,
                true,
                new GdsManagedApplicationsDataStore(
                    database,
                    Path.Combine(
                        Path.GetDirectoryName(databaseStorePath) ?? string.Empty,
                        "ManagedApplications"),
                    telemetry),
                m_ticketStore,
                CreateServerConfigurationOptions(config, telemetry));
        }

        /// <summary>
        /// Turns on the Optional OPC 10000-12 §7.10.3 <c>ServerConfiguration</c> members.
        /// </summary>
        /// <remarks>
        /// Each member is suppressed from the address space unless it is configured here, so
        /// this method is the whole difference between a push server that only exposes the
        /// mandatory surface and one that also advertises <c>HasSecureElement</c>,
        /// <c>InApplicationSetup</c>, <c>ResetToServerDefaults</c> and <c>ConfigurationFile</c>.
        /// </remarks>
        private ServerConfigurationOptions CreateServerConfigurationOptions(
            ApplicationConfiguration config,
            ITelemetryContext telemetry)
        {
            return new ServerConfigurationOptions {
                // the sample keeps its private keys in the file system, not in a TPM or
                // secure element - reporting that honestly is the point of the Property.
                HasSecureElement = false,

                // the GDS is commissioned, not in the OPC 10000-21 setup state.
                InApplicationSetup = false,

                ResetProvider = m_resetProvider,

                ConfigurationFileProvider = new SampleApplicationConfigurationFileProvider(
                    config.SourceFilePath,
                    telemetry)
            };
        }

        /// <summary>
        /// Accepts an untrusted certificate after reporting it, which the
        /// configuration file cannot express.
        /// </summary>
        private static void AcceptUntrustedCertificatesInteractively(ApplicationConfiguration config)
        {
            if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                return;
            }

            config.CertificateManager.AcceptError = (certificate, error) => {
                bool accept = error.StatusCode == StatusCodes.BadCertificateUntrusted;
                if (accept)
                {
                    Console.WriteLine("Accepted Certificate: {0}", certificate.AsX509Certificate2().Subject);
                }
                return accept;
            };
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
                userDatabase.DeleteUser("ApplicationAdmin");

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
                userDatabase.DeleteUser("ApplicationAdmin");

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

                // OPC 10000-12 §7.2: an ApplicationAdmin may administer the applications it
                // was granted and no others. The role alone grants nothing - use the
                // 'admins' command to bind registered applications to this user.
                userDatabase.CreateUser(
                    "ApplicationAdmin",
                    Encoding.UTF8.GetBytes("demo"),
                    [Role.AuthenticatedUser, GdsRole.ApplicationAdmin]);
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
