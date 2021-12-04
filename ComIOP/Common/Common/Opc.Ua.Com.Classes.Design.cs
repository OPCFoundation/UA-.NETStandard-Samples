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

using System.Reflection;

namespace Opc.Ua.Com
{
    #region ObjectType Identifiers
    /// <summary>
    /// A class that declares constants for all ObjectTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelDesigner", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <summary>
        /// The identifier for the ComServerStatusType ObjectType.
        /// </summary>
        public const uint ComServerStatusType = 9;
    }
    #endregion

    #region Variable Identifiers
    /// <summary>
    /// A class that declares constants for all Variables in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelDesigner", "1.0.0.0")]
    public static partial class Variables
    {
        /// <summary>
        /// The identifier for the ComServerStatusType_ServerUrl Variable.
        /// </summary>
        public const uint ComServerStatusType_ServerUrl = 10;

        /// <summary>
        /// The identifier for the ComServerStatusType_VendorInfo Variable.
        /// </summary>
        public const uint ComServerStatusType_VendorInfo = 11;

        /// <summary>
        /// The identifier for the ComServerStatusType_SoftwareVersion Variable.
        /// </summary>
        public const uint ComServerStatusType_SoftwareVersion = 12;

        /// <summary>
        /// The identifier for the ComServerStatusType_ServerState Variable.
        /// </summary>
        public const uint ComServerStatusType_ServerState = 13;

        /// <summary>
        /// The identifier for the ComServerStatusType_CurrentTime Variable.
        /// </summary>
        public const uint ComServerStatusType_CurrentTime = 14;

        /// <summary>
        /// The identifier for the ComServerStatusType_StartTime Variable.
        /// </summary>
        public const uint ComServerStatusType_StartTime = 15;

        /// <summary>
        /// The identifier for the ComServerStatusType_LastUpdateTime Variable.
        /// </summary>
        public const uint ComServerStatusType_LastUpdateTime = 16;
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <summary>
    /// A class that declares constants for all ObjectTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelDesigner", "1.0.0.0")]
    public static partial class ObjectTypeIds
    {
        /// <summary>
        /// The identifier for the ComServerStatusType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType = new ExpandedNodeId(Opc.Ua.Com.ObjectTypes.ComServerStatusType, Opc.Ua.Com.Namespaces.OpcUaCom);
    }
    #endregion

    #region Variable Node Identifiers
    /// <summary>
    /// A class that declares constants for all Variables in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelDesigner", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <summary>
        /// The identifier for the ComServerStatusType_ServerUrl Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_ServerUrl = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_ServerUrl, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_VendorInfo Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_VendorInfo = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_VendorInfo, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_SoftwareVersion Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_SoftwareVersion = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_SoftwareVersion, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_ServerState Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_ServerState = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_ServerState, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_CurrentTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_CurrentTime = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_CurrentTime, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_StartTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_StartTime = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_StartTime, Opc.Ua.Com.Namespaces.OpcUaCom);

        /// <summary>
        /// The identifier for the ComServerStatusType_LastUpdateTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId ComServerStatusType_LastUpdateTime = new ExpandedNodeId(Opc.Ua.Com.Variables.ComServerStatusType_LastUpdateTime, Opc.Ua.Com.Namespaces.OpcUaCom);
    }
    #endregion

    #region BrowseName Declarations
    /// <summary>
    /// Declares all of the BrowseNames used in the Model Design.
    /// </summary>
    public static partial class BrowseNames
    {
        /// <summary>
        /// The BrowseName for the ComServerStatusType component.
        /// </summary>
        public const string ComServerStatusType = "ComServerStatusType";

        /// <summary>
        /// The BrowseName for the CurrentTime component.
        /// </summary>
        public const string CurrentTime = "CurrentTime";

        /// <summary>
        /// The BrowseName for the LastUpdateTime component.
        /// </summary>
        public const string LastUpdateTime = "LastUpdateTime";

        /// <summary>
        /// The BrowseName for the ServerState component.
        /// </summary>
        public const string ServerState = "ServerState";

        /// <summary>
        /// The BrowseName for the ServerUrl component.
        /// </summary>
        public const string ServerUrl = "ServerUrl";

        /// <summary>
        /// The BrowseName for the SoftwareVersion component.
        /// </summary>
        public const string SoftwareVersion = "SoftwareVersion";

        /// <summary>
        /// The BrowseName for the StartTime component.
        /// </summary>
        public const string StartTime = "StartTime";

        /// <summary>
        /// The BrowseName for the VendorInfo component.
        /// </summary>
        public const string VendorInfo = "VendorInfo";
    }
    #endregion

    #region Namespace Declarations
    /// <summary>
    /// Defines constants for all namespaces referenced by the model design.
    /// </summary>
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaCom namespace (.NET code namespace is 'Opc.Ua.Com').
        /// </summary>
        public const string OpcUaCom = "http://opcfoundation.org/UA/SDK/COMInterop";

        /// <summary>
        /// Returns a namespace table with all of the URIs defined.
        /// </summary>
        /// <remarks>
        /// This table is was used to create any relative paths in the model design.
        /// </remarks>
        public static NamespaceTable GetNamespaceTable()
        {
            FieldInfo[] fields = typeof(Namespaces).GetFields(BindingFlags.Public | BindingFlags.Static);

            NamespaceTable namespaceTable = new NamespaceTable();

            foreach (FieldInfo field in fields)
            {
                string namespaceUri = (string)field.GetValue(typeof(Namespaces));

                if (namespaceTable.GetIndex(namespaceUri) == -1)
                {
                    namespaceTable.Append(namespaceUri);
                }
            }

            return namespaceTable;
        }
    }
    #endregion
}
