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
using Opc.Ua.Configuration;
using Opc.Ua.Samples.Hosting;
using Opc.Ua.Server.Controls;

[assembly: System.Resources.NeutralResourcesLanguage("en-US")]

namespace Quickstarts.NodeManagement.Server
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

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();

            // the generic host owns the logging and the lifetime of the sample; the
            // server is hosted by the stack, its configuration is loaded straight from
            // the configuration file, and the form shows the running server.
            SampleWinFormsHost.Run<ServerForm>(
                args,
                services => services
                    .AddNodeManagementServer(),
                ExceptionDlg.Show);
        }
    }

    /// <summary>
    /// The <b>NodeManagement.Server</b> namespace contains a Quickstart Server whose address
    /// space is built by its clients through the OPC UA Part 4 5.8 NodeManagement service set.
    /// </summary>
    /// <exclude/>
    [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class NamespaceDoc
    {
    }
}
