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
using Opc.Ua;

namespace AggregationServer
{
    /// <summary>
    /// Stores the type information provided by the AE server.
    /// </summary>
    public class NamespaceMapper
    {
        #region Public Methods
        /// <summary>
        /// Gets the local namespace indexes.
        /// </summary>
        public int[] LocalNamespaceIndexes
        {
            get { return m_localNamespaceIndexes; }
        }

        private Array CastArray(Array source, Func<object, BuiltInType, BuiltInType, object> converter)
        {
            Type elementType = source.GetType().GetElementType() ?? typeof(object);
            Array result = Array.CreateInstance(elementType, source.Length);
            for (int ii = 0; ii < source.Length; ii++)
            {
                object mapped = converter(source.GetValue(ii), BuiltInType.Null, BuiltInType.Null);
                result.SetValue(mapped, ii);
            }
            return result;
        }

        /// <summary>
        /// Gets or sets the Uris for the Node Managers it supports e.g. "http://samples.org/UA/memorybuffer".
        /// </summary>
        public string[] TypeSystemNamespaceUris { get; set; }

        /// <summary>
        /// Initializes the mapper.
        /// </summary>
        /// <param name="localNamespaceUris"></param>
        /// <param name="remoteNamespaceUris"></param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Sample public API preserves string application URI signature.")]
        public void Initialize(StringTable localNamespaceUris, StringTable remoteNamespaceUris, string applicationUri)
        {
            m_localNamespaceIndexes = new int[remoteNamespaceUris.Count];

            for (int ii = 1; ii < remoteNamespaceUris.Count; ii++)
            {
                string namespaceUri = remoteNamespaceUris.GetString((uint)ii);

                bool isTypeSystemUri = false;

                if (TypeSystemNamespaceUris != null)
                {
                    for (int jj = 0; jj < TypeSystemNamespaceUris.Length; jj++)
                    {
                        if (TypeSystemNamespaceUris[jj] == namespaceUri)
                        {
                            isTypeSystemUri = true;
                            break;
                        }
                    }
                }

                if (!isTypeSystemUri)
                {
                    namespaceUri = applicationUri + ":" + namespaceUri;
                }

                m_localNamespaceIndexes[ii] = localNamespaceUris.GetIndexOrAppend(namespaceUri);
            }

            m_remoteNamespaceIndexes = new int[localNamespaceUris.Count];

            for (int ii = 0; ii < m_localNamespaceIndexes.Length; ii++)
            {
                if (m_remoteNamespaceIndexes.Length > m_localNamespaceIndexes[ii])
                {
                    m_remoteNamespaceIndexes[m_localNamespaceIndexes[ii]] = ii;
                }
            }
        }

        /// <summary>
        /// Converts a remote NodeId to a local NodeId.
        /// </summary>
        public NodeId ToLocalId(NodeId value)
        {
            return ToId(value, m_localNamespaceIndexes);
        }

        /// <summary>
        /// Converts a local NodeId to a remote NodeId.
        /// </summary>
        public NodeId ToRemoteId(NodeId value)
        {
            return ToId(value, m_remoteNamespaceIndexes);
        }

        /// <summary>
        /// Converts a remote ExpandedNodeId to a local ExpandedNodeId.
        /// </summary>
        public ExpandedNodeId ToLocalId(ExpandedNodeId value)
        {
            return ToId(value, m_localNamespaceIndexes);
        }

        /// <summary>
        /// Converts a local ExpandedNodeId to a remote ExpandedNodeId.
        /// </summary>
        public ExpandedNodeId ToRemoteId(ExpandedNodeId value)
        {
            return ToId(value, m_remoteNamespaceIndexes);
        }

        /// <summary>
        /// Converts a remote QualifiedName to a local QualifiedName.
        /// </summary>
        public QualifiedName ToLocalName(QualifiedName value)
        {
            return ToName(value, m_localNamespaceIndexes);
        }

        /// <summary>
        /// Converts a local QualifiedName to a remote QualifiedName.
        /// </summary>
        public QualifiedName ToRemoteName(QualifiedName value)
        {
            return ToName(value, m_remoteNamespaceIndexes);
        }

        /// <summary>
        /// Converts a remote ExtensionObject to a local ExtensionObject.
        /// </summary>
        public ExtensionObject ToLocalExtensionObject(ExtensionObject value)
        {
            return ToExtensionObject(value, m_localNamespaceIndexes);
        }

