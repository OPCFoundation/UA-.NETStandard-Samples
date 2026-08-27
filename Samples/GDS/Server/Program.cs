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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server.Database.Sql;
using Opc.Ua.Samples.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Controls;

[assembly: System.Resources.NeutralResourcesLanguage("en-US")]

namespace Opc.Ua.Gds.Server
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Initialize the user interface.
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();

            // the generic host owns the configuration, the certificate, the logging
            // and the lifetime of the server; the databases, the alias merger and the
            // main form are resolved from the services registered here.
            SampleWinFormsHost.Run(
                args,
                ConfigureServices,
                CreateServerForm,
                ExceptionDlg.Show);
        }

        /// <summary>
        /// Registers everything the global discovery server is made of.
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            services
                .AddSampleApplication(options => {
                    options.ApplicationType = ApplicationType.Server;
                    options.ConfigSectionName = "Opc.Ua.GlobalDiscoveryServer";
                })
                .AddSingleton(CreateUserDatabase)
                .AddSingleton<SqlApplicationsDatabase>()
                .AddSingleton(provider => new GlobalDiscoveryServerAliasMerger(
                    provider.GetRequiredService<ITelemetryContext>()))
                .AddSampleServer(CreateServer)
                .AddHostedService<AliasMergerHostedService>();
        }

        /// <summary>
        /// Creates the user database and asks which users it should hold.
        /// </summary>
        private static SqlUsersDatabase CreateUserDatabase(IServiceProvider provider)
        {
            ILogger logger = provider.GetRequiredService<ITelemetryContext>()
                .CreateLogger<SqlUsersDatabase>();

            var userDatabase = new SqlUsersDatabase();
            userDatabase.Initialize(logger);

            ConfigureUsers(userDatabase);

            return userDatabase;
        }

        /// <summary>
        /// Creates the server, which merges the AliasNames list of every server which
        /// registers with it into a master list of its own (issue #274).
        /// </summary>
        private static AliasMergingGlobalDiscoverySampleServer CreateServer(IServiceProvider provider)
        {
            SqlApplicationsDatabase database = provider.GetRequiredService<SqlApplicationsDatabase>();
            ITelemetryContext telemetry = provider.GetRequiredService<ITelemetryContext>();

            return new AliasMergingGlobalDiscoverySampleServer(
                database,
                database,
                #pragma warning disable CA2000 // Justification: Certificate group ownership is transferred to the server.
                new CertificateGroup(telemetry),
                #pragma warning restore CA2000
                provider.GetRequiredService<SqlUsersDatabase>(),
                telemetry,
                provider.GetRequiredService<GlobalDiscoveryServerAliasMerger>(),
                true);
        }

        /// <summary>
        /// Creates the main form, which shows the state of the running server.
        /// </summary>
        private static System.Windows.Forms.Form CreateServerForm(IServiceProvider provider)
        {
            return new ServerForm(
                provider.GetRequiredService<AliasMergingGlobalDiscoverySampleServer>(),
                provider.GetRequiredService<ApplicationConfiguration>(),
                provider.GetRequiredService<ITelemetryContext>());
        }

        /// <summary>
        /// Keeps the master AliasNames list of the server up to date: it refreshes the
        /// list once the server runs, and merges every server as soon as it registers.
        /// </summary>
        /// <remarks>
        /// Registered after the server, so the host starts it once the server listens.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Created by the generic host.")]
        private sealed class AliasMergerHostedService : IHostedService
        {
            private readonly SampleApplication m_application;
            private readonly SqlApplicationsDatabase m_database;
            private readonly GlobalDiscoveryServerAliasMerger m_merger;

            public AliasMergerHostedService(
                SampleApplication application,
                SqlApplicationsDatabase database,
                GlobalDiscoveryServerAliasMerger merger)
            {
                m_application = application;
                m_database = database;
                m_merger = merger;
            }

            /// <inheritdoc/>
            public Task StartAsync(CancellationToken cancellationToken)
            {
                m_merger.Start(m_application.Configuration, m_database);
                m_database.ApplicationRegistered += OnApplicationRegistered;

                return Task.CompletedTask;
            }

            /// <inheritdoc/>
            public Task StopAsync(CancellationToken cancellationToken)
            {
                m_database.ApplicationRegistered -= OnApplicationRegistered;
                m_merger.Dispose();

                return Task.CompletedTask;
            }

            private void OnApplicationRegistered(object sender, ApplicationRegisteredEventArgs e)
            {
                m_merger.QueueMerge(
                    e.Application.ApplicationUri,
                    e.Application.DiscoveryUrls.IsNull
                        ? new List<string>()
                        : e.Application.DiscoveryUrls.ToArray());
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
