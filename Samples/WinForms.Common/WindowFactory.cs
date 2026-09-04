/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Samples.WinForms
{
    /// <summary>
    /// The <see cref="IWindowFactory"/> of a sample, over the container the generic
    /// host built.
    /// </summary>
    internal sealed class WindowFactory : IWindowFactory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WindowFactory"/> class.
        /// </summary>
        /// <param name="services">The services of the sample.</param>
        public WindowFactory(IServiceProvider services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        /// <inheritdoc/>
        public IServiceProvider Services { get; }

        /// <inheritdoc/>
        public TWindow Create<TWindow>(params object[] arguments)
        {
            return (TWindow)Create(typeof(TWindow), arguments);
        }

        /// <inheritdoc/>
        public object Create(Type windowType, params object[] arguments)
        {
            ArgumentNullException.ThrowIfNull(windowType);

            object window = ActivatorUtilities.CreateInstance(
                Services,
                windowType,
                arguments ?? []);

            // the window opens windows of its own with the same factory, and so do the
            // controls the designer put on it.
            if (window is Control control)
            {
                control.AttachWindows(this);
            }
            else if (window is IWindowFactoryConsumer consumer)
            {
                consumer.AttachedWindows = this;
            }

            return window;
        }
    }
}
