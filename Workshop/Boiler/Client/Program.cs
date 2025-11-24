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
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;

namespace Quickstarts.Boiler.Client
{
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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance application = new ApplicationInstance(m_telemetry);
            application.ApplicationType = ApplicationType.Client;
            application.ConfigSectionName = "BoilerClient";

            try
            {
                // load the application configuration.
                application.LoadApplicationConfigurationAsync(false).AsTask().Wait();

                // check the application certificate.
                application.CheckApplicationInstanceCertificatesAsync(false).AsTask().Wait();

                // run the application interactively.
                Application.Run(new MainForm(application.ApplicationConfiguration, m_telemetry));
            }
            catch (Exception e)
            {
                ExceptionDlg.Show(m_telemetry, application.ApplicationName, e);
                return;
            }
        }
    }

    /// <summary>
    /// The <b>BoilerClient</b> namespace contains classes which implement a Quickstart Client.
    /// </summary>
    /// <exclude/>
    [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class NamespaceDoc
    {
    }
}
