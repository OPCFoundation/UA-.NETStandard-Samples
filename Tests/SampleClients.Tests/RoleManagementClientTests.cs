/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Linq;
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
    /// This is the case which motivated driving every sample client past connect, so it is
    /// worth stating plainly. The Reset button of this sample shipped broken for every
    /// account: it resolved the method with a browse path built from a bare string, and a
    /// <see cref="QualifiedName"/> made from a bare string is in namespace zero while the
    /// method's browse name is in the model's namespace, so the path resolved to nothing and
    /// the button answered the sample's own BadNotFound. Measured against the running server,
    /// <c>ns=0 Reset</c> did not resolve and <c>ns=2 Reset</c> gave <c>ns=2;i=6</c>.
    /// </para>
    /// <para>
    /// The server was right the whole time, which is why nothing caught it: the tier 1.5
    /// fixture calls the same method successfully because it resolves with the namespace
    /// index, and tier 2 only asked whether the client connects.
    /// </para>
    /// <para>
    /// This signs in as an Operator, presses Reset, and asserts the server answered Good and
    /// the set point reads its default. Anything that breaks the client's own path to the
    /// method - the namespace, the browse, the call - fails here.
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

        /// <summary>
        /// How long the sample gets to answer a press.
        /// </summary>
        private static readonly TimeSpan s_actionTimeout = TimeSpan.FromSeconds(30);

        [Test]
        [CancelAfter(kTimeout)]
        public async Task OperatorCanCallResetFromTheForm(CancellationToken ct)
        {
            SampleClientUnderTest sample = SampleClientFactories.All
                .Single(entry => entry.Sample.Name == kSample);

            SampleServerUnderTest server = SampleServerFactories.All
                .Single(entry => entry.Sample.Name == kSample);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(kSample, server.Sample.ServerConfig, server.ConfigureServices, ct)
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

            SampleFormDriver.CreateHandles(form);

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
                bool listed = await SampleFormDriver
                    .PumpUntilAsync(() => RowOf(form, "SetPoint") != null, s_actionTimeout, ct)
                    .ConfigureAwait(true);

                Assert.That(listed, Is.True, "The machine never appeared in the node list; " + Seen(form));

                // the regression this fixture exists for: the client has to find its own
                // method and call it. Nothing is written first on purpose - a Reset which
                // restores the value it already had still goes the whole way through the
                // resolve and the call, and the status line is where the answer shows up.
                var reset = WinFormsHarness.FindControl(form, "ResetBTN") as Button;

                Assert.That(reset, Is.Not.Null, "The sample no longer has a Reset button.");
                Assert.That(reset.Enabled, Is.True, "Reset is disabled while connected.");

                Assert.That(
                    SampleFormDriver.TryInvokeHandler(form, "ResetBTN_ClickAsync", reset),
                    Is.True,
                    "The sample no longer has a ResetBTN_ClickAsync handler.");

                bool reported = await SampleFormDriver
                    .PumpUntilAsync(() => StatusText(form).Length > 0, s_actionTimeout, ct)
                    .ConfigureAwait(true);

                Assert.That(reported, Is.True, "Reset never reported a status; " + Seen(form));

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
            return SampleFormDriver.TryReadNumber(ValueOf(form, "SetPoint"), out double value)
                && Math.Abs(value - expected) < 0.0001;
        }

        /// <summary>
        /// What the status bar of the sample last reported.
        /// </summary>
        private static string StatusText(Form form)
        {
            return SampleFormDriver.StatusText(form, "StatusBar", "ActionStatusLB");
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
        #endregion
    }
}
