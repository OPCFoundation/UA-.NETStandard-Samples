/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// What a sample needs to say about itself to be bootstrapped.
    /// </summary>
    public sealed class SampleApplicationOptions
    {
        /// <summary>
        /// The OPC UA application configuration XML file of the sample, for example
        /// <c>BoilerClient.Config.xml</c>. A relative path is resolved against the
        /// application directory and the current working directory.
        /// </summary>
        public string ConfigurationFile { get; set; }

        /// <summary>
        /// The name to report before the configuration has been read, for example in
        /// the dialog which shows a failure to read it. The name in the configuration
        /// wins once it is available.
        /// </summary>
        public string ApplicationName { get; set; }

        /// <summary>
        /// Whether the sample is a client, a server, or both.
        /// </summary>
        public ApplicationType ApplicationType { get; set; } = ApplicationType.Client;

        /// <summary>
        /// Whether a missing or invalid application instance certificate stops the
        /// sample instead of only being reported.
        /// </summary>
        public bool RequireApplicationCertificate { get; set; } = true;

        /// <summary>
        /// Whether the configuration is loaded without asking the user about
        /// problems it finds.
        /// </summary>
        public bool Silent { get; set; }

        /// <summary>
        /// Applied to the configuration right after it has been read, and before the
        /// certificate is checked and the server is started. For the settings which
        /// the configuration file cannot express, such as a certificate validation
        /// callback.
        /// </summary>
        public Action<ApplicationConfiguration> ConfigureConfiguration { get; set; }
    }
}
