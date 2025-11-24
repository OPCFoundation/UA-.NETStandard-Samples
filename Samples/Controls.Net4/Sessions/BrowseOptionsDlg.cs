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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using System.Threading.Tasks;
using System.Threading;

namespace Opc.Ua.Sample.Controls
{
    public partial class BrowseOptionsDlg : Form
    {
        #region Constructors
        public BrowseOptionsDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            foreach (object value in Enum.GetValues(typeof(BrowseDirection)))
            {
                BrowseDirectionCB.Items.Add(value);
            }

            BrowseDirectionCB.SelectedIndex = 0;
        }
        #endregion

        #region Private Fields
        private Browser m_browser;
        private ISession m_session;
        private ITelemetryContext m_telemetry;
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to specify the browse options.
        /// </summary>
        public async Task<bool> ShowDialogAsync(Browser browser, ISession session, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            if (browser == null) throw new ArgumentNullException(nameof(browser));

            m_browser = browser;
            m_session = session;
            m_telemetry = telemetry;
            await ReferenceTypeCTRL.InitializeAsync(m_browser.Session as Session, null, ct);

            ViewIdTB.Text = null;
            ViewTimestampDP.Value = ViewTimestampDP.MinDate;
            ViewVersionNC.Value = 0;

            if (browser.View != null)
            {
                ViewIdTB.Text = String.Format("{0}", browser.View.ViewId);
                ViewVersionNC.Value = browser.View.ViewVersion;
                ViewVersionCK.Checked = browser.View.ViewVersion != 0;

                if (browser.View.Timestamp > ViewTimestampDP.MinDate)
                {
                    ViewTimestampDP.Value = browser.View.Timestamp;
                    ViewTimestampCK.Checked = true;
                }
            }

            MaxReferencesReturnedNC.Value = browser.MaxReferencesReturned;
            BrowseDirectionCB.SelectedItem = browser.BrowseDirection;
            ReferenceTypeCTRL.SelectedTypeId = browser.ReferenceTypeId;
            IncludeSubtypesCK.Checked = browser.IncludeSubtypes;
            NodeClassMaskCK.Checked = browser.NodeClassMask != 0;

            NodeClassList.Items.Clear();

            foreach (NodeClass value in Enum.GetValues(typeof(NodeClass)))
            {
                if (value == NodeClass.Unspecified)
                {
                    continue;
                }

                int index = NodeClassList.Items.Add(value);
                NodeClassList.SetItemChecked(index, (browser.NodeClassMask & (int)value) != 0);
            }

            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }
        #endregion

        #region Event Handlers
        private void ViewIdTB_TextChanged(object sender, EventArgs e)
        {
            ViewTimestampCK.Enabled = ViewVersionCK.Enabled = !String.IsNullOrEmpty(ViewIdTB.Text);
        }

        private void NodeClassMask_CheckedChanged(object sender, EventArgs e)
        {
            NodeClassList.Enabled = NodeClassMaskCK.Checked;
        }

        private void ViewVersionCK_CheckedChanged(object sender, EventArgs e)
        {
            ViewVersionNC.Enabled = ViewVersionCK.Checked;
        }

        private void ViewTimestampCK_CheckedChanged(object sender, EventArgs e)
        {
            ViewTimestampDP.Enabled = ViewTimestampCK.Checked;
        }

        private async void BrowseBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                Browser browser = new Browser(m_browser.Session);

                browser.BrowseDirection = BrowseDirection.Forward;
                browser.NodeClassMask = (int)NodeClass.View | (int)NodeClass.Object;
                browser.ReferenceTypeId = ReferenceTypeIds.Organizes;
                browser.IncludeSubtypes = true;

                ReferenceDescription reference = await new SelectNodeDlg().ShowDialogAsync(browser, Objects.ViewsFolder, m_session, m_telemetry);

                if (reference != null)
                {
                    if (reference.NodeClass != NodeClass.View)
                    {
                        MessageBox.Show("Please select a valid view node id.", this.Text);
                        return;
                    }

                    ViewIdTB.Text = Utils.Format("{0}", reference.NodeId);
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            NodeId viewId = null;

            try
            {
                viewId = NodeId.Parse(ViewIdTB.Text);
            }
            catch (Exception)
            {
                MessageBox.Show("Please enter a valid node id for the view id.", this.Text);
            }

            try
            {
                ViewDescription view = null;

                if (!NodeId.IsNull(viewId) || ViewTimestampCK.Checked || ViewVersionCK.Checked)
                {
                    view = new ViewDescription();

                    view.ViewId = viewId;
                    view.Timestamp = DateTime.MinValue;
                    view.ViewVersion = 0;

                    if (ViewTimestampCK.Checked && ViewTimestampDP.Value > ViewTimestampDP.MinDate)
                    {
                        view.Timestamp = ViewTimestampDP.Value;
                    }

                    if (ViewVersionCK.Checked)
                    {
                        view.ViewVersion = (uint)ViewVersionNC.Value;
                    }
                }

                m_browser.View = view;
                m_browser.MaxReferencesReturned = (uint)MaxReferencesReturnedNC.Value;
                m_browser.BrowseDirection = (BrowseDirection)BrowseDirectionCB.SelectedItem;
                m_browser.NodeClassMask = (int)NodeClass.View | (int)NodeClass.Object;
                m_browser.ReferenceTypeId = ReferenceTypeCTRL.SelectedTypeId;
                m_browser.IncludeSubtypes = IncludeSubtypesCK.Checked;
                m_browser.NodeClassMask = 0;

                uint nodeClassMask = 0;

                foreach (NodeClass nodeClass in NodeClassList.CheckedItems)
                {
                    nodeClassMask |= (uint)nodeClass;
                }

                m_browser.NodeClassMask = nodeClassMask;

                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion
    }
}
