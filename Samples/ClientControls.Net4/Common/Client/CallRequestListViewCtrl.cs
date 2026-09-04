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
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using System.Threading.Tasks;
using System.Threading;
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays the results from a history read operation.
    /// </summary>
    public partial class CallRequestListViewCtrl : SampleUserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public CallRequestListViewCtrl()
        {
            InitializeComponent();
            ResultsDV.AutoGenerateColumns = false;
            #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
            ImageList = new ClientUtils().ImageList;
            #pragma warning restore CA2000

            m_dataset = new DataSet();
            m_dataset.Tables.Add("Arguments");

            m_dataset.Tables[0].Columns.Add("Argument", typeof(Argument));
            m_dataset.Tables[0].Columns.Add("Icon", typeof(Image));
            m_dataset.Tables[0].Columns.Add("Name", typeof(string));
            m_dataset.Tables[0].Columns.Add("DataType", typeof(string));
            m_dataset.Tables[0].Columns.Add("Value", typeof(Variant));
            m_dataset.Tables[0].Columns.Add("Result", typeof(string));

            ResultsDV.DataSource = m_dataset.Tables[0];
        }
        #endregion

        #region Private Fields
        #pragma warning disable CA2213 // Justification: WinForms designer/owner lifetime manages this sample field.
        private DataSet m_dataset;
        #pragma warning restore CA2213
        private ISession m_session;
        private NodeId m_objectId;
        private NodeId m_methodId;
        private Argument[] m_inputArguments;
        private Argument[] m_outputArguments;
        #endregion

        #region Public Members
        /// <summary>
        /// Changes the session used for the call request.
        /// </summary>
        public void ChangeSession(ISession session)
        {
            m_session = session;
        }

        /// <summary>
        /// Sets the method for the call request.
        /// </summary>
        public async Task SetMethodAsync(NodeId objectId, NodeId methodId, CancellationToken ct = default)
        {
            if (objectId.IsNull)
            {
                throw new ArgumentNullException(nameof(objectId));
            }

            if (methodId.IsNull)
            {
                throw new ArgumentNullException(nameof(methodId));
            }

            m_objectId = objectId;
            m_methodId = methodId;

            await ReadArgumentsAsync(methodId, ct);
            await DisplayInputArgumentsAsync(ct);
        }

        /// <summary>
        /// Calls the method.
        /// </summary>
        public async Task CallAsync(CancellationToken ct = default)
        {
            // build list of methods to call.
            List<CallMethodRequest> methodsToCall = new List<CallMethodRequest>();

            CallMethodRequest methodToCall = new CallMethodRequest();

            methodToCall.ObjectId = m_objectId;
            methodToCall.MethodId = m_methodId;

            List<Variant> inputArguments = new List<Variant>();

            foreach (DataRow row in m_dataset.Tables[0].Rows)
            {
                Argument argument = (Argument)row[0];
                Variant value = (Variant)row[4];
                argument.Value = value;
                inputArguments.Add(value);
            }

            methodToCall.InputArguments = inputArguments;

            methodsToCall.Add(methodToCall);

            // call the method.

            CallResponse response = await m_session.CallAsync(
                null,
                methodsToCall,
                ct);

            ResponseHeader responseHeader = response.ResponseHeader;
            List<CallMethodResult> results = response.Results.ToList();
            List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

            ClientBase.ValidateResponse(results, methodsToCall);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, methodsToCall);

            for (int ii = 0; ii < results.Count; ii++)
            {
                // display any input argument errors.
                if (results[ii].InputArgumentResults != null)
                {
                    for (int jj = 0; jj < results[ii].InputArgumentResults.Count; jj++)
                    {
                        if (StatusCode.IsBad(results[ii].InputArgumentResults[jj]))
                        {
                            DataRow row = m_dataset.Tables[0].Rows[jj];
                            row[5] = results[ii].InputArgumentResults[jj].ToString();
                            ResultCH.Visible = true;
                        }
                    }
                }

                // throw an exception on error.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    throw ServiceResultException.Create(results[ii].StatusCode, ii, diagnosticInfos, responseHeader.StringTable);
                }

                // display the output arguments
                ResultCH.Visible = false;
                NoArgumentsLB.Visible = m_outputArguments == null || m_outputArguments.Length == 0;
                NoArgumentsLB.Text = "Method invoked successfully.\r\nNo output arguments to display.";
                m_dataset.Tables[0].Rows.Clear();

                if (m_outputArguments != null)
                {
                    for (int jj = 0; jj < m_outputArguments.Length; jj++)
                    {
                        DataRow row = m_dataset.Tables[0].NewRow();

                        if (results[ii].OutputArguments.Count > jj)
                        {
                            await UpdateRowAsync(row, m_outputArguments[jj], results[ii].OutputArguments[jj], true, ct);
                        }
                        else
                        {
                            await UpdateRowAsync(row, m_outputArguments[jj], Variant.Null, true, ct);
                        }

                        m_dataset.Tables[0].Rows.Add(row);
                    }
                }
            }
        }

        /// <summary>
        /// Returns the grid to the enter input arguments state.
        /// </summary>
        public async Task BackAsync(CancellationToken ct = default)
        {
            await DisplayInputArgumentsAsync(ct);

            // clear any selection.
            foreach (DataGridViewRow row in ResultsDV.Rows)
            {
                row.Selected = false;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Displays the input arguments.
        /// </summary>
        private async Task DisplayInputArgumentsAsync(CancellationToken ct = default)
        {
            ResultCH.Visible = false;
            NoArgumentsLB.Visible = m_inputArguments == null || m_inputArguments.Length == 0;
            NoArgumentsLB.Text = "No input arguments to display.";

            m_dataset.Tables[0].Rows.Clear();

            if (m_inputArguments != null)
            {
                foreach (Argument argument in m_inputArguments)
                {
                    DataRow row = m_dataset.Tables[0].NewRow();
                    await UpdateRowAsync(row, argument, GetArgumentValue(argument), false, ct);
                    m_dataset.Tables[0].Rows.Add(row);
                }
            }
        }

        /// <summary>
        /// Returns the value of an argument. The control keeps the values it
        /// edits as Variants in the argument's local value slot.
        /// </summary>
        private static Variant GetArgumentValue(Argument argument)
        {
            return argument.Value is Variant value ? value : Variant.Null;
        }

        /// <summary>
        /// Updates the row with an argument and its value.
        /// </summary>
        private async Task UpdateRowAsync(DataRow row, Argument argument, Variant value, bool isOutputArgument, CancellationToken ct = default)
        {
            string dataType = await m_session.NodeCache.GetDisplayTextAsync(argument.DataType, ct);

            if (argument.ValueRank >= 0)
            {
                dataType += "[]";
            }

            row[0] = argument;
            row[1] = ImageList.Images[ClientUtils.GetImageIndex(isOutputArgument, value)];
            row[2] = argument.Name;
            row[3] = dataType;
            row[4] = value;
            row[5] = String.Empty;
        }

        /// <summary>
        /// Reads the arguments for the method.
        /// </summary>
        private async Task ReadArgumentsAsync(NodeId nodeId, CancellationToken ct = default)
        {
            m_inputArguments = null;
            m_outputArguments = null;

            // build list of references to browse.
            List<BrowseDescription> nodesToBrowse = new List<BrowseDescription>();

            BrowseDescription nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = nodeId;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)NodeClass.Variable;
            nodeToBrowse.ResultMask = (uint)BrowseResultMask.BrowseName;

            nodesToBrowse.Add(nodeToBrowse);

            // find properties.
            List<ReferenceDescription> references = await ClientUtils.BrowseAsync(m_session, null, nodesToBrowse, false, ct);

            // build list of properties to read.
            List<ReadValueId> nodesToRead = new List<ReadValueId>();

            for (int ii = 0; references != null && ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];

                // ignore out of server references.
                if (reference.NodeId.IsAbsolute)
                {
                    continue;
                }

                // ignore other properties.
                if (reference.BrowseName != Opc.Ua.BrowseNames.InputArguments && reference.BrowseName != Opc.Ua.BrowseNames.OutputArguments)
                {
                    continue;
                }

                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = (NodeId)reference.NodeId;
                nodeToRead.AttributeId = Attributes.Value;
                nodeToRead.Handle = reference;
                nodesToRead.Add(nodeToRead);
            }

            // method has no arguments.
            if (nodesToRead.Count == 0)
            {
                return;
            }

            // read the arguments.
            ReadResponse response = await m_session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct);

            List<DataValue> results = response.Results.ToList();
            List<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos.ToList();

            ClientBase.ValidateResponse(results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            // save the results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                ReferenceDescription reference = (ReferenceDescription)nodesToRead[ii].Handle;

                if (StatusCode.IsGood(results[ii].StatusCode))
                {
                    if (reference.BrowseName == Opc.Ua.BrowseNames.InputArguments)
                    {
                        m_inputArguments = ExtensionObject.ToArray<Argument>(results[ii].GetValue<ExtensionObject[]>(null)).ToArray();
                    }

                    if (reference.BrowseName == Opc.Ua.BrowseNames.OutputArguments)
                    {
                        m_outputArguments = ExtensionObject.ToArray<Argument>(results[ii].GetValue<ExtensionObject[]>(null)).ToArray();
                    }
                }
            }

            // set default values for input arguments. the stack knows the
            // default for scalars; arrays start as an empty typed array.
            if (m_inputArguments != null)
            {
                foreach (Argument argument in m_inputArguments)
                {
                    #pragma warning disable CA1849 // Justification: sample keeps the existing synchronous call pattern.
                    Variant defaultValue = TypeInfo.GetDefaultVariantValue(argument.DataType, argument.ValueRank, m_session.TypeTree);

                    if (defaultValue.IsNull && argument.ValueRank >= 0)
                    {
                        BuiltInType builtInType = TypeInfo.GetBuiltInType(argument.DataType, m_session.TypeTree);
                        defaultValue = VariantElements.CreateDefault(new TypeInfo(builtInType, argument.ValueRank));
                    }
                    #pragma warning restore CA1849

                    argument.Value = defaultValue;
                }
            }
        }
        #endregion

        #region Event Handlers
        private async void EditMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                foreach (DataGridViewRow row in ResultsDV.SelectedRows)
                {
                    DataRowView source = row.DataBoundItem as DataRowView;
                    Argument argument = (Argument)source.Row[0];

                    BuiltInType builtInType = TypeInfo.GetBuiltInType(argument.DataType, m_session.TypeTree);

                    bool edited = Windows.Create<EditComplexValueDlg>().TryShowDialog(
                        new TypeInfo(builtInType, argument.ValueRank),
                        argument.Name,
                        GetArgumentValue(argument),
                        "Edit Input Argument",
                        out Variant result);

                    if (edited)
                    {
                        argument.Value = result;
                        await UpdateRowAsync(source.Row, argument, result, false);
                    }

                    break;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, exception);
            }
        }
        #endregion
    }
}
