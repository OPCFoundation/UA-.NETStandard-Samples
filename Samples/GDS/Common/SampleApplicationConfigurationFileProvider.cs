/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Sample implementation of the OPC 10000-12 §7.10.20 <c>ConfigurationFile</c> provider
    /// that serves the GDS's own application configuration XML file over the standard
    /// <c>ApplicationConfigurationFileType</c> read/update flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>ConfigurationFile</c> Object is Optional and <c>ConfigurationNodeManager</c>
    /// only exposes it - together with its whole <c>FileType</c> sub-tree - when a provider
    /// is configured. The node manager owns the stream lifecycle, the <c>SecurityAdmin</c>
    /// Role and authenticated-channel checks, exclusion against an open PushManagement
    /// transaction, the <c>VersionToUpdate</c> / <c>CurrentVersion</c> check and the
    /// <c>UpdateId</c> handling; this provider only reads, validates and applies the content.
    /// </para>
    /// <para>
    /// §7.8.5.1 defines the stream body as a serialized <c>UABinaryFileDataType</c> and the
    /// node manager treats it opaquely, leaving the concrete encoding to the provider. The
    /// sample uses the configuration file the server was started from verbatim, because that
    /// is the artefact a GDS administrator actually wants to read back and replace, and it
    /// keeps the example inspectable with a text editor.
    /// </para>
    /// <para>
    /// <see cref="RequiresConfirmation"/> is <c>true</c>: the applied file only becomes
    /// permanent once the Client has reconnected and called <c>ConfirmUpdate</c>. Until then
    /// the previous file is kept next to it, and <see cref="RevertUpdateAsync"/> puts it back
    /// when the confirmation does not arrive - which is exactly the protection a remote
    /// configuration change needs, since a bad configuration is otherwise only discovered
    /// after the server can no longer be reached.
    /// </para>
    /// <para>
    /// A configuration change written through this provider takes effect the next time the
    /// server is started; the sample does not hot-reload the running server from it.
    /// </para>
    /// </remarks>
    public sealed class SampleApplicationConfigurationFileProvider : IApplicationConfigurationFileProvider
    {
        private const string kBackupSuffix = ".previous";

        private readonly string m_configurationFilePath;
        private readonly ILogger m_logger;
        private readonly object m_lock = new object();

        /// <summary>
        /// Creates the provider over the supplied configuration file.
        /// </summary>
        /// <param name="configurationFilePath">
        /// Path of the application configuration file the server was started from.
        /// </param>
        /// <param name="telemetry">Telemetry context used for logging.</param>
        /// <exception cref="ArgumentNullException"><paramref name="configurationFilePath"/> is <c>null</c>.</exception>
        public SampleApplicationConfigurationFileProvider(
            string configurationFilePath,
            ITelemetryContext telemetry)
        {
            if (configurationFilePath == null)
            {
                throw new ArgumentNullException(nameof(configurationFilePath));
            }

            m_configurationFilePath =
                Utils.ReplaceSpecialFolderNames(configurationFilePath) ?? configurationFilePath;
            m_logger = telemetry.CreateLogger<SampleApplicationConfigurationFileProvider>();

            LastUpdateTime = File.Exists(m_configurationFilePath)
                ? File.GetLastWriteTimeUtc(m_configurationFilePath)
                : DateTime.UtcNow;
        }

        /// <inheritdoc/>
        public uint CurrentVersion { get; private set; }

        /// <inheritdoc/>
        public DateTime LastUpdateTime { get; private set; }

        /// <inheritdoc/>
        public bool RequiresConfirmation => true;

        /// <inheritdoc/>
        public ValueTask<ByteString> ReadConfigurationAsync(CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                if (!File.Exists(m_configurationFilePath))
                {
                    return new ValueTask<ByteString>(ByteString.Empty);
                }

                #pragma warning disable CA1849 // Justification: the provider serializes its file access with a lock, which must not be held across an await.
                return new ValueTask<ByteString>(File.ReadAllBytes(m_configurationFilePath).ToByteString());
                #pragma warning restore CA1849
            }
        }

        /// <inheritdoc/>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadInvalidArgument"/> when the content is empty or is not a
        /// well-formed application configuration document.
        /// </exception>
        public ValueTask ValidateConfigurationAsync(
            ByteString configuration,
            CancellationToken cancellationToken = default)
        {
            if (configuration.IsNull || configuration.Length == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "The configuration must not be empty.");
            }

            try
            {
                using var stream = new MemoryStream(configuration.ToArray(), writable: false);
                using XmlReader reader = XmlReader.Create(
                    stream,
                    new XmlReaderSettings {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        CloseInput = false
                    });

                // reading to the end proves the document is well formed; the root element
                // check catches an unrelated - but well formed - document being pushed.
                string rootElement = null;

                #pragma warning disable CA1849 // Justification: the reader runs over an in-memory buffer, there is nothing to await on.
                while (reader.Read())
                #pragma warning restore CA1849
                {
                    if (rootElement == null && reader.NodeType == XmlNodeType.Element)
                    {
                        rootElement = reader.LocalName;
                    }
                }

                if (!String.Equals(rootElement, "ApplicationConfiguration", StringComparison.Ordinal))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        Utils.Format(
                            "Expected an ApplicationConfiguration document, found <{0}>.",
                            rootElement ?? "(empty)"));
                }
            }
            catch (XmlException ex)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "The configuration is not a well formed XML document: " + ex.Message);
            }

            return default;
        }

        /// <inheritdoc/>
        public ValueTask ApplyConfigurationAsync(
            ByteString configuration,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                string backup = m_configurationFilePath + kBackupSuffix;

                // the write is the only step that can fail here, and it goes to a temporary
                // file first so a half-written configuration never replaces the live one.
                string staged = m_configurationFilePath + ".staged";
                #pragma warning disable CA1849 // Justification: the provider serializes its file access with a lock, which must not be held across an await.
                File.WriteAllBytes(staged, configuration.ToArray());
                #pragma warning restore CA1849

                if (File.Exists(m_configurationFilePath))
                {
                    File.Copy(m_configurationFilePath, backup, overwrite: true);
                }

                File.Copy(staged, m_configurationFilePath, overwrite: true);
                File.Delete(staged);

                CurrentVersion = unchecked(CurrentVersion + 1);
                LastUpdateTime = DateTime.UtcNow;

                #pragma warning disable CA1873 // Justification: the logged values are trivial reads in a sample.
                m_logger.LogInformation(
                    "ConfigurationFile: applied version {Version}, awaiting ConfirmUpdate.",
                    CurrentVersion);
                #pragma warning restore CA1873
            }

            return default;
        }

        /// <inheritdoc/>
        public ValueTask ConfirmUpdateAsync(CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                string backup = m_configurationFilePath + kBackupSuffix;

                if (File.Exists(backup))
                {
                    File.Delete(backup);

                    #pragma warning disable CA1873 // Justification: the logged values are trivial reads in a sample.
                    m_logger.LogInformation(
                        "ConfigurationFile: confirmed version {Version}.", CurrentVersion);
                    #pragma warning restore CA1873
                }
            }

            return default;
        }

        /// <inheritdoc/>
        public ValueTask RevertUpdateAsync(CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                string backup = m_configurationFilePath + kBackupSuffix;

                if (!File.Exists(backup))
                {
                    return default;
                }

                File.Copy(backup, m_configurationFilePath, overwrite: true);
                File.Delete(backup);

                CurrentVersion = unchecked(CurrentVersion + 1);
                LastUpdateTime = DateTime.UtcNow;

                #pragma warning disable CA1873 // Justification: the logged values are trivial reads in a sample.
                m_logger.LogWarning(
                    "ConfigurationFile: the update was not confirmed, the previous configuration was restored (version {Version}).",
                    CurrentVersion);
                #pragma warning restore CA1873
            }

            return default;
        }
    }
}
