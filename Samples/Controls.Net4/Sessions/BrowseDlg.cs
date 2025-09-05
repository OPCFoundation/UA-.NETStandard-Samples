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
    public partial class BrowseDlg : Form
    {
        #region Constructors
        public BrowseDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            m_SessionClosing = new EventHandler(Session_Closing);
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private EventHandler m_SessionClosing;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the address space with the specified view
        /// </summary>
        public async Task ShowAsync(Session session, NodeId startId, CancellationToken ct = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            if (m_session != null)
            {
                m_session.SessionClosing -= m_SessionClosing;
            }

            m_session = session;
            m_session.SessionClosing += m_SessionClosing;

            Browser browser = new Browser(session);

            browser.BrowseDirection = BrowseDirection.Both;
            browser.ContinueUntilDone = true;
            browser.ReferenceTypeId = ReferenceTypeIds.References;

            await BrowseCTRL.InitializeAsync(browser, startId, ct);

            await UpdateNavigationBarAsync(ct);

            Show();
            BringToFront();
        }
        #endregion

        /// <summary>
        /// Updates the navigation bar with the current positions in the browse control.
        /// </summary>
        private async Task UpdateNavigationBarAsync(CancellationToken ct = default)
        {
            int index = 0;

            foreach (NodeId nodeId in BrowseCTRL.Positions)
            {
                Node node = await m_session.NodeCache.FindAsync(nodeId, ct) as Node;

                string displayText = await m_session.NodeCache.GetDisplayTextAsync(node, ct);

                if (index < NodeCTRL.Items.Count)
                {
                    if (displayText != NodeCTRL.Items[index] as string)
                    {
                        NodeCTRL.Items[index] = displayText;
                    }
                }
                else
                {
                    NodeCTRL.Items.Add(displayText);
                }

                index++;
            }

            while (index < NodeCTRL.Items.Count)
            {
                NodeCTRL.Items.RemoveAt(NodeCTRL.Items.Count - 1);
            }

            NodeCTRL.SelectedIndex = BrowseCTRL.Position;
        }

        private void Session_Closing(object sender, EventArgs e)
        {
            if (Object.ReferenceEquals(sender, m_session))
            {
                m_session.SessionClosing -= m_SessionClosing;
                m_session = null;
                Close();
            }
        }

        private void AddressSpaceDlg_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (m_session != null)
            {
                m_session.SessionClosing -= m_SessionClosing;
                m_session = null;
            }
        }

        private async void BackBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await BrowseCTRL.BackAsync();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void ForwardBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await BrowseCTRL.ForwardAsync();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void NodeCTRL_SelectedIndexChangedAsync(object sender, EventArgs e)
        {
            try
            {
                await BrowseCTRL.SetPositionAsync(NodeCTRL.SelectedIndex);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void BrowseCTRL_PositionChanged(object sender, EventArgs e)
        {
            try
            {
                if (BrowseCTRL.Position < NodeCTRL.Items.Count)
                {
                    NodeCTRL.SelectedIndex = BrowseCTRL.Position;
                }
                else
                {
                    NodeCTRL.SelectedIndex = -1;
                }
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void BrowseCTRL_PositionAddedAsync(object sender, EventArgs e)
        {
            try
            {
                await UpdateNavigationBarAsync();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
}
