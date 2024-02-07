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
                return new Role(new NodeId(sqlRole.RoleId), sqlRole.Name);
            }

            return new Role(NodeId.Null, sqlRole.Name);
        }

        public static explicit operator SqlRole(Role Role)
        {
            return new SqlRole() {
                Id = Guid.NewGuid(),
                Name = Role.Name,
                RoleId = Role.RoleId.Identifier as Guid?
            };
        }
    }
}
