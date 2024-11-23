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
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace Quickstarts.DataTypes.Types
{
    #region DataType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypes
    {
        /// <remarks />
        public const uint EngineType = 15006;

        /// <remarks />
        public const uint VehicleType = 314;

        /// <remarks />
        public const uint CarType = 315;

        /// <remarks />
        public const uint TruckType = 316;
    }
    #endregion

    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint VehicleType_Encoding_DefaultBinary = 329;

        /// <remarks />
        public const uint CarType_Encoding_DefaultBinary = 330;

        /// <remarks />
        public const uint TruckType_Encoding_DefaultBinary = 331;

        /// <remarks />
        public const uint VehicleType_Encoding_DefaultXml = 317;

        /// <remarks />
        public const uint CarType_Encoding_DefaultXml = 318;

        /// <remarks />
        public const uint TruckType_Encoding_DefaultXml = 319;

        /// <remarks />
        public const uint VehicleType_Encoding_DefaultJson = 15003;

        /// <remarks />
        public const uint CarType_Encoding_DefaultJson = 15004;

        /// <remarks />
        public const uint TruckType_Encoding_DefaultJson = 15005;
    }
    #endregion

    #region ObjectType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <remarks />
        public const uint DriverType = 341;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <remarks />
        public const uint EngineType_EnumValues = 15007;

        /// <remarks />
        public const uint DriverType_PrimaryVehicle = 342;

        /// <remarks />
        public const uint DriverType_OwnedVehicles = 344;

        /// <remarks />
        public const uint DataTypes_BinarySchema = 302;

        /// <remarks />
        public const uint DataTypes_BinarySchema_NamespaceUri = 304;

        /// <remarks />
        public const uint DataTypes_BinarySchema_Deprecated = 15001;

        /// <remarks />
        public const uint DataTypes_BinarySchema_VehicleType = 332;

        /// <remarks />
        public const uint DataTypes_BinarySchema_CarType = 335;

        /// <remarks />
        public const uint DataTypes_BinarySchema_TruckType = 338;

        /// <remarks />
        public const uint DataTypes_XmlSchema = 287;

        /// <remarks />
        public const uint DataTypes_XmlSchema_NamespaceUri = 289;

        /// <remarks />
        public const uint DataTypes_XmlSchema_Deprecated = 15002;

        /// <remarks />
        public const uint DataTypes_XmlSchema_VehicleType = 320;

        /// <remarks />
        public const uint DataTypes_XmlSchema_CarType = 323;

        /// <remarks />
        public const uint DataTypes_XmlSchema_TruckType = 326;
    }
    #endregion

    #region DataType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId EngineType = new ExpandedNodeId(Quickstarts.DataTypes.Types.DataTypes.EngineType, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId VehicleType = new ExpandedNodeId(Quickstarts.DataTypes.Types.DataTypes.VehicleType, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId CarType = new ExpandedNodeId(Quickstarts.DataTypes.Types.DataTypes.CarType, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId TruckType = new ExpandedNodeId(Quickstarts.DataTypes.Types.DataTypes.TruckType, Quickstarts.DataTypes.Types.Namespaces.DataTypes);
    }
    #endregion

    #region Object Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId VehicleType_Encoding_DefaultBinary = new ExpandedNodeId(Quickstarts.DataTypes.Types.Objects.VehicleType_Encoding_DefaultBinary, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId CarType_Encoding_DefaultBinary = new ExpandedNodeId(Quickstarts.DataTypes.Types.Objects.CarType_Encoding_DefaultBinary, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId TruckType_Encoding_DefaultBinary = new ExpandedNodeId(Quickstarts.DataTypes.Types.Objects.TruckType_Encoding_DefaultBinary, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId VehicleType_Encoding_DefaultXml = new ExpandedNodeId(Quickstarts.DataTypes.Types.Objects.VehicleType_Encoding_DefaultXml, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId CarType_Encoding_DefaultXml = new ExpandedNodeId(Quickstarts.DataTypes.Types.Objects.CarType_Encoding_DefaultXml, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId TruckType_Encoding_DefaultXml = new ExpandedNodeId(Quickstarts.DataTypes.Types.Objects.TruckType_Encoding_DefaultXml, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId VehicleType_Encoding_DefaultJson = new ExpandedNodeId(Quickstarts.DataTypes.Types.Objects.VehicleType_Encoding_DefaultJson, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId CarType_Encoding_DefaultJson = new ExpandedNodeId(Quickstarts.DataTypes.Types.Objects.CarType_Encoding_DefaultJson, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId TruckType_Encoding_DefaultJson = new ExpandedNodeId(Quickstarts.DataTypes.Types.Objects.TruckType_Encoding_DefaultJson, Quickstarts.DataTypes.Types.Namespaces.DataTypes);
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId DriverType = new ExpandedNodeId(Quickstarts.DataTypes.Types.ObjectTypes.DriverType, Quickstarts.DataTypes.Types.Namespaces.DataTypes);
    }
    #endregion

    #region Variable Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId EngineType_EnumValues = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.EngineType_EnumValues, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DriverType_PrimaryVehicle = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DriverType_PrimaryVehicle, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DriverType_OwnedVehicles = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DriverType_OwnedVehicles, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_BinarySchema = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_BinarySchema, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_BinarySchema_NamespaceUri = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_BinarySchema_NamespaceUri, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_BinarySchema_Deprecated = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_BinarySchema_Deprecated, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_BinarySchema_VehicleType = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_BinarySchema_VehicleType, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_BinarySchema_CarType = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_BinarySchema_CarType, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_BinarySchema_TruckType = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_BinarySchema_TruckType, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_XmlSchema = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_XmlSchema, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_XmlSchema_NamespaceUri = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_XmlSchema_NamespaceUri, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_XmlSchema_Deprecated = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_XmlSchema_Deprecated, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_XmlSchema_VehicleType = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_XmlSchema_VehicleType, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_XmlSchema_CarType = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_XmlSchema_CarType, Quickstarts.DataTypes.Types.Namespaces.DataTypes);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypes_XmlSchema_TruckType = new ExpandedNodeId(Quickstarts.DataTypes.Types.Variables.DataTypes_XmlSchema_TruckType, Quickstarts.DataTypes.Types.Namespaces.DataTypes);
    }
    #endregion

    #region BrowseName Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class BrowseNames
    {
        /// <remarks />
        public const string CarType = "CarType";

        /// <remarks />
        public const string DataTypes_BinarySchema = "Quickstarts.DataTypes.Types";

        /// <remarks />
        public const string DataTypes_XmlSchema = "Quickstarts.DataTypes.Types";

        /// <remarks />
        public const string DriverType = "DriverType";

        /// <remarks />
        public const string EngineType = "EngineType";

        /// <remarks />
        public const string OwnedVehicles = "OwnedVehicles";

        /// <remarks />
        public const string PrimaryVehicle = "PrimaryVehicle";

        /// <remarks />
        public const string TruckType = "TruckType";

        /// <remarks />
        public const string VehicleType = "VehicleType";
    }
    #endregion

    #region Namespace Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaXsd namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        /// <summary>
        /// The URI for the DataTypes namespace (.NET code namespace is 'Quickstarts.DataTypes.Types').
        /// </summary>
        public const string DataTypes = "http://opcfoundation.org/UA/Quickstarts/DataTypes/Types";
    }
    #endregion
}