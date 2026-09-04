/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Opc.Ua.Samples.Client
{
    /// <summary>
    /// What one operation of a client model answered: what was attempted, and the status
    /// code the server gave. The window renders it into its status bar.
    /// </summary>
    /// <param name="What">What was attempted, for instance "Calling Reset".</param>
    /// <param name="Status">What the server answered.</param>
    public sealed record OperationResult(string What, StatusCode Status)
    {
        /// <summary>
        /// True when the server answered with a good status.
        /// </summary>
        public bool Succeeded => StatusCode.IsGood(Status);

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{What} answered {Status}";
        }
    }
}
