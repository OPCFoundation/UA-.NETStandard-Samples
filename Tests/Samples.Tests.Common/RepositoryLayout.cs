/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Locates the repository on disk so that tests can work with the sample sources
    /// (configuration files, hard coded endpoint urls) instead of build output.
    /// </summary>
    public static class RepositoryLayout
    {
        /// <summary>
        /// A file which only exists in the repository root.
        /// </summary>
        private const string kRootMarker = "UA Samples.slnx";

        private static readonly Lazy<DirectoryInfo> s_root = new Lazy<DirectoryInfo>(FindRoot);

        /// <summary>
        /// The root directory of the repository.
        /// </summary>
        public static DirectoryInfo Root => s_root.Value;

        /// <summary>
        /// Returns the absolute path for a path relative to the repository root.
        /// </summary>
        public static string PathOf(string relativePath)
        {
            if (relativePath == null)
            {
                throw new ArgumentNullException(nameof(relativePath));
            }

            return Path.GetFullPath(
                Path.Combine(Root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>
        /// Returns the path relative to the repository root, using forward slashes.
        /// </summary>
        public static string RelativePathOf(string absolutePath)
        {
            return Path.GetRelativePath(Root.FullName, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Returns true if the path exists relative to the repository root.
        /// </summary>
        public static bool Exists(string relativePath)
        {
            string path = PathOf(relativePath);
            return File.Exists(path) || Directory.Exists(path);
        }

        /// <summary>
        /// All OPC UA application configuration files in the repository, as repository
        /// relative paths. Discovered rather than listed, so that a new sample cannot be
        /// added without its configuration being validated.
        /// </summary>
        public static IReadOnlyList<string> EnumerateConfigurationFiles()
        {
            return Directory
                .EnumerateFiles(Root.FullName, "*.Config.xml", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .Select(RelativePathOf)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Directory names whose content is generated, not source.
        /// </summary>
        private static readonly string[] s_generatedDirectories = [".git", "bin", "obj", "TestResults"];

        private static bool IsBuildOutput(string path)
        {
            // the samples build into a shared bin directory in the repository root, so the
            // check has to look at every segment and not only at nested paths
            return RepositoryLayout.RelativePathOf(path)
                .Split('/')
                .Any(segment => s_generatedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
        }

        private static DirectoryInfo FindRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, kRootMarker)))
                {
                    return directory;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate the repository root ('{kRootMarker}') above '{AppContext.BaseDirectory}'.");
        }
    }
}
