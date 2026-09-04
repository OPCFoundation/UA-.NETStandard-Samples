/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The rule which makes the model tier possible: a client model knows nothing about the
    /// window. Nothing in a <c>Model</c> namespace of a sample client may mention a type of
    /// Windows Forms or of the shared control library, in any field, property, event,
    /// parameter or return type.
    /// </summary>
    /// <remarks>
    /// The models live in the same assembly as their window, so the compiler cannot enforce
    /// this. Reflection can, cheaply, for every client at once. The assemblies are found
    /// through their main forms, the one type of each client which is guaranteed to exist
    /// once - the generated model types exist in the server assembly as well.
    /// </remarks>
    [TestFixture]
    [Category("ClientModel")]
    public class ModelContractTests
    {
        private static readonly string[] s_forbiddenAssemblies =
        {
            "System.Windows.Forms",
            "System.Drawing",
            "Opc.Ua.ClientControls",
        };

        private static readonly Type[] s_clientMainForms =
        {
            typeof(AggregationClient.MainForm),
            typeof(Quickstarts.AlarmConditionClient.MainForm),
            typeof(Quickstarts.AliasNames.Client.MainForm),
            typeof(Quickstarts.Boiler.Client.MainForm),
            typeof(Quickstarts.DataAccessClient.MainForm),
            typeof(Quickstarts.DataTypes.MainForm),
            typeof(Quickstarts.EmptyClient.MainForm),
            typeof(Quickstarts.FileTransferClient.MainForm),
            typeof(Quickstarts.HistoricalAccess.Client.MainForm),
            typeof(Quickstarts.HistoricalEvents.Client.MainForm),
            typeof(Quickstarts.MethodsClient.MainForm),
            typeof(Quickstarts.NodeManagement.Client.MainForm),
            typeof(Quickstarts.PerfTestClient.MainForm),
            typeof(Quickstarts.RoleManagement.Client.MainForm),
            typeof(Quickstarts.SimpleEvents.Client.MainForm),
            typeof(Quickstarts.StateMachines.Client.MainForm),
            typeof(Quickstarts.UserAuthenticationClient.MainForm),
            typeof(Quickstarts.ViewsClient.MainForm),
        };

        /// <summary>
        /// Every type in a Model namespace of a sample client.
        /// </summary>
        public static IEnumerable<Type> ModelTypes()
        {
            return s_clientMainForms
                .Select(form => form.Assembly)
                .Distinct()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.Namespace != null
                    && (type.Namespace.EndsWith(".Model", StringComparison.Ordinal)
                        || type.Namespace.Contains(".Model.", StringComparison.Ordinal)))
                .OrderBy(type => type.FullName, StringComparer.Ordinal);
        }

        [Test]
        public void EveryClientHasAModel()
        {
            IEnumerable<Assembly> withoutModel = s_clientMainForms
                .Select(form => form.Assembly)
                .Where(assembly => !assembly.GetTypes().Any(type => typeof(SampleClientModel).IsAssignableFrom(type)));

            Assert.That(
                withoutModel.Select(assembly => assembly.GetName().Name),
                Is.Empty,
                "These sample clients have no client model, so their logic still lives in the window.");
        }

        [TestCaseSource(nameof(ModelTypes))]
        public void ModelKnowsNothingAboutTheWindow(Type model)
        {
            const BindingFlags everything = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            var offenders = new List<string>();

            foreach (FieldInfo field in model.GetFields(everything))
            {
                Check(offenders, $"field {field.Name}", field.FieldType);
            }

            foreach (PropertyInfo property in model.GetProperties(everything))
            {
                Check(offenders, $"property {property.Name}", property.PropertyType);
            }

            foreach (EventInfo evt in model.GetEvents(everything))
            {
                Check(offenders, $"event {evt.Name}", evt.EventHandlerType);
            }

            foreach (MethodInfo method in model.GetMethods(everything))
            {
                Check(offenders, $"return of {method.Name}", method.ReturnType);

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Check(offenders, $"parameter {parameter.Name} of {method.Name}", parameter.ParameterType);
                }
            }

            Assert.That(
                offenders,
                Is.Empty,
                $"{model.FullName} reaches into the window, which the model tier cannot drive.");
        }

        private static void Check(ICollection<string> offenders, string what, Type type)
        {
            foreach (Type seen in Unwrap(type))
            {
                string assembly = seen.Assembly.GetName().Name;

                if (s_forbiddenAssemblies.Contains(assembly, StringComparer.Ordinal))
                {
                    offenders.Add($"{what}: {seen.FullName} ({assembly})");
                }
            }
        }

        private static IEnumerable<Type> Unwrap(Type type)
        {
            if (type == null)
            {
                yield break;
            }

            if (type.HasElementType)
            {
                foreach (Type element in Unwrap(type.GetElementType()))
                {
                    yield return element;
                }

                yield break;
            }

            yield return type;

            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments().SelectMany(Unwrap))
                {
                    yield return argument;
                }
            }
        }
    }
}
