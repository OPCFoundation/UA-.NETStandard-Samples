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
        /// The entry of the identity drop down which opens an anonymous Session.
        /// </summary>
        private const string kAnonymous = "Anonymous";

        /// <summary>
        /// The columns of the node list, as the sample orders them.
        /// </summary>
        private const int kStatusColumn = 2;
        private const int kRestrictionsColumn = 3;

        /// <summary>
        /// How long the sample gets to answer a press.
        /// </summary>
        private static readonly TimeSpan s_actionTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The endpoint of the server the harness started, for the body of a fixture.
        /// </summary>
        /// <remarks>
        /// The fixture is NonParallelizable and one server runs at a time, so a static is
        /// enough to carry the url onto the STA thread the form lives on.
        /// </remarks>
        private static string s_endpointUrl;

        [Test]
        [CancelAfter(kTimeout)]
        public async Task OperatorCanCallResetFromTheForm(CancellationToken ct)
        {
            await RunAsync((form, token) => DriveAsync(form, token), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The nodes which demand an encrypted channel say so, and say why they refused.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The Operator holds Browse and Read on the maintenance note in both sessions here.
        /// What changes is the channel, and the client has to be able to tell the difference:
        /// BadSecurityModeInsufficient means reconnect with security, where
        /// BadUserAccessDenied would mean sign in as somebody else.
        /// </para>
        /// <para>
        /// The AccessRestrictions column is only filled in the session which satisfies them.
        /// A read of any attribute other than the Value is checked against the restrictions
        /// too, so a Session on an unencrypted channel cannot read the attribute which would
        /// tell it that the channel is the problem - the status code is what it has.
        /// </para>
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task AnUnencryptedSessionSeesWhyTheRestrictedNodesRefuse(CancellationToken ct)
        {
            await RunAsync(async (form, token) => {
                await ConnectAsAsync(form, "operator1", useSecurity: false, token).ConfigureAwait(true);

                bool listed = await SampleFormDriver
                    .PumpUntilAsync(() => RowOf(form, "MaintenanceNote") != null, s_actionTimeout, token)
                    .ConfigureAwait(true);

                Assert.That(listed, Is.True, "The maintenance note never appeared; " + Seen(form));

                Assert.That(
                    ColumnOf(form, "MaintenanceNote", kStatusColumn),
                    Does.Contain("BadSecurityModeInsufficient"),
                    "An Operator holds Read on the maintenance note, so the refusal has to " +
                    "name the channel rather than the user. " + Seen(form));

                // the same account, the same node, the other channel
                await WinFormsHarness.GetConnectControl(form)
                    .DisconnectAsync(CancellationToken.None)
                    .ConfigureAwait(true);

                await ConnectAsAsync(form, "operator1", useSecurity: true, token).ConfigureAwait(true);

                bool readable = await SampleFormDriver
                    .PumpUntilAsync(
                        () => ColumnOf(form, "MaintenanceNote", kStatusColumn)?.Contains(
                            "Good", StringComparison.Ordinal) == true,
                        s_actionTimeout,
                        token)
                    .ConfigureAwait(true);

                Assert.That(readable, Is.True, "The encrypted session has to read the note; " + Seen(form));

                Assert.That(
                    ColumnOf(form, "MaintenanceNote", kRestrictionsColumn),
                    Does.Contain("EncryptionRequired"),
                    "A Session which may touch the node at all has to be able to read the " +
                    "AccessRestrictions which apply to it. " + Seen(form));
            }, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The certificate of this client application earns it a Role of its own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The server maps the subject name of the application instance certificate the
        /// sample client creates for itself onto the ConfigureAdmin Role, restricted to its
        /// encrypted endpoints (Part 18 4.4.3 X509Subject and the 4.4.1 Endpoints filter).
        /// So an <b>anonymous</b> Session from this client holds a Role which no account of
        /// the sample can earn, and the service code is in its address space.
        /// </para>
        /// <para>
        /// This is the fixture which holds the server's hard coded criteria to the
        /// certificate the client's own configuration file produces. It fails if either of
        /// the two is edited without the other.
        /// </para>
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheCertificateOfThisClientEarnsTheServiceCode(CancellationToken ct)
        {
            await RunAsync(async (form, token) => {
                await ConnectAsAsync(form, kAnonymous, useSecurity: true, token).ConfigureAwait(true);

                bool listed = await SampleFormDriver
                    .PumpUntilAsync(() => RowOf(form, "ServiceCode") != null, s_actionTimeout, token)
                    .ConfigureAwait(true);

                Assert.That(
                    listed,
                    Is.True,
                    "An anonymous Session on an encrypted endpoint has to hold the ConfigureAdmin " +
                    "Role, because the server maps the subject of this client's certificate onto " +
                    "it. " + Seen(form));

                Assert.That(
                    ColumnOf(form, "ServiceCode", kStatusColumn),
                    Does.Contain("Good"),
                    "The ConfigureAdmin Role carries Read on the service code. " + Seen(form));
            }, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts the sample server, opens the sample client on the STA thread of the
        /// harness, and runs the body against the form.
        /// </summary>
        /// <remarks>
        /// The disconnect is awaited rather than the synchronous Disconnect, which blocks the
        /// UI thread on work that needs the same message loop and deadlocks the fixture.
        /// </remarks>
        private static async Task RunAsync(Func<Form, CancellationToken, Task> body, CancellationToken ct)
        {
            SampleClientUnderTest sample = SampleClientFactories.All
                .Single(entry => entry.Sample.Name == kSample);

            SampleServerUnderTest server = SampleServerFactories.All
                .Single(entry => entry.Sample.Name == kSample);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(kSample, server.Sample.ServerConfig, server.ConfigureServices, ct)
                .ConfigureAwait(false);

            await WinFormsHarness.RunAsync(
                async _ => {
                    using var pki = new TemporaryPki($"client-{kSample}");

                    ApplicationConfiguration configuration = await SampleConfigurationLoader
                        .LoadAsync(sample.Sample.ClientConfig, pki, ct)
                        .ConfigureAwait(true);

                    await using var application =
                        new ApplicationInstance(configuration, NullTelemetry.Instance);

                    Assert.That(
                        await application
                            .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                            .ConfigureAwait(true),
                        Is.True,
                        "The client certificate of the sample could not be created.");

                    using Form form = sample.CreateMainForm(configuration, NullTelemetry.Instance);

                    SampleFormDriver.CreateHandles(form);

                    s_endpointUrl = host.EndpointUrl;

                    try
                    {
                        await body(form, ct).ConfigureAwait(true);
                    }
                    finally
                    {
                        await WinFormsHarness.GetConnectControl(form)
                            .DisconnectAsync(CancellationToken.None)
                            .ConfigureAwait(true);
                    }
                },
                TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Signs in through the drop down of the sample and connects.
        /// </summary>
        /// <remarks>
        /// The drop down handler is what puts the identity token on the connect control, so
        /// this is the same path a user takes rather than a short cut around the sample.
        /// </remarks>
        private static async Task ConnectAsAsync(
            Form form,
            string account,
            bool useSecurity,
            CancellationToken ct)
        {
            var identity = WinFormsHarness.FindControl(form, "IdentityCB") as ComboBox;

            Assert.That(identity, Is.Not.Null, "The sample no longer offers an identity drop down.");

            identity.SelectedItem = account;

            ConnectServerCtrl connect = WinFormsHarness.GetConnectControl(form);

            Assert.That(
                connect.UserIdentity?.DisplayName,
                Is.EqualTo(string.Equals(account, kAnonymous, StringComparison.Ordinal) ? null : account),
                "Choosing an account did not reach the connect control.");

            ISession session = await connect
                .ConnectAsync(NullTelemetry.Instance, s_endpointUrl, useSecurity, 30_000, ct)
                .ConfigureAwait(true);

            Assert.That(session, Is.Not.Null, "The sample did not connect.");
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task DriveAsync(Form form, CancellationToken ct)
        {
            await ConnectAsAsync(form, "operator1", useSecurity: false, ct).ConfigureAwait(true);

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
            return ColumnOf(form, node, 1);
        }

        /// <summary>
        /// One column of a node's row, or null when the row or the column is not there.
        /// </summary>
        private static string ColumnOf(Form form, string node, int column)
        {
            ListViewItem row = RowOf(form, node);

            return row != null && row.SubItems.Count > column ? row.SubItems[column].Text : null;
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
