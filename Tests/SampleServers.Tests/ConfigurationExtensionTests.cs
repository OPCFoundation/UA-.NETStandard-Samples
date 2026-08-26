/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;
using SysXmlDocument = System.Xml.XmlDocument;
using SysXmlElement = System.Xml.XmlElement;
using SysXmlNode = System.Xml.XmlNode;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Regression cover for configuration elements which nobody reads any more.
    /// </summary>
    /// <remarks>
    /// The OPC UA configuration decoder ignores what it does not recognize. A renamed or
    /// retyped configuration property therefore does not fail, it silently loses its value,
    /// and the sample only breaks later and somewhere else - the GDS samples spent a release
    /// declaring a certificate group with a <c>CertificateType</c> element after the stack had
    /// renamed it to <c>CertificateTypes</c>, and answered "Please specify at least one valid
    /// Certificate Type" at startup.
    /// <para>
    /// These tests walk the element tree of every sample configuration against the classes
    /// which decode it, and report every element no class has a member for. They live here
    /// rather than in the tier 0 project because the extension classes belong to the samples,
    /// and the sample assemblies are what makes them resolvable.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Configuration")]
    public class ConfigurationExtensionTests
    {
        /// <summary>
        /// Element names which are read by something other than a member of the class.
        /// </summary>
        /// <remarks>
        /// Keep this list short and explain every entry. An entry which is not understood is
        /// how the defect above hid in plain sight.
        /// </remarks>
        private static readonly IReadOnlyDictionary<string, string> s_knownUnmapped =
            new Dictionary<string, string>(StringComparer.Ordinal) {
                // the configuration file carries its own xml schema instance information
                ["schemaLocation"] = "xml schema annotation, not configuration",
            };

        public static IEnumerable<string> ConfigurationFiles => RepositoryLayout.EnumerateConfigurationFiles();

        [OneTimeSetUp]
        public void LoadSampleAssemblies()
        {
            // the extension classes live in the sample assemblies, which are only loaded once
            // something touches them
            foreach (string path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
            {
                try
                {
                    Assembly.LoadFrom(path);
                }
                catch (BadImageFormatException)
                {
                    // native or mixed mode libraries are of no interest here
                }
                catch (FileLoadException)
                {
                    // already loaded from somewhere else
                }
            }
        }

        /// <summary>
        /// Every element of every extension in a sample configuration has to map onto a
        /// member of the class which decodes that extension.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(ConfigurationFiles))]
        public async Task ExtensionElementsAreUnderstood(string relativePath)
        {
            var document = new SysXmlDocument();
            document.Load(RepositoryLayout.PathOf(relativePath));

            var unknown = new List<string>();
            var unresolved = new List<string>();

            foreach (SysXmlElement extension in EnumerateExtensions(document))
            {
                Type type = ResolveType(extension.LocalName);

                if (type == null)
                {
                    unresolved.Add(extension.LocalName);
                    continue;
                }

                unknown.AddRange(FindUnknownElements(extension, type, extension.LocalName));
            }

            if (unresolved.Count > 0)
            {
                await TestContext.Out.WriteLineAsync(
                    $"{relativePath}: no class found for {string.Join(", ", unresolved)}, not checked")
                    .ConfigureAwait(false);
            }

            Assert.That(
                unknown,
                Is.Empty,
                $"{relativePath} configures elements which no class reads, so their values are " +
                "silently dropped. Either the sample configuration or the class it belongs to " +
                "is out of date.");
        }

        /// <summary>
        /// The same check for the configuration itself, outside the extensions.
        /// </summary>
        [Test]
        [TestCaseSource(nameof(ConfigurationFiles))]
        public void ConfigurationElementsAreUnderstood(string relativePath)
        {
            var document = new SysXmlDocument();
            document.Load(RepositoryLayout.PathOf(relativePath));

            IEnumerable<string> unknown = FindUnknownElements(
                document.DocumentElement,
                typeof(ApplicationConfiguration),
                "ApplicationConfiguration");

            Assert.That(
                unknown,
                Is.Empty,
                $"{relativePath} configures elements which no class reads, so their values are " +
                "silently dropped. Either the sample configuration or the class it belongs to " +
                "is out of date.");
        }

        /// <summary>
        /// Guards the guard: an element which no class has a member for has to be found,
        /// otherwise the test above would pass for any configuration at all.
        /// </summary>
        [Test]
        public void UnknownElementsAreDetected()
        {
            var document = new SysXmlDocument();
            document.LoadXml(
                "<CertificateGroupConfiguration>" +
                "<Id>Default</Id>" +
                // the element the GDS samples used after the stack had renamed it
                "<CertificateType>RsaSha256ApplicationCertificateType</CertificateType>" +
                "</CertificateGroupConfiguration>");

            Type type = ResolveType("CertificateGroupConfiguration");

            Assert.That(type, Is.Not.Null, "The GDS configuration classes are not loaded, so this guard proves nothing.");

            IEnumerable<string> unknown = FindUnknownElements(document.DocumentElement, type, type.Name);

            Assert.That(
                unknown,
                Has.Some.Contains("CertificateType"),
                "A configuration element with no member behind it was not reported.");
        }

        /// <summary>
        /// The extension elements of a configuration file, which are the children of its
        /// Extensions element.
        /// </summary>
        private static IEnumerable<SysXmlElement> EnumerateExtensions(SysXmlDocument document)
        {
            foreach (SysXmlNode extensions in document.GetElementsByTagName("Extensions"))
            {
                foreach (SysXmlNode child in extensions.ChildNodes)
                {
                    if (child is not SysXmlElement element)
                    {
                        continue;
                    }

                    // an extension is wrapped in a ua:XmlElement, the configuration element
                    // itself is inside it
                    if (element.LocalName == "XmlElement")
                    {
                        foreach (SysXmlElement wrapped in element.ChildNodes.OfType<SysXmlElement>())
                        {
                            yield return wrapped;
                        }

                        continue;
                    }

                    yield return element;
                }
            }
        }

        /// <summary>
        /// Walks the element against the class which decodes it and reports what does not map.
        /// </summary>
        private static IEnumerable<string> FindUnknownElements(SysXmlElement element, Type type, string path)
        {
            foreach (SysXmlNode node in element.ChildNodes)
            {
                if (node is not SysXmlElement child)
                {
                    continue;
                }

                if (s_knownUnmapped.ContainsKey(child.LocalName))
                {
                    continue;
                }

                Type memberType = FindMemberType(type, child.LocalName);

                if (memberType == null)
                {
                    // the item elements of a collection carry the name of the item type, and
                    // the collections of the stack use the built in ua: names for them
                    if (IsCollectionItem(type, child.LocalName, out Type ownItemType))
                    {
                        foreach (string unknown in FindUnknownElements(child, ownItemType, $"{path}/{child.LocalName}"))
                        {
                            yield return unknown;
                        }

                        continue;
                    }

                    yield return $"{path}/{child.LocalName} has no member on {type.Name}";
                    continue;
                }

                if (!HasChildElements(child))
                {
                    continue;
                }

                Type itemType = ItemTypeOf(memberType);

                if (itemType != null)
                {
                    // a collection: its children are the items, each of which carries the
                    // name of its own type
                    if (IsOpaque(itemType))
                    {
                        continue;
                    }

                    foreach (SysXmlElement item in child.ChildNodes.OfType<SysXmlElement>())
                    {
                        foreach (string unknown in FindUnknownElements(
                            item,
                            itemType,
                            $"{path}/{child.LocalName}/{item.LocalName}"))
                        {
                            yield return unknown;
                        }
                    }

                    continue;
                }

                if (IsOpaque(memberType))
                {
                    continue;
                }

                foreach (string unknown in FindUnknownElements(child, memberType, $"{path}/{child.LocalName}"))
                {
                    yield return unknown;
                }
            }
        }

        /// <summary>
        /// The type behind an element name, following the name a serialization attribute
        /// gives a member, because that is what the decoder matches on.
        /// </summary>
        private static Type FindMemberType(Type type, string elementName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (SerializedNames(property).Contains(elementName, StringComparer.Ordinal))
                {
                    return property.PropertyType;
                }
            }

            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (SerializedNames(field).Contains(elementName, StringComparer.Ordinal))
                {
                    return field.FieldType;
                }
            }

            return null;
        }

        private static IEnumerable<string> SerializedNames(MemberInfo member)
        {
            yield return member.Name;

            foreach (CustomAttributeData attribute in member.GetCustomAttributesData())
            {
                foreach (CustomAttributeNamedArgument argument in attribute.NamedArguments)
                {
                    if (argument.MemberName == "Name" && argument.TypedValue.Value is string name)
                    {
                        yield return name;
                    }
                }
            }
        }

        /// <summary>
        /// True when the element is an item of the collection the parent type is.
        /// </summary>
        private static bool IsCollectionItem(Type type, string elementName, out Type itemType)
        {
            itemType = ItemTypeOf(type);

            return itemType != null
                && (itemType.Name == elementName || elementName == "String" || elementName == itemType.Name + "Collection");
        }

        private static Type ItemTypeOf(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            // ArrayOf<T> is the collection of the 2.0 stack. It is a struct which does not
            // advertise IEnumerable, so a generic type with one argument and a ToArray counts
            // as a collection too.
            if (type.IsGenericType
                && type.GetGenericArguments().Length == 1
                && (typeof(IEnumerable).IsAssignableFrom(type) || type.GetMethod("ToArray") != null))
            {
                return type.GetGenericArguments()[0];
            }

            foreach (Type contract in type.GetInterfaces())
            {
                if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return contract.GetGenericArguments()[0];
                }
            }

            return null;
        }

        /// <summary>
        /// Types whose content is not described by members: raw xml, strings and everything
        /// which decodes from the text of an element.
        /// </summary>
        private static bool IsOpaque(Type type)
        {
            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(object)
                || typeof(SysXmlNode).IsAssignableFrom(type) || type.Name.Contains("XmlElement", StringComparison.Ordinal)
                || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true;
        }

        private static bool HasChildElements(SysXmlElement element)
        {
            return element.ChildNodes.OfType<SysXmlElement>().Any();
        }

        /// <summary>
        /// Finds the class which decodes an extension, by the name of its element.
        /// </summary>
        private static Type ResolveType(string name)
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .SelectMany(GetTypes)
                .FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.Ordinal));
        }

        private static IEnumerable<Type> GetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(type => type != null);
            }
        }
    }
}
