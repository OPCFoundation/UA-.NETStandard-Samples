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
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// The payload of <see cref="SampleConnection.StatusChanged"/>: one line for a status
    /// bar, and whether it reports a problem. Named apart from the <c>ConnectionStatusEventArgs</c>
    /// of the stack, which every namespace under <c>Opc.Ua</c> would otherwise resolve to first.
    /// </summary>
    public sealed class SampleConnectionStatusEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        /// <param name="isError">Whether the status reports a problem.</param>
        /// <param name="time">The time the status belongs to.</param>
        /// <param name="message">The status itself.</param>
        public SampleConnectionStatusEventArgs(bool isError, DateTime time, string message)
        {
            IsError = isError;
            Time = time;
            Message = message;
        }

        /// <summary>
        /// Whether the status reports a problem.
        /// </summary>
        public bool IsError { get; }

        /// <summary>
        /// The time the status belongs to.
        /// </summary>
        public DateTime Time { get; }

        /// <summary>
        /// The status itself.
        /// </summary>
        public string Message { get; }
    }

    /// <summary>
    /// The connection of a sample client: discovery, the session, the reconnect it reports
    /// and the close - without a window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the half of the shared connect control which is not a tool bar. The control
    /// used to own all of it, which meant that the only way to open the kind of session the
    /// samples run on was to create a <c>UserControl</c>; the headless tests had to go
    /// through <see cref="SampleSessionFactory"/> instead and so exercised a second,
    /// parallel implementation of the same sequence. The control now reads its two input
    /// fields, hands them here and renders what comes back.
    /// </para>
    /// <para>
    /// <b>Threading.</b> Every event is raised on whichever thread reached the point that
    /// raises it - a keep alive on a session worker, a status while connecting on the
    /// caller's thread. That is deliberate: a window has to marshal them anyway, and the
    /// one caller which must not be marshalled is a window on its way out. A form closing
    /// blocks its own message loop in <see cref="Disconnect"/>, so an event posted to that
    /// loop would arrive after the form is gone; raised inline, the form still sees it.
    /// </para>
    /// <para>
    /// <b>Certificates.</b> Accepting an untrusted certificate is a question for a person,
    /// so it stays with the caller: <see cref="CertificateValidation"/> is hooked onto the
    /// configuration and is what a window points at its dialog. With no handler set, the
    /// <c>AutoAcceptUntrustedCertificates</c> flag of the configuration decides alone.
    /// </para>
    /// </remarks>
    public sealed class SampleConnection : IAsyncDisposable
    {
        /// <summary>
        /// The discovery timeout, in milliseconds.
        /// </summary>
        public const int DefaultDiscoverTimeout = SampleSessionFactory.DefaultDiscoverTimeout;

        /// <summary>
        /// The session timeout, in milliseconds.
        /// </summary>
        public const uint DefaultSessionTimeout = SampleSessionFactory.DefaultSessionTimeout;

        private readonly Dictionary<Uri, EndpointDescription> m_endpoints = new Dictionary<Uri, EndpointDescription>();
        private readonly Func<Opc.Ua.Security.Certificates.Certificate, ServiceResult, bool> m_acceptError;
        private ApplicationConfiguration m_configuration;
        private ITelemetryContext m_telemetry;
        private ILogger m_logger;
        private ISession m_session;

        /// <summary>
        /// Creates a connection which is not attached to a configuration yet.
        /// </summary>
        public SampleConnection()
        {
            m_acceptError = OnCertificateValidation;
        }

        /// <summary>
        /// The configuration of the client. Setting it takes over the certificate
        /// validation of its certificate manager.
        /// </summary>
        public ApplicationConfiguration Configuration
        {
            get => m_configuration;

            set
            {
                if (ReferenceEquals(m_configuration, value))
                {
                    return;
                }

                if (m_configuration != null)
                {
                    m_configuration.CertificateManager.AcceptError = null;
                }

                m_configuration = value;

                if (m_configuration != null)
                {
                    m_configuration.CertificateManager.AcceptError = m_acceptError;
                }
            }
        }

        /// <summary>
        /// What a person is asked when the certificate of a server does not validate.
        /// Null lets the configuration decide on its own.
        /// </summary>
        public Func<Opc.Ua.Security.Certificates.Certificate, ServiceResult, bool> CertificateValidation { get; set; }

        /// <summary>
        /// The name of the session to create, for the logs of the server. Empty names the
        /// session after the application.
        /// </summary>
        public string SessionName { get; set; }

        /// <summary>
        /// Whether the certificate of the server may name a different host than the one it
        /// was reached on.
        /// </summary>
        public bool DisableDomainCheck { get; set; }

        /// <summary>
        /// The locales to ask the server for, or null.
        /// </summary>
        #pragma warning disable CA1819 // Justification: the sample API shape of the connect control is preserved.
        public string[] PreferredLocales { get; set; }
        #pragma warning restore CA1819

        /// <summary>
        /// The user to open the session for. Null opens it anonymously.
        /// </summary>
        public IUserIdentity UserIdentity { get; set; }

        /// <summary>
        /// The discovery timeout, in milliseconds.
        /// </summary>
        public int DiscoverTimeout { get; set; } = DefaultDiscoverTimeout;

        /// <summary>
        /// The session timeout, in milliseconds.
        /// </summary>
        public uint SessionTimeout { get; set; } = DefaultSessionTimeout;

        /// <summary>
        /// The session which is open, or null.
        /// </summary>
        public ISession Session => m_session;

        /// <summary>
        /// True while a session is open.
        /// </summary>
        public bool IsConnected => m_session != null;

        /// <summary>
        /// Raised whenever there is something new to show in a status bar.
        /// </summary>
        public event EventHandler<SampleConnectionStatusEventArgs> StatusChanged;

        /// <summary>
        /// Raised after a session was opened and after it was closed. The
        /// <see cref="Session"/> tells the two apart.
        /// </summary>
        public event EventHandler ConnectComplete;

        /// <summary>
        /// Raised for every keep alive the server answers.
        /// </summary>
        public event EventHandler<KeepAliveEventArgs> KeepAlive;

        /// <summary>
        /// Raised when the session lost its connection and started to reconnect.
        /// </summary>
        public event EventHandler ReconnectStarting;

        /// <summary>
        /// Raised when the session has its connection back.
        /// </summary>
        public event EventHandler ReconnectComplete;

        /// <summary>
        /// The endpoint a previous discovery of this url found, or null.
        /// </summary>
        /// <param name="url">The url which was discovered.</param>
        public EndpointDescription GetEndpointDescription(Uri url)
        {
            return m_endpoints.TryGetValue(url, out EndpointDescription endpoint) ? endpoint : null;
        }

        /// <summary>
        /// Discovers the endpoints of a server and opens a session on the best one.
        /// </summary>
        /// <param name="endpointUrl">The url of the server.</param>
        /// <param name="useSecurity">Whether to prefer a secured endpoint.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        /// <param name="sessionTimeout">The session timeout in milliseconds, or zero for <see cref="SessionTimeout"/>.</param>
        /// <param name="ct">The cancellation token.</param>
        #pragma warning disable CA1054 // Justification: the samples spell endpoint urls as strings.
        public async Task<ISession> ConnectAsync(
            string endpointUrl,
            bool useSecurity,
            ITelemetryContext telemetry,
            uint sessionTimeout = 0,
            CancellationToken ct = default)
        #pragma warning restore CA1054
        {
            ArgumentNullException.ThrowIfNull(telemetry);

            RememberTelemetry(telemetry);
            ReportStatus(false, DateTime.Now, Utils.Format("Connecting [{0}]", endpointUrl));

            await DisconnectAsync(ct).ConfigureAwait(false);

            EndpointDescription endpointDescription = await CoreClientUtils
                .SelectEndpointAsync(m_configuration, endpointUrl, useSecurity, DiscoverTimeout, telemetry, ct)
                .ConfigureAwait(false);

            return await OpenAsync(
                endpoint => new ManagedSessionFactory(telemetry).CreateAsync(
                    m_configuration,
                    endpoint,
                    false,
                    !DisableDomainCheck,
                    NameOfSession(),
                    sessionTimeout == 0 ? SessionTimeout : sessionTimeout,
                    UserIdentity ?? new UserIdentity(),
                    PreferredLocales,
                    ct),
                endpointDescription,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens a session over a connection the server established itself.
        /// </summary>
        /// <remarks>
        /// The first reverse hello of a server is spent on the discovery, which consumes
        /// the connection: this returns null then, and the next hello of the same server
        /// finds the endpoint remembered and opens the session on it.
        /// </remarks>
        /// <param name="connection">The connection the server opened.</param>
        /// <param name="useSecurity">Whether to prefer a secured endpoint.</param>
        /// <param name="telemetry">The telemetry context of the client.</param>
        /// <param name="discoverTimeout">The discovery timeout in milliseconds, or -1 for <see cref="DiscoverTimeout"/>.</param>
        /// <param name="sessionTimeout">The session timeout in milliseconds, or zero for <see cref="SessionTimeout"/>.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<ISession> ConnectAsync(
            ITransportWaitingConnection connection,
            bool useSecurity,
            ITelemetryContext telemetry,
            int discoverTimeout = -1,
            uint sessionTimeout = 0,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(telemetry);

            if (connection.EndpointUrl == null)
            {
                throw new ArgumentException("Endpoint URL is not valid.", nameof(connection));
            }

            RememberTelemetry(telemetry);

            if (!m_endpoints.TryGetValue(connection.EndpointUrl, out EndpointDescription endpointDescription))
            {
                // the discovery uses the reverse connection up; return and wait for the
                // next reverse hello of the server.
                m_endpoints[connection.EndpointUrl] = await CoreClientUtils
                    .SelectEndpointAsync(m_configuration, connection, useSecurity, discoverTimeout, telemetry, ct)
                    .ConfigureAwait(false);

                return null;
            }

            await DisconnectAsync(ct).ConfigureAwait(false);

            return await OpenAsync(
                endpoint => new ManagedSessionFactory(telemetry).CreateAsync(
                    m_configuration,
                    connection,
                    endpoint,
                    false,
                    !DisableDomainCheck,
                    NameOfSession(),
                    sessionTimeout,
                    UserIdentity ?? new UserIdentity(),
                    PreferredLocales,
                    ct),
                endpointDescription,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Closes the session and reports it.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            await CloseSessionAsync(ct).ConfigureAwait(false);

            ConnectComplete?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Closes the session for a caller which cannot await.
        /// </summary>
        /// <remarks>
        /// The close runs on a thread pool thread, where there is no synchronization
        /// context for its continuations to be posted back to: awaited on the thread of a
        /// window which is closing, the continuation would be posted to the message loop
        /// the wait is blocking and neither side would move again. The completion is then
        /// reported on the caller's thread rather than from inside the task, so that a
        /// window which is already on its way out still receives it.
        /// </remarks>
        public void Disconnect()
        {
            ReportStatus(false, DateTime.UtcNow, "Disconnected");

            SampleSession.WaitForTeardown(() => CloseSessionAsync());

            ConnectComplete?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Configuration = null;

            await CloseSessionAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Opens the session the factory delegate creates and reports it.
        /// </summary>
        private async Task<ISession> OpenAsync(
            Func<ConfiguredEndpoint, Task<ISession>> createSession,
            EndpointDescription endpointDescription,
            CancellationToken ct)
        {
            var endpointConfiguration = EndpointConfiguration.Create(m_configuration);
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            // the managed session brings its own connection state machine and reconnect
            // policy, so there is no SessionReconnectHandler to wire up here.
            m_session = await createSession(endpoint).ConfigureAwait(false);

            AttachSession();

            ConnectComplete?.Invoke(this, EventArgs.Empty);

            try
            {
                ReportStatus(false, DateTime.Now, "Connected, loading complex type system.");

                var typeSystem = ComplexTypeSystemClientExtensions.Create(m_session, m_telemetry);

                await typeSystem.LoadAsync(ct: ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // the session is usable without the custom types; a sample which needs them
                // fails later, with an error which says which type is missing
                ReportStatus(true, DateTime.Now, "Connected, failed to load complex type system.");
                m_logger?.LogWarning(e, "Failed to load complex type system.");
            }

            return m_session;
        }

        /// <summary>
        /// Closes and releases the session without reporting it.
        /// </summary>
        /// <remarks>
        /// Closing the managed session also stops its connection state machine, so there is
        /// no separate reconnect handler to cancel. The close is bounded: a session which is
        /// in the middle of a reconnect attempt against a server that is gone cannot close
        /// until that attempt has run out. See <see cref="SampleSession.CloseAndDisposeAsync(ISession, CancellationToken)"/>.
        /// </remarks>
        private async Task CloseSessionAsync(CancellationToken ct = default)
        {
            ISession session = m_session;

            if (session == null)
            {
                return;
            }

            DetachSession(session);
            m_session = null;

            await SampleSession.CloseAndDisposeAsync(session, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Subscribes to the session events which are reported on.
        /// </summary>
        private void AttachSession()
        {
            m_session.KeepAlive += OnKeepAlive;

            if (m_session is ManagedSession managedSession)
            {
                managedSession.ConnectionStateChanged += OnConnectionStateChanged;
            }
        }

        /// <summary>
        /// Unsubscribes from the session events which are reported on.
        /// </summary>
        private void DetachSession(ISession session)
        {
            session.KeepAlive -= OnKeepAlive;

            if (session is ManagedSession managedSession)
            {
                managedSession.ConnectionStateChanged -= OnConnectionStateChanged;
            }
        }

        /// <summary>
        /// Reports a keep alive, and the communication error a bad one means.
        /// </summary>
        private void OnKeepAlive(ISession session, KeepAliveEventArgs e)
        {
            // a keep alive of a session which was already replaced says nothing about this one
            if (!ReferenceEquals(session, m_session))
            {
                return;
            }

            if (ServiceResult.IsBad(e.Status))
            {
                // the managed session starts the reconnect sequence itself, this only reports it
                ReportStatus(true, e.CurrentTime, Utils.Format("Communication Error ({0})", e.Status));
                return;
            }

            ReportStatus(false, e.CurrentTime, Utils.Format("Connected [{0}]", session.Endpoint.EndpointUrl));

            KeepAlive?.Invoke(this, e);
        }

        /// <summary>
        /// Reports the connection state changes of the managed session.
        /// </summary>
        /// <remarks>
        /// The managed session keeps the same <see cref="ISession"/> instance across a
        /// reconnect, so only the transitions are reported - there is no session to swap
        /// out as there was with the SessionReconnectHandler.
        /// </remarks>
        private void OnConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            // the event may be raised by the session or by the connection state machine
            // behind it, so only a sender which is a session is worth comparing.
            if (m_session == null || (sender is ISession sessionOfEvent && !ReferenceEquals(sessionOfEvent, m_session)))
            {
                return;
            }

            switch (e.NewState)
            {
                case ConnectionState.Reconnecting:
                case ConnectionState.Failover:
                {
                    ReportStatus(true, DateTime.UtcNow, Utils.Format("Reconnecting (attempt {0})", e.ReconnectAttempt));
                    ReconnectStarting?.Invoke(this, e);
                    break;
                }

                case ConnectionState.Connected:
                {
                    ReportStatus(false, DateTime.UtcNow, Utils.Format("Connected [{0}]", m_session.Endpoint.EndpointUrl));

                    if (e.PreviousState is ConnectionState.Reconnecting or ConnectionState.Failover)
                    {
                        ReconnectComplete?.Invoke(this, e);
                    }

                    break;
                }

                case ConnectionState.Disconnected:
                {
                    ReportStatus(true, DateTime.UtcNow, Utils.Format("Disconnected ({0})", e.Error));
                    break;
                }
            }
        }

        /// <summary>
        /// Asks the caller about a certificate which did not validate.
        /// </summary>
        private bool OnCertificateValidation(Opc.Ua.Security.Certificates.Certificate certificate, ServiceResult error)
        {
            if (m_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                return true;
            }

            Func<Opc.Ua.Security.Certificates.Certificate, ServiceResult, bool> validation = CertificateValidation;

            return validation != null && validation(certificate, error);
        }

        /// <summary>
        /// Reports a line for a status bar.
        /// </summary>
        private void ReportStatus(bool isError, DateTime time, string message)
        {
            StatusChanged?.Invoke(this, new SampleConnectionStatusEventArgs(isError, time, message));
        }

        /// <summary>
        /// The session name to ask for, which falls back to the application name.
        /// </summary>
        private string NameOfSession()
        {
            return String.IsNullOrEmpty(SessionName) ? m_configuration?.ApplicationName : SessionName;
        }

        /// <summary>
        /// Keeps the telemetry context of the last connect, for the paths which have no
        /// caller to take one from.
        /// </summary>
        private void RememberTelemetry(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<SampleConnection>();
        }
    }
}
