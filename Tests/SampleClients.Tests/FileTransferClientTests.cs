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
    /// Tier 2: the file transfer client has to keep the place the user browsed to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A file transfer client is used by navigating: open a directory, look at what is in
    /// it, pick a file. Anything which throws the tree away underneath that is worse than
    /// a cosmetic defect - the user loses their position, and with it the answer to "which
    /// directory am I about to upload into".
    /// </para>
    /// <para>
    /// A reconnect is the event which used to do exactly that. The sample rebuilt its file
    /// system client, and with it the whole tree, whenever the connection hiccupped, on the
    /// assumption that the reconnect brings a new session whose file handles are gone. A
    /// managed session keeps its <c>ISession</c> across a reconnect, so that rebuild was
    /// both unnecessary and destructive. This fixture pins the behaviour down.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class FileTransferClientTests
    {
        /// <summary>
        /// How long the sample gets to start its server, connect and browse.
        /// </summary>
        private const int kTimeout = 120_000;

        /// <summary>
        /// How long a single service call of the sample may take.
        /// </summary>
        private const int kOperationTimeout = 30_000;

        /// <summary>
        /// The name of the sample in the catalog.
        /// </summary>
        private const string kSample = "FileTransfer";

        /// <summary>
        /// The mount the shipped configuration of the sample server publishes.
        /// </summary>
        private const string kMount = "SampleFiles";

        /// <summary>
        /// A reconnect must not cost the user the directory they were looking at.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SelectionSurvivesAReconnect(CancellationToken ct)
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
            using var pki = new TemporaryPki($"client-filetransfer-{kSample}");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(sample.Sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            configuration.TransportQuotas.OperationTimeout = kOperationTimeout;

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            bool certificateOk = await application
                .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                .ConfigureAwait(true);

            Assert.That(certificateOk, Is.True, "The client certificate of the sample could not be created.");

            using Form form = sample.CreateMainForm(configuration, NullTelemetry.Instance);

            CreateHandles(form);

            ConnectServerCtrl connect = WinFormsHarness.GetConnectControl(form);

            ISession session = await connect
                .ConnectAsync(NullTelemetry.Instance, endpointUrl, false, 30_000, ct)
                .ConfigureAwait(true);

            try
            {
                Assert.That(
                    session,
                    Is.InstanceOf<ManagedSession>(),
                    "The sample no longer connects with a managed session, which is what keeps " +
                    "the same ISession across a reconnect - the premise of this test.");

                var tree = (TreeView)WinFormsHarness.FindControl(form, "DirectoriesTV");

                Assert.That(
                    tree,
                    Is.Not.Null,
                    "The file transfer client no longer has a 'DirectoriesTV'. Rename it here too.");

                // the sample browses the file system in its ConnectComplete handler
                bool browsed = await WaitAsync(
                    () => FindMount(tree) != null,
                    ct).ConfigureAwait(true);

                Assert.That(
                    browsed,
                    Is.True,
                    $"The client never showed the '{kMount}' mount of its own server, so its " +
                    "post connect browse did not run.");

                // navigate somewhere, the way a user would
                TreeNode mount = FindMount(tree);

                tree.SelectedNode = mount;
                mount.Expand();

                // both halves of the navigation are asynchronous and independent: selecting
                // lists the entries, expanding browses the subdirectories. The tree is only
                // settled once the placeholder below the mount has been replaced by the real
                // subdirectories, which is what makes the node count below meaningful.
                bool listed = await WaitAsync(
                    () => HasRows(form, "EntriesLV") && IsLoaded(mount),
                    ct).ConfigureAwait(true);

                Assert.That(
                    listed,
                    Is.True,
                    $"Selecting and expanding '{kMount}' never listed its content and its " +
                    "subdirectories.");

                int nodesBefore = tree.GetNodeCount(includeSubTrees: true);

                // the connection hiccups and comes back on the same session
                RaiseReconnectComplete(form);

                // give a rebuild the chance to happen before concluding that none did
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(
                        tree.SelectedNode,
                        Is.SameAs(mount),
                        "The reconnect moved the selection, so the user lost the directory they " +
                        "were looking at. Note this compares the node object: a rebuilt tree " +
                        "carries new nodes even where the text matches.");

                    Assert.That(
                        tree.GetNodeCount(includeSubTrees: true),
                        Is.EqualTo(nodesBefore),
                        "The reconnect rebuilt the tree instead of leaving it alone.");

                    Assert.That(
                        HasRows(form, "EntriesLV"),
                        Is.True,
                        "The reconnect emptied the entry list.");
                });

                // and the sample is usable again afterwards
                var upload = (Button)WinFormsHarness.FindControl(form, "UploadBTN");

                Assert.That(
                    upload.Enabled,
                    Is.True,
                    "The commands stayed disabled after the reconnect, so the client is stuck.");
            }
            finally
            {
                await connect.DisconnectAsync(ct).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Reports a completed reconnect to the sample, the way the connect control does.
        /// </summary>
        /// <remarks>
        /// The event belongs to the shared control and cannot be raised from outside it, so
        /// the handler the sample registered is called directly. What is under test is what
        /// the sample does with the notification, not how the control delivers it.
        /// </remarks>
        private static void RaiseReconnectComplete(Form form)
        {
            MethodInfo handler = form.GetType().GetMethod(
                "Server_ReconnectCompleteAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(
                handler,
                Is.Not.Null,
                "The file transfer client no longer has a 'Server_ReconnectCompleteAsync'. " +
                "Rename it here too.");

            handler.Invoke(form, [form, EventArgs.Empty]);
        }

        /// <summary>
        /// The node of the mount the sample server publishes, or null while it is not there.
        /// </summary>
        private static TreeNode FindMount(TreeView tree)
        {
            foreach (TreeNode root in tree.Nodes)
            {
                foreach (TreeNode child in root.Nodes)
                {
                    if (child.Text == kMount)
                    {
                        return child;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// True once a node carries its real subdirectories rather than the placeholder the
        /// sample adds to give an unbrowsed node its expander.
        /// </summary>
        private static bool IsLoaded(TreeNode node)
        {
            return node.Nodes.Count == 0 || node.Nodes[0].Text.Length > 0;
        }

        private static bool HasRows(Control parent, string name)
        {
            var list = (ListView)WinFormsHarness.FindControl(parent, name);

            return list != null && list.Items.Count > 0;
        }

        private static void CreateHandles(Control parent)
        {
            _ = parent.Handle;

            foreach (Control child in parent.Controls)
            {
                CreateHandles(child);
            }
        }

        private static async Task<bool> WaitAsync(Func<bool> condition, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);

            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(100, ct).ConfigureAwait(true);
            }

            return condition();
        }
    }
}
