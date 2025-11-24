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
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Opc.Ua;
using Opc.Ua.Client;
using System.Threading.Tasks;
using System.Threading;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a value.
    /// </summary>
    public partial class EditComplexValue2Dlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditComplexValue2Dlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }
        #endregion

        #region Private Fields
        private ISession m_session;
        private NodeId m_variableId;
        private Variant m_value;
        private bool m_textChanged;
        private QualifiedName m_encodingName;
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to edit a value.
        /// </summary>
        public async Task<Variant> ShowDialogAsync(ISession session, NodeId variableId, Variant value, string caption, CancellationToken ct = default)
        {
            if (caption != null)
            {
                this.Text = caption;
            }

            m_session = session;
            m_variableId = variableId;

            await SetValueAsync(value, ct);

            if (ShowDialog() != DialogResult.OK)
            {
                return Variant.Null;
            }

            return GetValue();
        }
        #endregion

        /// <summary>
        /// Sets the value shown in the control.
        /// </summary>
        private async Task SetValueAsync(Variant value, CancellationToken ct = default)
        {
            ValueTB.ForeColor = Color.Empty;
            ValueTB.Font = new Font(ValueTB.Font, FontStyle.Regular);

            m_textChanged = false;

            // check for null.
            if (Variant.Null == value)
            {
                ValueTB.Text = String.Empty;
                m_value = Variant.Null;
                return;
            }

            // get the source type.
            TypeInfo sourceType = value.TypeInfo;

            if (sourceType == null)
            {
                sourceType = TypeInfo.Construct(value.Value);
            }

            m_value = new Variant(value.Value, sourceType);

            // display value as text.
            StringBuilder buffer = new StringBuilder();
            XmlWriter writer = XmlWriter.Create(buffer, new XmlWriterSettings() { Indent = true, OmitXmlDeclaration = true });
            XmlEncoder encoder = new XmlEncoder(new XmlQualifiedName("Value", Namespaces.OpcUaXsd), writer, m_session.MessageContext);
            encoder.WriteVariantContents(m_value.Value, m_value.TypeInfo);
            writer.Close();

            ValueTB.Text = buffer.ToString();

            // extract the encoding id from the value.
            ExpandedNodeId encodingId = null;
            ExtensionObjectEncoding encoding = ExtensionObjectEncoding.None;

            if (sourceType.BuiltInType == BuiltInType.ExtensionObject)
            {
                ExtensionObject extension = null;

                if (sourceType.ValueRank == ValueRanks.Scalar)
                {
                    extension = (ExtensionObject)m_value.Value;
                }
                else
                {
                    // only use the first item in the list for arrays.
                    ExtensionObject[] list = (ExtensionObject[])m_value.Value;

                    if (list.Length > 0)
                    {
                        extension = list[0];
                    }
                }

                encodingId = extension.TypeId;
                encoding = extension.Encoding;
            }

            if (encodingId == null)
            {
                StatusCTRL.Visible = false;
                return;
            }

            // check if the encoding is known.
            IObject encodingNode = await m_session.NodeCache.FindAsync(encodingId, ct) as IObject;

            if (encodingNode == null)
            {
                StatusCTRL.Visible = false;
                return;
            }

            // update the encoding shown.
            if (encoding == ExtensionObjectEncoding.EncodeableObject)
            {
                EncodingCB.Text = "(Converted to XML by Client)";
            }
            else
            {
                EncodingCB.Text = await m_session.NodeCache.GetDisplayTextAsync(encodingNode, ct);
            }

            m_encodingName = encodingNode.BrowseName;

            // find the data type for the encoding.
            IDataType dataTypeNode = null;

            foreach (INode node in await m_session.NodeCache.FindAsync(encodingNode.NodeId, Opc.Ua.ReferenceTypeIds.HasEncoding, true, false, ct))
            {
                dataTypeNode = node as IDataType;

                if (dataTypeNode != null)
                {
                    break;
                }
            }

            if (dataTypeNode == null)
            {
                StatusCTRL.Visible = false;
                return;
            }

            // update data type display.
            DataTypeTB.Text = await m_session.NodeCache.GetDisplayTextAsync(dataTypeNode, ct);
            DataTypeTB.Tag = dataTypeNode;

            // update encoding drop down.
            EncodingCB.DropDownItems.Clear();

            foreach (INode node in await m_session.NodeCache.FindAsync(dataTypeNode.NodeId, Opc.Ua.ReferenceTypeIds.HasEncoding, false, false, ct))
            {
                IObject encodingNode2 = node as IObject;

                if (encodingNode2 != null)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(await m_session.NodeCache.GetDisplayTextAsync(encodingNode2, ct));
                    item.Tag = encodingNode2;
                    item.Click += new EventHandler(EncodingCB_Item_Click);
                    EncodingCB.DropDownItems.Add(item);
                }
            }

            StatusCTRL.Visible = true;
        }

        /// <summary>
        /// Converts the XML back to a value.
        /// </summary>
        private Variant GetValue()
        {
            if (!m_textChanged)
            {
                return m_value;
            }

            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(ValueTB.Text, new XmlReaderSettings() { XmlResolver = null }))
            {
                document.Load(reader);
            }

            // find the first element.
            XmlElement element = null;

            for (XmlNode node = document.DocumentElement.FirstChild; node != null; node = node.NextSibling)
            {
                element = node as XmlElement;

                if (element != null)
                {
                    break;
                }
            }

            XmlDecoder decoder = new XmlDecoder(element, m_session.MessageContext);

            decoder.PushNamespace(Namespaces.OpcUaXsd);
            TypeInfo typeInfo = null;
            object value = decoder.ReadVariantContents(out typeInfo);

            return new Variant(value, typeInfo);
        }

        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }
        #endregion

        private void EncodingCB_Item_Click(object sender, EventArgs e)
        {
            try
            {
                ToolStripMenuItem item = sender as ToolStripMenuItem;

                if (item != null)
                {
                    IObject encodingNode = item.Tag as IObject;
                    m_encodingName = encodingNode.BrowseName;
                    EncodingCB.Text = item.Text;
                    ValueTB.Text = null;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }

        private async void RefreshBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = m_variableId;
                nodeToRead.AttributeId = Attributes.Value;
                nodeToRead.DataEncoding = m_encodingName;


                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
                nodesToRead.Add(nodeToRead);

                // read the attributes.
                ReadResponse response = await m_session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    default);

                DataValueCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                // check for error.
                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    ValueTB.Text = results[0].StatusCode.ToString();
                    ValueTB.ForeColor = Color.Red;
                    ValueTB.Font = new Font(ValueTB.Font, FontStyle.Bold);
                    return;
                }

                await SetValueAsync(results[0].WrappedValue);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }

        private async void UpdateBTN_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                WriteValue nodeToWrite = new WriteValue();
                nodeToWrite.NodeId = m_variableId;
                nodeToWrite.AttributeId = Attributes.Value;
                nodeToWrite.Value = new DataValue();
                nodeToWrite.Value.WrappedValue = GetValue();

                WriteValueCollection nodesToWrite = new WriteValueCollection();
                nodesToWrite.Add(nodeToWrite);

                // read the attributes.
                WriteResponse response = await m_session.WriteAsync(
                    null,
                    nodesToWrite,
                    default);

                ResponseHeader responseHeader = response.ResponseHeader;
                StatusCodeCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                ClientBase.ValidateResponse(results, nodesToWrite);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToWrite);

                // check for error.
                if (StatusCode.IsBad(results[0]))
                {
                    throw ServiceResultException.Create(results[0], 0, diagnosticInfos, responseHeader.StringTable);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }

        private void ValueTB_TextChanged(object sender, EventArgs e)
        {
            m_textChanged = true;
        }

        private void EncodingCB_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}
