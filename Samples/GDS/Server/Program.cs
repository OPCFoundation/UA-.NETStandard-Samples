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

using Microsoft.Extensions.Logging;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Gds.Server.Database.Sql;
using Opc.Ua.Server;
using Opc.Ua.Server.Controls;
using System;
using System.Collections.Generic;
using System.Text;

[assembly: System.Resources.NeutralResourcesLanguage("en-US")]

namespace Opc.Ua.Gds.Server
{
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
    static class Program
    {
        private static readonly ITelemetryContext m_telemetry = new ConsoleTelemetry();

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Initialize the user interface.
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance application = new ApplicationInstance(m_telemetry)
            {
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "Opc.Ua.GlobalDiscoveryServer"
            };

            try
            {
                // load the application configuration.
                var config = application.LoadApplicationConfigurationAsync(false).AsTask().GetAwaiter().GetResult();

                // check the application certificate.
                bool haveAppCertificate = application.CheckApplicationInstanceCertificatesAsync(false).AsTask().GetAwaiter().GetResult();
                if (!haveAppCertificate)
                {
                    #pragma warning disable CA2201 // Justification: Public sample API compatibility is preserved.
                    throw new Exception("Application instance certificate invalid!");
                    #pragma warning restore CA2201
                }

                ILogger logger = m_telemetry.CreateLogger<SqlUsersDatabase>();
                // load the user database.
                var userDatabase = new SqlUsersDatabase();
                //initialize users Database
                userDatabase.Initialize(logger);

                bool createStandardUsers = ConfigureUsers(userDatabase);


                // start the server.
                var database = new SqlApplicationsDatabase();

                // GDS master AliasNames list (issue #274): merge each
                // registered server's AliasNames into a master list served by
                // this GDS.
                var aliasMerger = new GlobalDiscoveryServerAliasMerger(m_telemetry);

                #pragma warning disable CA2000 // Justification: WinForms/sample ownership or lifetime is managed outside the local scope.
                var server = new AliasMergingGlobalDiscoverySampleServer(
                #pragma warning restore CA2000
                    database,
                    database,
                    new CertificateGroup(m_telemetry),
                    userDatabase,
                    m_telemetry,
                    aliasMerger,
                    true);
                application.StartAsync(server).Wait();

                // start refreshing the master AliasNames list and merge each
                // server as soon as it registers.
                aliasMerger.Start(application.ApplicationConfiguration, database);
                database.ApplicationRegistered += (sender, app) =>
                    aliasMerger.QueueMerge(
                        app.ApplicationUri,
                        app.DiscoveryUrls.IsNull
                            ? new List<string>()
                            : app.DiscoveryUrls.ToArray());

                // run the application interactively.
                #pragma warning disable CA2000 // Justification: WinForms/sample ownership or lifetime is managed outside the local scope.
                System.Windows.Forms.Application.Run(new ServerForm(server, application.ApplicationConfiguration, m_telemetry));
                #pragma warning restore CA2000

                aliasMerger.Dispose();
            }
            catch (Exception e)
            {
                ExceptionDlg.Show(m_telemetry, application.ApplicationName, e);
            }
        }

        private static bool ConfigureUsers(SqlUsersDatabase userDatabase)
        {
            ApplicationInstance.MessageDlg.Message("Use default users?", true);
            bool createStandardUsers = ApplicationInstance.MessageDlg.ShowAsync().GetAwaiter().GetResult();
            if (!createStandardUsers)
            {
                //Delete existing standard users
                userDatabase.DeleteUser("appadmin");
                userDatabase.DeleteUser("appuser");
                userDatabase.DeleteUser("sysadmin");
                userDatabase.DeleteUser("DiscoveryAdmin");
                userDatabase.DeleteUser("CertificateAuthorityAdmin");

                //Create new admin user
                string username = InputDlg.Show("Please specify user name of the application admin user:", false);
                #pragma warning disable CA2208 // Justification: Public sample API compatibility is preserved.
                _ = username ?? throw new ArgumentNullException("User name is not allowed to be empty");
                #pragma warning restore CA2208

                Console.Write($"Please specify the password of {username}:");

                string password = InputDlg.Show($"Please specify the password of {username}:", true);
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
    }
}
