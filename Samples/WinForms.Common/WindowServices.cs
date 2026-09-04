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

namespace Opc.Ua.Samples.WinForms
{
    /// <summary>
    /// Hands the <see cref="IWindowFactory"/> of a sample down a control tree, and
    /// finds it again from anywhere in that tree.
    /// </summary>
    /// <remarks>
    /// The container creates the top of the tree - the main form of the sample, and
    /// every window it opens - and the factory it is handed reaches the controls the
    /// Windows Forms designer created through <see cref="AttachWindows"/>. A control
    /// which is created and parented after that finds the factory of its form through
    /// <see cref="FindWindows"/>, which is what the <c>Windows</c> property of
    /// <see cref="SampleForm"/> and <see cref="SampleUserControl"/> falls back to.
    /// </remarks>
    public static class WindowServices
    {
        /// <summary>
        /// Hands the factory to a control and to every control below it.
        /// </summary>
        /// <param name="control">The root of the control tree.</param>
        /// <param name="windows">The factory of the sample.</param>
        public static TControl AttachWindows<TControl>(this TControl control, IWindowFactory windows)
            where TControl : Control
        {
            ArgumentNullException.ThrowIfNull(control);

            if (control is IWindowFactoryConsumer consumer)
            {
                consumer.AttachedWindows = windows;
            }

            foreach (Control child in control.Controls)
            {
                child.AttachWindows(windows);
            }

            return control;
        }

        /// <summary>
        /// Finds the factory of the sample from a control, by walking up to the window
        /// the container created.
        /// </summary>
        /// <param name="control">The control which needs a window.</param>
        /// <returns>The factory, or <c>null</c> when the control is not below a window
        /// the container created.</returns>
        public static IWindowFactory FindWindows(Control control)
        {
            for (Control parent = control?.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is IWindowFactoryConsumer consumer && consumer.AttachedWindows != null)
                {
                    return consumer.AttachedWindows;
                }
            }

            // a dialog is not a child of the window which opened it.
            if (control is Form form && form.Owner is IWindowFactoryConsumer owner)
            {
                return owner.AttachedWindows;
            }

            return null;
        }

        /// <summary>
        /// The factory a control has to create its windows with, which says so when the
        /// control was never reached by the container.
        /// </summary>
        /// <param name="control">The control which needs a window.</param>
        /// <exception cref="InvalidOperationException">The control is not below a window
        /// the container created.</exception>
        public static IWindowFactory RequireWindows(Control control)
        {
            return FindWindows(control)
                ?? throw new InvalidOperationException(
                    $"'{control?.GetType().Name}' has no window factory: it was not created by the " +
                    "container and is not on a window which was. Create it with IWindowFactory.Create, " +
                    "and register the factory with services.AddSampleWindows().");
        }
    }
}
