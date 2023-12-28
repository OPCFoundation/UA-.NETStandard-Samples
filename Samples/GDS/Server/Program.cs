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
using Opc.Ua.Server.Controls;
using System;
using System.Data.Entity;

namespace Opc.Ua.Gds.Server
{
    static class Program
    {
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
            ApplicationInstance application = new ApplicationInstance
            {
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "Opc.Ua.GlobalDiscoveryServer"
            };

            try
            {
                // load the application configuration.
                var config = application.LoadApplicationConfiguration(false).Result;

                // check the application certificate.
                bool haveAppCertificate = application.CheckApplicationInstanceCertificate(false, 0).Result;
                if (!haveAppCertificate)
                {
                    throw new Exception("Application instance certificate invalid!");
                }

               
                // load the user database.
                var userDatabase = new SqlUsersDatabase();
                //initialize users Database
                userDatabase.Initialize();

                bool createStandardUsers = ConfigureUsers(userDatabase);
                

                // start the server.
                var database = new SqlApplicationsDatabase();
                var server = new GlobalDiscoverySampleServer(
                    database,
                    database,
                    new CertificateGroup(),
                    userDatabase,
                    true,
                    createStandardUsers);
                application.Start(server).Wait();

                // run the application interactively.
                System.Windows.Forms.Application.Run(new ServerForm(server, application.ApplicationConfiguration));
            }
            catch (Exception e)
            {
                ExceptionDlg.Show(application.ApplicationName, e);
            }
        }

        private static bool ConfigureUsers(SqlUsersDatabase userDatabase)
        {
            ApplicationInstance.MessageDlg.Message("Use default users?", true);
            bool createStandardUsers = ApplicationInstance.MessageDlg.ShowAsync().Result;
            if (!createStandardUsers)
            {
                //Delete existing standard users
                userDatabase.DeleteUser("appadmin");
                userDatabase.DeleteUser("appuser");
                userDatabase.DeleteUser("sysadmin");

                //Create new admin user
                string username = InputDlg.Show("Please specify user name of the application admin user:", false);
                _ = username ?? throw new ArgumentNullException("User name is not allowed to be empty");

                Console.Write($"Please specify the password of {username}:");

                string password = InputDlg.Show($"Please specify the password of {username}:", true);
                _ = password ?? throw new ArgumentNullException("Password is not allowed to be empty");

                //create User, if User exists delete & recreate
                if (!userDatabase.CreateUser(username, password, GdsRole.ApplicationAdmin))
                {
                    userDatabase.DeleteUser(username);
                    userDatabase.CreateUser(username, password, GdsRole.ApplicationAdmin);
                }
            }
            return createStandardUsers;
        }
    }
}
