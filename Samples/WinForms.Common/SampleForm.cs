/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.ComponentModel;
using System.Windows.Forms;

namespace Opc.Ua.Samples.WinForms
{
    /// <summary>
    /// The base class of the forms and dialogs of the samples: a form the container
    /// creates, which creates the windows it opens the same way.
    /// </summary>
    /// <remarks>
    /// A form takes what it needs through its constructor and is created with
    /// <see cref="IWindowFactory.Create{TWindow}"/>; the factory reaches it and the
    /// controls the designer put on it through <see cref="WindowServices.AttachWindows"/>.
    /// </remarks>
    public class SampleForm : Form, IWindowFactoryConsumer
    {
        private IWindowFactory m_windows;

        /// <summary>
        /// The factory the form creates the windows it opens with.
        /// </summary>
        /// <remarks>
        /// Handed to the form by the container which created it. A form which was opened
        /// by another window of the sample without going through the factory finds it
        /// through its owner instead.
        /// </remarks>
        /// <exception cref="System.InvalidOperationException">The form was not created
        /// by the container and is not owned by a window which was.</exception>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IWindowFactory Windows => m_windows ??= WindowServices.RequireWindows(this);

        /// <inheritdoc/>
        IWindowFactory IWindowFactoryConsumer.Windows
        {
            get => m_windows;
            set => m_windows = value;
        }
    }
}
