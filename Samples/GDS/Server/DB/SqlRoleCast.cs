using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server.DB
{
    public partial class SqlRole
    {
        public static explicit operator Role(SqlRole sqlRole)
        {
            if (sqlRole.RoleId != null)
            {
                return new Role(new NodeId((uint)sqlRole.RoleId, (ushort)sqlRole.NamespaceIndex), sqlRole.Name);
            }

            return new Role(NodeId.Null, sqlRole.Name);
        }

        public static explicit operator SqlRole(Role role)
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
