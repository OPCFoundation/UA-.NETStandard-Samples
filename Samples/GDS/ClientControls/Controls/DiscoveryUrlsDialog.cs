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
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Gds.Client.Controls
{
    public partial class DiscoveryUrlsDialog : SampleForm
    {
        public DiscoveryUrlsDialog(ITelemetryContext telemetry)
        {
            InitializeComponent();
            Icon = ImageListControl.AppIcon;

            m_logger = telemetry.CreateLogger<DiscoveryUrlsDialog>();
        }

        private List<string> m_discoveryUrls;
        private readonly ILogger m_logger;

        public IList<string> ShowDialog(IWin32Window owner, IList<string> discoveryUrls)
        {
            StringBuilder builder = new StringBuilder();

            if (discoveryUrls != null)
            {
                foreach (var discoveryUrl in discoveryUrls)
                {
                    if (discoveryUrl != null && !String.IsNullOrEmpty(discoveryUrl.Trim()))
                    {
                        if (builder.Length > 0)
                        {
                            builder.Append("\r\n");
                        }

                        builder.Append(discoveryUrl.Trim());
                    }
                }
            }

            DiscoveryUrlsTextBox.Text = builder.ToString();

            if (base.ShowDialog(owner) != DialogResult.OK)
            {
                return null;
            }

            return m_discoveryUrls;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            try
            {
                List<string> validatedUrls = new List<string>();

                string[] discoveryUrls = DiscoveryUrlsTextBox.Text.Split('\n');

                foreach (var discoveryUrl in discoveryUrls)
                {
                    if (discoveryUrl != null && !String.IsNullOrEmpty(discoveryUrl.Trim()))
                    {
                        string url = discoveryUrl.Trim();

                        if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                        {
                            #pragma warning disable CA2208 // Justification: Public sample API compatibility is preserved.
                            throw new ArgumentException("'" + discoveryUrl + "' is not a valid URL.", "discoveryUrls");
                            #pragma warning restore CA2208
                        }

                        validatedUrls.Add(url);
                    }
                }

                m_discoveryUrls = validatedUrls;
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(m_logger, Text, ex);
            }
        }

        private void DiscoveryUrlsDialog_VisibleChanged(object sender, EventArgs e)
        {
            DiscoveryUrlsTextBox.SelectedText = "";
        }
    }
}
