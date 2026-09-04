/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua.Configuration;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// The entry point of the Windows Forms samples: it builds the host, starts it
    /// and shows the main form the container creates.
    /// </summary>
    /// <remarks>
    /// The message loop is started before the host, and the asynchronous startup then
    /// runs on the user interface thread through the Windows Forms synchronization
    /// context. That keeps the thread a single threaded apartment - which the common
    /// dialogs, drag and drop and the clipboard need - without a blocking wait in the
    /// entry point of the sample.
    /// </remarks>
    public static class SampleWinFormsHost
    {
        /// <summary>
        /// Runs a sample whose main form the container can create on its own.
        /// </summary>
        /// <typeparam name="TMainForm">The main form of the sample.</typeparam>
        /// <param name="args">The command line of the sample.</param>
        /// <param name="configureServices">The registrations of the sample.</param>
        /// <param name="showException">Reports a startup failure to the user.</param>
        public static void Run<TMainForm>(
            string[] args,
            Action<IServiceCollection> configureServices,
            Action<ITelemetryContext, string, Exception> showException)
            where TMainForm : Form
            => Run(
                args,
                configureServices,
                provider => provider.GetRequiredService<IWindowFactory>().Create<TMainForm>(),
                showException);

        /// <summary>
        /// Builds the host, starts it on the user interface thread and shows the main
        /// form the container created.
        /// </summary>
        /// <param name="args">The command line of the sample.</param>
        /// <param name="configureServices">The registrations of the sample.</param>
        /// <param name="createMainForm">Creates the main form from the container.</param>
        /// <param name="showException">Reports a startup failure to the user.</param>
        private static void Run(
            string[] args,
            Action<IServiceCollection> configureServices,
            Func<IServiceProvider, Form> createMainForm,
            Action<ITelemetryContext, string, Exception> showException)
        {
            ArgumentNullException.ThrowIfNull(configureServices);
            ArgumentNullException.ThrowIfNull(createMainForm);
            ArgumentNullException.ThrowIfNull(showException);

            HostApplicationBuilder builder = SampleHost.CreateBuilder(args);

            // the forms of the sample create the dialogs they open from the container.
            builder.Services.AddSampleWindows();

            configureServices(builder.Services);

            IHost host = builder.Build();
            try
            {
                RunMessageLoop(host, createMainForm, showException);
            }
            finally
            {
                if (host is IAsyncDisposable asyncDisposable)
                {
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                else
                {
                    host.Dispose();
                }

                SampleLogging.CloseAndFlush();
            }
        }

        /// <summary>
        /// Starts the message loop, and the host inside it.
        /// </summary>
        private static void RunMessageLoop(
            IHost host,
            Func<IServiceProvider, Form> createMainForm,
            Action<ITelemetryContext, string, Exception> showException)
        {
            using var applicationContext = new ApplicationContext();

            // installed before the loop starts so the callback below can be queued to
            // the user interface thread. Application.Run keeps it in place.
            using var synchronizationContext = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);

            SynchronizationContext.Current.Post(
                _ => StartAsync(host, applicationContext, createMainForm, showException),
                null);

            Application.Run(applicationContext);

            host.StopAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Starts the host on the user interface thread and shows the main form.
        /// </summary>
        /// <remarks>
        /// Every continuation runs on the Windows Forms synchronization context, so
        /// the form is created on the thread which owns the message loop.
        /// </remarks>
        private static async void StartAsync(
            IHost host,
            ApplicationContext applicationContext,
            Func<IServiceProvider, Form> createMainForm,
            Action<ITelemetryContext, string, Exception> showException)
        {
            try
            {
                // the stack asks its questions - whether to create a certificate, for
                // example - through the message box the container registered.
                ApplicationInstance.MessageDlg = host.Services.GetRequiredService<IApplicationMessageDlg>();

                await host.StartAsync().ConfigureAwait(true);

                Form mainForm = createMainForm(host.Services);

                // the context ends the message loop when the main form is closed.
                applicationContext.MainForm = mainForm;
                mainForm.Show();
            }
            catch (Exception e)
            {
                ReportAndExit(host, applicationContext, showException, e);
            }
        }

        /// <summary>
        /// Shows a startup failure and closes the sample.
        /// </summary>
        private static void ReportAndExit(
            IHost host,
            ApplicationContext applicationContext,
            Action<ITelemetryContext, string, Exception> showException,
            Exception exception)
        {
            try
            {
                var telemetry = host.Services.GetRequiredService<ITelemetryContext>();
                string caption = host.Services.GetService<SampleApplication>()?.Instance.ApplicationName
                    ?? host.Services.GetService<SampleServerStartup>()?.ConfigurationOrNull?.ApplicationName
                    ?? Application.ProductName;

                showException(telemetry, caption, exception);
            }
            finally
            {
                applicationContext.ExitThread();
            }
        }
    }
}
