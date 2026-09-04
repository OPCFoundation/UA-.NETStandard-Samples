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
    /// Reads the configuration of a sample and sets up its certificate when the host
    /// starts, so the forms of the sample can take the loaded configuration in their
    /// constructors.
    /// </summary>
    /// <remarks>
    /// A server of the sample is started and stopped by the hosted server of the
    /// stack, see <c>AddSampleServer(configureServer)</c>; this service only makes the
    /// load happen before the forms are created.
    /// </remarks>
    public sealed class SampleApplicationHostedService : IHostedService
    {
        private readonly SampleApplication m_application;

        /// <summary>
        /// Creates the hosted service.
        /// </summary>
        /// <param name="application">The application instance of the sample.</param>
        public SampleApplicationHostedService(SampleApplication application)
        {
            ArgumentNullException.ThrowIfNull(application);

            m_application = application;
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return m_application.GetAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
