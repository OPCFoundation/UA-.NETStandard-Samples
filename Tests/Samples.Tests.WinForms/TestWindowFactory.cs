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
using Opc.Ua.Samples.WinForms;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// The window factory of a test which drives controls on a form it built itself
    /// rather than a main form the container created.
    /// </summary>
    /// <remarks>
    /// The controls of the samples create the dialogs they open through the factory the
    /// container handed their form. A test which assembles a form out of single controls
    /// has to hand it to them the same way, or the first dialog the control under test
    /// opens fails with no factory - which is the point: nothing reaches a window except
    /// through the container.
    /// </remarks>
    public sealed class TestWindowFactory : IDisposable
    {
        private readonly ServiceProvider m_provider;

        /// <summary>
        /// Builds a container out of the services a control needs - the application
        /// configuration and the telemetry of the test as a rule - and the registration
        /// every Windows Forms sample shares.
        /// </summary>
        /// <param name="services">The instances the windows take through their
        /// constructors, registered under their own types.</param>
        public TestWindowFactory(params object[] services)
        {
            var collection = new ServiceCollection();

            foreach (object service in services ?? [])
            {
                collection.AddSingleton(service.GetType(), service);
            }

            collection.AddSingleton<ITelemetryContext>(NullTelemetry.Instance);
            collection.AddSampleWindows();

            m_provider = collection.BuildServiceProvider();
            Windows = m_provider.GetRequiredService<IWindowFactory>();
        }

        /// <summary>
        /// The factory the controls under test create their dialogs with.
        /// </summary>
        public IWindowFactory Windows { get; }

        /// <summary>
        /// Hands the factory to a form the test built itself and to its controls.
        /// </summary>
        /// <param name="form">The form the controls under test are on.</param>
        public TForm AttachTo<TForm>(TForm form) where TForm : Control
        {
            return form.AttachWindows(Windows);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_provider.Dispose();
        }
    }
}
