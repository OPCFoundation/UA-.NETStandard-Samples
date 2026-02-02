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
            Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddConsole();
            })
            )
        {
        }
    }

    public class NetCoreGlobalDiscoveryServer
    {
        private GlobalDiscoverySampleServer server;
        private readonly ITelemetryContext m_telemetry = new ConsoleTelemetry();
        private Task status;
        private DateTime lastEventTime;
        public static ExitCode exitCode;

        public NetCoreGlobalDiscoveryServer()
        {
        }

        public async Task RunAsync()
        {
            try
            {
                exitCode = ExitCode.ErrorServerNotStarted;
                await ConsoleGlobalDiscoveryServerAsync(m_telemetry).ConfigureAwait(false);
                Console.WriteLine("Server started. Press Ctrl-C to exit...");
                exitCode = ExitCode.ErrorServerRunning;
            }
            catch (Exception ex)
            {
                m_telemetry.CreateLogger<NetCoreGlobalDiscoveryServer>()
                    .LogError("ServiceResultException:" + ex.Message);
                Console.WriteLine("Exception: {0}", ex.Message);
                exitCode = ExitCode.ErrorServerException;
                return;
            }

            using (ManualResetEvent quitEvent = new ManualResetEvent(false))
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

                // wait for timeout or Ctrl-C
                quitEvent.WaitOne();
            }

            if (server != null)
            {
                Console.WriteLine("Server stopped. Waiting for exit...");

                using (GlobalDiscoverySampleServer _server = server)
                {
                    // Stop status thread
                    server = null;
                    await status.ConfigureAwait(false);
                    // Stop server and dispose
                    await _server.StopAsync();
                }
            }

            exitCode = ExitCode.Ok;
        }

        public static ExitCode ExitCode => exitCode;

        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                // GDS accepts any client certificate
                e.Accept = true;
                Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
            }
        }

        private async Task ConsoleGlobalDiscoveryServerAsync(ITelemetryContext telemetry)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            var application = new ApplicationInstance(telemetry)
            {
                ApplicationName = "Global Discovery Server",
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "Opc.Ua.GlobalDiscoveryServer"
            };

            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfigurationAsync(false).ConfigureAwait(false);

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificatesAsync(false).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (!config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }

            // get the DatabaseStorePath configuration parameter.
            GlobalDiscoveryServerConfiguration gdsConfiguration = config.ParseExtension<GlobalDiscoveryServerConfiguration>();
            string databaseStorePath = Utils.ReplaceSpecialFolderNames(gdsConfiguration.DatabaseStorePath);
            string userdatabaseStorePath = Utils.ReplaceSpecialFolderNames(gdsConfiguration.UsersDatabaseStorePath);

            var database = JsonApplicationsDatabase.Load(databaseStorePath);
            var userDatabase = JsonUserDatabase.Load(userdatabaseStorePath, telemetry);

            bool createStandardUsers = ConfigureUsers(userDatabase);

            // start the server.
            server = new GlobalDiscoverySampleServer(
                database,
                database,
                new CertificateGroup(telemetry),
                userDatabase,
                true);
            await application.StartAsync(server).ConfigureAwait(false);

            // print endpoint info
            IEnumerable<string> endpoints = application.Server.GetEndpoints().Select(e => e.EndpointUrl).Distinct();
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
                _ = username ?? throw new ArgumentNullException("User name is not allowed to be empty");

                Console.Write($"Please specify the password of {username}:");

                //string password = Console.ReadLine();
                string password = GetPassword();
                _ = password ?? throw new ArgumentNullException("Password is not allowed to be empty");

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
            lock (session.DiagnosticsLock)
            {
                string item = Utils.Format("{0,9}:{1,20}:", reason, session.SessionDiagnostics.SessionName);
                if (lastContact)
                {
                    item += Utils.Format("Last Event:{0:HH:mm:ss}", session.SessionDiagnostics.ClientLastContactTime.ToLocalTime());
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
