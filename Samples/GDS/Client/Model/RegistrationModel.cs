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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Client.Model
{
    // the samples spell application, product and discovery urls as strings, the way the
    // OPC UA application record does.
    #pragma warning disable CA1054, CA1056
    /// <summary>
    /// What a window collected about an application before it is registered.
    /// </summary>
    /// <param name="ApplicationName">The name to register it under.</param>
    /// <param name="ApplicationUri">The uri which identifies it.</param>
    /// <param name="ProductUri">The uri of the product it is an instance of.</param>
    /// <param name="IsClient">True for a client, which has no endpoints of its own.</param>
    /// <param name="DiscoveryUrls">The urls a server can be reached on.</param>
    /// <param name="ServerCapabilities">The capability identifiers a server declares.</param>
    public sealed record ApplicationRegistration(
        string ApplicationName,
        string ApplicationUri,
        string ProductUri,
        bool IsClient,
        IList<string> DiscoveryUrls,
        IList<string> ServerCapabilities);

    /// <summary>
    /// The application record of the Global Discovery Server: finding it, registering it,
    /// and taking it off again.
    /// </summary>
    /// <remarks>
    /// Registering an application is the first thing a client does with a GDS, because
    /// everything else - a certificate, a trust list - is issued against the application id
    /// it hands back. The record is built out of what a person typed, which is why the
    /// validation lives here with it rather than in the window: it is the same validation
    /// whoever collected the values.
    /// </remarks>
    public sealed class RegistrationModel
    {
        private GlobalDiscoveryServerClient m_gds;

        /// <summary>
        /// Points the model at the Global Discovery Server client.
        /// </summary>
        /// <param name="gds">The client.</param>
        public void Initialize(GlobalDiscoveryServerClient gds)
        {
            m_gds = gds;
        }

        /// <summary>
        /// The records the Global Discovery Server holds for an application uri.
        /// </summary>
        /// <remarks>
        /// More than one is possible and is not an error: the same application uri may have
        /// been registered from several machines. Which of them to work on is a question for
        /// a person, so this hands all of them back.
        /// </remarks>
        /// <param name="applicationUri">The uri to look for.</param>
        /// <param name="ct">The cancellation token.</param>
        public async Task<IReadOnlyList<ApplicationRecordDataType>> FindApplicationsAsync(
            string applicationUri,
            CancellationToken ct = default)
        {
            ArrayOf<ApplicationRecordDataType> records = await m_gds
                .FindApplicationAsync(applicationUri, ct)
                .ConfigureAwait(false);

            return records.IsNull ? Array.Empty<ApplicationRecordDataType>() : records.ToArray();
        }

        /// <summary>
        /// Registers an application, or replaces the record it already has.
        /// </summary>
        /// <param name="registration">What to register.</param>
        /// <param name="recordToReplace">The record to overwrite, or null to create one.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The record as it now stands, with the application id the GDS assigned.</returns>
        public async Task<ApplicationRecordDataType> RegisterAsync(
            ApplicationRegistration registration,
            ApplicationRecordDataType recordToReplace,
            CancellationToken ct = default)
        {
            ApplicationRecordDataType record = BuildRecord(registration, recordToReplace);

            record.ApplicationId = await m_gds.RegisterApplicationAsync(record, ct).ConfigureAwait(false);

            return record;
        }

        /// <summary>
        /// Takes an application off the Global Discovery Server.
        /// </summary>
        /// <param name="applicationId">The application to remove.</param>
        /// <param name="ct">The cancellation token.</param>
        public ValueTask UnregisterAsync(NodeId applicationId, CancellationToken ct = default)
        {
            return m_gds.UnregisterApplicationAsync(applicationId, ct);
        }

        /// <summary>
        /// Checks what a person typed and turns it into an application record.
        /// </summary>
        /// <remarks>
        /// Every <c>localhost</c> becomes the real host name on the way: the record is read
        /// by other machines, which would resolve <c>localhost</c> to themselves.
        /// </remarks>
        /// <param name="registration">What to register.</param>
        /// <param name="recordToReplace">The record to fill in, or null for a new one.</param>
        /// <exception cref="ArgumentException">A required field is missing or malformed.</exception>
        public static ApplicationRecordDataType BuildRecord(
            ApplicationRegistration registration,
            ApplicationRecordDataType recordToReplace = null)
        {
            ArgumentNullException.ThrowIfNull(registration);

            string applicationName = RequireText(registration.ApplicationName, "ApplicationName", "The Application Name must specified.");
            string applicationUri = RequireUri(registration.ApplicationUri, "ApplicationUri", "The Application URI must specified.");
            string productUri = RequireUri(registration.ProductUri, "ProductUri", "The Product URI must specified.");

            // a client has no endpoints and therefore no discovery urls and no capabilities;
            // a server has to have both.
            if (!registration.IsClient)
            {
                if (registration.DiscoveryUrls == null || registration.DiscoveryUrls.Count == 0)
                {
                    #pragma warning disable CA2208 // Justification: Public sample API compatibility is preserved.
                    throw new ArgumentException("At least one Discovery URL must specified.", "DiscoveryUrls");
                    #pragma warning restore CA2208
                }

                if (registration.ServerCapabilities == null || registration.ServerCapabilities.Count == 0)
                {
                    #pragma warning disable CA2208 // Justification: Public sample API compatibility is preserved.
                    throw new ArgumentException("At least one Server Capability must specified.", "ServerCapabilities");
                    #pragma warning restore CA2208
                }
            }

            var urls = new List<string>();

            if (!registration.IsClient && registration.DiscoveryUrls != null)
            {
                foreach (string discoveryUrl in registration.DiscoveryUrls)
                {
                    urls.Add(ReplaceLocalhost(discoveryUrl));
                }
            }

            ApplicationRecordDataType record = recordToReplace ?? new ApplicationRecordDataType();

            record.ApplicationUri = applicationUri;
            record.ApplicationType = registration.IsClient ? ApplicationType.Client : ApplicationType.Server;
            record.ApplicationNames = new LocalizedText[] { new LocalizedText(ReplaceLocalhost(applicationName)) };
            record.ProductUri = productUri;
            record.DiscoveryUrls = urls;
            record.ServerCapabilities = (!registration.IsClient && registration.ServerCapabilities != null)
                ? new List<string>(registration.ServerCapabilities)
                : new List<string>();

            return record;
        }

        /// <summary>
        /// Replaces <c>localhost</c> with the name of this host, so that what is written into
        /// the record means the same thing on another machine.
        /// </summary>
        /// <param name="value">The value to rewrite, which may be null.</param>
        public static string ReplaceLocalhost(string value)
        {
            return value?.Replace("localhost", Utils.GetHostName(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Replaces the name of this host with <c>localhost</c>, which is what makes a
        /// configuration written here portable to another machine.
        /// </summary>
        /// <param name="value">The value to rewrite, which may be null.</param>
        public static string HostnameToLocalhost(string value)
        {
            return value?.Replace(Utils.GetHostName(), "localhost", StringComparison.Ordinal);
        }

        /// <summary>
        /// Puts the placeholder of a special folder back into a path which starts in one, so
        /// that a saved configuration means the same thing under another account.
        /// </summary>
        /// <param name="filePath">The path to rewrite, which may be null.</param>
        public static string AddSpecialFolders(string filePath)
        {
            if (filePath == null)
            {
                return null;
            }

            foreach (Environment.SpecialFolder folder in s_specialFolders)
            {
                string prefix = Environment.GetFolderPath(folder);

                if (!String.IsNullOrEmpty(prefix) &&
                    filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return String.Concat("%", folder.ToString(), "%", filePath.AsSpan(prefix.Length));
                }
            }

            return filePath;
        }

        /// <summary>
        /// Resolves the placeholders of a path back into the folders of this machine.
        /// </summary>
        /// <param name="filePath">The path to resolve, which may be null.</param>
        public static string RemoveSpecialFolders(string filePath)
        {
            return filePath != null ? Utils.GetAbsoluteFilePath(filePath, true, false, false) : null;
        }

        /// <summary>
        /// The folders whose placeholders a saved configuration may carry, most specific
        /// first: the local application data of a user lives under its application data, so
        /// the wrong order would turn one into the other.
        /// </summary>
        private static readonly Environment.SpecialFolder[] s_specialFolders =
        {
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.CommonProgramFiles,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.MyDocuments,
        };

        /// <summary>
        /// A trimmed value which has to be there.
        /// </summary>
        private static string RequireText(string value, string name, string message)
        {
            string trimmed = value?.Trim();

            if (String.IsNullOrEmpty(trimmed))
            {
                #pragma warning disable CA2208 // Justification: Public sample API compatibility is preserved.
                throw new ArgumentException(message, name);
                #pragma warning restore CA2208
            }

            return trimmed;
        }

        /// <summary>
        /// A trimmed value which has to be there and has to be an absolute uri.
        /// </summary>
        private static string RequireUri(string value, string name, string message)
        {
            string trimmed = RequireText(value, name, message);

            if (!Uri.IsWellFormedUriString(trimmed, UriKind.Absolute))
            {
                #pragma warning disable CA2208 // Justification: Public sample API compatibility is preserved.
                throw new ArgumentException(trimmed + "is not a valid URI.", name);
                #pragma warning restore CA2208
            }

            return ReplaceLocalhost(trimmed);
        }
    }
}
#pragma warning restore CA1054, CA1056
