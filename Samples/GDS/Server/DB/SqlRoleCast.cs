using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.DB
{
    public partial class SqlRole
    {
        #pragma warning disable CA2225 // Justification: Public sample API compatibility is preserved.
        public static explicit operator Role(SqlRole sqlRole)
        #pragma warning restore CA2225
        {
            if (sqlRole.RoleId != null)
            {
                return new Role(new NodeId((uint)sqlRole.RoleId, (ushort)sqlRole.NamespaceIndex), sqlRole.Name);
            }

            return new Role(NodeId.Null, sqlRole.Name);
        }

        #pragma warning disable CA2225 // Justification: Public sample API compatibility is preserved.
        public static explicit operator SqlRole(Role role)
        #pragma warning restore CA2225
        {
            return new SqlRole() {
                Id = Guid.NewGuid(),
                Name = role.Name,
                RoleId = (int?)(role.RoleId.Identifier as uint?),
                NamespaceIndex = role.RoleId.NamespaceIndex
            };
        }
    }
}
