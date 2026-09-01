/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Configuration;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 2: the role management client has to be able to act, not only connect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SampleClientTests"/> proves the sample connects and finishes its post
    /// connect logic, which is not enough for a sample whose whole content is what happens
    /// when you press its buttons. The Reset button shipped broken for every account -
    /// it resolved the method by a browse path built from a bare string, so the browse name
    /// went out in namespace zero while the method's is in the model's namespace, and the
    /// path resolved to nothing. The server was never at fault, and the tier 1.5 fixture,
    /// which resolves the same method with the namespace index, could not see it.
    /// </para>
    /// <para>
    /// This drives the real form: signs in as an Operator, presses Reset, and checks the
    /// server answered Good and the set point reads its default. Anything that breaks the
    /// client's own path to the method - the namespace, the browse, the call - fails here.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class RoleManagementClientTests
    {
        private const int kTimeout = 120_000;
        private const string kSample = "RoleManagement";

        /// <summary>
        /// The default the Reset method of the sample restores.
        /// </summary>
        private const double kDefaultSetPoint = 20.0;

        [Test]
        [CancelAfter(kTimeout)]
        public async Task OperatorCanCallResetFromTheForm(CancellationToken ct)
        {
            SampleClientUnderTest sample = SampleClientFactories.All
                .Single(entry => entry.Sample.Name == kSample);

            SampleServerUnderTest server = SampleServerFactories.All
                .Single(entry => entry.Sample.Name == kSample);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(kSample, server.Sample.ServerConfig, server.CreateServer, ct)
                .ConfigureAwait(false);

            await WinFormsHarness.RunAsync(
                async _ => await DriveAsync(sample, host.EndpointUrl, ct).ConfigureAwait(true),
                TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task DriveAsync(
            SampleClientUnderTest sample,
            string endpointUrl,
            CancellationToken ct)
        {
            using var pki = new TemporaryPki($"client-{kSample}");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(sample.Sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            Assert.That(
                await application.CheckApplicationInstanceCertificatesAsync(true, null, ct).ConfigureAwait(true),
                Is.True,
                "The client certificate of the sample could not be created.");

            using Form form = sample.CreateMainForm(configuration, NullTelemetry.Instance);

            CreateHandles(form);

            // sign in as the Operator: the drop down handler is what puts the identity token
            // on the connect control, so this is the same path a user takes
            var identity = WinFormsHarness.FindControl(form, "IdentityCB") as ComboBox;

            Assert.That(identity, Is.Not.Null, "The sample no longer offers an identity drop down.");

            identity.SelectedItem = "operator1";

            ConnectServerCtrl connect = WinFormsHarness.GetConnectControl(form);

            Assert.That(
                connect.UserIdentity?.DisplayName,
                Is.EqualTo("operator1"),
                "Choosing an account did not reach the connect control.");

            ISession session = await connect
                .ConnectAsync(NullTelemetry.Instance, endpointUrl, false, 30_000, ct)
                .ConfigureAwait(true);

            try
            {
                Assert.That(session, Is.Not.Null, "The sample did not connect.");

                // the machine is listed by the sample's own async void ConnectComplete handler
                await WaitForAsync(
                    () => RowOf(form, "SetPoint") != null,
                    () => "the machine to appear in the node list; " + Seen(form),
                    ct).ConfigureAwait(true);

                // the regression this fixture exists for: the client has to find its own
                // method and call it. Nothing is written first on purpose - a Reset which
                // restores the value it already had still goes the whole way through the
                // resolve and the call, and the status line is where the answer shows up.
                Click(form, "ResetBTN");

                await WaitForAsync(
                    () => StatusText(form).Length > 0,
                    () => "Reset to report a status; " + Seen(form),
                    ct).ConfigureAwait(true);

                Assert.That(
                    StatusText(form),
                    Does.Contain("Good"),
                    "Calling Reset as an Operator has to succeed. " +
                    "BadNotFound means the client could not resolve its own method, " +
                    "BadUserAccessDenied means the call was refused.");

                Assert.That(
                    SetPointIs(form, kDefaultSetPoint),
                    Is.True,
                    $"After Reset the set point has to read {kDefaultSetPoint}; " + Seen(form));
            }
            finally
            {
                // awaited, not the synchronous Disconnect: that one blocks the UI thread on
                // work which needs the same message loop, and the fixture deadlocks
                await connect.DisconnectAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }

        #region Form Helpers
        /// <summary>
        /// Creates the window handles without ever showing the form.
        /// </summary>
        private static void CreateHandles(Form form)
        {
            _ = form.Handle;

            foreach (Control control in AllControls(form))
            {
                _ = control.Handle;
            }
        }

        private static System.Collections.Generic.IEnumerable<Control> AllControls(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;

                foreach (Control grandChild in AllControls(child))
                {
                    yield return grandChild;
                }
            }
        }

        private static ListViewItem RowOf(Form form, string node)
        {
            var list = WinFormsHarness.FindControl(form, "NodesLV") as ListView;

            return list?.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(item => string.Equals(item.Text, node, StringComparison.Ordinal));
        }

        /// <summary>
        /// The value column of a node's row, or null when the row is not there.
        /// </summary>
        private static string ValueOf(Form form, string node)
        {
            ListViewItem row = RowOf(form, node);

            return row != null && row.SubItems.Count > 1 ? row.SubItems[1].Text : null;
        }

        /// <summary>
        /// True when the set point row shows the given number.
        /// </summary>
        /// <remarks>
        /// Compared numerically rather than as text, because how a Double renders depends on
        /// the culture of the machine the test runs on.
        /// </remarks>
        private static bool SetPointIs(Form form, double expected)
        {
            string shown = ValueOf(form, "SetPoint");

            if (string.IsNullOrEmpty(shown))
            {
                return false;
            }

            bool parsed = double.TryParse(shown, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
                || double.TryParse(shown, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

            return parsed && Math.Abs(value - expected) < 0.0001;
        }

        /// <summary>
        /// What the form is showing right now, for a failure message.
        /// </summary>
        private static string Seen(Form form)
        {
            var list = WinFormsHarness.FindControl(form, "NodesLV") as ListView;

            string rows = list == null
                ? "<no node list>"
                : string.Join(
                    ", ",
                    list.Items.Cast<ListViewItem>().Select(item =>
                        $"{item.Text}={(item.SubItems.Count > 1 ? item.SubItems[1].Text : string.Empty)}"));

            return $"the list shows [{rows}] and the status line says '{StatusText(form)}'";
        }

        /// <summary>
        /// Selects a node's row the way a click would.
        /// </summary>
        /// <remarks>
        /// Through <c>SelectedIndices</c> rather than <c>ListViewItem.Selected</c>: the form
        /// is never shown, and on a list that has not been displayed the item level property
        /// does not always reach the control, which leaves the sample's handler looking at an
        /// empty selection and returning without doing anything.
        /// </remarks>
        private static void Select(Form form, string node)
        {
            var list = WinFormsHarness.FindControl(form, "NodesLV") as ListView;

            Assert.That(list, Is.Not.Null, "The sample no longer has a node list.");

            ListViewItem row = RowOf(form, node);

            Assert.That(row, Is.Not.Null, $"The node list has no row for '{node}'.");

            list.Focus();
            list.SelectedIndices.Clear();
            list.SelectedIndices.Add(row.Index);

            Assert.That(
                list.SelectedItems.Count,
                Is.EqualTo(1),
                $"Selecting the '{node}' row did not take, so the sample would see no selection.");
        }

        private static void SetText(Form form, string control, string text)
        {
            var box = WinFormsHarness.FindControl(form, control) as TextBox;

            Assert.That(box, Is.Not.Null, $"The sample no longer has a '{control}' text box.");

            box.Text = text;
        }

        /// <summary>
        /// Presses a button of the sample.
        /// </summary>
        /// <remarks>
        /// The button has to be there and enabled - that part is what a user sees - but the
        /// press itself goes through the handler rather than through
        /// <see cref="Button.PerformClick"/>. <c>PerformClick</c> is gated on
        /// <c>CanSelect</c>, which is false while no parent of the control is visible, so on
        /// a form the harness never shows it silently does nothing. The other client fixtures
        /// invoke sample handlers the same way.
        /// </remarks>
        private static void Click(Form form, string control)
        {
            var button = WinFormsHarness.FindControl(form, control) as Button;

            Assert.That(button, Is.Not.Null, $"The sample no longer has a '{control}' button.");
            Assert.That(button.Enabled, Is.True, $"'{control}' is disabled while connected.");

            string handlerName = control + "_ClickAsync";

            MethodInfo handler = form.GetType().GetMethod(
                handlerName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.That(
                handler,
                Is.Not.Null,
                $"The sample no longer has a '{handlerName}' handler for the '{control}' button.");

            handler.Invoke(form, new object[] { button, EventArgs.Empty });
        }

        /// <summary>
        /// What the status bar of the sample last reported.
        /// </summary>
        private static string StatusText(Form form)
        {
            var status = WinFormsHarness.FindControl(form, "StatusBar") as StatusStrip;

            ToolStripItem label = status?.Items
                .Cast<ToolStripItem>()
                .FirstOrDefault(item => item.Name == "ActionStatusLB");

            return label?.Text ?? string.Empty;
        }

        /// <summary>
        /// Pumps the message loop until the condition holds, so the sample's async void
        /// handlers can run.
        /// </summary>
        private static async Task WaitForAsync(Func<bool> condition, Func<string> what, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);

            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail($"Timed out waiting for {what()}.");
                }

                ct.ThrowIfCancellationRequested();

                Application.DoEvents();

                await Task.Delay(100, ct).ConfigureAwait(true);
            }
        }
        #endregion
    }
}
