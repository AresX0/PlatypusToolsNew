using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models
{
    /// <summary>
    /// Represents Active Directory domain information discovered during analysis.
    /// </summary>
    public class AdDomainInfo
    {
        public string DomainDn { get; set; } = string.Empty;
        public string DomainNetbiosName { get; set; } = string.Empty;
        public string DomainFqdn { get; set; } = string.Empty;
        public string ForestDn { get; set; } = string.Empty;
        public string ForestNetbiosName { get; set; } = string.Empty;
        public string ForestFqdn { get; set; } = string.Empty;
        public string DomainSid { get; set; } = string.Empty;
        public string ForestSid { get; set; } = string.Empty;
        public string PdcEmulator { get; set; } = string.Empty;
        public string ChosenDc { get; set; } = string.Empty;
        public string SysvolReplicationInfo { get; set; } = string.Empty;
        public bool IsAdRecycleBinEnabled { get; set; }
        public bool IsDomainJoined { get; set; }
        public bool IsRunningOnDc { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public Dictionary<string, string> FsmoRoles { get; set; } = new();
        public List<string> DomainControllers { get; set; } = new();
        public DateTime DiscoveryTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Represents a member of a privileged AD group.
    /// </summary>
    public class AdPrivilegedMember
    {
        public string SamAccountName { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string ObjectClass { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public DateTime? PasswordLastSet { get; set; }
        public DateTime? LastLogon { get; set; }
        public bool PasswordNeverExpires { get; set; }
        public bool TrustedForDelegation { get; set; }
        public bool HasSpn { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsNested { get; set; }
        public string NestedPath { get; set; } = string.Empty;
        public List<string> RiskyUacFlags { get; set; } = new();
    }

    /// <summary>
    /// Represents a risky ACL entry found on an AD object.
    /// </summary>
    public class AdRiskyAcl
    {
        public string ObjectDn { get; set; } = string.Empty;
        public string ObjectClass { get; set; } = string.Empty;
        public string IdentityReference { get; set; } = string.Empty;
        public string ActiveDirectoryRights { get; set; } = string.Empty;
        public string AccessControlType { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string ObjectTypeName { get; set; } = string.Empty;
        public string InheritedObjectType { get; set; } = string.Empty;
        public bool IsInherited { get; set; }
        public string Severity { get; set; } = "Medium";
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a risky GPO configuration.
    /// </summary>
    public class AdRiskyGpo
    {
        public string GpoName { get; set; } = string.Empty;
        public string GpoGuid { get; set; } = string.Empty;
        public DateTime CreatedTime { get; set; }
        public DateTime ModifiedTime { get; set; }
        public List<string> RiskySettings { get; set; } = new();
        public bool HasScheduledTasks { get; set; }
        public bool HasRegistryMods { get; set; }
        public bool HasFileOperations { get; set; }
        public bool HasSoftwareInstallation { get; set; }
        public bool HasLocalUserMods { get; set; }
        public bool HasEnvironmentMods { get; set; }
        public string Severity { get; set; } = "Medium";
    }

    /// <summary>
    /// Represents a risky file found in SYSVOL.
    /// </summary>
    public class SysvolRiskyFile
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public long FileSize { get; set; }
        public string Sha256Hash { get; set; } = string.Empty;
        public string Severity { get; set; } = "Medium";
    }

    /// <summary>
    /// Represents an account with Kerberos delegation configured.
    /// </summary>
    public class AdKerberosDelegation
    {
        public string SamAccountName { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string ObjectClass { get; set; } = string.Empty;
        public string DelegationType { get; set; } = string.Empty; // Unconstrained, Constrained, Resource-Based
        public List<string> AllowedToDelegateTo { get; set; } = new();
        public List<string> AllowedToActOnBehalfOf { get; set; } = new();
        public bool IsSensitive { get; set; }
        public string Severity { get; set; } = "High";
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents an account with AdminCount anomaly.
    /// </summary>
    public class AdAdminCountAnomaly
    {
        public string SamAccountName { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string ObjectClass { get; set; } = string.Empty;
        public int AdminCount { get; set; }
        public bool IsCurrentlyPrivileged { get; set; }
        public string Issue { get; set; } = string.Empty;
    }

    /// <summary>
    /// Overall AD Security Analysis result.
    /// </summary>
    public class AdSecurityAnalysisResult
    {
        public AdDomainInfo DomainInfo { get; set; } = new();
        public List<AdPrivilegedMember> PrivilegedMembers { get; set; } = new();
        public List<AdRiskyAcl> RiskyAcls { get; set; } = new();
        public List<AdRiskyGpo> RiskyGpos { get; set; } = new();
        public List<SysvolRiskyFile> SysvolRiskyFiles { get; set; } = new();
        public List<AdKerberosDelegation> KerberosDelegations { get; set; } = new();
        public List<AdAdminCountAnomaly> AdminCountAnomalies { get; set; } = new();
        
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        
        public int TotalFindings => 
            PrivilegedMembers.Count + 
            RiskyAcls.Count + 
            RiskyGpos.Count + 
            SysvolRiskyFiles.Count + 
            KerberosDelegations.Count + 
            AdminCountAnomalies.Count;

        public int CriticalCount { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public int LowCount { get; set; }

        public List<string> Errors { get; set; } = new();
        public bool IsComplete { get; set; }
    }

    /// <summary>
    /// Analysis options for AD Security scans.
    /// </summary>
    public class AdSecurityAnalysisOptions
    {
        public bool AnalyzePrivilegedGroups { get; set; } = true;
        public bool AnalyzeRiskyAcls { get; set; } = true;
        public bool AnalyzeGpos { get; set; } = true;
        public bool AnalyzeSysvol { get; set; } = true;
        public bool AnalyzeKerberosDelegation { get; set; } = true;
        public bool AnalyzeAdminCount { get; set; } = true;
        public bool IncludeForestMode { get; set; } = false;
        public bool FilterSafeIdentities { get; set; } = true;
        public int GpoDaysThreshold { get; set; } = 30;
        public string? TargetDomain { get; set; }
        public string? TargetDc { get; set; }
    }

    /// <summary>
    /// Well-known privileged group definitions.
    /// </summary>
    public static class WellKnownPrivilegedGroups
    {
        public static readonly string[] DomainGroups = new[]
        {
            "Domain Admins",
            "Enterprise Admins",
            "Schema Admins",
            "Account Operators",
            "Backup Operators",
            "Server Operators",
            "Print Operators",
            "Group Policy Creator Owners",
            "DNSAdmins",
            "DnsAdmins"
        };

        public static readonly Dictionary<string, string> WellKnownSids = new()
        {
            { "S-1-5-32-544", "Administrators" },
            { "S-1-5-32-548", "Account Operators" },
            { "S-1-5-32-549", "Server Operators" },
            { "S-1-5-32-550", "Print Operators" },
            { "S-1-5-32-551", "Backup Operators" },
            { "-512", "Domain Admins" },
            { "-516", "Domain Controllers" },
            { "-518", "Schema Admins" },
            { "-519", "Enterprise Admins" },
            { "-520", "Group Policy Creator Owners" }
        };
    }

    /// <summary>
    /// Risky AD rights that should trigger alerts.
    /// </summary>
    public static class RiskyAdRights
    {
        public static readonly string[] DangerousRights = new[]
        {
            "GenericAll",
            "GenericWrite",
            "WriteDacl",
            "WriteOwner",
            "AllExtendedRights",
            "WriteProperty"
        };

        public static readonly string[] DangerousExtendedRights = new[]
        {
            "DS-Replication-Get-Changes-All",
            "DS-Replication-Get-Changes",
            "User-Force-Change-Password",
            "Member",
            "GP-Link",
            "Allowed-To-Authenticate"
        };

        public static readonly string[] RiskyUacFlags = new[]
        {
            "DONT_REQ_PREAUTH",
            "ENCRYPTED_TEXT_PWD_ALLOWED",
            "PASSWD_NOTREQD",
            "USE_DES_KEY_ONLY",
            "TRUSTED_TO_AUTH_FOR_DELEGATION",
            "TRUSTED_FOR_DELEGATION",
            "DONT_EXPIRE_PASSWORD"
        };
    }

    /// <summary>
    /// Identities that are normally safe to have risky permissions.
    /// </summary>
    public static class SafeIdentities
    {
        public static readonly string[] SystemIdentities = new[]
        {
            "NT AUTHORITY\\SELF",
            "NT AUTHORITY\\SYSTEM",
            "Everyone",
            "Enterprise Read-only Domain Controllers",
            "Domain Admins",
            "Enterprise Admins",
            "Schema Admins",
            "Domain Controllers",
            "NT AUTHORITY\\Enterprise Domain Controllers",
            "BUILTIN\\Administrators"
        };
    }

    #region GPO and OU Models

    /// <summary>
    /// Represents a Group Policy Object for creation or analysis.
    /// </summary>
    public class AdGroupPolicy
    {
        public string Name { get; set; } = string.Empty;
        public string GpoGuid { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedTime { get; set; }
        public DateTime ModifiedTime { get; set; }
        public List<string> LinkedOUs { get; set; } = new();
        public string Description { get; set; } = string.Empty;
        public GpoSettings Settings { get; set; } = new();
    }

    /// <summary>
    /// GPO Settings container.
    /// </summary>
    public class GpoSettings
    {
        public bool ComputerEnabled { get; set; } = true;
        public bool UserEnabled { get; set; } = true;
        public List<string> SecuritySettings { get; set; } = new();
        public List<string> RegistrySettings { get; set; } = new();
        public List<string> ScriptSettings { get; set; } = new();
    }

    /// <summary>
    /// Represents an Organizational Unit.
    /// </summary>
    public class AdOrganizationalUnit
    {
        public string Name { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string ParentDn { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsProtectedFromDeletion { get; set; }
        public DateTime CreatedTime { get; set; }
        public int ChildCount { get; set; }
        public List<string> LinkedGpos { get; set; } = new();
    }

    /// <summary>
    /// Template for creating tiered admin model OUs (BILL model).
    /// </summary>
    public class BillOuTemplate
    {
        public string BaseName { get; set; } = "Admin";
        public string Tier0Name { get; set; } = "Tier0";
        public string Tier1Name { get; set; } = "Tier1";
        public string Tier2Name { get; set; } = "Tier2";
        public bool CreatePawOus { get; set; } = true;
        public bool CreateServiceAccountOus { get; set; } = true;
        public bool CreateGroupsOus { get; set; } = true;
    }

    /// <summary>
    /// Result of GPO/OU creation operations.
    /// </summary>
    public class AdObjectCreationResult
    {
        public bool Success { get; set; }
        public string ObjectType { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Exception? Error { get; set; }
    }

    #endregion

    #region Entra ID (Azure AD) Models

    /// <summary>
    /// Represents an Entra ID (formerly Azure AD) tenant.
    /// </summary>
    public class EntraIdTenant
    {
        public string TenantId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string DefaultDomain { get; set; } = string.Empty;
        public List<string> VerifiedDomains { get; set; } = new();
        public DateTime DiscoveryTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Represents a privileged role assignment in Entra ID.
    /// </summary>
    public class EntraIdPrivilegedRole
    {
        public string RoleId { get; set; } = string.Empty;
        public string RoleDisplayName { get; set; } = string.Empty;
        public string RoleTemplateId { get; set; } = string.Empty;
        public bool IsBuiltIn { get; set; }
        public List<EntraIdRoleMember> Members { get; set; } = new();
    }

    /// <summary>
    /// Represents a member of an Entra ID privileged role.
    /// </summary>
    public class EntraIdRoleMember
    {
        public string ObjectId { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty; // User, ServicePrincipal, Group
        public string DisplayName { get; set; } = string.Empty;
        public string UserPrincipalName { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public bool IsEligible { get; set; } // PIM eligible vs permanent
        public DateTime? AssignmentStart { get; set; }
        public DateTime? AssignmentEnd { get; set; }
        public string AssignmentType { get; set; } = "Permanent"; // Permanent, Eligible, Active
    }

    /// <summary>
    /// Represents a risky application in Entra ID.
    /// </summary>
    public class EntraIdRiskyApp
    {
        public string AppId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ObjectId { get; set; } = string.Empty;
        public List<EntraIdAppPermissionInfo> Permissions { get; set; } = new();
        public List<string> Owners { get; set; } = new();
        public DateTime? CreatedDateTime { get; set; }
        public string Severity { get; set; } = "Medium";
        public string RiskReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents an API permission for an Entra ID app.
    /// </summary>
    public class EntraIdAppPermissionInfo
    {
        public string PermissionId { get; set; } = string.Empty;
        public string PermissionName { get; set; } = string.Empty;
        public string ResourceAppId { get; set; } = string.Empty;
        public string ResourceDisplayName { get; set; } = string.Empty;
        public string PermissionType { get; set; } = string.Empty; // Delegated, Application
        public bool IsHighRisk { get; set; }
        public string ConsentType { get; set; } = string.Empty; // Admin, User
    }

    /// <summary>
    /// Represents a Conditional Access Policy in Entra ID.
    /// </summary>
    public class EntraIdConditionalAccessPolicy
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty; // Enabled, Disabled, EnabledForReportingButNotEnforced
        public DateTime? CreatedDateTime { get; set; }
        public DateTime? ModifiedDateTime { get; set; }
        public List<string> IncludedUsers { get; set; } = new();
        public List<string> ExcludedUsers { get; set; } = new();
        public List<string> IncludedApplications { get; set; } = new();
        public List<string> GrantControls { get; set; } = new();
        public string SessionControls { get; set; } = string.Empty;
    }

    /// <summary>
    /// Entra ID Security Analysis Result.
    /// </summary>
    public class EntraIdSecurityResult
    {
        public EntraIdTenant? Tenant { get; set; }
        public List<EntraIdPrivilegedRole> PrivilegedRoles { get; set; } = new();
        public List<EntraIdRiskyApp> RiskyApps { get; set; } = new();
        public List<EntraIdConditionalAccessPolicy> ConditionalAccessPolicies { get; set; } = new();
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public List<string> Errors { get; set; } = new();
        public bool IsComplete { get; set; }
        
        public int TotalPrivilegedUsers => PrivilegedRoles.SelectMany(r => r.Members).Count();
        public int HighRiskAppCount => RiskyApps.Count(a => a.Severity == "High" || a.Severity == "Critical");
    }

    /// <summary>
    /// Known risky Entra ID API permissions.
    /// </summary>
    public static class RiskyEntraIdPermissions
    {
        public static readonly Dictionary<string, string> HighRiskPermissions = new()
        {
            { "810c84a8-4a9e-49e6-bf7d-12d183f40d01", "Mail.Read" },
            { "e2a3a72e-5f79-4c64-b1b1-878b674786c9", "Mail.ReadWrite" },
            { "b633e1c5-b582-4048-a93e-9f11b44c7e96", "Mail.Send" },
            { "d56682ec-c09e-4743-aaf4-1a3aac4caa21", "Contacts.ReadWrite" },
            { "01d4889c-1287-42c6-ac1f-5d1e02578ef6", "Files.Read.All" },
            { "75359482-378d-4052-8f01-80520e7db3cd", "Files.ReadWrite.All" },
            { "7ab1d382-f21e-4acd-a863-ba3e13f7da61", "Directory.Read.All" },
            { "19dbc75e-c2e2-444c-a770-ec69d8559fc7", "Directory.ReadWrite.All" },
            { "62a82d76-70ea-41e2-9197-370581804d09", "Group.ReadWrite.All" },
            { "9e3f62cf-ca93-4989-b6ce-bf83c28f9fe8", "RoleManagement.ReadWrite.Directory" },
            { "06b708a9-e830-4db3-a914-8e69da51d44f", "AppRoleAssignment.ReadWrite.All" },
            { "741f803b-c850-494e-b5df-cde7c675a1ca", "User.ReadWrite.All" }
        };

        public static readonly string[] CriticalPermissions = new[]
        {
            "Directory.ReadWrite.All",
            "RoleManagement.ReadWrite.Directory",
            "AppRoleAssignment.ReadWrite.All",
            "Application.ReadWrite.All",
            "Mail.ReadWrite",
            "Mail.Send"
        };
    }

    #endregion

    #region Analysis Database Models

    /// <summary>
    /// Represents a stored analysis run for historical tracking.
    /// </summary>
    public class StoredAnalysisRun
    {
        public long Id { get; set; }
        public string AnalysisType { get; set; } = string.Empty; // AD, EntraID, Combined
        public DateTime RunTime { get; set; }
        public string TargetDomain { get; set; } = string.Empty;
        public string TargetTenantId { get; set; } = string.Empty;
        public int TotalFindings { get; set; }
        public int CriticalCount { get; set; }
        public int HighCount { get; set; }
        public int MediumCount { get; set; }
        public int LowCount { get; set; }
        public double DurationSeconds { get; set; }
        public bool IsComplete { get; set; }
        public string ResultJson { get; set; } = string.Empty;
    }

    #endregion
}
