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
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;

namespace Opc.Ua.Sample
{
    public sealed class ConsoleTelemetry : TelemetryContextBase
    {
        public ConsoleTelemetry()
        : base(
            #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance application = new ApplicationInstance(m_telemetry);
            application.ApplicationName = "UA Sample Client";
            application.ApplicationType = ApplicationType.ClientAndServer;
            application.ConfigSectionName = "Opc.Ua.SampleClient";

            try
            {
                application.LoadApplicationConfigurationAsync(false).AsTask().GetAwaiter().GetResult();

                // check the application certificate.
                bool certOK = application.CheckApplicationInstanceCertificatesAsync(false).AsTask().Result;
                if (!certOK)
                {
                    #pragma warning disable CA2201 // Justification: Sample code retains existing ownership/lifetime and behavior.
                    throw new Exception("Application instance certificate invalid!");
                    #pragma warning restore CA2201
                }

                // start the server.
                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                application.StartAsync(new SampleServer(m_telemetry)).Wait();
                #pragma warning restore CA2000

                // run the application interactively.
                #pragma warning disable CA2000 // Justification: Sample code retains existing ownership/lifetime and behavior.
                Application.Run(new SampleClientForm(application, null, application.ApplicationConfiguration, m_telemetry));
                #pragma warning restore CA2000
            }
            catch (Exception e)
            {
                ExceptionDlg.Show(m_telemetry, application.ApplicationName, e);
            }
            finally
            {
                // ApplicationInstance is only IAsyncDisposable, and Main is synchronous
                application.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }
}
