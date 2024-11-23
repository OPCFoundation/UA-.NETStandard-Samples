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
using Quickstarts.DataTypes.Types;
using Opc.Ua;

namespace Quickstarts.DataTypes.Instances
{
    #region ParkingLotType Enumeration
    #if (!OPCUA_EXCLUDE_ParkingLotType)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances)]
    public enum ParkingLotType
    {
        /// <remarks />
        [EnumMember(Value = "Open_1")]
        Open = 1,

        /// <remarks />
        [EnumMember(Value = "Covered_2")]
        Covered = 2,
    }

    #region ParkingLotTypeCollection Class
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfParkingLotType", Namespace = Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances, ItemName = "ParkingLotType")]
    public partial class ParkingLotTypeCollection : List<ParkingLotType>, ICloneable
    {
        #region Constructors
        /// <remarks />
        public ParkingLotTypeCollection() {}

        /// <remarks />
        public ParkingLotTypeCollection(int capacity) : base(capacity) {}

        /// <remarks />
        public ParkingLotTypeCollection(IEnumerable<ParkingLotType> collection) : base(collection) {}
        #endregion

        #region Static Operators
        /// <remarks />
        public static implicit operator ParkingLotTypeCollection(ParkingLotType[] values)
        {
            if (values != null)
            {
                return new ParkingLotTypeCollection(values);
            }

            return new ParkingLotTypeCollection();
        }

        /// <remarks />
        public static explicit operator ParkingLotType[](ParkingLotTypeCollection values)
        {
            if (values != null)
            {
                return values.ToArray();
            }

            return null;
        }
        #endregion

