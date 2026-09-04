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
        /// Set by the container when the form is created. A form which was shown from
        /// another form of the sample without being created by the factory - a designer
        /// surface, or a test which constructs it directly - finds it through its owner.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IWindowFactory Windows
        {
            get => m_windows ??= WindowServices.FindWindows(this);
            set => m_windows = value;
        }

        /// <summary>
        /// The factory the form creates the windows it opens with, which says so when
        /// the form was not created by the container.
        /// </summary>
        protected IWindowFactory RequiredWindows => WindowServices.RequireWindows(this, m_windows);
    }
}
