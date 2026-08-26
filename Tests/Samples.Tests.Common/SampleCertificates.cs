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
using Opc.Ua.Configuration;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Provisions the application instance certificate of a sample before the sample runs.
    /// </summary>
    /// <remarks>
    /// A sample which is started as a process brings its own configuration and its own
    /// certificate store, and creates its certificate on first run. That makes the first run
    /// on a fresh machine different from every later one: the certificate is created while
    /// the server is starting, and the endpoints the server then publishes describe a
    /// certificate the test has never seen. Creating it up front, through the same
    /// ApplicationInstance the sample itself uses, makes both runs the same.
    /// </remarks>
    public static class SampleCertificates
    {
        /// <summary>
        /// Creates the application instance certificate of a sample if its store has none.
        /// </summary>
        /// <remarks>
        /// A store which already holds a certificate is left alone, whatever the stack thinks
        /// of it. On a developer machine that certificate may well have been created for a
        /// different host name and be rejected here, and a test has no business deleting it -
        /// the sample runs with what it finds, exactly as it would by hand.
        /// </remarks>
        /// <param name="configPath">The sample configuration, relative to the repository root.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>What was done, for the test output.</returns>
        public static async Task<string> EnsureApplicationCertificateAsync(
            string configPath,
            CancellationToken ct = default)
        {
            // deliberately not redirected into a temporary PKI: the sample process reads the
            // configuration file as it ships, so the certificate has to be created in the
            // store that configuration names
            ApplicationConfiguration configuration =
                await SampleConfigurationLoader.LoadAsync(configPath, null, ct).ConfigureAwait(false);

            // the sample replaces localhost with the host name when it starts, and the
            // certificate has to carry the application uri the server will then publish -
            // otherwise the client rejects it with BadCertificateUriInvalid
            ApplicationInstance.FixupAppConfig(configuration);

            CertificateIdentifier certificate = configuration.SecurityConfiguration.ApplicationCertificate;
            string subject = certificate?.SubjectName ?? configuration.ApplicationName;

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            try
            {
                bool certificateOk = await application
                    .CheckApplicationInstanceCertificatesAsync(true, null, ct)
                    .ConfigureAwait(false);

                return certificateOk
                    ? $"'{subject}' is in place"
                    : $"'{subject}' could not be created, the sample will have to deal with it";
            }
            catch (ServiceResultException e)
            {
                // the store holds a certificate the stack will not accept, and in silent mode
                // it refuses to replace it. That is a machine which has run the sample before,
                // not something a test should repair
                return $"'{subject}' was left as it is: {e.Message.Split('\n')[0]}";
            }
        }
    }
}
