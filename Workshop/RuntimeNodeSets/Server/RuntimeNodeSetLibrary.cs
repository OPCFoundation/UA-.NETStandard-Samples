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
using System.Reflection;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Quickstarts.RuntimeNodeSets.Server
{
    /// <summary>
    /// The NodeSet2 documents this sample ships, and the
    /// <see cref="RuntimeNodeSetOptions"/> built from them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything the server knows about the vendor model is in this class: a file name
    /// and a namespace URI. There is no generated code for it, no node manager class and
    /// no constants - the SDK reads the document and materializes the nodes.
    /// </para>
    /// <para>
    /// The two revisions carry the same <c>ModelUri</c>, which is what makes loading one
    /// over the other a reload of the same namespace rather than a second model.
    /// </para>
    /// </remarks>
    public sealed class RuntimeNodeSetLibrary
    {
        /// <summary>
        /// The namespace of the vendor model, the same in both revisions.
        /// </summary>
        public const string VendorNamespaceUri =
            "http://opcfoundation.org/UA/Quickstarts/RuntimeNodeSets/Line/";

        /// <summary>
        /// The namespace of the control model, which the sample never reloads.
        /// </summary>
        public const string ControlNamespaceUri =
            "http://opcfoundation.org/UA/Quickstarts/RuntimeNodeSets/Control/";

        /// <summary>
        /// The revision the server publishes when it starts.
        /// </summary>
        public const string InitialRevision = "Rev1";

        private static readonly IReadOnlyDictionary<string, string> s_files =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Rev1"] = "ConveyorLine.Rev1.NodeSet2.xml",
                ["Rev2"] = "ConveyorLine.Rev2.NodeSet2.xml",
            };

        private readonly string m_directory;

        /// <summary>
        /// Creates the library over the <c>NodeSets</c> directory next to the executable.
        /// </summary>
        public RuntimeNodeSetLibrary()
            : this(DefaultDirectory())
        {
        }

        /// <summary>
        /// Creates the library over a directory of NodeSet2 documents, for a host which
        /// runs the sample from somewhere else than its own output directory.
        /// </summary>
        /// <param name="directory">The directory the NodeSet2 files are in.</param>
        public RuntimeNodeSetLibrary(string directory)
        {
            m_directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        /// <summary>
        /// The revisions of the vendor model the server has a file for.
        /// </summary>
        public static IReadOnlyList<string> Revisions { get; } = s_files.Keys.ToArray();

        /// <summary>
        /// The full path of the NodeSet2 document of a revision.
        /// </summary>
        /// <param name="revision">One of <see cref="Revisions"/>.</param>
        /// <exception cref="ArgumentException">The revision is not one this sample ships.</exception>
        public string FilePath(string revision)
        {
            if (revision == null || !s_files.TryGetValue(revision, out string file))
            {
                throw new ArgumentException(
                    $"Unknown revision '{revision}'. Known revisions: {string.Join(", ", Revisions)}.",
                    nameof(revision));
            }

            return Path.Combine(m_directory, file);
        }

        /// <summary>
        /// The options which publish a revision of the vendor model.
        /// </summary>
        /// <remarks>
        /// <see cref="RuntimeNodeSetOptions.AllowLifecycleFromRequestCallback"/> is what
        /// lets the <c>Load</c>, <c>Reload</c> and <c>Remove</c> Methods of the control
        /// model act on this registration: without it the lifecycle refuses an operation
        /// which was started from a Client request, because it would wait for the very
        /// request that started it to complete. It is safe here for the reason the SDK
        /// names - the Methods are served by the control node manager, which is a
        /// different, stable one - and it would deadlock if the Methods lived on the
        /// vendor model itself.
        /// </remarks>
        /// <param name="revision">One of <see cref="Revisions"/>.</param>
        public RuntimeNodeSetOptions VendorOptions(string revision)
        {
            return new RuntimeNodeSetOptions
            {
                Sources = [RuntimeNodeSetSource.FromFile(FilePath(revision))],
                DefaultNamespaceUri = VendorNamespaceUri,
                AllowLifecycleFromRequestCallback = true,
            };
        }

        /// <summary>
        /// The path of the NodeSet2 document of the control model.
        /// </summary>
        public string ControlFilePath()
        {
            return Path.Combine(m_directory, "ModelControl.NodeSet2.xml");
        }

        /// <summary>
        /// The directory the sample keeps its NodeSet2 documents in: next to the
        /// executable, where the project copies them to.
        /// </summary>
        private static string DefaultDirectory()
        {
            string baseDirectory =
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? AppContext.BaseDirectory;

            return Path.Combine(baseDirectory, "NodeSets");
        }
    }
}
