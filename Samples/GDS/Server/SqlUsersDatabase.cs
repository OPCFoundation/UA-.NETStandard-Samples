using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Database.Sql;

namespace Opc.Ua.Gds.Server
{
    public class SqlUsersDatabase: IUsersDatabase
    {

        #region IUsersDatabase
        public void Initialize()
        {
            using (usersdbEntities entities = new usersdbEntities())
            {
                //only run initizailation logic if the database does not work -> throwS an exception
                try
                {
                    CheckCredentials("Test", "Test");
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Could not connect to the Database!");

                    var ie = e.InnerException;

                    while (ie != null)
                    {
                        Utils.LogInfo(ie, "");
                        ie = ie.InnerException;
                    }
                    Utils.LogInfo("Initialize Database tables!");
                    Assembly assembly = typeof(SqlApplicationsDatabase).GetTypeInfo().Assembly;
                    StreamReader istrm = new StreamReader(assembly.GetManifestResourceStream("Opc.Ua.Gds.Server.DB.usersdb.edmx.sql"));
                    string tables = istrm.ReadToEnd();
                    entities.Database.Initialize(true);
                    entities.Database.CreateIfNotExists();
                    var parts = tables.Split(new string[] { "GO" }, System.StringSplitOptions.None);
                    foreach (var part in parts) { entities.Database.ExecuteSqlCommand(part); }
                    entities.SaveChanges();
                    Utils.LogInfo("Database Initialized!");
                }
                
            }
        }

        public bool CreateUser(string userName, string password, GdsRole role)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName cannot be empty.", nameof(userName));
            }
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password cannot be empty.", nameof(password));
            }
            using (usersdbEntities entities = new usersdbEntities())
            {
                if (//User Exists
                entities.UserSet.SingleOrDefault(x => x.UserName == userName) != null)
                {
                    return false;
                }

                string hash = Hash(password);

                var user = new User { ID = Guid.NewGuid(), UserName = userName, Hash = hash, GdsRole = (int)role };

                entities.UserSet.Add(user);

                entities.SaveChanges();

                return true;
            }            
        }

        public bool DeleteUser(string userName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName cannot be empty.", nameof(userName));
            }
            using (usersdbEntities entities = new usersdbEntities())
            {
                var user = entities.UserSet.SingleOrDefault(x => x.UserName == userName);

                if (user == null)
                {
                    return false;
                }
                entities.UserSet.Remove(user);
                entities.SaveChanges();
                return true;
            }
        }

        public bool CheckCredentials(string userName, string password)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName cannot be empty.", nameof(userName));
            }
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password cannot be empty.", nameof(password));
            }
            using (usersdbEntities entities = new usersdbEntities())
            {
                var user = entities.UserSet.SingleOrDefault(x => x.UserName == userName);

                if (user == null)
                {
                    return false;
                }

                return Check(user.Hash, password);
            }
        }

        public GdsRole GetUserRole(string userName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName cannot be empty.", nameof(userName));
            }
            using (usersdbEntities entities = new usersdbEntities())
            {
                var user = entities.UserSet.SingleOrDefault(x => x.UserName == userName);

                if (user == null)
                {
                    throw new ArgumentException("No user found with the UserName " + userName);
                }

                return (GdsRole)user.GdsRole;
            }
        }

        public bool ChangePassword(string userName, string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName cannot be empty.", nameof(userName));
            }
            if (string.IsNullOrEmpty(oldPassword))
            {
                throw new ArgumentException("Current Password cannot be empty.", nameof(oldPassword));
            }
            if (string.IsNullOrEmpty(newPassword))
            {
                throw new ArgumentException("New Password cannot be empty.", nameof(newPassword));
            }
            using (usersdbEntities entities = new usersdbEntities())
            {
                var user = entities.UserSet.SingleOrDefault(x => x.UserName == userName);


                if (user == null)
                {
                    return false;
                }

                if (Check(user.Hash, oldPassword))
                {
                    var newHash = Hash(newPassword);
                    user.Hash = newHash;
                    entities.SaveChanges();
                    return true;
                }
                return false;
            }
        }
        #endregion

        #region IPasswordHasher
        private string Hash(string password)
        {
#if NETSTANDARD2_0 || NET462
#pragma warning disable CA5379 // Ensure Key Derivation Function algorithm is sufficiently strong
            using (var algorithm = new Rfc2898DeriveBytes(
                password,
                kSaltSize,
                kIterations))
            {
#pragma warning restore CA5379 // Ensure Key Derivation Function algorithm is sufficiently strong
#else
            using (var algorithm = new Rfc2898DeriveBytes(
                password,
                kSaltSize,
                kIterations,
                HashAlgorithmName.SHA512))
            {
#endif
                var key = Convert.ToBase64String(algorithm.GetBytes(kKeySize));
                var salt = Convert.ToBase64String(algorithm.Salt);

                return $"{kIterations}.{salt}.{key}";
            }
        }

        private bool Check(string hash, string password)
        {
            var separator = new Char[] { '.' };
            var parts = hash.Split(separator, 3);

            if (parts.Length != 3)
            {
                throw new FormatException("Unexpected hash format. " +
                  "Should be formatted as `{iterations}.{salt}.{hash}`");
            }

            var iterations = Convert.ToInt32(parts[0], CultureInfo.InvariantCulture.NumberFormat);
            var salt = Convert.FromBase64String(parts[1]);
            var key = Convert.FromBase64String(parts[2]);

#if NETSTANDARD2_0 || NET462
#pragma warning disable CA5379 // Ensure Key Derivation Function algorithm is sufficiently strong
            using (var algorithm = new Rfc2898DeriveBytes(
                password,
                salt,
                iterations))
            {
#pragma warning restore CA5379 // Ensure Key Derivation Function algorithm is sufficiently strong
#else
            using (var algorithm = new Rfc2898DeriveBytes(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA512))
            {
#endif
                var keyToCheck = algorithm.GetBytes(kKeySize);

                var verified = keyToCheck.SequenceEqual(key);

                return verified;
            }
        }

        #endregion
        #region Internal Members
       
        #endregion

        #region Internal Fields
        private const int kSaltSize = 16; // 128 bit
        private const int kIterations = 10000; // 10k
        private const int kKeySize = 32; // 256 bit

        #endregion
    }
}



