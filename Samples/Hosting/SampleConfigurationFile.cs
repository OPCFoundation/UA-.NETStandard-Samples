/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// Resolves the configuration file of a sample, which is named relative to the
    /// application so a sample can be started from any working directory.
    /// </summary>
    internal static class SampleConfigurationFile
    {
        /// <summary>
        /// Turns the configuration file name of a sample into the path to load, the
        /// same way the stack resolves configuration files: the application directory
        /// and the current working directory are both probed.
        /// </summary>
        /// <param name="configurationFile">The file the sample named.</param>
        /// <exception cref="ArgumentException">When no file was named.</exception>
        public static string Resolve(string configurationFile)
        {
            if (string.IsNullOrEmpty(configurationFile))
            {
                throw new ArgumentException(
                    "The sample did not name its configuration file.",
                    nameof(configurationFile));
            }

            try
            {
                return Utils.GetAbsoluteFilePath(
                    configurationFile,
                    checkCurrentDirectory: true,
                    createAlways: false)
                    ?? configurationFile;
            }
            catch (Exception e) when (e is ServiceResultException or IOException)
            {
                // a file which cannot be found here is reported by the load, together
                // with where it was looked for
                return configurationFile;
            }
        }
    }
}
