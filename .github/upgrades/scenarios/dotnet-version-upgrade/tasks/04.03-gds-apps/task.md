# 04.03-gds-apps: Retarget GDS Client & GDS Server

## Objective
Retarget `Samples\GDS\Client\GlobalDiscoveryClient.csproj` and `Samples\GDS\Server\GlobalDiscoveryServer.csproj` from net48 to net10.0-windows.

## Scope
- GlobalDiscoveryClient.csproj
- GlobalDiscoveryServer.csproj

## Steps
1. Set `<TargetFramework>net10.0-windows</TargetFramework>`, keep `<OutputType>WinExe</OutputType>`, add `<UseWindowsForms>true</UseWindowsForms>`.
2. Remove legacy framework `<Reference>`s, `<RequiredTargetFramework>` metadata, bootstrapper packages, binding redirects.
3. Fix API breaking changes inline; add Windows Compatibility Pack if needed.
4. Build each project; fix all warnings.

## Research: GDS Server EF6 EDMX → EF Core (user chose EF Core)
EDMX Model-First is unsupported on .NET 10. Two EDMX models: `gdsdb` (5 tables) and `usersdb` (2 tables). Entity POCOs are plain classes — reusable. Generated `*.Context.cs` derive from `System.Data.Entity.DbContext`.

### Schema (from embedded .edmx.sql — kept as EmbeddedResource for runtime DB init)
- Applications: PK int ID identity; ApplicationId Guid; uris/name/type; Certificate/HttpsCertificate varbinary(max).
- ApplicationNames: PK int ID identity; FK ApplicationId→Applications.ID; Locale, Text.
- ServerEndpoints: PK int ID identity; FK ApplicationId→Applications.ID; DiscoveryUrl.
- CertificateRequests: PK int ID identity; FK ApplicationId→Applications.ID; State, ids, CSR, subject...
- CertificateStores: PK int ID identity; Path, CertificateType, ApplicationId (plain int), **Application_ID (real FK→Applications.ID)**. Nav `Application` maps to shadow FK `Application_ID`.
- UserSet: PK Guid ID (assigned → ValueGeneratedNever); UserName, Hash.
- SqlRoleSet: PK Guid Id (assigned); RoleId int?; Name; FK UserID→UserSet.ID; NamespaceIndex.

### Steps (GDS Server)
1. csproj: TFM net10.0-windows; strip bootstrapper/ClickOnce/RID; remove System.Data.Entity/IdentityModel/Security/DataAnnotations/System.Core refs & EntityDeploy/tt/edmx/diagram items (keep `.edmx.sql`); swap EntityFramework → Microsoft.EntityFrameworkCore.SqlServer + Design; add System.Configuration.ConfigurationManager.
2. Rewrite both Context files as EF Core DbContext (OnConfiguring via ConfigurationManager conn string; OnModelCreating fluent config).
3. App.config: plain SqlClient connection strings; drop entityFramework/EDMX metadata.
4. Rewrite both Initialize() methods: EF Core Database.ExecuteSqlRaw over GO-split script (skip blanks).
5. Delete empty T4 placeholder files (gdsdb.cs/.Designer.cs, usersdb.cs/.Designer.cs).
6. Build & fix warnings.

## Done when
Both projects build on net10.0-windows with no removed-framework references and no System.Data.Entity usage.
