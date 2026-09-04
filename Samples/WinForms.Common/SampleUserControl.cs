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
    /// The base class of the user controls of the samples: a control which opens
    /// windows of its own, and is handed the factory to create them with by the form
    /// the container created.
    /// </summary>
    /// <remarks>
    /// The Windows Forms designer writes a parameterless constructor call into the
    /// generated code of the form, so a control cannot take the factory through its
    /// constructor. It is handed down the control tree instead, and a control which is
    /// parented after that finds it by walking up to its form.
    /// </remarks>
    public class SampleUserControl : UserControl, IWindowFactoryConsumer
    {
        private IWindowFactory m_windows;

        /// <summary>
        /// The factory the control creates the windows it opens with.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">The control is not on a
        /// window the container created.</exception>
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
