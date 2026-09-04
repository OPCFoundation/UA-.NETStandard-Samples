/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Samples.WinForms;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The registration every Windows Forms sample shares: the factory its forms
    /// create their dialogs with.
    /// </summary>
    public static class WinFormsServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the <see cref="IWindowFactory"/> of a Windows Forms sample.
        /// </summary>
        /// <remarks>
        /// Called by the entry point helper of the samples, so a sample does not have
        /// to register it itself.
        /// </remarks>
        /// <param name="services">The service collection.</param>
        public static IServiceCollection AddSampleWindows(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<IWindowFactory>(
                provider => new WindowFactory(provider));

            return services;
        }
    }
}
