/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Opc.Ua.Samples.WinForms
{
    /// <summary>
    /// A form or a control which opens windows of its own, and is therefore handed the
    /// <see cref="IWindowFactory"/> of the sample.
    /// </summary>
    /// <remarks>
    /// The Windows Forms designer creates the controls of a form itself, with the
    /// parameterless constructor it writes into the generated code, so a control cannot
    /// take the factory through its constructor. The form the container creates hands
    /// it down its control tree instead - see
    /// <see cref="WindowServices.AttachWindows"/> - and a control which is parented
    /// later finds it by walking up to its form.
    /// </remarks>
    public interface IWindowFactoryConsumer
    {
        /// <summary>
        /// The factory the form or control creates its windows with.
        /// </summary>
        IWindowFactory Windows { get; set; }
    }
}
