/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NUnit.Framework;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Controls.Common;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 1: the Variant based value editors have to navigate and update
    /// values without boxed CLR objects.
    /// </summary>
    /// <remarks>
    /// The WinForms value editors were redesigned to navigate Variants:
    /// structure fields are captured with <see cref="VariantFieldCollection"/>
    /// (which drives IEncodeable.Encode/Decode instead of reflection) and
    /// array elements with <see cref="VariantElements"/>. These tests prove
    /// the round trips the editors rely on, plus the editor control itself
    /// editing values in its grid.
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class ValueEditorTests
    {
        private const int kTimeout = 60_000;

        #region VariantFieldCollection
        [Test]
        public void FieldCollectionCapturesArgumentFields()
        {
            var argument = new Argument("Iterations", DataTypeIds.UInt32, ValueRanks.Scalar, "The number of iterations.");

            Assert.That(VariantFieldCollection.TryCapture(argument, null, out VariantFieldCollection fields), Is.True);

            // the fields appear in encode order with their data member names.
            Assert.That(fields.Count, Is.EqualTo(5));
            Assert.That(fields.GetName(0), Is.EqualTo("Name"));
            Assert.That(fields.GetName(1), Is.EqualTo("DataType"));
            Assert.That(fields.GetName(2), Is.EqualTo("ValueRank"));
            Assert.That(fields.GetName(3), Is.EqualTo("ArrayDimensions"));
            Assert.That(fields.GetName(4), Is.EqualTo("Description"));

            Assert.That(fields.GetValue(0).GetString(), Is.EqualTo("Iterations"));
            Assert.That(fields.GetValue(1).GetNodeId(), Is.EqualTo((NodeId)DataTypeIds.UInt32));
            Assert.That(fields.GetValue(2).GetInt32(), Is.EqualTo(ValueRanks.Scalar));
            Assert.That(fields.GetSlotType(0).BuiltInType, Is.EqualTo(BuiltInType.String));
            Assert.That(fields.GetSlotType(3).ValueRank, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void FieldCollectionAppliesEditedFields()
        {
            var argument = new Argument("Iterations", DataTypeIds.UInt32, ValueRanks.Scalar, "The number of iterations.");

            Assert.That(VariantFieldCollection.TryCapture(argument, null, out VariantFieldCollection fields), Is.True);

            fields.SetValue(0, Variant.From("Renamed"));
            fields.SetValue(2, Variant.From(ValueRanks.OneDimension));

            var updated = (Argument)fields.ApplyTo(argument);

            Assert.That(updated, Is.Not.SameAs(argument));
            Assert.That(updated.Name, Is.EqualTo("Renamed"));
            Assert.That(updated.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(updated.DataType, Is.EqualTo((NodeId)DataTypeIds.UInt32));
            Assert.That(updated.Description.Text, Is.EqualTo("The number of iterations."));

            // the original instance is untouched.
            Assert.That(argument.Name, Is.EqualTo("Iterations"));
        }

        [Test]
        public void FieldCollectionConvertsToTheSlotType()
        {
            var argument = new Argument("Iterations", DataTypeIds.UInt32, ValueRanks.Scalar, null);

            Assert.That(VariantFieldCollection.TryCapture(argument, null, out VariantFieldCollection fields), Is.True);

            // a string is converted to the Int32 slot of ValueRank.
            fields.SetValue(2, Variant.From("2"));

            var updated = (Argument)fields.ApplyTo(argument);
            Assert.That(updated.ValueRank, Is.EqualTo(2));

            // a value that cannot be converted is rejected.
            Assert.Catch<Exception>(() => fields.SetValue(2, Variant.From("not a number")));
        }

        [Test]
        public void FieldCollectionRoundTripsNestedStructuresAndEnums()
        {
            var status = new ServerStatusDataType
            {
                State = ServerState.Running,
                BuildInfo = new BuildInfo
                {
                    ProductName = "Sample Server",
                    BuildNumber = "1"
                },
                SecondsTillShutdown = 30
            };

            Assert.That(VariantFieldCollection.TryCapture(status, null, out VariantFieldCollection fields), Is.True);

            int stateIndex = FindField(fields, "State");
            int buildInfoIndex = FindField(fields, "BuildInfo");

            // the enum field is captured as an enumeration.
            Assert.That(fields.GetValue(stateIndex).GetEnumeration<ServerState>(), Is.EqualTo(ServerState.Running));

            // the nested structure is captured as an extension object.
            Variant buildInfoValue = fields.GetValue(buildInfoIndex);
            Assert.That(buildInfoValue.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));

            // navigate into the nested structure and edit a field.
            var buildInfo = buildInfoValue.GetStructure<BuildInfo>();
            Assert.That(buildInfo, Is.Not.Null);
            Assert.That(VariantFieldCollection.TryCapture(buildInfo, null, out VariantFieldCollection nestedFields), Is.True);

            int productNameIndex = FindField(nestedFields, "ProductName");
            nestedFields.SetValue(productNameIndex, Variant.From("Edited Product"));

            var updatedBuildInfo = (BuildInfo)nestedFields.ApplyTo(buildInfo);

            // write the nested structure and an edited enum back into the parent.
            fields.SetValue(buildInfoIndex, Variant.FromStructure(updatedBuildInfo));
            fields.SetValue(stateIndex, Variant.From(ServerState.Shutdown));

            var updated = (ServerStatusDataType)fields.ApplyTo(status);

            Assert.That(updated.State, Is.EqualTo(ServerState.Shutdown));
            Assert.That(updated.BuildInfo.ProductName, Is.EqualTo("Edited Product"));
            Assert.That(updated.BuildInfo.BuildNumber, Is.EqualTo("1"));
            Assert.That(updated.SecondsTillShutdown, Is.EqualTo(30u));

            // the original graph is untouched.
            Assert.That(status.BuildInfo.ProductName, Is.EqualTo("Sample Server"));
            Assert.That(status.State, Is.EqualTo(ServerState.Running));
        }

        private static int FindField(VariantFieldCollection fields, string name)
        {
            for (int ii = 0; ii < fields.Count; ii++)
            {
                if (fields.GetName(ii) == name)
                {
                    return ii;
                }
            }

            Assert.Fail($"The field '{name}' was not captured.");
            return -1;
        }
        #endregion

        #region VariantElements
        [Test]
        public void ElementsRoundTripAnArray()
        {
            Variant value = Variant.From((ArrayOf<int>)new int[] { 1, 2, 3 });

            Assert.That(VariantElements.TryGetElements(value, out ArrayOf<Variant> elements, out int[] dimensions), Is.True);
            Assert.That(elements.Count, Is.EqualTo(3));
            Assert.That(dimensions, Is.EqualTo(new int[] { 3 }));
            Assert.That(elements[1].GetInt32(), Is.EqualTo(2));

            // replace an element and rebuild.
            Variant[] edited = elements.ToArray();
            edited[1] = Variant.From(42);

            Variant rebuilt = VariantElements.CreateFromElements(BuiltInType.Int32, edited, dimensions);

            Assert.That(rebuilt.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(rebuilt.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(rebuilt.GetInt32Array().ToArray(), Is.EqualTo(new int[] { 1, 42, 3 }));
        }

        [Test]
        public void ElementsRoundTripAMatrix()
        {
            var matrix = ((ArrayOf<double>)new double[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }).ToMatrix(new int[] { 2, 3 });
            Variant value = Variant.From(matrix);

            Assert.That(VariantElements.TryGetElements(value, out ArrayOf<Variant> elements, out int[] dimensions), Is.True);
            Assert.That(dimensions, Is.EqualTo(new int[] { 2, 3 }));
            Assert.That(elements.Count, Is.EqualTo(6));

            Variant[] edited = elements.ToArray();
            edited[4] = Variant.From(55.0);

            Variant rebuilt = VariantElements.CreateFromElements(BuiltInType.Double, edited, dimensions);

            Assert.That(rebuilt.TryGetValue(out MatrixOf<double> rebuiltMatrix), Is.True);
            Assert.That(rebuiltMatrix.Dimensions, Is.EqualTo(new int[] { 2, 3 }));
            Assert.That(rebuiltMatrix.Span[4], Is.EqualTo(55.0));
        }

        [Test]
        public void ElementsRoundTripAVariantArray()
        {
            Variant value = Variant.From((ArrayOf<Variant>)new Variant[] { Variant.From(1), Variant.From("two") });

            Assert.That(VariantElements.TryGetElements(value, out ArrayOf<Variant> elements, out int[] dimensions), Is.True);
            Assert.That(elements.Count, Is.EqualTo(2));
            Assert.That(elements[0].GetInt32(), Is.EqualTo(1));
            Assert.That(elements[1].GetString(), Is.EqualTo("two"));

            Variant rebuilt = VariantElements.CreateFromElements(BuiltInType.Variant, elements.ToArray(), dimensions);
            Assert.That(rebuilt.GetVariantArray().Count, Is.EqualTo(2));
        }

        [Test]
        public void ElementsRoundTripStructures()
        {
            var arguments = new Argument[]
            {
                new Argument("A", DataTypeIds.UInt32, ValueRanks.Scalar, null),
                new Argument("B", DataTypeIds.String, ValueRanks.Scalar, null)
            };

            Variant value = Variant.FromStructure((ArrayOf<Argument>)arguments);

            Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));
            Assert.That(VariantElements.TryGetElements(value, out ArrayOf<Variant> elements, out int[] dimensions), Is.True);
            Assert.That(elements.Count, Is.EqualTo(2));
            Assert.That(elements[1].GetStructure<Argument>().Name, Is.EqualTo("B"));

            Variant rebuilt = VariantElements.CreateFromElements(BuiltInType.ExtensionObject, elements.ToArray(), dimensions);
            Assert.That(rebuilt.GetStructureArray<Argument>()[0].Name, Is.EqualTo("A"));
        }

        [Test]
        public void DefaultValuesMatchTheType()
        {
            // scalars come from the stack's own defaults.
            Variant text = VariantElements.CreateDefault(TypeInfo.Scalars.String);
            Assert.That(text.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.String));
            Assert.That(VariantElements.CreateDefault(TypeInfo.Scalars.Int32).GetInt32(), Is.EqualTo(0));

            // the stack has no array defaults, so an empty typed array is created.
            Variant array = VariantElements.CreateDefault(new TypeInfo(BuiltInType.Double, ValueRanks.OneDimension));
            Assert.That(array.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(array.GetDoubleArray().Count, Is.EqualTo(0));
        }
        #endregion

        #region EditComplexValueCtrl
        [Test]
        [CancelAfter(kTimeout)]
        public async Task EditorEditsArrayElementsInTheGrid(CancellationToken ct)
        {
            await WinFormsHarness.RunAsync(
                _ =>
                {
                    using var form = new Form();
                    using var editor = new EditComplexValueCtrl();
                    editor.Dock = DockStyle.Fill;
                    form.Controls.Add(editor);
                    form.Show();

                    editor.ShowValue(TypeInfo.Arrays.Int32, "Test", Variant.From((ArrayOf<int>)new int[] { 1, 2, 3 }));
                    Application.DoEvents();

                    var grid = (DataGridView)WinFormsHarness.FindControl(editor, "ValuesDV");
                    Assert.That(grid.Rows.Count, Is.EqualTo(3));
                    Assert.That(grid.Rows[0].Cells[1].Value, Is.EqualTo("[0]"));

                    // edit the middle element in the grid.
                    grid.Rows[1].Cells[3].Value = "42";
                    Application.DoEvents();

                    Variant result = editor.GetValue();
                    Assert.That(result.GetInt32Array().ToArray(), Is.EqualTo(new int[] { 1, 42, 3 }));

                    form.Close();
                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task EditorEditsStructureFieldsInTheGrid(CancellationToken ct)
        {
            await WinFormsHarness.RunAsync(
                _ =>
                {
                    using var form = new Form();
                    using var editor = new EditComplexValueCtrl();
                    editor.Dock = DockStyle.Fill;
                    form.Controls.Add(editor);
                    form.Show();

                    var argument = new Argument("Iterations", DataTypeIds.UInt32, ValueRanks.Scalar, "The number of iterations.");

                    editor.ShowValue(TypeInfo.Scalars.ExtensionObject, "Argument", Variant.FromStructure(argument));
                    Application.DoEvents();

                    var grid = (DataGridView)WinFormsHarness.FindControl(editor, "ValuesDV");
                    Assert.That(grid.Rows.Count, Is.EqualTo(5));
                    Assert.That(grid.Rows[0].Cells[1].Value, Is.EqualTo("Name"));

                    // edit the name field in the grid.
                    grid.Rows[0].Cells[3].Value = "Renamed";
                    Application.DoEvents();

                    Variant result = editor.GetValue();
                    var updated = result.GetStructure<Argument>();

                    Assert.That(updated, Is.Not.Null);
                    Assert.That(updated.Name, Is.EqualTo("Renamed"));
                    Assert.That(updated.DataType, Is.EqualTo((NodeId)DataTypeIds.UInt32));

                    // the value handed to the editor is untouched.
                    Assert.That(argument.Name, Is.EqualTo("Iterations"));

                    form.Close();
                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(kTimeout)]
        public async Task EditorShowsDataValueComponents(CancellationToken ct)
        {
            await WinFormsHarness.RunAsync(
                async _ =>
                {
                    using var form = new Form();
                    using var editor = new EditComplexValueCtrl();
                    editor.Dock = DockStyle.Fill;
                    form.Controls.Add(editor);
                    form.Show();

                    var dataValue = new DataValue(Variant.From(3.14), StatusCodes.Good, DateTimeUtc.Now, DateTimeUtc.Now);

                    await editor.ShowValueAsync(NodeId.Null, 0, "Result", Variant.From(dataValue), true, ct).ConfigureAwait(true);
                    Application.DoEvents();

                    var grid = (DataGridView)WinFormsHarness.FindControl(editor, "ValuesDV");

                    Assert.That(grid.Rows.Count, Is.EqualTo(6));
                    Assert.That(grid.Rows[0].Cells[1].Value, Is.EqualTo("Value"));
                    Assert.That(grid.Rows[1].Cells[1].Value, Is.EqualTo("StatusCode"));

                    form.Close();
                },
                TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        #endregion

        #region DataListCtrl
        [Test]
        [CancelAfter(kTimeout)]
        public async Task DataListShowsDataValueRows(CancellationToken ct)
        {
            await WinFormsHarness.RunAsync(
                async _ =>
                {
                    using var form = new Form();
                    using var list = new DataListCtrl();
                    list.Dock = DockStyle.Fill;
                    form.Controls.Add(list);
                    form.Show();

                    var dataValue = new DataValue(
                        Variant.From((ArrayOf<int>)new int[] { 7, 8 }),
                        StatusCodes.Good,
                        DateTimeUtc.Now,
                        DateTimeUtc.Now);

                    await list.ShowValueAsync(dataValue, ct).ConfigureAwait(true);
                    Application.DoEvents();

                    var view = (ListView)WinFormsHarness.FindControl(list, "ItemsLV");

                    // Value, StatusCode, SourceTimestamp, ServerTimestamp.
                    Assert.That(view.Items.Count, Is.EqualTo(4));
                    Assert.That(view.Items[0].SubItems[0].Text, Is.EqualTo("Value"));
                    Assert.That(view.Items[0].SubItems[1].Text, Does.Contain("Int32[2]"));

                    // the edited root is available from the control.
                    Assert.That(list.GetValue().GetDataValue().WrappedValue.GetInt32Array().Count, Is.EqualTo(2));

                    form.Close();
                },
                TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        #endregion
    }
}
