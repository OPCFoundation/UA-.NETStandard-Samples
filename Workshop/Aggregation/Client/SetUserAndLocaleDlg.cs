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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AggregationClient.Model;
using Opc.Ua;
using Opc.Ua.Client.Controls;

namespace AggregationClient
{
    /// <summary>
    /// Prompts the user to change the user name and the locale of the session.
    /// </summary>
    /// <remarks>
    /// The dialog only collects what the user types; the model changes the session in
    /// place, which is what the sample shows: a session keeps its subscriptions and its
    /// node cache while its user or its locale changes.
    /// </remarks>
    public partial class SetUserAndLocaleDlg: Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SetUserAndLocaleDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private AggregationClientModel m_model;
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to specify the user name and locale.
        /// </summary>
        public async Task<bool> ShowDialogAsync(AggregationClientModel model, CancellationToken ct = default)
        {
            m_model = model;

            #region Task #D3 - Change Locale and User Identity
            UpdateUserIdentity();
            await UpdateLocaleAsync(ct);
            #endregion

            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            return true;
        }
        #endregion

        #region Task #D3 - Change Locale and User Identity
        /// <summary>
        /// Shows the user the session was opened with. The password is never read back.
        /// </summary>
        private void UpdateUserIdentity()
        {
            UserNameTB.Text = m_model.CurrentUserName;
            PasswordTB.Text = null;
        }

        /// <summary>
        /// Updates the locale displayed in the control.
        /// </summary>
        private async Task UpdateLocaleAsync(CancellationToken ct = default)
        {
            LocaleCB.Items.Clear();

            // get the locales from the server.
            foreach (string locale in await m_model.ReadAvailableLocalesAsync(ct))
            {
                LocaleCB.Items.Add(locale);
            }

            // select the default locale.
            if (LocaleCB.Items.Count > 0)
            {
                LocaleCB.SelectedIndex = 0;
            }

            // select the current locale for the session.
            foreach (string locale in m_model.PreferredLocales)
            {
                int index = LocaleCB.FindStringExact(locale);

                if (index >= 0)
                {
                    LocaleCB.SelectedIndex = index;
                    break;
                }
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Changes the user and the locale of the session to what the user typed.
        /// </summary>
        private async void OkBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                #region Task #D3 - Change Locale and User Identity
                UserIdentity identity = null;

                // use the anonymous identity of the user name is not provided.
                if (String.IsNullOrEmpty(UserNameTB.Text))
                {
#pragma warning disable CA2000 // Justification: UserIdentity ownership is transferred to UpdateSessionAsync.
                    identity = new UserIdentity();
#pragma warning restore CA2000
                }

                // could add check for domain name in user name and use a kerberos token instead.
                else
                {
#pragma warning disable CA2000 // Justification: UserIdentity ownership is transferred to UpdateSessionAsync.
                    identity = new UserIdentity(UserNameTB.Text, Encoding.UTF8.GetBytes(PasswordTB.Text));
#pragma warning restore CA2000
                }

                // can specify multiple locales but just use one here to keep the UI simple.
                var preferredLocales = new List<string> { LocaleCB.SelectedItem as string };

                // the session is updated in place, and the window keeps reacting while the
                // server processes the request.
                await m_model.UpdateSessionAsync(identity, preferredLocales);
                #endregion

                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_model?.Telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
