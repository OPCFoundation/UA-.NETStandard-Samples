/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;

namespace Opc.Ua.Samples.WinForms
{
    /// <summary>
    /// Creates the windows of a sample - its dialogs and its secondary forms - from
    /// the container, so that they take their dependencies through their constructors
    /// instead of having them handed to them by whoever opens them.
    /// </summary>
    /// <remarks>
    /// A window is a transient object with a lifetime of its own, so it is created
    /// rather than resolved: the factory picks the constructor of the window, takes the
    /// arguments the caller passes and resolves the rest from the container. That is
    /// what lets a dialog ask for an <c>ITelemetryContext</c> or an
    /// <c>ApplicationConfiguration</c> in its constructor while its caller only knows
    /// the value the user is about to edit.
    ///
    /// The factory hands itself to every window it creates - see
    /// <see cref="IWindowFactoryConsumer"/> - so a dialog can open the next dialog
    /// without knowing the container.
    /// </remarks>
    public interface IWindowFactory
    {
        /// <summary>
        /// The services of the sample, for the rare window which has to resolve
        /// something the container knows only at run time.
        /// </summary>
        IServiceProvider Services { get; }

        /// <summary>
        /// Creates a window, resolving the constructor parameters which are not given
        /// from the container.
        /// </summary>
        /// <typeparam name="TWindow">The window to create.</typeparam>
        /// <param name="arguments">The constructor arguments the container does not
        /// know, matched to the parameters by type.</param>
        TWindow Create<TWindow>(params object[] arguments);

        /// <summary>
        /// Creates a window whose type is only known at run time.
        /// </summary>
        /// <param name="windowType">The window to create.</param>
        /// <param name="arguments">The constructor arguments the container does not
        /// know, matched to the parameters by type.</param>
        object Create(Type windowType, params object[] arguments);
    }
}
