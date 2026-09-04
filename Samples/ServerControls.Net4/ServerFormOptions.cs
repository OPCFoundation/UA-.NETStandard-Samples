/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Opc.Ua.Server.Controls
{
    /// <summary>
    /// What the <see cref="ServerForm"/> of a sample does beyond showing the state of
    /// the running server.
    /// </summary>
    /// <remarks>
    /// A sample which wants something other than the defaults says so where it
    /// registers its server - <c>services.Configure&lt;ServerFormOptions&gt;(...)</c> -
    /// instead of the form being created by hand with the flags as arguments.
    /// </remarks>
    public sealed class ServerFormOptions
    {
        /// <summary>
        /// Whether the form asks the user about a certificate the server does not
        /// trust, instead of letting the validator reject it.
        /// </summary>
        /// <remarks>
        /// Off by default: a sample whose server is hosted by the stack starts before
        /// the form exists, so there is nothing to prompt on.
        /// </remarks>
        public bool ShowCertificateValidationDialog { get; set; }
    }
}
