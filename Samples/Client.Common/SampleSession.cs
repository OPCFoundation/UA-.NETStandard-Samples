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
using Microsoft.Extensions.Options;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// The OPC UA helpers the sample client models share: browsing, browse path
    /// translation, event decoding, session teardown and the V2 subscription engine.
    /// </summary>
    /// <remarks>
    /// These used to live in <c>Opc.Ua.Client.Controls.ClientUtils</c>, next to the
    /// dialogs. They have no user interface in them, which is why the client models can
    /// use them from a class library which never references Windows Forms; the control
    /// library keeps forwarding wrappers under the old names.
    /// </remarks>
    public static class SampleSession
    {
        #region Browse
        /// <summary>
        /// Browses the address space and returns the references found.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodesToBrowse">The set of browse operations to perform.</param>
        /// <param name="throwOnError">if set to <c>true</c> a exception will be thrown on an error.</param>
        /// <param name="ct">A cancellation token to use to cancel the operation.</param>
        /// <returns>
        /// The references found. Null if an error occurred.
        /// </returns>
        public static Task<List<ReferenceDescription>> BrowseAsync(ISession session, IReadOnlyList<BrowseDescription> nodesToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            return BrowseAsync(session, null, nodesToBrowse, throwOnError, ct);
        }

        /// <summary>
        /// Browses the address space and returns the references found.
        /// </summary>
        public static async Task<List<ReferenceDescription>> BrowseAsync(ISession session, ViewDescription view, IReadOnlyList<BrowseDescription> nodesToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            try
            {
                List<ReferenceDescription> references = new List<ReferenceDescription>();

                while (nodesToBrowse.Count > 0)
                {
                    // start the browse operation.
                    BrowseResponse response = await session.BrowseAsync(
                        null,
                        view,
                        0,
                        nodesToBrowse.ToArrayOf(),
                        ct).ConfigureAwait(false);

                    var results = response.Results.ToList();
                    var diagnosticInfos = response.DiagnosticInfos.ToList();

                    ClientBase.ValidateResponse(results, nodesToBrowse);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                    List<ByteString> continuationPoints = new List<ByteString>();
                    List<BrowseDescription> unprocessedOperations = new List<BrowseDescription>();

                    for (int ii = 0; ii < nodesToBrowse.Count; ii++)
                    {
                        // check for error.
                        if (StatusCode.IsBad(results[ii].StatusCode))
                        {
                            // this error indicates that the server does not have enough simultaneously active
                            // continuation points. This request will need to be resent after the other operations
                            // have been completed and their continuation points released.
                            if (results[ii].StatusCode == StatusCodes.BadNoContinuationPoints)
                            {
                                unprocessedOperations.Add(nodesToBrowse[ii]);
                            }

                            continue;
                        }

                        // check if all references have been fetched.
                        if (results[ii].References.Count == 0)
                        {
                            continue;
                        }

                        // save results.
                        references.AddRange(results[ii].References);

                        // check for continuation point.
                        if (!results[ii].ContinuationPoint.IsNull)
                        {
                            continuationPoints.Add(results[ii].ContinuationPoint);
                        }
                    }

                    // process continuation points.
                    while (continuationPoints.Count > 0)
                    {
                        // continue browse operation.
                        BrowseNextResponse response2 = await session.BrowseNextAsync(
                            null,
                            false,
                            continuationPoints,
                            ct).ConfigureAwait(false);

                        results = response2.Results.ToList();
                        diagnosticInfos = response2.DiagnosticInfos.ToList();

                        ClientBase.ValidateResponse(results, continuationPoints);
                        ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

                        List<ByteString> revisedContinuationPoints = new List<ByteString>();
                        for (int ii = 0; ii < continuationPoints.Count; ii++)
                        {
                            // check for error.
                            if (StatusCode.IsBad(results[ii].StatusCode))
                            {
                                continue;
                            }

                            // check if all references have been fetched.
                            if (results[ii].References.Count == 0)
                            {
                                continue;
                            }

                            // save results.
                            references.AddRange(results[ii].References);

                            // check for continuation point.
                            if (!results[ii].ContinuationPoint.IsNull)
                            {
                                revisedContinuationPoints.Add(results[ii].ContinuationPoint);
                            }
                        }

                        // check if browsing must continue;
                        continuationPoints = revisedContinuationPoints;
                    }

                    // check if unprocessed results exist.
                    nodesToBrowse = unprocessedOperations;
                }

                // return complete list.
                return references;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }

        /// <summary>
        /// Browses the address space and returns the references found.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodeToBrowse">The NodeId for the starting node.</param>
        /// <param name="throwOnError">if set to <c>true</c> a exception will be thrown on an error.</param>
        /// <param name="ct">The cancellation token to cancel the operation</param>
        /// <returns>
        /// The references found. Null if an error occurred.
        /// </returns>
        public static Task<List<ReferenceDescription>> BrowseAsync(ISession session, BrowseDescription nodeToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            return BrowseAsync(session, null, nodeToBrowse, throwOnError, ct);
        }

        /// <summary>
        /// Browses the address space and returns the references found.
        /// </summary>
        public static Task<List<ReferenceDescription>> BrowseAsync(ISession session, ViewDescription view, BrowseDescription nodeToBrowse, bool throwOnError, CancellationToken ct = default)
        {
            // construct browse request.
            List<BrowseDescription> nodesToBrowse = new List<BrowseDescription> {
                nodeToBrowse
            };

            return BrowseAsync(session, view, nodesToBrowse, throwOnError, ct);
        }

        /// <summary>
        /// Browses the address space and returns all of the supertypes of the specified type node.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="typeId">The NodeId for a type node in the address space.</param>
        /// <param name="throwOnError">if set to <c>true</c> a exception will be thrown on an error.</param>
        /// <param name="ct">A cancellation token to use to cancel the operation.</param>
        /// <returns>
        /// The references found. Null if an error occurred.
        /// </returns>
        public static async Task<List<ReferenceDescription>> BrowseSuperTypesAsync(ISession session, NodeId typeId, bool throwOnError, CancellationToken ct = default)
        {
            List<ReferenceDescription> supertypes = new List<ReferenceDescription>();

            try
            {
                // find all of the children of the field.
                BrowseDescription nodeToBrowse = new BrowseDescription();

                nodeToBrowse.NodeId = typeId;
                nodeToBrowse.BrowseDirection = BrowseDirection.Inverse;
                nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasSubtype;
                nodeToBrowse.IncludeSubtypes = false; // more efficient to use IncludeSubtypes=False when possible.
                nodeToBrowse.NodeClassMask = 0; // the HasSubtype reference already restricts the targets to Types.
                nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

                List<ReferenceDescription> references = await BrowseAsync(session, nodeToBrowse, throwOnError, ct).ConfigureAwait(false);

                while (references != null && references.Count > 0)
                {
                    // should never be more than one supertype.
                    supertypes.Add(references[0]);

                    // only follow references within this server.
                    if (references[0].NodeId.IsAbsolute)
                    {
                        break;
                    }

                    // get the references for the next level up.
                    nodeToBrowse.NodeId = (NodeId)references[0].NodeId;
                    references = await BrowseAsync(session, nodeToBrowse, throwOnError, ct).ConfigureAwait(false);
                }

                // return complete list.
                return supertypes;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }

        /// <summary>
        /// Returns the node ids for a set of relative paths.
        /// </summary>
        /// <param name="session">An open session with the server to use.</param>
        /// <param name="startNodeId">The starting node for the relative paths.</param>
        /// <param name="namespacesUris">The namespace URIs referenced by the relative paths.</param>
        /// <param name="ct">A cancellation token to use to cancel the operation.</param>
        /// <param name="relativePaths">The relative paths.</param>
        /// <returns>A collection of local nodes.</returns>
        public static async Task<List<NodeId>> TranslateBrowsePathsAsync(
            ISession session,
            NodeId startNodeId,
            NamespaceTable namespacesUris,
            CancellationToken ct,
            params string[] relativePaths)
        {
            // build the list of browse paths to follow by parsing the relative paths.
            List<BrowsePath> browsePaths = new List<BrowsePath>();

            if (relativePaths != null)
            {
                for (int ii = 0; ii < relativePaths.Length; ii++)
                {
                    BrowsePath browsePath = new BrowsePath();

                    // The relative paths used indexes in the namespacesUris table. These must be
                    // converted to indexes used by the server. An error occurs if the relative path
                    // refers to a namespaceUri that the server does not recognize.

                    // The relative paths may refer to ReferenceType by their BrowseName. The TypeTree object
                    // allows the parser to look up the server's NodeId for the ReferenceType.

                    browsePath.RelativePath = RelativePath.Parse(
                        relativePaths[ii],
                        session.TypeTree,
                        namespacesUris,
                        session.NamespaceUris);

                    browsePath.StartingNode = startNodeId;

                    browsePaths.Add(browsePath);
                }
            }

            // make the call to the server.
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                browsePaths,
                ct).ConfigureAwait(false);

            ResponseHeader responseHeader = response.ResponseHeader;
            var results = response.Results.ToList();
            var diagnosticInfos = response.DiagnosticInfos.ToList();

            // ensure that the server returned valid results.
            Session.ValidateResponse(results, browsePaths);
            Session.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);

            // collect the list of node ids found.
            List<NodeId> nodes = new List<NodeId>();

            for (int ii = 0; ii < results.Count; ii++)
            {
                // check if the start node actually exists.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    nodes.Add(NodeId.Null);
                    continue;
                }

                // an empty list is returned if no node was found.
                if (results[ii].Targets.Count == 0)
                {
                    nodes.Add(NodeId.Null);
                    continue;
                }

                // Multiple matches are possible, however, the node that matches the type model is the
                // one we are interested in here. The rest can be ignored.
                BrowsePathTarget target = results[ii].Targets[0];

                if (target.RemainingPathIndex != UInt32.MaxValue)
                {
                    nodes.Add(NodeId.Null);
                    continue;
                }

                // The targetId is an ExpandedNodeId because it could be node in another server.
                // The ToNodeId function is used to convert a local NodeId stored in a ExpandedNodeId to a NodeId.
                nodes.Add(ExpandedNodeId.ToNodeId(target.TargetId, session.NamespaceUris));
            }

            // return whatever was found.
            return nodes;
        }
        #endregion

        #region Events
        /// <summary>
        /// Finds the type of the event for the notification.
        /// </summary>
        /// <param name="filter">The filter the notification was produced with.</param>
        /// <param name="notification">The notification.</param>
        /// <returns>The NodeId of the EventType.</returns>
        /// <remarks>
        /// The V2 subscription engine reports revised values but not the filter a monitored
        /// item was created with, so the caller which owns the filter passes it in. The fields
        /// of a notification line up one to one with its select clauses.
        /// </remarks>
        public static NodeId FindEventType(EventFilter filter, EventFieldList notification)
        {
            if (filter != null)
            {
                for (int ii = 0; ii < filter.SelectClauses.Count; ii++)
                {
                    SimpleAttributeOperand clause = filter.SelectClauses[ii];

                    if (clause.BrowsePath.Count == 1 && clause.BrowsePath[0] == BrowseNames.EventType)
                    {
                        return notification.EventFields[ii].TryGetValue(out NodeId nodeId) ? nodeId : NodeId.Null;
                    }
                }
            }

            return NodeId.Null;
        }

        /// <summary>
        /// Constructs an event object from a notification.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="filter">The filter the notification was produced with.</param>
        /// <param name="notification">The notification.</param>
        /// <param name="knownEventTypes">The known event types.</param>
        /// <param name="eventTypeMappings">Mapping between event types and known event types.</param>
        /// <param name="ct">Cancellation token to use to cancel operation</param>
        /// <returns>
        /// The event object. Null if the notification is not a valid event type.
        /// </returns>
        public static async Task<BaseEventState> ConstructEventAsync(
            ISession session,
            EventFilter filter,
            EventFieldList notification,
            Dictionary<NodeId, Type> knownEventTypes,
            Dictionary<NodeId, NodeId> eventTypeMappings,
            CancellationToken ct = default)
        {
            // find the event type.
            NodeId eventTypeId = FindEventType(filter, notification);

            if (eventTypeId.IsNull)
            {
                return null;
            }

            // look up the known event type.
            Type knownType = null;
            NodeId knownTypeId = NodeId.Null;

            if (eventTypeMappings.TryGetValue(eventTypeId, out knownTypeId))
            {
                knownType = knownEventTypes[knownTypeId];
            }

            // try again.
            if (knownType == null)
            {
                if (knownEventTypes.TryGetValue(eventTypeId, out knownType))
                {
                    knownTypeId = eventTypeId;
                    eventTypeMappings.TryAdd(eventTypeId, eventTypeId);
                }
            }

            // try mapping it to a known type.
            if (knownType == null)
            {
                // browse for the supertypes of the event type.
                List<ReferenceDescription> supertypes = await BrowseSuperTypesAsync(session, eventTypeId, false, ct).ConfigureAwait(false);

                // can't do anything with unknown types.
                if (supertypes == null)
                {
                    return null;
                }

                // find the first supertype that matches a known event type.
                for (int ii = 0; ii < supertypes.Count; ii++)
                {
                    NodeId superTypeId = (NodeId)supertypes[ii].NodeId;

                    if (knownEventTypes.TryGetValue(superTypeId, out knownType))
                    {
                        knownTypeId = superTypeId;
                        eventTypeMappings.TryAdd(eventTypeId, superTypeId);
                    }

                    if (!knownTypeId.IsNull)
                    {
                        break;
                    }
                }

                // can't do anything with unknown types.
                if (knownTypeId.IsNull)
                {
                    return null;
                }
            }

            // construct the event based on the known event type.
            BaseEventState e = (BaseEventState)Activator.CreateInstance(knownType, new object[] { (NodeState)null });

            // initialize the event with the values in the notification.
            e.Update(session.SystemContext, filter.SelectClauses, notification);

            // save the orginal notification.
            e.Handle = notification;

            return e;
        }
        #endregion

        #region Sessions
        /// <summary>
        /// How long a sample waits for a session to close before it stops waiting and tears
        /// the session down instead.
        /// </summary>
        public static readonly TimeSpan DefaultCloseTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Closes a session and disposes it, without letting a server which is no longer
        /// reachable hold the sample up.
        /// </summary>
        /// <remarks>
        /// A managed session cannot close while its connection state machine is inside a
        /// reconnect attempt: requesting the close only moves the state machine to Closing,
        /// and the attempt in flight runs to its own end first. Against a host which accepts
        /// the connection but never answers - a server which was stopped while its machine
        /// stayed up, a firewall which swallows the traffic - that end is the OperationTimeout
        /// of the endpoint, which the sample configurations set to ten minutes. Closing the
        /// window of a sample would then block for those ten minutes.
        ///
        /// So the wait is bounded and, when it expires, the session is disposed instead:
        /// disposing cancels the state machine, which does cancel the attempt in flight. The
        /// session is disposed either way, which is also what stops its background workers.
        /// </remarks>
        /// <param name="session">The session to close. A null session is ignored.</param>
        /// <param name="ct">The cancellation token.</param>
        public static Task CloseAndDisposeAsync(ISession session, CancellationToken ct = default)
        {
            return CloseAndDisposeAsync(session, DefaultCloseTimeout, ct);
        }

        /// <summary>
        /// Closes a session and disposes it, waiting no longer than the given timeout.
        /// </summary>
        /// <param name="session">The session to close. A null session is ignored.</param>
        /// <param name="timeout">How long to wait for the close before disposing instead.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task CloseAndDisposeAsync(
            ISession session,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            if (session == null)
            {
                return;
            }

            try
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
                bounded.CancelAfter(timeout);

                await session.CloseAsync((int)timeout.TotalMilliseconds, bounded.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // the server did not answer in time, or the caller gave up: the dispose below
                // is what actually tears the session down
            }
            catch (ServiceResultException)
            {
                // closing a session on a server which is already gone is not an error a
                // sample has anything to do about
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Runs an asynchronous teardown from a synchronous callback and waits for it.
        /// </summary>
        /// <remarks>
        /// FormClosing and Dispose cannot await, so a sample which releases something
        /// asynchronously on its way out has to wait for it. Awaiting it on the UI thread
        /// would deadlock: the continuation is posted back to the message loop which the
        /// wait is blocking, and neither side moves again. Running the teardown on a thread
        /// pool thread, where there is no synchronization context to post back to, is what
        /// lets the wait complete.
        ///
        /// The teardown therefore runs off the UI thread and must not touch any control.
        /// </remarks>
        /// <param name="teardown">The teardown to run.</param>
        public static void WaitForTeardown(Func<Task> teardown)
        {
            if (teardown == null)
            {
                throw new ArgumentNullException(nameof(teardown));
            }

            Task.Run(teardown).GetAwaiter().GetResult();
        }
        #endregion

        #region Subscriptions
        /// <summary>
        /// Adds a subscription driven by the V2 subscription engine to the session.
        /// </summary>
        /// <param name="session">The session, which has to run the V2 subscription engine.</param>
        /// <param name="handler">The handler which receives the notifications.</param>
        /// <param name="options">The options of the subscription. The caller keeps the monitor
        /// so it can reconfigure the subscription later on.</param>
        public static ISubscription AddSubscription(
            ISession session,
            ISubscriptionNotificationHandler handler,
            IOptionsMonitor<Opc.Ua.Client.Subscriptions.SubscriptionOptions> options)
        {
            ArgumentNullException.ThrowIfNull(session);

            if (!session.TryGetSubscriptionManager(out ISubscriptionManager manager))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The session does not use the V2 subscription engine. Create it with a " +
                    "ManagedSessionFactory or a session factory configured with the " +
                    "DefaultSubscriptionEngineFactory.");
            }

            return manager.Add(handler, options);
        }

        /// <summary>
        /// The options a control uses for a subscription it creates itself.
        /// </summary>
        public static Opc.Ua.Client.Subscriptions.SubscriptionOptions DefaultSubscriptionOptions => new Opc.Ua.Client.Subscriptions.SubscriptionOptions {
            PublishingInterval = TimeSpan.FromSeconds(1),
            KeepAliveCount = 10,
            LifetimeCount = 100,
            MaxNotificationsPerPublish = 1000,
            PublishingEnabled = true,
        };

        /// <summary>
        /// Waits until the V2 engine has applied the pending monitored item changes.
        /// </summary>
        /// <remarks>
        /// The V2 engine applies added, modified and removed items on its own worker instead of
        /// on an explicit ApplyChanges call, so a wizard which wants to show the operation
        /// results of a step has to wait for that worker to catch up first.
        /// </remarks>
        /// <param name="subscription">The subscription to wait for.</param>
        /// <param name="timeout">How long to wait before giving up and showing what there is.</param>
        /// <param name="ct">Cancellation token to use to cancel operation.</param>
        public static async Task WaitForPendingChangesAsync(
            ISubscription subscription,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(subscription);

            DateTime deadline = DateTime.UtcNow.Add(timeout);

            while (HasPendingChanges(subscription))
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return;
                }

                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Returns true while the V2 engine still has monitored item changes to apply.
        /// </summary>
        private static bool HasPendingChanges(ISubscription subscription)
        {
            foreach (IMonitoredItem monitoredItem in subscription.MonitoredItems.Items)
            {
                if (monitoredItem is IMonitoredItemApplyState applyState && applyState.HasPendingChanges)
                {
                    return true;
                }
            }

            return false;
        }
        #endregion
    }
}
