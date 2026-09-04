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
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Gds.Client.Controls
{
    /// <summary>
    /// Displays the details of a certificate. Every caller of this control
    /// shows an X.509 certificate read only, so the control reads the named
    /// certificate properties directly instead of navigating a boxed CLR
    /// object with reflection.
    /// </summary>
    public partial class EditValueCtrl : SampleUserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs the object.
        /// </summary>
        public EditValueCtrl()
        {
            InitializeComponent();
            MaxDisplayTextLength = 100;
            ValuesDV.AutoGenerateColumns = false;
            #pragma warning disable CA2000 // Justification: WinForms/sample ownership or lifetime is managed outside the local scope.
            ImageList = new ImageListControl().ImageList;
            #pragma warning restore CA2000

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Values");

            m_dataset.Tables[0].Columns.Add("Field", typeof(CertificateField));
            m_dataset.Tables[0].Columns.Add("Name", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataType", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(string));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));

            ValuesDV.DataSource = m_dataset.Tables[0];
        }
        #endregion

        #region Private Fields
        #pragma warning disable CA2213 // Justification: Designer-generated Dispose owns the WinForms disposal pattern for this sample.
        private DataSet m_dataset;
        #pragma warning restore CA2213
        private int m_maxDisplayTextLength;
        private event EventHandler m_ValueChanged;
        #endregion

        #region CertificateField Class
        /// <summary>
        /// One displayed certificate property.
        /// </summary>
        private sealed class CertificateField
        {
            public string Name;
            public string DataType;
            public string Value;
            public ByteString Bytes;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The maximum length of a value string displayed in a column.
        /// </summary>
        [DefaultValue(100)]
        public int MaxDisplayTextLength
        {
            get
            {
                return m_maxDisplayTextLength;
            }

            set
            {
                if (value < 20)
                {
                    m_maxDisplayTextLength = 20;
                }

                m_maxDisplayTextLength = value;
            }
        }

        /// <summary>
        /// Returns true if the Back command can be called.
        /// </summary>
        public bool CanGoBack
        {
            get
            {
                return TextValueTB.Visible;
            }
        }

        /// <summary>
        /// Raised when the displayed content changes.
        /// </summary>
        public event EventHandler ValueChanged
        {
            add { m_ValueChanged += value; }
            remove { m_ValueChanged -= value; }
        }

        /// <summary>
        /// Moves the display back from a detail view to the property list.
        /// </summary>
        public void Back()
        {
            if (!CanGoBack)
            {
                return;
            }

            ShowFieldList();
        }

        /// <summary>
        /// Displays the details of a certificate in the control.
        /// </summary>
        public void ShowCertificate(X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));

            ButtonPanel.Visible = false;
            ValuesDV.ReadOnly = true;
            m_dataset.Tables[0].Clear();

            Certificate details = Certificate.From(certificate);

            AddField("SubjectName", "String", details.Subject);
            AddField("IssuerName", "String", details.Issuer);
            AddField("ValidFrom", "DateTime", Utils.Format("{0:yyyy-MM-dd HH:mm:ss}", details.NotBefore));
            AddField("ValidTo", "DateTime", Utils.Format("{0:yyyy-MM-dd HH:mm:ss}", details.NotAfter));
            AddField("SerialNumber", "String", details.SerialNumber);
            AddField("Thumbprint", "String", details.Thumbprint);
            AddField("SignatureAlgorithm", "String", details.SignatureAlgorithm.FriendlyName);
            AddField("PublicKeyAlgorithm", "String", details.PublicKey.Oid.FriendlyName);
            AddField("PublicKey", "ByteString", ByteString.From(details.PublicKey.EncodedKeyValue.RawData));
            AddField("KeySize", "Int32", Utils.Format("{0}", X509Utils.GetRSAPublicKeySize(details)));

            try
            {
                IReadOnlyList<string> applicationUris = X509Utils.GetApplicationUrisFromCertificate(details);
                AddField("ApplicationUri", "String", applicationUris.Count > 0 ? applicationUris[0] : null);
            }
            catch (Exception e)
            {
                AddField("ApplicationUri", "String", e.Message);
            }

            try
            {
                AddField("Domains", "String[]", String.Join(", ", X509Utils.GetDomainsFromCertificate(details)));
            }
            catch (Exception e)
            {
                AddField("Domains", "String[]", e.Message);
            }

            AddField("RawData", "ByteString", ByteString.From(certificate.RawData));

            ShowFieldList();
        }

        /// <summary>
        /// Clears the control.
        /// </summary>
        public void ShowNothing()
        {
            ButtonPanel.Visible = false;
            m_dataset.Tables[0].Clear();
            ValuesDV.Visible = false;
            TextValueTB.Visible = true;
            TextValueTB.ReadOnly = true;
            TextValueTB.Text = null;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Adds a text property to the list.
        /// </summary>
        private void AddField(string name, string dataType, string value)
        {
            AddRow(new CertificateField { Name = name, DataType = dataType, Value = value ?? String.Empty });
        }

        /// <summary>
        /// Adds a byte string property to the list. The bytes are shown in a
        /// detail view when the row is double clicked.
        /// </summary>
        private void AddField(string name, string dataType, ByteString value)
        {
            AddRow(new CertificateField { Name = name, DataType = dataType, Value = "<double click to see data>", Bytes = value });
        }

        private void AddRow(CertificateField field)
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            row[0] = field;
            row[1] = field.Name;
            row[2] = field.DataType;
            row[3] = Truncate(field.Value);
            row[4] = ImageList.Images[ImageIndex.Get(Attributes.Value, field.Value)];

            m_dataset.Tables[0].Rows.Add(row);
        }

        private string Truncate(string text)
        {
            if (text != null && text.Length > MaxDisplayTextLength)
            {
                return string.Concat(text.AsSpan(0, MaxDisplayTextLength), "...");
            }

            return text;
        }

        /// <summary>
        /// Shows the property list.
        /// </summary>
        private void ShowFieldList()
        {
            ValuesDV.Visible = true;
            TextValueTB.Visible = false;
            ValuesDV.ClearSelection();

            m_ValueChanged?.Invoke(this, null);
        }

        /// <summary>
        /// Shows the bytes of a property in the detail view.
        /// </summary>
        private void ShowDetailView(CertificateField field)
        {
            ValuesDV.Visible = false;
            TextValueTB.Visible = true;
            TextValueTB.ReadOnly = true;

            StringBuilder buffer = new StringBuilder();

            if (!field.Bytes.IsNull)
            {
                int count = 0;

                foreach (byte b in field.Bytes.Span)
                {
                    if (buffer.Length > 0 && (count % 30) == 0)
                    {
                        buffer.Append("\r\n");
                    }

                    buffer.AppendFormat("{0:X2} ", b);
                    count++;
                }
            }
            else
            {
                buffer.Append(field.Value);
            }

            TextValueTB.Font = new Font("Courier New", TextValueTB.Font.Size);
            TextValueTB.Text = buffer.ToString();

            m_ValueChanged?.Invoke(this, null);
        }
        #endregion

        #region Event Handlers
        private void ValuesDV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewRow row in ValuesDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    CertificateField field = (CertificateField)source.Row[0];

                    if (!field.Bytes.IsNull || (field.Value != null && field.Value.Length > MaxDisplayTextLength))
                    {
                        ShowDetailView(field);
                    }

                    break;
                }
            }
            catch (Exception ex)
            {
                Opc.Ua.Client.Controls.ExceptionDlg.Show(LoggerUtils.Null.Logger, Text, ex);
            }
        }

        private void ValuesDV_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            // the certificate view is read only.
        }

        private void ValuesDV_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            // the certificate view is read only.
        }

        private void TextValueTB_VisibleChanged(object sender, EventArgs e)
        {
            if (this.Visible)
            {
                foreach (DataGridViewRow row in ValuesDV.Rows)
                {
                    row.Selected = false;
                }
            }
        }
        #endregion
    }
}
