/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Client.Controls;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Gds.Client.Controls
{
    /// <summary>
    /// Lists the certificates a GDS or a push-managed server holds for an application, and
    /// drives the OPC 10000-12 v1.05.07 Methods that act on a single certificate slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same dialog serves both certificate-management models, because both grew a
    /// <c>GetCertificates</c> Method that answers the same question - "which certificate is
    /// currently assigned to which CertificateType?" - from opposite ends:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// pull model (§7.9.8): the GDS reports the certificates it has issued for a registered
    /// application, and §7.9.11 <c>CheckRevocationStatus</c> answers whether one of them is
    /// still valid and until when;
    /// </item>
    /// <item>
    /// push model (§7.10.8): the managed server reports the certificates it actually has
    /// installed, §7.10.7 <c>DeleteCertificate</c> empties a slot and §7.10.6
    /// <c>CreateSelfSignedCertificate</c> fills one without involving a CA at all.
    /// </item>
    /// </list>
    /// <para>
    /// Both push Methods are <em>staged</em>: they change nothing until <c>ApplyChanges</c>
    /// runs on the Server Status panel, and they can be undone with <c>CancelChanges</c>
    /// until then. That is the whole point of the v1.05.07 transaction model, so the dialog
    /// reports the staging rather than pretending the change already happened.
    /// </para>
    /// </remarks>
    public partial class CertificateManagementDialog : SampleForm
    {
        private GlobalDiscoveryServerClient m_gds;
        private ServerPushConfigurationClient m_server;
        private RegisteredApplication m_application;
        private readonly ITelemetryContext m_telemetry;
        private bool m_pushMode;

        /// <summary>
        /// Creates the dialog.
        /// </summary>
        public CertificateManagementDialog(ITelemetryContext telemetry)
        {
            InitializeComponent();
            Icon = ClientUtils.GetAppIcon();

            m_telemetry = telemetry;
        }

        /// <summary>
        /// Shows the certificates of the supplied application and returns once the user
        /// closes the dialog.
        /// </summary>
        /// <param name="owner">The owning window.</param>
        /// <param name="gds">The GDS client, used for the pull model.</param>
        /// <param name="server">The push client, used for the push model.</param>
        /// <param name="application">The registration the certificates belong to.</param>
        /// <param name="m_telemetry">The m_telemetry context used to report failures.</param>
        /// <returns>
        /// <c>true</c> when a push Method staged a change, so the caller knows an
        /// <c>ApplyChanges</c> is now pending.
        /// </returns>
        public bool ShowDialog(
            IWin32Window owner,
            GlobalDiscoveryServerClient gds,
            ServerPushConfigurationClient server,
            RegisteredApplication application)
        {
            m_gds = gds;
            m_server = server;
            m_application = application;
            m_pushMode = application?.RegistrationType == RegistrationType.ServerPush;

            Text = m_pushMode
                ? "Server Certificates (Push Management)"
                : "Application Certificates (Pull Management)";

            // the Methods that only exist on one side of the model are hidden rather than
            // disabled: an operation that can never apply here is not a state the user has
            // to reason about.
            DeleteButton.Visible = m_pushMode;
            SelfSignedButton.Visible = m_pushMode;
            CheckRevocationButton.Visible = !m_pushMode;

            StagedChanges = false;

            ShowDialog(owner);

            return StagedChanges;
        }

        /// <summary>
        /// Loads the list once the dialog is up.
        /// </summary>
        /// <remarks>
        /// The read has to happen here rather than before <c>ShowDialog</c>: its continuations
        /// are posted to the message loop of the UI thread, so waiting for it from the thread
        /// that has not started that loop yet would deadlock.
        /// </remarks>
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);

            await RefreshAsync();
        }

        /// <summary>
        /// <c>true</c> once a push Method has staged a change that still needs
        /// <c>ApplyChanges</c>.
        /// </summary>
        public bool StagedChanges { get; private set; }

        /// <summary>
        /// Reloads the list from the GDS or from the managed server.
        /// </summary>
        private async Task RefreshAsync()
        {
            CertificatesListView.Items.Clear();
            StatusLabel.Text = String.Empty;

            try
            {
                ArrayOf<NodeId> certificateTypeIds;
                ArrayOf<ByteString> certificates;

                if (m_pushMode)
                {
                    // the CertificateGroup has to be named: unlike UpdateCertificate and the
                    // other §7.10 Methods, GetCertificates does not read a null group as the
                    // DefaultApplicationGroup and answers Bad_InvalidArgument instead.
                    (certificateTypeIds, certificates) =
                        await m_server.GetCertificatesAsync(m_server.DefaultApplicationGroup);
                }
                else
                {
                    if (String.IsNullOrEmpty(m_application?.ApplicationId))
                    {
                        StatusLabel.Text = "Register the application first - the GDS reports certificates per ApplicationId.";
                        return;
                    }

                    (certificateTypeIds, certificates) = await m_gds.GetCertificatesAsync(
                        NodeId.Parse(m_application.ApplicationId),
                        NodeId.Null);
                }

                NodeId[] typeIds = certificateTypeIds.ToArray() ?? Array.Empty<NodeId>();
                ByteString[] blobs = certificates.ToArray() ?? Array.Empty<ByteString>();

                // §7.9.8 / §7.10.8 return the two arrays aligned; a server that returns
                // them ragged is broken, and truncating is better than throwing at the user.
                int count = Math.Min(typeIds.Length, blobs.Length);

                for (int ii = 0; ii < count; ii++)
                {
                    AddCertificate(typeIds[ii], blobs[ii]);
                }

                StatusLabel.Text = count == 0
                    ? "No certificate is assigned."
                    : String.Format(CultureInfo.CurrentCulture, "{0} certificate(s).", count);
            }
            catch (Exception exception)
            {
                #pragma warning disable CA1849 // Justification: the modal error dialog pumps its own message loop.
                ExceptionDlg.Show(m_telemetry, Text, exception);
                #pragma warning restore CA1849
            }
        }

        private void AddCertificate(NodeId certificateTypeId, ByteString blob)
        {
            var item = new ListViewItem(certificateTypeId.IsNull ? "---" : certificateTypeId.ToString()) {
                Tag = blob
            };

            if (blob.IsNull || blob.Length == 0)
            {
                item.SubItems.Add("(empty slot)");
                item.SubItems.Add(String.Empty);
                item.SubItems.Add(String.Empty);
                item.SubItems.Add(String.Empty);
                CertificatesListView.Items.Add(item);
                return;
            }

            try
            {
                #pragma warning disable CA2000 // Justification: the certificate is only read for display.
                using var certificate = X509CertificateLoader.LoadCertificate(blob.ToArray());
                #pragma warning restore CA2000

                item.SubItems.Add(certificate.Subject);
                item.SubItems.Add(certificate.Issuer);
                item.SubItems.Add(certificate.NotAfter.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture));
                item.SubItems.Add(certificate.Thumbprint);
            }
            catch (Exception exception)
            {
                item.SubItems.Add(exception.Message);
                item.SubItems.Add(String.Empty);
                item.SubItems.Add(String.Empty);
                item.SubItems.Add(String.Empty);
            }

            CertificatesListView.Items.Add(item);
        }

        private ListViewItem SelectedItem
        {
            get
            {
                return CertificatesListView.SelectedItems.Count > 0
                    ? CertificatesListView.SelectedItems[0]
                    : null;
            }
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            await RefreshAsync();
        }

        /// <summary>
        /// Asks the GDS whether the selected certificate is still valid, and until when
        /// (OPC 10000-12 §7.9.11 <c>CheckRevocationStatus</c>).
        /// </summary>
        /// <remarks>
        /// The Method answers with a StatusCode <em>and</em> a <c>ValidityTime</c>: the
        /// answer is only good until that moment, after which the client has to ask again.
        /// Reporting only the status code would hide the half that makes the Method useful.
        /// </remarks>
        private async void CheckRevocationButton_Click(object sender, EventArgs e)
        {
            ListViewItem item = SelectedItem;

            // an empty slot has an empty - not a null - ByteString, so Length is what says
            // whether there is a certificate to ask about.
            if (item?.Tag is not ByteString certificate || certificate.Length == 0)
            {
                return;
            }

            try
            {
                (StatusCode status, DateTimeUtc validityTime) =
                    await m_gds.CheckRevocationStatusAsync(certificate);

                StatusLabel.Text = String.Format(
                    CultureInfo.CurrentCulture,
                    "Revocation status: {0}, valid until {1:yyyy-MM-dd HH:mm:ss}.",
                    status,
                    ((DateTime)validityTime).ToLocalTime());
            }
            catch (Exception exception)
            {
                #pragma warning disable CA1849 // Justification: the modal error dialog pumps its own message loop.
                ExceptionDlg.Show(m_telemetry, Text, exception);
                #pragma warning restore CA1849
            }
        }

        /// <summary>
        /// Stages the removal of the selected certificate slot on the managed server
        /// (OPC 10000-12 §7.10.7 <c>DeleteCertificate</c>).
        /// </summary>
        /// <remarks>
        /// The server refuses to delete a certificate an Endpoint still refers to, but it
        /// only finds out at <c>ApplyChanges</c> - which is exactly why the deletion is
        /// staged. The dialog therefore reports "staged", never "deleted".
        /// </remarks>
        private async void DeleteButton_Click(object sender, EventArgs e)
        {
            ListViewItem item = SelectedItem;

            if (item == null)
            {
                return;
            }

            NodeId certificateTypeId;

            try
            {
                certificateTypeId = NodeId.Parse(item.Text);
            }
            catch (ServiceResultException)
            {
                return;
            }

            if (MessageBox.Show(
                    this,
                    "Stage the removal of this certificate? It takes effect on Apply Changes.",
                    Text,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                await m_server.DeleteCertificateAsync(m_server.DefaultApplicationGroup, certificateTypeId);

                StagedChanges = true;
                StatusLabel.Text =
                    "The removal is staged. Use Apply Changes on the Server Status panel to commit it, or Cancel Changes to discard it.";
            }
            catch (Exception exception)
            {
                #pragma warning disable CA1849 // Justification: the modal error dialog pumps its own message loop.
                ExceptionDlg.Show(m_telemetry, Text, exception);
                #pragma warning restore CA1849
            }
        }

        /// <summary>
        /// Asks the managed server to create a self-signed certificate for the selected slot
        /// (OPC 10000-12 §7.10.6 <c>CreateSelfSignedCertificate</c>).
        /// </summary>
        /// <remarks>
        /// This is the one way to give a server a working certificate without a CA in the
        /// picture at all - the private key never leaves the server. The slot has to be
        /// empty: the server answers <c>Bad_InvalidState</c> for an occupied one, so delete
        /// (and apply) first.
        /// </remarks>
        private async void SelfSignedButton_Click(object sender, EventArgs e)
        {
            ListViewItem item = SelectedItem;

            if (item == null)
            {
                return;
            }

            NodeId certificateTypeId;

            try
            {
                certificateTypeId = NodeId.Parse(item.Text);
            }
            catch (ServiceResultException)
            {
                return;
            }

            string subjectName = m_application?.CertificateSubjectName;

            if (String.IsNullOrEmpty(subjectName))
            {
                subjectName = "CN=" + (m_application?.ApplicationName ?? "Server");
            }

            subjectName = Utils.ReplaceDCLocalhost(subjectName);

            ArrayOf<string> domainNames = m_application != null
                ? m_application.GetDomainNames(null)
                : ArrayOf<string>.Empty;

            try
            {
                await m_server.CreateSelfSignedCertificateAsync(
                    m_server.DefaultApplicationGroup,
                    certificateTypeId,
                    subjectName,
                    domainNames,
                    ArrayOf<string>.Empty,
                    kSelfSignedLifetimeInDays,
                    kSelfSignedKeySizeInBits);

                StagedChanges = true;
                StatusLabel.Text = String.Format(
                    CultureInfo.CurrentCulture,
                    "A self-signed certificate for '{0}' is staged. Use Apply Changes on the Server Status panel to commit it.",
                    subjectName);

                await RefreshAsync();
            }
            catch (Exception exception)
            {
                #pragma warning disable CA1849 // Justification: the modal error dialog pumps its own message loop.
                ExceptionDlg.Show(m_telemetry, Text, exception);
                #pragma warning restore CA1849
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Lifetime of a self-signed certificate created through §7.10.6, in days.
        /// </summary>
        private const ushort kSelfSignedLifetimeInDays = 365;

        /// <summary>
        /// RSA key size of a self-signed certificate created through §7.10.6, in bits.
        /// </summary>
        private const ushort kSelfSignedKeySizeInBits = 2048;
    }
}
