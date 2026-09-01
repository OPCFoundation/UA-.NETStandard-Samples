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
    /// Tier 2: the sample clients which are left have to do the thing they exist for, not
    /// only connect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SampleClientTests"/> proves that a client builds its form, connects and
    /// finishes its post connect logic. A client can pass that and still be wired so that
    /// every one of its buttons does nothing, and no tier notices - which is exactly what
    /// happened to the role management client, whose Reset button resolved its method with a
    /// browse name in namespace zero and answered a synthetic BadNotFound for every account.
    /// The server was right the whole time, the tier 1.5 fixture resolved the same method
    /// with the namespace index and passed, and a user found it by pressing the button once.
    /// </para>
    /// <para>
    /// So one test per client, which presses what the sample is for and asserts what it is
    /// meant to show. <see cref="WorkshopClientSubscriptionTests"/> already does this for the
    /// six clients whose content is a subscription, and RoleManagement has its own fixture;
    /// these are the rest.
    /// </para>
    /// <para>
    /// The Empty client is deliberately not here. It is the template a new sample is copied
    /// from: its form has a connect control, a menu and a status bar, and not one control of
    /// its own - there is nothing to press, and a test which pressed something would be
    /// testing the shared controls rather than the sample. Should the template ever grow a
    /// control, it belongs in this fixture with the others.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class SampleClientActionTests
    {
        /// <summary>
        /// How long a test gets, including starting its sample server.
        /// </summary>
        private const int kTimeout = 120_000;

        /// <summary>
        /// How long a single service call of a sample client may take.
        /// </summary>
        /// <remarks>
        /// The sample configurations allow ten minutes, which is longer than one of these
        /// tests may run, so a call which never comes back would surface as a bare harness
        /// timeout with nothing to point at.
        /// </remarks>
        private const int kOperationTimeout = 30_000;

        /// <summary>
        /// How long a sample gets to answer one press.
        /// </summary>
        private static readonly TimeSpan s_step = TimeSpan.FromSeconds(30);

        #region DataTypes
        /// <summary>
        /// The data types client has to reach the structured value of the second node set
        /// through its own browse tree, and show it decoded.
        /// </summary>
        /// <remarks>
        /// What the sample is for: the server serves data types of its own, out of two node
        /// sets, and a client which knew none of them at compile time loads the type system
        /// after connecting and can then make sense of a value. The client does that load in
        /// its ConnectComplete handler and shows what it read in the attribute list of its
        /// browse control, so walking the tree to the parking lot's structured property and
        /// reading the attribute list follows the whole chain.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public Task DataTypesClientShowsAStructuredValue(CancellationToken ct)
        {
            return DriveAsync("DataTypes", async (form, session, phase, token) => {
                phase.Enter("walking its browse tree to the parking lot");

                TreeView tree = SampleFormDriver.BrowseTreeOf(form);

                Assert.That(tree, Is.Not.Null, "The sample no longer hosts the shared browse control.");

                TreeNode root = await RootOfAsync(tree, token).ConfigureAwait(true);

                // the tree of this client is rooted at the Root folder rather than at Objects
                TreeNode vehicle = await SampleFormDriver.NavigateAsync(
                    root,
                    ["Objects", "ParkingLot", "DriverOfTheMonth", "PrimaryVehicle"],
                    s_step,
                    token).ConfigureAwait(true);

                Assert.That(
                    vehicle,
                    Is.Not.Null,
                    "The browse tree of the sample does not reach the primary vehicle of the parking lot. " +
                    $"Below the root it shows: {SampleFormDriver.ChildrenOf(root)}");

                phase.Enter("reading the attributes of the structured value");

                ListView attributes = SampleFormDriver.AttributeListOf(form);

                SampleFormDriver.Select(tree, vehicle);

                bool read = await SampleFormDriver.PumpUntilAsync(
                    () => SampleFormDriver.AttributeText(attributes, "Value") != null,
                    s_step,
                    token).ConfigureAwait(true);

                Assert.That(
                    read,
                    Is.True,
                    "Selecting the primary vehicle did not make the sample read its attributes. " +
                    $"The attribute list shows: {SampleFormDriver.AttributesSeen(attributes)}");

                string dataType = SampleFormDriver.AttributeText(attributes, "DataType");
                string value = SampleFormDriver.AttributeText(attributes, "Value");

                await TestContext.Out
                    .WriteLineAsync($"The client shows DataType '{dataType}' and Value '{value}'")
                    .ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(
                        dataType,
                        Is.EqualTo("VehicleType"),
                        "The property has to be typed with the data type of the other node set. " +
                        "A node id instead of a name means the type node never arrived.");

                    // the client shows the fields of the structure, so what is asserted is that
                    // they are there rather than how they are laid out
                    Assert.That(
                        value,
                        Does.Contain("Trek"),
                        "The value has to arrive decoded into its fields. Anything else means the " +
                        "client could not make sense of a structure, which is the whole point of " +
                        "the sample. " +
                        $"The attribute list shows: {SampleFormDriver.AttributesSeen(attributes)}");

                    // Make and Model come from the vehicle type of the first node set, the
                    // manufacturer and the gears from the bicycle type of the second. A value
                    // decoded only as its declared base type would be missing the last two,
                    // which is a defect the sample would otherwise show without complaining
                    Assert.That(
                        value,
                        Does.Contain("Cube").And.Contain("10"),
                        "The value has to keep the fields the derived structure adds to the one " +
                        "the property is declared as, not only the inherited ones. " +
                        $"It shows '{value}'.");
                });
            }, ct);
        }
        #endregion

        #region HistoricalAccess
        /// <summary>
        /// The historical access client has to read recorded history into its grid.
        /// </summary>
        /// <remarks>
        /// What the sample is for. The form itself is thin - it hands a node and a session to
        /// the shared history control the way its Select Variable dialog does, and the read is
        /// the Go button of that control. Connecting proves none of it: the control is only
        /// usable if the sample gave it the session in its ConnectComplete handler, and the
        /// read only returns anything if the node it was given is one the server historizes.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public Task HistoricalAccessClientReadsHistoryIntoItsGrid(CancellationToken ct)
        {
            return DriveAsync("HistoricalAccess", async (form, session, phase, token) => {
                phase.Enter("looking for an archive item to read");

                NodeId item = await ArchiveItemAsync(session, token).ConfigureAwait(true);

                var history = WinFormsHarness.FindControl(form, "ReadCTRL") as HistoryDataListView;

                Assert.That(history, Is.Not.Null, "The sample no longer hosts the shared history control.");

                phase.Enter($"pointing the history control at {item}");

                // what the sample does once its Select Variable dialog comes back with a node
                await history.ChangeNodeAsync(item, token).ConfigureAwait(true);

                var results = WinFormsHarness.FindField<DataGridView>(history, "ResultsDV");

                Assert.That(results, Is.Not.Null, "The history control no longer has its results grid.");

                phase.Enter("pressing Go");

                Assert.That(
                    SampleFormDriver.TryInvokeHandler(history, "GoBTN_ClickAsync", null),
                    Is.True,
                    "The history control no longer has a Go handler.");

                bool filled = await SampleFormDriver.PumpUntilAsync(
                    () => results.Rows.Count > 0,
                    s_step,
                    token).ConfigureAwait(true);

                Assert.That(
                    filled,
                    Is.True,
                    "A raw read of an archive item has to put its recorded values in the grid. " +
                    "An empty grid means the read never reached the server, or the sample handed " +
                    "the control a node which is not historized.");

                string[] values = results.Rows
                    .Cast<DataGridViewRow>()
                    .Take(3)
                    .Select(row => $"{row.Cells[0].Value} = {row.Cells[2].Value}")
                    .ToArray();

                await TestContext.Out
                    .WriteLineAsync($"{results.Rows.Count} rows, the first are: {string.Join("; ", values)}")
                    .ConfigureAwait(true);

                Assert.That(
                    results.Rows[0].Cells[2].Value?.ToString(),
                    Is.Not.Null.And.Not.Empty,
                    "A row of the grid has to carry the value that was recorded, not only a timestamp.");
            }, ct);
        }
        #endregion

        #region PerfTest
        /// <summary>
        /// The performance test client has to count the updates it is measuring.
        /// </summary>
        /// <remarks>
        /// What the sample is for: it subscribes to a block of items as soon as it connects
        /// and reports the throughput it sees. The counters are filled by a WinForms timer
        /// which reads the tester, so a number above zero is the proof that the subscription
        /// was created, that notifications arrive on the publish worker and that the timer
        /// gets them onto the form - none of which a connect shows. Pressing Stop afterwards
        /// is the other half: the sample has to be able to end the run it started.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public Task PerfTestClientCountsTheUpdatesItMeasures(CancellationToken ct)
        {
            return DriveAsync("PerfTest", async (form, session, phase, token) => {
                phase.Enter("waiting for the first measured updates");

                var messages = WinFormsHarness.FindControl(form, "MessageCountTB") as TextBox;
                var updates = WinFormsHarness.FindControl(form, "TotalItemUpdateCountTB") as TextBox;

                Assert.That(messages, Is.Not.Null, "The sample no longer shows a message count.");
                Assert.That(updates, Is.Not.Null, "The sample no longer shows an item update count.");

                bool counted = await SampleFormDriver.PumpUntilAsync(
                    () => SampleFormDriver.TryReadNumber(updates.Text, out double seen) && seen > 0,
                    s_step,
                    token).ConfigureAwait(true);

                await TestContext.Out
                    .WriteLineAsync($"The client reports {messages.Text} messages and {updates.Text} item updates")
                    .ConfigureAwait(true);

                Assert.That(
                    counted,
                    Is.True,
                    "The sample starts its test as soon as it connects, so its item update count " +
                    $"has to leave zero. It shows '{updates.Text}' after {s_step.TotalSeconds:F0} seconds.");

                Assert.That(
                    SampleFormDriver.TryReadNumber(messages.Text, out double publishes) && publishes > 0,
                    Is.True,
                    $"Item updates arrived but no publish was counted, which cannot be: '{messages.Text}'.");

                phase.Enter("pressing Stop");

                var stop = WinFormsHarness.FindControl(form, "StopBTN") as Button;

                Assert.That(stop, Is.Not.Null, "The sample no longer has a Stop button.");

                // whether the sample is still running is read from its own timer, not from
                // Button.Visible. The visibility of a control is answered from the whole parent
                // chain, and on a form the harness never shows every control reports itself
                // invisible however the sample set the property.
                var ticker = WinFormsHarness.FindField<System.Windows.Forms.Timer>(form, "UpdateTimer");

                Assert.That(ticker, Is.Not.Null, "The sample no longer has its update timer.");
                Assert.That(ticker.Enabled, Is.True, "The sample was not running a test to stop.");

                Assert.That(
                    SampleFormDriver.TryInvokeHandler(form, "StopBTN_ClickAsync", stop),
                    Is.True,
                    "The sample no longer has a Stop handler.");

                bool stopped = await SampleFormDriver.PumpUntilAsync(
                    () => !ticker.Enabled,
                    s_step,
                    token).ConfigureAwait(true);

                Assert.That(stopped, Is.True, "Pressing Stop did not end the run the sample had started.");
            }, ct);
        }
        #endregion

        #region Reference
        /// <summary>
        /// The reference client has to browse into the address space of its server and read
        /// the attributes of what it found.
        /// </summary>
        /// <remarks>
        /// The whole client is a browse tree and an attribute list, so this is all of it. A
        /// connect only proves that the tree was handed a session; the browse of a child node,
        /// which the control does when the node is expanded, and the read of the attributes,
        /// which it does when a node is selected, are both service calls the sample makes on
        /// its own and neither happens before a user touches the tree.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public Task ReferenceClientBrowsesAndReadsAScalar(CancellationToken ct)
        {
            return DriveAsync("Reference", async (form, session, phase, token) => {
                phase.Enter("walking its browse tree into the static scalars");

                TreeView tree = SampleFormDriver.BrowseTreeOf(form);

                Assert.That(tree, Is.Not.Null, "The sample no longer hosts the shared browse control.");

                TreeNode root = await RootOfAsync(tree, token).ConfigureAwait(true);

                TreeNode scalars = await SampleFormDriver.NavigateAsync(
                    root,
                    ["Data", "Static", "Scalar"],
                    s_step,
                    token).ConfigureAwait(true);

                Assert.That(
                    scalars,
                    Is.Not.Null,
                    "The browse tree does not reach the static scalars of the reference server. " +
                    $"Below Objects it shows: {SampleFormDriver.ChildrenOf(root)}");

                Assert.That(
                    await SampleFormDriver.ExpandAsync(scalars, s_step, token).ConfigureAwait(true),
                    Is.True,
                    "Expanding the scalars folder never finished.");

                TreeNode scalar = scalars.Nodes
                    .Cast<TreeNode>()
                    .FirstOrDefault(node => (node.Tag as ReferenceDescription)?.NodeClass == NodeClass.Variable);

                Assert.That(
                    scalar,
                    Is.Not.Null,
                    "The static scalars folder of the reference server holds no variable. " +
                    $"It shows: {SampleFormDriver.ChildrenOf(scalars)}");

                phase.Enter($"reading the attributes of {SampleFormDriver.BrowseNameOf(scalar)}");

                ListView attributes = SampleFormDriver.AttributeListOf(form);

                SampleFormDriver.Select(tree, scalar);

                bool read = await SampleFormDriver.PumpUntilAsync(
                    () => SampleFormDriver.AttributeText(attributes, "Value") != null,
                    s_step,
                    token).ConfigureAwait(true);

                Assert.That(
                    read,
                    Is.True,
                    "Selecting a variable did not make the sample read its attributes. " +
                    $"The attribute list shows: {SampleFormDriver.AttributesSeen(attributes)}");

                await TestContext.Out
                    .WriteLineAsync(
                        $"{SampleFormDriver.BrowseNameOf(scalar)}: " +
                        SampleFormDriver.AttributesSeen(attributes))
                    .ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(
                        SampleFormDriver.AttributeText(attributes, "NodeClass"),
                        Is.EqualTo("Variable"),
                        "The attributes the sample read are not the ones of the node it was asked about.");

                    // the qualified browse name, which is how the control renders it and also
                    // what makes this worth asserting: the namespace index has to be the one
                    // the tree got from the server
                    Assert.That(
                        SampleFormDriver.AttributeText(attributes, "BrowseName"),
                        Is.EqualTo(((ReferenceDescription)scalar.Tag).BrowseName.ToString()),
                        "The attribute list is showing a different node than the tree selected.");
                });
            }, ct);
        }
        #endregion

        #region UserAuthentication
        /// <summary>
        /// The user authentication client has to be refused what the identity it is holding
        /// may not do, and say so.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What the sample is for: the same node looks different depending on who is asking.
        /// The tier 1.5 fixture proves the server end of that - an anonymous session is told
        /// the log file path is read only and its write is refused. This is the client end,
        /// which is a different question: the sample has to read the node after connecting,
        /// send the write the user asked for, and report the refusal instead of pretending it
        /// worked.
        /// </para>
        /// <para>
        /// Anonymously, because that needs no account: the successful write is only reachable
        /// with a real Windows account, which is why the tier 1.5 test for it is gated on two
        /// environment variables. Changing identity is driven through the user name button
        /// with an account no machine has, which exercises the same UpdateSession call and
        /// leaves the session as it was.
        /// </para>
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public Task UserAuthenticationClientIsRefusedWhatItMayNotDo(CancellationToken ct)
        {
            return DriveAsync("UserAuthentication", async (form, session, phase, token) => {
                phase.Enter("waiting for the log file path the sample reads after connecting");

                var path = WinFormsHarness.FindControl(form, "LogFilePathTB") as TextBox;

                Assert.That(path, Is.Not.Null, "The sample no longer shows the log file path.");

                bool shown = await SampleFormDriver.PumpUntilAsync(
                    () => !string.IsNullOrEmpty(path.Text),
                    s_step,
                    token).ConfigureAwait(true);

                Assert.That(
                    shown,
                    Is.True,
                    "The sample reads the log file path in its ConnectComplete handler, so it has " +
                    "to be showing one. An empty box means that read never came back.");

                await TestContext.Out
                    .WriteLineAsync($"The client read the log file path '{path.Text}'")
                    .ConfigureAwait(true);

                phase.Enter("writing the log file path as an anonymous session");

                path.Text = @".\NotAllowed.txt";

                Assert.That(
                    SampleFormDriver.TryInvokeHandler(form, "ChangeLogFileBTN_ClickAsync", null),
                    Is.True,
                    "The sample no longer has a handler for its change button.");

                bool answered = await SampleFormDriver.PumpUntilAsync(
                    () => Reported(form).Length > 0,
                    s_step,
                    token).ConfigureAwait(true);

                Assert.That(answered, Is.True, "Writing the log file path never reported an outcome.");

                await TestContext.Out
                    .WriteLineAsync($"The write reported: {Reported(form)}")
                    .ConfigureAwait(true);

                Assert.That(
                    Reported(form),
                    Does.Contain(nameof(StatusCodes.BadUserAccessDenied)),
                    "The server refuses a write from an anonymous session, and the sample has to " +
                    "report that refusal rather than swallow it.");

                phase.Enter("changing to an identity the server cannot verify");

                var user = WinFormsHarness.FindControl(form, "UserNameTB") as TextBox;
                var password = WinFormsHarness.FindControl(form, "PasswordTB") as TextBox;

                Assert.That(user, Is.Not.Null, "The sample no longer takes a user name.");
                Assert.That(password, Is.Not.Null, "The sample no longer takes a password.");

                user.Text = "no-such-user-8f2c";
                password.Text = "not the password";

                ClearReport(form);

                Assert.That(
                    SampleFormDriver.TryInvokeHandler(form, "UserNameImpersonateBTN_Click", null),
                    Is.True,
                    "The sample no longer has a handler for its user name impersonate button.");

                bool refused = await SampleFormDriver.PumpUntilAsync(
                    () => Reported(form).Length > 0,
                    s_step,
                    token).ConfigureAwait(true);

                Assert.That(refused, Is.True, "Impersonating never reported an outcome.");

                await TestContext.Out
                    .WriteLineAsync($"Impersonating reported: {Reported(form)}")
                    .ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(
                        Reported(form),
                        Does.Not.Contain("Good"),
                        "An account the server cannot verify must not be reported as a successful " +
                        "change of identity.");

                    Assert.That(
                        session.Identity?.TokenType,
                        Is.EqualTo(UserTokenType.Anonymous),
                        "A refused UpdateSession has to leave the session with the identity it had.");
                });
            }, ct);
        }
        #endregion

        #region Views
        /// <summary>
        /// The views client has to browse through a view and see something different.
        /// </summary>
        /// <remarks>
        /// What the sample is for, and the only sample which has it: the server overrides
        /// IsNodeInView and IsReferenceInView, and the filtering they implement is invisible
        /// unless a browse actually carries a view. The client is the half that has to put the
        /// view on the browse, which is what its Change button does - it takes the view the
        /// user picked and re-browses the selected node with it. Wire that up wrongly, and the
        /// sample connects, fills its drop down and shows the unfiltered address space forever.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public Task ViewsClientSeesADifferentAddressSpaceThroughAView(CancellationToken ct)
        {
            return DriveAsync("Views", async (form, session, phase, token) => {
                phase.Enter("walking its browse tree to a flow meter");

                ushort engineering = NamespaceIndexOf(session, Quickstarts.Views.Namespaces.Engineering);
                ushort operations = NamespaceIndexOf(session, Quickstarts.Views.Namespaces.Operations);

                TreeView tree = SampleFormDriver.BrowseTreeOf(form);

                Assert.That(tree, Is.Not.Null, "The sample no longer hosts the shared browse control.");

                TreeNode root = await RootOfAsync(tree, token).ConfigureAwait(true);

                TreeNode flow = await SampleFormDriver.NavigateAsync(
                    root,
                    ["Plant", "Boiler #1", "WaterIn", "Flow"],
                    s_step,
                    token).ConfigureAwait(true);

                Assert.That(
                    flow,
                    Is.Not.Null,
                    "The browse tree does not reach the flow meter of the first boiler. " +
                    $"Below Objects it shows: {SampleFormDriver.ChildrenOf(root)}");

                Assert.That(
                    await SampleFormDriver.ExpandAsync(flow, s_step, token).ConfigureAwait(true),
                    Is.True,
                    "Expanding the flow meter never finished.");

                string[] unfiltered = ChildrenIn(flow, engineering).Concat(ChildrenIn(flow, operations)).ToArray();

                await TestContext.Out
                    .WriteLineAsync($"Without a view the flow meter shows: {SampleFormDriver.ChildrenOf(flow)}")
                    .ConfigureAwait(true);

                // the baseline the filtered browse is compared against: if this ever stops
                // showing both disciplines, the assertion below would pass for the wrong reason
                Assert.Multiple(() => {
                    Assert.That(
                        ChildrenIn(flow, engineering),
                        Is.Not.Empty,
                        "Browsing without a view has to show the engineering nodes.");

                    Assert.That(
                        ChildrenIn(flow, operations),
                        Is.Not.Empty,
                        "Browsing without a view has to show the operations nodes.");
                });

                phase.Enter("changing to the engineering view");

                SampleFormDriver.Select(tree, flow);

                var views = WinFormsHarness.FindControl(form, "ViewCB") as ComboBox;

                Assert.That(views, Is.Not.Null, "The sample no longer offers a view to pick.");

                object view = views.Items
                    .Cast<object>()
                    .FirstOrDefault(item => (item as ReferenceDescription)?.BrowseName.Name == "Engineering");

                Assert.That(
                    view,
                    Is.Not.Null,
                    "The sample did not list the engineering view after connecting. " +
                    $"It offers: {string.Join(", ", views.Items.Cast<object>().Select(item => item.ToString()))}");

                views.SelectedItem = view;

                Assert.That(
                    SampleFormDriver.TryInvokeHandler(form, "ChangeViewBTN_Click", null),
                    Is.True,
                    "The sample no longer has a handler for its Change button.");

                bool rebrowsed = await SampleFormDriver.PumpUntilAsync(
                    () => ChildrenIn(flow, engineering).Length > 0 || flow.Nodes.Count == 0,
                    s_step,
                    token).ConfigureAwait(true);

                Assert.That(rebrowsed, Is.True, "Changing the view never re-browsed the flow meter.");

                await TestContext.Out
                    .WriteLineAsync(
                        $"Through the engineering view it shows: {SampleFormDriver.ChildrenOf(flow)}")
                    .ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(
                        ChildrenIn(flow, operations),
                        Is.Empty,
                        "The engineering view suppresses the operations nodes, so a client which " +
                        "put the view on its browse must not see them any more. Seeing them means " +
                        "the browse went out without the view. " +
                        $"Unfiltered it showed: {string.Join(", ", unfiltered)}");

                    Assert.That(
                        ChildrenIn(flow, engineering),
                        Is.Not.Empty,
                        "The engineering view keeps the engineering nodes, so an empty tree means " +
                        "the re-browse failed rather than filtered.");
                });
            }, ct);
        }
        #endregion

        #region Helpers
        /// <summary>
        /// Starts the sample's server, builds the client's real main form, connects it and
        /// runs the body.
        /// </summary>
        /// <remarks>
        /// The body runs on the STA thread of the harness with a message loop pumping, which
        /// is what lets the <c>async void</c> handlers of a sample get anywhere.
        /// </remarks>
        private static async Task DriveAsync(
            string sampleName,
            Func<Form, ISession, ClientPhase, CancellationToken, Task> body,
            CancellationToken ct)
        {
            SampleClientUnderTest client = SampleClientFactories.All
                .Single(entry => entry.Sample.Name == sampleName);

            SampleServerUnderTest server = SampleServerFactories.All
                .Single(entry => entry.Sample.Name == sampleName);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(sampleName, server.Sample.ServerConfig, server.CreateServer, ct)
                .ConfigureAwait(false);

            DialogWatchdog watchdog = null;
            var phase = new ClientPhase();

            try
            {
                await WinFormsHarness.RunAsync(
                    async dialogs => {
                        watchdog = dialogs;

                        await ConnectAndRunAsync(client, host.EndpointUrl, body, phase, ct).ConfigureAwait(true);
                    },
                    TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(20))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException expired)
            {
                throw phase.Explain(expired);
            }

            if (watchdog?.DuringTeardown.Count > 0)
            {
                await TestContext.Out.WriteLineAsync(
                    $"{sampleName}: while the form was being disposed - " +
                    string.Join("; ", watchdog.DuringTeardown))
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task ConnectAndRunAsync(
            SampleClientUnderTest client,
            string endpointUrl,
            Func<Form, ISession, ClientPhase, CancellationToken, Task> body,
            ClientPhase phase,
            CancellationToken ct)
        {
            phase.Enter("loading the configuration of the sample");

            using var pki = new TemporaryPki($"client-{client.Sample.Name}");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(client.Sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            configuration.TransportQuotas.OperationTimeout = kOperationTimeout;

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            phase.Enter("creating its client certificate");

            Assert.That(
                await application.CheckApplicationInstanceCertificatesAsync(true, null, ct).ConfigureAwait(true),
                Is.True,
                "The client certificate of the sample could not be created.");

            phase.Enter("building its main form");

            using Form form = client.CreateMainForm(configuration, NullTelemetry.Instance);

            // handles for the whole form, not only for the form itself: a tree or a list which
            // never got one silently drops what a test does to it
            SampleFormDriver.CreateHandles(form);

            ConnectServerCtrl connect = WinFormsHarness.GetConnectControl(form);

            phase.Enter($"connecting to {endpointUrl}");

            ISession session = await connect
                .ConnectAsync(NullTelemetry.Instance, endpointUrl, false, 30_000, ct)
                .ConfigureAwait(true);

            Assert.That(session, Is.Not.Null, "The sample did not connect.");

            try
            {
                await body(form, session, phase, ct).ConfigureAwait(true);
            }
            finally
            {
                phase.Enter("disconnecting");

                // awaited, not the synchronous Disconnect: that one blocks the UI thread on
                // work which needs the same message loop, and the fixture deadlocks
                await connect.DisconnectAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// The root of the browse tree, once the sample's post connect logic has put it there.
        /// </summary>
        private static async Task<TreeNode> RootOfAsync(TreeView tree, CancellationToken ct)
        {
            bool rooted = await SampleFormDriver.PumpUntilAsync(
                () => tree.Nodes.Count > 0,
                s_step,
                ct).ConfigureAwait(true);

            Assert.That(
                rooted,
                Is.True,
                "The sample never rooted its browse tree, so its post connect logic did not run.");

            return tree.Nodes[0];
        }

        /// <summary>
        /// The browse names of the children of a tree node which are in one namespace.
        /// </summary>
        private static string[] ChildrenIn(TreeNode node, ushort namespaceIndex)
        {
            return node.Nodes
                .Cast<TreeNode>()
                .Select(child => child.Tag as ReferenceDescription)
                .Where(reference => reference != null && reference.BrowseName.NamespaceIndex == namespaceIndex)
                .Select(reference => reference.BrowseName.Name)
                .ToArray();
        }

        private static ushort NamespaceIndexOf(ISession session, string namespaceUri)
        {
            int index = session.NamespaceUris.GetIndex(namespaceUri);

            Assert.That(
                index,
                Is.GreaterThanOrEqualTo(0),
                $"The server does not serve the namespace '{namespaceUri}'. It serves: " +
                string.Join(", ", session.NamespaceUris.ToArray()));

            return (ushort)index;
        }

        /// <summary>
        /// What the user authentication client last reported in its status bar.
        /// </summary>
        private static string Reported(Form form)
        {
            return SampleFormDriver.StatusText(form, "StatusBar", "ActionStatusLB");
        }

        private static void ClearReport(Form form)
        {
            var status = WinFormsHarness.FindControl(form, "StatusBar") as StatusStrip;

            ToolStripItem label = status?.Items
                .Cast<ToolStripItem>()
                .FirstOrDefault(item => item.Name == "ActionStatusLB");

            if (label != null)
            {
                label.Text = string.Empty;
            }
        }

        /// <summary>
        /// The archive item of the historical access server the read is driven against.
        /// </summary>
        /// <remarks>
        /// The double item of the fixed Sample archive, which is the one the tier 1.5 fixture
        /// reads too: it is recorded rather than still being collected, so a raw read of it
        /// returns the same values every run.
        /// </remarks>
        private static async Task<NodeId> ArchiveItemAsync(ISession session, CancellationToken ct)
        {
            ushort ns = NamespaceIndexOf(session, Quickstarts.HistoricalAccessServer.Namespaces.HistoricalAccess);

            IReadOnlyList<ReferenceDescription> items = await SessionOps
                .BrowseAsync(session, new NodeId("Sample", ns), ct)
                .ConfigureAwait(true);

            ReferenceDescription item = items.FirstOrDefault(child =>
                child.NodeClass == NodeClass.Variable
                && child.BrowseName.Name.Contains("Double", StringComparison.Ordinal));

            Assert.That(
                item,
                Is.Not.Null,
                "The Sample folder of the archive holds no Double item. It holds: " +
                string.Join(", ", items.Select(child => child.BrowseName.Name)));

            return ExpandedNodeId.ToNodeId(item.NodeId, session.NamespaceUris);
        }
        #endregion
    }
}
