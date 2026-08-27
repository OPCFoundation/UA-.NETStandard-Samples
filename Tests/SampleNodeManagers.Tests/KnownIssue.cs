/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Records behaviour which is broken today and has to be fixed by the migration.
    /// </summary>
    /// <remarks>
    /// Some of the samples do not do what they were written to do any more. Asserting the
    /// broken behaviour would be worse than useless, because the migration would then be
    /// asked to preserve it, so the expectation is written the right way round and
    /// reported as ignored until it holds. The moment it starts passing, the test fails
    /// and asks for the note to be removed, which is the same bargain tier 1 makes with
    /// its known issues - and which has already caught two expectations that were wrong
    /// about the sample rather than about the stack.
    /// </remarks>
    public static class KnownIssue
    {
        /// <summary>
        /// Runs an assertion which is expected to fail, and reports the test as ignored.
        /// </summary>
        /// <param name="check">The assertion, written as though the sample worked.</param>
        /// <param name="issue">What is broken, and what a reader should know about it.</param>
        public static async Task RecordAsync(Func<Task> check, string issue)
        {
            ArgumentNullException.ThrowIfNull(check);

            try
            {
                await check().ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not SuccessException)
            {
                Assert.Ignore($"Known issue: {issue}{Environment.NewLine}The test reported: {failure.Message}");
                return;
            }

            Assert.Fail(
                $"This is recorded as a known issue, but it passed: {issue}{Environment.NewLine}" +
                "Remove the KnownIssue.RecordAsync wrapper and let the assertion stand on its own.");
        }
    }
}
