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
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.Controls;

namespace Quickstarts.AliasNames.Client
{
    // The source generator emits a Quickstarts.AliasNames.BrowseNames for the model of the
    // server. This namespace is a child of that one, so it would win over the standard set of
    // the same name: both are named apart here.
    using ModelNames = Quickstarts.AliasNames.BrowseNames;
    using ObjectIds = Opc.Ua.ObjectIds;

    /// <summary>
    /// The main form of the OPC UA Part 17 alias names Quickstart client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The form puts the two ways of finding a node side by side, because Part 17 only makes
    /// sense as the difference between them.
    /// </para>
    /// <para>
    /// The upper list is the plant as an ordinary client sees it: the result of browsing down
    /// from the Objects folder. Every row is a node found by structure, and its last column is
    /// the tag name that node answers to - which the client did not browse for, because the
    /// name is not in the address space. It asked an
    /// <see cref="AliasNameResolver"/> to map the node back to a name.
    /// </para>
    /// <para>
    /// The lower half is the search that runs the other way, and is the reason a client would
    /// use Part 17 at all: given a name, or a wildcard over names, which nodes are meant? Pick
    /// a category, type a pattern, and the server answers from an index it keeps beside the
    /// address space. Nothing in the search knows how the plant is laid out.
    /// </para>
    /// <para>
    /// The two mutation buttons show the other half of the specification: a tag list is
    /// configuration, not a compile time constant. They are left enabled for every account on
    /// purpose, because seeing the server answer <c>BadUserAccessDenied</c> to an anonymous
    /// session is as much a part of the sample as seeing it succeed for an administrator.
    /// </para>
    /// </remarks>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        /// <param name="telemetry">The telemetry context of the application.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_telemetry = telemetry;

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62577/Quickstarts/AliasNamesServer";
            this.Text = m_configuration.ApplicationName;

            // the two accounts of the sample server. Which one is signed in decides only
            // whether the mutation Methods are allowed - searching is open to everyone.
            IdentityCB.Items.AddRange(new object[] { kAnonymous, kSecurityAdmin });
            IdentityCB.SelectedIndex = 0;
            IdentityCB.SelectedIndexChanged += IdentityCB_SelectedIndexChanged;

            // the categories this client can search. The standard one needs no prior
            // knowledge of the server; the other three are this server's own.
            CategoryCB.Items.AddRange(new object[] {
                new CategoryChoice("TagVariables (standard, i=23479)", null),
                new CategoryChoice("PlantTags (application defined)", AliasCategories.PlantTags),
                new CategoryChoice("PlantTags/Reactor", AliasCategories.Reactor),
                new CategoryChoice("PlantTags/Boiler", AliasCategories.Boiler),
            });

            CategoryCB.SelectedIndex = 0;

