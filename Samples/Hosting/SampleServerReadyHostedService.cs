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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Opc.Ua.Samples.Hosting
{
    /// <summary>
    /// Holds the startup of the host until the hosted server of the sample is
    /// listening, or until its startup has failed.
    /// </summary>
    /// <remarks>
    /// The hosted server of the stack is a <see cref="BackgroundService"/>: the start
    /// of the host returns while the configuration is still being read and the server
    /// is still coming up. The samples resolve the running server and its
    /// configuration right after <c>StartAsync</c>, and their tests treat a returned
    /// start as a listening server, so this service - registered right after the
    /// hosted server - blocks the start until <see cref="SampleServerStartup"/>
    /// reports the server up. A startup failure is taken from the execute task of the
    /// background services which started earlier, so the original error surfaces
    /// instead of a hang.
    /// </remarks>
    internal sealed class SampleServerReadyHostedService : IHostedService
    {
        private readonly SampleServerStartup m_startup;
        private readonly IServiceProvider m_provider;

        /// <summary>
        /// Creates the service.
        /// </summary>
        /// <param name="startup">The startup of the sample server.</param>
        /// <param name="provider">The container, to observe the hosted services which
        /// started before this one.</param>
        public SampleServerReadyHostedService(
            SampleServerStartup startup,
            IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(startup);
            ArgumentNullException.ThrowIfNull(provider);

            m_startup = startup;
            m_provider = provider;
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Task started = m_startup.Started;

            // the hosted services registered before this one have been started; the
            // ones that keep starting in the background - the hosted server among
            // them - expose the work as their execute task. A faulted task is the
            // startup failure of the server.
            var starting = ((IEnumerable<IHostedService>)m_provider
                .GetService(typeof(IEnumerable<IHostedService>)))
                .OfType<BackgroundService>()
                .Select(service => service.ExecuteTask)
                .Where(task => task is { IsCompletedSuccessfully: false })
                .ToList();

            while (true)
            {
                Task completed = await Task
                    .WhenAny([started, .. starting])
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (completed == started)
                {
                    return;
                }

                if (completed.IsFaulted)
                {
                    // rethrows the original startup failure of the server
                    await completed.ConfigureAwait(false);
                }

                // a background service which ran to completion is not the server;
                // keep waiting on the remaining ones.
                starting.Remove(completed);
            }
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
