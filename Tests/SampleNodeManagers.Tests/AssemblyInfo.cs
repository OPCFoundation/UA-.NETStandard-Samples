/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using NUnit.Framework;

// every fixture in this assembly starts a sample server, and the samples bind the fixed
// ports they ship with. Two fixtures running at the same time would fight over a port,
// so the whole assembly runs one fixture at a time. The same reason keeps a test run
// from being started twice on one machine, which docs/TESTING.md spells out.
[assembly: NonParallelizable]
[assembly: LevelOfParallelism(1)]
