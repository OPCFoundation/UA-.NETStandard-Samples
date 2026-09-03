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
    /// Tier 2: the node management client has to build an address space, not only connect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole of this sample is what its buttons do, and none of it runs before one is
    /// pressed: the server starts with four nodes of its own and an empty Devices folder, so
    /// a client which connects and lists what it finds proves nothing at all.
    /// </para>
    /// <para>
    /// So the fixture drives all four services through the form - add an object, reference it
    /// from the group, drop the reference again, delete the node - and asserts after each one
    /// that the lists show what that service is supposed to have done. In particular that
    /// dropping a reference leaves the node alone, which is the difference between the two
    /// pairs of services and the easiest thing for a client to get wrong.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class NodeManagementClientTests
    {
        private const int kTimeout = 120_000;
        private const string kSample = "NodeManagement";

        /// <summary>
        /// The node the fixture creates. Named after the fixture so that a leftover from a
        /// previous run is recognisable rather than confusing.
        /// </summary>
        private const string kNode = "PumpUnderTest";

        /// <summary>
        /// How long the sample gets to answer a press.
        /// </summary>
        private static readonly TimeSpan s_actionTimeout = TimeSpan.FromSeconds(30);

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TheClientAddsReferencesAndDeletesANode(CancellationToken ct)
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

            SampleFormDriver.CreateHandles(form);

            ConnectServerCtrl connect = WinFormsHarness.GetConnectControl(form);

            ISession session = await connect
                .ConnectAsync(NullTelemetry.Instance, endpointUrl, false, 30_000, ct)
                .ConfigureAwait(true);

            try
            {
                Assert.That(session, Is.Not.Null, "The sample did not connect.");

                var add = WinFormsHarness.FindControl(form, "AddObjectBTN") as Button;
                var name = WinFormsHarness.FindControl(form, "NewNameTB") as TextBox;

                Assert.Multiple(() => {
                    Assert.That(add, Is.Not.Null, "The sample no longer has an Add object button.");
                    Assert.That(name, Is.Not.Null, "The sample no longer has a name box.");
                });

                // the buttons are enabled by the sample's own async void ConnectComplete
                // handler, which is also where it resolves the folders it works on
                bool ready = await SampleFormDriver
                    .PumpUntilAsync(() => add.Enabled, s_actionTimeout, ct)
                    .ConfigureAwait(true);

                Assert.That(ready, Is.True, "The sample never finished its post connect logic.");

                name.Text = kNode;

                await PressAsync(form, "AddObjectBTN", add, ct).ConfigureAwait(true);

                bool added = await SampleFormDriver
                    .PumpUntilAsync(() => Row(form, "NodesLV", kNode) != null, s_actionTimeout, ct)
                    .ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(added, Is.True, "AddNodes did not put the node in the list; " + Seen(form));

                    Assert.That(
                        StatusText(form),
                        Does.Contain("Good"),
                        "AddNodes has to succeed below the folder the server opens to its clients. " + Seen(form));
                });

                Select(form, "NodesLV", kNode);

                await PressAsync(form, "AddReferenceBTN", Button(form, "AddReferenceBTN"), ct).ConfigureAwait(true);

                bool referenced = await SampleFormDriver
                    .PumpUntilAsync(() => Row(form, "GroupLV", kNode) != null, s_actionTimeout, ct)
                    .ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(referenced, Is.True, "AddReferences did not make the node reachable from the group; " + Seen(form));

                    Assert.That(
                        Row(form, "NodesLV", kNode),
                        Is.Not.Null,
                        "A reference must not move the node out of the folder it was created in.");
                });

                Select(form, "GroupLV", kNode);

                await PressAsync(form, "DeleteReferenceBTN", Button(form, "DeleteReferenceBTN"), ct).ConfigureAwait(true);

                bool dropped = await SampleFormDriver
                    .PumpUntilAsync(() => Row(form, "GroupLV", kNode) == null, s_actionTimeout, ct)
                    .ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(dropped, Is.True, "DeleteReferences left the node in the group; " + Seen(form));

                    Assert.That(
                        Row(form, "NodesLV", kNode),
                        Is.Not.Null,
                        "Deleting a reference to a node must not delete the node; " + Seen(form));
                });

                Select(form, "NodesLV", kNode);

                await PressAsync(form, "DeleteBTN", Button(form, "DeleteBTN"), ct).ConfigureAwait(true);

                bool deleted = await SampleFormDriver
                    .PumpUntilAsync(() => Row(form, "NodesLV", kNode) == null, s_actionTimeout, ct)
                    .ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(deleted, Is.True, "DeleteNodes left the node behind; " + Seen(form));

                    Assert.That(
                        StatusText(form),
                        Does.Contain("Good"),
                        "Deleting a node a client created has to succeed. " + Seen(form));
                });
            }
            finally
            {
                // the sample's own disconnect, because it deletes its subscription first:
                // closing a session which still carries one waits for the publish pipeline
                SampleFormDriver.TryInvokeHandler(form, "Server_DisconnectMI_ClickAsync", form);

                await SampleFormDriver
                    .PumpUntilAsync(() => connect.Session == null, s_actionTimeout, CancellationToken.None)
                    .ConfigureAwait(true);
            }
        }

        #region Form Helpers
        /// <summary>
        /// Invokes the click handler of a button and asserts that the sample still has it.
        /// </summary>
        private static async Task PressAsync(Form form, string button, Button control, CancellationToken ct)
        {
            Assert.That(
                SampleFormDriver.TryInvokeHandler(form, $"{button}_ClickAsync", control),
                Is.True,
                $"The sample no longer has a {button}_ClickAsync handler.");

            // let the handler get as far as its first await before anything is asserted
            await SampleFormDriver
                .PumpUntilAsync(() => false, TimeSpan.FromMilliseconds(50), ct)
                .ConfigureAwait(true);
        }

        private static Button Button(Form form, string name)
        {
            var button = WinFormsHarness.FindControl(form, name) as Button;

            Assert.That(button, Is.Not.Null, $"The sample no longer has a {name}.");

            return button;
        }

        /// <summary>
        /// The row of a node in one of the lists, or null.
        /// </summary>
        /// <remarks>
        /// The name column is indented by the depth of the node, so it is compared trimmed.
        /// </remarks>
        private static ListViewItem Row(Form form, string list, string node)
        {
            var view = WinFormsHarness.FindControl(form, list) as ListView;

            return view?.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(item => string.Equals(item.Text.Trim(), node, StringComparison.Ordinal));
        }

        /// <summary>
        /// Selects the row of a node, the way a user would before pressing a button.
        /// </summary>
        private static void Select(Form form, string list, string node)
        {
            ListViewItem row = Row(form, list, node);

            Assert.That(row, Is.Not.Null, $"'{node}' is not in {list}; " + Seen(form));

            row.Selected = true;
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
            return $"the plant shows [{Rows(form, "NodesLV")}], the group shows [{Rows(form, "GroupLV")}] " +
                $"and the status line says '{StatusText(form)}'";
        }

        private static string Rows(Form form, string list)
        {
            var view = WinFormsHarness.FindControl(form, list) as ListView;

            return view == null
                ? "<no list>"
                : string.Join(", ", view.Items.Cast<ListViewItem>().Select(item => item.Text.Trim()));
        }
        #endregion
    }
}
