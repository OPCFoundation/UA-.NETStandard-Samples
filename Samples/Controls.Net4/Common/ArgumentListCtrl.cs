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
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class ArgumentListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        public ArgumentListCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);
        }

        #region Private Fields
        private Session m_session;
        private ITelemetryContext m_telemetry;

        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
        {
            new object[] { "Name",        HorizontalAlignment.Left, null },
            new object[] { "DataType",    HorizontalAlignment.Left, null },
            new object[] { "Value",       HorizontalAlignment.Left, null },
            new object[] { "Description", HorizontalAlignment.Left, null },
        };
        #endregion

        #region Public Interface
        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
            AdjustColumns();
        }

        /// <summary>
        /// Sets the nodes in the control.
        /// </summary>
        public async Task<bool> UpdateAsync(Session session, NodeId methodId, bool inputArgs, ITelemetryContext telemetry, CancellationToken ct = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (methodId == null) throw new ArgumentNullException(nameof(methodId));

            Clear();

            m_session = session;
            m_telemetry = telemetry;

            // find the method.
            MethodNode method = await session.NodeCache.FindAsync(methodId, ct) as MethodNode;

            if (method == null)
            {
                return false;
            }

            // select the property to find.
            QualifiedName browseName = null;

            if (inputArgs)
            {
                browseName = Opc.Ua.BrowseNames.InputArguments;
            }
            else
            {
                browseName = Opc.Ua.BrowseNames.OutputArguments;
            }

            // fetch the argument list.
            VariableNode argumentsNode = await session.NodeCache.FindAsync(methodId, ReferenceTypeIds.HasProperty, false, true, browseName, ct) as VariableNode;

            if (argumentsNode == null)
            {
                return false;
            }

            // read the value from the server.
            DataValue value = await m_session.ReadValueAsync(argumentsNode.NodeId, ct);

            ExtensionObject[] argumentsList = value.Value as ExtensionObject[];

            if (argumentsList != null)
            {
                for (int ii = 0; ii < argumentsList.Length; ii++)
                {
                    AddItem(argumentsList[ii].Body as Argument);
                }
            }

            AdjustColumns();

            return ItemsLV.Items.Count > 0;
        }

        /// <summary>
        /// Returns the argument values
        /// </summary>
        public VariantCollection GetValues()
        {
            VariantCollection values = new VariantCollection();

            foreach (ListViewItem item in ItemsLV.Items)
            {
                Argument argument = item.Tag as Argument;

                if (argument != null)
                {
                    values.Add(new Variant(argument.Value));
                }
            }

            return values;
        }

        /// <summary>
        /// Updates the argument values.
        /// </summary>
        public async Task SetValuesAsync(VariantCollection values, CancellationToken ct = default)
        {
            int ii = 0;

            foreach (ListViewItem item in ItemsLV.Items)
            {
                Argument argument = item.Tag as Argument;

                if (argument != null)
                {
                    argument.Value = values[ii++].Value;
                    await UpdateItemAsync(item, argument, ct);
                }
            }

            AdjustColumns();
        }
        #endregion

        #region Overridden Methods
        /// <see cref="BaseListCtrl.PickItems" />
        protected override void PickItems()
        {
            base.PickItems();
            EditMI_ClickAsync(this, null);
        }

        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
        {
            EditMI.Enabled = ItemsLV.SelectedItems.Count == 1;
            ClearValueMI.Enabled = EditMI.Enabled;
        }

        /// <see cref="BaseListCtrl.UpdateItemAsync" />
        protected override async Task UpdateItemAsync(ListViewItem listItem, object item, CancellationToken ct = default)
        {
            try
            {
                Argument argument = item as Argument;

                if (argument == null)
                {
                    await base.UpdateItemAsync(listItem, item, ct);
                    return;
                }

                listItem.SubItems[0].Text = String.Format("{0}", argument.Name);

                INode datatype = await m_session.NodeCache.FindAsync(argument.DataType, ct);

                if (datatype != null)
                {
                    listItem.SubItems[1].Text = String.Format("{0}", datatype);
                }
                else
                {
                    listItem.SubItems[1].Text = String.Format("{0}", argument.DataType);
                }

                if (argument.ValueRank >= ValueRanks.OneOrMoreDimensions)
                {
                    listItem.SubItems[1].Text += "[]";
                }

                if (argument.Value == null)
                {
                    argument.Value = TypeInfo.GetDefaultValue(argument.DataType, argument.ValueRank, m_session.TypeTree);

                    if (argument.Value == null)
                    {
                        Type type = m_session.MessageContext.Factory.GetSystemType(argument.DataType);

                        if (type != null)
                        {
                            if (argument.ValueRank == ValueRanks.Scalar)
                            {
                                argument.Value = new ExtensionObject(Activator.CreateInstance(type));
                            }
                            else
                            {
                                argument.Value = Array.Empty<ExtensionObject>();
                            }
                        }
                    }
                }

                listItem.SubItems[2].Text = String.Format("{0}", argument.Value);
                listItem.SubItems[3].Text = String.Format("{0}", argument.Description.Text);

                listItem.Tag = item;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
        #endregion

        private async void EditMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                Argument[] arguments = GetSelectedItems(typeof(Argument)) as Argument[];

                if (arguments == null || arguments.Length != 1)
                {
                    return;
                }

                object value = GuiUtils.EditValue(m_session, arguments[0].Value, arguments[0].DataType, arguments[0].ValueRank, m_telemetry);

                if (value != null)
                {
                    arguments[0].Value = value;
                }

                await UpdateItemAsync(ItemsLV.SelectedItems[0], arguments[0]);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private async void ClearValueMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                Argument[] arguments = GetSelectedItems(typeof(Argument)) as Argument[];

                if (arguments == null || arguments.Length != 1)
                {
                    return;
                }

                arguments[0].Value = null;

                await UpdateItemAsync(ItemsLV.SelectedItems[0], arguments[0]);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
}