            UpdateIdentityHint();
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.ConnectAsync(m_telemetry);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        private void Server_DisconnectMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Disconnect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        private void Server_DiscoverMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Discover(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Picks the identity token the next connect uses.
        /// </summary>
        private void IdentityCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                string account = IdentityCB.SelectedItem as string;

                ConnectServerCTRL.UserIdentity = string.Equals(account, kAnonymous, StringComparison.Ordinal)
                    ? null
                    : new UserIdentity(account, Encoding.UTF8.GetBytes(account));

                UpdateIdentityHint();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after connecting to or disconnecting from the server.
        /// </summary>
        private async void Server_ConnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                await ReleaseResolverAsync().ConfigureAwait(true);

                m_session = ConnectServerCTRL.Session;

                if (m_session == null)
                {
                    PlantLV.Items.Clear();
                    AliasLV.Items.Clear();
                    LastChangeLB.Text = string.Empty;
                    SetButtonsEnabled(false);
                    return;
                }

                // the reverse mapping of the upper list. The standard category holds every tag
                // of the plant, so one resolver over it answers for the whole address space.
                //
                // The refresh mode is left at its default, Manual: the resolver then loads the
                // inventory once and reloads it when this form asks. The automatic modes are
                // worth having when a server's tag list changes under a long lived client, but
                // they cost a poll or a subscription, and this form re-reads on every refresh
                // anyway.
                m_resolver = new AliasNameResolver(AliasNameClient.OpenStandardTagVariables(m_session));

                SetButtonsEnabled(true);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after a communication error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
                SetButtonsEnabled(false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the display after reconnecting to the server.
        /// </summary>
        private async void Server_ReconnectCompleteAsync(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                SetButtonsEnabled(m_session != null);

                if (m_session != null)
                {
                    await RefreshAsync().ConfigureAwait(true);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ConnectServerCTRL.Disconnect();
        }

        /// <summary>
        /// Reads the plant and searches the selected category again.
        /// </summary>
        private async void RefreshBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Searches the selected category for the pattern in the text box.
        /// </summary>
        private async void FindBTN_ClickAsync(object sender, EventArgs e)
        {
            await SearchAsync(verbose: false).ConfigureAwait(true);
        }

        /// <summary>
        /// Searches the selected category with the optional verbose Method.
        /// </summary>
        private async void FindVerboseBTN_ClickAsync(object sender, EventArgs e)
        {
            await SearchAsync(verbose: true).ConfigureAwait(true);
        }

        /// <summary>
        /// Searches again whenever another category is picked.
        /// </summary>
        private async void CategoryCB_SelectedIndexChangedAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session != null)
                {
                    await SearchAsync(verbose: false).ConfigureAwait(true);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
        }

        /// <summary>
        /// Gives the node selected in the plant list another tag name.
        /// </summary>
        /// <remarks>
        /// The target is taken from the upper list rather than typed, because that is the
        /// realistic direction: an engineer looking at a signal decides what the control
        /// system calls it. The name goes into whichever category is selected below.
        /// </remarks>
        private async void AddAliasBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null || PlantLV.SelectedItems.Count == 0)
                {
                    Report("Adding an alias", "select a node in the plant first");
                    return;
                }

                string name = NewAliasTB.Text?.Trim();

                if (string.IsNullOrEmpty(name))
                {
                    Report("Adding an alias", "type a tag name first");
                    return;
                }

                var node = (PlantRow)PlantLV.SelectedItems[0].Tag;

                AliasNameClient category = OpenSelectedCategory();

                var request = new AliasNameAddRequest(
                    name,
                    NodeId.ToExpandedNodeId(node.NodeId, m_session.NamespaceUris),

                    // no server uri: the target is a node of the server being talked to
                    null,

                    // the Part 17 8.2 reference type which says "this name stands for that
                    // node". A store may hold other reference types, but only this one is an
                    // alias association.
                    ReferenceTypeIds.AliasFor);

                IReadOnlyList<StatusCode> results = await category
                    .AddAliasesToCategoryAsync(new[] { request }, default)
                    .ConfigureAwait(true);

                Report($"Adding '{name}' for {node.Path}", results[0]);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ReportRefusal("Adding an alias", exception);
            }
        }

        /// <summary>
        /// Removes the alias selected in the search results.
        /// </summary>
        private async void DeleteAliasBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null || AliasLV.SelectedItems.Count == 0)
                {
                    Report("Deleting an alias", "select a search result first");
                    return;
                }

                var alias = (AliasRow)AliasLV.SelectedItems[0].Tag;

                AliasNameClient category = OpenSelectedCategory();

                IReadOnlyList<StatusCode> results = await category
                    .DeleteAliasesFromCategoryAsync(
                        new[] { new AliasNameDeleteRequest(alias.Name, alias.Target) },
                        default)
                    .ConfigureAwait(true);

                Report($"Deleting '{alias.Name}'", results[0]);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                ReportRefusal("Deleting an alias", exception);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Reads the plant and searches the selected category.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (m_session == null)
            {
                return;
            }

            // the inventory may have changed, so the cached reverse mapping is dropped
            m_resolver?.Invalidate();

            await LoadPlantAsync().ConfigureAwait(true);
            await SearchAsync(verbose: false).ConfigureAwait(true);
        }

        /// <summary>
        /// Fills the upper list by browsing the plant, and names each node from the alias
        /// inventory.
        /// </summary>
        private async Task LoadPlantAsync()
        {
            PlantLV.Items.Clear();

            NodeId plantId = await ResolveModelNodeAsync(ModelNames.Plant).ConfigureAwait(true);

            if (plantId.IsNull)
            {
                return;
            }

            foreach (ReferenceDescription unit in
                await BrowseAsync(plantId, NodeClass.Object).ConfigureAwait(true))
            {
                NodeId unitId = ExpandedNodeId.ToNodeId(unit.NodeId, m_session.NamespaceUris);

                foreach (ReferenceDescription signal in
                    await BrowseAsync(unitId, NodeClass.Variable).ConfigureAwait(true))
                {
                    NodeId signalId = ExpandedNodeId.ToNodeId(signal.NodeId, m_session.NamespaceUris);

                    var row = new PlantRow {
                        Path = $"{unit.BrowseName.Name}/{signal.BrowseName.Name}",
                        NodeId = signalId,
                    };

                    DataValue value = await ReadAsync(signalId).ConfigureAwait(true);

                    var item = new ListViewItem(row.Path) { Tag = row };

                    item.SubItems.Add(signalId.ToString());
                    item.SubItems.Add(StatusCode.IsGood(value.StatusCode)
                        ? value.WrappedValue.ToString()
                        : value.StatusCode.ToString());

                    // the column the sample is about: the node was found by structure, and
                    // this is the name the alias inventory knows it by. Nothing was browsed
                    // to get it - the name does not exist in the address space.
                    item.SubItems.Add(await ResolveAliasNameAsync(signalId).ConfigureAwait(true));

                    PlantLV.Items.Add(item);
                }
            }
        }

        /// <summary>
        /// Searches the selected category and fills the lower list.
        /// </summary>
        private async Task SearchAsync(bool verbose)
        {
            if (m_session == null)
            {
                return;
            }

            try
            {
                AliasLV.Items.Clear();

                AliasNameClient category = OpenSelectedCategory();

                string pattern = string.IsNullOrEmpty(PatternTB.Text) ? "%" : PatternTB.Text;

                await ShowLastChangeAsync(category).ConfigureAwait(true);

                if (verbose)
                {
                    IReadOnlyList<AliasNameVerboseDataType> found = await category
                        .FindAliasVerboseAsync(pattern, null, default)
                        .ConfigureAwait(true);

                    foreach (AliasNameVerboseDataType alias in found)
                    {
                        await AddAliasRowAsync(
                            alias.AliasName.Name,
                            alias.ReferencedNodes.ToArray(),
                            NameOfCategory(alias.AliasNameCategoryId),
                            alias.ServerUris.ToArray()).ConfigureAwait(true);
                    }

                    Report($"FindAliasVerbose('{pattern}')", $"{found.Count} alias(es)");
                }
                else
                {
                    IReadOnlyList<AliasNameDataType> found = await category
                        .FindAliasAsync(pattern, null, default)
                        .ConfigureAwait(true);

                    foreach (AliasNameDataType alias in found)
                    {
                        await AddAliasRowAsync(
                            alias.AliasName.Name,
                            alias.ReferencedNodes.ToArray(),

                            // FindAlias does not say which category an entry came from, nor
                            // whether its target is remote. That is what the verbose variant
                            // is for, and the two empty columns are the difference.
                            string.Empty,
                            Array.Empty<string>()).ConfigureAwait(true);
                    }

                    Report($"FindAlias('{pattern}')", $"{found.Count} alias(es)");
                }
            }
            catch (Exception exception)
            {
                ReportRefusal("Searching", exception);
            }
        }

        /// <summary>
        /// Adds one search result to the lower list, with the value of its target.
        /// </summary>
        private async Task AddAliasRowAsync(
            string name,
            IReadOnlyList<ExpandedNodeId> targets,
            string category,
            IReadOnlyList<string> serverUris)
        {
            ExpandedNodeId target = targets.Count > 0 ? targets[0] : ExpandedNodeId.Null;

            var row = new AliasRow { Name = name, Target = target };

            var item = new ListViewItem(name) { Tag = row };

            if (target.IsNull)
            {
                item.SubItems.Add(string.Empty);
                item.SubItems.Add(string.Empty);
            }
            else
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(target, m_session.NamespaceUris);

                item.SubItems.Add(nodeId.IsNull ? target.ToString() : nodeId.ToString());

                // reading the target is what proves the search answered with a usable
                // address rather than just a matching string
                DataValue value = nodeId.IsNull
                    ? DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown)
                    : await ReadAsync(nodeId).ConfigureAwait(true);

                item.SubItems.Add(StatusCode.IsGood(value.StatusCode)
                    ? value.WrappedValue.ToString()
                    : value.StatusCode.ToString());
            }

            item.SubItems.Add(category);
            item.SubItems.Add(string.Join(
                ", ",
                serverUris.Where(uri => !string.IsNullOrEmpty(uri))));

            AliasLV.Items.Add(item);
        }

        /// <summary>
        /// Shows the VersionTime of the selected category, or why there is none.
        /// </summary>
        /// <remarks>
        /// Part 17 §6.3.1. A client which caches an inventory watches this instead of
        /// re-reading the whole list: it changes whenever the category does. The standard
        /// well known categories of this server do not expose it, which is itself worth
        /// seeing in the field.
        /// </remarks>
        private async Task ShowLastChangeAsync(AliasNameClient category)
        {
            try
            {
                uint? lastChange = await category.ReadLastChangeAsync(default).ConfigureAwait(true);

                LastChangeLB.Text = lastChange.HasValue
                    ? $"LastChange {lastChange.Value}"
                    : "no LastChange";
            }
            catch (Exception)
            {
                LastChangeLB.Text = "no LastChange";
            }
        }

        /// <summary>
        /// The alias name of a node, or an empty cell when the inventory does not name it.
        /// </summary>
        private async Task<string> ResolveAliasNameAsync(NodeId nodeId)
        {
            if (m_resolver == null)
            {
                return string.Empty;
            }

            try
            {
                return await m_resolver
                    .ResolveAliasNameAsync(NodeId.ToExpandedNodeId(nodeId, m_session.NamespaceUris), default)
                    .ConfigureAwait(true)
                    ?? string.Empty;
            }
            catch (Exception)
            {
                // a node the inventory does not cover is an ordinary outcome, not an error
                return string.Empty;
            }
        }

        /// <summary>
        /// Opens the category selected in the drop down.
        /// </summary>
        /// <remarks>
        /// The standard entry costs no round trip at all - <c>OpenStandardTagVariables</c>
        /// knows the NodeId of the category and of its FindAlias Method from Part 17 §9.3.
        /// An application defined category is addressed by the namespace uri and identifier
        /// the server publishes it under.
        /// </remarks>
        private AliasNameClient OpenSelectedCategory()
        {
            var choice = (CategoryChoice)CategoryCB.SelectedItem;

            if (choice.CategoryName == null)
            {
                return AliasNameClient.OpenStandardTagVariables(m_session);
            }

            return new AliasNameClient(
                m_session,
                AliasCategories.NodeIdOf(choice.CategoryName, m_session.NamespaceUris));
        }

        /// <summary>
        /// The readable name of a category NodeId reported by a verbose search.
        /// </summary>
        private static string NameOfCategory(NodeId categoryId)
        {
            if (categoryId.IsNull)
            {
                return string.Empty;
            }

            // the categories of this server carry their name as a string identifier, which
            // reads better in a column than the whole node id
            return categoryId.IdentifierAsString;
        }

        /// <summary>
        /// Follows one browse name of the model from the Objects folder.
        /// </summary>
        private async Task<NodeId> ResolveModelNodeAsync(string browseName)
        {
            var wellKnownNamespaceUris = new NamespaceTable();
            wellKnownNamespaceUris.Append(Quickstarts.AliasNames.Namespaces.AliasNames);

            List<NodeId> nodes = await ClientUtils.TranslateBrowsePathsAsync(
                m_session,
                ObjectIds.ObjectsFolder,
                wellKnownNamespaceUris,
                default,
                "1:" + browseName).ConfigureAwait(true);

            return nodes.Count > 0 ? nodes[0] : NodeId.Null;
        }

        /// <summary>
        /// Browses the hierarchical children of a node of one node class.
        /// </summary>
        private async Task<IReadOnlyList<ReferenceDescription>> BrowseAsync(NodeId nodeId, NodeClass nodeClass)
        {
            var browse = new BrowseDescription {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)nodeClass,
                ResultMask = (uint)BrowseResultMask.All,
            };

            BrowseResponse response = await m_session
                .BrowseAsync(null, null, 0, new List<BrowseDescription> { browse }, default)
                .ConfigureAwait(true);

            return response.Results.ToArray()[0].References.ToArray();
        }

        /// <summary>
        /// Reads the value of a node without throwing on a bad status code.
        /// </summary>
        private async Task<DataValue> ReadAsync(NodeId nodeId)
        {
            var valuesToRead = new List<ReadValueId> {
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
            };

            ReadResponse response = await m_session
                .ReadAsync(null, 0, TimestampsToReturn.Both, valuesToRead, default)
                .ConfigureAwait(true);

            return response.Results.ToArray()[0];
        }

        /// <summary>
        /// Disposes the resolver of the previous session.
        /// </summary>
        private async Task ReleaseResolverAsync()
        {
            if (m_resolver != null)
            {
                AliasNameResolver resolver = m_resolver;
                m_resolver = null;

                await resolver.DisposeAsync().ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Disposes the resolver from a synchronous context, for the form's own Dispose.
        /// </summary>
        /// <remarks>
        /// Disposal of a resolver in the Manual refresh mode this client uses completes
        /// without doing any I/O - there is no timer and no subscription to unwind - so
        /// waiting for it here does not block on the server. It is documented as idempotent
        /// and as never throwing, and a form which is being torn down has no way to report a
        /// failure anyway.
        /// </remarks>
        private void ReleaseResolver()
        {
            AliasNameResolver resolver = m_resolver;
            m_resolver = null;

            resolver?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reports what the server answered to an operation the user asked for.
        /// </summary>
        private void Report(string what, StatusCode status)
        {
            ActionStatusLB.Text = $"{what} answered {status}";
            ActionStatusLB.ForeColor = StatusCode.IsGood(status) ? Color.Empty : Color.Red;
        }

        /// <summary>
        /// Reports a plain message.
        /// </summary>
        private void Report(string what, string outcome)
        {
            ActionStatusLB.Text = $"{what}: {outcome}";
            ActionStatusLB.ForeColor = Color.Empty;
        }

        /// <summary>
        /// Reports why the alias name client refused or could not complete a call.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="AliasNameClient"/> maps the Part 17 status codes onto ordinary .NET
        /// exceptions, so the refusals this sample is meant to show up arrive as
        /// <see cref="UnauthorizedAccessException"/> and
        /// <see cref="NotSupportedException"/> rather than as status codes.
        /// </para>
        /// <para>
        /// They go into the status bar rather than a message box: half the point of this
        /// sample is trying an operation as one account after another and comparing the
        /// answers, and a modal dialog between every click makes that tedious. It also keeps
        /// the buttons drivable from a test.
        /// </para>
        /// </remarks>
        private void ReportRefusal(string what, Exception exception)
        {
            string outcome = exception switch {
                UnauthorizedAccessException => "refused - this needs the SecurityAdmin role on an encrypted channel",
                NotSupportedException => "this category does not expose that method",
                ServiceResultException service => service.StatusCode.ToString(),
                _ => exception.Message,
            };

            ActionStatusLB.Text = $"{what}: {outcome}";
            ActionStatusLB.ForeColor = Color.Red;
        }

        /// <summary>
        /// Enables the controls which need a session.
        /// </summary>
        private void SetButtonsEnabled(bool enabled)
        {
            RefreshBTN.Enabled = enabled;
            FindBTN.Enabled = enabled;
            FindVerboseBTN.Enabled = enabled;
            AddAliasBTN.Enabled = enabled;
            DeleteAliasBTN.Enabled = enabled;
        }

        /// <summary>
        /// Explains what the selected account may do.
        /// </summary>
        private void UpdateIdentityHint()
        {
            IdentityHintLB.Text = string.Equals(
                IdentityCB.SelectedItem as string,
                kSecurityAdmin,
                StringComparison.Ordinal)
                ? "SecurityAdmin: may add and delete aliases, over an encrypted channel."
                : "Anonymous: may search the tag list, and is refused every change to it.";
        }
        #endregion

        #region Private Types
        /// <summary>
        /// One entry of the category drop down.
        /// </summary>
        /// <param name="Label">What the user sees.</param>
        /// <param name="CategoryName">
        /// The identifier of one of the server's own categories, or <c>null</c> for the
        /// standard TagVariables object.
        /// </param>
        private sealed record CategoryChoice(string Label, string CategoryName)
        {
            /// <inheritdoc/>
            public override string ToString() => Label;
        }

        /// <summary>
        /// One row of the plant list.
        /// </summary>
        private sealed class PlantRow
        {
            public string Path { get; init; }
            public NodeId NodeId { get; init; }
        }

        /// <summary>
        /// One row of the search results.
        /// </summary>
        private sealed class AliasRow
        {
            public string Name { get; init; }
            public ExpandedNodeId Target { get; init; }
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// The entry of the identity drop down which opens an anonymous Session.
        /// </summary>
        private const string kAnonymous = "Anonymous";

        /// <summary>
        /// The account of the sample server which holds the SecurityAdmin Role.
        /// </summary>
        private const string kSecurityAdmin = "secadmin";

        private readonly ApplicationConfiguration m_configuration;
        private readonly ITelemetryContext m_telemetry;
        private ISession m_session;
        private AliasNameResolver m_resolver;
        #endregion
    }
}
