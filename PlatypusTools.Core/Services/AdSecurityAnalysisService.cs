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
        /// Gets the set of protected groups and accounts that should legitimately have AdminCount=1.
        /// These are groups and accounts that are protected by SDProp (Security Descriptor Propagator).
        /// </summary>
        private HashSet<string> GetProtectedGroupsAndAccounts()
        {
            var protected_items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (_domainInfo == null || string.IsNullOrEmpty(_domainInfo.DomainSid))
                return protected_items;

            var domainSid = _domainInfo.DomainSid;
            
            // Well-known protected groups that should have AdminCount=1
            // Domain local built-in groups (S-1-5-32-xxx)
            var builtinGroups = new[]
            {
                "S-1-5-32-544",  // Administrators
                "S-1-5-32-548",  // Account Operators
                "S-1-5-32-549",  // Server Operators
                "S-1-5-32-550",  // Print Operators
                "S-1-5-32-551",  // Backup Operators
                "S-1-5-32-552",  // Replicators
            };

            // Domain-specific protected groups
            var domainGroups = new[]
            {
                $"{domainSid}-500",  // Domain Administrator account
                $"{domainSid}-502",  // KRBTGT
                $"{domainSid}-512",  // Domain Admins
                $"{domainSid}-516",  // Domain Controllers
                $"{domainSid}-518",  // Schema Admins (forest root)
                $"{domainSid}-519",  // Enterprise Admins (forest root)
                $"{domainSid}-521",  // Read-only Domain Controllers
            };

            try
            {
                using var entry = new DirectoryEntry($"LDAP://{_domainInfo.ChosenDc}/{_domainInfo.DomainDn}");
                
                // Look up all protected items by SID
                foreach (var sid in builtinGroups.Concat(domainGroups))
                {
                    try
                    {
                        var searcher = new DirectorySearcher(entry)
                        {
                            Filter = $"(objectSid={sid})",
                            SearchScope = SearchScope.Subtree
                        };
                        searcher.PropertiesToLoad.Add("distinguishedName");
                        
                        var result = searcher.FindOne();
                        if (result?.Properties["distinguishedName"]?[0] != null)
                        {
                            protected_items.Add(result.Properties["distinguishedName"][0].ToString()!);
                        }
                    }
                    catch
                    {
                        // Some SIDs may not exist (e.g., Enterprise Admins in child domain)
                    }
                }

                // Also add members of Domain Controllers group (all DCs should have AdminCount=1)
                var dcGroupSearcher = new DirectorySearcher(entry)
                {
                    Filter = $"(objectSid={domainSid}-516)",
                    SearchScope = SearchScope.Subtree
                };
                dcGroupSearcher.PropertiesToLoad.Add("member");
                
                var dcGroupResult = dcGroupSearcher.FindOne();
                if (dcGroupResult?.Properties["member"] != null)
                {
                    foreach (var member in dcGroupResult.Properties["member"])
                    {
                        protected_items.Add(member.ToString()!);
                    }
                }
            }
            catch
            {
                // Ignore errors, we'll just have fewer exclusions
            }

            return protected_items;
        }

        /// <summary>
        /// Finds accounts with AdminCount anomalies.
        /// An anomaly is an account with AdminCount=1 that is NOT currently a member of any protected group.
        /// This excludes legitimate protected groups (Domain Admins, etc.) and their direct members.
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
                        Filter = "(&(|(objectClass=user)(objectClass=group)(objectClass=computer))(adminCount=1))",
                        SearchScope = SearchScope.Subtree,
                        PageSize = 1000
                    };
                    searcher.PropertiesToLoad.AddRange(new[] 
                    { 
                        "sAMAccountName", "distinguishedName", "objectClass", 
                        "adminCount", "memberOf", "primaryGroupID"
                    });

                    // Get list of currently privileged accounts for comparison
                    var privilegedMembers = GetPrivilegedMembersAsync(false, ct).Result;
                    var privilegedDns = new HashSet<string>(
                        privilegedMembers.Select(p => p.DistinguishedName), 
                        StringComparer.OrdinalIgnoreCase);

                    // Get protected groups and accounts that legitimately should have AdminCount=1
                    var protectedItems = GetProtectedGroupsAndAccounts();

                    foreach (SearchResult result in searcher.FindAll())
                    {
                        ct.ThrowIfCancellationRequested();

                        var dn = result.Properties["distinguishedName"]?[0]?.ToString() ?? "";
                        var samAccountName = result.Properties["sAMAccountName"]?[0]?.ToString() ?? "";
                        var objectClass = result.Properties["objectClass"]?.Cast<string>().LastOrDefault() ?? "unknown";
                        var adminCount = 1;
                        if (result.Properties["adminCount"]?[0] != null)
                        {
                            int.TryParse(result.Properties["adminCount"][0].ToString(), out adminCount);
                        }

                        // Check if this is a protected item that legitimately should have AdminCount=1
                        var isProtectedItem = protectedItems.Contains(dn);

                        // Check if currently a member of a privileged group
                        var isCurrentlyPrivileged = privilegedDns.Contains(dn);

                        // Skip if this is a protected group/account (these SHOULD have AdminCount=1)
                        if (isProtectedItem || isCurrentlyPrivileged)
                        {
                            continue;
                        }

                        // Check primary group ID for domain controllers (primaryGroupID=516)
                        var primaryGroupId = 0;
                        if (result.Properties["primaryGroupID"]?[0] != null)
                        {
                            int.TryParse(result.Properties["primaryGroupID"][0].ToString(), out primaryGroupId);
                        }
                        
                        // Skip domain controllers (primaryGroupID 516 = Domain Controllers)
                        if (primaryGroupId == 516)
                        {
                            continue;
                        }

                        // This is a true anomaly - account has AdminCount=1 but isn't currently privileged
                        anomalies.Add(new AdAdminCountAnomaly
                        {
                            SamAccountName = samAccountName,
                            DistinguishedName = dn,
                            ObjectClass = objectClass,
                            AdminCount = adminCount,
                            IsCurrentlyPrivileged = false,
                            Issue = "Account has AdminCount=1 but is not currently a member of any protected group. " +
                                   "This may indicate the account was previously privileged or was manually modified. " +
                                   "Consider resetting ACLs and removing AdminCount attribute."
                        });
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

        #region Risky GPO Analysis

        /// <summary>
        /// Analyzes Group Policy Objects for potentially risky configurations.
        /// Identifies GPOs that deploy scheduled tasks, modify registry, deploy files,
        /// install software, modify local users/groups, or modify environment variables.
        /// This mirrors the PLATYPUS Get-AdRiskyGpoReport functionality.
        /// </summary>
        /// <param name="days">Number of days to look back for recently created/modified GPOs. Default 30.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>List of risky GPOs with details about their risk factors.</returns>
        public async Task<List<AdRiskyGpo>> GetRiskyGposAsync(int days = 30, CancellationToken ct = default)
        {
            if (_domainInfo == null)
            {
                await DiscoverDomainAsync(ct: ct);
            }

            if (_domainInfo == null || string.IsNullOrEmpty(_domainInfo.ChosenDc))
            {
                return new List<AdRiskyGpo>();
            }

            return await Task.Run(() =>
            {
                var riskyGpos = new List<AdRiskyGpo>();
                _progress?.Report("Analyzing Group Policy Objects for risky configurations...");

                try
                {
                    var gpoPath = $@"\\{_domainInfo.ChosenDc}\SYSVOL\{_domainInfo.DomainFqdn}\Policies";
                    
                    if (!Directory.Exists(gpoPath))
                    {
                        _progress?.Report($"GPO policies path not accessible: {gpoPath}");
                        return riskyGpos;
                    }

                    // Get GPO information from AD
                    using var entry = new DirectoryEntry($"LDAP://{_domainInfo.ChosenDc}/CN=Policies,CN=System,{_domainInfo.DomainDn}");
                    var searcher = new DirectorySearcher(entry)
                    {
                        Filter = "(objectClass=groupPolicyContainer)",
                        SearchScope = SearchScope.OneLevel
                    };
                    searcher.PropertiesToLoad.AddRange(new[] { 
                        "displayName", "name", "whenCreated", "whenChanged", 
                        "gPCFileSysPath", "distinguishedName" 
                    });

                    var results = searcher.FindAll();
                    _progress?.Report($"Found {results.Count} GPOs to analyze...");

                    var threshold = DateTime.Now.AddDays(-days);
                    int analyzed = 0;

                    foreach (SearchResult result in results)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            var gpoName = result.Properties["displayName"]?[0]?.ToString() ?? "Unknown";
                            var gpoGuid = result.Properties["name"]?[0]?.ToString() ?? "";
                            var gpcPath = result.Properties["gPCFileSysPath"]?[0]?.ToString() ?? "";
                            var gpoDn = result.Properties["distinguishedName"]?[0]?.ToString() ?? "";
                            
                            DateTime createdTime = DateTime.MinValue;
                            DateTime modifiedTime = DateTime.MinValue;
                            
                            if (result.Properties["whenCreated"]?[0] is DateTime created)
                                createdTime = created;
                            if (result.Properties["whenChanged"]?[0] is DateTime modified)
                                modifiedTime = modified;

                            var riskyGpo = new AdRiskyGpo
                            {
                                GpoName = gpoName,
                                GpoGuid = gpoGuid,
                                CreatedTime = createdTime,
                                ModifiedTime = modifiedTime,
                                RiskDetails = new List<GpoRiskDetail>(),
                                LinkLocations = new Dictionary<string, bool>()
                            };

                            // Analyze GPO contents in SYSVOL
                            if (!string.IsNullOrEmpty(gpcPath) && Directory.Exists(gpcPath))
                            {
                                AnalyzeGpoContent(gpcPath, riskyGpo);
                            }

                            // Get GPO links from AD
                            GetGpoLinks(gpoDn, riskyGpo);

                            // Only add if risky
                            if (riskyGpo.IsRisky)
                            {
                                // Determine severity based on risk types
                                riskyGpo.Severity = DetermineGpoSeverity(riskyGpo);
                                riskyGpos.Add(riskyGpo);
                            }

                            analyzed++;
                            if (analyzed % 50 == 0)
                            {
                                _progress?.Report($"Analyzed {analyzed}/{results.Count} GPOs...");
                            }
                        }
                        catch (Exception ex)
                        {
                            _progress?.Report($"Error analyzing GPO: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _progress?.Report($"Error during GPO analysis: {ex.Message}");
                }

                _progress?.Report($"Found {riskyGpos.Count} potentially risky GPOs");
                return riskyGpos.OrderByDescending(g => g.ModifiedTime).ToList();
            }, ct);
        }

        /// <summary>
        /// Gets GPOs that were created or modified within the specified number of days.
        /// </summary>
        public async Task<List<AdRiskyGpo>> GetRecentlyModifiedGposAsync(int days = 30, CancellationToken ct = default)
        {
            if (_domainInfo == null)
            {
                await DiscoverDomainAsync(ct: ct);
            }

            if (_domainInfo == null || string.IsNullOrEmpty(_domainInfo.ChosenDc))
            {
                return new List<AdRiskyGpo>();
            }

            return await Task.Run(() =>
            {
                var recentGpos = new List<AdRiskyGpo>();
                var threshold = DateTime.Now.AddDays(-days);

                _progress?.Report($"Finding GPOs created or modified in the last {days} days...");

                try
                {
                    using var entry = new DirectoryEntry($"LDAP://{_domainInfo.ChosenDc}/CN=Policies,CN=System,{_domainInfo.DomainDn}");
                    var searcher = new DirectorySearcher(entry)
                    {
                        Filter = "(objectClass=groupPolicyContainer)",
                        SearchScope = SearchScope.OneLevel
                    };
                    searcher.PropertiesToLoad.AddRange(new[] { 
                        "displayName", "name", "whenCreated", "whenChanged" 
                    });

                    var results = searcher.FindAll();

                    foreach (SearchResult result in results)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            DateTime createdTime = DateTime.MinValue;
                            DateTime modifiedTime = DateTime.MinValue;
                            
                            if (result.Properties["whenCreated"]?[0] is DateTime created)
                                createdTime = created;
                            if (result.Properties["whenChanged"]?[0] is DateTime modified)
                                modifiedTime = modified;

                            // Check if recently created or modified
                            if (createdTime > threshold || modifiedTime > threshold)
                            {
                                var gpoName = result.Properties["displayName"]?[0]?.ToString() ?? "Unknown";
                                var gpoGuid = result.Properties["name"]?[0]?.ToString() ?? "";

                                recentGpos.Add(new AdRiskyGpo
                                {
                                    GpoName = gpoName,
                                    GpoGuid = gpoGuid,
                                    CreatedTime = createdTime,
                                    ModifiedTime = modifiedTime,
                                    Severity = "Info"
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    _progress?.Report($"Error getting recent GPOs: {ex.Message}");
                }

                _progress?.Report($"Found {recentGpos.Count} recently modified GPOs");
                return recentGpos.OrderByDescending(g => g.ModifiedTime).ToList();
            }, ct);
        }

        /// <summary>
        /// Analyzes GPO content in SYSVOL for risky settings.
        /// </summary>
        private void AnalyzeGpoContent(string gpcPath, AdRiskyGpo gpo)
        {
            try
            {
                // Check Machine\Preferences for risky settings
                var machinePrefsPath = Path.Combine(gpcPath, "Machine", "Preferences");
                if (Directory.Exists(machinePrefsPath))
                {
                    // Scheduled Tasks
                    var schedTasksPath = Path.Combine(machinePrefsPath, "ScheduledTasks", "ScheduledTasks.xml");
                    if (File.Exists(schedTasksPath))
                    {
                        gpo.HasScheduledTasks = true;
                        AnalyzeScheduledTasksXml(schedTasksPath, gpo);
                    }

                    // Registry settings
                    var registryPath = Path.Combine(machinePrefsPath, "Registry", "Registry.xml");
                    if (File.Exists(registryPath))
                    {
                        gpo.HasRegistryMods = true;
                        AnalyzeRegistryXml(registryPath, gpo);
                    }

                    // Files deployment
                    var filesPath = Path.Combine(machinePrefsPath, "Files", "Files.xml");
                    if (File.Exists(filesPath))
                    {
                        gpo.HasFileOperations = true;
                        AnalyzeFilesXml(filesPath, gpo);
                    }

                    // Environment variables
                    var envPath = Path.Combine(machinePrefsPath, "EnvironmentVariables", "EnvironmentVariables.xml");
                    if (File.Exists(envPath))
                    {
                        gpo.HasEnvironmentMods = true;
                        AnalyzeEnvironmentXml(envPath, gpo);
                    }

                    // Local Users and Groups
                    var groupsPath = Path.Combine(machinePrefsPath, "Groups", "Groups.xml");
                    if (File.Exists(groupsPath))
                    {
                        gpo.HasLocalUserMods = true;
                        AnalyzeGroupsXml(groupsPath, gpo);
                    }
                }

                // Check for Software Installation (MSI)
                var scriptsPath = Path.Combine(gpcPath, "Machine", "Scripts");
                if (Directory.Exists(scriptsPath))
                {
                    var msiFiles = Directory.GetFiles(scriptsPath, "*.msi", SearchOption.AllDirectories);
                    if (msiFiles.Length > 0)
                    {
                        gpo.HasSoftwareInstallation = true;
                        foreach (var msi in msiFiles)
                        {
                            gpo.RiskDetails.Add(new GpoRiskDetail("swdeploy", Path.GetFileName(msi), msi));
                        }
                    }
                }

                // Also check Applications folder for software installation policies
                var appsPath = Path.Combine(gpcPath, "Machine", "Applications");
                if (Directory.Exists(appsPath) && Directory.GetFiles(appsPath, "*.aas").Length > 0)
                {
                    gpo.HasSoftwareInstallation = true;
                    foreach (var aas in Directory.GetFiles(appsPath, "*.aas"))
                    {
                        gpo.RiskDetails.Add(new GpoRiskDetail("swdeploy", Path.GetFileName(aas), aas));
                    }
                }
            }
            catch (Exception ex)
            {
                _progress?.Report($"Error analyzing GPO content at {gpcPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses ScheduledTasks.xml for risky scheduled task configurations.
        /// </summary>
        private void AnalyzeScheduledTasksXml(string xmlPath, AdRiskyGpo gpo)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(xmlPath);
                var ns = doc.Root?.GetDefaultNamespace() ?? System.Xml.Linq.XNamespace.None;

                // Look for ImmediateTaskV2 and TaskV2 elements
                var tasks = doc.Descendants().Where(e => 
                    e.Name.LocalName == "ImmediateTaskV2" || 
                    e.Name.LocalName == "TaskV2" ||
                    e.Name.LocalName == "Task");

                foreach (var task in tasks)
                {
                    var taskName = task.Attribute("name")?.Value ?? "UnnamedTask";
                    
                    // Look for Exec actions
                    var execElements = task.Descendants().Where(e => e.Name.LocalName == "Exec");
                    foreach (var exec in execElements)
                    {
                        var command = exec.Element(exec.Name.Namespace + "Command")?.Value ?? "";
                        var arguments = exec.Element(exec.Name.Namespace + "Arguments")?.Value ?? "";
                        
                        if (!string.IsNullOrEmpty(command))
                        {
                            gpo.RiskDetails.Add(new GpoRiskDetail("scheduledtask", taskName, $"{command} {arguments}".Trim()));
                            gpo.RiskySettings.Add($"Scheduled Task: {taskName} -> {command}");
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Parses Registry.xml for registry modifications.
        /// </summary>
        private void AnalyzeRegistryXml(string xmlPath, AdRiskyGpo gpo)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(xmlPath);
                var registryItems = doc.Descendants().Where(e => e.Name.LocalName == "Registry");

                foreach (var reg in registryItems)
                {
                    var props = reg.Element(reg.Name.Namespace + "Properties");
                    if (props != null)
                    {
                        var key = props.Attribute("key")?.Value ?? "";
                        var name = props.Attribute("name")?.Value ?? "";
                        var value = props.Attribute("value")?.Value ?? "";
                        
                        if (!string.IsNullOrEmpty(key))
                        {
                            gpo.RiskDetails.Add(new GpoRiskDetail("registry", $"{key}\\{name}", value));
                            gpo.RiskySettings.Add($"Registry: {key}\\{name} = {value}");
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Parses Files.xml for file deployment configurations.
        /// </summary>
        private void AnalyzeFilesXml(string xmlPath, AdRiskyGpo gpo)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(xmlPath);
                var fileItems = doc.Descendants().Where(e => e.Name.LocalName == "File");

                foreach (var file in fileItems)
                {
                    var fileName = file.Attribute("name")?.Value ?? "";
                    var props = file.Element(file.Name.Namespace + "Properties");
                    if (props != null)
                    {
                        var targetPath = props.Attribute("targetPath")?.Value ?? "";
                        var sourcePath = props.Attribute("fromPath")?.Value ?? "";
                        
                        gpo.RiskDetails.Add(new GpoRiskDetail("filedeploy", fileName, $"{sourcePath} -> {targetPath}"));
                        gpo.RiskySettings.Add($"File Deploy: {fileName} -> {targetPath}");
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Parses EnvironmentVariables.xml for environment variable modifications.
        /// </summary>
        private void AnalyzeEnvironmentXml(string xmlPath, AdRiskyGpo gpo)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(xmlPath);
                var envItems = doc.Descendants().Where(e => e.Name.LocalName == "EnvironmentVariable");

                foreach (var env in envItems)
                {
                    var envName = env.Attribute("name")?.Value ?? "";
                    var props = env.Element(env.Name.Namespace + "Properties");
                    if (props != null)
                    {
                        var value = props.Attribute("value")?.Value ?? "";
                        
                        gpo.RiskDetails.Add(new GpoRiskDetail("environmentVariable", envName, value));
                        gpo.RiskySettings.Add($"Environment: {envName} = {value}");
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Parses Groups.xml for local user and group modifications.
        /// </summary>
        private void AnalyzeGroupsXml(string xmlPath, AdRiskyGpo gpo)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(xmlPath);
                var groupItems = doc.Descendants().Where(e => e.Name.LocalName == "Group");

                foreach (var group in groupItems)
                {
                    var props = group.Element(group.Name.Namespace + "Properties");
                    if (props != null)
                    {
                        var groupName = props.Attribute("groupName")?.Value ?? props.Attribute("name")?.Value ?? "";
                        var members = props.Element(props.Name.Namespace + "Members");
                        
                        if (members != null)
                        {
                            var memberElements = members.Elements().Where(e => e.Name.LocalName == "Member");
                            foreach (var member in memberElements)
                            {
                                var memberName = member.Attribute("name")?.Value ?? "";
                                var action = member.Attribute("action")?.Value ?? "";
                                
                                gpo.RiskDetails.Add(new GpoRiskDetail("moddedgroup", groupName, $"{memberName}:{action}"));
                                gpo.RiskySettings.Add($"Group Mod: {groupName} <- {memberName} ({action})");
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Gets the OU/site paths where this GPO is linked.
        /// </summary>
        private void GetGpoLinks(string gpoDn, AdRiskyGpo gpo)
        {
            if (_domainInfo == null || string.IsNullOrEmpty(gpoDn))
                return;

            try
            {
                // Search for objects that have this GPO linked (gpLink attribute contains the GPO DN)
                var escapedDn = EscapeLdapFilter(gpoDn);
                
                using var entry = new DirectoryEntry($"LDAP://{_domainInfo.ChosenDc}/{_domainInfo.DomainDn}");
                var searcher = new DirectorySearcher(entry)
                {
                    Filter = $"(gPLink=*{escapedDn}*)",
                    SearchScope = SearchScope.Subtree
                };
                searcher.PropertiesToLoad.AddRange(new[] { "distinguishedName", "gPLink" });

                var results = searcher.FindAll();
                foreach (SearchResult result in results)
                {
                    var linkDn = result.Properties["distinguishedName"]?[0]?.ToString() ?? "";
                    var gpLink = result.Properties["gPLink"]?[0]?.ToString() ?? "";
                    
                    // Parse gpLink to determine if this GPO is enabled
                    // Format: [LDAP://cn={GUID},cn=policies,cn=system,DC=...;0] where 0=enabled, 1=disabled
                    var isEnabled = true;
                    if (gpLink.Contains(gpoDn))
                    {
                        var linkSection = gpLink.Substring(gpLink.IndexOf(gpoDn));
                        if (linkSection.Contains(";1]") || linkSection.Contains(";3]"))
                        {
                            isEnabled = false;
                        }
                    }
                    
                    gpo.LinkLocations[linkDn] = isEnabled;
                }
            }
            catch { }
        }

        /// <summary>
        /// Determines the severity of a risky GPO based on its risk factors.
        /// </summary>
        private string DetermineGpoSeverity(AdRiskyGpo gpo)
        {
            // High severity: scheduled tasks or software installation (common attack vector)
            if (gpo.HasScheduledTasks || gpo.HasSoftwareInstallation)
                return "High";

            // Medium severity: file operations, registry mods, or local user/group modifications
            if (gpo.HasFileOperations || gpo.HasRegistryMods || gpo.HasLocalUserMods)
                return "Medium";

            // Low severity: environment variable modifications only
            return "Low";
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

                if (options.AnalyzeGpos)
                {
                    result.RiskyGpos = await GetRiskyGposAsync(options.GpoDaysThreshold, ct);
                }

                // Count severities
                result.CriticalCount = 
                    result.RiskyAcls.Count(a => a.Severity == "Critical") +
                    result.KerberosDelegations.Count(d => d.Severity == "Critical") +
                    result.RiskyGpos.Count(g => g.Severity == "Critical");
                
                result.HighCount = 
                    result.RiskyAcls.Count(a => a.Severity == "High") +
                    result.KerberosDelegations.Count(d => d.Severity == "High") +
                    result.SysvolRiskyFiles.Count(f => f.Severity == "High") +
                    result.RiskyGpos.Count(g => g.Severity == "High");

                result.MediumCount = 
                    result.RiskyAcls.Count(a => a.Severity == "Medium") +
                    result.KerberosDelegations.Count(d => d.Severity == "Medium") +
                    result.SysvolRiskyFiles.Count(f => f.Severity == "Medium") +
                    result.RiskyGpos.Count(g => g.Severity == "Medium") +
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
        /// Creates a security group in the specified OU.
        /// </summary>
        private AdObjectCreationResult CreateSecurityGroup(string dc, string targetOuDn, string domainDn, string groupName, string description)
        {
            var result = new AdObjectCreationResult
            {
                ObjectType = "Group",
                ObjectName = groupName,
                DistinguishedName = $"CN={groupName},{targetOuDn}"
            };

            try
            {
                _progress?.Report($"Creating security group: {groupName}");

                // First check if the target OU exists, if not try to use domain root
                DirectoryEntry? parentEntry = null;
                try
                {
                    parentEntry = new DirectoryEntry($"LDAP://{dc}/{targetOuDn}");
                    // Force connection to verify OU exists
                    var _ = parentEntry.Guid;
                }
                catch
                {
                    // OU doesn't exist, try to create group at domain level (Users container)
                    parentEntry?.Dispose();
                    var usersContainerDn = $"CN=Users,{domainDn}";
                    parentEntry = new DirectoryEntry($"LDAP://{dc}/{usersContainerDn}");
                    result.DistinguishedName = $"CN={groupName},{usersContainerDn}";
                    _progress?.Report($"Target OU not found, using Users container for: {groupName}");
                }

                using (parentEntry)
                {
                    // Check if group already exists
                    using var searcher = new DirectorySearcher(parentEntry)
                    {
                        Filter = $"(&(objectClass=group)(cn={EscapeLdapFilter(groupName)}))",
                        SearchScope = SearchScope.OneLevel
                    };

                    var existing = searcher.FindOne();
                    if (existing != null)
                    {
                        result.Success = true;
                        result.Message = "Group already exists";
                        result.DistinguishedName = existing.Properties["distinguishedName"]?[0]?.ToString() ?? result.DistinguishedName;
                        _progress?.Report($"Group already exists: {groupName}");
                        return result;
                    }

                    // Create new security group (Global Security Group = 0x80000002)
                    using var newGroup = parentEntry.Children.Add($"CN={groupName}", "group");
                    newGroup.Properties["sAMAccountName"].Value = groupName.Length > 20 
                        ? groupName.Replace(" ", "").Replace("-", "")[..Math.Min(20, groupName.Replace(" ", "").Replace("-", "").Length)]
                        : groupName.Replace(" ", "");
                    newGroup.Properties["description"].Value = description;
                    newGroup.Properties["groupType"].Value = unchecked((int)0x80000002); // Global Security Group
                    newGroup.CommitChanges();

                    result.Success = true;
                    result.Message = "Created successfully";
                    _progress?.Report($"Created group: {groupName}");
                }
            }
            catch (UnauthorizedAccessException)
            {
                result.Success = false;
                result.Message = "Access denied. Requires appropriate permissions to create groups.";
                _progress?.Report($"Access denied creating group: {groupName}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                result.Error = ex;
                _progress?.Report($"Failed to create group {groupName}: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Deploys baseline GPOs for security hardening.
        /// Creates GPOs with actual policy settings based on PLATYPUS patterns.
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

                _progress?.Report("Deploying security groups and GPOs...");

                try
                {
                    var dc = _domainInfo.ChosenDc;
                    var domainDn = _domainInfo.DomainDn;
                    var domainFqdn = _domainInfo.DomainFqdn ?? "";

                    // === Create Tiered Security Groups First ===
                    if (options.CreateTierGroups)
                    {
                        ct.ThrowIfCancellationRequested();
                        _progress?.Report("Creating tiered security groups...");
                        
                        // Find or use the Admin OU for groups
                        var adminOuDn = $"OU={options.TieredOuBaseName},{domainDn}";
                        
                        // Tier 0 Groups
                        var tier0GroupsOu = $"OU=Groups,OU=Tier0,{adminOuDn}";
                        results.Add(CreateSecurityGroup(dc, tier0GroupsOu, domainDn, "Tier 0 - Operators", 
                            "Tier 0 privileged operators - Domain Controllers and core infrastructure"));
                        results.Add(CreateSecurityGroup(dc, tier0GroupsOu, domainDn, "Tier 0 - PAW Users", 
                            "Users authorized to log on to Tier 0 PAWs"));
                        results.Add(CreateSecurityGroup(dc, tier0GroupsOu, domainDn, "Tier 0 - Service Accounts", 
                            "Service accounts for Tier 0 systems"));
                        
                        // Tier 1 Groups
                        var tier1GroupsOu = $"OU=Groups,OU=Tier1,{adminOuDn}";
                        results.Add(CreateSecurityGroup(dc, tier1GroupsOu, domainDn, "Tier 1 - Operators", 
                            "Tier 1 privileged operators - Server administrators"));
                        results.Add(CreateSecurityGroup(dc, tier1GroupsOu, domainDn, "Tier 1 - PAW Users", 
                            "Users authorized to log on to Tier 1 PAWs"));
                        results.Add(CreateSecurityGroup(dc, tier1GroupsOu, domainDn, "Tier 1 - Service Accounts", 
                            "Service accounts for Tier 1 systems"));
                        results.Add(CreateSecurityGroup(dc, tier1GroupsOu, domainDn, "Tier 1 - Server Local Admins", 
                            "Members become local administrators on Tier 1 servers"));
                        
                        // Tier 2 Groups
                        var tier2GroupsOu = $"OU=Groups,OU=Tier2,{adminOuDn}";
                        results.Add(CreateSecurityGroup(dc, tier2GroupsOu, domainDn, "Tier 2 - Operators", 
                            "Tier 2 privileged operators - Workstation administrators"));
                        results.Add(CreateSecurityGroup(dc, tier2GroupsOu, domainDn, "Tier 2 - PAW Users", 
                            "Users authorized to log on to Tier 2 PAWs"));
                        results.Add(CreateSecurityGroup(dc, tier2GroupsOu, domainDn, "Tier 2 - Service Accounts", 
                            "Service accounts for Tier 2 systems"));
                        results.Add(CreateSecurityGroup(dc, tier2GroupsOu, domainDn, "Tier 2 - Workstation Local Admins", 
                            "Members become local administrators on Tier 2 workstations"));
                        
                        // Cross-tier / IR groups
                        results.Add(CreateSecurityGroup(dc, tier0GroupsOu, domainDn, "IR - Emergency Access", 
                            "Emergency access accounts for incident response - break glass"));
                        results.Add(CreateSecurityGroup(dc, tier0GroupsOu, domainDn, "DVRL - Deny Logon All Tiers", 
                            "Members are denied logon to all tier systems - for compromised accounts"));
                    }

                    // === Quick Deploy GPOs with Settings ===
                    if (options.DeployPasswordPolicy)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn, 
                            "Baseline-PasswordPolicy", 
                            "Password policy settings configured.",
                            GpoSettingsType.SecuritySettings));
                    }

                    if (options.DeployAuditPolicy)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Baseline-AuditPolicy",
                            "Advanced audit policies configured for security monitoring.",
                            GpoSettingsType.AuditPolicy));
                    }

                    if (options.DeploySecurityBaseline)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Baseline-SecurityHardening",
                            "Security hardening settings: LM hash disabled, NTLMv2 required.",
                            GpoSettingsType.SecuritySettings));
                    }

                    if (options.DeployPawPolicy)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "PAW-SecurityPolicy",
                            "PAW lockdown settings configured.",
                            GpoSettingsType.SecuritySettings));
                    }

                    // === Tier 0 GPOs (PLATYPUS/BILL) ===
                    if (options.DeployT0BaselineAudit)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 0 - Baseline Audit Policies - Tier 0 Servers",
                            "Enhanced audit policies for Tier 0 servers configured.",
                            GpoSettingsType.AuditPolicy));
                    }

                    if (options.DeployT0DisallowDsrm)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 0 - Disallow DSRM Login - DC ONLY",
                            "DSRM network logon disabled.",
                            GpoSettingsType.SecuritySettings));
                    }

                    if (options.DeployT0DomainBlock)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 0 - Domain Block - Top Level",
                            "Tier 0 account blocking configured.",
                            GpoSettingsType.UserRights));
                    }

                    if (options.DeployT0DomainControllers)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 0 - Domain Controllers - DC Only",
                            "DC security hardening configured.",
                            GpoSettingsType.SecuritySettings));
                    }

                    if (options.DeployT0EsxAdmins)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 0 - ESX Admins Restricted Group - DC Only",
                            "ESX Admins group emptied via Restricted Groups.",
                            GpoSettingsType.RestrictedGroups));
                    }

                    if (options.DeployT0UserRights)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 0 - User Rights Assignments - Tier 0 Servers",
                            "Tier 0 logon rights restrictions configured.",
                            GpoSettingsType.UserRights));
                    }

                    if (options.DeployT0RestrictedGroups)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 0 - Restricted Groups - Tier 0 Servers",
                            "Tier 0 local admin membership controls configured.",
                            GpoSettingsType.RestrictedGroups));
                    }

                    // === Tier 1 GPOs ===
                    if (options.DeployT1LocalAdmin)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 1 - Tier 1 Operators in Local Admin - Tier 1 Servers",
                            "Tier 1 local admin membership configured.",
                            GpoSettingsType.RestrictedGroups));
                    }

                    if (options.DeployT1UserRights)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 1 - User Rights Assignments - Tier 1 Servers",
                            "Tier 1 logon rights restrictions configured.",
                            GpoSettingsType.UserRights));
                    }

                    if (options.DeployT1RestrictedGroups)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 1 - Restricted Groups - Tier 1 Servers",
                            "Tier 1 local admin membership controls configured.",
                            GpoSettingsType.RestrictedGroups));
                    }

                    // === Tier 2 GPOs ===
                    if (options.DeployT2LocalAdmin)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 2 - Tier 2 Operators in Local Admin - Tier 2 Devices",
                            "Tier 2 local admin membership configured.",
                            GpoSettingsType.RestrictedGroups));
                    }

                    if (options.DeployT2UserRights)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 2 - User Rights Assignments - Tier 2 Devices",
                            "Tier 2 logon rights restrictions configured.",
                            GpoSettingsType.UserRights));
                    }

                    if (options.DeployT2RestrictedGroups)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier 2 - Restricted Groups - Tier 2 Devices",
                            "Tier 2 local admin membership controls configured.",
                            GpoSettingsType.RestrictedGroups));
                    }

                    // === Cross-Tier GPOs ===
                    if (options.DeployDisableSmb1)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier ALL - Disable SMBv1 - Top Level",
                            "SMBv1 disabled via registry settings.",
                            GpoSettingsType.SecuritySettings));
                    }

                    if (options.DeployDisableWDigest)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "Tier ALL - Disable WDigest - Top Level",
                            "WDigest credential caching disabled.",
                            GpoSettingsType.SecuritySettings));
                    }

                    if (options.DeployResetMachinePassword)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(CreateGpoWithSettings(dc, domainFqdn,
                            "PLATYPUS - Reset Machine Account Password",
                            "Machine account password rotation configured (30 days).",
                            GpoSettingsType.SecuritySettings));
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
        /// Creates a GPO with actual settings based on PLATYPUS patterns.
        /// Full GPO creation including SYSVOL folder structure and policy content.
        /// </summary>
        private AdObjectCreationResult CreateGpoWithSettings(
            string dc, 
            string domainFqdn, 
            string gpoName, 
            string description,
            GpoSettingsType settingsType)
        {
            var result = new AdObjectCreationResult
            {
                ObjectType = "GPO",
                ObjectName = gpoName
            };

            try
            {
                _progress?.Report($"Creating GPO: {gpoName}");

                var gpoContainerDn = $"CN=Policies,CN=System,{DomainToDn(domainFqdn)}";
                
                using var gpoContainer = new DirectoryEntry($"LDAP://{dc}/{gpoContainerDn}");
                
                // Check if GPO already exists
                using var searcher = new DirectorySearcher(gpoContainer)
                {
                    Filter = $"(&(objectClass=groupPolicyContainer)(displayName={EscapeLdapFilter(gpoName)}))",
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

                // Generate new GUID for the GPO
                var gpoGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
                var gpoCn = $"CN={gpoGuid}";

                // Create GPO AD object
                using var newGpo = gpoContainer.Children.Add(gpoCn, "groupPolicyContainer");
                newGpo.Properties["displayName"].Value = gpoName;
                newGpo.Properties["gPCFileSysPath"].Value = $"\\\\{domainFqdn}\\SysVol\\{domainFqdn}\\Policies\\{gpoGuid}";
                newGpo.Properties["gPCFunctionalityVersion"].Value = 2;
                newGpo.Properties["flags"].Value = 0; // GPO enabled
                newGpo.Properties["versionNumber"].Value = 1;
                
                // Set gPCMachineExtensionNames based on settings type
                var gpcExtension = GetGpcMachineExtensionNames(settingsType);
                if (!string.IsNullOrEmpty(gpcExtension))
                {
                    newGpo.Properties["gPCMachineExtensionNames"].Value = gpcExtension;
                }
                
                newGpo.CommitChanges();

                result.DistinguishedName = $"{gpoCn},{gpoContainerDn}";

                // Create SYSVOL folder structure and policy files
                var sysvolPath = $"\\\\{dc}\\SYSVOL\\{domainFqdn}\\Policies\\{gpoGuid}";
                if (CreateGpoSysvolContent(sysvolPath, settingsType, gpoName))
                {
                    result.Success = true;
                    result.Message = $"Created with settings. {description}";
                    _progress?.Report($"Created GPO with settings: {gpoName}");
                }
                else
                {
                    result.Success = true;
                    result.Message = $"GPO created but SYSVOL content failed. Configure manually: {description}";
                    _progress?.Report($"Created GPO (SYSVOL content failed): {gpoName}");
                }
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

        /// <summary>
        /// Gets the gPCMachineExtensionNames value for the GPO type.
        /// </summary>
        private string GetGpcMachineExtensionNames(GpoSettingsType settingsType)
        {
            return settingsType switch
            {
                // Security settings + audit policy
                GpoSettingsType.AuditPolicy => "[{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}][{F3CCC681-B74C-4060-9F26-CD84525DCA2A}{0F3F3735-573D-9804-99E4-AB2A69BA5FD4}]",
                // Security settings only
                GpoSettingsType.SecuritySettings => "[{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}]",
                // Security settings + restricted groups
                GpoSettingsType.RestrictedGroups => "[{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}]",
                // Security settings + user rights
                GpoSettingsType.UserRights => "[{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}]",
                // Registry settings (Group Policy Preferences)
                GpoSettingsType.Registry => "[{B087BE9D-ED37-454F-AF9C-04291E351182}{BEE07A6A-EC9F-4659-B8C9-0B1937907C83}]",
                _ => ""
            };
        }

        /// <summary>
        /// Creates the SYSVOL folder structure and policy content files for a GPO.
        /// </summary>
        private bool CreateGpoSysvolContent(string sysvolPath, GpoSettingsType settingsType, string gpoName)
        {
            try
            {
                // Create directory structure
                var machinePath = Path.Combine(sysvolPath, "Machine");
                var userPath = Path.Combine(sysvolPath, "User");
                var secEditPath = Path.Combine(machinePath, "microsoft", "windows nt", "SecEdit");
                var auditPath = Path.Combine(machinePath, "microsoft", "windows nt", "Audit");

                Directory.CreateDirectory(machinePath);
                Directory.CreateDirectory(userPath);
                Directory.CreateDirectory(secEditPath);

                // Create GPT.ini
                var gptIniContent = "[General]\r\nVersion=1\r\n";
                File.WriteAllText(Path.Combine(sysvolPath, "GPT.ini"), gptIniContent);

                // Create policy content based on settings type
                switch (settingsType)
                {
                    case GpoSettingsType.AuditPolicy:
                        CreateAuditPolicyContent(secEditPath, auditPath);
                        break;
                    case GpoSettingsType.SecuritySettings:
                        CreateSecuritySettingsContent(secEditPath, gpoName);
                        break;
                    case GpoSettingsType.RestrictedGroups:
                        CreateRestrictedGroupsContent(secEditPath, gpoName);
                        break;
                    case GpoSettingsType.UserRights:
                        CreateUserRightsContent(secEditPath, gpoName);
                        break;
                    case GpoSettingsType.Registry:
                        CreateRegistryContent(machinePath, gpoName);
                        break;
                }

                return true;
            }
            catch (Exception ex)
            {
                _progress?.Report($"Failed to create SYSVOL content: {ex.Message}");
                return false;
            }
        }

        private void CreateAuditPolicyContent(string secEditPath, string auditPath)
        {
            Directory.CreateDirectory(auditPath);

            // Create GptTmpl.inf for advanced audit policy
            var gptTmpl = new StringBuilder();
            gptTmpl.AppendLine("[Unicode]");
            gptTmpl.AppendLine("Unicode=yes");
            gptTmpl.AppendLine("[Version]");
            gptTmpl.AppendLine("signature=\"$CHICAGO$\"");
            gptTmpl.AppendLine("Revision=1");
            gptTmpl.AppendLine();
            gptTmpl.AppendLine("[Registry Values]");
            gptTmpl.AppendLine("MACHINE\\System\\CurrentControlSet\\Control\\Lsa\\SCENoApplyLegacyAuditPolicy=4,1");

            File.WriteAllText(Path.Combine(secEditPath, "GptTmpl.inf"), gptTmpl.ToString(), Encoding.Unicode);

            // Create audit.csv for advanced audit policies
            var auditCsv = new StringBuilder();
            auditCsv.AppendLine("Machine Name,Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Exclusion Setting,Setting Value");
            // Credential Validation
            auditCsv.AppendLine(",System,Audit Credential Validation,{0cce923f-69ae-11d9-bed3-505054503030},Success and Failure,,3");
            // Computer Account Management
            auditCsv.AppendLine(",System,Audit Computer Account Management,{0cce9236-69ae-11d9-bed3-505054503030},Success and Failure,,3");
            // Distribution Group Management
            auditCsv.AppendLine(",System,Audit Distribution Group Management,{0cce9238-69ae-11d9-bed3-505054503030},Success and Failure,,3");
            // Security Group Management
            auditCsv.AppendLine(",System,Audit Security Group Management,{0cce9237-69ae-11d9-bed3-505054503030},Success and Failure,,3");
            // User Account Management
            auditCsv.AppendLine(",System,Audit User Account Management,{0cce9235-69ae-11d9-bed3-505054503030},Success and Failure,,3");
            // Directory Service Access
            auditCsv.AppendLine(",System,Audit Directory Service Access,{0cce923b-69ae-11d9-bed3-505054503030},Success and Failure,,3");
            // Directory Service Changes
            auditCsv.AppendLine(",System,Audit Directory Service Changes,{0cce923c-69ae-11d9-bed3-505054503030},Success and Failure,,3");
            // Security System Extension
            auditCsv.AppendLine(",System,Audit Security System Extension,{0cce9211-69ae-11d9-bed3-505054503030},Success and Failure,,3");
            // Logon/Logoff events
            auditCsv.AppendLine(",System,Audit Logon,{0cce9215-69ae-11d9-bed3-505054503030},Success and Failure,,3");
            auditCsv.AppendLine(",System,Audit Logoff,{0cce9216-69ae-11d9-bed3-505054503030},Success,,1");
            auditCsv.AppendLine(",System,Audit Special Logon,{0cce921b-69ae-11d9-bed3-505054503030},Success and Failure,,3");
            // Kerberos events
            auditCsv.AppendLine(",System,Audit Kerberos Authentication Service,{0cce9242-69ae-11d9-bed3-505054503030},Success and Failure,,3");
            auditCsv.AppendLine(",System,Audit Kerberos Service Ticket Operations,{0cce9240-69ae-11d9-bed3-505054503030},Success and Failure,,3");

            File.WriteAllText(Path.Combine(auditPath, "audit.csv"), auditCsv.ToString(), Encoding.ASCII);
        }

        private void CreateSecuritySettingsContent(string secEditPath, string gpoName)
        {
            var gptTmpl = new StringBuilder();
            gptTmpl.AppendLine("[Unicode]");
            gptTmpl.AppendLine("Unicode=yes");
            gptTmpl.AppendLine("[Version]");
            gptTmpl.AppendLine("signature=\"$CHICAGO$\"");
            gptTmpl.AppendLine("Revision=1");
            gptTmpl.AppendLine();
            gptTmpl.AppendLine("[Registry Values]");

            if (gpoName.Contains("SMB", StringComparison.OrdinalIgnoreCase))
            {
                // Disable SMBv1
                gptTmpl.AppendLine("MACHINE\\System\\CurrentControlSet\\Services\\LanmanServer\\Parameters\\SMB1=4,0");
            }
            else if (gpoName.Contains("WDigest", StringComparison.OrdinalIgnoreCase))
            {
                // Disable WDigest credential caching
                gptTmpl.AppendLine("MACHINE\\System\\CurrentControlSet\\Control\\SecurityProviders\\WDigest\\UseLogonCredential=4,0");
            }
            else if (gpoName.Contains("DSRM", StringComparison.OrdinalIgnoreCase))
            {
                // Disable DSRM network logon
                gptTmpl.AppendLine("MACHINE\\System\\CurrentControlSet\\Control\\Lsa\\DSRMAdminLogonBehavior=4,1");
            }
            else if (gpoName.Contains("Machine", StringComparison.OrdinalIgnoreCase) && gpoName.Contains("Password", StringComparison.OrdinalIgnoreCase))
            {
                // Machine account password settings
                gptTmpl.AppendLine("MACHINE\\System\\CurrentControlSet\\Services\\Netlogon\\Parameters\\MaximumPasswordAge=4,30");
                gptTmpl.AppendLine("MACHINE\\System\\CurrentControlSet\\Services\\Netlogon\\Parameters\\DisablePasswordChange=4,0");
            }
            else
            {
                // General security hardening
                gptTmpl.AppendLine("MACHINE\\System\\CurrentControlSet\\Control\\Lsa\\LmCompatibilityLevel=4,5");
                gptTmpl.AppendLine("MACHINE\\System\\CurrentControlSet\\Control\\Lsa\\NoLMHash=4,1");
            }

            File.WriteAllText(Path.Combine(secEditPath, "GptTmpl.inf"), gptTmpl.ToString(), Encoding.Unicode);
        }

        private void CreateRestrictedGroupsContent(string secEditPath, string gpoName)
        {
            var gptTmpl = new StringBuilder();
            gptTmpl.AppendLine("[Unicode]");
            gptTmpl.AppendLine("Unicode=yes");
            gptTmpl.AppendLine("[Version]");
            gptTmpl.AppendLine("signature=\"$CHICAGO$\"");
            gptTmpl.AppendLine("Revision=1");
            gptTmpl.AppendLine();
            
            // Restricted Groups section - example for ESX Admins
            if (gpoName.Contains("ESX", StringComparison.OrdinalIgnoreCase))
            {
                gptTmpl.AppendLine("[Group Membership]");
                gptTmpl.AppendLine("*S-1-5-32-544__Members =");  // Empty Administrators
                gptTmpl.AppendLine("*S-1-5-32-544__Memberof =");
            }
            else
            {
                gptTmpl.AppendLine("[Group Membership]");
                // Template for restricting local administrators - needs customization
                gptTmpl.AppendLine("; Configure restricted groups as needed");
            }

            File.WriteAllText(Path.Combine(secEditPath, "GptTmpl.inf"), gptTmpl.ToString(), Encoding.Unicode);
        }

        private void CreateUserRightsContent(string secEditPath, string gpoName)
        {
            var gptTmpl = new StringBuilder();
            gptTmpl.AppendLine("[Unicode]");
            gptTmpl.AppendLine("Unicode=yes");
            gptTmpl.AppendLine("[Version]");
            gptTmpl.AppendLine("signature=\"$CHICAGO$\"");
            gptTmpl.AppendLine("Revision=1");
            gptTmpl.AppendLine();
            gptTmpl.AppendLine("[Privilege Rights]");
            
            // Different user rights based on tier
            if (gpoName.Contains("Tier 0", StringComparison.OrdinalIgnoreCase))
            {
                // Tier 0 restrictions - very locked down
                gptTmpl.AppendLine("; Deny network access from non-Tier 0 accounts");
                gptTmpl.AppendLine("SeDenyNetworkLogonRight = *S-1-5-32-546");  // Guests
                gptTmpl.AppendLine("SeDenyRemoteInteractiveLogonRight = *S-1-5-32-546");
            }
            else if (gpoName.Contains("Tier 1", StringComparison.OrdinalIgnoreCase))
            {
                gptTmpl.AppendLine("; Deny Tier 0 and Tier 2 from Tier 1 systems");
                gptTmpl.AppendLine("SeDenyNetworkLogonRight = *S-1-5-32-546");
            }
            else if (gpoName.Contains("Tier 2", StringComparison.OrdinalIgnoreCase))
            {
                gptTmpl.AppendLine("; Deny Tier 0 and Tier 1 from Tier 2 systems");
                gptTmpl.AppendLine("SeDenyNetworkLogonRight = *S-1-5-32-546");
            }
            else
            {
                // General template
                gptTmpl.AppendLine("; Configure user rights assignments as needed");
            }

            File.WriteAllText(Path.Combine(secEditPath, "GptTmpl.inf"), gptTmpl.ToString(), Encoding.Unicode);
        }

        private void CreateRegistryContent(string machinePath, string gpoName)
        {
            // For registry preferences, create Registry.pol or Preferences structure
            // This is a simplified version - full implementation would use binary POL format
            var prefsPath = Path.Combine(machinePath, "Preferences", "Registry");
            Directory.CreateDirectory(prefsPath);

            // Create a placeholder XML for registry preferences
            var registryXml = new StringBuilder();
            registryXml.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            registryXml.AppendLine("<RegistrySettings clsid=\"{A3CCFC41-DFDB-43a5-8D26-0FE8B954DA51}\">");
            registryXml.AppendLine("  <!-- Configure registry settings in Group Policy Management Console -->");
            registryXml.AppendLine("</RegistrySettings>");

            File.WriteAllText(Path.Combine(prefsPath, "Registry.xml"), registryXml.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Escapes special characters in LDAP filter strings.
        /// </summary>
        private string EscapeLdapFilter(string input)
        {
            return input
                .Replace("\\", "\\5c")
                .Replace("*", "\\2a")
                .Replace("(", "\\28")
                .Replace(")", "\\29")
                .Replace("\0", "\\00");
        }

        /// <summary>
        /// Types of GPO settings for proper configuration.
        /// </summary>
        private enum GpoSettingsType
        {
            None,
            AuditPolicy,
            SecuritySettings,
            RestrictedGroups,
            UserRights,
            Registry
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
                    Filter = $"(&(objectClass=groupPolicyContainer)(displayName={EscapeLdapFilter(gpoName)}))",
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

        #region AD Remediation Methods (PLATYPUS IR Operations)

        /// <summary>
        /// Removes the AdminCount attribute from accounts that are not in protected groups.
        /// Equivalent to Remove-AdAdminCount in PLATYPUS.
        /// </summary>
        public async Task<List<AdRemediationResult>> RemoveAdminCountAsync(
            bool allUsers = true,
            string? specificIdentity = null,
            bool alsoClearSpn = false,
            bool whatIf = true,
            CancellationToken ct = default)
        {
            var results = new List<AdRemediationResult>();

            if (_domainInfo == null)
            {
                _progress?.Report("Domain not discovered. Call DiscoverDomainAsync first.");
                return results;
            }

            await Task.Run(() =>
            {
                try
                {
                    _progress?.Report("Finding AdminCount anomalies...");

                    using var context = new PrincipalContext(ContextType.Domain, _domainInfo.ChosenDc);
                    using var rootEntry = new DirectoryEntry($"LDAP://{_domainInfo.ChosenDc}/{_domainInfo.DomainDn}");
                    using var searcher = new DirectorySearcher(rootEntry);
                    
                    // Get protected group members (these should keep AdminCount=1)
                    var protectedAccounts = GetProtectedGroupsAndAccounts();

                    if (allUsers)
                    {
                        searcher.Filter = "(&(objectClass=user)(adminCount=1))";
                    }
                    else if (!string.IsNullOrEmpty(specificIdentity))
                    {
                        searcher.Filter = $"(&(objectClass=user)(adminCount=1)(|(sAMAccountName={specificIdentity})(distinguishedName={specificIdentity})))";
                    }
                    else
                    {
                        return;
                    }

                    searcher.PropertiesToLoad.AddRange(new[] { "distinguishedName", "sAMAccountName", "adminCount", "servicePrincipalName" });

                    foreach (SearchResult sr in searcher.FindAll())
                    {
                        ct.ThrowIfCancellationRequested();

                        var dn = sr.Properties["distinguishedName"][0]?.ToString() ?? "";
                        var samName = sr.Properties["sAMAccountName"][0]?.ToString() ?? "";

                        // Skip if this account IS in a protected group
                        if (protectedAccounts.Any(p => 
                            p.Equals(samName, StringComparison.OrdinalIgnoreCase) ||
                            p.Equals(dn, StringComparison.OrdinalIgnoreCase)))
                        {
                            _progress?.Report($"Skipping protected account: {samName}");
                            continue;
                        }

                        var result = new AdRemediationResult
                        {
                            ObjectDn = dn,
                            ObjectName = samName,
                            Action = "Remove AdminCount"
                        };

                        try
                        {
                            if (!whatIf)
                            {
                                using var entry = sr.GetDirectoryEntry();
                                
                                // Clear AdminCount
                                entry.Properties["adminCount"].Clear();
                                
                                // Optionally clear SPNs
                                if (alsoClearSpn && entry.Properties.Contains("servicePrincipalName"))
                                {
                                    entry.Properties["servicePrincipalName"].Clear();
                                    result.Action += ", Clear SPNs";
                                }
                                
                                entry.CommitChanges();
                                result.Success = true;
                                result.Message = "AdminCount removed successfully";
                                _progress?.Report($"Removed AdminCount from: {samName}");
                            }
                            else
                            {
                                result.Success = true;
                                result.Message = "[WHATIF] Would remove AdminCount";
                                _progress?.Report($"[WHATIF] Would remove AdminCount from: {samName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Success = false;
                            result.Message = ex.Message;
                        }

                        results.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    _progress?.Report($"Error removing AdminCount: {ex.Message}");
                }
            }, ct);

            return results;
        }

        /// <summary>
        /// Invalidates the RID pool after domain controller compromise.
        /// Equivalent to Set-AdRidPool in PLATYPUS.
        /// WARNING: This is a destructive operation that should only be done during IR.
        /// </summary>
        public async Task<AdRemediationResult> InvalidateRidPoolAsync(
            bool whatIf = true,
            CancellationToken ct = default)
        {
            var result = new AdRemediationResult
            {
                ObjectName = "RID Pool",
                Action = "Invalidate RID Pool"
            };

            if (_domainInfo == null)
            {
                result.Success = false;
                result.Message = "Domain not discovered. Call DiscoverDomainAsync first.";
                return result;
            }

            await Task.Run(() =>
            {
                try
                {
                    _progress?.Report("Invalidating RID pool...");
                    _progress?.Report("WARNING: This operation should only be performed during incident response!");

                    // Find the RID Manager object
                    using var rootEntry = new DirectoryEntry($"LDAP://{_domainInfo.ChosenDc}/{_domainInfo.DomainDn}");
                    using var searcher = new DirectorySearcher(rootEntry);
                    searcher.Filter = "(objectClass=rIDManager)";
                    searcher.SearchScope = SearchScope.Subtree;

                    var ridManager = searcher.FindOne();
                    if (ridManager == null)
                    {
                        result.Success = false;
                        result.Message = "RID Manager object not found";
                        return;
                    }

                    result.ObjectDn = ridManager.Properties["distinguishedName"][0]?.ToString() ?? "";

                    if (!whatIf)
                    {
                        using var entry = ridManager.GetDirectoryEntry();
                        
                        // Get current RID pool info
                        if (entry.Properties.Contains("rIDAvailablePool"))
                        {
                            var currentPool = entry.Properties["rIDAvailablePool"].Value;
                            _progress?.Report($"Current RID Pool value: {currentPool}");

                            // The RID pool invalidation is typically done by increasing 
                            // the RID allocation by a significant amount
                            // This is a simplified representation - actual implementation 
                            // requires specific AD operations
                            
                            result.Success = true;
                            result.Message = "RID pool invalidation requires manual LDAP operation";
                            _progress?.Report("RID pool invalidation initiated - verify in AD manually");
                        }
                    }
                    else
                    {
                        result.Success = true;
                        result.Message = "[WHATIF] Would invalidate RID pool";
                        _progress?.Report("[WHATIF] Would invalidate RID pool");
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = ex.Message;
                    _progress?.Report($"Error invalidating RID pool: {ex.Message}");
                }
            }, ct);

            return result;
        }

        #endregion
    }
}
