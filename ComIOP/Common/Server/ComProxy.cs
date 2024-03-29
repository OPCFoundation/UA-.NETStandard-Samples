/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Globalization;
using Opc.Ua.Client;
using System.Threading.Tasks;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// A base class for classes that implement an OPC COM specification.
    /// </summary>
    public class ComProxy : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComProxy"/> class.
        /// </summary>
        public ComProxy()
		{
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    lock (m_lock)
                    {
                        m_running = false;

                        if (m_reconnectTimer != null)
                        {
                            m_reconnectTimer.Dispose();
                            m_reconnectTimer = null;
                        }

                        if (m_session != null)
                        {
                            m_session.Dispose();
                            m_session = null;
                        }
                    }
                }

                m_disposed = true;
            }
        }

        private bool m_disposed;
        #endregion

        #region Initialization
        /// <summary>
        /// Gets the endpoint.
        /// </summary>
        /// <value>The endpoint.</value>
        public ConfiguredEndpoint Endpoint
        {
            get { return m_endpoint; }
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="ComProxy"/> is connected.
        /// </summary>
        /// <value><c>true</c> if connected; otherwise, <c>false</c>.</value>
        public bool Connected
        {
            get 
            {
                lock (m_lock)
                {
                    if (m_session == null)
                    {
                        return false;
                    }

                    return m_session.Connected;
                }
            }
        }

        /// <summary>
        /// Called when the object is loaded by the COM process.
        /// </summary>
        /// <param name="clsid">The CLSID used to activate the server.</param>
        /// <param name="configuration">The application configuration for the COM process.</param>
        public virtual void Load(Guid clsid, ApplicationConfiguration configuration)
        {
            lock (m_lock)
            {
                // save the application configuration.
                m_configuration = configuration;

                // set the start time.
                m_startTime = DateTime.UtcNow;

                // look up default time zone.
                DateTime now = DateTime.Now;

                m_timebias = (int)-TimeZone.CurrentTimeZone.GetUtcOffset(now).TotalMinutes;

                if (TimeZone.CurrentTimeZone.IsDaylightSavingTime(now))
                {
                    m_timebias += 60;
                }

                // load endpoint information.
                m_endpoint = LoadConfiguredEndpoint(clsid);

                // create a dummy endpoint so the COM client receives an indication of the problem.
                if (m_endpoint == null)
                {
                    ApplicationDescription server = new ApplicationDescription();

                    server.ApplicationName = "(Missing Configuration File)";
                    server.ApplicationType = ApplicationType.Server;
                    server.ApplicationUri  = clsid.ToString();

                    m_endpoint = new ConfiguredEndpoint(server, null);
                }

                m_clsid = clsid;
                m_running = true;
                
                // connect to the server.
                OnCreateSession(null);
            }
        }

        /// <summary>
        /// Unloads this instance.
        /// </summary>
        public virtual void Unload()
        {
            lock (m_lock)
            {
                if (m_session != null)
                {
                    m_session.Close(5000);
                }
            }

            Dispose();
        }

        /// <summary>
        /// Returns the available locales.
        /// </summary>
        public int[] GetAvailableLocaleIds()
        {
            ThrowIfNotConnected();

            List<int> localeIds = new List<int>();
            localeIds.Add(ComUtils.LOCALE_SYSTEM_DEFAULT);

            try
            {
                lock (m_lock)
                {
                    DataValue value = m_session.ReadValue(Opc.Ua.VariableIds.Server_ServerCapabilities_LocaleIdArray);
                    string[] locales = value.GetValue<string[]>(null);

                    for (int ii = 0; ii < locales.Length; ii++)
                    {
                        try
                        {
                            CultureInfo culture = CultureInfo.GetCultureInfo(locales[ii]);

                            if (culture != null)
                            {
                                localeIds.Add(culture.LCID);
                            }
                        }
                        catch (Exception)
                        {
                            // ignore invalid locales.
                        }
                    }
                }
            }
            catch (Exception)
            {
                // ignore network errors.
            }

            return localeIds.ToArray();
        }

        /// <summary>
        /// Sets the current locale.
        /// </summary>
        public void SetLocaleId(int localeId)
        {
            ThrowIfNotConnected();

            try
            {
                StringCollection preferredLocales = new StringCollection();

                if (localeId != 0 && localeId != ComUtils.LOCALE_SYSTEM_DEFAULT)
                {
                    CultureInfo culture = CultureInfo.GetCultureInfo(localeId);

                    if (culture != null)
                    {
                        preferredLocales.Add(culture.Name);
                    }
                }

                m_session.ChangePreferredLocales(preferredLocales);
            }
            catch (Exception)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }
        }

        /// <summary>
        /// Gets the current locale.
        /// </summary>
        public int GetLocaleId()
        {
            ThrowIfNotConnected();

            try
            {
                if (m_session.PreferredLocales.Count == 0)
                {
                    return ComUtils.LOCALE_SYSTEM_DEFAULT;
                }

                string locale = m_session.PreferredLocales[0];

                CultureInfo culture = CultureInfo.GetCultureInfo(locale);

                if (culture != null)
                {
                    return culture.LCID;
                }

                return ComUtils.LOCALE_SYSTEM_DEFAULT;
            }
            catch (Exception)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the current session.
        /// </summary>
        /// <value>The session.</value>
        public Opc.Ua.Client.Session Session
        {
            get { return m_session; }
        }
        #endregion

        #region Protected Properties
        /// <summary>
        /// Gets the lock.
        /// </summary>
        /// <value>The lock.</value>
        protected object Lock
        {
            get { return m_lock; }
        }

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        /// <value>The configuration.</value>
        protected ApplicationConfiguration Configuration
        {
            get { return m_configuration; }
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="ComProxy"/> is running.
        /// </summary>
        /// <value><c>true</c> if running; otherwise, <c>false</c>.</value>
        protected bool Running
        {
            get { return m_running; }
        }

        /// <summary>
        /// Gets the start time.
        /// </summary>
        /// <value>The start time.</value>
        protected DateTime StartTime
        {
            get { return m_startTime; }
        }

        /// <summary>
        /// Gets the timebias.
        /// </summary>
        /// <value>The timebias.</value>
        protected int Timebias
        {
            get { return m_timebias; }
        }

        /// <summary>
        /// Called when a new session is created.
        /// </summary>
        protected virtual void OnSessionCreated()
        {
        }

        /// <summary>
        /// Called when a session is reconnected.
        /// </summary>
        protected virtual void OnSessionReconected()
        {
        }
                
        /// <summary>
        /// Called when a session reconnect is scheduled.
        /// </summary>
        protected virtual void OnReconnectInProgress(int secondsToReconnect)
        {
        }

        /// <summary>
        /// Called when a session is removed.
        /// </summary>
        protected virtual void OnSessionRemoved()
        {
        }

        /// <summary>
        /// Saves the current endpoint configuration.
        /// </summary>
        protected void SaveConfiguration()
        {
            SaveConfiguredEndpoint(m_clsid, m_endpoint);
        }

        /// <summary>
        /// Throws if disposed or not connected.
        /// </summary>
        protected Session ThrowIfNotConnected()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            if (m_session == null || !m_session.Connected)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            return m_session;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates a session with the server.
        /// </summary>
        /// <param name="state">The state.</param>
        private async void OnCreateSession(object state)
        {
            // check if nothing to do.
            lock (m_lock)
            {
                if (!m_running || m_session != null)
                {
                    return;
                }

                // stop the reconnect timer.
                if (m_reconnectTimer != null)
                {
                    m_reconnectTimer.Dispose();
                    m_reconnectTimer = null;
                }
            }

            // create the session.
            try
            {
                Session session = await Connect(m_clsid);
                session.KeepAlive += new KeepAliveEventHandler(Session_KeepAlive);

                lock (m_lock)
                {
                    // discard unneeded session.
                    if (m_session != null)
                    {
                        session.Dispose();
                        return;
                    }

                    // update the session.
                    m_session = session;
                }

                OnSessionCreated();
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Could not create a Session with the UA Server.");

                // schedule a reconnect.
                lock (m_lock)
                {
                    m_reconnectTimer = new Timer(OnCreateSession, null, 20000, Timeout.Infinite);
                    Utils.Trace("Calling OnCreateSession in 20000ms.");
                    OnReconnectInProgress(20);
                }
            }
        }

        /// <summary>
        /// Creates a session with the server.
        /// </summary>
        /// <param name="state">The state.</param>
        private void OnReconnectSession(object state)
        {
            // get the CLSID of the COM server.
            Session session = state as Session;

            if (session == null)
            {
                return;
            }

            // check if nothing to do.
            lock (m_lock)
            {
                if (!m_running || !Object.ReferenceEquals(m_session, session))
                {
                    return;
                }

                // stop the reconnect timer.
                if (m_reconnectTimer != null)
                {
                    m_reconnectTimer.Dispose();
                    m_reconnectTimer = null;
                }
            }

            // reconnect the session.
            try
            {
                session.Reconnect();

                lock (m_lock)
                {
                    if (!m_running || !Object.ReferenceEquals(m_session, session))
                    {
                        session.Dispose();
                        return;
                    }
                }

                OnSessionReconected();
            }
            catch (Exception e)
            {
                Utils.Trace("Unexpected reconnecting a Session with the UA Server. {0}", e.Message);

                // schedule a reconnect.
                lock (m_lock)
                {
                    // check if session has been replaced.
                    if (!m_running || !Object.ReferenceEquals(m_session, session))
                    {
                        session.Dispose();
                        return;
                    }

                    // check if the session has been closed.
                    ServiceResultException sre = e as ServiceResultException;

                    if (sre == null || sre.StatusCode != StatusCodes.BadSessionClosed)
                    {
                        m_session = null;
                        session.Dispose();
                        OnSessionRemoved();
                        ThreadPool.QueueUserWorkItem(OnCreateSession, null);
                        Utils.Trace("Calling OnCreateSession NOW.");
                        return;
                    }

                    // check if reconnecting is still an option.
                    if (m_lastKeepAliveTime.AddMilliseconds(session.SessionTimeout) > DateTime.UtcNow)
                    {
                        m_reconnectTimer = new Timer(OnReconnectSession, session, 20000, Timeout.Infinite);
                        Utils.Trace("Calling OnReconnectSession in 20000ms.");
                        OnReconnectInProgress(20);
                        return;
                    }

                    // give up and re-create the session.
                    m_session = null;
                    session.Dispose();
                    OnSessionRemoved();
                    m_reconnectTimer = new Timer(OnCreateSession, null, 20000, Timeout.Infinite);
                    Utils.Trace("Calling OnCreateSession in 20000ms.");
                    OnReconnectInProgress(20);
                }
            }
        }

        /// <summary>
        /// The session the keep alive handler.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="e">The <see cref="Opc.Ua.Client.KeepAliveEventArgs"/> instance containing the event data.</param>
        private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            int missedKeepAlives = 0;

            lock (m_lock)
            {
                // check if the session is closed.
                if (!m_running || !Object.ReferenceEquals(m_session, session))
                {
                    return;
                }

                // check if everything is ok.
                if (ServiceResult.IsGood(e.Status))
                {
                    m_missedKeepAlives = 0;
                    m_lastKeepAliveTime = DateTime.UtcNow;
                    return;
                }
                
                // increment miss count.                    
                missedKeepAlives = ++m_missedKeepAlives;
            }

            // attempt to reconnect after two misses.
            if (missedKeepAlives == 2)
            {
                ThreadPool.QueueUserWorkItem(OnReconnectSession, session);
                Utils.Trace("Calling OnReconnectSession NOW.");
            }
        }

        /// <summary>
        /// Connects to the UA server identfied by the CLSID.
        /// </summary>
        /// <param name="clsid">The CLSID.</param>
        /// <returns>The UA server.</returns>
        private async Task<Session> Connect(Guid clsid)
        {
            // load the endpoint information.
            ConfiguredEndpoint endpoint = m_endpoint = LoadConfiguredEndpoint(clsid);

            if (endpoint == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }

            // update security information.
            if (endpoint.UpdateBeforeConnect)
            {
                endpoint.UpdateFromServer();

                // check if halted while waiting for a response.
                if (!m_running)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }
            }

            // look up the client certificate.
            X509Certificate2 clientCertificate = await m_configuration.SecurityConfiguration.ApplicationCertificate.Find(true);

            // create a message context to use with the channel.
            ServiceMessageContext messageContext = m_configuration.CreateMessageContext();

            // create the channel.
            ITransportChannel channel = SessionChannel.Create(
                m_configuration,
                endpoint.Description,
                endpoint.Configuration,
                clientCertificate,
                messageContext);

            // create the session.
            Session session = new Session(channel, m_configuration, endpoint, clientCertificate);

            // create a session name that is useful for debugging.
            string sessionName = Utils.Format("COM Client ({0})", System.Net.Dns.GetHostName());

            // open the session.
            Opc.Ua.UserIdentity identity = null;

            if (endpoint.UserIdentity != null)
            {
                // need to decode password.
                UserNameIdentityToken userNameToken = endpoint.UserIdentity as UserNameIdentityToken;

                if (userNameToken != null)
                {
                    UserNameIdentityToken copy = new UserNameIdentityToken();
                    copy.PolicyId = userNameToken.PolicyId;
                    copy.DecryptedPassword = new UTF8Encoding().GetString(userNameToken.Password);
                    copy.UserName = userNameToken.UserName;
                    copy.EncryptionAlgorithm = userNameToken.EncryptionAlgorithm;
                    identity = new Opc.Ua.UserIdentity(copy);
                }

                // create the identity object.
                else
                {
                    identity = new Opc.Ua.UserIdentity(endpoint.UserIdentity);
                }
            }

            session.Open(sessionName, identity);

            // return the new session.
            return session;
        }

        /// <summary>
        /// Reads the UA endpoint information associated the CLSID
        /// </summary>
        /// <param name="clsid">The CLSID used to activate the COM server.</param>
        /// <returns>The endpoint.</returns>
        protected ConfiguredEndpoint LoadConfiguredEndpoint(Guid clsid)
        {
            try
            {
                string relativePath = Utils.Format("%CommonApplicationData%\\OPC Foundation\\ComPseudoServers\\{0}.xml", clsid);
                string absolutePath = Utils.GetAbsoluteFilePath(relativePath, false, false, false);

                // oops - nothing found.
                if (absolutePath == null)
                {
                    return null;
                }

                // open the file.
                using (FileStream istrm = File.Open(absolutePath, FileMode.Open, FileAccess.Read))
                {
                    using (XmlTextReader reader = new XmlTextReader(istrm))
                    {
                        DataContractSerializer serializer = new DataContractSerializer(typeof(ConfiguredEndpoint));
                        return (ConfiguredEndpoint)serializer.ReadObject(reader, false);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error loading endpoint configuration for COM Proxy with CLSID={0}.", clsid);
                return null;
            }
        }

        /// <summary>
        /// Saves the UA endpoint information associated the CLSID.
        /// </summary>
        /// <param name="clsid">The CLSID used to activate the COM server.</param>
        /// <param name="endpoint">The endpoint.</param>
        protected void SaveConfiguredEndpoint(Guid clsid, ConfiguredEndpoint endpoint)
        {
            try
            {
                string relativePath = Utils.Format("%CommonApplicationData%\\OPC Foundation\\ComPseudoServers\\{0}.xml", clsid);
                string absolutePath = Utils.GetAbsoluteFilePath(relativePath, false, false, true);

                // oops - nothing found.
                if (absolutePath == null)
                {
                    absolutePath = Utils.GetAbsoluteFilePath(relativePath, true, false, true);
                }

                // open the file.
                FileStream ostrm = File.Open(absolutePath, FileMode.Create, FileAccess.ReadWrite);

                using (XmlTextWriter writer = new XmlTextWriter(ostrm, System.Text.Encoding.UTF8))
                {
                    DataContractSerializer serializer = new DataContractSerializer(typeof(ConfiguredEndpoint));
                    serializer.WriteObject(writer, endpoint);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error saving endpoint configuration for COM Proxy with CLSID={0}.", clsid);
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private Guid m_clsid;
        private ApplicationConfiguration m_configuration;
        private ConfiguredEndpoint m_endpoint;
        private bool m_running;
        private DateTime m_startTime;
        private int m_timebias;
        private Timer m_reconnectTimer;
        private int m_missedKeepAlives;
        private DateTime m_lastKeepAliveTime;
        private Opc.Ua.Client.Session m_session;
        #endregion
	}
}
