/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Quickstarts.RoleManagement.Server
{
    /// <summary>
    /// A node manager whose nodes are protected by OPC UA Part 18 RolePermissions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>[NodeManager]</c> attribute opts this partial class in to source generation:
    /// the generator emits a sibling partial which derives from <c>AsyncCustomNodeManager</c>,
    /// loads the predefined nodes generated from <c>ModelDesign.xml</c>, and calls
    /// <see cref="Configure"/> once the address space is in place.
    /// </para>
    /// <para>
    /// Everything this sample demonstrates happens in <see cref="Configure"/>: it writes a
    /// RolePermissions attribute onto each node of the model. The master node manager reads
    /// that attribute before it lets a service touch the node and answers BadUserAccessDenied
    /// unless one of the Roles the Session was granted carries the permission the service
    /// needs. No service handler in this node manager checks anything itself.
    /// </para>
    /// <para>
    /// Browse is a permission like any other, so a Role which is not granted Browse on a node
    /// does not see the node at all: an Observer browsing the machine finds three children,
    /// an Engineer finds five.
    /// </para>
    /// </remarks>
    [NodeManager]
    public partial class RoleManagementNodeManager
    {
        #region Constants
        /// <summary>
        /// The set point the Reset method restores.
        /// </summary>
        private const double kDefaultSetPoint = 20.0;
        #endregion

        #region Configure
        /// <summary>
        /// Seeds the values of the model and protects each of its nodes with RolePermissions.
        /// </summary>
        partial void Configure(IRoleManagementNodeManagerBuilder builder)
        {
            BaseVariableState temperature = builder.Machine.Temperature.Node;
            BaseVariableState calibration = builder.Machine.Calibration.Node;
            BaseVariableState maintenanceNote = builder.Machine.MaintenanceNote.Node;

            // the set point is the only node the sample keeps, because the Reset method has
            // to put it back
            m_setPoint = builder.Machine.SetPoint.Node;

            SetValue(temperature, Variant.From(21.5));
            SetValue(m_setPoint, Variant.From(kDefaultSetPoint));
            SetValue(calibration, Variant.From(0.25));
            SetValue(maintenanceNote, Variant.From("Filter replaced during the last service."));

            // Everybody who reaches the server may see that the machine is there. Without
            // Browse on the object itself none of its children would be reachable either.
            //
            // Note what the Anonymous Role means here: Part 18 4.3 gives it both the
            // Anonymous and the AuthenticatedUser identity criteria, so every Session holds
            // it. Naming it in a table is how a node says "anyone who got this far", and a
            // signed in Session holds AuthenticatedUser and its own Role on top of it.
            Protect(
                builder.Machine.Node,
                (Role.Anonymous, PermissionType.Browse),
                (Role.AuthenticatedUser, PermissionType.Browse),
                (Role.Observer, PermissionType.Browse),
                (Role.Operator, PermissionType.Browse),
                (Role.Engineer, PermissionType.Browse),
                (Role.Supervisor, PermissionType.Browse),
                (Role.SecurityAdmin, PermissionType.Browse));

            // The measurement: live data, so every Role which is meant to watch the machine
            // may read it. An anonymous Session sees the node but is refused the value.
            Protect(
                temperature,
                (Role.Anonymous, PermissionType.Browse),
                (Role.AuthenticatedUser, PermissionType.Browse),
                (Role.Observer, PermissionType.Browse | PermissionType.Read),
                (Role.Operator, PermissionType.Browse | PermissionType.Read),
                (Role.Engineer, PermissionType.Browse | PermissionType.Read),
                (Role.Supervisor, PermissionType.Browse | PermissionType.Read),
                (Role.SecurityAdmin, PermissionType.Browse));

            // Operational data: an Observer watches it, an Operator and an Engineer change it.
            Protect(
                m_setPoint,
                (Role.Anonymous, PermissionType.Browse),
                (Role.AuthenticatedUser, PermissionType.Browse),
                (Role.Observer, PermissionType.Browse | PermissionType.Read),
                (Role.Operator, PermissionType.Browse | PermissionType.Read | PermissionType.Write),
                (Role.Engineer, PermissionType.Browse | PermissionType.Read | PermissionType.Write),
                (Role.Supervisor, PermissionType.Browse | PermissionType.Read),
                (Role.SecurityAdmin, PermissionType.Browse));

            // Configuration data: an Engineer owns it, a Supervisor may look. Nobody else is
            // granted Browse, so for an Observer this node is not part of the address space.
            Protect(
                calibration,
                (Role.Operator, PermissionType.Browse),
                (Role.Engineer, PermissionType.Browse | PermissionType.Read | PermissionType.Write),
                (Role.Supervisor, PermissionType.Browse | PermissionType.Read));

            // The maintenance log: written by a Supervisor, readable by the two Roles which
            // work on the machine.
            Protect(
                maintenanceNote,
                (Role.Operator, PermissionType.Browse | PermissionType.Read),
                (Role.Engineer, PermissionType.Browse | PermissionType.Read),
                (Role.Supervisor, PermissionType.Browse | PermissionType.Read | PermissionType.Write));

            // Calling a Method needs the Call permission, which is separate from Write: an
            // Observer sees the method and is refused the call.
            Protect(
                builder.Machine.Reset.Node,
                (Role.Anonymous, PermissionType.Browse),
                (Role.AuthenticatedUser, PermissionType.Browse),
                (Role.Observer, PermissionType.Browse),
                (Role.Operator, PermissionType.Browse | PermissionType.Call),
                (Role.Engineer, PermissionType.Browse | PermissionType.Call),
                (Role.Supervisor, PermissionType.Browse),
                (Role.SecurityAdmin, PermissionType.Browse));

            builder.Machine.Reset.OnCall(ResetSetPoint);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Restores the default set point.
        /// </summary>
        /// <remarks>
        /// The handler runs only for a Session whose Roles carry the Call permission on the
        /// method node: the master node manager validates that before it dispatches the call.
        /// </remarks>
        private void ResetSetPoint()
        {
            SetValue(m_setPoint, Variant.From(kDefaultSetPoint));
        }

        /// <summary>
        /// Writes the RolePermissions attribute of a node, and reports the entries which
        /// apply to the calling Session as its UserRolePermissions.
        /// </summary>
        /// <remarks>
        /// <para>
        /// RolePermissions is the whole table and is the same for everyone;
        /// UserRolePermissions is the part of that table which the Roles of the reading
        /// Session earn. A client reads the second one to find out what it may do before it
        /// tries, which is what the sample client shows in its permission column.
        /// </para>
        /// <para>
        /// Reporting UserRolePermissions is not what enforces anything - the master node
        /// manager falls back to RolePermissions when a node reports no UserRolePermissions,
        /// and intersects the two when a node reports both, so the effective permissions are
        /// the same either way.
        /// </para>
        /// </remarks>
        private void Protect(NodeState node, params (Role Role, PermissionType Permissions)[] permissions)
        {
            node.RolePermissions = permissions
                .Select(entry => new RolePermissionType {
                    RoleId = ExpandedNodeId.ToNodeId(entry.Role.RoleId, Server.NamespaceUris),

                    // Reading the UserRolePermissions attribute needs the ReadRolePermissions
                    // permission of its own, so a Role which may see the node at all is given
                    // it: a client which cannot read the permissions cannot tell the user why
                    // something is refused, which is worse than telling it too much.
                    Permissions = (uint)(entry.Permissions | PermissionType.ReadRolePermissions),
                })
                .ToArrayOf();

            node.OnReadUserRolePermissions = OnReadUserRolePermissions;
        }

        /// <summary>
        /// Reports the entries of the node's RolePermissions which the calling Session earns.
        /// </summary>
        private static ServiceResult OnReadUserRolePermissions(
            ISystemContext context,
            NodeState node,
            ref ArrayOf<RolePermissionType> value)
        {
            // the effective identity of the Session, which the session manager wrapped in a
            // RoleBasedIdentity carrying the Roles the Role manager granted it on activation
            IUserIdentity identity = (context as ISessionSystemContext)?.UserIdentity;

            ArrayOf<NodeId> grantedRoleIds = identity?.GrantedRoleIds ?? default;

            var granted = new List<RolePermissionType>();

            foreach (RolePermissionType permission in node.RolePermissions.ToArray())
            {
                if (grantedRoleIds.Contains(permission.RoleId))
                {
                    granted.Add(permission);
                }
            }

            value = granted.ToArrayOf();

            return ServiceResult.Good;
        }

        /// <summary>
        /// Sets the value of a variable and stamps it with the current time.
        /// </summary>
        private void SetValue(BaseVariableState node, Variant value)
        {
            node.Value = value;
            node.Timestamp = DateTimeUtc.Now;
            node.ClearChangeMasks(SystemContext, false);
        }
        #endregion

        #region Private Fields
        private BaseVariableState m_setPoint;
        #endregion
    }
}
