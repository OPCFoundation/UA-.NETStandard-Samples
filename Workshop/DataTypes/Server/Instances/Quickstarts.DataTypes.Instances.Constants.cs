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
using Quickstarts.DataTypes.Types;
using Opc.Ua;

namespace Quickstarts.DataTypes.Instances
{
    #region DataType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypes
    {
        /// <remarks />
        public const uint ParkingLotType = 378;

        /// <remarks />
        public const uint TwoWheelerType = 15014;

        /// <remarks />
        public const uint BicycleType = 15004;

        /// <remarks />
        public const uint ScooterType = 15015;
    }
    #endregion

    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint ParkingLot = 281;

        /// <remarks />
        public const uint ParkingLot_DriverOfTheMonth = 375;

        /// <remarks />
        public const uint TwoWheelerType_Encoding_DefaultBinary = 15016;

        /// <remarks />
        public const uint BicycleType_Encoding_DefaultBinary = 15005;

        /// <remarks />
        public const uint ScooterType_Encoding_DefaultBinary = 15017;

        /// <remarks />
        public const uint TwoWheelerType_Encoding_DefaultXml = 15024;

        /// <remarks />
        public const uint BicycleType_Encoding_DefaultXml = 15009;

        /// <remarks />
        public const uint ScooterType_Encoding_DefaultXml = 15025;

        /// <remarks />
        public const uint TwoWheelerType_Encoding_DefaultJson = 15032;

        /// <remarks />
        public const uint BicycleType_Encoding_DefaultJson = 15013;

        /// <remarks />
        public const uint ScooterType_Encoding_DefaultJson = 15033;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <remarks />
        public const uint ParkingLotType_EnumValues = 15001;

        /// <remarks />
        public const uint ParkingLot_LotType = 380;

        /// <remarks />
        public const uint ParkingLot_DriverOfTheMonth_PrimaryVehicle = 376;

        /// <remarks />
        public const uint ParkingLot_DriverOfTheMonth_OwnedVehicles = 377;

        /// <remarks />
        public const uint ParkingLot_VehiclesInLot = 283;

        /// <remarks />
        public const uint DataTypeInstances_BinarySchema = 353;

        /// <remarks />
        public const uint DataTypeInstances_BinarySchema_NamespaceUri = 355;

        /// <remarks />
        public const uint DataTypeInstances_BinarySchema_Deprecated = 15002;

        /// <remarks />
        public const uint DataTypeInstances_BinarySchema_TwoWheelerType = 15018;

        /// <remarks />
        public const uint DataTypeInstances_BinarySchema_BicycleType = 15006;

        /// <remarks />
        public const uint DataTypeInstances_BinarySchema_ScooterType = 15021;

        /// <remarks />
        public const uint DataTypeInstances_XmlSchema = 341;

        /// <remarks />
        public const uint DataTypeInstances_XmlSchema_NamespaceUri = 343;

        /// <remarks />
        public const uint DataTypeInstances_XmlSchema_Deprecated = 15003;

        /// <remarks />
        public const uint DataTypeInstances_XmlSchema_TwoWheelerType = 15026;

        /// <remarks />
        public const uint DataTypeInstances_XmlSchema_BicycleType = 15010;

