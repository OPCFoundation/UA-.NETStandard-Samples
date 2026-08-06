# 05.12-userauthentication — Progress Details

## Objective
Retarget the Workshop UserAuthentication Client & Server to `net10.0-windows` and resolve
the unsupported WIF/WCF security dependencies.

## Result
| Project | Target | Build |
|---|---|---|
| `Workshop\UserAuthentication\Server\UserAuthentication Server.csproj` | `net10.0-windows` | ✅ Succeeded — 0 warnings, 0 errors |
| `Workshop\UserAuthentication\Client\UserAuthentication Client.csproj` | `net10.0-windows` | ✅ Succeeded — 0 project warnings, 0 errors |

> Note: a single MSB3884 (`AllRules.ruleset` not found) warning originates from the
> dependency project `Samples\ClientControls.Net4\UA Client Controls.csproj`, not from
> the UserAuthentication projects. It is out of scope for this task.

## Compatibility Decision — Option C (WIF/WCF stub-out)
The projects depend on `System.IdentityModel.*`, `System.ServiceModel.Security`, Kerberos
token types, and `System.Security.Principal.WindowsImpersonationContext`, none of which have
a .NET 10 port or Windows Compatibility Pack equivalent. Per the user-approved **Option C**,
these paths were stubbed / adapted so the projects compile while keeping username/password
and certificate authentication functional.

### csproj changes (both projects)
- Retargeted `net48` → `net10.0-windows`; kept `UseWindowsForms=true` and `OutputType=WinExe`.
- Removed ClickOnce/bootstrapper items, legacy framework references, `ImportWindowsDesktopTargets`,
  `UseVSHostingProcess`, `CodeAnalysisRuleSet`, and the `win7-x64` RID block.

### Server — `UserAuthenticationServer.cs`
- Removed `System.IdentityModel.Selectors`, `System.IdentityModel.Tokens`, and
  `System.ServiceModel.Security` usings; added `System.Security.Cryptography`.
- `X509CertificateValidator m_certificateValidator` field replaced with a
  `bool m_certificateValidatorEnabled` flag.
- `VerifyCertificate` now uses standard `System.Security.Cryptography.X509Certificates.X509Chain`
  validation instead of `X509CertificateValidator.PeerTrust`.
- `CreateSecurityTokenResolver`, `ParseAndVerifyKerberosToken`, and
  `LogonUser(OperationContext, UserNameSecurityToken)` are stubbed to throw
  `NotSupportedException` (Kerberos / WS-Security / impersonation).
- `ImpersonationContext` no longer holds a `WindowsImpersonationContext`; `OnRequestComplete`
  no longer calls `.Context.Undo()`.

### Client — `MainForm.cs`
- Removed all `System.IdentityModel.*` usings.
- `CreateSAMLTokenAsync` and `GetKerberosToken` stubbed to throw `NotSupportedException`
  (SAML / Kerberos issued tokens). Username/password + certificate auth paths remain intact.

## Validation
- `dotnet build` for both projects: **Build succeeded**, 0 errors.
