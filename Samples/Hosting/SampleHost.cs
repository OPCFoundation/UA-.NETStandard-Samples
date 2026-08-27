/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// The generic host every sample is built on.
    /// </summary>
    public static class SampleHost
    {
        /// <summary>
        /// Creates the host builder of a sample: the logging providers of the samples
        /// and nothing else, so the sample itself only has to add what is specific to
        /// it.
        /// </summary>
        /// <param name="args">The command line of the sample.</param>
        public static HostApplicationBuilder CreateBuilder(string[] args)
        {
            SampleLogging.CreateBootstrapLogger();

            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

            // the samples are Windows Forms applications for the most part and log to
            // the file their configuration names, so the console provider the host
            // installs by default is of no use to them.
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Logging.AddSerilog();

            return builder;
        }
    }
}