        #region ICloneable Methods
        /// <remarks />
        public object Clone()
        {
            return (ParkingLotTypeCollection)this.MemberwiseClone();
        }
        #endregion

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            ParkingLotTypeCollection clone = new ParkingLotTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((ParkingLotType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion

    #region TwoWheelerType Class
    #if (!OPCUA_EXCLUDE_TwoWheelerType)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances)]
    public partial class TwoWheelerType : Quickstarts.DataTypes.Types.VehicleType
    {
        #region Constructors
        /// <remarks />
        public TwoWheelerType()
        {
            Initialize();
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        private void Initialize()
        {
            m_manufacturerName = null;
        }
        #endregion

        #region Public Properties
        /// <remarks />
        [DataMember(Name = "ManufacturerName", IsRequired = false, Order = 1)]
        public string ManufacturerName
        {
            get { return m_manufacturerName;  }
            set { m_manufacturerName = value; }
        }
        #endregion

        #region IEncodeable Members
        /// <summary cref="IEncodeable.TypeId" />
        public override ExpandedNodeId TypeId => DataTypeIds.TwoWheelerType; 

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.TwoWheelerType_Encoding_DefaultBinary;

        /// <summary cref="IEncodeable.XmlEncodingId" />
        public override ExpandedNodeId XmlEncodingId => ObjectIds.TwoWheelerType_Encoding_DefaultXml;
            
        /// <summary cref="IJsonEncodeable.JsonEncodingId" />
        public override ExpandedNodeId JsonEncodingId => ObjectIds.TwoWheelerType_Encoding_DefaultJson; 

        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

            encoder.WriteString("ManufacturerName", ManufacturerName);

            encoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

            ManufacturerName = decoder.ReadString("ManufacturerName");

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            TwoWheelerType value = encodeable as TwoWheelerType;

            if (value == null)
            {
                return false;
            }

            if (!Utils.IsEqual(m_manufacturerName, value.m_manufacturerName)) return false;

            return base.IsEqual(encodeable);
        }    

        /// <summary cref="ICloneable.Clone" />
        public override object Clone()
        {
            return (TwoWheelerType)this.MemberwiseClone();
        }

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            TwoWheelerType clone = (TwoWheelerType)base.MemberwiseClone();

            clone.m_manufacturerName = (string)Utils.Clone(this.m_manufacturerName);

            return clone;
        }
        #endregion

        #region Private Fields
        private string m_manufacturerName;
        #endregion
    }

    #region TwoWheelerTypeCollection Class
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfTwoWheelerType", Namespace = Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances, ItemName = "TwoWheelerType")]
    public partial class TwoWheelerTypeCollection : List<TwoWheelerType>, ICloneable
    {
        #region Constructors
        /// <remarks />
        public TwoWheelerTypeCollection() {}

        /// <remarks />
        public TwoWheelerTypeCollection(int capacity) : base(capacity) {}

        /// <remarks />
        public TwoWheelerTypeCollection(IEnumerable<TwoWheelerType> collection) : base(collection) {}
        #endregion

        #region Static Operators
        /// <remarks />
        public static implicit operator TwoWheelerTypeCollection(TwoWheelerType[] values)
        {
            if (values != null)
            {
                return new TwoWheelerTypeCollection(values);
            }

            return new TwoWheelerTypeCollection();
        }

        /// <remarks />
        public static explicit operator TwoWheelerType[](TwoWheelerTypeCollection values)
        {
            if (values != null)
            {
                return values.ToArray();
            }

            return null;
        }
        #endregion

        #region ICloneable Methods
        /// <remarks />
        public object Clone()
        {
            return (TwoWheelerTypeCollection)this.MemberwiseClone();
        }
        #endregion

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            TwoWheelerTypeCollection clone = new TwoWheelerTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((TwoWheelerType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion

    #region BicycleType Class
    #if (!OPCUA_EXCLUDE_BicycleType)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances)]
    public partial class BicycleType : Quickstarts.DataTypes.Instances.TwoWheelerType
    {
        #region Constructors
        /// <remarks />
        public BicycleType()
        {
            Initialize();
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        private void Initialize()
        {
            m_noOfGears = (uint)0;
        }
        #endregion

        #region Public Properties
        /// <remarks />
        [DataMember(Name = "NoOfGears", IsRequired = false, Order = 1)]
        public uint NoOfGears
        {
            get { return m_noOfGears;  }
            set { m_noOfGears = value; }
        }
        #endregion

        #region IEncodeable Members
        /// <summary cref="IEncodeable.TypeId" />
        public override ExpandedNodeId TypeId => DataTypeIds.BicycleType; 

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.BicycleType_Encoding_DefaultBinary;

        /// <summary cref="IEncodeable.XmlEncodingId" />
        public override ExpandedNodeId XmlEncodingId => ObjectIds.BicycleType_Encoding_DefaultXml;
            
        /// <summary cref="IJsonEncodeable.JsonEncodingId" />
        public override ExpandedNodeId JsonEncodingId => ObjectIds.BicycleType_Encoding_DefaultJson; 

        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

            encoder.WriteUInt32("NoOfGears", NoOfGears);

            encoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

            NoOfGears = decoder.ReadUInt32("NoOfGears");

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            BicycleType value = encodeable as BicycleType;

            if (value == null)
            {
                return false;
            }

            if (!Utils.IsEqual(m_noOfGears, value.m_noOfGears)) return false;

            return base.IsEqual(encodeable);
        }    

        /// <summary cref="ICloneable.Clone" />
        public override object Clone()
        {
            return (BicycleType)this.MemberwiseClone();
        }

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            BicycleType clone = (BicycleType)base.MemberwiseClone();

            clone.m_noOfGears = (uint)Utils.Clone(this.m_noOfGears);

            return clone;
        }
        #endregion

        #region Private Fields
        private uint m_noOfGears;
        #endregion
    }

    #region BicycleTypeCollection Class
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfBicycleType", Namespace = Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances, ItemName = "BicycleType")]
    public partial class BicycleTypeCollection : List<BicycleType>, ICloneable
    {
        #region Constructors
        /// <remarks />
        public BicycleTypeCollection() {}

        /// <remarks />
        public BicycleTypeCollection(int capacity) : base(capacity) {}

        /// <remarks />
        public BicycleTypeCollection(IEnumerable<BicycleType> collection) : base(collection) {}
        #endregion

        #region Static Operators
        /// <remarks />
        public static implicit operator BicycleTypeCollection(BicycleType[] values)
        {
            if (values != null)
            {
                return new BicycleTypeCollection(values);
            }

            return new BicycleTypeCollection();
        }

        /// <remarks />
        public static explicit operator BicycleType[](BicycleTypeCollection values)
        {
            if (values != null)
            {
                return values.ToArray();
            }

            return null;
        }
        #endregion

        #region ICloneable Methods
        /// <remarks />
        public object Clone()
        {
            return (BicycleTypeCollection)this.MemberwiseClone();
        }
        #endregion

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            BicycleTypeCollection clone = new BicycleTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((BicycleType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion

    #region ScooterType Class
    #if (!OPCUA_EXCLUDE_ScooterType)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances)]
    public partial class ScooterType : Quickstarts.DataTypes.Instances.TwoWheelerType
    {
        #region Constructors
        /// <remarks />
        public ScooterType()
        {
            Initialize();
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        private void Initialize()
        {
            m_noOfSeats = (uint)0;
        }
        #endregion

        #region Public Properties
        /// <remarks />
        [DataMember(Name = "NoOfSeats", IsRequired = false, Order = 1)]
        public uint NoOfSeats
        {
            get { return m_noOfSeats;  }
            set { m_noOfSeats = value; }
        }
        #endregion

        #region IEncodeable Members
        /// <summary cref="IEncodeable.TypeId" />
        public override ExpandedNodeId TypeId => DataTypeIds.ScooterType; 

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.ScooterType_Encoding_DefaultBinary;

        /// <summary cref="IEncodeable.XmlEncodingId" />
        public override ExpandedNodeId XmlEncodingId => ObjectIds.ScooterType_Encoding_DefaultXml;
            
        /// <summary cref="IJsonEncodeable.JsonEncodingId" />
        public override ExpandedNodeId JsonEncodingId => ObjectIds.ScooterType_Encoding_DefaultJson; 

        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

            encoder.WriteUInt32("NoOfSeats", NoOfSeats);

            encoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances);

            NoOfSeats = decoder.ReadUInt32("NoOfSeats");

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            ScooterType value = encodeable as ScooterType;

            if (value == null)
            {
                return false;
            }

            if (!Utils.IsEqual(m_noOfSeats, value.m_noOfSeats)) return false;

            return base.IsEqual(encodeable);
        }    

        /// <summary cref="ICloneable.Clone" />
        public override object Clone()
        {
            return (ScooterType)this.MemberwiseClone();
        }

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            ScooterType clone = (ScooterType)base.MemberwiseClone();

            clone.m_noOfSeats = (uint)Utils.Clone(this.m_noOfSeats);

            return clone;
        }
        #endregion

        #region Private Fields
        private uint m_noOfSeats;
        #endregion
    }

    #region ScooterTypeCollection Class
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [CollectionDataContract(Name = "ListOfScooterType", Namespace = Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances, ItemName = "ScooterType")]
    public partial class ScooterTypeCollection : List<ScooterType>, ICloneable
    {
        #region Constructors
        /// <remarks />
        public ScooterTypeCollection() {}

        /// <remarks />
        public ScooterTypeCollection(int capacity) : base(capacity) {}

        /// <remarks />
        public ScooterTypeCollection(IEnumerable<ScooterType> collection) : base(collection) {}
        #endregion

        #region Static Operators
        /// <remarks />
        public static implicit operator ScooterTypeCollection(ScooterType[] values)
        {
            if (values != null)
            {
                return new ScooterTypeCollection(values);
            }

            return new ScooterTypeCollection();
        }

        /// <remarks />
        public static explicit operator ScooterType[](ScooterTypeCollection values)
        {
            if (values != null)
            {
                return values.ToArray();
            }

            return null;
        }
        #endregion

        #region ICloneable Methods
        /// <remarks />
        public object Clone()
        {
            return (ScooterTypeCollection)this.MemberwiseClone();
        }
        #endregion

        /// <summary cref="Object.MemberwiseClone" />
        public new object MemberwiseClone()
        {
            ScooterTypeCollection clone = new ScooterTypeCollection(this.Count);

            for (int ii = 0; ii < this.Count; ii++)
            {
                clone.Add((ScooterType)Utils.Clone(this[ii]));
            }

            return clone;
        }
    }
    #endregion
    #endif
    #endregion
}