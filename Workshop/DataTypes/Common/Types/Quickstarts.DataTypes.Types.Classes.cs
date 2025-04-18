/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace Quickstarts.DataTypes.Types
{
    #region DriverState Class
    #if (!OPCUA_EXCLUDE_DriverState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class DriverState : BaseObjectState
    {
        #region Constructors
        /// <remarks />
        public DriverState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Quickstarts.DataTypes.Types.ObjectTypes.DriverType, Quickstarts.DataTypes.Types.Namespaces.DataTypes, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAADcAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvUXVpY2tzdGFydHMvRGF0YVR5cGVzL1R5" +
           "cGVz/////wRggAIBAAAAAQASAAAARHJpdmVyVHlwZUluc3RhbmNlAQFVAQEBVQFVAQAA/////wIAAAAV" +
           "YKkKAgAAAAEADgAAAFByaW1hcnlWZWhpY2xlAQFWAQAuAERWAQAAFgEBPgECtQAAADxDYXJUeXBlIHht" +
           "bG5zPSJodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvUXVpY2tzdGFydHMvRGF0YVR5cGVzL1R5cGVz" +
           "Ij48TWFrZT5Ub3lvdGE8L01ha2U+PE1vZGVsPlByaXVzPC9Nb2RlbD48RW5naW5lPkh5YnJpZF80PC9F" +
           "bmdpbmU+PE5vT2ZQYXNzZW5nZXJzPjQ8L05vT2ZQYXNzZW5nZXJzPjwvQ2FyVHlwZT4BAToB/////wMD" +
           "/////wAAAAAXYKkKAgAAAAEADQAAAE93bmVkVmVoaWNsZXMBAVgBAC4ARFgBAACWAwAAAAEBPwECtgAA" +
           "ADxUcnVja1R5cGUgeG1sbnM9Imh0dHA6Ly9vcGNmb3VuZGF0aW9uLm9yZy9VQS9RdWlja3N0YXJ0cy9E" +
           "YXRhVHlwZXMvVHlwZXMiPjxNYWtlPkRvZGdlPC9NYWtlPjxNb2RlbD5SYW08L01vZGVsPjxFbmdpbmU+" +
           "RGllc2VsXzI8L0VuZ2luZT48Q2FyZ29DYXBhY2l0eT41MDA8L0NhcmdvQ2FwYWNpdHk+PC9UcnVja1R5" +
           "cGU+AQE+AQIKAQAAPFZlaGljbGVUeXBlIHhzaTp0eXBlPSJDYXJUeXBlIiB4bWxuczp4c2k9Imh0dHA6" +
           "Ly93d3cudzMub3JnLzIwMDEvWE1MU2NoZW1hLWluc3RhbmNlIiB4bWxucz0iaHR0cDovL29wY2ZvdW5k" +
           "YXRpb24ub3JnL1VBL1F1aWNrc3RhcnRzL0RhdGFUeXBlcy9UeXBlcyI+PE1ha2U+UG9yc2NoZTwvTWFr" +
           "ZT48TW9kZWw+Um9hZHN0ZXI8L01vZGVsPjxFbmdpbmU+UGV0cm9sXzE8L0VuZ2luZT48Tm9PZlBhc3Nl" +
           "bmdlcnM+MjwvTm9PZlBhc3NlbmdlcnM+PC9WZWhpY2xlVHlwZT4BAT4BAgkBAAA8VmVoaWNsZVR5cGUg" +
           "eHNpOnR5cGU9IkNhclR5cGUiIHhtbG5zOnhzaT0iaHR0cDovL3d3dy53My5vcmcvMjAwMS9YTUxTY2hl" +
           "bWEtaW5zdGFuY2UiIHhtbG5zPSJodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvUXVpY2tzdGFydHMv" +
           "RGF0YVR5cGVzL1R5cGVzIj48TWFrZT5UZXNsYTwvTWFrZT48TW9kZWw+TW9kZWwgWDwvTW9kZWw+PEVu" +
           "Z2luZT5FbGVjdHJpY18zPC9FbmdpbmU+PE5vT2ZQYXNzZW5nZXJzPjQ8L05vT2ZQYXNzZW5nZXJzPjwv" +
           "VmVoaWNsZVR5cGU+AQE6AQEAAAABAAAAAAAAAAMD/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public PropertyState<VehicleType> PrimaryVehicle
        {
            get
            {
                return m_primaryVehicle;
            }

            set
            {
                if (!Object.ReferenceEquals(m_primaryVehicle, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_primaryVehicle = value;
            }
        }

        /// <remarks />
        public PropertyState<VehicleType[]> OwnedVehicles
        {
            get
            {
                return m_ownedVehicles;
            }

            set
            {
                if (!Object.ReferenceEquals(m_ownedVehicles, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_ownedVehicles = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <remarks />
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_primaryVehicle != null)
            {
                children.Add(m_primaryVehicle);
            }

            if (m_ownedVehicles != null)
            {
                children.Add(m_ownedVehicles);
            }

            base.GetChildren(context, children);
        }
            
        /// <remarks />
        protected override BaseInstanceState FindChild(
            ISystemContext context,
            QualifiedName browseName,
            bool createOrReplace,
            BaseInstanceState replacement)
        {
            if (QualifiedName.IsNull(browseName))
            {
                return null;
            }

            BaseInstanceState instance = null;

            switch (browseName.Name)
            {
                case Quickstarts.DataTypes.Types.BrowseNames.PrimaryVehicle:
                {
                    if (createOrReplace)
                    {
                        if (PrimaryVehicle == null)
                        {
                            if (replacement == null)
                            {
                                PrimaryVehicle = new PropertyState<VehicleType>(this);
                            }
                            else
                            {
                                PrimaryVehicle = (PropertyState<VehicleType>)replacement;
                            }
                        }
                    }

                    instance = PrimaryVehicle;
                    break;
                }

                case Quickstarts.DataTypes.Types.BrowseNames.OwnedVehicles:
                {
                    if (createOrReplace)
                    {
                        if (OwnedVehicles == null)
                        {
                            if (replacement == null)
                            {
                                OwnedVehicles = new PropertyState<VehicleType[]>(this);
                            }
                            else
                            {
                                OwnedVehicles = (PropertyState<VehicleType[]>)replacement;
                            }
                        }
                    }

                    instance = OwnedVehicles;
                    break;
                }
            }

            if (instance != null)
            {
                return instance;
            }

            return base.FindChild(context, browseName, createOrReplace, replacement);
        }
        #endregion

        #region Private Fields
        private PropertyState<VehicleType> m_primaryVehicle;
        private PropertyState<VehicleType[]> m_ownedVehicles;
        #endregion
    }
    #endif
    #endregion
}