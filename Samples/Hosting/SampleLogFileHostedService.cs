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
using Microsoft.Extensions.Hosting;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// Points the logging of a sample at a file, for the samples which have no
    /// application configuration to take the path from.
    /// </summary>
    public sealed class SampleLogFileHostedService : IHostedService
    {
        private readonly string m_outputFilePath;

        /// <summary>
        /// Creates the hosted service.
        /// </summary>
        /// <param name="outputFilePath">The file to log to.</param>
        public SampleLogFileHostedService(string outputFilePath)
        {
            ArgumentNullException.ThrowIfNull(outputFilePath);

            m_outputFilePath = outputFilePath;
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            SampleLogging.UseLogFile(m_outputFilePath);

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