        /// <remarks />
        public const uint DataTypeInstances_XmlSchema_ScooterType = 15029;
    }
    #endregion

    #region DataType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId ParkingLotType = new ExpandedNodeId(Quickstarts.DataTypes.Instances.DataTypes.ParkingLotType, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId TwoWheelerType = new ExpandedNodeId(Quickstarts.DataTypes.Instances.DataTypes.TwoWheelerType, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId BicycleType = new ExpandedNodeId(Quickstarts.DataTypes.Instances.DataTypes.BicycleType, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId ScooterType = new ExpandedNodeId(Quickstarts.DataTypes.Instances.DataTypes.ScooterType, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);
    }
    #endregion

    #region Object Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId ParkingLot = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Objects.ParkingLot, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId ParkingLot_DriverOfTheMonth = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Objects.ParkingLot_DriverOfTheMonth, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId TwoWheelerType_Encoding_DefaultBinary = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Objects.TwoWheelerType_Encoding_DefaultBinary, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId BicycleType_Encoding_DefaultBinary = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Objects.BicycleType_Encoding_DefaultBinary, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId ScooterType_Encoding_DefaultBinary = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Objects.ScooterType_Encoding_DefaultBinary, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId TwoWheelerType_Encoding_DefaultXml = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Objects.TwoWheelerType_Encoding_DefaultXml, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId BicycleType_Encoding_DefaultXml = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Objects.BicycleType_Encoding_DefaultXml, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId ScooterType_Encoding_DefaultXml = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Objects.ScooterType_Encoding_DefaultXml, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId TwoWheelerType_Encoding_DefaultJson = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Objects.TwoWheelerType_Encoding_DefaultJson, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId BicycleType_Encoding_DefaultJson = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Objects.BicycleType_Encoding_DefaultJson, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId ScooterType_Encoding_DefaultJson = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Objects.ScooterType_Encoding_DefaultJson, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);
    }
    #endregion

    #region Variable Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId ParkingLotType_EnumValues = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.ParkingLotType_EnumValues, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId ParkingLot_LotType = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.ParkingLot_LotType, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId ParkingLot_DriverOfTheMonth_PrimaryVehicle = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.ParkingLot_DriverOfTheMonth_PrimaryVehicle, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId ParkingLot_DriverOfTheMonth_OwnedVehicles = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.ParkingLot_DriverOfTheMonth_OwnedVehicles, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId ParkingLot_VehiclesInLot = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.ParkingLot_VehiclesInLot, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_BinarySchema = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_BinarySchema, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_BinarySchema_NamespaceUri = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_BinarySchema_NamespaceUri, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_BinarySchema_Deprecated = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_BinarySchema_Deprecated, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_BinarySchema_TwoWheelerType = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_BinarySchema_TwoWheelerType, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_BinarySchema_BicycleType = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_BinarySchema_BicycleType, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_BinarySchema_ScooterType = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_BinarySchema_ScooterType, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_XmlSchema = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_XmlSchema, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_XmlSchema_NamespaceUri = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_XmlSchema_NamespaceUri, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_XmlSchema_Deprecated = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_XmlSchema_Deprecated, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_XmlSchema_TwoWheelerType = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_XmlSchema_TwoWheelerType, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_XmlSchema_BicycleType = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_XmlSchema_BicycleType, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

        /// <remarks />
        public static readonly ExpandedNodeId DataTypeInstances_XmlSchema_ScooterType = new ExpandedNodeId(Quickstarts.DataTypes.Instances.Variables.DataTypeInstances_XmlSchema_ScooterType, Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);
    }
    #endregion

    #region BrowseName Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class BrowseNames
    {
        /// <remarks />
        public const string BicycleType = "BicycleType";

        /// <remarks />
        public const string DataTypeInstances_BinarySchema = "Quickstarts.DataTypes.Instances";

        /// <remarks />
        public const string DataTypeInstances_XmlSchema = "Quickstarts.DataTypes.Instances";

        /// <remarks />
        public const string DriverOfTheMonth = "DriverOfTheMonth";

        /// <remarks />
        public const string LotType = "LotType";

        /// <remarks />
        public const string ParkingLot = "ParkingLot";

        /// <remarks />
        public const string ParkingLotType = "ParkingLotType";

        /// <remarks />
        public const string ScooterType = "ScooterType";

        /// <remarks />
        public const string TwoWheelerType = "TwoWheelerType";

        /// <remarks />
        public const string VehiclesInLot = "VehiclesInLot";
    }
    #endregion

    #region Namespace Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the DataTypeInstances namespace (.NET code namespace is 'Quickstarts.DataTypes.Instances').
        /// </summary>
        public const string DataTypeInstances = "http://opcfoundation.org/UA/Quickstarts/DataTypes/Instances";

        /// <summary>
        /// The URI for the DataTypes namespace (.NET code namespace is 'Quickstarts.DataTypes.Types').
        /// </summary>
        public const string DataTypes = "http://opcfoundation.org/UA/Quickstarts/DataTypes/Types";

        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaXsd namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";
    }
    #endregion
}