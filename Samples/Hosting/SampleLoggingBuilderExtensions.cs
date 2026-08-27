/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.Logging.Console;

namespace Microsoft.Extensions.Logging
{
    /// <summary>
    /// The console output of the samples which really are console applications.
    /// </summary>
    public static class SampleLoggingBuilderExtensions
    {
        /// <summary>
        /// Adds a console logger which only reports <paramref name="minimumLevel"/> and
        /// above, without holding back the rest of the logging of the sample.
        /// </summary>
        /// <remarks>
        /// <c>SetMinimumLevel</c> is a floor for every provider at once, so raising it
        /// to keep the console readable would also stop the verbose events the log file
        /// is asked for through the trace masks of the configuration. The console
        /// provider is filtered on its own instead.
        /// </remarks>
        /// <param name="builder">The logging builder of the sample.</param>
        /// <param name="minimumLevel">The lowest level to write to the console.</param>
        public static ILoggingBuilder AddSampleConsole(
            this ILoggingBuilder builder,
            LogLevel minimumLevel = LogLevel.Information)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.AddConsole();
            builder.AddFilter<ConsoleLoggerProvider>(level => level >= minimumLevel);

            return builder;
        }
    }
}
