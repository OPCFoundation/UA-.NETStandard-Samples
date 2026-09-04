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

namespace AggregationClient.Model
{
    /// <summary>
    /// One reference of a node, with the names the client displays already looked up.
    /// </summary>
    /// <param name="ReferenceTypeName">The display name of the reference type, or its inverse name for an inverse reference.</param>
    /// <param name="TargetName">The display name of the target node.</param>
    /// <param name="NodeClass">The node class of the target.</param>
    /// <param name="TypeDefinitionName">The display name of the type definition of the target.</param>
    /// <param name="Reference">The reference as the server returned it.</param>
    public sealed record ReferenceRow(
        string ReferenceTypeName,
        string TargetName,
        NodeClass NodeClass,
        string TypeDefinitionName,
        ReferenceDescription Reference);

    /// <summary>
    /// The client model of the Aggregation client: browses the references of a node and
    /// changes the user and the locale of the session.
    /// </summary>
    /// <remarks>
    /// The aggregation server presents the address spaces of other servers as its own,
    /// so what this client has to show is the references a node carries - in both
    /// directions, and paged in small batches to show that a client has to follow
    /// continuation points - and that the user and the locale of a session can be changed
    /// without opening a new one. There are no subscriptions.
    /// </remarks>
    public sealed class AggregationClientModel : SampleClientModel
    {
        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public AggregationClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// The user name the session was opened with, or null for an anonymous session.
        /// </summary>
        public string CurrentUserName
        {
            get
            {
                IUserIdentity identity = Session?.Identity;

                if (identity != null
                    && identity.TokenType == UserTokenType.UserName
                    && identity.TokenHandler is UserNameIdentityTokenHandler token)
                {
                    return token.UserName;
                }

                return null;
            }
        }

        /// <summary>
        /// The locales the session asked the server for, most preferred first.
        /// </summary>
        public IReadOnlyList<string> PreferredLocales => Session?.PreferredLocales.ToArray() ?? Array.Empty<string>();

        /// <summary>
        /// Reads the locales the server can render its texts in.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public async Task<IReadOnlyList<string>> ReadAvailableLocalesAsync(CancellationToken ct = default)
        {
            DataValue value = await RequireSession()
                .ReadValueAsync(Opc.Ua.VariableIds.Server_ServerCapabilities_LocaleIdArray, ct)
                .ConfigureAwait(false);

            if (value.IsNull)
            {
                return Array.Empty<string>();
            }

            return value.GetValue<string[]>(null) ?? Array.Empty<string>();
        }

        /// <summary>
        /// Changes the user and the locales of the session in place.
        /// </summary>
        /// <param name="identity">The new user, or an anonymous identity.</param>
        /// <param name="preferredLocales">The locales, most preferred first.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task UpdateSessionAsync(
            IUserIdentity identity,
            IReadOnlyList<string> preferredLocales,
            CancellationToken ct = default)
        {
            ISession session = RequireSession();

            // override the default diagnostics to get error messages.
            DiagnosticsMasks returnDiagnostics = session.ReturnDiagnostics;

            try
            {
                session.ReturnDiagnostics = DiagnosticsMasks.ServiceSymbolicIdAndText;
                await session.UpdateSessionAsync(identity, new List<string>(preferredLocales), ct).ConfigureAwait(false);
            }
            finally
            {
                session.ReturnDiagnostics = returnDiagnostics;
            }
        }

        /// <summary>
        /// Browses every reference of a node, in both directions, and looks up the names
        /// the client displays.
        /// </summary>
        /// <param name="nodeId">The node.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<IReadOnlyList<ReferenceRow>> BrowseReferencesAsync(NodeId nodeId, CancellationToken ct = default)
        {
            ISession session = RequireSession();

            List<ReferenceDescription> references = await BrowseAsync(session, nodeId, ct).ConfigureAwait(false);

            var rows = new List<ReferenceRow>(references.Count);

            foreach (ReferenceDescription reference in references)
            {
                string referenceType = null;

                // look up the name for the reference
                if (await session.NodeCache.FindAsync(reference.ReferenceTypeId, ct).ConfigureAwait(false)
                    is IReferenceType referenceTypeNode)
                {
                    referenceType = referenceTypeNode.DisplayName.Text;

                    if (!reference.IsForward && !referenceTypeNode.InverseName.IsNullOrEmpty)
                    {
                        referenceType = referenceTypeNode.InverseName.Text;
                    }
                }

                // the node cache is used to store the type model so it can be accessed locally.
                string typeDefinition = await session.NodeCache
                    .GetDisplayTextAsync(reference.TypeDefinition, ct)
                    .ConfigureAwait(false);

                // the ToString() operator on the ReferenceDescription returns the target name.
                rows.Add(new ReferenceRow(
                    referenceType,
                    reference.ToString(),
                    reference.NodeClass,
                    typeDefinition,
                    reference));
            }

            return rows;
        }

        /// <summary>
        /// Fetches the references for the node.
        /// </summary>
        /// <remarks>
        /// The server is asked for two references at a time on purpose: this is the one
        /// sample which shows a client following continuation points.
        /// </remarks>
        private static async Task<List<ReferenceDescription>> BrowseAsync(ISession session, NodeId nodeId, CancellationToken ct)
        {
            var references = new List<ReferenceDescription>();

            // specify the references to follow and the fields to return.
            var nodeToBrowse = new BrowseDescription {
                NodeId = nodeId,
                ReferenceTypeId = ReferenceTypeIds.References,
                IncludeSubtypes = true,
                BrowseDirection = BrowseDirection.Both,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All,
            };

            var nodesToBrowse = new List<BrowseDescription> { nodeToBrowse };

            // start the browse operation.
            BrowseResponse response = await session.BrowseAsync(
                null,
                null,
                2,
                nodesToBrowse,
                ct).ConfigureAwait(false);

            ResponseHeader responseHeader = response.ResponseHeader;
            ArrayOf<BrowseResult> results = response.Results;
            ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

            // these do sanity checks on the result - make sure response matched the request.
            ClientBase.ValidateResponse<BrowseDescription, BrowseResult>((IReadOnlyList<BrowseResult>)results.ToArray(), (IReadOnlyList<BrowseDescription>)nodesToBrowse.ToArray());
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos.ToArray(), nodesToBrowse);

            // check status.
            if (StatusCode.IsBad(results[0].StatusCode))
            {
                // embed the diagnostic information in a exception.
                throw ServiceResultException.Create(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable);
            }

            // add first batch.
            references.AddRange(results[0].References);

            // check if server limited the results.
            while (!results[0].ContinuationPoint.IsNull && results[0].ContinuationPoint.Length > 0)
            {
                var continuationPoints = new List<ByteString> { results[0].ContinuationPoint };

                // continue browse operation.
                BrowseNextResponse response2 = await session.BrowseNextAsync(
                    null,
                    false,
                    continuationPoints,
                    ct).ConfigureAwait(false);

                responseHeader = response2.ResponseHeader;
                results = response2.Results;
                diagnosticInfos = response2.DiagnosticInfos;

                ClientBase.ValidateResponse<ByteString, BrowseResult>((IReadOnlyList<BrowseResult>)results.ToArray(), continuationPoints);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos.ToArray(), continuationPoints);

                // check status.
                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    // embed the diagnostic information in a exception.
                    throw ServiceResultException.Create(results[0].StatusCode, 0, diagnosticInfos, responseHeader.StringTable);
                }

                // add next batch.
                references.AddRange(results[0].References);
            }

            return references;
        }
    }
}
