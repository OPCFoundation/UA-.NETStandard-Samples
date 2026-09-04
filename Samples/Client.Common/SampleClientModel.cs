/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// What changed about the connection of a client model.
    /// </summary>
    public enum ConnectionChange
    {
        /// <summary>A session was attached and the model has finished setting itself up on it.</summary>
        Attached,

        /// <summary>The session was detached; the model holds nothing of it any more.</summary>
        Detached,

        /// <summary>The session lost its connection and is trying to get it back.</summary>
        ReconnectStarting,

        /// <summary>The session has its connection back.</summary>
        ReconnectCompleted,
    }

    /// <summary>
    /// The payload of <see cref="SampleClientModel.ConnectionChanged"/>.
    /// </summary>
    public sealed class ConnectionChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public ConnectionChangedEventArgs(ConnectionChange change)
        {
            Change = change;
        }

        /// <summary>
        /// What changed.
        /// </summary>
        public ConnectionChange Change { get; }
    }

    /// <summary>
    /// The payload of <see cref="SampleClientModel.Error"/>: a failure on a background
    /// path of the model, which has no caller to throw to.
    /// </summary>
    public sealed class ModelErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        public ModelErrorEventArgs(string what, Exception exception)
        {
            What = what;
            Exception = exception;
        }

        /// <summary>
        /// What the model was doing.
        /// </summary>
        public string What { get; }

        /// <summary>
        /// What went wrong.
        /// </summary>
        public Exception Exception { get; }
    }

    /// <summary>
    /// The base class of the client models of the samples: the half of a sample client
    /// which talks OPC UA and knows nothing about the window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A model does not open its own session. The window connects through the shared
    /// connect control and hands the session over with <see cref="AttachAsync"/>; the
    /// model resolves what it needs, creates its subscriptions and exposes what it found
    /// as properties, methods and events. <see cref="DetachAsync"/> undoes that - and it
    /// runs <b>before</b> the control closes the session, because closing a session which
    /// still carries a subscription waits for the publish pipeline to drain.
    /// </para>
    /// <para>
    /// <b>Threading.</b> Methods may be called from any thread and complete on any thread:
    /// the model awaits with <c>ConfigureAwait(false)</c> throughout. Events are raised on
    /// the <see cref="SynchronizationContext"/> which was current when the model was
    /// constructed, the way <see cref="Progress{T}"/> does it. A window creates its model on
    /// the user interface thread and therefore receives every event there, and updates its
    /// controls directly; a headless test creates it with no context and receives the
    /// events inline, on whichever thread raised them. Events are posted, never sent: a
    /// window may be blocking its thread on <see cref="DetachAsync"/> while it closes, and
    /// a synchronous send from the teardown would deadlock against that.
    /// </para>
    /// <para>
    /// <b>Errors.</b> A method throws, and the window reports the exception the way it
    /// always has. A failure on a background path - a pump which streams notifications,
    /// for instance - has no caller to throw to and is reported through <see cref="Error"/>
    /// instead, after being logged.
    /// </para>
    /// </remarks>
    public abstract class SampleClientModel : IAsyncDisposable, IDisposable
    {
        private readonly SynchronizationContext m_context;
        private readonly SemaphoreSlim m_lifecycle = new SemaphoreSlim(1, 1);
        private ISession m_session;
        private bool m_disposed;

        /// <summary>
        /// Creates a model, capturing the synchronization context of the calling thread
        /// for its events.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        protected SampleClientModel(ITelemetryContext telemetry)
        {
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Logger = telemetry.LoggerFactory.CreateLogger(GetType().FullName ?? GetType().Name);
            m_context = SynchronizationContext.Current;
        }

        /// <summary>
        /// The session the model is attached to, or null while it is detached.
        /// </summary>
        public ISession Session => m_session;

        /// <summary>
        /// True while a session is attached.
        /// </summary>
        public bool IsConnected => m_session != null;

        /// <summary>
        /// True between a reconnect starting and completing.
        /// </summary>
        public bool IsReconnecting { get; private set; }

        /// <summary>
        /// The telemetry context of the client.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// The logger of the model.
        /// </summary>
        protected ILogger Logger { get; }

        /// <summary>
        /// Raised after a session was attached or detached, and around a reconnect.
        /// </summary>
        public event EventHandler<ConnectionChangedEventArgs> ConnectionChanged;

        /// <summary>
        /// Raised for a failure on a background path of the model, which has no caller to
        /// throw to.
        /// </summary>
        public event EventHandler<ModelErrorEventArgs> Error;

        /// <summary>
        /// Attaches the model to a session and lets it set itself up on it.
        /// </summary>
        /// <remarks>
        /// A model which is already attached is detached first. If the setup fails the
        /// session is released again and the exception reaches the caller.
        /// </remarks>
        /// <param name="session">The session to attach to.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task AttachAsync(ISession session, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);

            await m_lifecycle.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                if (m_session != null)
                {
                    await DetachCoreAsync().ConfigureAwait(false);
                }

                m_session = session;
                IsReconnecting = false;

                try
                {
                    await OnAttachedAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    m_session = null;
                    throw;
                }
            }
            finally
            {
                m_lifecycle.Release();
            }

            Raise(ConnectionChanged, new ConnectionChangedEventArgs(ConnectionChange.Attached));
        }

        /// <summary>
        /// Releases everything the model holds of its session: subscriptions are deleted
        /// on the server, pumps are stopped, resolved nodes are forgotten.
        /// </summary>
        /// <remarks>
        /// Never throws, and may be called any number of times. The session may already be
        /// closed when this runs - the connect control disposes its session before it
        /// reports the disconnect - and a subscription which cannot be deleted on a server
        /// which is gone is not an error the window has anything to do about; it is logged.
        /// </remarks>
        public async Task DetachAsync()
        {
            bool detached;

            await m_lifecycle.WaitAsync().ConfigureAwait(false);

            try
            {
                detached = await DetachCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                m_lifecycle.Release();
            }

            if (detached)
            {
                Raise(ConnectionChanged, new ConnectionChangedEventArgs(ConnectionChange.Detached));
            }
        }

        /// <summary>
        /// Reports that the session lost its connection and is reconnecting.
        /// </summary>
        public void NotifyReconnectStarting()
        {
            if (m_session == null)
            {
                return;
            }

            IsReconnecting = true;

            try
            {
                OnReconnectStarting();
            }
            catch (Exception e)
            {
                ReportError("Handling the start of a reconnect", e);
            }

            Raise(ConnectionChanged, new ConnectionChangedEventArgs(ConnectionChange.ReconnectStarting));
        }

        /// <summary>
        /// Reports that the session has its connection back.
        /// </summary>
        /// <remarks>
        /// A managed session keeps its identity across a reconnect, and so do the
        /// subscriptions of the V2 engine, so most models have nothing to do here. A model
        /// which caches something the server may have changed while the connection was down
        /// re-reads it in <see cref="OnReconnectCompletedAsync"/>.
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        public async Task NotifyReconnectCompletedAsync(CancellationToken ct = default)
        {
            if (m_session == null)
            {
                return;
            }

            IsReconnecting = false;

            try
            {
                await OnReconnectCompletedAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                ReportError("Handling the completion of a reconnect", e);
            }

            Raise(ConnectionChanged, new ConnectionChangedEventArgs(ConnectionChange.ReconnectCompleted));
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;

            await DetachAsync().ConfigureAwait(false);
            await DisposeAsyncCore().ConfigureAwait(false);
            m_lifecycle.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Detaches the model and releases it, for a caller which cannot await.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is what a window calls from its <c>Dispose(bool)</c>. Windows Forms disposes
        /// a form from <c>FormClosed</c> and from the message loop, neither of which can
        /// await a <see cref="DisposeAsync"/>, so the model has to be able to tear itself
        /// down synchronously - otherwise every window needs a CA2213 suppression for the
        /// field which holds it.
        /// </para>
        /// <para>
        /// The detach runs on a thread pool thread, where there is no synchronization
        /// context to post the continuations back to; awaiting it on the user interface
        /// thread would deadlock against the very message loop the wait is blocking. It
        /// therefore must not touch a control - and it does not: a model knows nothing
        /// about the window. See <see cref="SampleSession.WaitForTeardown"/>, which this
        /// is the model's own use of.
        /// </para>
        /// <para>
        /// A window which is already awaiting <see cref="DetachAsync"/> on its way out -
        /// most of them do, so the subscription is gone before the connect control closes
        /// the session - pays nothing here: the second detach finds no session and returns
        /// at once.
        /// </para>
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the resources of the model.
        /// </summary>
        /// <remarks>
        /// The same two steps as <see cref="DisposeAsync"/> and in the same order, waited
        /// for instead of awaited: a model which owns something of its own releases it in
        /// <see cref="DisposeAsyncCore"/>, and that has to run whichever way the model is
        /// disposed. A window disposes synchronously, so the asynchronous path alone would
        /// leak exactly the thing that override exists for.
        /// </remarks>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || m_disposed)
            {
                return;
            }

            m_disposed = true;

            SampleSession.WaitForTeardown(async () => {
                await DetachAsync().ConfigureAwait(false);
                await DisposeAsyncCore().ConfigureAwait(false);
            });

            m_lifecycle.Dispose();
        }

        /// <summary>
        /// Releases what the model owns beyond the session, after it has detached.
        /// </summary>
        /// <remarks>
        /// For the rare model which holds something of its own rather than something of
        /// the session - everything which belongs to the session is released by
        /// <see cref="OnDetachingAsync"/>, which also runs when the window disconnects
        /// without closing.
        /// </remarks>
        protected virtual ValueTask DisposeAsyncCore()
        {
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Sets the model up on the session which was just attached.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        protected virtual Task OnAttachedAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Releases what the model holds of the session which is about to be detached.
        /// </summary>
        /// <remarks>
        /// Runs before the session is released and before the window closes it, so this
        /// is where subscriptions are deleted. The session may already be gone; a failure
        /// here is logged by the base class rather than thrown.
        /// </remarks>
        protected virtual Task OnDetachingAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called when the session lost its connection.
        /// </summary>
        protected virtual void OnReconnectStarting()
        {
        }

        /// <summary>
        /// Called when the session has its connection back.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        protected virtual Task OnReconnectCompletedAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// The attached session, or an exception for a caller which needs one while the
        /// model is detached.
        /// </summary>
        /// <exception cref="InvalidOperationException">The model is not attached.</exception>
        protected ISession RequireSession()
        {
            return m_session ?? throw new InvalidOperationException(
                $"{GetType().Name} is not attached to a session.");
        }

        /// <summary>
        /// Raises an event on the context the model was created on, or inline when there
        /// was none.
        /// </summary>
        /// <typeparam name="TArgs">The type of the event arguments.</typeparam>
        /// <param name="handler">The event, which may be null.</param>
        /// <param name="args">The arguments.</param>
        protected void Raise<TArgs>(EventHandler<TArgs> handler, TArgs args)
        {
            if (handler == null)
            {
                return;
            }

            if (m_context != null)
            {
                m_context.Post(_ => handler(this, args), null);
                return;
            }

            handler(this, args);
        }

        /// <summary>
        /// Logs a failure on a background path and reports it through <see cref="Error"/>.
        /// </summary>
        /// <param name="what">What the model was doing.</param>
        /// <param name="exception">What went wrong.</param>
        protected void ReportError(string what, Exception exception)
        {
            Logger.LogError(exception, "{What} failed.", what);
            Raise(Error, new ModelErrorEventArgs(what, exception));
        }

        /// <summary>
        /// Detaches under the lifecycle lock; true if there was a session to detach.
        /// </summary>
        private async Task<bool> DetachCoreAsync()
        {
            if (m_session == null)
            {
                return false;
            }

            try
            {
                await OnDetachingAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // the session is usually already closed when this runs, and a subscription
                // which cannot be deleted on a server that is gone is nothing to report
                Logger.LogWarning(e, "Detaching the model from its session did not complete cleanly.");
            }
            finally
            {
                m_session = null;
                IsReconnecting = false;
            }

            return true;
        }
    }
}
