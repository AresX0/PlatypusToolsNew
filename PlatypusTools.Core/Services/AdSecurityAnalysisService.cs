using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models;

#pragma warning disable CA2000 // Dispose objects before losing scope - using var handles disposal correctly

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Active Directory Security Analysis Service.
    /// Provides incident response and security assessment capabilities similar to PLATYPUS.
    /// Implemented in native C#/.NET for Windows platform.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class AdSecurityAnalysisService
    {
        private readonly IProgress<string>? _progress;
        private AdDomainInfo? _domainInfo;
        private Dictionary<Guid, string> _schemaGuids = new();
        private Dictionary<Guid, string> _extendedRightsGuids = new();

        // Risky file extensions to scan in SYSVOL
        private static readonly HashSet<string> RiskyExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".bat", ".cmd", ".js", ".vbs", ".ps1", ".wsf", ".hta", ".msi", ".msp"
        };

        public AdSecurityAnalysisService(IProgress<string>? progress = null)
        {
            _progress = progress;
        }

        #region Domain Discovery

        /// <summary>
        /// Checks if the current machine is domain joined.
        /// </summary>
        public bool IsDomainJoined()
        {
            try
            {
                return Domain.GetComputerDomain() != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Discovers Active Directory domain information.
        /// </summary>
        public async Task<AdDomainInfo> DiscoverDomainAsync(string? domainName = null, string? dcName = null, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                _progress?.Report("Discovering Active Directory domain...");

                var info = new AdDomainInfo
                {
                    Hostname = Environment.MachineName,
                    DiscoveryTime = DateTime.Now
                };

                try
                {
                    // Check if domain joined
                    info.IsDomainJoined = IsDomainJoined();

                    if (!info.IsDomainJoined && string.IsNullOrEmpty(domainName))
                    {
                        _progress?.Report("Machine is not domain joined. Specify a target domain.");
                        _domainInfo = info;
                        return info;
                    }

                    // Get domain context
                    Domain domain;
                    if (!string.IsNullOrEmpty(domainName))
                    {
                        var context = new DirectoryContext(DirectoryContextType.Domain, domainName);
                        domain = Domain.GetDomain(context);
                    }
                    else
                    {
                        domain = Domain.GetComputerDomain();
                    }

                    info.DomainFqdn = domain.Name;
                    info.DomainDn = DomainToDn(domain.Name);

                    // Get a usable DC
                    if (!string.IsNullOrEmpty(dcName))
                    {
                        if (TestDcConnection(dcName))
                        {
                            info.ChosenDc = dcName;
                        }
                        else
                        {
                            _progress?.Report($"Specified DC {dcName} is not reachable. Finding alternative...");
                        }
                    }

                    if (string.IsNullOrEmpty(info.ChosenDc))
                    {
                        info.ChosenDc = FindReachableDc(domain);
                    }

                    if (string.IsNullOrEmpty(info.ChosenDc))
                    {
                        _progress?.Report("Could not find a reachable domain controller.");
                        _domainInfo = info;
                        return info;
                    }

                    _progress?.Report($"Using domain controller: {info.ChosenDc}");

                    // Get additional domain details
                    using var domainEntry = new DirectoryEntry($"LDAP://{info.ChosenDc}/{info.DomainDn}");
                    var domainSearcher = new DirectorySearcher(domainEntry)
                    {
                        Filter = "(objectClass=domain)",
                        SearchScope = SearchScope.Base
                    };
                    domainSearcher.PropertiesToLoad.AddRange(new[] { "objectSid", "whenCreated" });

                    var domainResult = domainSearcher.FindOne();
                    if (domainResult != null)
                    {
                        var sidBytes = domainResult.Properties["objectSid"]?[0] as byte[];
                        if (sidBytes != null)
                        {
                            var sid = new SecurityIdentifier(sidBytes, 0);
                            info.DomainSid = sid.Value;
                        }
                    }

                    // Get forest info
                    var forest = domain.Forest;
                    info.ForestFqdn = forest.Name;
                    info.ForestDn = DomainToDn(forest.Name);

                    // Get FSMO roles
                    try
                    {
                        info.FsmoRoles["PDCEmulator"] = domain.PdcRoleOwner?.Name ?? "Unknown";
                        info.FsmoRoles["RIDMaster"] = domain.RidRoleOwner?.Name ?? "Unknown";
                        info.FsmoRoles["InfrastructureMaster"] = domain.InfrastructureRoleOwner?.Name ?? "Unknown";
                        info.FsmoRoles["SchemaMaster"] = forest.SchemaRoleOwner?.Name ?? "Unknown";
                        info.FsmoRoles["DomainNamingMaster"] = forest.NamingRoleOwner?.Name ?? "Unknown";
                        info.PdcEmulator = info.FsmoRoles["PDCEmulator"];
                    }
                    catch (Exception ex)
                    {
                        _progress?.Report($"Warning: Could not retrieve all FSMO roles: {ex.Message}");
                    }

                    // Get domain controllers
                    foreach (DomainController dc in domain.DomainControllers)
                    {
                        info.DomainControllers.Add(dc.Name);
                    }

                    // Check if running on a DC
                    info.IsRunningOnDc = info.DomainControllers.Any(dc => 
                        dc.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                        dc.StartsWith(Environment.MachineName + ".", StringComparison.OrdinalIgnoreCase));

                    // Check AD Recycle Bin
                    info.IsAdRecycleBinEnabled = CheckAdRecycleBin(info.ChosenDc, info.ForestDn);

                    // Check SYSVOL replication
                    info.SysvolReplicationInfo = GetSysvolReplicationInfo(info.ChosenDc, info.DomainDn);

                    _progress?.Report($"Domain discovery complete: {info.DomainFqdn}");
                }
                catch (Exception ex)
                {
                    _progress?.Report($"Error discovering domain: {ex.Message}");
                }

                _domainInfo = info;
                return info;
            }, ct);
        }

        private string FindReachableDc(Domain domain)
        {
            // Try PDC first
            try
            {
                var pdc = domain.PdcRoleOwner;
                if (pdc != null && TestDcConnection(pdc.Name))
                {
                    return pdc.Name;
                }
            }
            catch { }

            // Try other DCs
            foreach (DomainController dc in domain.DomainControllers)
            {
                if (TestDcConnection(dc.Name))
                {
                    return dc.Name;
                }
            }

            return string.Empty;
        }

        private bool TestDcConnection(string dcName, int port = 389, int timeoutMs = 3000)
        {
            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect(dcName, port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs));
                
                if (success)
                {
                    client.EndConnect(result);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private string DomainToDn(string domainName)
        {
            var parts = domainName.Split('.');
            return string.Join(",", parts.Select(p => $"DC={p}"));
        }

        private bool CheckAdRecycleBin(string dc, string forestDn)
        {
            try
            {
                var configDn = $"CN=Configuration,{forestDn}";
                using var entry = new DirectoryEntry($"LDAP://{dc}/CN=Recycle Bin Feature,CN=Optional Features,CN=Directory Service,CN=Windows NT,{configDn}");
                using var searcher = new DirectorySearcher(entry)
                {
                    Filter = "(objectClass=msDS-OptionalFeature)",
                    SearchScope = SearchScope.Base
                };
                searcher.PropertiesToLoad.Add("msDS-EnabledFeatureBL");
                
                var result = searcher.FindOne();
                return result?.Properties["msDS-EnabledFeatureBL"]?.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private string GetSysvolReplicationInfo(string dc, string domainDn)
        {
            try
            {
                // Check for DFSR
                var dcOu = $"OU=Domain Controllers,{domainDn}";
                using var entry = new DirectoryEntry($"LDAP://{dc}/{dcOu}");
                using var searcher = new DirectorySearcher(entry)
                {
                    Filter = $"(&(objectClass=computer)(dNSHostName={dc}))",
                    SearchScope = SearchScope.OneLevel
                };

                var dcResult = searcher.FindOne();
                if (dcResult != null)
                {
                    // Look for DFSR subscription
                    using var dcEntry = dcResult.GetDirectoryEntry();
                    using var dfsrSearcher = new DirectorySearcher(dcEntry)
                    {
                        Filter = "(&(objectClass=msDFSR-Subscription)(name=SYSVOL Subscription))",
                        SearchScope = SearchScope.Subtree
                    };

                    if (dfsrSearcher.FindOne() != null)
                    {
                        return "SYSVOL is using DFSR replication";
                    }

                    // Look for FRS
                    using var frsSearcher = new DirectorySearcher(dcEntry)
                    {
                        Filter = "(&(objectClass=nTFRSSubscriber)(name=Domain System Volume (SYSVOL share)))",
                        SearchScope = SearchScope.Subtree
                    };

                    if (frsSearcher.FindOne() != null)
                    {
                        return "SYSVOL is using FRS replication (should migrate to DFSR)";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Could not determine SYSVOL replication: {ex.Message}";
            }

            return "Unknown SYSVOL replication type";
        }

        #endregion

        #region Schema GUID Resolution

        private void LoadSchemaGuids(string dc, string forestDn)
        {
            try
            {
                _progress?.Report("Loading schema GUIDs...");
                
                var configDn = $"CN=Configuration,{forestDn}";
                var schemaDn = $"CN=Schema,{configDn}";

                // Load schema object GUIDs
                using var schemaEntry = new DirectoryEntry($"LDAP://{dc}/{schemaDn}");
                using var schemaSearcher = new DirectorySearcher(schemaEntry)
                {
                    Filter = "(schemaIDGUID=*)",
                    SearchScope = SearchScope.OneLevel,
                    PageSize = 1000
                };
                schemaSearcher.PropertiesToLoad.AddRange(new[] { "name", "schemaIDGUID" });

                foreach (SearchResult result in schemaSearcher.FindAll())
                {
                    var name = result.Properties["name"]?[0]?.ToString();
                    var guidBytes = result.Properties["schemaIDGUID"]?[0] as byte[];
                    if (name != null && guidBytes != null)
                    {
                        var guid = new Guid(guidBytes);
                        _schemaGuids[guid] = name;
                    }
                }

                // Load extended rights GUIDs
                var extendedRightsDn = $"CN=Extended-Rights,{configDn}";
                using var extEntry = new DirectoryEntry($"LDAP://{dc}/{extendedRightsDn}");
                using var extSearcher = new DirectorySearcher(extEntry)
                {
                    Filter = "(objectClass=controlAccessRight)",
                    SearchScope = SearchScope.OneLevel,
                    PageSize = 1000
                };
                extSearcher.PropertiesToLoad.AddRange(new[] { "name", "rightsGuid" });

                foreach (SearchResult result in extSearcher.FindAll())
                {
                    var name = result.Properties["name"]?[0]?.ToString();
                    var guidStr = result.Properties["rightsGuid"]?[0]?.ToString();
                    if (name != null && !string.IsNullOrEmpty(guidStr) && Guid.TryParse(guidStr, out var guid))
                    {
                        _extendedRightsGuids[guid] = name;
                    }
                }

                _progress?.Report($"Loaded {_schemaGuids.Count} schema GUIDs and {_extendedRightsGuids.Count} extended rights");
            }
            catch (Exception ex)
            {
                _progress?.Report($"Warning: Could not load all schema GUIDs: {ex.Message}");
            }
        }

        private string ResolveGuid(Guid guid)
        {
            if (guid == Guid.Empty)
                return "All";

            if (_schemaGuids.TryGetValue(guid, out var schemaName))
                return schemaName;

            if (_extendedRightsGuids.TryGetValue(guid, out var extName))
                return extName;

            return guid.ToString();
        }

        #endregion

        #region Privileged Role Analysis

        /// <summary>
        /// Gets all members of privileged AD groups.
        /// </summary>
        public async Task<List<AdPrivilegedMember>> GetPrivilegedMembersAsync(
            bool forestMode = false, 
            CancellationToken ct = default)
        {
            if (_domainInfo == null)
            {
                await DiscoverDomainAsync(ct: ct);
            }

            if (_domainInfo == null || string.IsNullOrEmpty(_domainInfo.ChosenDc))
            {
                return new List<AdPrivilegedMember>();
            }

            return await Task.Run(() =>
            {
                var members = new List<AdPrivilegedMember>();
                var searchedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                _progress?.Report("Analyzing privileged group memberships...");

                foreach (var groupName in WellKnownPrivilegedGroups.DomainGroups)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        _progress?.Report($"Checking group: {groupName}");
                        var groupMembers = GetGroupMembersRecursive(
                            _domainInfo.ChosenDc, 
                            _domainInfo.DomainDn, 
                            groupName, 
                            searchedGroups,
                            string.Empty);

                        foreach (var member in groupMembers)
                        {
                            member.GroupName = groupName;
                            members.Add(member);
                        }
                    }
                    catch (Exception ex)
                    {
                        _progress?.Report($"Warning: Could not enumerate {groupName}: {ex.Message}");
                    }
                }

                _progress?.Report($"Found {members.Count} privileged accounts");
                return members;
            }, ct);
        }

        private List<AdPrivilegedMember> GetGroupMembersRecursive(
            string dc, 
            string domainDn, 
            string groupName, 
            HashSet<string> searchedGroups,
            string nestedPath)
        {
            var members = new List<AdPrivilegedMember>();

            try
            {
                using var entry = new DirectoryEntry($"LDAP://{dc}/{domainDn}");
                using var searcher = new DirectorySearcher(entry)
                {
                    Filter = $"(&(objectClass=group)(|(sAMAccountName={groupName})(cn={groupName})))",
                    SearchScope = SearchScope.Subtree
                };
                searcher.PropertiesToLoad.Add("member");
                searcher.PropertiesToLoad.Add("distinguishedName");

                var groupResult = searcher.FindOne();
                if (groupResult == null) return members;

                var groupDn = groupResult.Properties["distinguishedName"]?[0]?.ToString() ?? string.Empty;
                if (searchedGroups.Contains(groupDn)) return members;
                searchedGroups.Add(groupDn);

                var memberDns = groupResult.Properties["member"];
                if (memberDns == null) return members;

                foreach (string memberDn in memberDns)
                {
                    try
                    {
                        using var memberEntry = new DirectoryEntry($"LDAP://{dc}/{memberDn}");
                        using var memberSearcher = new DirectorySearcher(memberEntry)
                        {
                            Filter = "(objectClass=*)",
                            SearchScope = SearchScope.Base
                        };
                        memberSearcher.PropertiesToLoad.AddRange(new[] 
                        { 
                            "sAMAccountName", "objectClass", "pwdLastSet", "lastLogon", 
                            "userAccountControl", "servicePrincipalName", "distinguishedName",
                            "memberOf"
                        });

                        var memberResult = memberSearcher.FindOne();
                        if (memberResult == null) continue;

                        var objectClass = memberResult.Properties["objectClass"];
                        bool isGroup = objectClass?.Contains("group") == true;

                        if (isGroup)
                        {
                            // Recursive call for nested groups
                            var nestedName = memberResult.Properties["sAMAccountName"]?[0]?.ToString() ?? string.Empty;
                            var newPath = string.IsNullOrEmpty(nestedPath) ? groupName : $"{nestedPath} -> {groupName}";
                            var nestedMembers = GetGroupMembersRecursive(dc, domainDn, nestedName, searchedGroups, newPath);
                            
                            foreach (var nm in nestedMembers)
                            {
                                nm.IsNested = true;
                                nm.NestedPath = newPath + " -> " + nestedName;
                            }
                            members.AddRange(nestedMembers);
                        }
                        else
                        {
                            var member = new AdPrivilegedMember
                            {
                                SamAccountName = memberResult.Properties["sAMAccountName"]?[0]?.ToString() ?? string.Empty,
                                DistinguishedName = memberDn,
                                ObjectClass = objectClass?.Cast<string>().LastOrDefault() ?? "unknown",
                                IsNested = !string.IsNullOrEmpty(nestedPath),
                                NestedPath = nestedPath
                            };

                            // Parse UAC flags
                            var uac = memberResult.Properties["userAccountControl"]?[0];
                            if (uac != null && int.TryParse(uac.ToString(), out var uacInt))
                            {
                                member.IsEnabled = (uacInt & 0x2) == 0; // ACCOUNTDISABLE flag
                                member.PasswordNeverExpires = (uacInt & 0x10000) != 0;
                                member.TrustedForDelegation = (uacInt & 0x80000) != 0;
                                member.RiskyUacFlags = DecodeUac(uacInt);
                            }

                            // Password last set
                            var pwdLastSet = memberResult.Properties["pwdLastSet"]?[0];
                            if (pwdLastSet != null && long.TryParse(pwdLastSet.ToString(), out var pwdTicks) && pwdTicks > 0)
                            {
                                member.PasswordLastSet = DateTime.FromFileTime(pwdTicks);
                            }

                            // Last logon
                            var lastLogon = memberResult.Properties["lastLogon"]?[0];
                            if (lastLogon != null && long.TryParse(lastLogon.ToString(), out var logonTicks) && logonTicks > 0)
                            {
                                member.LastLogon = DateTime.FromFileTime(logonTicks);
                            }

                            // SPNs
                            member.HasSpn = memberResult.Properties["servicePrincipalName"]?.Count > 0;

                            members.Add(member);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return members;
        }

        private List<string> DecodeUac(int uac)
        {
            var riskyFlags = new List<string>();

            if ((uac & 0x00400000) != 0) riskyFlags.Add("DONT_REQ_PREAUTH");
            if ((uac & 0x00000080) != 0) riskyFlags.Add("ENCRYPTED_TEXT_PWD_ALLOWED");
            if ((uac & 0x00000020) != 0) riskyFlags.Add("PASSWD_NOTREQD");
            if ((uac & 0x00200000) != 0) riskyFlags.Add("USE_DES_KEY_ONLY");
            if ((uac & 0x01000000) != 0) riskyFlags.Add("TRUSTED_TO_AUTH_FOR_DELEGATION");
            if ((uac & 0x00080000) != 0) riskyFlags.Add("TRUSTED_FOR_DELEGATION");
            if ((uac & 0x00010000) != 0) riskyFlags.Add("DONT_EXPIRE_PASSWORD");

            return riskyFlags;
        }

        #endregion

        #region Risky ACL Analysis

        /// <summary>
        /// Analyzes ACLs on sensitive AD objects for risky permissions.
        /// </summary>
        public async Task<List<AdRiskyAcl>> GetRiskyAclsAsync(
            bool filterSafe = true,
            string? specificObjectDn = null,
            CancellationToken ct = default)
        {
            if (_domainInfo == null)
            {
                await DiscoverDomainAsync(ct: ct);
            }

            if (_domainInfo == null || string.IsNullOrEmpty(_domainInfo.ChosenDc))
            {
                return new List<AdRiskyAcl>();
            }

            // Load schema GUIDs if not already loaded
            if (_schemaGuids.Count == 0)
            {
                LoadSchemaGuids(_domainInfo.ChosenDc, _domainInfo.ForestDn);
            }

            return await Task.Run(() =>
            {
                var riskyAcls = new List<AdRiskyAcl>();

                _progress?.Report("Analyzing ACLs on sensitive objects...");

                // Objects to check
                var objectsToCheck = new List<string>();

                if (!string.IsNullOrEmpty(specificObjectDn))
                {
                    objectsToCheck.Add(specificObjectDn);
                }
                else
                {
                    // Standard IR objects
                    objectsToCheck.Add(_domainInfo.DomainDn); // Domain DSE
                    objectsToCheck.Add($"OU=Domain Controllers,{_domainInfo.DomainDn}");
                    objectsToCheck.Add($"CN=AdminSDHolder,CN=System,{_domainInfo.DomainDn}");
                    
                    // Find krbtgt
                    try
                    {
                        using var entry = new DirectoryEntry($"LDAP://{_domainInfo.ChosenDc}/{_domainInfo.DomainDn}");
                        var searcher = new DirectorySearcher(entry)
                        {
                            Filter = "(sAMAccountName=krbtgt)",
                            SearchScope = SearchScope.Subtree
                        };
                        searcher.PropertiesToLoad.Add("distinguishedName");
                        var krbtgt = searcher.FindOne();
                        if (krbtgt != null)
                        {
                            objectsToCheck.Add(krbtgt.Properties["distinguishedName"]?[0]?.ToString() ?? string.Empty);
                        }
                    }
                    catch { }

                    // Find privileged groups
                    foreach (var groupName in new[] { "Domain Admins", "Enterprise Admins", "Schema Admins" })
                    {
                        try
                        {
                            using var entry = new DirectoryEntry($"LDAP://{_domainInfo.ChosenDc}/{_domainInfo.DomainDn}");
                            var searcher = new DirectorySearcher(entry)
                            {
                                Filter = $"(&(objectClass=group)(sAMAccountName={groupName}))",
                                SearchScope = SearchScope.Subtree
                            };
                            searcher.PropertiesToLoad.Add("distinguishedName");
                            var group = searcher.FindOne();
                            if (group != null)
                            {
                                objectsToCheck.Add(group.Properties["distinguishedName"]?[0]?.ToString() ?? string.Empty);
                            }
                        }
                        catch { }
                    }
                }

                foreach (var objectDn in objectsToCheck.Where(o => !string.IsNullOrEmpty(o)))
                {
                    ct.ThrowIfCancellationRequested();
                    
                    try
                    {
                        _progress?.Report($"Checking ACLs on: {objectDn}");
                        var objectAcls = AnalyzeObjectAcls(objectDn, filterSafe);
                        riskyAcls.AddRange(objectAcls);
                    }
                    catch (Exception ex)
                    {
                        _progress?.Report($"Warning: Could not analyze ACLs on {objectDn}: {ex.Message}");
                    }
                }

                _progress?.Report($"Found {riskyAcls.Count} risky ACL entries");
                return riskyAcls;
            }, ct);
        }

        private List<AdRiskyAcl> AnalyzeObjectAcls(string objectDn, bool filterSafe)
        {
            var riskyAcls = new List<AdRiskyAcl>();

            try
            {
                using var entry = new DirectoryEntry($"LDAP://{_domainInfo!.ChosenDc}/{objectDn}");
                var security = entry.ObjectSecurity;
                var rules = security.GetAccessRules(true, true, typeof(NTAccount));

                foreach (ActiveDirectoryAccessRule rule in rules)
                {
                    // Skip inherited ACEs for brevity
                    if (rule.IsInherited) continue;

                    var identity = rule.IdentityReference.Value;

                    // Filter safe identities
                    if (filterSafe && IsSafeIdentity(identity))
                        continue;

                    // Check for risky rights
                    var rightsStr = rule.ActiveDirectoryRights.ToString();
                    bool isRisky = RiskyAdRights.DangerousRights.Any(r => 
                        rightsStr.Contains(r, StringComparison.OrdinalIgnoreCase));

                    // Check for risky extended rights
                    var objectTypeName = ResolveGuid(rule.ObjectType);
                    bool isRiskyExtended = RiskyAdRights.DangerousExtendedRights.Any(r =>
                        objectTypeName.Equals(r, StringComparison.OrdinalIgnoreCase));

                    if (isRisky || isRiskyExtended)
                    {
                        riskyAcls.Add(new AdRiskyAcl
                        {
                            ObjectDn = objectDn,
                            IdentityReference = identity,
                            ActiveDirectoryRights = rightsStr,
                            AccessControlType = rule.AccessControlType.ToString(),
                            ObjectType = rule.ObjectType.ToString(),
                            ObjectTypeName = objectTypeName,
                            InheritedObjectType = rule.InheritedObjectType.ToString(),
                            IsInherited = rule.IsInherited,
                            Severity = DetermineAclSeverity(rightsStr, objectTypeName),
                            Description = $"{identity} has {rightsStr} on {objectTypeName}"
                        });
                    }
                }
            }
            catch { }

            return riskyAcls;
        }

        private bool IsSafeIdentity(string identity)
        {
            return SafeIdentities.SystemIdentities.Any(s => 
                identity.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        private string DetermineAclSeverity(string rights, string objectType)
        {
            if (rights.Contains("GenericAll") || rights.Contains("WriteDacl") || rights.Contains("WriteOwner"))
                return "Critical";
            
            if (objectType.Contains("Replication") || objectType.Contains("Force-Change-Password"))
                return "Critical";

            if (rights.Contains("GenericWrite") || rights.Contains("AllExtendedRights"))
                return "High";

            return "Medium";
        }

        #endregion

        #region Kerberos Delegation Analysis

        /// <summary>
        /// Finds accounts with Kerberos delegation configured.
        /// </summary>
        public async Task<List<AdKerberosDelegation>> GetKerberosDelegationsAsync(CancellationToken ct = default)
        {
            if (_domainInfo == null)
            {
                await DiscoverDomainAsync(ct: ct);
            }

            if (_domainInfo == null || string.IsNullOrEmpty(_domainInfo.ChosenDc))
            {
                return new List<AdKerberosDelegation>();
            }

            return await Task.Run(() =>
            {
                var delegations = new List<AdKerberosDelegation>();

                _progress?.Report("Searching for Kerberos delegation...");

                try
                {
                    using var entry = new DirectoryEntry($"LDAP://{_domainInfo.ChosenDc}/{_domainInfo.DomainDn}");
                    
                    // Find unconstrained delegation
                    var unconstrainedSearcher = new DirectorySearcher(entry)
                    {
                        // UserAccountControl bit 0x80000 = TRUSTED_FOR_DELEGATION
                        Filter = "(&(|(objectClass=user)(objectClass=computer))(userAccountControl:1.2.840.113556.1.4.803:=524288))",
                        SearchScope = SearchScope.Subtree,
                        PageSize = 1000
                    };
                    unconstrainedSearcher.PropertiesToLoad.AddRange(new[] 
                    { 
                        "sAMAccountName", "distinguishedName", "objectClass", "userAccountControl"
                    });

                    foreach (SearchResult result in unconstrainedSearcher.FindAll())
                    {
                        ct.ThrowIfCancellationRequested();

                        var samName = result.Properties["sAMAccountName"]?[0]?.ToString() ?? "";
                        
                        // Skip domain controllers (they legitimately have unconstrained)
                        if (samName.EndsWith("$") && result.Path.Contains("OU=Domain Controllers"))
                            continue;

                        delegations.Add(new AdKerberosDelegation
                        {
                            SamAccountName = samName,
                            DistinguishedName = result.Properties["distinguishedName"]?[0]?.ToString() ?? "",
                            ObjectClass = result.Properties["objectClass"]?.Cast<string>().LastOrDefault() ?? "unknown",
                            DelegationType = "Unconstrained",
                            Severity = "Critical",
                            Description = "Unconstrained delegation allows impersonation of any user to any service"
                        });
                    }

                    // Find constrained delegation
                    var constrainedSearcher = new DirectorySearcher(entry)
                    {
                        Filter = "(msDS-AllowedToDelegateTo=*)",
                        SearchScope = SearchScope.Subtree,
                        PageSize = 1000
                    };
                    constrainedSearcher.PropertiesToLoad.AddRange(new[] 
                    { 
                        "sAMAccountName", "distinguishedName", "objectClass", 
                        "msDS-AllowedToDelegateTo", "userAccountControl"
                    });

                    foreach (SearchResult result in constrainedSearcher.FindAll())
                    {
                        ct.ThrowIfCancellationRequested();

                        var allowedTo = result.Properties["msDS-AllowedToDelegateTo"]?
                            .Cast<string>().ToList() ?? new List<string>();

                        var uac = 0;
                        if (result.Properties["userAccountControl"]?[0] != null)
                        {
                            int.TryParse(result.Properties["userAccountControl"][0].ToString(), out uac);
                        }

                        var delegationType = (uac & 0x01000000) != 0 
                            ? "Constrained (Protocol Transition)" 
                            : "Constrained";

                        delegations.Add(new AdKerberosDelegation
                        {
                            SamAccountName = result.Properties["sAMAccountName"]?[0]?.ToString() ?? "",
                            DistinguishedName = result.Properties["distinguishedName"]?[0]?.ToString() ?? "",
                            ObjectClass = result.Properties["objectClass"]?.Cast<string>().LastOrDefault() ?? "unknown",
                            DelegationType = delegationType,
                            AllowedToDelegateTo = allowedTo,
                            Severity = (uac & 0x01000000) != 0 ? "High" : "Medium",
                            Description = $"Can delegate to: {string.Join(", ", allowedTo.Take(3))}"
                        });
                    }

                    // Find resource-based constrained delegation
                    var rbcdSearcher = new DirectorySearcher(entry)
                    {
                        Filter = "(msDS-AllowedToActOnBehalfOfOtherIdentity=*)",
                        SearchScope = SearchScope.Subtree,
                        PageSize = 1000
                    };
                    rbcdSearcher.PropertiesToLoad.AddRange(new[] 
                    { 
                        "sAMAccountName", "distinguishedName", "objectClass",
                        "msDS-AllowedToActOnBehalfOfOtherIdentity"
                    });

                    foreach (SearchResult result in rbcdSearcher.FindAll())
                    {
                        ct.ThrowIfCancellationRequested();

                        delegations.Add(new AdKerberosDelegation
                        {
                            SamAccountName = result.Properties["sAMAccountName"]?[0]?.ToString() ?? "",
                            DistinguishedName = result.Properties["distinguishedName"]?[0]?.ToString() ?? "",
                            ObjectClass = result.Properties["objectClass"]?.Cast<string>().LastOrDefault() ?? "unknown",
                            DelegationType = "Resource-Based Constrained",
                            Severity = "High",
                            Description = "Has resource-based constrained delegation configured"
                        });
                    }
                }
                catch (Exception ex)
                {
                    _progress?.Report($"Error analyzing Kerberos delegation: {ex.Message}");
                }

                _progress?.Report($"Found {delegations.Count} accounts with delegation");
                return delegations;
            }, ct);
        }

        #endregion

        #region AdminCount Analysis

        /// <summary>
        /// Finds accounts with AdminCount anomalies.
        /// </summary>
        public async Task<List<AdAdminCountAnomaly>> GetAdminCountAnomaliesAsync(CancellationToken ct = default)
        {
            if (_domainInfo == null)
            {
                await DiscoverDomainAsync(ct: ct);
            }

            if (_domainInfo == null || string.IsNullOrEmpty(_domainInfo.ChosenDc))
            {
                return new List<AdAdminCountAnomaly>();
            }

            return await Task.Run(() =>
            {
                var anomalies = new List<AdAdminCountAnomaly>();

                _progress?.Report("Analyzing AdminCount attributes...");

                try
                {
                    using var entry = new DirectoryEntry($"LDAP://{_domainInfo.ChosenDc}/{_domainInfo.DomainDn}");
                    var searcher = new DirectorySearcher(entry)
                    {
                        Filter = "(&(|(objectClass=user)(objectClass=group))(adminCount=1))",
                        SearchScope = SearchScope.Subtree,
                        PageSize = 1000
                    };
                    searcher.PropertiesToLoad.AddRange(new[] 
                    { 
                        "sAMAccountName", "distinguishedName", "objectClass", 
                        "adminCount", "memberOf"
                    });

                    // Get list of currently privileged accounts for comparison
                    var privilegedMembers = GetPrivilegedMembersAsync(false, ct).Result;
                    var privilegedDns = new HashSet<string>(
                        privilegedMembers.Select(p => p.DistinguishedName), 
                        StringComparer.OrdinalIgnoreCase);

                    foreach (SearchResult result in searcher.FindAll())
                    {
                        ct.ThrowIfCancellationRequested();

                        var dn = result.Properties["distinguishedName"]?[0]?.ToString() ?? "";
                        var adminCount = 1;
                        if (result.Properties["adminCount"]?[0] != null)
                        {
                            int.TryParse(result.Properties["adminCount"][0].ToString(), out adminCount);
                        }

                        var isCurrentlyPrivileged = privilegedDns.Contains(dn);

                        // Flag accounts that have adminCount but aren't currently privileged
                        if (!isCurrentlyPrivileged)
                        {
                            anomalies.Add(new AdAdminCountAnomaly
                            {
                                SamAccountName = result.Properties["sAMAccountName"]?[0]?.ToString() ?? "",
                                DistinguishedName = dn,
                                ObjectClass = result.Properties["objectClass"]?.Cast<string>().LastOrDefault() ?? "unknown",
                                AdminCount = adminCount,
                                IsCurrentlyPrivileged = false,
                                Issue = "Account has AdminCount=1 but is not currently a member of any privileged group. " +
                                       "This may indicate the account was previously privileged or SDProp is not running correctly."
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _progress?.Report($"Error analyzing AdminCount: {ex.Message}");
                }

                _progress?.Report($"Found {anomalies.Count} AdminCount anomalies");
                return anomalies;
            }, ct);
        }

        #endregion

        #region SYSVOL Analysis

        /// <summary>
        /// Scans SYSVOL for risky files (executables, scripts).
        /// </summary>
        public async Task<List<SysvolRiskyFile>> ScanSysvolAsync(CancellationToken ct = default)
        {
            if (_domainInfo == null)
            {
                await DiscoverDomainAsync(ct: ct);
            }

            if (_domainInfo == null || string.IsNullOrEmpty(_domainInfo.ChosenDc))
            {
                return new List<SysvolRiskyFile>();
            }

            return await Task.Run(() =>
            {
                var riskyFiles = new List<SysvolRiskyFile>();

                _progress?.Report("Scanning SYSVOL for risky files...");

                try
                {
                    var sysvolPath = $@"\\{_domainInfo.ChosenDc}\SYSVOL\{_domainInfo.DomainFqdn}";

                    if (!Directory.Exists(sysvolPath))
                    {
                        _progress?.Report($"SYSVOL path not accessible: {sysvolPath}");
                        return riskyFiles;
                    }

                    var files = Directory.EnumerateFiles(sysvolPath, "*.*", SearchOption.AllDirectories);

                    foreach (var filePath in files)
                    {
                        ct.ThrowIfCancellationRequested();

                        var ext = Path.GetExtension(filePath);
                        if (!RiskyExtensions.Contains(ext))
                            continue;

                        try
                        {
                            var fileInfo = new FileInfo(filePath);
                            var hash = ComputeSha256(filePath);

                            riskyFiles.Add(new SysvolRiskyFile
                            {
                                FileName = fileInfo.Name,
                                FilePath = fileInfo.DirectoryName ?? "",
                                Extension = ext,
                                CreationTime = fileInfo.CreationTime,
                                LastWriteTime = fileInfo.LastWriteTime,
                                FileSize = fileInfo.Length,
                                Sha256Hash = hash,
                                Severity = DetermineFileSeverity(ext)
                            });
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    _progress?.Report($"Error scanning SYSVOL: {ex.Message}");
                }

                _progress?.Report($"Found {riskyFiles.Count} risky files in SYSVOL");
                return riskyFiles.OrderByDescending(f => f.LastWriteTime).ToList();
            }, ct);
        }

        private string ComputeSha256(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return "ERROR";
            }
        }

        private string DetermineFileSeverity(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".exe" or ".dll" or ".msi" => "High",
                ".ps1" or ".bat" or ".cmd" => "Medium",
                ".vbs" or ".js" or ".wsf" => "Medium",
                _ => "Low"
            };
        }

        #endregion

        #region Full Analysis

        /// <summary>
        /// Runs a complete AD security analysis.
        /// </summary>
        public async Task<AdSecurityAnalysisResult> RunFullAnalysisAsync(
            AdSecurityAnalysisOptions options,
            CancellationToken ct = default)
        {
            var result = new AdSecurityAnalysisResult
            {
                StartTime = DateTime.Now
            };

            try
            {
                // Discover domain
                result.DomainInfo = await DiscoverDomainAsync(options.TargetDomain, options.TargetDc, ct);

                if (string.IsNullOrEmpty(result.DomainInfo.ChosenDc))
                {
                    result.Errors.Add("Could not connect to a domain controller");
                    return result;
                }

                // Run selected analyses
                if (options.AnalyzePrivilegedGroups)
                {
                    result.PrivilegedMembers = await GetPrivilegedMembersAsync(options.IncludeForestMode, ct);
                }

                if (options.AnalyzeRiskyAcls)
                {
                    result.RiskyAcls = await GetRiskyAclsAsync(options.FilterSafeIdentities, null, ct);
                }

                if (options.AnalyzeKerberosDelegation)
                {
                    result.KerberosDelegations = await GetKerberosDelegationsAsync(ct);
                }

                if (options.AnalyzeAdminCount)
                {
                    result.AdminCountAnomalies = await GetAdminCountAnomaliesAsync(ct);
                }

                if (options.AnalyzeSysvol)
                {
                    result.SysvolRiskyFiles = await ScanSysvolAsync(ct);
                }

                // Count severities
                result.CriticalCount = 
                    result.RiskyAcls.Count(a => a.Severity == "Critical") +
                    result.KerberosDelegations.Count(d => d.Severity == "Critical");
                
                result.HighCount = 
                    result.RiskyAcls.Count(a => a.Severity == "High") +
                    result.KerberosDelegations.Count(d => d.Severity == "High") +
                    result.SysvolRiskyFiles.Count(f => f.Severity == "High");

                result.MediumCount = 
                    result.RiskyAcls.Count(a => a.Severity == "Medium") +
                    result.KerberosDelegations.Count(d => d.Severity == "Medium") +
                    result.SysvolRiskyFiles.Count(f => f.Severity == "Medium") +
                    result.AdminCountAnomalies.Count;

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

            _progress?.Report($"Analysis complete. Total findings: {result.TotalFindings}");
            return result;
        }

        #endregion

        #region Report Generation

        /// <summary>
        /// Exports the analysis result to a CSV file.
        /// </summary>
        public async Task ExportToCsvAsync(AdSecurityAnalysisResult result, string outputPath)
        {
            await Task.Run(() =>
            {
                var sb = new StringBuilder();

                // Domain Info
                sb.AppendLine("=== DOMAIN INFORMATION ===");
                sb.AppendLine($"Domain,{result.DomainInfo.DomainFqdn}");
                sb.AppendLine($"Domain Controller,{result.DomainInfo.ChosenDc}");
                sb.AppendLine($"PDC Emulator,{result.DomainInfo.PdcEmulator}");
                sb.AppendLine($"Analysis Time,{result.StartTime}");
                sb.AppendLine();

                // Privileged Members
                if (result.PrivilegedMembers.Any())
                {
                    sb.AppendLine("=== PRIVILEGED MEMBERS ===");
                    sb.AppendLine("SamAccountName,GroupName,ObjectClass,Enabled,PasswordNeverExpires,TrustedForDelegation,HasSPN,Nested,RiskyFlags");
                    foreach (var m in result.PrivilegedMembers)
                    {
                        sb.AppendLine($"{m.SamAccountName},{m.GroupName},{m.ObjectClass},{m.IsEnabled},{m.PasswordNeverExpires},{m.TrustedForDelegation},{m.HasSpn},{m.IsNested},{string.Join(";", m.RiskyUacFlags)}");
                    }
                    sb.AppendLine();
                }

                // Risky ACLs
                if (result.RiskyAcls.Any())
                {
                    sb.AppendLine("=== RISKY ACLs ===");
                    sb.AppendLine("ObjectDN,Identity,Rights,ObjectType,Severity");
                    foreach (var a in result.RiskyAcls)
                    {
                        sb.AppendLine($"\"{a.ObjectDn}\",{a.IdentityReference},{a.ActiveDirectoryRights},{a.ObjectTypeName},{a.Severity}");
                    }
                    sb.AppendLine();
                }

                // Kerberos Delegations
                if (result.KerberosDelegations.Any())
                {
                    sb.AppendLine("=== KERBEROS DELEGATIONS ===");
                    sb.AppendLine("SamAccountName,DelegationType,Severity,AllowedToDelegateTo");
                    foreach (var d in result.KerberosDelegations)
                    {
                        sb.AppendLine($"{d.SamAccountName},{d.DelegationType},{d.Severity},{string.Join(";", d.AllowedToDelegateTo)}");
                    }
                    sb.AppendLine();
                }

                // SYSVOL Files
                if (result.SysvolRiskyFiles.Any())
                {
                    sb.AppendLine("=== SYSVOL RISKY FILES ===");
                    sb.AppendLine("FileName,Path,CreationTime,LastWriteTime,Size,SHA256,Severity");
                    foreach (var f in result.SysvolRiskyFiles)
                    {
                        sb.AppendLine($"{f.FileName},\"{f.FilePath}\",{f.CreationTime},{f.LastWriteTime},{f.FileSize},{f.Sha256Hash},{f.Severity}");
                    }
                    sb.AppendLine();
                }

                // AdminCount Anomalies
                if (result.AdminCountAnomalies.Any())
                {
                    sb.AppendLine("=== ADMINCOUNT ANOMALIES ===");
                    sb.AppendLine("SamAccountName,ObjectClass,Issue");
                    foreach (var a in result.AdminCountAnomalies)
                    {
                        sb.AppendLine($"{a.SamAccountName},{a.ObjectClass},\"{a.Issue}\"");
                    }
                }

                File.WriteAllText(outputPath, sb.ToString());
            });
        }

        #endregion

        #region Deployment Methods

        /// <summary>
        /// Deploys the tiered admin OU structure (BILL model).
        /// </summary>
        public async Task<List<AdObjectCreationResult>> DeployTieredOuStructureAsync(
            BillOuTemplate template,
            bool protectFromDeletion,
            bool createUsersOus,
            bool createDevicesOus,
            CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var results = new List<AdObjectCreationResult>();

                if (_domainInfo == null || string.IsNullOrEmpty(_domainInfo.ChosenDc))
                {
                    results.Add(new AdObjectCreationResult
                    {
                        Success = false,
                        ObjectType = "Prerequisite",
                        ObjectName = "Domain Connection",
                        Message = "Domain not discovered. Run DiscoverDomainAsync first."
                    });
                    return results;
                }

                try
                {
                    var domainDn = _domainInfo.DomainDn;
                    var dc = _domainInfo.ChosenDc;

                    // Create base OU
                    var baseOuDn = $"OU={template.BaseName},{domainDn}";
                    results.Add(CreateOrganizationalUnit(dc, domainDn, template.BaseName, $"Tiered Administration Root OU", protectFromDeletion));

                    // Create tier OUs
                    var tiers = new[] { template.Tier0Name, template.Tier1Name, template.Tier2Name };
                    foreach (var tier in tiers)
                    {
                        ct.ThrowIfCancellationRequested();

                        var tierOuDn = $"OU={tier},{baseOuDn}";
                        results.Add(CreateOrganizationalUnit(dc, baseOuDn, tier, $"{tier} - Tiered Admin OU", protectFromDeletion));

                        // Create sub-OUs for each tier
                        if (template.CreatePawOus)
                        {
                            results.Add(CreateOrganizationalUnit(dc, tierOuDn, "PAW", $"Privileged Access Workstations for {tier}", protectFromDeletion));
                        }

                        if (template.CreateServiceAccountOus)
                        {
                            results.Add(CreateOrganizationalUnit(dc, tierOuDn, "ServiceAccounts", $"Service Accounts for {tier}", protectFromDeletion));
                        }

                        if (template.CreateGroupsOus)
                        {
                            results.Add(CreateOrganizationalUnit(dc, tierOuDn, "Groups", $"Security Groups for {tier}", protectFromDeletion));
                        }

                        if (createUsersOus)
                        {
                            results.Add(CreateOrganizationalUnit(dc, tierOuDn, "Users", $"Admin Users for {tier}", protectFromDeletion));
                        }

                        if (createDevicesOus && tier != template.Tier0Name)
                        {
                            var deviceOuName = tier == template.Tier1Name ? "Servers" : "Workstations";
                            results.Add(CreateOrganizationalUnit(dc, tierOuDn, deviceOuName, $"{deviceOuName} for {tier}", protectFromDeletion));
                        }
                    }

                    _progress?.Report($"OU deployment complete: {results.Count(r => r.Success)} succeeded, {results.Count(r => !r.Success)} failed");
                }
                catch (Exception ex)
                {
                    results.Add(new AdObjectCreationResult
                    {
                        Success = false,
                        ObjectType = "Error",
                        ObjectName = "Deployment",
                        Message = ex.Message,
                        Error = ex
                    });
                }

                return results;
            }, ct);
        }

        /// <summary>
        /// Creates an organizational unit.
        /// </summary>
        private AdObjectCreationResult CreateOrganizationalUnit(string dc, string parentDn, string ouName, string description, bool protectFromDeletion)
        {
            var result = new AdObjectCreationResult
            {
                ObjectType = "OU",
                ObjectName = ouName,
                DistinguishedName = $"OU={ouName},{parentDn}"
            };

            try
            {
                _progress?.Report($"Creating OU: {ouName} in {parentDn}");

                using var parentEntry = new DirectoryEntry($"LDAP://{dc}/{parentDn}");
                
                // Check if OU already exists
                using var searcher = new DirectorySearcher(parentEntry)
                {
                    Filter = $"(&(objectClass=organizationalUnit)(ou={ouName}))",
                    SearchScope = SearchScope.OneLevel
                };

                var existing = searcher.FindOne();
                if (existing != null)
                {
                    result.Success = true;
                    result.Message = "OU already exists";
                    _progress?.Report($"OU already exists: {ouName}");
                    return result;
                }

                // Create new OU
                using var newOu = parentEntry.Children.Add($"OU={ouName}", "organizationalUnit");
                newOu.Properties["description"].Value = description;
                newOu.CommitChanges();

                // Set protection from accidental deletion
                if (protectFromDeletion)
                {
                    try
                    {
                        var security = newOu.ObjectSecurity;
                        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                        var denyDeleteRule = new ActiveDirectoryAccessRule(
                            everyoneSid,
                            ActiveDirectoryRights.Delete | ActiveDirectoryRights.DeleteTree,
                            System.Security.AccessControl.AccessControlType.Deny);
                        security.AddAccessRule(denyDeleteRule);
                        newOu.CommitChanges();
                    }
                    catch (Exception protectEx)
                    {
                        _progress?.Report($"Warning: Could not set deletion protection on {ouName}: {protectEx.Message}");
                    }
                }

                result.Success = true;
                result.Message = "Created successfully";
                _progress?.Report($"Created OU: {ouName}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                result.Error = ex;
                _progress?.Report($"Failed to create OU {ouName}: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Deploys baseline GPOs for security hardening.
        /// </summary>
        public async Task<List<AdObjectCreationResult>> DeployBaselineGposAsync(
            GpoDeploymentOptions options,
            CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var results = new List<AdObjectCreationResult>();

                if (_domainInfo == null || string.IsNullOrEmpty(_domainInfo.ChosenDc))
                {
                    results.Add(new AdObjectCreationResult
                    {
                        Success = false,
                        ObjectType = "Prerequisite",
                        ObjectName = "Domain Connection",
                        Message = "Domain not discovered. Run DiscoverDomainAsync first."
                    });
                    return results;
                }

                _progress?.Report("Note: GPO deployment requires PowerShell GroupPolicy module.");
                _progress?.Report("This feature creates GPO shells. Use Group Policy Management Console to configure settings.");

                try
                {
                    var dc = _domainInfo.ChosenDc;
                    var domainDn = _domainInfo.DomainDn;
                    var domainFqdn = _domainInfo.DomainFqdn;

                    // === Quick Deploy GPOs ===
                    if (options.DeployPasswordPolicy)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn, 
                            "Baseline-PasswordPolicy", 
                            "Fine-grained password policy. Configure in GPMC: Password must be 14+ chars, complexity enabled, 90-day max age."));
                    }

                    if (options.DeployAuditPolicy)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Baseline-AuditPolicy",
                            "Advanced audit policy. Configure: Logon/Logoff, Account Logon, Object Access, Policy Change, Privilege Use."));
                    }

                    if (options.DeploySecurityBaseline)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Baseline-SecurityHardening",
                            "Security hardening baseline. Configure: Disable LM hash, SMB signing, LDAP signing, restrict anonymous."));
                    }

                    if (options.DeployPawPolicy)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "PAW-SecurityPolicy",
                            "PAW lockdown policy. Configure: Credential Guard, Device Guard, AppLocker, restricted network."));
                    }

                    // === Tier 0 GPOs (PLATYPUS/BILL) ===
                    if (options.DeployT0BaselineAudit)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 0 - Baseline Audit Policies - Tier 0 Servers",
                            "Enhanced audit policies for Tier 0 servers. Configure: Success/Failure auditing for security events."));
                    }

                    if (options.DeployT0DisallowDsrm)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 0 - Disallow DSRM Login - DC ONLY",
                            "Prevents DSRM (Directory Services Restore Mode) network logon. Link to Domain Controllers OU."));
                    }

                    if (options.DeployT0DomainBlock)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 0 - Domain Block - Top Level",
                            "Blocks Tier 0 accounts from logging into non-Tier 0 systems. Link to domain root."));
                    }

                    if (options.DeployT0DomainControllers)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 0 - Domain Controllers - DC Only",
                            "Security hardening for Domain Controllers. Link to Domain Controllers OU."));
                    }

                    if (options.DeployT0EsxAdmins)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 0 - ESX Admins Restricted Group - DC Only",
                            "Empties ESX Admins group via Restricted Groups to prevent VMware privilege escalation."));
                    }

                    if (options.DeployT0UserRights)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 0 - User Rights Assignments - Tier 0 Servers",
                            "Restricts logon rights on Tier 0 to only Tier 0 operators. Configure: Allow log on locally, Remote Desktop, etc."));
                    }

                    if (options.DeployT0RestrictedGroups)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 0 - Restricted Groups - Tier 0 Servers",
                            "Controls local admin membership on Tier 0 servers. Configure: Administrators, Remote Desktop Users groups."));
                    }

                    // === Tier 1 GPOs ===
                    if (options.DeployT1LocalAdmin)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 1 - Tier 1 Operators in Local Admin - Tier 1 Servers",
                            "Adds Tier 1 Operators group to local Administrators on Tier 1 servers."));
                    }

                    if (options.DeployT1UserRights)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 1 - User Rights Assignments - Tier 1 Servers",
                            "Restricts logon rights on Tier 1 servers to Tier 1 operators. Blocks Tier 0 and Tier 2."));
                    }

                    if (options.DeployT1RestrictedGroups)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 1 - Restricted Groups - Tier 1 Servers",
                            "Controls local admin membership on Tier 1 servers."));
                    }

                    // === Tier 2 GPOs ===
                    if (options.DeployT2LocalAdmin)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 2 - Tier 2 Operators in Local Admin - Tier 2 Devices",
                            "Adds Tier 2 Operators group to local Administrators on workstations."));
                    }

                    if (options.DeployT2UserRights)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 2 - User Rights Assignments - Tier 2 Devices",
                            "Restricts logon rights on workstations to Tier 2 operators. Blocks Tier 0 and Tier 1."));
                    }

                    if (options.DeployT2RestrictedGroups)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier 2 - Restricted Groups - Tier 2 Devices",
                            "Controls local admin membership on workstations."));
                    }

                    // === Cross-Tier GPOs ===
                    if (options.DeployDisableSmb1)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier ALL - Disable SMBv1 - Top Level",
                            "Disables SMBv1 client and server across all systems. Link to domain root."));
                    }

                    if (options.DeployDisableWDigest)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "Tier ALL - Disable WDigest - Top Level",
                            "Disables WDigest credential caching to prevent plaintext password storage in memory."));
                    }

                    if (options.DeployResetMachinePassword)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoPlaceholder(dc, domainFqdn,
                            "PLATYPUS - Reset Machine Account Password",
                            "Configures automatic machine account password rotation (default: 30 days)."));
                    }

                    _progress?.Report($"GPO deployment complete: {results.Count(r => r.Success)} succeeded, {results.Count(r => !r.Success)} failed");
                }
                catch (Exception ex)
                {
                    results.Add(new AdObjectCreationResult
                    {
                        Success = false,
                        ObjectType = "Error",
                        ObjectName = "GPO Deployment",
                        Message = ex.Message,
                        Error = ex
                    });
                }

                return results;
            }, ct);
        }

        /// <summary>
        /// Creates a GPO placeholder (shell GPO without configured settings).
        /// Full GPO configuration requires the GroupPolicy PowerShell module or GPMC.
        /// </summary>
        private AdObjectCreationResult CreateGpoPlaceholder(string dc, string domainFqdn, string gpoName, string description)
        {
            var result = new AdObjectCreationResult
            {
                ObjectType = "GPO",
                ObjectName = gpoName
            };

            try
            {
                _progress?.Report($"Creating GPO: {gpoName}");

                // GPO creation via LDAP requires creating objects in CN=Policies,CN=System
                // This is complex - for production use, recommend PowerShell GroupPolicy module
                // Here we'll create a placeholder entry to track intent

                var gpoContainerDn = $"CN=Policies,CN=System,{DomainToDn(domainFqdn)}";
                
                using var gpoContainer = new DirectoryEntry($"LDAP://{dc}/{gpoContainerDn}");
                
                // Check if GPO already exists (search by displayName in groupPolicyContainer objects)
                using var searcher = new DirectorySearcher(gpoContainer)
                {
                    Filter = $"(&(objectClass=groupPolicyContainer)(displayName={gpoName}))",
                    SearchScope = SearchScope.OneLevel
                };

                var existing = searcher.FindOne();
                if (existing != null)
                {
                    result.Success = true;
                    result.Message = "GPO already exists";
                    result.DistinguishedName = existing.Properties["distinguishedName"]?[0]?.ToString() ?? "";
                    _progress?.Report($"GPO already exists: {gpoName}");
                    return result;
                }

                // Create new GPO container
                // Generate a new GUID for the GPO
                var gpoGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
                var gpoCn = $"CN={gpoGuid}";

                using var newGpo = gpoContainer.Children.Add(gpoCn, "groupPolicyContainer");
                newGpo.Properties["displayName"].Value = gpoName;
                newGpo.Properties["gPCFileSysPath"].Value = $"\\\\{domainFqdn}\\SysVol\\{domainFqdn}\\Policies\\{gpoGuid}";
                newGpo.Properties["gPCFunctionalityVersion"].Value = 2;
                newGpo.Properties["flags"].Value = 0; // GPO enabled
                newGpo.Properties["versionNumber"].Value = 0;
                newGpo.CommitChanges();

                result.Success = true;
                result.Message = $"Created. {description}";
                result.DistinguishedName = $"{gpoCn},{gpoContainerDn}";
                _progress?.Report($"Created GPO: {gpoName}");

                // Note: SYSVOL folder structure and GPT.INI would need to be created separately
                // For full GPO functionality, use PowerShell: New-GPO, Set-GPRegistryValue, etc.
            }
            catch (UnauthorizedAccessException)
            {
                result.Success = false;
                result.Message = "Access denied. Requires Domain Admin or GPO Creator Owners rights.";
                _progress?.Report($"Access denied creating GPO: {gpoName}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                result.Error = ex;
                _progress?.Report($"Failed to create GPO {gpoName}: {ex.Message}");
            }

            return result;
        }

        #endregion
    }
}
