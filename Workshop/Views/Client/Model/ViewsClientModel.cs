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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Samples.Client;

namespace Quickstarts.ViewsClient.Model
{
    /// <summary>
    /// The client model of the Views client: lists the views the server offers and
    /// browses the address space through the one which is selected.
    /// </summary>
    /// <remarks>
    /// A view is a filter the server applies to a browse: a node or a reference which is
    /// not in the view is not returned. The client can only see that by putting the view
    /// on its browse requests, which is what <see cref="SelectView"/> prepares and
    /// <see cref="BrowseAsync"/> does. The window hands the same <see cref="ViewDescription"/>
    /// to the shared browse control, so its tree browses through the view as well. There
    /// are no subscriptions.
    /// </remarks>
    public sealed class ViewsClientModel : SampleClientModel
    {
        private IReadOnlyList<ReferenceDescription> m_views = Array.Empty<ReferenceDescription>();

        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public ViewsClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// The views the server offers below its Views folder, found when the session
        /// was attached.
        /// </summary>
        /// <remarks>
        /// Kept as the references the browse returned: the window puts them straight into
        /// its combo box, and a <see cref="ReferenceDescription"/> renders as the display
        /// name of its target.
        /// </remarks>
        public IReadOnlyList<ReferenceDescription> Views => m_views;

        /// <summary>
        /// The view browses go through, or null for the whole address space.
        /// </summary>
        public ViewDescription CurrentView { get; private set; }

        /// <summary>
        /// Selects the view to browse through.
        /// </summary>
        /// <param name="reference">One of <see cref="Views"/>, or null (or a reference with
        /// a null node id) for the whole address space.</param>
        /// <returns>The view description to put on a browse, which is also <see cref="CurrentView"/>.</returns>
        public ViewDescription SelectView(ReferenceDescription reference)
        {
            ISession session = RequireSession();

            ViewDescription view = null;

            if (reference != null && !reference.NodeId.IsNull)
            {
                // the version and the timestamp are left at their defaults: the sample
                // server keeps no history of its views, so there is only the current one.
                view = new ViewDescription {
                    ViewId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris),
                    ViewVersion = 0,
                    Timestamp = DateTime.MinValue,
                };
            }

            CurrentView = view;

            return view;
        }

        /// <summary>
        /// Browses the hierarchical references of a node through <see cref="CurrentView"/>.
        /// </summary>
        /// <param name="nodeId">The node to browse.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The references the server returned for the view.</returns>
        public async Task<IReadOnlyList<ReferenceDescription>> BrowseAsync(NodeId nodeId, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            var nodeToBrowse = new BrowseDescription {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All,
            };

            // the view goes on the request: that is the whole difference between a browse
            // which sees the filtered address space and one which does not.
            List<ReferenceDescription> references = await SampleSession.BrowseAsync(
                session,
                CurrentView,
                nodeToBrowse,
                true,
                ct).ConfigureAwait(false);

            return references ?? new List<ReferenceDescription>();
        }

        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            var nodeToBrowse = new BrowseDescription {
                NodeId = Opc.Ua.ObjectIds.ViewsFolder,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All,
            };

            List<ReferenceDescription> references = await SampleSession.BrowseAsync(
                session,
                nodeToBrowse,
                false,
                ct).ConfigureAwait(false);

            m_views = references ?? new List<ReferenceDescription>();
        }

        /// <inheritdoc/>
        protected override Task OnDetachingAsync()
        {
            m_views = Array.Empty<ReferenceDescription>();
            CurrentView = null;

            return Task.CompletedTask;
        }
    }
}
