/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Adds the OPC 10000-12 §7.2 <c>ApplicationAdmin</c> privilege to any
    /// <see cref="IUserDatabase"/> by storing, per user, the set of registered
    /// <c>ApplicationId</c>s that user is allowed to administer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ApplicationAdmin</c> sits between <c>DiscoveryAdmin</c> (may administer
    /// <em>every</em> application) and <c>ApplicationSelfAdmin</c> (may administer only the
    /// application whose certificate authenticated the secure channel): the holder may
    /// administer a configured <em>subset</em> of the registered applications.
    /// </para>
    /// <para>
    /// The stack does the rest on its own. <c>GlobalDiscoverySampleServer</c> probes the
    /// user database it was given for <see cref="IGdsUserDatabase"/> during user-name
    /// authentication and, when it finds one, seeds
    /// <c>GdsRoleBasedIdentity.AdministeredApplicationIds</c> from
    /// <see cref="GetAdministeredApplicationIds"/>. <c>AuthorizationHelper</c> then checks
    /// the target application of each Method call against that set. Nothing else has to be
    /// wired up: assigning <see cref="GdsRole.ApplicationAdmin"/> to the user and granting
    /// it a few application ids here is the whole story.
    /// </para>
    /// <para>
    /// This is a decorator rather than a subclass so the same implementation serves both
    /// sample servers - the console GDS on a <c>JsonUserDatabase</c> and the Windows GDS on
    /// its Entity Framework <c>SqlUsersDatabase</c> - without either backing store needing a
    /// schema change. The grants live in their own small JSON file next to the user
    /// database.
    /// </para>
    /// </remarks>
    public sealed class GdsApplicationAdminUserDatabase : IGdsUserDatabase
    {
        private static readonly JsonSerializerOptions s_jsonOptions =
            new JsonSerializerOptions { WriteIndented = true };

        private readonly IUserDatabase m_users;
        private readonly string m_grantsFilePath;
        private readonly object m_lock = new object();
        private readonly Dictionary<string, List<string>> m_grants =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>
        /// Wraps the supplied user database and loads the application-admin grants from
        /// <paramref name="grantsFilePath"/>, if the file exists.
        /// </summary>
        /// <param name="users">The user database that owns credentials and roles.</param>
        /// <param name="grantsFilePath">
        /// Path of the JSON file the grants are persisted in. Special folder names are
        /// expanded. The file is created on the first grant.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="users"/> is <c>null</c>.</exception>
        public GdsApplicationAdminUserDatabase(IUserDatabase users, string grantsFilePath)
        {
            m_users = users ?? throw new ArgumentNullException(nameof(users));
            m_grantsFilePath = Utils.ReplaceSpecialFolderNames(grantsFilePath) ?? grantsFilePath;
            Load();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Returns <c>null</c> - not an empty list - for a user with no grants, which is what
        /// the stack expects for "this user holds no ApplicationAdmin privilege".
        /// </remarks>
        public IReadOnlyList<NodeId> GetAdministeredApplicationIds(string userName)
        {
            if (String.IsNullOrEmpty(userName))
            {
                return null;
            }

            lock (m_lock)
            {
                if (!m_grants.TryGetValue(userName, out List<string> ids) || ids.Count == 0)
                {
                    return null;
                }

                var result = new List<NodeId>(ids.Count);

                foreach (string id in ids)
                {
                    try
                    {
                        result.Add(NodeId.Parse(id));
                    }
                    catch (ServiceResultException)
                    {
                        // a grant written for an application that has since been
                        // unregistered, or hand-edited into the file: skip it rather than
                        // failing the whole authentication.
                    }
                }

                return result.Count > 0 ? result : null;
            }
        }

        /// <summary>
        /// Grants <paramref name="userName"/> the right to administer the supplied
        /// application ids, replacing any previous grant for that user.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="userName"/> is <c>null</c>.</exception>
        public void GrantApplicationAdmin(string userName, IEnumerable<NodeId> applicationIds)
        {
            if (userName == null)
            {
                throw new ArgumentNullException(nameof(userName));
            }

            lock (m_lock)
            {
                m_grants[userName] = applicationIds?
                    .Where(id => id != null && !id.IsNull)
                    .Select(id => id.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .ToList() ?? new List<string>();

                Save();
            }
        }

        /// <summary>
        /// Removes every application-admin grant of <paramref name="userName"/>.
        /// </summary>
        public bool RevokeApplicationAdmin(string userName)
        {
            if (String.IsNullOrEmpty(userName))
            {
                return false;
            }

            lock (m_lock)
            {
                if (!m_grants.Remove(userName))
                {
                    return false;
                }

                Save();
                return true;
            }
        }

        /// <summary>
        /// A snapshot of every grant, keyed by user name.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Grants
        {
            get
            {
                lock (m_lock)
                {
                    return m_grants.ToDictionary(
                        entry => entry.Key,
                        entry => (IReadOnlyList<string>)entry.Value.ToList(),
                        StringComparer.Ordinal);
                }
            }
        }

        /// <inheritdoc/>
        public bool CreateUser(string userName, ReadOnlySpan<byte> password, ICollection<Role> roles)
        {
            return m_users.CreateUser(userName, password, roles);
        }

        /// <inheritdoc/>
        public bool DeleteUser(string userName)
        {
            RevokeApplicationAdmin(userName);
            return m_users.DeleteUser(userName);
        }

        /// <inheritdoc/>
        public bool CheckCredentials(string userName, ReadOnlySpan<byte> password)
        {
            return m_users.CheckCredentials(userName, password);
        }

        /// <inheritdoc/>
        public ICollection<Role> GetUserRoles(string userName)
        {
            return m_users.GetUserRoles(userName);
        }

        /// <inheritdoc/>
        public IReadOnlyList<UserManagementDataType> GetUsers()
        {
            return m_users.GetUsers();
        }

        /// <inheritdoc/>
        public bool ChangePassword(
            string userName,
            ReadOnlySpan<byte> oldPassword,
            ReadOnlySpan<byte> newPassword)
        {
            return m_users.ChangePassword(userName, oldPassword, newPassword);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(m_grantsFilePath))
                {
                    return;
                }

                Dictionary<string, List<string>> loaded =
                    JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                        File.ReadAllText(m_grantsFilePath));

                if (loaded == null)
                {
                    return;
                }

                foreach (KeyValuePair<string, List<string>> entry in loaded)
                {
                    m_grants[entry.Key] = entry.Value ?? new List<string>();
                }
            }
            catch (Exception)
            {
                // an unreadable or hand-corrupted grants file must not stop the GDS from
                // starting; it only costs the ApplicationAdmin grants.
                m_grants.Clear();
            }
        }

        private void Save()
        {
            string directory = Path.GetDirectoryName(m_grantsFilePath);

            if (!String.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                m_grantsFilePath,
                JsonSerializer.Serialize(m_grants, s_jsonOptions));
        }
    }
}
