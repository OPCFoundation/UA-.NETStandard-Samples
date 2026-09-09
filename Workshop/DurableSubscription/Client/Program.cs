/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.Hosting;

[assembly: System.Resources.NeutralResourcesLanguage("en-US")]

namespace Quickstarts.DurableSubscriptionClient
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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // the generic host owns the configuration, the certificate, the logging
            // and the lifetime of the client; the main form is created by the container
            // from the services registered here.
            SampleWinFormsHost.Run<MainForm>(
                args,
                services => services
                    .AddSampleApplication(options => {
                        options.ApplicationType = ApplicationType.Client;
                        options.ConfigurationFile = "Quickstarts.DurableSubscriptionClient.Config.xml";
                    })
                    // the client model the main form is built on
                    .AddDurableSubscriptionClient(),
                ExceptionDlg.Show);
        }
    }

    /// <summary>
    /// The <b>DurableSubscriptionClient</b> namespace contains a Quickstart Client which
    /// creates a durable subscription, persists it, restarts itself and transfers the
    /// subscription back to recover the values queued on the server while it was gone.
    /// </summary>
    /// <exclude/>
    [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class NamespaceDoc
    {
    }
}