        /// <summary>
        /// Converts a local ExtensionObject to a remote ExtensionObject.
        /// </summary>
        public ExtensionObject ToRemoteExtensionObject(ExtensionObject value)
        {
            return ToExtensionObject(value, m_remoteNamespaceIndexes);
        }

        /// <summary>
        /// Converts a remote ExtensionObject to a local ExtensionObject.
        /// </summary>
        public Variant ToLocalVariant(Variant value)
        {
            return ToVariant(value, m_localNamespaceIndexes);
        }

        /// <summary>
        /// Converts a local ExtensionObject to a remote ExtensionObject.
        /// </summary>
        public Variant ToRemoteVariant(Variant value)
        {
            return ToVariant(value, m_remoteNamespaceIndexes);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Converts a remote NodeId to a local NodeId.
        /// </summary>
        private NodeId ToId(NodeId nodeId, int[] namespaceIndexes)
        {
            if ((nodeId).IsNull)
            {
                return NodeId.Null;
            }

            if (nodeId.NamespaceIndex == 0)
            {
                return nodeId;
            }

            if (namespaceIndexes == null ||
                namespaceIndexes.Length <= nodeId.NamespaceIndex)
            {
                return NodeId.Null;
            }

            if (nodeId.TryGetValue(out uint numericId))
            {
                return new NodeId(numericId, (ushort)namespaceIndexes[nodeId.NamespaceIndex]);
            }

            if (nodeId.TryGetValue(out string stringId))
            {
                return new NodeId(stringId, (ushort)namespaceIndexes[nodeId.NamespaceIndex]);
            }

            if (nodeId.TryGetValue(out Guid guidId))
            {
                return new NodeId(guidId, (ushort)namespaceIndexes[nodeId.NamespaceIndex]);
            }

            if (nodeId.TryGetValue(out ByteString byteStringId))
            {
                return new NodeId(byteStringId, (ushort)namespaceIndexes[nodeId.NamespaceIndex]);
            }

            return NodeId.Null;
        }

        /// <summary>
        /// Converts a remote ExpandedNodeId to a local ExpandedNodeId.
        /// </summary>
        private ExpandedNodeId ToId(ExpandedNodeId nodeId, int[] namespaceIndexes)
        {
            if ((nodeId).IsNull)
            {
                return NodeId.Null;
            }

            if (nodeId.NamespaceIndex == 0)
            {
                return nodeId;
            }

            if (namespaceIndexes.Length <= nodeId.NamespaceIndex)
            {
                return NodeId.Null;
            }

            if (nodeId.TryGetValue(out uint numericId))
            {
                return new ExpandedNodeId(numericId, (ushort)namespaceIndexes[nodeId.NamespaceIndex], nodeId.NamespaceUri, nodeId.ServerIndex);
            }

            if (nodeId.TryGetValue(out string stringId))
            {
                return new ExpandedNodeId(stringId, (ushort)namespaceIndexes[nodeId.NamespaceIndex], nodeId.NamespaceUri, nodeId.ServerIndex);
            }

            if (nodeId.TryGetValue(out Guid guidId))
            {
                return new ExpandedNodeId(guidId, (ushort)namespaceIndexes[nodeId.NamespaceIndex], nodeId.NamespaceUri, nodeId.ServerIndex);
            }

            if (nodeId.TryGetValue(out ByteString byteStringId))
            {
                return new ExpandedNodeId(byteStringId, (ushort)namespaceIndexes[nodeId.NamespaceIndex], nodeId.NamespaceUri, nodeId.ServerIndex);
            }

            return NodeId.Null;
        }

        /// <summary>
        /// Converts a remote QualifiedName to a local QualifiedName.
        /// </summary>
        private QualifiedName ToName(QualifiedName name, int[] namespaceIndexes)
        {
            if ((name).IsNull)
            {
                return QualifiedName.Null;
            }

            if (name.NamespaceIndex == 0)
            {
                return name;
            }

            if (namespaceIndexes.Length <= name.NamespaceIndex)
            {
                return QualifiedName.Null;
            }

            return new QualifiedName(name.Name, (ushort)namespaceIndexes[name.NamespaceIndex]);
        }

        /// <summary>
        /// Converts a remote ExtensionObject to a local ExtensionObject.
        /// </summary>
        private ExtensionObject ToExtensionObject(ExtensionObject extension, int[] namespaceIndexes)
        {
            if ((extension).IsNull)
            {
                return extension;
            }

            Argument argument = extension.TryGetValue<Argument>(out var value, ServiceMessageContext.CreateEmpty(null)) ? value : null;

            if (argument != null)
            {
                Argument argument2 = new Argument {
                    Name = argument.Name,
                    DataType = argument.DataType,
                    ValueRank = argument.ValueRank,
                    ArrayDimensions = argument.ArrayDimensions,
                    Description = argument.Description
                };
                argument2.DataType = ToId(argument.DataType, namespaceIndexes);
                return new ExtensionObject(ExpandedNodeId.Null, argument2, false);
            }

            return extension;
        }

        /// <summary>
        /// Converts a remote Variant to a local Variant.
        /// </summary>
        private Variant ToVariant(Variant value, int[] namespaceIndexes)
        {
            if (Variant.Null == value)
            {
                return Variant.Null;
            }

            TypeInfo type = value.TypeInfo;

            if (type.IsUnknown)
            {
                type = TypeInfo.Construct(value.AsBoxedObject());
            }

            if (type.IsUnknown)
            {
                return Variant.Null;
            }

            if (type.ValueRank == ValueRanks.Scalar)
            {
                switch (type.BuiltInType)
                {
                    case BuiltInType.NodeId:
                    {
                        return Variant.From(ToId((NodeId)value.AsBoxedObject(), namespaceIndexes));
                    }

                    case BuiltInType.ExpandedNodeId:
                    {
                        return Variant.From(ToId((ExpandedNodeId)value.AsBoxedObject(), namespaceIndexes));
                    }

                    case BuiltInType.QualifiedName:
                    {
                        return Variant.From(ToName((QualifiedName)value.AsBoxedObject(), namespaceIndexes));
                    }

                    case BuiltInType.ExtensionObject:
                    {
                        return Variant.From(ToExtensionObject((ExtensionObject)value.AsBoxedObject(), namespaceIndexes));
                    }
                }
            }
            else
            {
                switch (type.BuiltInType)
                {
                    case BuiltInType.NodeId:
                    case BuiltInType.ExpandedNodeId:
                    case BuiltInType.QualifiedName:
                    case BuiltInType.ExtensionObject:
                    case BuiltInType.Variant:
                    {
                        Array array = null;

                        if (Object.ReferenceEquals(m_localNamespaceIndexes, namespaceIndexes))
                        {
                            array = CastArray((Array)value.AsBoxedObject(), CastArrayToLocal);
                        }
                        else
                        {
                            array = CastArray((Array)value.AsBoxedObject(), CastArrayToRemote);
                        }

                        return type.BuiltInType switch
                        {
                            BuiltInType.NodeId => Variant.From((NodeId[])array),
                            BuiltInType.ExpandedNodeId => Variant.From((ExpandedNodeId[])array),
                            BuiltInType.QualifiedName => Variant.From((QualifiedName[])array),
                            BuiltInType.ExtensionObject => Variant.From((ExtensionObject[])array),
                            BuiltInType.Variant => Variant.From((Variant[])array),
                            _ => value
                        };
                    }
                }
            }

            return value;
        }

        /// <summary>
        /// Casts an array value to a local value.
        /// </summary>
        private object CastArrayToLocal(object source, BuiltInType srcType, BuiltInType dstType)
        {
            return MapElement(source, m_localNamespaceIndexes);
        }

        /// <summary>
        /// Casts an array value to a remote value.
        /// </summary>
        private object CastArrayToRemote(object source, BuiltInType srcType, BuiltInType dstType)
        {
            return MapElement(source, m_remoteNamespaceIndexes);
        }

        /// <summary>
        /// Maps the namespace indexes of a single array element.
        /// </summary>
        private object MapElement(object source, int[] namespaceIndexes)
        {
            switch (source)
            {
                case Variant variant: return ToVariant(variant, namespaceIndexes);
                case NodeId nodeId: return ToId(nodeId, namespaceIndexes);
                case ExpandedNodeId expandedNodeId: return ToId(expandedNodeId, namespaceIndexes);
                case QualifiedName qualifiedName: return ToName(qualifiedName, namespaceIndexes);
                case ExtensionObject extension: return ToExtensionObject(extension, namespaceIndexes);
                default: return source;
            }
        }
        #endregion

        #region Private Fields
        private int[] m_localNamespaceIndexes;
        private int[] m_remoteNamespaceIndexes;
        #endregion
    }
}
