/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Windows.Forms;
using Opc.Ua.Schema;
using Quickstarts.DataTypes.Model;
using Opc.Ua.Samples.WinForms;

namespace Quickstarts.DataTypes
{
    /// <summary>
    /// Shows the schema of one of the data types of the server, in any of the encodings
    /// the stack can generate.
    /// </summary>
    /// <remarks>
    /// The dialog does nothing but call
    /// <see cref="DataTypesClientModel.CreateSchema"/> and put the result in a text box.
    /// What is worth looking at is the list of types: the ones marked <i>compiled</i> came
    /// out of a generated class of the shared DataTypes Library, the rest out of the
    /// <c>DataTypeDefinition</c> Attribute the server sent, and the documents are produced
    /// the same way either way.
    /// </remarks>
    public partial class SchemaDlg : SampleForm
    {
        /// <summary>
        /// The formats the dialog offers, with the labels it shows for them.
        /// </summary>
        private static readonly (string Label, UaSchemaFormat Format)[] s_formats = [
            ("XML Schema (XSD)", UaSchemaFormat.Xsd),
            ("OPC Binary (BSD)", UaSchemaFormat.Bsd),
            ("JSON Schema, compact", UaSchemaFormat.JsonCompact),
            ("JSON Schema, verbose", UaSchemaFormat.JsonVerbose),
        ];

        private DataTypesClientModel m_model;

        /// <summary>
        /// Creates the dialog.
        /// </summary>
        public SchemaDlg()
        {
            InitializeComponent();

            foreach ((string label, UaSchemaFormat _) in s_formats)
            {
                FormatCB.Items.Add(label);
            }

            FormatCB.SelectedIndex = 0;
        }

        /// <summary>
        /// Shows the schemas of the data types the model found.
        /// </summary>
        /// <param name="model">The attached client model.</param>
        public void ShowDialog(DataTypesClientModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            m_model = model;

            TypeCB.Items.Clear();

            foreach (SchemaDataType type in model.DataTypes)
            {
                TypeCB.Items.Add(type);
            }

            if (TypeCB.Items.Count > 0)
            {
                TypeCB.SelectedIndex = 0;
            }
            else
            {
                SchemaTB.Text = "The server declares no data types outside the standard address space.";
            }

            ShowDialog();
        }

        /// <summary>
        /// Regenerates the schema whenever the type, the format or the scope changes.
        /// </summary>
        private void Selection_Changed(object sender, EventArgs e)
        {
            if (m_model == null || TypeCB.SelectedItem is not SchemaDataType type)
            {
                return;
            }

            SourceLB.Text = type.FromCompiledType
                ? $"{type.Namespace} - the definition came out of the generated class this client compiled."
                : $"{type.Namespace} - the definition came off the wire, as the DataTypeDefinition Attribute of the node.";

            try
            {
                UaSchemaFormat format = s_formats[Math.Max(FormatCB.SelectedIndex, 0)].Format;

                UaSchemaScope scope = NamespaceScopeCK.Checked
                    ? UaSchemaScope.Namespace
                    : UaSchemaScope.Type;

                SchemaTB.Text = m_model.CreateSchema(type, format, scope)
                    .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
            }
            catch (Exception exception)
            {
                SchemaTB.Text = exception.Message;
            }
        }
    }
}
