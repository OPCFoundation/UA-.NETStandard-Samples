/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Runs test code on a real WinForms message loop.
    /// </summary>
    /// <remarks>
    /// The sample clients are WinForms applications: their controls need an STA thread, and
    /// their asynchronous callbacks are marshalled back with <c>BeginInvoke</c>, which needs a
    /// pumping message loop. The harness provides both, without ever showing a window, and
    /// watches for the modal dialogs the samples use to report errors.
    /// </remarks>
    public static class WinFormsHarness
    {
        /// <summary>
        /// Runs the body on a dedicated STA thread with a message loop, and fails if the
        /// sample opened a dialog while it ran.
        /// </summary>
        /// <param name="body">The test body. It receives the watchdog of the run.</param>
        /// <param name="timeout">How long the body may take.</param>
        public static async Task RunAsync(Func<DialogWatchdog, Task> body, TimeSpan timeout)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            var completion = new TaskCompletionSource<DialogWatchdog>(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() => RunMessageLoop(body, completion)) {
                IsBackground = true,
                Name = "WinForms sample client test",
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(timeout)).ConfigureAwait(false);

            if (finished != completion.Task)
            {
                throw new TimeoutException(
                    $"The sample client did not finish within {timeout.TotalSeconds:F0} seconds.");
            }

            DialogWatchdog watchdog = await completion.Task.ConfigureAwait(false);

            if (watchdog.Captured.Count > 0)
            {
                throw new InvalidOperationException(
                    "The sample opened a dialog, which in an interactive session a user would " +
                    $"have to click away:{Environment.NewLine}{watchdog.Describe()}");
            }
        }

        /// <summary>
        /// The connect control of a sample client main form.
        /// </summary>
        /// <remarks>
        /// Every sample client hosts the shared <see cref="ConnectServerCtrl"/> under the same
        /// designer generated name. The field is private, and reflection is used rather than
        /// changing the samples to expose it just for the tests.
        /// </remarks>
        public static ConnectServerCtrl GetConnectControl(Form form)
        {
            if (form == null)
            {
                throw new ArgumentNullException(nameof(form));
            }

            const string fieldName = "ConnectServerCTRL";

            for (Type type = form.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

                if (field?.GetValue(form) is ConnectServerCtrl control)
                {
                    return control;
                }
            }

            throw new InvalidOperationException(
                $"{form.GetType().FullName} has no '{fieldName}' of type {nameof(ConnectServerCtrl)}. " +
                "Either the client no longer uses the shared connect control, or the designer renamed the field.");
        }

        /// <summary>
        /// Reads a control of the form by its designer name, to check what the sample enabled
        /// or filled in after connecting.
        /// </summary>
        public static Control FindControl(Form form, string name)
        {
            if (form == null)
            {
                throw new ArgumentNullException(nameof(form));
            }

            for (Type type = form.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

                if (field?.GetValue(form) is Control control)
                {
                    return control;
                }
            }

            return null;
        }

        private static void RunMessageLoop(Func<DialogWatchdog, Task> body, TaskCompletionSource<DialogWatchdog> completion)
        {
            var context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);

            var watchdog = new DialogWatchdog();
            watchdog.Start();

            // an unhandled exception on the UI thread otherwise opens the WinForms
            // ThreadExceptionDialog, which is a modal dialog nobody will click away. Handling
            // the event keeps the loop under the control of the test.
            bool running = true;
            var onUiThread = new ThreadExceptionEventHandler((sender, e) => {
                if (running)
                {
                    completion.TrySetException(e.Exception);
                    Application.ExitThread();
                    return;
                }

                // after the body is done the form is being disposed, and a callback of the
                // sample which arrives late finds a window that is already gone. That is the
                // harness tearing down, not the sample failing.
                watchdog.NoteDuringTeardown(e.Exception);
            });

            Application.ThreadException += onUiThread;

            try
            {
                // per thread, not for the whole process: the process wide setting can only be
                // made once and before the first window exists, and every test brings its own
                // thread
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException, false);
            }
            catch (InvalidOperationException)
            {
                // a window already exists on this thread, so the mode is what it is
            }

            context.Post(
                async _ => {
                    try
                    {
                        await body(watchdog).ConfigureAwait(true);
                        completion.TrySetResult(watchdog);
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                    finally
                    {
                        running = false;
                        watchdog.Stop();
                        Application.ExitThread();
                    }
                },
                null);

            try
            {
                Application.Run();
            }
            finally
            {
                Application.ThreadException -= onUiThread;
                watchdog.Dispose();
                context.Dispose();

                // if the loop ended without the body reporting anything, do not leave the
                // caller waiting for a result which will never arrive
                completion.TrySetException(
                    new InvalidOperationException("The message loop of the sample client ended unexpectedly."));
            }
        }
    }
}
