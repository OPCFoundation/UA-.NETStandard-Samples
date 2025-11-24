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
    public partial class BrowseListCtrl : Opc.Ua.Client.Controls.BaseListCtrl
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public BrowseListCtrl()
        {
            InitializeComponent();
            SetColumns(m_ColumnNames);

            ItemsLV.Sorting = SortOrder.Ascending;

            m_stack = new List<ItemData>();
            m_position = -1;
        }
        #endregion

        #region Private Fields
        private ISession m_session;
        private Browser m_browser;
        private NodeId m_startId;
        private List<ItemData> m_stack;
        private int m_position;
        private event EventHandler m_PositionChanged;
        private event EventHandler m_PositionAdded;

        /// <summary>
		/// The columns to display in the control.
		/// </summary>
		private readonly object[][] m_ColumnNames = new object[][]
        {
            new object[] { "ReferenceType", HorizontalAlignment.Left, null },
            new object[] { "Node",          HorizontalAlignment.Left, null },
            new object[] { "Type",          HorizontalAlignment.Left, null },
            new object[] { "Value",         HorizontalAlignment.Left, null }
        };
        #endregion

        #region Public Interface
        /// <summary>
        /// Raised when the position was changed.
        /// </summary>
        public event EventHandler PositionChanged
        {
            add { m_PositionChanged += value; }
            remove { m_PositionChanged -= value; }
        }

        /// <summary>
        /// Raised when a new position is added to the control.
        /// </summary>
        public event EventHandler PositionAdded
        {
            add { m_PositionAdded += value; }
            remove { m_PositionAdded -= value; }
        }

        /// <summary>
        /// The current position
        /// </summary>
        [DefaultValue(-1)]
        public int Position
        {
            get { return m_position + 1; }
            set { SetPositionAsync(value - 1).GetAwaiter().GetResult(); }
        }

        /// <summary>
        /// Returns the NodeIds of the positions stored in the control.
        /// </summary>
        public ICollection<NodeId> Positions
        {
            get
            {
                List<NodeId> positions = new List<NodeId>();

                positions.Add(m_startId);

                foreach (ItemData itemData in m_stack)
                {
                    positions.Add(itemData.Target.NodeId);
                }

                return positions;
            }
        }

        /// <summary>
        /// Clears the contents of the control,
        /// </summary>
        public void Clear()
        {
            ItemsLV.Items.Clear();
            AdjustColumns();
        }

        /// <summary>
        /// Initializes the control with the session/subscription indicated.
        /// </summary>
        public async Task InitializeAsync(Browser browser, NodeId startId, CancellationToken ct = default)
        {
            m_browser = null;
            m_session = null;

            Clear();

            // nothing to do if no browser provided.
            if (browser == null)
            {
                return;
            }

            m_browser = browser;
            m_session = browser.Session as Session;
            Telemetry = m_session?.MessageContext?.Telemetry;
            m_startId = startId;
            m_position = -1;

            m_stack.Clear();

            await BrowseAsync(startId, ct);
        }

        /// <summary>
        /// Moves to the previous position.
        /// </summary>
        public async Task BackAsync(CancellationToken ct = default)
        {
            await SetPositionAsync(m_position, ct);
        }

        /// <summary>
        /// Moves to the next position.
        /// </summary>
        public async Task ForwardAsync(CancellationToken ct = default)
        {
            await SetPositionAsync(m_position + 2, ct);
        }

        /// <summary>
        /// Sets the current position.
        /// </summary>
        public async Task SetPositionAsync(int position, CancellationToken ct = default)
        {
            position--;

            if (position < 0)
            {
                position = -1;
            }

            if (position >= m_stack.Count)
            {
                position = m_stack.Count - 1;
            }

            if (m_position == position)
            {
                return;
            }

            m_position = position;

            if (m_position == -1)
            {
                await BrowseAsync(m_startId, ct);
            }
            else
            {
                await BrowseAsync(m_stack[m_position].Target.NodeId, ct);
            }

            m_PositionChanged?.Invoke(this, null);
        }

        /// <summary>
        /// Displays the target of a browse operation in the control.
        /// </summary>
        private async Task BrowseAsync(NodeId startId, CancellationToken ct = default)
        {
            if (m_browser == null || NodeId.IsNull(startId))
            {
                Clear();
                return;
            }

            List<ItemData> variables = new List<ItemData>();

            // browse the references from the node and build list of variables.
            BeginUpdate();

            foreach (ReferenceDescription reference in await m_browser.BrowseAsync(startId, ct))
            {
                Node target = await m_session.NodeCache.FindAsync(reference.NodeId, ct) as Node;

                if (target == null)
                {
                    continue;
                }

                ReferenceTypeNode referenceType = await m_session.NodeCache.FindAsync(reference.ReferenceTypeId, ct) as ReferenceTypeNode;

                Node typeDefinition = null;

                if ((target.NodeClass & (NodeClass.Variable | NodeClass.Object)) != 0)
                {
                    typeDefinition = await m_session.NodeCache.FindAsync(reference.TypeDefinition, ct) as Node;
                }
                else
                {
                    typeDefinition = await m_session.NodeCache.FindAsync(await m_session.NodeCache.TypeTree.FindSuperTypeAsync(target.NodeId, ct), ct) as Node;
                }

                ItemData item = new ItemData(referenceType, !reference.IsForward, target, typeDefinition);
                AddItem(item, await GuiUtils.GetTargetIconAsync(m_browser.Session as Session, reference, ct), -1);

                if ((target.NodeClass & (NodeClass.Variable | NodeClass.VariableType)) != 0)
                {
                    variables.Add(item);
                }
            }

            EndUpdate();

            // read the current value for any variables.
            if (variables.Count > 0)
            {
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

                foreach (ItemData item in variables)
                {
                    ReadValueId valueId = new ReadValueId();

                    valueId.NodeId = item.Target.NodeId;
                    valueId.AttributeId = Attributes.Value;
                    valueId.IndexRange = null;
                    valueId.DataEncoding = null;

                    nodesToRead.Add(valueId);
                }

                ReadResponse response = await m_session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    ct);

                DataValueCollection values = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                ClientBase.ValidateResponse(values, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                for (int ii = 0; ii < variables.Count; ii++)
                {
                    variables[ii].Value = values[ii];

                    foreach (ListViewItem item in ItemsLV.Items)
                    {
                        if (Object.ReferenceEquals(item.Tag, variables[ii]))
                        {
                            await UpdateItemAsync(item, variables[ii], ct);
                            break;
                        }
                    }
                }
            }

            AdjustColumns();
        }
        #endregion

        #region ItemData Class
        /// <summary>
        /// Stores the data associated with a list view item.
        /// </summary>
        private sealed class ItemData : IComparable
        {
            public ReferenceTypeNode ReferenceType;
            public bool IsInverse;
            public Node Target;
            public Node TypeDefinition;
            public DataValue Value;
            public string SortKey;

            public ItemData(ReferenceTypeNode referenceType, bool isInverse, Node target, Node typeDefinition)
            {
                ReferenceType = referenceType;
                IsInverse = isInverse;
                Target = target;
                TypeDefinition = typeDefinition;
            }

            #region IComparable Members
            /// <summary>
            /// Compares the obj.
            /// </summary>
            public int CompareTo(object obj)
            {
                ItemData target = obj as ItemData;

                if (Object.ReferenceEquals(target, null))
                {
                    return -1;
                }

                if (Object.ReferenceEquals(target, this))
                {
                    return 0;
                }

                return this.SortKey.CompareTo(target.SortKey);
            }
            #endregion
        }
        #endregion

        #region Overridden Methods
        /// <see cref="BaseListCtrl.EnableMenuItems" />
		protected override void EnableMenuItems(ListViewItem clickedItem)
        {
            // TBD
        }

        /// <summary>
        /// Handles a double click.
        /// </summary>
        protected override async void PickItems()
        {
            try
            {
                if (ItemsLV.SelectedItems.Count <= 0)
                {
                    return;
                }

                ItemData itemData = ItemsLV.SelectedItems[0].Tag as ItemData;

                if (itemData == null)
                {
                    return;
                }

                base.PickItems();

                if (m_position >= 0 && m_position < m_stack.Count - 1)
                {
                    m_stack.RemoveRange(m_position, m_stack.Count - m_position);
                }
                else if (m_position == -1)
                {
                    m_stack.Clear();
                }

                m_position++;
                m_stack.Add(itemData);

                m_PositionAdded?.Invoke(this, null);

                await BrowseAsync(itemData.Target.NodeId);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(m_session?.MessageContext?.Telemetry, this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        /// <see cref="BaseListCtrl.UpdateItemAsync" />
        protected override async Task UpdateItemAsync(ListViewItem listItem, object item, CancellationToken ct = default)
        {
            ItemData itemData = item as ItemData;

            if (itemData == null)
            {
                await base.UpdateItemAsync(listItem, item, ct);
                return;
            }

            if (itemData.ReferenceType != null)
            {
                if (itemData.IsInverse)
                {
                    listItem.SubItems[0].Text = String.Format("{0}", itemData.ReferenceType.InverseName);
                }
                else
                {
                    listItem.SubItems[0].Text = String.Format("{0}", itemData.ReferenceType.DisplayName);
                }
            }
            else
            {
                listItem.SubItems[0].Text = "(unknown)";
            }

            listItem.SubItems[1].Text = String.Format("{0}", itemData.Target);
            listItem.SubItems[2].Text = String.Format("{0}", itemData.TypeDefinition);

            if (itemData.Value != null)
            {
                listItem.SubItems[3].Text = String.Format("{0}", itemData.Value);
            }
            else
            {
                listItem.SubItems[3].Text = String.Empty;
            }

            itemData.SortKey = String.Format("{0}{1}", listItem.SubItems[0].Text, listItem.SubItems[1].Text);

            listItem.Tag = item;
        }
        #endregion
    }
}
