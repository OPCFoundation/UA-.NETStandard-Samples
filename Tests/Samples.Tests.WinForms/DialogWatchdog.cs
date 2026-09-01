/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Closes modal dialogs which pop up while a sample client is driven from a test, and
    /// remembers what they said.
    /// </summary>
    /// <remarks>
    /// The sample clients route their errors into a modal <c>ExceptionDlg</c>. In an
    /// interactive session somebody clicks it away; in a test run nobody does, and the test
    /// would wait forever. The watchdog turns those dialogs into a readable failure instead,
    /// which makes it the most important part of the client harness.
    /// </remarks>
    public sealed partial class DialogWatchdog : IDisposable
    {
        private readonly List<string> m_captured = [];
        private readonly List<string> m_duringTeardown = [];
        private readonly Dictionary<Type, string> m_accepted = [];
        private readonly Timer m_timer;
        private bool m_disposed;

        /// <summary>
        /// Creates a watchdog. It has to be created on the thread which runs the message loop.
        /// </summary>
        public DialogWatchdog()
        {
            m_timer = new Timer { Interval = 200 };
            m_timer.Tick += OnTick;
        }

        /// <summary>
        /// What the dialogs said, in the order they appeared.
        /// </summary>
        public IReadOnlyList<string> Captured => m_captured;

        /// <summary>
        /// Clicks a button of a dialog the sample opens on purpose, instead of cancelling it
        /// and reporting it as a complaint.
        /// </summary>
        /// <remarks>
        /// Not every modal dialog of a sample is an error. The UA Sample Client opens its
        /// session through a modal dialog, and a test which drives that sample has to answer
        /// it the way a user would - otherwise the only two outcomes are a cancelled connect
        /// or a hang. Registering the dialog here keeps everything else the sample opens a
        /// failure, so this cannot silently swallow the next complaint.
        /// </remarks>
        /// <typeparam name="TDialog">The dialog to answer.</typeparam>
        /// <param name="buttonName">The designer name of the button to click.</param>
        public void Accept<TDialog>(string buttonName) where TDialog : Form
        {
            m_accepted[typeof(TDialog)] = buttonName;
        }

        /// <summary>
        /// Starts watching.
        /// </summary>
        public void Start() => m_timer.Start();

        /// <summary>
        /// Stops watching.
        /// </summary>
        public void Stop() => m_timer.Stop();

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            m_timer.Stop();
            m_timer.Tick -= OnTick;
            m_timer.Dispose();
        }

        /// <summary>
        /// What went wrong on the UI thread after the test body was done.
        /// </summary>
        /// <remarks>
        /// The form is being disposed at that point and a late callback of the sample can
        /// find a window which is already gone. It is recorded for the test output rather
        /// than failing the test, because it says nothing about the sample.
        /// </remarks>
        public IReadOnlyList<string> DuringTeardown => m_duringTeardown;

        /// <summary>
        /// Records an exception which reached the UI thread while the harness was tearing
        /// the form down.
        /// </summary>
        public void NoteDuringTeardown(Exception exception)
        {
            if (exception != null)
            {
                m_duringTeardown.Add($"{exception.GetType().Name}: {exception.Message.Split('\n')[0].Trim()}");
            }
        }

        /// <summary>
        /// Renders what was captured, for a test failure message.
        /// </summary>
        public string Describe()
        {
            return string.Join(Environment.NewLine, m_captured);
        }

        private void OnTick(object sender, EventArgs e)
        {
            // this runs on the message loop of the test. An exception escaping from here
            // would come out of the loop rather than out of the sample, so the whole tick is
            // guarded, not only the parts which are known to be able to fail.
            try
            {
                CloseOpenDialogs();
            }
            catch (Exception exception)
            {
                m_captured.Add($"<the watchdog itself failed: {exception.GetType().Name}: {exception.Message}>");
            }
        }

        /// <summary>
        /// Closes the dialogs which belong to this test.
        /// </summary>
        /// <remarks>
        /// Application.OpenForms is process wide while every test runs on its own thread, so
        /// a dialog another test left behind would otherwise be reported here - and could not
        /// even be closed from this thread. Only forms of this thread are touched.
        /// </remarks>
        internal void CloseOpenDialogs()
        {
            // only modal dialogs are of interest: the form under test is never shown modally,
            // so anything modal is a popup the sample opened to complain about something
            foreach (Form form in Application.OpenForms
                .OfType<Form>()
                .Where(form => form.Modal && BelongsToThisThread(form))
                .ToArray())
            {
                if (m_accepted.TryGetValue(form.GetType(), out string buttonName))
                {
                    ClickAccepted(form, buttonName);
                    continue;
                }

                m_captured.Add(Describe(form));

                try
                {
                    form.DialogResult = DialogResult.Cancel;
                    form.Close();
                }
                catch (InvalidOperationException)
                {
                    // a dialog which is already closing must not break the watchdog
                }
                catch (COMException)
                {
                    // nor one whose window is already gone
                }
            }
        }

        /// <summary>
        /// Clicks the button of a dialog the test registered with <see cref="Accept{TDialog}"/>.
        /// </summary>
        /// <remarks>
        /// The button of a dialog which is already working on the previous click is disabled,
        /// and clicking it again would start the operation twice. Skipping a disabled button
        /// is what lets the watchdog keep ticking while the dialog does its work.
        /// </remarks>
        private void ClickAccepted(Form form, string buttonName)
        {
            try
            {
                if (FindButton(form, buttonName) is not Button button)
                {
                    m_captured.Add($"{Describe(form)} <no button '{buttonName}' to click>");
                    form.DialogResult = DialogResult.Cancel;
                    form.Close();
                    return;
                }

                if (button.Enabled)
                {
                    button.PerformClick();
                }
            }
            catch (Exception e) when (e is InvalidOperationException or COMException or ObjectDisposedException)
            {
                // a dialog which closed itself between the scan and the click is not a failure
            }
        }

        /// <summary>
        /// Finds a button of a dialog by its designer name.
        /// </summary>
        private static Control FindButton(Control parent, string name)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is Button && string.Equals(child.Name, name, StringComparison.Ordinal))
                {
                    return child;
                }

                Control found = FindButton(child, name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// True when the form was created on the thread this watchdog runs on.
        /// </summary>
        private static bool BelongsToThisThread(Form form)
        {
            try
            {
                return !form.InvokeRequired;
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or COMException)
            {
                return false;
            }
        }

        private static string Describe(Form form)
        {
            var text = new StringBuilder();

            text.Append(CultureInfo.InvariantCulture, $"{form.GetType().Name}");

            try
            {
                if (!string.IsNullOrWhiteSpace(form.Text))
                {
                    text.Append(CultureInfo.InvariantCulture, $" '{form.Text}'");
                }
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or ObjectDisposedException)
            {
                text.Append(" <caption unreadable>");
            }

            string content = string.Join(" | ", CollectText(form).Distinct());

            if (!string.IsNullOrWhiteSpace(content))
            {
                text.Append(": ").Append(content);
            }

            return text.ToString();
        }

        /// <summary>
        /// Collects whatever the dialog shows: labels and text boxes carry the message of a
        /// message dialog, the exception dialog of the samples renders its detail as html
        /// into a browser control.
        /// </summary>
        /// <remarks>
        /// Nothing in here may throw. Reading a control can fail - on a build agent the COM
        /// object behind a <see cref="WebBrowser"/> is not always there, and asking it for its
        /// document answers RPC_E_DISCONNECTED - and a watchdog which dies while describing a
        /// dialog leaves the dialog open and the test without its message.
        /// </remarks>
        private static List<string> CollectText(Control control)
        {
            var texts = new List<string>();

            Collect(control, texts);

            return texts;
        }

        private static void Collect(Control control, List<string> texts)
        {
            foreach (Control child in control.Controls)
            {
                try
                {
                    switch (child)
                    {
                        case Label label when !string.IsNullOrWhiteSpace(label.Text):
                            texts.Add(label.Text.Trim());
                            break;

                        case TextBoxBase textBox when !string.IsNullOrWhiteSpace(textBox.Text):
                            texts.Add(textBox.Text.Trim());
                            break;

                        case WebBrowser browser when !string.IsNullOrWhiteSpace(browser.DocumentText):
                            texts.Add(StripMarkup(browser.DocumentText));
                            break;
                    }
                }
                catch (Exception e) when (e is COMException or InvalidOperationException or ObjectDisposedException)
                {
                    texts.Add($"<{child.GetType().Name} could not be read: {e.GetType().Name}>");
                }

                Collect(child, texts);
            }
        }

        private static string StripMarkup(string html)
        {
            string text = MarkupRegex().Replace(html, " ");

            return WhitespaceRegex().Replace(text, " ").Trim();
        }

        [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
        private static partial Regex MarkupRegex();

        [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
        private static partial Regex WhitespaceRegex();
    }
}
