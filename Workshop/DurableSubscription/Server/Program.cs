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
using Opc.Ua.Samples.Hosting;
using Opc.Ua.Server.Controls;

[assembly: System.Resources.NeutralResourcesLanguage("en-US")]

namespace Quickstarts.DurableSubscriptionServer
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

            // the generic host owns the logging and the lifetime of the sample; the
            // server is hosted by the stack, its configuration turns durable
            // subscriptions on, and the form shows the running server.
            SampleWinFormsHost.Run<ServerForm>(
                args,
                services => services
                    .AddDurableSubscriptionServer(),
                ExceptionDlg.Show);
        }
    }

    /// <summary>
    /// The <b>DurableSubscriptionServer</b> namespace contains a Quickstart Server which
    /// has durable subscriptions turned on, so a client can transfer a subscription back
    /// after a disconnect, a close or a restart and recover the values it missed.
    /// </summary>
    /// <exclude/>
    [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class NamespaceDoc
    {
    }
}
