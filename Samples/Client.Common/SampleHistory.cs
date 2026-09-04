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
using Opc.Ua.Client;using Opc.Ua.Client.Historian;

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// One property of a node which keeps a history of its own.
    /// </summary>
    /// <param name="DisplayText">What to show for the property.</param>
    /// <param name="NodeId">The property.</param>
    /// <param name="BrowseName">The browse name, which is what tells Annotations apart.</param>
    /// <param name="AccessLevel">The access level the server reported for it.</param>
    public sealed record HistoricalProperty(
        string DisplayText,
        NodeId NodeId,
        QualifiedName BrowseName,
        byte AccessLevel)
    {
        /// <inheritdoc/>
        public override string ToString() => DisplayText;
    }

    /// <summary>
    /// The history reads and updates a client makes which its historian client has no
    /// method for: what a node offers, how it is configured, and where its archive ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most of a history client is the <c>HistoryClient</c> of the stack -
    /// <c>session.Historian()</c> - which reads raw, modified, processed and at-time and
    /// writes the updates. What is left over is the part a window used to carry: finding
    /// the properties of a node which have a history of their own, reading the
    /// HistoricalDataConfiguration object beside a variable, asking whether a node offers
    /// its history at all, and probing the two edges of the archive so that a date picker
    /// can start somewhere sensible.
    /// </para>
    /// <para>
    /// None of it needs a window, and all of it is what a headless client would have to
    /// write again.
    /// </para>
    /// </remarks>
    public static class SampleHistory
    {
        /// <summary>
        /// The properties of a node which keep a history of their own.
        /// </summary>
        /// <remarks>
        /// The first entry is always the node itself, under the name <c>(none)</c>: the
        /// history of the variable is what a caller reads when it picks no property.
        /// </remarks>
        /// <param name="session">The session to read on.</param>
        /// <param name="nodeId">The node whose properties to look at.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<List<HistoricalProperty>> FindPropertiesWithHistoryAsync(
            ISession session,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);

            var nodeToBrowse = new BrowseDescription {
                NodeId = nodeId,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                IncludeSubtypes = true,
                BrowseDirection = BrowseDirection.Forward,
                NodeClassMask = 0,
                ResultMask = (uint)(BrowseResultMask.DisplayName | BrowseResultMask.BrowseName),
            };

            List<ReferenceDescription> references = await SampleSession
                .BrowseAsync(session, nodeToBrowse, false, ct)
                .ConfigureAwait(false);

            var nodesToRead = new List<ReadValueId>();

            foreach (ReferenceDescription reference in references)
            {
                if (reference.NodeId.IsAbsolute)
                {
                    continue;
                }

                nodesToRead.Add(new ReadValueId {
                    NodeId = (NodeId)reference.NodeId,
                    AttributeId = Attributes.AccessLevel,
                    Handle = reference,
                });
            }

            var properties = new List<HistoricalProperty> {
                new("(none)", nodeId, QualifiedName.Null, AccessLevels.HistoryReadOrWrite),
            };

            if (nodesToRead.Count == 0)
            {
                return properties;
            }

            ReadResponse response = await session
                .ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, ct)
                .ConfigureAwait(false);

            List<DataValue> values = response.Results.ToList();
            List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

            ClientBase.ValidateResponse(values, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                byte accessLevel = values[ii].GetValue<byte>(0);

                if ((accessLevel & AccessLevels.HistoryRead) == 0)
                {
                    continue;
                }

                var reference = (ReferenceDescription)nodesToRead[ii].Handle;

                properties.Add(new HistoricalProperty(
                    reference.ToString(),
                    (NodeId)reference.NodeId,
                    reference.BrowseName,
                    accessLevel));
            }

            return properties;
        }

        /// <summary>
        /// Reads the HistoricalDataConfiguration object beside a variable.
        /// </summary>
        /// <remarks>
        /// The configuration is an object with a set of properties, reachable through the
        /// HasHistoricalConfiguration reference of the variable. A default instance is built
        /// first so that its children name the browse paths to translate; the values which
        /// come back are written into that instance, and a property the server does not have
        /// keeps <c>BadNotSupported</c>.
        /// </remarks>
        /// <param name="session">The session to read on.</param>
        /// <param name="nodeId">The variable whose configuration to read.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<HistoricalDataConfigurationState> ReadConfigurationAsync(
            ISession session,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);

            // load the defaults for the historical configuration object.
            var configuration = new HistoricalDataConfigurationState(null) {
            };

            configuration.Definition = PropertyState<string>.With<VariantBuilder>(configuration);
            configuration.MaxTimeInterval = PropertyState<double>.With<VariantBuilder>(configuration);
            configuration.MinTimeInterval = PropertyState<double>.With<VariantBuilder>(configuration);
            configuration.ExceptionDeviation = PropertyState<double>.With<VariantBuilder>(configuration);
            configuration.ExceptionDeviationFormat = PropertyState<ExceptionDeviationFormat>.With<EnumerationBuilder<ExceptionDeviationFormat>>(configuration);
            configuration.StartOfArchive = PropertyState<DateTimeUtc>.With<VariantBuilder>(configuration);
            configuration.StartOfOnlineArchive = PropertyState<DateTimeUtc>.With<VariantBuilder>(configuration);

            configuration.Create(
                session.SystemContext,
                NodeId.Null,
                new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration),
                LocalizedText.Null,
                false);

            // get the browse paths to query.
            var relativePath = new RelativePath {
                Elements = new List<RelativePathElement> {
                    new() {
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasHistoricalConfiguration,
                        IsInverse = false,
                        IncludeSubtypes = false,
                        TargetName = new QualifiedName(Opc.Ua.BrowseNames.HAConfiguration),
                    },
                },
            };

            var pathsToTranslate = new List<BrowsePath>();

            CollectBrowsePaths(session.SystemContext, nodeId, configuration, relativePath, pathsToTranslate);

            // translate browse paths.
            TranslateBrowsePathsToNodeIdsResponse response = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, pathsToTranslate, ct)
                .ConfigureAwait(false);

            List<BrowsePathResult> results = response.Results.ToList();
            List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

            ClientBase.ValidateResponse(results, pathsToTranslate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, pathsToTranslate);

            // build list of values to read.
            var valuesToRead = new List<ReadValueId>();

            for (int ii = 0; ii < pathsToTranslate.Count; ii++)
            {
                var variable = (BaseVariableState)pathsToTranslate[ii].Handle;

                variable.Value = null;
                variable.StatusCode = StatusCodes.BadNotSupported;

                if (StatusCode.IsBad(results[ii].StatusCode) || results[ii].Targets.Count == 0)
                {
                    continue;
                }

                if (results[ii].Targets[0].RemainingPathIndex == UInt32.MaxValue &&
                    !results[ii].Targets[0].TargetId.IsAbsolute)
                {
                    variable.NodeId = (NodeId)results[ii].Targets[0].TargetId;

                    valuesToRead.Add(new ReadValueId {
                        NodeId = variable.NodeId,
                        AttributeId = Attributes.Value,
                        Handle = variable,
                    });
                }
            }

            // read the values.
            if (valuesToRead.Count > 0)
            {
                ReadResponse valueResponse = await session
                    .ReadAsync(null, 0, TimestampsToReturn.Neither, valuesToRead, ct)
                    .ConfigureAwait(false);

                List<DataValue> values = valueResponse.Results.ToList();
                diagnosticInfos = valueResponse.DiagnosticInfos.ToList();

                ClientBase.ValidateResponse(values, valuesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

                for (int ii = 0; ii < valuesToRead.Count; ii++)
                {
                    var variable = (BaseVariableState)valuesToRead[ii].Handle;

                    variable.WrappedValue = values[ii].WrappedValue;
                    variable.StatusCode = values[ii].StatusCode;
                }
            }

            return configuration;
        }

        /// <summary>
        /// Whether a node offers its history.
        /// </summary>
        /// <remarks>
        /// A variable says so in the history bit of its access level. Anything which is not
        /// a variable - a folder, an object, a method - has no access level and no history,
        /// and a caller offers a live subscription for it instead.
        /// </remarks>
        /// <param name="session">The session to read on.</param>
        /// <param name="nodeId">The node to ask about.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<bool> IsHistoryReadableAsync(
            ISession session,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);

            var nodesToRead = new List<ReadValueId> {
                new() { NodeId = nodeId, AttributeId = Attributes.AccessLevel },
            };

            ReadResponse response = await session
                .ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, ct)
                .ConfigureAwait(false);

            DataValue accessLevel = response.Results.ToList()[0];

            return StatusCode.IsGood(accessLevel.StatusCode) &&
                accessLevel.WrappedValue.TryGetValue(out byte flags) &&
                (flags & AccessLevels.HistoryRead) != 0;
        }

        /// <summary>
        /// The time of the first sample in the archive, as a local time truncated to the
        /// second, or <see cref="DateTime.MinValue"/> when the archive is empty.
        /// </summary>
        /// <remarks>
        /// The HistoricalDataConfiguration answers this without a read when the server
        /// publishes it. Otherwise the archive is probed, which some servers take a long
        /// time over.
        /// </remarks>
        /// <param name="session">The session to read on.</param>
        /// <param name="nodeId">The variable whose archive to look at.</param>
        /// <param name="configuration">The configuration of the variable, or null.</param>
        /// <param name="ct">The cancellation token.</param>
        public static Task<DateTime> ReadFirstDateAsync(
            ISession session,
            NodeId nodeId,
            HistoricalDataConfigurationState configuration,
            CancellationToken ct = default)
        {
            if (configuration != null)
            {
                if (StatusCode.IsGood(configuration.StartOfOnlineArchive.StatusCode))
                {
                    return Task.FromResult(configuration.StartOfOnlineArchive.Value.ToLocalTime());
                }

                if (StatusCode.IsGood(configuration.StartOfArchive.StatusCode))
                {
                    return Task.FromResult(configuration.StartOfArchive.Value.ToLocalTime());
                }
            }

            // do it the hard way (may take a long time with some servers).
            return ReadEdgeOfArchiveAsync(
                session,
                nodeId,
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DateTime.MinValue,
                ct);
        }

        /// <summary>
        /// One second past the last sample in the archive, so that a read of the whole
        /// archive has it inside its window rather than on the edge of it. Local time,
        /// truncated to the second, or <see cref="DateTime.MinValue"/> when empty.
        /// </summary>
        /// <param name="session">The session to read on.</param>
        /// <param name="nodeId">The variable whose archive to look at.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<DateTime> ReadLastDateAsync(
            ISession session,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            DateTime endTime = await ReadEdgeOfArchiveAsync(
                session,
                nodeId,
                DateTime.MinValue,
                DateTime.UtcNow.AddDays(1),
                ct).ConfigureAwait(false);

            return endTime != DateTime.MinValue ? endTime.AddSeconds(1) : endTime;
        }

        /// <summary>
        /// Gives back a continuation point the caller is not going to read to the end.
        /// </summary>
        /// <remarks>
        /// A server holds resources for an open point until it is released or the session
        /// ends. Failing to release it is not worth reporting: the point is gone either way
        /// once the session is done with it.
        /// </remarks>
        /// <param name="session">The session the read was made on.</param>
        /// <param name="nodeId">The node which was read.</param>
        /// <param name="details">The details of the read.</param>
        /// <param name="continuationPoint">The point to release.</param>
        public static async Task ReleaseContinuationPointAsync(
            ISession session,
            NodeId nodeId,
            ExtensionObject details,
            ByteString continuationPoint)
        {
            ArgumentNullException.ThrowIfNull(session);

            try
            {
                await session.HistoryReadAsync(
                    null,
                    details,
                    TimestampsToReturn.Neither,
                    true,
                    new List<HistoryReadValueId> {
                        new() { NodeId = nodeId, ContinuationPoint = continuationPoint },
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // the point is gone either way once the session is done with it.
            }
        }

        /// <summary>
        /// Removes a set of values from the history of a node.
        /// </summary>
        /// <remarks>
        /// The historian of the stack offers the two deletes of Part 11 - by time range and
        /// at given times - but not the Remove flavour of HistoryUpdate, so this makes the
        /// service call itself.
        /// </remarks>
        /// <param name="session">The session to update on.</param>
        /// <param name="nodeId">The node to update.</param>
        /// <param name="values">The values to remove.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ServiceResultException">The server rejected the update as a whole.</exception>
        public static async Task<IList<StatusCode>> RemoveAsync(
            ISession session,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);

            var details = new UpdateDataDetails {
                NodeId = nodeId,
                PerformInsertReplace = PerformUpdateType.Remove,
                UpdateValues = new List<DataValue>(values),
            };

            var nodesToUpdate = new List<ExtensionObject> { new ExtensionObject(details) };

            HistoryUpdateResponse response = await session
                .HistoryUpdateAsync(null, nodesToUpdate, ct)
                .ConfigureAwait(false);

            List<HistoryUpdateResult> results = response.Results.ToList();
            List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

            ClientBase.ValidateResponse(results, nodesToUpdate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToUpdate);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            return results[0].OperationResults.ToList();
        }

        /// <summary>
        /// Reads the one sample at an edge of the archive, as a local time truncated to the
        /// second so that it can be shown in a date picker.
        /// </summary>
        /// <remarks>
        /// One end of the window is left unset. A read without an end time runs forwards
        /// from its start and a read without a start time runs backwards from its end, so
        /// asking for a single value either way answers with the sample at that end of the
        /// archive. Leaving the enumeration after that one value is what releases the
        /// continuation point the server opened for the rest.
        /// </remarks>
        private static async Task<DateTime> ReadEdgeOfArchiveAsync(
            ISession session,
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(session);

            await foreach (DataValue value in session.Historian().ReadRawAsync(
                nodeId,
                startTime,
                endTime,
                maxValuesPerNode: 1,
                returnBounds: false,
                TimestampsToReturn.Source,
                ct).ConfigureAwait(false))
            {
                DateTime timestamp = (DateTime)value.SourceTimestamp;

                return new DateTime(
                    timestamp.Year,
                    timestamp.Month,
                    timestamp.Day,
                    timestamp.Hour,
                    timestamp.Minute,
                    timestamp.Second,
                    0,
                    DateTimeKind.Utc).ToLocalTime();
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// Collects a browse path for every variable below a node state, so that the
        /// children of a locally built object can be found on the server.
        /// </summary>
        private static void CollectBrowsePaths(
            ISystemContext context,
            NodeId rootId,
            NodeState parent,
            RelativePath parentPath,
            IList<BrowsePath> browsePaths)
        {
            var children = new List<BaseInstanceState>();

            parent.GetChildren(context, children);

            foreach (BaseInstanceState child in children)
            {
                var browsePath = new BrowsePath {
                    StartingNode = rootId,
                    Handle = child,
                };

                var elements = new List<RelativePathElement>();

                if (parentPath != null)
                {
                    elements.AddRange(parentPath.Elements);
                }

                elements.Add(new RelativePathElement {
                    ReferenceTypeId = child.ReferenceTypeId,
                    IsInverse = false,
                    IncludeSubtypes = false,
                    TargetName = child.BrowseName,
                });

                browsePath.RelativePath.Elements = elements;

                if (child.NodeClass == NodeClass.Variable)
                {
                    browsePaths.Add(browsePath);
                }

                CollectBrowsePaths(context, rootId, child, browsePath.RelativePath, browsePaths);
            }
        }
    }
}
