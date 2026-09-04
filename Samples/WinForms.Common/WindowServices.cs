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
    /// every dialog it opens - and the factory it injects there reaches the controls
    /// the Windows Forms designer created through <see cref="AttachWindows"/>. A
    /// control which is created and parented after that finds the factory of its form
    /// through <see cref="FindWindows"/>, which is what the <c>Windows</c> property of
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
                consumer.Windows = windows;
            }

            foreach (Control child in control.Controls)
            {
                child.AttachWindows(windows);
            }

            return control;
        }

        /// <summary>
        /// Finds the factory of the sample from a control, by walking up to the form
        /// the container created.
        /// </summary>
        /// <param name="control">The control which needs a window.</param>
        /// <returns>The factory, or <c>null</c> for a control which is not on a form
        /// the container created - at design time, for example.</returns>
        public static IWindowFactory FindWindows(Control control)
        {
            for (Control parent = control?.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is IWindowFactoryConsumer consumer && consumer.Windows != null)
                {
                    return consumer.Windows;
                }
            }

            // a dialog is not parented to the form which opened it.
            if (control is Form form && form.Owner is IWindowFactoryConsumer owner)
            {
                return owner.Windows;
            }

            return null;
        }

        /// <summary>
        /// Returns the factory a control has to create its windows with, and says so
        /// when the control was not reached by the container.
        /// </summary>
        /// <param name="control">The control which needs a window.</param>
        /// <param name="windows">The factory the control was handed, if any.</param>
        public static IWindowFactory RequireWindows(Control control, IWindowFactory windows)
        {
            IWindowFactory found = windows ?? FindWindows(control);

            if (found == null)
            {
                throw new InvalidOperationException(
                    $"'{control?.GetType().Name}' has no window factory: it was not created by the " +
                    "container and is not on a form which was. Create it with IWindowFactory.Create.");
            }

            return found;
        }
    }
}
