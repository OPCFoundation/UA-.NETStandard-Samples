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
using System.Text;
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Stores a type declaration retrieved from a server.
    /// </summary>
    public class TypeDeclaration
    {
        /// <summary>
        /// The node if for the type.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public NodeId NodeId;
        #pragma warning restore CA1051

        /// <summary>
        /// The fully inhierited list of instance declarations for the type.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public IList<InstanceDeclaration> Declarations;
        #pragma warning restore CA1051
    }

    /// <summary>
    /// Stores an instance declaration fetched from the server.
    /// </summary>
    public class InstanceDeclaration
    {
        /// <summary>
        /// The type that the declaration belongs to.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public NodeId RootTypeId;
        #pragma warning restore CA1051

        /// <summary>
        /// The browse path to the instance declaration.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public IList<QualifiedName> BrowsePath;
        #pragma warning restore CA1051

        /// <summary>
        /// The browse path to the instance declaration.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public string BrowsePathDisplayText;
        #pragma warning restore CA1051

        /// <summary>
        /// A localized path to the instance declaration.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public string DisplayPath;
        #pragma warning restore CA1051

        /// <summary>
        /// The node id for the instance declaration.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public NodeId NodeId;
        #pragma warning restore CA1051

        /// <summary>
        /// The node class of the instance declaration.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public NodeClass NodeClass;
        #pragma warning restore CA1051

        /// <summary>
        /// The browse name for the instance declaration.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public QualifiedName BrowseName;
        #pragma warning restore CA1051

        /// <summary>
        /// The display name for the instance declaration.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public string DisplayName;
        #pragma warning restore CA1051

        /// <summary>
        /// The description for the instance declaration.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public string Description;
        #pragma warning restore CA1051

        /// <summary>
        /// The modelling rule for the instance declaration (i.e. Mandatory or Optional).
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public NodeId ModellingRule;
        #pragma warning restore CA1051

        /// <summary>
        /// The data type for the instance declaration.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public NodeId DataType;
        #pragma warning restore CA1051

        /// <summary>
        /// The value rank for the instance declaration.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public int ValueRank;
        #pragma warning restore CA1051

        /// <summary>
        /// The built-in type parent for the data type.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public BuiltInType BuiltInType;
        #pragma warning restore CA1051

        /// <summary>
        /// A localized name for the data type.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public string DataTypeDisplayText;
        #pragma warning restore CA1051

        /// <summary>
        /// An instance declaration that has been overridden by the current instance.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public InstanceDeclaration OverriddenDeclaration;
        #pragma warning restore CA1051
    }

    /// <summary>
    /// A field in a filter declaration.
    /// </summary>
    public class FilterDeclarationField
    {
        /// <summary>
        /// Creates a new instance of a FilterDeclarationField.
        /// </summary>
        public FilterDeclarationField()
        {
            Selected = true;
            DisplayInList = false;
            FilterEnabled = false;
            FilterOperator = FilterOperator.Equals;
            FilterValue = Variant.Null;
            InstanceDeclaration = null;
        }

        /// <summary>
        /// Creates a new instance of a FilterDeclarationField.
        /// </summary>
        public FilterDeclarationField(InstanceDeclaration instanceDeclaration)
        {
            Selected = true;
            DisplayInList = false;
            FilterEnabled = false;
            FilterOperator = FilterOperator.Equals;
            FilterValue = Variant.Null;
            InstanceDeclaration = instanceDeclaration;
        }

        /// <summary>
        /// Creates a new instance of a FilterDeclarationField.
        /// </summary>
        public FilterDeclarationField(FilterDeclarationField field)
        {
            Selected = field.Selected;
            DisplayInList = field.DisplayInList;
            FilterEnabled = field.FilterEnabled;
            FilterOperator = field.FilterOperator;
            FilterValue = field.FilterValue;
            InstanceDeclaration = field.InstanceDeclaration;
        }

        /// <summary>
        /// Whether the field is returned as part of the event notification.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public bool Selected;
        #pragma warning restore CA1051

        /// <summary>
        /// Whether the field is displayed in the list view.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public bool DisplayInList;
        #pragma warning restore CA1051

        /// <summary>
        /// Whether the filter is enabled.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public bool FilterEnabled;
        #pragma warning restore CA1051

        /// <summary>
        /// The filter operator to use in the where clause.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public FilterOperator FilterOperator;
        #pragma warning restore CA1051

        /// <summary>
        /// The filter value to use in the where clause.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public Variant FilterValue;
        #pragma warning restore CA1051

        /// <summary>
        /// The instance declaration associated with the field.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public InstanceDeclaration InstanceDeclaration;
        #pragma warning restore CA1051
    }

    /// <summary>
    /// A declararion of an event filter.
    /// </summary>
    public class FilterDeclaration
    {
        /// <summary>
        /// Creates a new instance of a FilterDeclaration.
        /// </summary>
        public FilterDeclaration()
        {
            EventTypeId = Opc.Ua.ObjectTypeIds.BaseEventType;
            Fields = new List<FilterDeclarationField>();
        }

        /// <summary>
        /// Creates a new instance of a FilterDeclaration.
        /// </summary>
        public FilterDeclaration(TypeDeclaration eventType, FilterDeclaration template)
        {
            EventTypeId = eventType.NodeId;
            Fields = new List<FilterDeclarationField>();

            foreach (InstanceDeclaration instanceDeclaration in eventType.Declarations)
            {
                if (instanceDeclaration.NodeClass == NodeClass.Method)
                {
                    continue;
                }

                if ((instanceDeclaration.ModellingRule).IsNull)
                {
                    continue;
                }

                FilterDeclarationField element = new FilterDeclarationField(instanceDeclaration);
                Fields.Add(element);

                // set reasonable defaults.
                if (template == null)
                {
                    if (instanceDeclaration.RootTypeId == Opc.Ua.ObjectTypeIds.BaseEventType && instanceDeclaration.BrowseName != Opc.Ua.BrowseNames.EventId)
                    {
                        element.DisplayInList = true;
                    }
                }

                // preserve filter settings.
                else
                {
                    foreach (FilterDeclarationField field in template.Fields)
                    {
                        if (field.InstanceDeclaration.BrowsePathDisplayText == element.InstanceDeclaration.BrowsePathDisplayText)
                        {
                            element.DisplayInList = field.DisplayInList;
                            element.FilterEnabled = field.FilterEnabled;
                            element.FilterOperator = field.FilterOperator;
                            element.FilterValue = field.FilterValue;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Creates a new instance of a FilterDeclaration.
        /// </summary>
        public FilterDeclaration(FilterDeclaration declaration)
        {
            EventTypeId = declaration.EventTypeId;
            Fields = new List<FilterDeclarationField>(declaration.Fields.Count);

            for (int ii = 0; ii < declaration.Fields.Count; ii++)
            {
                Fields.Add(new FilterDeclarationField(declaration.Fields[ii]));
            }
        }

        /// <summary>
        /// Returns the event filter defined by the filter declaration.
        /// </summary>
        public EventFilter GetFilter()
        {
            EventFilter filter = new EventFilter();
            filter.SelectClauses = GetSelectClause().ToArrayOf();
            filter.WhereClause = GetWhereClause();
            return filter;
        }

        /// <summary>
        /// Adds a simple field to the declaration.
        /// </summary>
        public void AddSimpleField(QualifiedName browseName, BuiltInType dataType, bool displayInList)
        {
            AddSimpleField(new QualifiedName[] { browseName }, NodeClass.Variable, dataType, ValueRanks.Scalar, displayInList);
        }

        /// <summary>
        /// Adds a simple field to the declaration.
        /// </summary>
        public void AddSimpleField(QualifiedName browseName, BuiltInType dataType, int valueRank, bool displayInList)
        {
            AddSimpleField(new QualifiedName[] { browseName }, NodeClass.Variable, dataType, valueRank, displayInList);
        }

        /// <summary>
        /// Adds a simple field to the declaration.
        /// </summary>
        public void AddSimpleField(QualifiedName[] browseNames, BuiltInType dataType, int valueRank, bool displayInList)
        {
            AddSimpleField(browseNames, NodeClass.Variable, dataType, valueRank, displayInList);
        }

        /// <summary>
        /// Adds a simple field to the declaration.
        /// </summary>
        public void AddSimpleField(QualifiedName[] browseNames, NodeClass nodeClass, BuiltInType dataType, int valueRank, bool displayInList)
        {
            FilterDeclarationField field = new FilterDeclarationField();

            field.DisplayInList = displayInList;
            field.InstanceDeclaration = new InstanceDeclaration();
            field.InstanceDeclaration.NodeClass = nodeClass;

            if (browseNames != null)
            {
                field.InstanceDeclaration.BrowseName = browseNames[browseNames.Length - 1];
                field.InstanceDeclaration.BrowsePath = new List<QualifiedName>();

                StringBuilder path = new StringBuilder();

                for (int ii = 0; ii < browseNames.Length; ii++)
                {
                    if (path.Length > 0)
                    {
                        path.Append('/');
                    }

                    path.Append(browseNames[ii]);
                    field.InstanceDeclaration.BrowsePath.Add(browseNames[ii]);
                }

                field.InstanceDeclaration.BrowsePathDisplayText = path.ToString();
            }

            field.InstanceDeclaration.BuiltInType = dataType;
            field.InstanceDeclaration.DataType = new NodeId((uint)dataType);
            field.InstanceDeclaration.ValueRank = valueRank;
            field.InstanceDeclaration.DataTypeDisplayText = dataType.ToString();

            if (valueRank >= 0)
            {
                field.InstanceDeclaration.DataTypeDisplayText += "[]";
            }

            field.InstanceDeclaration.DisplayName = field.InstanceDeclaration.BrowseName.Name;
            field.InstanceDeclaration.DisplayPath = field.InstanceDeclaration.BrowsePathDisplayText;
            field.InstanceDeclaration.RootTypeId = ObjectTypeIds.BaseEventType;
            Fields.Add(field);
        }

        /// <summary>
        /// Returns the select clause defined by the filter declaration.
        /// </summary>
        public IList<SimpleAttributeOperand> GetSelectClause()
        {
            List<SimpleAttributeOperand> selectClause = new List<SimpleAttributeOperand>();

            SimpleAttributeOperand operand = new SimpleAttributeOperand();
            operand.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseEventType;
            operand.AttributeId = Attributes.NodeId;
            selectClause.Add(operand);

            foreach (FilterDeclarationField field in Fields)
            {
                if (field.Selected)
                {
                    operand = new SimpleAttributeOperand();
                    operand.TypeDefinitionId = field.InstanceDeclaration.RootTypeId;
                    operand.AttributeId = (field.InstanceDeclaration.NodeClass == NodeClass.Object) ? Attributes.NodeId : Attributes.Value;
                    operand.BrowsePath = field.InstanceDeclaration.BrowsePath.ToArrayOf();
                    selectClause.Add(operand);
                }
            }

            return selectClause;
        }

        /// <summary>
        /// Returns the where clause defined by the filter declaration.
        /// </summary>
        public ContentFilter GetWhereClause()
        {
            ContentFilter whereClause = new ContentFilter();
            ContentFilterElement element1 = whereClause.Push(FilterOperator.OfType, EventTypeId);

            EventFilter filter = new EventFilter();

            foreach (FilterDeclarationField field in Fields)
            {
                if (field.FilterEnabled)
                {
                    SimpleAttributeOperand operand1 = new SimpleAttributeOperand();
                    operand1.TypeDefinitionId = field.InstanceDeclaration.RootTypeId;
                    operand1.AttributeId = (field.InstanceDeclaration.NodeClass == NodeClass.Object) ? Attributes.NodeId : Attributes.Value;
                    operand1.BrowsePath = field.InstanceDeclaration.BrowsePath.ToArrayOf();

                    LiteralOperand operand2 = new LiteralOperand();
                    operand2.Value = field.FilterValue;

                    ContentFilterElement element2 = whereClause.Push(field.FilterOperator, Variant.From(new ExtensionObject(operand1)), Variant.From(new ExtensionObject(operand2)));
                    element1 = whereClause.Push(FilterOperator.And, Variant.From(new ExtensionObject(element1)), Variant.From(new ExtensionObject(element2)));
                }
            }

            return whereClause;
        }

        /// <summary>
        /// Returns the value for the specified browse name.
        /// </summary>
        public Variant GetValue(QualifiedName browseName, IList<Variant> fields)
        {
            if (fields == null || fields.Count == 0)
            {
                return Variant.Null;
            }

            if (browseName.IsNull)
            {
                browseName = QualifiedName.Null;
            }

            for (int ii = 0; ii < this.Fields.Count; ii++)
            {
                if (this.Fields[ii].InstanceDeclaration.BrowseName == browseName)
                {
                    if (ii >= fields.Count + 1)
                    {
                        return Variant.Null;
                    }

                    return fields[ii + 1];
                }
            }

            return Variant.Null;
        }

        /// <summary>
        /// The type of event.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public NodeId EventTypeId;
        #pragma warning restore CA1051

        /// <summary>
        /// The list of declarations for the fields.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        public IList<FilterDeclarationField> Fields;
        #pragma warning restore CA1051
    }
}
