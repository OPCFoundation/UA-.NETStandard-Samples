/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Windows.Forms;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Threading;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a ApplicationDescription.
    /// </summary>
    public partial class CertificateDlg : SampleForm
    {
        private readonly ITelemetryContext m_telemetry;

        /// <summary>
        /// Contructs the object.
        /// </summary>
        public CertificateDlg(ITelemetryContext telemetry)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            PrivateKeyCB.Items.Add("No");
            PrivateKeyCB.Items.Add("Yes");
            PrivateKeyCB.SelectedIndex = 0;

            m_telemetry = telemetry;
        }

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public async Task<bool> ShowDialogAsync(CertificateIdentifier certificateIdentifier, CancellationToken ct = default)
        {
            CertificateStoreCTRL.Telemetry = m_telemetry;
            CertificateStoreCTRL.StoreType = null;
            CertificateStoreCTRL.StorePath = null;
            PrivateKeyCB.SelectedIndex = 0;
            PropertiesCTRL.Initialize((X509Certificate2)null);

            if (certificateIdentifier != null)
            {
                X509Certificate2 certificate = await FindCertificateAsync(certificateIdentifier, false, ct);

                CertificateStoreCTRL.StoreType = certificateIdentifier.StoreType;
                CertificateStoreCTRL.StorePath = certificateIdentifier.StorePath;

                if (certificate != null && await FindCertificateAsync(certificateIdentifier, true, ct) != null)
                {
                    PrivateKeyCB.SelectedIndex = 1;
                }
                else
                {
                    PrivateKeyCB.SelectedIndex = 0;
                }

                PropertiesCTRL.Initialize(certificate);
            }

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }

        private async Task<X509Certificate2> FindCertificateAsync(CertificateIdentifier certificateIdentifier, bool needPrivateKey, CancellationToken ct)
        {
            if (certificateIdentifier == null)
            {
                return null;
            }

            var resolvedCertificate = await CertificateIdentifierResolver.ResolveAsync(certificateIdentifier, null, needPrivateKey, null, m_telemetry, ct);

            if (resolvedCertificate != null)
            {
                return resolvedCertificate.AsX509Certificate2();
            }

            if (String.IsNullOrEmpty(certificateIdentifier.StoreType) ||
                String.IsNullOrEmpty(certificateIdentifier.StorePath) ||
                (String.IsNullOrEmpty(certificateIdentifier.Thumbprint) && String.IsNullOrEmpty(certificateIdentifier.SubjectName)))
            {
                return null;
            }

            CertificateStoreIdentifier storeId = new CertificateStoreIdentifier { StoreType = certificateIdentifier.StoreType, StorePath = certificateIdentifier.StorePath };

            using (ICertificateStore store = storeId.OpenStore(m_telemetry))
            {
                var certificates = !String.IsNullOrEmpty(certificateIdentifier.Thumbprint)
                    ? await store.FindByThumbprintAsync(certificateIdentifier.Thumbprint, ct)
                    : await store.EnumerateAsync(ct);

                var certificate = CertificateIdentifier.Find(
                    certificates,
                    certificateIdentifier.Thumbprint,
                    certificateIdentifier.SubjectName,
                    null,
                    certificateIdentifier.CertificateType,
                    needPrivateKey);

                return certificate?.AsX509Certificate2();
            }
        }

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public bool ShowDialog(X509Certificate2 certificate)
        {
            CertificateStoreCTRL.Telemetry = m_telemetry;
            CertificateStoreCTRL.StoreType = null;
            CertificateStoreCTRL.StorePath = null;
            PrivateKeyCB.SelectedIndex = 0;
            PropertiesCTRL.Initialize(certificate);

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
}
