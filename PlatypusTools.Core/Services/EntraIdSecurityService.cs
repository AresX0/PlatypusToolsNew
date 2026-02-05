using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using PlatypusTools.Core.Models;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Entra ID (Azure AD) Security Analysis Service.
    /// Provides security assessment capabilities for Microsoft Entra ID tenants.
    /// Implements functionality similar to PLATYPUS Azure analysis functions.
    /// </summary>
    public class EntraIdSecurityService
    {
        private readonly IProgress<string>? _progress;
        private GraphServiceClient? _graphClient;
        private string? _tenantId;

        public EntraIdSecurityService(IProgress<string>? progress = null)
        {
            _progress = progress;
        }

        #region Authentication

        /// <summary>
        /// Connects to Microsoft Graph using interactive browser authentication.
        /// </summary>
        public async Task<bool> ConnectAsync(string tenantId, CancellationToken ct = default)
        {
            try
            {
                _tenantId = tenantId;
                _progress?.Report($"Connecting to Entra ID tenant: {tenantId}...");

                // Use interactive browser authentication
                var options = new InteractiveBrowserCredentialOptions
                {
                    TenantId = tenantId,
                    ClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e", // Microsoft Graph PowerShell client ID
                    AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
                    RedirectUri = new Uri("http://localhost")
                };

                var credential = new InteractiveBrowserCredential(options);
                
                _graphClient = new GraphServiceClient(credential, new[] { 
                    "https://graph.microsoft.com/.default" 
                });

                // Test connection by getting organization info
                var org = await _graphClient.Organization.GetAsync(cancellationToken: ct);
                if (org?.Value?.FirstOrDefault() != null)
                {
                    _progress?.Report($"Connected to tenant: {org.Value.First().DisplayName}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _progress?.Report($"Failed to connect to Entra ID: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Connects using device code flow (useful for headless scenarios).
        /// </summary>
        public async Task<bool> ConnectWithDeviceCodeAsync(string tenantId, CancellationToken ct = default)
        {
            try
            {
                _tenantId = tenantId;
                _progress?.Report($"Initiating device code authentication for tenant: {tenantId}...");

                var options = new DeviceCodeCredentialOptions
                {
                    TenantId = tenantId,
                    ClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e",
                    DeviceCodeCallback = (code, cancellation) =>
                    {
                        _progress?.Report(code.Message);
                        return Task.CompletedTask;
                    }
                };

                var credential = new DeviceCodeCredential(options);
                _graphClient = new GraphServiceClient(credential);

                var org = await _graphClient.Organization.GetAsync(cancellationToken: ct);
                if (org?.Value?.FirstOrDefault() != null)
                {
                    _progress?.Report($"Connected to tenant: {org.Value.First().DisplayName}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _progress?.Report($"Failed to connect to Entra ID: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if we're connected to Microsoft Graph.
        /// </summary>
        public bool IsConnected => _graphClient != null;

        /// <summary>
        /// Disconnects from Microsoft Graph and clears the client.
        /// </summary>
        public void Disconnect()
        {
            _graphClient = null;
            _tenantId = null;
            _progress?.Report("Disconnected from Entra ID");
        }

        #endregion

        #region Tenant Discovery

        /// <summary>
        /// Gets basic tenant information.
        /// </summary>
        public async Task<EntraIdTenant?> GetTenantInfoAsync(CancellationToken ct = default)
        {
            if (_graphClient == null)
            {
                _progress?.Report("Not connected to Entra ID. Call ConnectAsync first.");
                return null;
            }

            try
            {
                _progress?.Report("Getting tenant information...");

                var org = await _graphClient.Organization.GetAsync(cancellationToken: ct);
                var orgInfo = org?.Value?.FirstOrDefault();

                if (orgInfo == null)
                    return null;

                var domains = await _graphClient.Domains.GetAsync(cancellationToken: ct);

                return new EntraIdTenant
                {
                    TenantId = orgInfo.Id ?? "",
                    DisplayName = orgInfo.DisplayName ?? "",
                    DefaultDomain = domains?.Value?.FirstOrDefault(d => d.IsDefault == true)?.Id ?? "",
                    VerifiedDomains = domains?.Value?.Where(d => d.IsVerified == true).Select(d => d.Id ?? "").ToList() ?? new List<string>(),
                    DiscoveryTime = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                _progress?.Report($"Error getting tenant info: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Privileged Role Analysis

        /// <summary>
        /// Gets all privileged role assignments in the tenant.
        /// Mirrors PLATYPUS Get-AzureAdPrivObjects functionality.
        /// </summary>
        public async Task<List<EntraIdPrivilegedRole>> GetPrivilegedRolesAsync(
            bool usersOnly = false, 
            CancellationToken ct = default)
        {
            if (_graphClient == null)
            {
                _progress?.Report("Not connected to Entra ID. Call ConnectAsync first.");
                return new List<EntraIdPrivilegedRole>();
            }

            var privilegedRoles = new List<EntraIdPrivilegedRole>();

            try
            {
                _progress?.Report("Getting privileged role assignments...");

                // Get all directory roles (activated roles)
                var directoryRoles = await _graphClient.DirectoryRoles.GetAsync(cancellationToken: ct);

                if (directoryRoles?.Value == null)
                    return privilegedRoles;

                // Filter for admin roles
                var adminRoles = directoryRoles.Value
                    .Where(r => r.DisplayName?.Contains("Administrator") == true || 
                                r.DisplayName?.Contains("Admin") == true ||
                                IsPrivilegedRole(r.DisplayName))
                    .ToList();

                int processed = 0;
                foreach (var role in adminRoles)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var privilegedRole = new EntraIdPrivilegedRole
                        {
                            RoleId = role.Id ?? "",
                            RoleDisplayName = role.DisplayName ?? "",
                            RoleTemplateId = role.RoleTemplateId ?? "",
                            IsBuiltIn = true,
                            Members = new List<EntraIdRoleMember>()
                        };

                        // Get role members
                        var members = await _graphClient.DirectoryRoles[role.Id].Members.GetAsync(cancellationToken: ct);

                        if (members?.Value != null)
                        {
                            foreach (var member in members.Value)
                            {
                                var roleMember = await CreateRoleMemberAsync(member, role.DisplayName ?? "", usersOnly, ct);
                                if (roleMember != null)
                                {
                                    privilegedRole.Members.Add(roleMember);
                                }
                            }
                        }

                        if (privilegedRole.Members.Count > 0)
                        {
                            privilegedRoles.Add(privilegedRole);
                        }

                        processed++;
                        if (processed % 5 == 0)
                        {
                            _progress?.Report($"Processed {processed}/{adminRoles.Count} roles...");
                        }
                    }
                    catch (Exception ex)
                    {
                        _progress?.Report($"Error processing role {role.DisplayName}: {ex.Message}");
                    }
                }

                _progress?.Report($"Found {privilegedRoles.Sum(r => r.Members.Count)} privileged role assignments");
            }
            catch (Exception ex)
            {
                _progress?.Report($"Error getting privileged roles: {ex.Message}");
            }

            return privilegedRoles;
        }

        private async Task<EntraIdRoleMember?> CreateRoleMemberAsync(
            DirectoryObject member, 
            string roleName, 
            bool usersOnly,
            CancellationToken ct)
        {
            try
            {
                var odataType = member.OdataType ?? "";
                var objectType = odataType.Split('.').LastOrDefault() ?? "Unknown";

                if (usersOnly && objectType != "user")
                    return null;

                var roleMember = new EntraIdRoleMember
                {
                    ObjectId = member.Id ?? "",
                    ObjectType = objectType,
                    RoleName = roleName,
                    AssignmentType = "Permanent"
                };

                // Get additional details based on type
                if (objectType == "user" && _graphClient != null)
                {
                    try
                    {
                        var user = await _graphClient.Users[member.Id].GetAsync(cancellationToken: ct);
                        if (user != null)
                        {
                            roleMember.DisplayName = user.DisplayName ?? "";
                            roleMember.UserPrincipalName = user.UserPrincipalName ?? "";
                        }
                    }
                    catch { }
                }
                else if (objectType == "servicePrincipal" && _graphClient != null)
                {
                    try
                    {
                        var sp = await _graphClient.ServicePrincipals[member.Id].GetAsync(cancellationToken: ct);
                        if (sp != null)
                        {
                            roleMember.DisplayName = sp.DisplayName ?? "";
                            roleMember.UserPrincipalName = sp.AppId ?? "";
                        }
                    }
                    catch { }
                }
                else if (objectType == "group" && _graphClient != null)
                {
                    try
                    {
                        var group = await _graphClient.Groups[member.Id].GetAsync(cancellationToken: ct);
                        if (group != null)
                        {
                            roleMember.DisplayName = group.DisplayName ?? "";
                        }
                    }
                    catch { }
                }

                return roleMember;
            }
            catch
            {
                return null;
            }
        }

        private bool IsPrivilegedRole(string? roleName)
        {
            // Use the comprehensive privileged roles list from the models
            return EntraIdPrivilegedRoles.IsPrivilegedRole(roleName);
        }

        #endregion

        #region PIM Analysis

        /// <summary>
        /// Analyzes PIM settings to find roles with permanent (active) assignments
        /// that should be eligible only. Returns role assignments that violate 
        /// PIM best practices (permanent assignments to privileged roles).
        /// </summary>
        public async Task<List<EntraIdPimViolation>> GetPimViolationsAsync(CancellationToken ct = default)
        {
            if (_graphClient == null)
            {
                _progress?.Report("Not connected to Entra ID. Call ConnectAsync first.");
                return new List<EntraIdPimViolation>();
            }

            var violations = new List<EntraIdPimViolation>();

            try
            {
                _progress?.Report("Analyzing PIM settings for permanent role assignments...");

                // Get all directory roles
                var roles = await _graphClient.DirectoryRoles.GetAsync(cancellationToken: ct);
                if (roles?.Value == null)
                    return violations;

                _progress?.Report($"Analyzing {roles.Value.Count} active roles...");

                foreach (var role in roles.Value)
                {
                    ct.ThrowIfCancellationRequested();

                    if (role == null || string.IsNullOrEmpty(role.Id))
                        continue;

                    var roleName = role.DisplayName ?? "Unknown";
                    
                    // Check if this role should never have permanent assignments
                    bool shouldBePimOnly = EntraIdPrivilegedRoles.ShouldNeverBePermanent(roleName);

                    // Get members of this role
                    var members = await _graphClient.DirectoryRoles[role.Id].Members.GetAsync(cancellationToken: ct);
                    if (members?.Value == null || members.Value.Count == 0)
                        continue;

                    foreach (var member in members.Value)
                    {
                        if (member == null)
                            continue;

                        // All direct role assignments are "Active/Permanent" 
                        // (vs PIM eligible which requires activation)
                        var violation = new EntraIdPimViolation
                        {
                            RoleId = role.RoleTemplateId ?? role.Id,
                            RoleName = roleName,
                            RoleDescription = role.Description ?? "",
                            PrincipalId = member.Id ?? "",
                            AssignmentType = "Permanent",
                            ViolationType = shouldBePimOnly ? "Critical" : "Warning",
                            Recommendation = shouldBePimOnly 
                                ? $"Remove permanent assignment. {roleName} should ONLY have PIM eligible assignments."
                                : $"Consider converting to PIM eligible assignment for better security."
                        };

                        // Get member details
                        if (member is Microsoft.Graph.Models.User user)
                        {
                            violation.PrincipalType = "User";
                            violation.PrincipalDisplayName = user.DisplayName ?? "";
                            violation.PrincipalUpn = user.UserPrincipalName ?? "";
                        }
                        else if (member is Microsoft.Graph.Models.ServicePrincipal sp)
                        {
                            violation.PrincipalType = "ServicePrincipal";
                            violation.PrincipalDisplayName = sp.DisplayName ?? "";
                            violation.PrincipalUpn = sp.AppId ?? "";
                        }
                        else if (member is Microsoft.Graph.Models.Group grp)
                        {
                            violation.PrincipalType = "Group";
                            violation.PrincipalDisplayName = grp.DisplayName ?? "";
                            violation.PrincipalUpn = grp.Id ?? "";
                        }
                        else
                        {
                            violation.PrincipalType = member.OdataType?.Replace("#microsoft.graph.", "") ?? "Unknown";
                            violation.PrincipalDisplayName = member.Id ?? "";
                        }

                        violations.Add(violation);
                        
                        if (shouldBePimOnly)
                        {
                            _progress?.Report($"⚠️ PIM VIOLATION: {violation.PrincipalDisplayName} has PERMANENT {roleName} assignment");
                        }
                    }
                }

                _progress?.Report($"Found {violations.Count(v => v.ViolationType == "Critical")} critical PIM violations and {violations.Count(v => v.ViolationType == "Warning")} warnings");
                return violations;
            }
            catch (Exception ex)
            {
                _progress?.Report($"Error analyzing PIM settings: {ex.Message}");
                return violations;
            }
        }

        #endregion

        #region Risky Applications Analysis

        /// <summary>
        /// Gets applications with risky API permissions.
        /// Mirrors PLATYPUS Get-AzureAdRiskyApps functionality.
        /// </summary>
        public async Task<List<EntraIdRiskyApp>> GetRiskyAppsAsync(CancellationToken ct = default)
        {
            if (_graphClient == null)
            {
                _progress?.Report("Not connected to Entra ID. Call ConnectAsync first.");
                return new List<EntraIdRiskyApp>();
            }

            var riskyApps = new List<EntraIdRiskyApp>();

            try
            {
                _progress?.Report("Scanning applications for risky API permissions...");

                // Get all applications
                var applications = await _graphClient.Applications.GetAsync(cancellationToken: ct);

                if (applications?.Value == null)
                    return riskyApps;

                _progress?.Report($"Found {applications.Value.Count} applications to analyze...");

                int processed = 0;
                foreach (var app in applications.Value)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var riskyPermissions = AnalyzeAppPermissions(app);

                        if (riskyPermissions.Count > 0)
                        {
                            var riskyApp = new EntraIdRiskyApp
                            {
                                AppId = app.AppId ?? "",
                                DisplayName = app.DisplayName ?? "",
                                ObjectId = app.Id ?? "",
                                CreatedDateTime = app.CreatedDateTime?.UtcDateTime,
                                Permissions = riskyPermissions,
                                Owners = new List<string>()
                            };

                            // Determine severity based on permissions
                            riskyApp.Severity = DetermineAppSeverity(riskyPermissions);
                            riskyApp.RiskReason = string.Join(", ", riskyPermissions.Select(p => p.PermissionName).Distinct());

                            // Get owners
                            try
                            {
                                var owners = await _graphClient.Applications[app.Id].Owners.GetAsync(cancellationToken: ct);
                                if (owners?.Value != null)
                                {
                                    foreach (var owner in owners.Value)
                                    {
                                        if (owner is User user)
                                        {
                                            riskyApp.Owners.Add(user.UserPrincipalName ?? user.DisplayName ?? owner.Id ?? "");
                                        }
                                    }
                                }
                            }
                            catch { }

                            riskyApps.Add(riskyApp);
                        }

                        processed++;
                        if (processed % 50 == 0)
                        {
                            _progress?.Report($"Analyzed {processed}/{applications.Value.Count} applications...");
                        }
                    }
                    catch (Exception ex)
                    {
                        _progress?.Report($"Error analyzing app {app.DisplayName}: {ex.Message}");
                    }
                }

                _progress?.Report($"Found {riskyApps.Count} applications with risky permissions");
            }
            catch (Exception ex)
            {
                _progress?.Report($"Error scanning applications: {ex.Message}");
            }

            return riskyApps.OrderByDescending(a => a.Severity == "Critical")
                           .ThenByDescending(a => a.Severity == "High")
                           .ToList();
        }

        private List<EntraIdAppPermissionInfo> AnalyzeAppPermissions(Application app)
        {
            var riskyPermissions = new List<EntraIdAppPermissionInfo>();

            if (app.RequiredResourceAccess == null)
                return riskyPermissions;

            foreach (var resource in app.RequiredResourceAccess)
            {
                if (resource.ResourceAccess == null)
                    continue;

                foreach (var permission in resource.ResourceAccess)
                {
                    var permId = permission.Id?.ToString() ?? "";
                    
                    if (RiskyEntraIdPermissions.HighRiskPermissions.TryGetValue(permId, out var permName))
                    {
                        var permInfo = new EntraIdAppPermissionInfo
                        {
                            PermissionId = permId,
                            PermissionName = permName,
                            PermissionDescription = RiskyEntraIdPermissions.GetPermissionDescription(permName),
                            ResourceAppId = resource.ResourceAppId?.ToString() ?? "",
                            PermissionType = permission.Type == "Role" ? "Application" : "Delegated",
                            IsHighRisk = true,
                            ConsentType = permission.Type == "Role" ? "Admin" : "User"
                        };

                        riskyPermissions.Add(permInfo);
                    }
                }
            }

            return riskyPermissions;
        }

        private string DetermineAppSeverity(List<EntraIdAppPermissionInfo> permissions)
        {
            var permNames = permissions.Select(p => p.PermissionName).ToHashSet();

            // Critical: Directory write, role management, or app management
            if (permNames.Any(p => RiskyEntraIdPermissions.CriticalPermissions.Contains(p)))
                return "Critical";

            // High: Mail access, file access with write
            if (permNames.Any(p => p.Contains("Mail") || p.Contains("Files.ReadWrite")))
                return "High";

            // Medium: Read-only sensitive access
            return "Medium";
        }

        #endregion

        #region Conditional Access Policy Analysis

        /// <summary>
        /// Gets all Conditional Access policies.
        /// Mirrors PLATYPUS Get-AzureAdCAPolicies functionality.
        /// </summary>
        public async Task<List<EntraIdConditionalAccessPolicy>> GetConditionalAccessPoliciesAsync(
            CancellationToken ct = default)
        {
            if (_graphClient == null)
            {
                _progress?.Report("Not connected to Entra ID. Call ConnectAsync first.");
                return new List<EntraIdConditionalAccessPolicy>();
            }

            var policies = new List<EntraIdConditionalAccessPolicy>();

            try
            {
                _progress?.Report("Getting Conditional Access policies...");

                var caPolicies = await _graphClient.Identity.ConditionalAccess.Policies.GetAsync(cancellationToken: ct);

                if (caPolicies?.Value == null)
                    return policies;

                foreach (var caPolicy in caPolicies.Value)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var policy = new EntraIdConditionalAccessPolicy
                        {
                            Id = caPolicy.Id ?? "",
                            DisplayName = caPolicy.DisplayName ?? "",
                            State = caPolicy.State?.ToString() ?? "Unknown",
                            CreatedDateTime = caPolicy.CreatedDateTime?.UtcDateTime,
                            ModifiedDateTime = caPolicy.ModifiedDateTime?.UtcDateTime
                        };

                        // Parse conditions
                        if (caPolicy.Conditions?.Users != null)
                        {
                            policy.IncludedUsers = caPolicy.Conditions.Users.IncludeUsers?.ToList() ?? new List<string>();
                            policy.ExcludedUsers = caPolicy.Conditions.Users.ExcludeUsers?.ToList() ?? new List<string>();
                        }

                        if (caPolicy.Conditions?.Applications != null)
                        {
                            policy.IncludedApplications = caPolicy.Conditions.Applications.IncludeApplications?.ToList() ?? new List<string>();
                        }

                        // Parse grant controls
                        if (caPolicy.GrantControls != null)
                        {
                            policy.GrantControls = caPolicy.GrantControls.BuiltInControls?
                                .Select(c => c.ToString() ?? "")
                                .ToList() ?? new List<string>();
                        }

                        // Parse session controls
                        if (caPolicy.SessionControls != null)
                        {
                            var sessionControls = new List<string>();
                            if (caPolicy.SessionControls.SignInFrequency != null)
                                sessionControls.Add($"SignInFrequency: {caPolicy.SessionControls.SignInFrequency.Value} {caPolicy.SessionControls.SignInFrequency.Type}");
                            if (caPolicy.SessionControls.PersistentBrowser != null)
                                sessionControls.Add($"PersistentBrowser: {caPolicy.SessionControls.PersistentBrowser.Mode}");
                            policy.SessionControls = string.Join("; ", sessionControls);
                        }

                        policies.Add(policy);
                    }
                    catch (Exception ex)
                    {
                        _progress?.Report($"Error parsing CA policy {caPolicy.DisplayName}: {ex.Message}");
                    }
                }

                _progress?.Report($"Found {policies.Count} Conditional Access policies");
            }
            catch (Exception ex)
            {
                _progress?.Report($"Error getting CA policies: {ex.Message}");
            }

            return policies;
        }

        #endregion

        #region Full Analysis

        /// <summary>
        /// Runs a complete Entra ID security analysis.
        /// </summary>
        public async Task<EntraIdSecurityResult> RunFullAnalysisAsync(
            string tenantId,
            bool usersOnly = false,
            CancellationToken ct = default)
        {
            var result = new EntraIdSecurityResult
            {
                StartTime = DateTime.Now
            };

            try
            {
                // Connect if not already connected
                if (!IsConnected || _tenantId != tenantId)
                {
                    var connected = await ConnectAsync(tenantId, ct);
                    if (!connected)
                    {
                        result.Errors.Add("Failed to connect to Entra ID");
                        return result;
                    }
                }

                // Get tenant info
                result.Tenant = await GetTenantInfoAsync(ct);

                // Get privileged roles
                _progress?.Report("Analyzing privileged role assignments...");
                result.PrivilegedRoles = await GetPrivilegedRolesAsync(usersOnly, ct);

                // Get risky apps
                _progress?.Report("Analyzing application permissions...");
                result.RiskyApps = await GetRiskyAppsAsync(ct);

                // Get CA policies
                _progress?.Report("Getting Conditional Access policies...");
                result.ConditionalAccessPolicies = await GetConditionalAccessPoliciesAsync(ct);

                result.IsComplete = true;
            }
            catch (OperationCanceledException)
            {
                result.Errors.Add("Analysis was cancelled");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Analysis error: {ex.Message}");
            }
            finally
            {
                result.EndTime = DateTime.Now;
            }

            _progress?.Report($"Entra ID analysis complete. " +
                $"Privileged users: {result.TotalPrivilegedUsers}, " +
                $"Risky apps: {result.RiskyApps.Count}, " +
                $"CA policies: {result.ConditionalAccessPolicies.Count}");

            return result;
        }

        #endregion

        #region Tenant Takeback / Remediation Methods (PLATYPUS IR Operations)

        /// <summary>
        /// Performs a tenant takeback operation - resets passwords, revokes sessions, 
        /// and optionally removes users from privileged roles.
        /// Equivalent to Invoke-AzureAdTenantTakeBack in PLATYPUS.
        /// </summary>
        public async Task<TenantTakebackResult> TakebackTenantAsync(
            TenantTakebackOptions options,
            CancellationToken ct = default)
        {
            var result = new TenantTakebackResult
            {
                StartTime = DateTime.Now,
                TenantId = options.TenantId
            };

            if (_graphClient == null)
            {
                _progress?.Report("Not connected to Entra ID. Call ConnectAsync first.");
                result.Errors.Add("Not connected to Entra ID");
                return result;
            }

            try
            {
                _progress?.Report("Starting tenant takeback operation...");
                _progress?.Report($"Exempted users: {string.Join(", ", options.ExemptedUserUpns)}");

                // Get all privileged role assignments
                var privilegedRoles = await GetPrivilegedRolesAsync(true, ct);
                var allPrivilegedMembers = privilegedRoles.SelectMany(r => r.Members).ToList();

                _progress?.Report($"Found {allPrivilegedMembers.Count} privileged role members");

                foreach (var member in allPrivilegedMembers)
                {
                    ct.ThrowIfCancellationRequested();

                    // Skip exempted users
                    if (options.ExemptedUserUpns.Any(e => 
                        e.Equals(member.UserPrincipalName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _progress?.Report($"Skipping exempted user: {member.UserPrincipalName}");
                        result.SkippedUsers.Add(member.UserPrincipalName);
                        continue;
                    }

                    // Skip external users (#EXT#)
                    if (member.UserPrincipalName.Contains("#EXT#"))
                    {
                        _progress?.Report($"Skipping external user: {member.UserPrincipalName}");
                        result.SkippedUsers.Add(member.UserPrincipalName);
                        continue;
                    }

                    var userResult = new UserTakebackResult
                    {
                        UserPrincipalName = member.UserPrincipalName,
                        ObjectId = member.ObjectId
                    };

                    try
                    {
                        if (!options.WhatIf)
                        {
                            // 1. Reset password
                            if (options.ResetPasswords)
                            {
                                var newPassword = GenerateSecurePassword();
                                await ResetUserPasswordAsync(member.ObjectId, newPassword, ct);
                                userResult.PasswordReset = true;
                                userResult.NewPassword = options.SavePasswordsToResult ? newPassword : "[REDACTED]";
                                _progress?.Report($"Password reset for: {member.UserPrincipalName}");
                            }

                            // 2. Revoke sessions
                            if (options.RevokeSessions)
                            {
                                await RevokeUserSessionsAsync(member.ObjectId, ct);
                                userResult.SessionsRevoked = true;
                                _progress?.Report($"Sessions revoked for: {member.UserPrincipalName}");
                            }

                            // 3. Remove from roles
                            if (options.RemoveFromRoles)
                            {
                                await RemoveUserFromAllPrivilegedRolesAsync(member.ObjectId, ct);
                                userResult.RolesRemoved = true;
                                _progress?.Report($"Removed from roles: {member.UserPrincipalName}");
                            }
                        }
                        else
                        {
                            _progress?.Report($"[WHATIF] Would process: {member.UserPrincipalName}");
                            userResult.WhatIfOnly = true;
                        }

                        result.ProcessedUsers.Add(userResult);
                    }
                    catch (Exception ex)
                    {
                        userResult.Error = ex.Message;
                        result.ProcessedUsers.Add(userResult);
                        _progress?.Report($"Error processing {member.UserPrincipalName}: {ex.Message}");
                    }
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Takeback error: {ex.Message}");
                _progress?.Report($"Takeback failed: {ex.Message}");
            }
            finally
            {
                result.EndTime = DateTime.Now;
            }

            return result;
        }

        /// <summary>
        /// Resets password for all users in tenant (mass password reset).
        /// Equivalent to Invoke-EntraMassPasswordReset in PLATYPUS.
        /// </summary>
        public async Task<MassPasswordResetResult> MassPasswordResetAsync(
            List<string> exemptedUserUpns,
            bool savePasswords = false,
            bool whatIf = true,
            CancellationToken ct = default)
        {
            var result = new MassPasswordResetResult { StartTime = DateTime.Now };

            if (_graphClient == null)
            {
                result.Errors.Add("Not connected to Entra ID");
                return result;
            }

            try
            {
                _progress?.Report("Starting mass password reset...");

                // Get all users (excluding guests)
                var users = await _graphClient.Users.GetAsync(r => r.QueryParameters.Select = 
                    new[] { "id", "userPrincipalName", "userType" }, ct);

                if (users?.Value == null)
                {
                    result.Errors.Add("No users found");
                    return result;
                }

                var allUsers = users.Value.ToList();
                
                // Handle pagination
                var pageIterator = users.OdataNextLink;
                while (!string.IsNullOrEmpty(pageIterator))
                {
                    ct.ThrowIfCancellationRequested();
                    var nextPage = await _graphClient.Users
                        .WithUrl(pageIterator)
                        .GetAsync(cancellationToken: ct);
                    if (nextPage?.Value != null)
                    {
                        allUsers.AddRange(nextPage.Value);
                    }
                    pageIterator = nextPage?.OdataNextLink;
                }

                _progress?.Report($"Found {allUsers.Count} total users");

                foreach (var user in allUsers)
                {
                    ct.ThrowIfCancellationRequested();

                    if (user.UserPrincipalName == null) continue;

                    // Skip external users
                    if (user.UserPrincipalName.Contains("#EXT#"))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    // Skip exempted users
                    if (exemptedUserUpns.Any(e => 
                        e.Equals(user.UserPrincipalName, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    try
                    {
                        if (!whatIf)
                        {
                            var newPassword = GenerateSecurePassword();
                            await ResetUserPasswordAsync(user.Id!, newPassword, ct);
                            
                            if (savePasswords)
                            {
                                result.ResetPasswords[user.UserPrincipalName] = newPassword;
                            }
                        }
                        result.ResetCount++;
                    }
                    catch
                    {
                        result.FailedCount++;
                    }
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
            }
            finally
            {
                result.EndTime = DateTime.Now;
            }

            return result;
        }

        /// <summary>
        /// Revokes all user refresh tokens in the tenant.
        /// Equivalent to Revoke-EntraAllUserRefreshTokens in PLATYPUS.
        /// </summary>
        public async Task<int> RevokeAllUserTokensAsync(
            List<string>? exemptedUserUpns = null,
            bool whatIf = true,
            CancellationToken ct = default)
        {
            if (_graphClient == null) return 0;

            exemptedUserUpns ??= new List<string>();
            int revokedCount = 0;

            try
            {
                _progress?.Report("Revoking all user refresh tokens...");

                var users = await _graphClient.Users.GetAsync(r => 
                    r.QueryParameters.Select = new[] { "id", "userPrincipalName" }, ct);

                if (users?.Value == null) return 0;

                foreach (var user in users.Value)
                {
                    ct.ThrowIfCancellationRequested();

                    if (user.UserPrincipalName == null) continue;

                    if (exemptedUserUpns.Any(e => 
                        e.Equals(user.UserPrincipalName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (!whatIf)
                    {
                        await RevokeUserSessionsAsync(user.Id!, ct);
                    }
                    revokedCount++;
                }

                _progress?.Report($"Revoked tokens for {revokedCount} users");
            }
            catch (Exception ex)
            {
                _progress?.Report($"Error revoking tokens: {ex.Message}");
            }

            return revokedCount;
        }

        /// <summary>
        /// Removes all members from specified privileged roles.
        /// Equivalent to Remove-EntraPrivilegedRoleMembers in PLATYPUS.
        /// </summary>
        public async Task<int> RemovePrivilegedRoleMembersAsync(
            List<string> roleNames,
            List<string> exemptedUserUpns,
            bool whatIf = true,
            CancellationToken ct = default)
        {
            if (_graphClient == null) return 0;

            int removedCount = 0;

            try
            {
                var directoryRoles = await _graphClient.DirectoryRoles.GetAsync(cancellationToken: ct);
                if (directoryRoles?.Value == null) return 0;

                foreach (var role in directoryRoles.Value)
                {
                    ct.ThrowIfCancellationRequested();

                    if (role.DisplayName == null || !roleNames.Contains(role.DisplayName))
                        continue;

                    _progress?.Report($"Processing role: {role.DisplayName}");

                    var members = await _graphClient.DirectoryRoles[role.Id].Members.GetAsync(cancellationToken: ct);
                    if (members?.Value == null) continue;

                    foreach (var member in members.Value)
                    {
                        if (member.Id == null) continue;

                        // Get UPN for exemption check
                        try
                        {
                            var user = await _graphClient.Users[member.Id].GetAsync(cancellationToken: ct);
                            if (user?.UserPrincipalName != null && 
                                exemptedUserUpns.Any(e => e.Equals(user.UserPrincipalName, StringComparison.OrdinalIgnoreCase)))
                            {
                                _progress?.Report($"Skipping exempted user: {user.UserPrincipalName}");
                                continue;
                            }

                            if (!whatIf)
                            {
                                await _graphClient.DirectoryRoles[role.Id].Members[member.Id].Ref.DeleteAsync(cancellationToken: ct);
                                _progress?.Report($"Removed {user?.UserPrincipalName ?? member.Id} from {role.DisplayName}");
                            }
                            removedCount++;
                        }
                        catch { /* Not a user or access denied */ }
                    }
                }
            }
            catch (Exception ex)
            {
                _progress?.Report($"Error removing role members: {ex.Message}");
            }

            return removedCount;
        }

        /// <summary>
        /// Removes owners from applications (for malicious app cleanup).
        /// Equivalent to Remove-EntraAppOwners in PLATYPUS.
        /// </summary>
        public async Task<int> RemoveAppOwnersAsync(
            string appId,
            List<string>? ownerIdsToRemove = null,
            bool whatIf = true,
            CancellationToken ct = default)
        {
            if (_graphClient == null) return 0;

            int removedCount = 0;

            try
            {
                var app = await _graphClient.Applications
                    .GetAsync(r => r.QueryParameters.Filter = $"appId eq '{appId}'", ct);

                if (app?.Value == null || app.Value.Count == 0)
                {
                    _progress?.Report($"Application not found: {appId}");
                    return 0;
                }

                var appObjectId = app.Value[0].Id;
                var owners = await _graphClient.Applications[appObjectId].Owners.GetAsync(cancellationToken: ct);

                if (owners?.Value == null) return 0;

                foreach (var owner in owners.Value)
                {
                    ct.ThrowIfCancellationRequested();

                    if (owner.Id == null) continue;

                    // Remove specific owners or all owners
                    if (ownerIdsToRemove == null || ownerIdsToRemove.Contains(owner.Id))
                    {
                        if (!whatIf)
                        {
                            await _graphClient.Applications[appObjectId].Owners[owner.Id].Ref.DeleteAsync(cancellationToken: ct);
                            _progress?.Report($"Removed owner {owner.Id} from app {appId}");
                        }
                        removedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                _progress?.Report($"Error removing app owners: {ex.Message}");
            }

            return removedCount;
        }

        /// <summary>
        /// Disables Conditional Access policies (for IR scenarios).
        /// Equivalent to disable-oldcapolicies in PLATYPUS.
        /// </summary>
        public async Task<int> DisableConditionalAccessPoliciesAsync(
            List<string>? policyIdsToExempt = null,
            bool whatIf = true,
            CancellationToken ct = default)
        {
            if (_graphClient == null) return 0;

            policyIdsToExempt ??= new List<string>();
            int disabledCount = 0;

            try
            {
                var policies = await _graphClient.Identity.ConditionalAccess.Policies.GetAsync(cancellationToken: ct);
                if (policies?.Value == null) return 0;

                foreach (var policy in policies.Value)
                {
                    ct.ThrowIfCancellationRequested();

                    if (policy.Id == null || policyIdsToExempt.Contains(policy.Id))
                        continue;

                    if (policy.State?.ToString() == "enabled")
                    {
                        if (!whatIf)
                        {
                            policy.State = Microsoft.Graph.Models.ConditionalAccessPolicyState.Disabled;
                            await _graphClient.Identity.ConditionalAccess.Policies[policy.Id].PatchAsync(policy, cancellationToken: ct);
                            _progress?.Report($"Disabled CA policy: {policy.DisplayName}");
                        }
                        disabledCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                _progress?.Report($"Error disabling CA policies: {ex.Message}");
            }

            return disabledCount;
        }

        #endregion

        #region Helper Methods

        private async Task ResetUserPasswordAsync(string userId, string newPassword, CancellationToken ct)
        {
            if (_graphClient == null) return;

            var passwordProfile = new Microsoft.Graph.Models.PasswordProfile
            {
                Password = newPassword,
                ForceChangePasswordNextSignIn = true
            };

            var user = new Microsoft.Graph.Models.User { PasswordProfile = passwordProfile };
            await _graphClient.Users[userId].PatchAsync(user, cancellationToken: ct);
        }

        private async Task RevokeUserSessionsAsync(string userId, CancellationToken ct)
        {
            if (_graphClient == null) return;
            await _graphClient.Users[userId].RevokeSignInSessions.PostAsync(cancellationToken: ct);
        }

        private async Task RemoveUserFromAllPrivilegedRolesAsync(string userId, CancellationToken ct)
        {
            if (_graphClient == null) return;

            var memberOf = await _graphClient.Users[userId].MemberOf.GetAsync(cancellationToken: ct);
            if (memberOf?.Value == null) return;

            foreach (var membership in memberOf.Value)
            {
                if (membership.OdataType == "#microsoft.graph.directoryRole" && membership.Id != null)
                {
                    try
                    {
                        await _graphClient.DirectoryRoles[membership.Id].Members[userId].Ref.DeleteAsync(cancellationToken: ct);
                    }
                    catch { /* Role removal failed, might not be removable */ }
                }
            }
        }

        private static string GenerateSecurePassword(int length = 16)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        #endregion
    }
}
