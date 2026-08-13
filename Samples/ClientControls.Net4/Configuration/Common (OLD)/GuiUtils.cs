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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A class that provide various common utility functions and shared resources.
    /// </summary>
    public partial class GuiUtils : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GuiUtils"/> class.
        /// </summary>
        public GuiUtils()
        {
            InitializeComponent();
        }

        /// <summary>
        /// The list of icon images.
        /// </summary>
        #pragma warning disable CA1051 // Justification: sample public API shape is preserved by design.
        #pragma warning disable CA2213 // Justification: WinForms designer/owner lifetime manages this sample field.
        public System.Windows.Forms.ImageList ImageList;
        #pragma warning restore CA1051
        #pragma warning restore CA2213

        /// <summary>
        /// Displays the details of an exception.
        /// </summary>
        public static void HandleException(ITelemetryContext telemetry, string caption, MethodBase method, Exception e)
        {
            if (String.IsNullOrEmpty(caption))
            {
                caption = method.Name;
            }

            ExceptionDlg.Show(telemetry, caption, e);
        }

        /// <summary>
        /// Displays the details of an exception.
        /// </summary>
        public static void HandleException(ILogger logger, string caption, MethodBase method, Exception e)
        {
            if (String.IsNullOrEmpty(caption))
            {
                caption = method.Name;
            }

            ExceptionDlg.Show(logger, caption, e);
        }

        /// <summary>
        /// Defines names for the available 16x16 icons.
        /// </summary>
        #pragma warning disable CA1034 // Justification: sample public API shape is preserved by design.
        public static class Icons
        #pragma warning restore CA1034
        {
            /// <summary>
            /// An attribute
            /// </summary>
            public const string Attribute = "SimpleItem";

            /// <summary>
            /// A property
            /// </summary>
            public const string Property = "Property";

            /// <summary>
            /// A variable
            /// </summary>
            public const string Variable = "Variable";

            /// <summary>
            /// An object
            /// </summary>
            #pragma warning disable CA1720 // Justification: sample public API shape is preserved by design.
            public const string Object = "Object";
            #pragma warning restore CA1720

            /// <summary>
            /// A method
            /// </summary>
            public const string Method = "Method";

            /// <summary>
            /// A single computer.
            /// </summary>
            public const string Computer = "Computer";

            /// <summary>
            /// A computer network.
            /// </summary>
            public const string Network = "Network";

            /// <summary>
            /// A folder.
            /// </summary>
            public const string Folder = "Folder";

            /// <summary>
            /// A selected folder.
            /// </summary>
            public const string SelectedFolder = "SelectedFolder";

            /// <summary>
            /// A process or application.
            /// </summary>
            public const string Process = "Process";

            /// <summary>
            /// A certificate
            /// </summary>
            public const string Certificate = "Certificate";

            /// <summary>
            /// An invalid certificate
            /// </summary>
            public const string InvalidCertificate = "InvalidCertificate";

            /// <summary>
            /// A certificate store
            /// </summary>
            public const string CertificateStore = "CertificateStore";

            /// <summary>
            /// A group of users.
            /// </summary>
            public const string Users = "Users";

            /// <summary>
            /// A service.
            /// </summary>
            public const string Service = "Service";

            /// <summary>
            /// A logical drive.
            /// </summary>
            public const string Drive = "Drive";

            /// <summary>
            /// The computer desktop.
            /// </summary>
            public const string Desktop = "Desktop";

            /// <summary>
            /// A single user.
            /// </summary>
            public const string SingleUser = "SingleUser";

            /// <summary>
            /// A group of services.
            /// </summary>
            public const string ServiceGroup = "ServiceGroup";

            /// <summary>
            /// A group of users.
            /// </summary>
            public const string UserGroup = "UserGroup";

            /// <summary>
            /// A green check
            /// </summary>
            public const string GreenCheck = "GreenCheck";

            /// <summary>
            /// A red cross
            /// </summary>
            public const string RedCross = "RedCross";

            /// <summary>
            /// A users icon with a red cross through it.
            /// </summary>
            public const string UsersRedCross = "UsersRedCross";
        }

        /// <summary>
        /// Uses the command line to override the UA TCP implementation specified in the configuration.
        /// </summary>
        /// <param name="configuration">The configuration instance that stores the configurable information for a UA application.
        /// </param>
        public static void OverrideUaTcpImplementation(ApplicationConfiguration configuration)
        {
            // check if UA TCP configuration included.
            TransportConfiguration transport = null;

            for (int ii = 0; ii < configuration.TransportConfigurations.Count; ii++)
            {
                if (configuration.TransportConfigurations[ii].UriScheme == Utils.UriSchemeOpcTcp)
                {
                    transport = configuration.TransportConfigurations[ii];
                    break;
                }
            }
        }

        /// <summary>
        /// Displays the UA-TCP configuration in the form.
        /// </summary>
        /// <param name="form">The form to display the UA-TCP configuration.</param>
        /// <param name="configuration">The configuration instance that stores the configurable information for a UA application.</param>
        public static void DisplayUaTcpImplementation(Form form, ApplicationConfiguration configuration)
        {
            // check if UA TCP configuration included.
            TransportConfiguration transport = null;

            for (int ii = 0; ii < configuration.TransportConfigurations.Count; ii++)
            {
                if (configuration.TransportConfigurations[ii].UriScheme == Utils.UriSchemeOpcTcp)
                {
                    transport = configuration.TransportConfigurations[ii];
                    break;
                }
            }

            // check if UA TCP implementation explicitly specified.
            if (transport != null)
            {
                string text = form.Text;

                int index = text.LastIndexOf("(UA TCP - ", StringComparison.Ordinal);

                if (index >= 0)
                {
                    text = text.Substring(0, index);
                }

                form.Text = Utils.Format("{0} (UA TCP - C#)", text);
            }
        }

        /// <summary>
        /// Handles a domain validation error.
        /// </summary>
        /// <param name="caption">The caller's text is used as the caption of the <see cref="MessageBox"/> shown to provide details about the error.</param>
        public static bool HandleDomainCheckError(string caption, ServiceResult serviceResult, X509Certificate2 certificate = null)
        {
            StringBuilder buffer = new StringBuilder();
            buffer.AppendFormat("Certificate could not be validated!\r\n");
            buffer.AppendFormat("Validation error(s): \r\n");
            buffer.AppendFormat("\t{0}\r\n", serviceResult.StatusCode);
            if (certificate != null)
            {
                buffer.AppendFormat("\r\nSubject: {0}\r\n", certificate.Subject);
                buffer.AppendFormat("Issuer: {0}\r\n", X509Utils.CompareDistinguishedName(certificate.Subject, certificate.Issuer)
                    ? "Self-signed" : certificate.Issuer);
                buffer.AppendFormat("Valid From: {0}\r\n", certificate.NotBefore);
                buffer.AppendFormat("Valid To: {0}\r\n", certificate.NotAfter);
                buffer.AppendFormat("Thumbprint: {0}\r\n\r\n", certificate.Thumbprint);
                var domains = X509Utils.GetDomainsFromCertificate(Opc.Ua.Security.Certificates.Certificate.From(certificate));
                if (domains.Count > 0)
                {
                    bool comma = false;
                    buffer.AppendFormat("Domains:");
                    foreach (var domain in domains)
                    {
                        if (comma)
                        {
                            buffer.Append(',');
                        }
                        buffer.AppendFormat(" {0}", domain);
                        comma = true;
                    }
                    buffer.AppendLine();
                }
            }
            buffer.Append("This certificate validation error indicates that the hostname used to connect");
            buffer.Append(" is not listed as a valid hostname in the server certificate.");
            buffer.Append("\r\n\r\nRetry with disabled hostname verification?");

            if (MessageBox.Show(buffer.ToString(), caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        /// <param name="form">The caller's form is used as the caption of the <see cref="MessageBox"/> shown to provide details about the error.</param>
        /// <param name="certificate">The certificate that failed validation.</param>
        /// <param name="error">The <see cref="ServiceResult"/> describing the validation error(s).</param>
        /// <returns><c>true</c> if the user chose to accept the certificate anyway; otherwise <c>false</c>.</returns>
        public static bool HandleCertificateValidationError(Form form, Opc.Ua.Security.Certificates.Certificate certificate, ServiceResult error)
        {
            return HandleCertificateValidationError(form.Text, certificate, error);
        }

        /// <summary>
        /// Handles a certificate validation error.
        /// </summary>
        /// <param name="caption">The caller's text is used as the caption of the <see cref="MessageBox"/> shown to provide details about the error.</param>
        /// <param name="certificate">The certificate that failed validation.</param>
        /// <param name="error">The <see cref="ServiceResult"/> describing the validation error(s).</param>
        /// <returns><c>true</c> if the user chose to accept the certificate anyway; otherwise <c>false</c>.</returns>
        public static bool HandleCertificateValidationError(string caption, Opc.Ua.Security.Certificates.Certificate certificate, ServiceResult error)
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append("Certificate could not be validated!\r\n");
            buffer.Append("Validation error(s): \r\n");
            ServiceResult current = error;
            while (current != null)
            {
                buffer.AppendFormat("- {0}\r\n", current.ToString().Split('\r', '\n').FirstOrDefault());
                current = current.InnerResult;
            }
            buffer.AppendFormat("\r\nSubject: {0}\r\n", certificate.Subject);
            buffer.AppendFormat("Issuer: {0}\r\n", X509Utils.CompareDistinguishedName(certificate.Subject, certificate.Issuer) ? "Self-signed" : certificate.Issuer);
            buffer.AppendFormat("Valid From: {0}\r\n", certificate.NotBefore);
            buffer.AppendFormat("Valid To: {0}\r\n", certificate.NotAfter);
            buffer.AppendFormat("Thumbprint: {0}\r\n\r\n", certificate.Thumbprint);
            buffer.Append("Certificate validation errors may indicate an attempt to intercept any data you send ");
            buffer.Append("to a server or to allow an untrusted client to connect to your server.");
            buffer.Append("\r\n\r\nAccept anyway?");

            return MessageBox.Show(buffer.ToString(), caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        /// <summary>
        /// Returns a default value for the data type.
        /// </summary>
        public static object GetDefaultValue(NodeId datatypeId, int valueRank)
        {
            Type type = TypeInfo.GetSystemType(datatypeId, EncodeableFactory.Create())?.Type;

            if (type == null)
            {
                return null;
            }

            if (valueRank < 0)
            {
                if (type == typeof(String))
                {
                    return System.String.Empty;
                }

                if (type == typeof(byte[]))
                {
                    return Array.Empty<byte>();
                }

                if (type == typeof(NodeId))
                {
                    return Opc.Ua.NodeId.Null;
                }

                if (type == typeof(ExpandedNodeId))
                {
                    return Opc.Ua.ExpandedNodeId.Null;
                }

                if (type == typeof(QualifiedName))
                {
                    return Opc.Ua.QualifiedName.Null;
                }

                if (type == typeof(LocalizedText))
                {
                    return Opc.Ua.LocalizedText.Null;
                }

                if (type == typeof(Guid))
                {
                    return System.Guid.Empty;
                }

                if (type == typeof(System.Xml.XmlElement))
                {
                    System.Xml.XmlDocument document = new System.Xml.XmlDocument { XmlResolver = null };
                    using XmlReader reader = XmlReader.Create("<Null/>", new XmlReaderSettings() { XmlResolver = null });
                    document.Load(reader);
                    return document.DocumentElement;
                }

                return Activator.CreateInstance(type);
            }

            return Array.CreateInstance(type, new int[valueRank]);
        }

        /// <summary>
        /// Displays a dialog that allows a use to edit a value.
        /// </summary>
        public static object EditValue(Session session, object value, ITelemetryContext telemetry)
        {
            TypeInfo typeInfo = TypeInfo.Construct(value);

            if (!typeInfo.IsUnknown)
            {
                return EditValue(session, value, new NodeId((uint)typeInfo.BuiltInType), typeInfo.ValueRank, telemetry);
            }

            return null;
        }

        /// <summary>
        /// Displays a dialog that allows a use to edit a value.
        /// </summary>
        public static object EditValue(Session session, object value, NodeId datatypeId, int valueRank, ITelemetryContext telemetry)
        {
            if (value == null)
            {
                value = GetDefaultValue(datatypeId, valueRank);
            }

            if (valueRank >= 0)
            {
                #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                return new ComplexValueEditDlg().ShowDialog(value, telemetry);
                #pragma warning restore CA2000
            }

            BuiltInType builtinType = TypeInfo.GetBuiltInType(datatypeId, session.TypeTree);

            switch (builtinType)
            {
                case BuiltInType.Boolean:
                case BuiltInType.Byte:
                case BuiltInType.SByte:
                case BuiltInType.Int16:
                case BuiltInType.UInt16:
                case BuiltInType.Int32:
                case BuiltInType.UInt32:
                case BuiltInType.Int64:
                case BuiltInType.UInt64:
                case BuiltInType.Float:
                case BuiltInType.Double:
                case BuiltInType.Enumeration:
                {
                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    return new NumericValueEditDlg().ShowDialog(value, TypeInfo.GetSystemType(builtinType).Type);
                    #pragma warning restore CA2000
                }

                case BuiltInType.Number:
                {
                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    return new NumericValueEditDlg().ShowDialog(value, TypeInfo.GetSystemType(BuiltInType.Double).Type);
                    #pragma warning restore CA2000
                }

                case BuiltInType.Integer:
                {
                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    return new NumericValueEditDlg().ShowDialog(value, TypeInfo.GetSystemType(BuiltInType.Int64).Type);
                    #pragma warning restore CA2000
                }

                case BuiltInType.UInteger:
                {
                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    return new NumericValueEditDlg().ShowDialog(value, TypeInfo.GetSystemType(BuiltInType.UInt64).Type);
                    #pragma warning restore CA2000
                }

                case BuiltInType.NodeId:
                {
                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    return new NodeIdValueEditDlg().ShowDialog(session, (NodeId)value, telemetry);
                    #pragma warning restore CA2000
                }

                case BuiltInType.ExpandedNodeId:
                {
                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    return new NodeIdValueEditDlg().ShowDialog(session, (ExpandedNodeId)value, telemetry);
                    #pragma warning restore CA2000
                }

                case BuiltInType.DateTime:
                {
                    DateTime datetime = (DateTime)value;

                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    if (new DateTimeValueEditDlg().ShowDialog(ref datetime))
                    #pragma warning restore CA2000
                    {
                        return datetime;
                    }

                    return null;
                }

                case BuiltInType.QualifiedName:
                {
                    QualifiedName qname = (QualifiedName)value;

                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    string name = new StringValueEditDlg().ShowDialog(qname.Name);
                    #pragma warning restore CA2000

                    if (name != null)
                    {
                        return new QualifiedName(name, qname.NamespaceIndex);
                    }

                    return null;
                }

                case BuiltInType.String:
                {
                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    return new StringValueEditDlg().ShowDialog((string)value);
                    #pragma warning restore CA2000
                }

                case BuiltInType.ByteString:
                {
                    byte[] bytes = value as byte[];
                    string hex = FormatByteString(bytes);

                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    string edited = new StringValueEditDlg().ShowDialog(hex);
                    #pragma warning restore CA2000

                    if (edited == null)
                    {
                        return null;
                    }

                    return ParseByteString(edited);
                }

                case BuiltInType.LocalizedText:
                {
                    LocalizedText ltext = (LocalizedText)value;

                    #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
                    string text = new StringValueEditDlg().ShowDialog(ltext.Text);
                    #pragma warning restore CA2000

                    if (text != null)
                    {
                        return new LocalizedText(ltext.Locale, text);
                    }

                    return null;
                }
            }

            #pragma warning disable CA2000 // Justification: ownership is transferred to WinForms/control owner or existing sample lifetime is preserved.
            return new ComplexValueEditDlg().ShowDialog(value, telemetry);
            #pragma warning restore CA2000
        }

        /// <summary>
        /// Formats a byte array as a whitespace separated hex string for editing.
        /// </summary>
        private static string FormatByteString(byte[] bytes)
        {
            if (bytes == null)
            {
                return String.Empty;
            }

            var builder = new StringBuilder(bytes.Length * 3);

            for (int ii = 0; ii < bytes.Length; ii++)
            {
                if (ii > 0)
                {
                    builder.Append(' ');
                }

                builder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:X2}", bytes[ii]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Parses an edited ByteString. Accepts hex (with optional whitespace, commas
        /// or 0x prefixes) or, as a fallback, a base64 encoded string.
        /// </summary>
        private static byte[] ParseByteString(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<byte>();
            }

            // Strip common separators and prefixes so both "0x0A, 0x0B" and "0A0B" work.
            string cleaned = text
                .Replace("0x", String.Empty)
                .Replace("0X", String.Empty)
                .Replace(",", String.Empty)
                .Replace("-", String.Empty);

            var hexOnly = new StringBuilder(cleaned.Length);

            foreach (char ch in cleaned)
            {
                if (Char.IsWhiteSpace(ch))
                {
                    continue;
                }

                hexOnly.Append(ch);
            }

            string hex = hexOnly.ToString();

            if (hex.Length > 0 &&
                hex.Length % 2 == 0 &&
                IsHexString(hex))
            {
                var bytes = new byte[hex.Length / 2];

                for (int ii = 0; ii < bytes.Length; ii++)
                {
                    bytes[ii] = Convert.ToByte(hex.Substring(ii * 2, 2), 16);
                }

                return bytes;
            }

            // Fall back to base64 if the text is not valid hex.
            try
            {
                return Convert.FromBase64String(text.Trim());
            }
            catch (FormatException)
            {
                throw new FormatException(
                    "The value could not be interpreted as a ByteString. Enter the bytes as hex (e.g. '0A 1B 2C') or as a base64 string.");
            }
        }

        /// <summary>
        /// Returns true if every character in the string is a hexadecimal digit.
        /// </summary>
        private static bool IsHexString(string text)
        {
            foreach (char ch in text)
            {
                bool isHexDigit =
                    (ch >= '0' && ch <= '9') ||
                    (ch >= 'a' && ch <= 'f') ||
                    (ch >= 'A' && ch <= 'F');

                if (!isHexDigit)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns to display icon for the target of a reference.
        /// </summary>
        public static Task<string> GetTargetIconAsync(ISession session, ReferenceDescription reference, CancellationToken ct = default)
        {
            return GetTargetIconAsync(session, reference.NodeClass, reference.TypeDefinition, ct);
        }

        /// <summary>
        /// Returns to display icon for the target of a reference.
        /// </summary>
        public static async Task<string> GetTargetIconAsync(ISession session, NodeClass nodeClass, ExpandedNodeId typeDefinitionId, CancellationToken ct = default)
        {
            // make sure the type definition is in the cache.
            INode typeDefinition = await session.NodeCache.FindAsync(typeDefinitionId, ct);

            switch (nodeClass)
            {
                case NodeClass.Object:
                {
                    if (session.TypeTree.IsTypeOf(typeDefinitionId, ObjectTypes.FolderType))
                    {
                        return "Folder";
                    }

                    return "Object";
                }

                case NodeClass.Variable:
                {
                    if (session.TypeTree.IsTypeOf(typeDefinitionId, VariableTypes.PropertyType))
                    {
                        return "Property";
                    }

                    return "Variable";
                }
            }

            return nodeClass.ToString();
        }

        #region Private Methods
        #endregion
    }
}
