/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Opc.Ua;

namespace Quickstarts.FileTransferServer
{
    /// <summary>
    /// Stores the configuration of the file system the server publishes.
    /// </summary>
    [DataType(Namespace = Namespaces.FileTransfer)]
    public sealed partial class FileTransferServerConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public FileTransferServerConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_rootDirectory = kDefaultRootDirectory;
            m_mountName = kDefaultMountName;
            m_writable = true;
        }
        #endregion

        #region Public Constants
        /// <summary>
        /// The directory published when the configuration does not name one.
        /// </summary>
        public const string kDefaultRootDirectory = @".\FileTransfer";

        /// <summary>
        /// The browse name of the mount when the configuration does not name one.
        /// </summary>
        public const string kDefaultMountName = "SampleFiles";
        #endregion

        #region Public Properties
        /// <summary>
        /// The directory which is published as the file system of the server.
        /// </summary>
        /// <remarks>
        /// A relative path is resolved against the working directory of the executable,
        /// and the directory is created if it does not exist yet. Everything below it is
        /// reachable by any client which is allowed to connect, and nothing above it is:
        /// the provider of the SDK rejects paths which leave the root.
        /// </remarks>
        [DataTypeField(Order = 1)]
        public string RootDirectory
        {
            get { return m_rootDirectory; }
            set { m_rootDirectory = value; }
        }

        /// <summary>
        /// The browse name the mount gets below the <c>Server/FileSystem</c> object.
        /// </summary>
        [DataTypeField(Order = 2)]
        public string MountName
        {
            get { return m_mountName; }
            set { m_mountName = value; }
        }

        /// <summary>
        /// Whether clients may create, write, move and delete.
        /// </summary>
        /// <remarks>
        /// A read only mount answers every write with <c>BadUserAccessDenied</c> without
        /// touching the disk.
        /// </remarks>
        [DataTypeField(Order = 3)]
        public bool Writable
        {
            get { return m_writable; }
            set { m_writable = value; }
        }
        #endregion

        #region Private Members
        private string m_rootDirectory;
        private string m_mountName;
        private bool m_writable;
        #endregion
    }
}
