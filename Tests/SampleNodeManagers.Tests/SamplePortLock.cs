/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Every fixture in this assembly starts sample servers on the fixed ports the samples
    /// ship with, so the whole assembly takes the machine wide sample port lock first.
    /// </summary>
    [SetUpFixture]
    public sealed class SamplePortLock : SamplePortLockFixture
    {
    }
}
