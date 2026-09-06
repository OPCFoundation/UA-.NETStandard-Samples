/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.WinForms;
using Quickstarts.DurableSubscriptionClient.Model;

namespace Quickstarts.DurableSubscriptionClient
{
    /// <summary>
    /// The main form of the durable subscription sample.
    /// </summary>
    /// <remarks>
    /// The window only renders what the client model
    /// (<see cref="DurableSubscriptionClientModel"/>) reports and turns clicks into calls
    /// on it. The model owns the session, the durable subscription and the persist,
    /// restart and transfer logic which is where the OPC UA part of the sample lives.
    ///
    /// The demo is: connect, create a durable subscription on the server's current time,
    /// then Save &amp; Restart. The client persists the subscription, drops the session
    /// without deleting the subscription on the server, and restarts. On the next start it
    /// transfers the subscription back and the values the server queued while the client
    /// was gone arrive marked as recovered, before the live values continue.
    /// </remarks>
    public partial class MainForm : SampleForm
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
        /// <param name="telemetry">The telemetry context of the client.</param>
        /// <param name="model">The client model of the sample, from the container.</param>
        public MainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry, DurableSubscriptionClientModel model)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_telemetry = telemetry;
            m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.Text = configuration.ApplicationName;

            // created by the container while this constructor runs, so on the thread of
            // the window: that is the context the model captures for its events, and it is
            // why the handlers below can touch the controls directly.
            m_model = model ?? throw new ArgumentNullException(nameof(model));
            m_model.StatusChanged += Model_StatusChanged;
            m_model.ValueReceived += Model_ValueReceived;

            UpdateButtons();
        }
        #endregion

        #region Private Fields
        private readonly ITelemetryContext m_telemetry;
        private readonly ApplicationConfiguration m_configuration;
        private readonly DurableSubscriptionClientModel m_model;
        #endregion

        #region Overrides
        /// <summary>
        /// Releases the resources of the window, and with them the model it owns.
        /// </summary>
        /// <param name="disposing">True if managed resources should be disposed.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                m_model?.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Offers to transfer a persisted subscription back as soon as the window is shown.
        /// </summary>
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // a subscription left behind by a previous run is the restart use case: connect
            // straight away so the model can transfer it back and recover the queued values.
            if (m_model.HasPersistedSubscription)
            {
                await ConnectAsync();
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to the server.
        /// </summary>
        private async void ConnectBTN_ClickAsync(object sender, EventArgs e)
        {
            await ConnectAsync();
        }

        /// <summary>
        /// Creates the durable subscription.
        /// </summary>
        private async void CreateSubscriptionBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                CreateSubscriptionBTN.Enabled = false;
                await m_model.CreateDurableSubscriptionAsync();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
            finally
            {
                UpdateButtons();
            }
        }

        /// <summary>
        /// Persists the subscription and restarts the client to transfer it back.
        /// </summary>
        private async void SaveAndRestartBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                SaveAndRestartBTN.Enabled = false;
                await m_model.PersistAndDropSessionAsync();

                // restart the process: the new instance finds the persisted file, transfers
                // the subscription back and recovers the values queued while it was gone.
                Application.Restart();
                Application.ExitThread();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
                UpdateButtons();
            }
        }

        /// <summary>
        /// Disconnects and deletes the subscription on the server.
        /// </summary>
        private async void DisconnectBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                DisconnectBTN.Enabled = false;
                await m_model.DisconnectAsync(deleteSubscription: true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
            finally
            {
                UpdateButtons();
            }
        }

        /// <summary>
        /// Shows a status message from the model.
        /// </summary>
        private void Model_StatusChanged(object sender, DurableStatusEventArgs e)
        {
            StatusLB.Text = e.Message;
            UpdateButtons();
        }

        /// <summary>
        /// Adds a value the model received to the list.
        /// </summary>
        private void Model_ValueReceived(object sender, DurableValueEventArgs e)
        {
            DataValue value = e.Value;

            var item = new ListViewItem(value.SourceTimestamp.ToLocalTime().ToString("HH:mm:ss.fff"));
            item.SubItems.Add(value.WrappedValue.ToString());
            item.SubItems.Add(e.SequenceNumber.ToString());
            item.SubItems.Add(e.Recovered ? "recovered" : "live");
            item.SubItems.Add(value.StatusCode.ToString());

            if (e.Recovered)
            {
                item.BackColor = System.Drawing.Color.LightYellow;
            }

            ValuesLV.Items.Add(item);
            item.EnsureVisible();

            // keep the list bounded so a long run does not grow without limit.
            const int maxRows = 500;
            while (ValuesLV.Items.Count > maxRows)
            {
                ValuesLV.Items.RemoveAt(0);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Connects to the server through the model and refreshes the window.
        /// </summary>
        private async System.Threading.Tasks.Task ConnectAsync()
        {
            try
            {
                ConnectBTN.Enabled = false;
                await m_model.ConnectAsync(EndpointTB.Text, m_configuration);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_telemetry, this.Text, exception);
            }
            finally
            {
                UpdateButtons();
            }
        }

        /// <summary>
        /// Enables the buttons which match the state of the model.
        /// </summary>
        private void UpdateButtons()
        {
            bool connected = m_model.IsConnected;
            bool hasSubscription = m_model.HasSubscription;

            EndpointTB.Enabled = !connected;
            ConnectBTN.Enabled = !connected;
            CreateSubscriptionBTN.Enabled = connected && !hasSubscription;
            SaveAndRestartBTN.Enabled = connected && hasSubscription;
            DisconnectBTN.Enabled = connected;
        }
        #endregion
    }
}
