/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Com
{
    #region ComServerStatusState Class
    #if (!OPCUA_EXCLUDE_ComServerStatusState)
    /// <summary>
    /// Stores an instance of the ComServerStatusType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class ComServerStatusState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public ComServerStatusState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.Com.ObjectTypes.ComServerStatusType, Opc.Ua.Com.Namespaces.OpcUaCom, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the instance with a node.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACoAAABodHRwOi8vb3BjZm91bmRhdGlvbi5vcmcvVUEvU0RLL0NPTUludGVyb3D/////BGCAAgEA" +
           "AAABABsAAABDb21TZXJ2ZXJTdGF0dXNUeXBlSW5zdGFuY2UBAQkAAQEJAAkAAAD/////BwAAABVgiQoC" +
           "AAAAAQAJAAAAU2VydmVyVXJsAQEKAAAuAEQKAAAAAAz/////AQH/////AAAAABVgiQoCAAAAAQAKAAAA" +
           "VmVuZG9ySW5mbwEBCwAALgBECwAAAAAM/////wEB/////wAAAAAVYIkKAgAAAAEADwAAAFNvZnR3YXJl" +
           "VmVyc2lvbgEBDAAALgBEDAAAAAAM/////wEB/////wAAAAAVYIkKAgAAAAEACwAAAFNlcnZlclN0YXRl" +
           "AQENAAAuAEQNAAAAAAz/////AQH/////AAAAABVgiQoCAAAAAQALAAAAQ3VycmVudFRpbWUBAQ4AAC4A" +
           "RA4AAAAADf////8BAf////8AAAAAFWCJCgIAAAABAAkAAABTdGFydFRpbWUBAQ8AAC4ARA8AAAAADf//" +
           "//8BAf////8AAAAAFWCJCgIAAAABAA4AAABMYXN0VXBkYXRlVGltZQEBEAAALgBEEAAAAAAN/////wEB" +
           "/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public PropertyState<string> ServerUrl
        {
            get
            {
                return m_serverUrl;
            }

            set
            {
                if (!Object.ReferenceEquals(m_serverUrl, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_serverUrl = value;
            }
        }

        /// <remarks />
        public PropertyState<string> VendorInfo
        {
            get
            {
                return m_vendorInfo;
            }

            set
            {
                if (!Object.ReferenceEquals(m_vendorInfo, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_vendorInfo = value;
            }
        }

        /// <remarks />
        public PropertyState<string> SoftwareVersion
        {
            get
            {
                return m_softwareVersion;
            }

            set
            {
                if (!Object.ReferenceEquals(m_softwareVersion, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_softwareVersion = value;
            }
        }

        /// <remarks />
        public PropertyState<string> ServerState
        {
            get
            {
                return m_serverState;
            }

            set
            {
                if (!Object.ReferenceEquals(m_serverState, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_serverState = value;
            }
        }

        /// <remarks />
        public PropertyState<DateTime> CurrentTime
        {
            get
            {
                return m_currentTime;
            }

            set
            {
                if (!Object.ReferenceEquals(m_currentTime, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_currentTime = value;
            }
        }

        /// <remarks />
        public PropertyState<DateTime> StartTime
        {
            get
            {
                return m_startTime;
            }

            set
            {
                if (!Object.ReferenceEquals(m_startTime, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_startTime = value;
            }
        }

        /// <remarks />
        public PropertyState<DateTime> LastUpdateTime
        {
            get
            {
                return m_lastUpdateTime;
            }

            set
            {
                if (!Object.ReferenceEquals(m_lastUpdateTime, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_lastUpdateTime = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Populates a list with the children that belong to the node.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="children">The list of children to populate.</param>
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_serverUrl != null)
            {
                children.Add(m_serverUrl);
            }

            if (m_vendorInfo != null)
            {
                children.Add(m_vendorInfo);
            }

            if (m_softwareVersion != null)
            {
                children.Add(m_softwareVersion);
            }

            if (m_serverState != null)
            {
                children.Add(m_serverState);
            }

            if (m_currentTime != null)
            {
                children.Add(m_currentTime);
            }

            if (m_startTime != null)
            {
                children.Add(m_startTime);
            }

            if (m_lastUpdateTime != null)
            {
                children.Add(m_lastUpdateTime);
            }

            base.GetChildren(context, children);
        }

        /// <summary>
        /// Finds the child with the specified browse name.
        /// </summary>
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
                case Opc.Ua.Com.BrowseNames.ServerUrl:
                {
                    if (createOrReplace)
                    {
                        if (ServerUrl == null)
                        {
                            if (replacement == null)
                            {
                                ServerUrl = new PropertyState<string>(this);
                            }
                            else
                            {
                                ServerUrl = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = ServerUrl;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.VendorInfo:
                {
                    if (createOrReplace)
                    {
                        if (VendorInfo == null)
                        {
                            if (replacement == null)
                            {
                                VendorInfo = new PropertyState<string>(this);
                            }
                            else
                            {
                                VendorInfo = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = VendorInfo;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.SoftwareVersion:
                {
                    if (createOrReplace)
                    {
                        if (SoftwareVersion == null)
                        {
                            if (replacement == null)
                            {
                                SoftwareVersion = new PropertyState<string>(this);
                            }
                            else
                            {
                                SoftwareVersion = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = SoftwareVersion;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.ServerState:
                {
                    if (createOrReplace)
                    {
                        if (ServerState == null)
                        {
                            if (replacement == null)
                            {
                                ServerState = new PropertyState<string>(this);
                            }
                            else
                            {
                                ServerState = (PropertyState<string>)replacement;
                            }
                        }
                    }

                    instance = ServerState;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.CurrentTime:
                {
                    if (createOrReplace)
                    {
                        if (CurrentTime == null)
                        {
                            if (replacement == null)
                            {
                                CurrentTime = new PropertyState<DateTime>(this);
                            }
                            else
                            {
                                CurrentTime = (PropertyState<DateTime>)replacement;
                            }
                        }
                    }

                    instance = CurrentTime;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.StartTime:
                {
                    if (createOrReplace)
                    {
                        if (StartTime == null)
                        {
                            if (replacement == null)
                            {
                                StartTime = new PropertyState<DateTime>(this);
                            }
                            else
                            {
                                StartTime = (PropertyState<DateTime>)replacement;
                            }
                        }
                    }

                    instance = StartTime;
                    break;
                }

                case Opc.Ua.Com.BrowseNames.LastUpdateTime:
                {
                    if (createOrReplace)
                    {
                        if (LastUpdateTime == null)
                        {
                            if (replacement == null)
                            {
                                LastUpdateTime = new PropertyState<DateTime>(this);
                            }
                            else
                            {
                                LastUpdateTime = (PropertyState<DateTime>)replacement;
                            }
                        }
                    }

                    instance = LastUpdateTime;
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
        private PropertyState<string> m_serverUrl;
        private PropertyState<string> m_vendorInfo;
        private PropertyState<string> m_softwareVersion;
        private PropertyState<string> m_serverState;
        private PropertyState<DateTime> m_currentTime;
        private PropertyState<DateTime> m_startTime;
        private PropertyState<DateTime> m_lastUpdateTime;
        #endregion
    }
    #endif
    #endregion
}