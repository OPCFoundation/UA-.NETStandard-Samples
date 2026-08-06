# Task 04.03 — GDS Client & Server: Progress Details

## Summary
Retargeted both GDS applications to `net10.0-windows`. GDS Client was a straightforward retarget. **GDS Server required migrating its data layer from EF6 EDMX (Model-First) to EF Core 10** (user-chosen approach) because EDMX/ObjectContext is unsupported on .NET 5+.

## Projects
| Project | Result |
|---------|--------|
| Samples\GDS\Client\GlobalDiscoveryClient.csproj | ✅ Built |
| Samples\GDS\Server\GlobalDiscoveryServer.csproj | ✅ Built (EF6→EF Core) |

## GDS Client Changes
- TFM → `net10.0-windows`.
- Removed legacy `System.Configuration`, `System.IdentityModel`, `System.ServiceModel`, `System.Xml.Serialization` references.

## GDS Server: EF6 EDMX → EF Core Migration
### Project file
- TFM → `net10.0-windows`; removed ClickOnce/bootstrapper/publish properties, `win7-x64` RID block, `System.Data.Entity`/`System.IdentityModel`/`System.Security`/`System.ComponentModel.DataAnnotations`/`System.Core` references, `<EntityDeploy>` items, `.tt`/`.edmx`/`.edmx.diagram` items, `<Service>` and `<BootstrapperPackage>` groups.
- Swapped `EntityFramework 6.5.1` → `Microsoft.EntityFrameworkCore.SqlServer 10.0.10` + `Microsoft.EntityFrameworkCore.Design 10.0.10`; added `System.Configuration.ConfigurationManager 10.0.10`.
- Kept `gdsdb.edmx.sql` / `usersdb.edmx.sql` as EmbeddedResources (used at runtime for DB creation).

### DbContexts (rewritten as EF Core)
- `gdsdb.Context.cs` (`gdsdbEntities`) and `usersdb.Context.cs` (`usersdbEntities`) now derive from `Microsoft.EntityFrameworkCore.DbContext`.
- `OnConfiguring` reads connection string via `ConfigurationManager.ConnectionStrings` and calls `UseSqlServer`.
- `OnModelCreating` fluent config maps table names, keys, identity (`ValueGeneratedOnAdd` for int identity, `ValueGeneratedNever` for assigned Guid PKs), and relationships. **CertificateStore→Application** relationship mapped to the real shadow FK column `Application_ID` (not the plain `ApplicationId` int column), matching the original EDMX/DDL.

### App.config
- Replaced EDMX EntityClient connection strings with plain `Microsoft.Data.SqlClient` connection strings.
- Removed `entityFramework` configSection and provider block.

### Data-access code
- `SqlApplicationsDatabase.Initialize()` and `SqlUsersDatabase.Initialize()`: replaced EF6 `Database.Initialize(true)` / `CreateIfNotExists()` / `ExecuteSqlCommand(part)` with EF Core `Database.EnsureCreated()` + `Database.ExecuteSqlRaw(part)` (skipping blank GO-split segments). Added `using Microsoft.EntityFrameworkCore;`.
- Removed dead usings: `System.Data.Entity` (Program.cs), `System.Runtime.InteropServices.WindowsRuntime` (SqlRoleCast.cs).

### Deleted files
- Empty T4 placeholders: `gdsdb.cs`, `gdsdb.Designer.cs`, `usersdb.cs`, `usersdb.Designer.cs`.
- EDMX artifacts: `gdsdb.edmx(.diagram)`, `usersdb.edmx(.diagram)`, all `.tt` templates.

## User Verification Required (runtime — DB access needed)
EF6→EF Core differences surface at runtime; the following must be validated against a live LocalDB:
1. CRUD on Applications/ApplicationNames/ServerEndpoints/CertificateRequests/CertificateStores.
2. Users/Roles CRUD and cascade delete of roles with users.
3. `Initialize()` DB provisioning via the embedded `.edmx.sql` scripts.
4. Navigation loading — EF Core disables lazy loading by default; existing code uses in-scope navigation collections/`.Include`-free access. Confirm eager/explicit loading where needed.
5. CertificateStore.Application association resolves via `Application_ID`.

## Notes
- Remaining `NU1201` errors in a full-solution build are expected bottom-up consumer breakage from still-net48 sibling apps (tier 05). Both GDS projects build cleanly in isolation.
