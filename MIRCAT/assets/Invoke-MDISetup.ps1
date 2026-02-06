begin {
Class AdAl {
    [system.object]$domainDetail
    [system.object]$forestDetail
    [string]$domainDn
    [string]$domainNetbiosName
    [string]$domainFqdn
    [string]$forestDn
    [string]$forestNetbiosName
    [string]$forestFqdn
    [string]$sysvolPath
    [string]$domainSid
    [string]$forestSid
    [system.object]$adForest
    [string]$chosenDc
    $runningSystem = [pscustomobject]@{
        hostname = $null
        isDc = $false
        isDomainJoined = $false
        domain = $null
        sysvolPath = $null
    }
    [system.object]$domainControllerDetail
    [string]$pdc
    [string]$remoteSysvolPath
    [string]$writableSysvolPath
    [string]$sysvolReplicationInfo
    [string]$usersContainer
    [string]$computersContainer
    [bool]$isAdRecycleBinEnabled
    [system.collections.hashtable]$fsmoMap
    [system.collections.hashtable]$dcConnectivityMap
    hidden [system.object]$privilegedGroupNames
    hidden [System.Management.Automation.PSCredential]$credential
    hidden [System.Collections.ArrayList]$gpcArray
    AdAl() {
        $this.credential = [System.Management.Automation.PSCredential]::Empty
        $this.SharedInit()
        $this.AddLogEntry("$env:temp\adalops.log","Running in standard mode. No credentials supplied.","Info")
    }
    AdAl([System.Management.Automation.PSCredential]$credential) {
        $this.credential = $credential
        $this.SharedInit()
        $this.AddLogEntry("$env:temp\adalops.log","Running in credentialed mode. Username provided is $($this.credential.username)","Info")
    }
    [void]SharedInit(){
        $this.privilegedGroupNames = @{}
        $this.BuildGpcMap()
        $wmiCs = Get-CimInstance -Class Win32_ComputerSystem
        if ($wmiCs.Domain -eq 'WORKGROUP') {
            $this.runningSystem.hostname = $wmiCs.Name
            $this.runningSystem.isDomainJoined = $false
        } else {
            $this.runningSystem.hostname = '{0}.{1}' -f $wmiCs.Name,$wmiCs.Domain
            $this.runningSystem.isDomainJoined = $true
            $this.runningSystem.isDC = $this.TestIsDc($null)
            $this.runningSystem.domain = $wmiCs.Domain
        }
    }
    [void]AddLogEntry([string]$logFilePath,[string]$logMessage,[string]$logSev){
        $normalizedLogMessage = $null
        try {
            if (!(test-path $logFilePath)) {
                new-item -itemtype file -path $logFilePath -force | out-null
            }
            try {
                $normalizedLogMessage = [string]::join(" ",($logMessage.Split("`r?`n")))
            } catch {
                $normalizedLogMessage = $logMessage
            }
            "{0}`t{1}`t{2}" -f (get-date).touniversaltime().tostring("dd/MM/yyyy HH:mm:ss"), $logSev, $normalizedLogMessage | add-content -path $logFilePath -force | out-null  
        } catch {
            write-warning "Logging engine failure!"
        }
    }
    [bool]TestIsDc([string]$Server){
        $cimInstanceParams = @{
            Class = "Win32_Operatingsystem"
        }
        if (-not [string]::IsNullOrEmpty($Server)) {
            $cimInstanceParams.Add("ComputerName",$Server)
        }
        $cimInstance = Get-CimInstance @cimInstanceParams
        if ( $cimInstance.ProductType -eq 2 ) {
            $isDc=$true
        } else {
            $isDc=$false
        }
        return $isDc
    }
    [void]AutoDomainDiscovery([string]$domainName) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering AutoDomainDiscovery. Provided domain name is`: $domainName","Info")
        if ([string]::IsNullOrEmpty($domainName)) {
            try {
                $domainCheck = get-wmiobject win32_computersystem
                if (($domainCheck).partofdomain) {
                    $domainName = $domainCheck.Domain
                }
            } catch {}
            if ([string]::IsNullOrEmpty($domainName)) {
                $domainName = $env:USERDNSDOMAIN
            }
            if ([string]::IsNullOrEmpty($domainName)) {
                $this.AddLogEntry("$env:temp\adalops.log","Prompting user for domain name","Info")
                $domainName = read-host -prompt "Please enter FQDN of target domain"
            }
        }
        $this.AddLogEntry("$env:temp\adalops.log","Discovered domain name is`: $domainName","Info")
        # this section is to handle a scenario where there are no credentials provided AND the logged on user is NOT a domain user
        if ($this.credential -eq [System.Management.Automation.PSCredential]::Empty -and [string]::IsNullOrEmpty($env:USERDNSDOMAIN) -and (([Security.Principal.WindowsIdentity]::GetCurrent()).name -ne 'NT AUTHORITY\SYSTEM')) {
            try {
                $this.credential = Get-Credential -Message "Provide DOMAIN or ENTERPRISE ADMIN credentials to connect to $domainName"
                if ($this.credential -eq [System.Management.Automation.PSCredential]::Empty) {
                    write-warning "Credential operation canceled. Credentials are still required and process will not continue!"
                    throw
                }
                try {
                    $pwdTest = ConvertFrom-SecureString $this.credential.password
                    if (!($pwdTest)) {
                        throw
                    }
                } catch {
                    write-warning "Unable to retrieve password from supplied credentials. Process will not continue!"
                    $this.AddLogEntry("$env:temp\adalops.log","Unable to retrieve password from supplied credentials. Process will not continue!","Warn")
                    throw
                }
            } catch {
                throw
            }
        }
        $this.DiscoverByDomainName($domainName)
    }
    [Void]GetWritableSysvolPath() {
        $fName = (get-date).tofiletime().tostring()
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetWritableSysvolPath.","Info")
        $returnVal = $null
        $domainShare = "\\{0}\SYSVOL\{1}" -f $this.chosenDc, $this.domainFqdn
        try {
            if ((Test-Path -Path $this.remoteSysvolPath) -and ($null -eq $returnVal)) {
                try {
                    $null = New-Item -Type File -Path "$($this.remoteSysvolPath)\domain\policies" -Name $fName
                    Start-Sleep -Seconds 1
                    $null = Remove-Item -Path "$($this.remoteSysvolPath)\domain\policies\$fName"
                    $returnVal = "{0}\{1}" -f $this.remoteSysvolPath,"domain"
                } catch {
                    $returnVal = $null
                }
            }

            if ((Test-Path -Path $domainShare) -and ($null -eq $returnVal)) {
                try {
                    $null = New-Item -Type File -Path "$domainShare\policies" -Name $fName
                    Start-Sleep -Seconds 1
                    $null = Remove-Item -Path "$domainShare\policies\$fName"
                    $returnVal = $domainShare
                } catch {
                    $returnVal = $null
                }
            }
            
            if (($this.runningSystem.isDC) -and ($this.domainFqdn -eq $this.runningSystem.domain) -and ($null -eq $returnVal)) {
                if (Test-Path -Path $this.runningSystem.sysvolPath) {
                    try {
                        $null = New-Item -Type File -Path "$($this.runningSystem.sysvolPath)\domain\policies" -Name $fName
                        Start-Sleep -Seconds 1
                        $null = Remove-Item -Path "$($this.runningSystem.sysvolPath)\domain\policies\$fName"
                        $returnVal = "{0}\{1}" -f $this.runningSystem.sysvolPath,"domain"
                    } catch {
                        $returnVal = $null
                    }
                }
            }
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","GetWritableSysvolPath threw an error $($_.Exception)","Error")
            $returnVal = $null
        }
        $this.writableSysvolPath =  $returnVal
        $this.AddLogEntry("$env:temp\adalops.log","Exiting GetWritableSysvolPath. writableSysvolPath was $returnVal","Info")
    }
    [string]GetSysvolPath([string]$Server) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetSysvolPath.","Info")
        $credParams = @{}
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $credParams.Add("Credential",$this.credential)
        }
        $detectedSysvolPath = try { 
                ((Get-ADObject @credParams -Server $Server -Identity "CN=SYSVOL Subscription,CN=Domain System Volume,CN=DFSR-LocalSettings,$($this.domaincontrollerdetail.ComputerObjectDN)" -properties msDFSR-RootPath)."msDFSR-RootPath") | split-path 
            } catch { 
                try { 
                    ((Get-ADObject @credParams -Server $Server -Identity "CN=Domain System Volume (SYSVOL share),CN=NTFRS Subscriptions,$($this.domaincontrollerdetail.ComputerObjectDN)" -properties fRSRootPath)."fRSRootPath") | split-path 
                } catch {
                    $detectedSysvolPath = $null
                }
            }
        if ($detectedSysvolPath -eq $null) {
            try {
               $detectedSysvolPath = invoke-command @credParams -computername $Server {(((get-smbshare -Name sysvol).path) |split-path)} 
            } catch {}
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting GetSysvolPath. DetectedSysvolPath was $detectedSysvolPath","Info")
        return $detectedSysvolPath
    }
    [void]GetSysvolReplicationInfo() {
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetSysvolReplicationInfo.","Info")
        $returnVal = "SYSVOL replication type failed detection!"
        $path = "LDAP://$($this.chosenDc)/OU=Domain Controllers,$($this.domainDn)"
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $SearchRoot = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $SearchRoot = [adsi]("$path")
        }
        $searcher = New-Object DirectoryServices.DirectorySearcher
        $searcher.Filter = "(&(objectClass=computer)(dNSHostName=$($this.chosenDc)))"
        $searcher.SearchRoot = $SearchRoot
        $dcObjectPath = $searcher.FindAll() | %{$_.Path}
        $searcher.dispose()
        # DFSR
        $searchDFSR = New-Object DirectoryServices.DirectorySearcher
        $searchDFSR.Filter = "(&(objectClass=msDFSR-Subscription)(name=SYSVOL Subscription))"
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $SearchRootDc = New-Object System.DirectoryServices.DirectoryEntry($dcObjectPath,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $SearchRootDc = [adsi]("$dcObjectPath")
        }
        $searchDFSR.SearchRoot = $SearchRootDc
        $dfsrSubObject = $searchDFSR.FindAll()

        if ($dfsrSubObject -ne $null){
            $returnVal = "SYSVOL is DFSR (unknown migration state)"
            try {
                $c = 'cmd /c "dfsrmig.exe /getglobalstate"'
                $command  = "Invoke-command -scriptblock {$c} -ComputerName $($this.chosenDc)"+' 2> $null'
                $dfsState = iex $command
                $eliminatedCheck = $dfsState.split(':')[1].trim().replace("'","") -like 'Eliminated'
                if ($eliminatedCheck) {
                    $returnVal = "SYSVOL is DFSR and Eliminated"
                } else {
                    $returnVal = "SYSVOL is DFSR but not Eliminated"
                }
            } catch {}        
        }

        # FRS
        $searchFRS = New-Object DirectoryServices.DirectorySearcher
        $searchFRS.Filter = "(&(objectClass=nTFRSSubscriber)(name=Domain System Volume (SYSVOL share)))"
        $searchFRS.SearchRoot = $SearchRoot
        $frsSubObject = $searchFRS.FindAll()
        $searchFRS.dispose()
        if($frsSubObject -ne $null){
            $returnVal = "SYSVOL is FRS and should be migrated to DFSR"
        }
        $this.sysvolReplicationInfo = $returnVal
        $this.AddLogEntry("$env:temp\adalops.log","Exiting GetSysvolReplicationInfo. returnVal is $returnVal","Info")
    }
    [void]GetPrivGroupNameMaps() {
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetPrivGroupNameMaps.","Info")
        $this.privilegedGroupNames = @{}
        $this.privilegedGroupNames.Add("DNSAdmins","DNSAdmins")
        try {
            $this.privilegedGroupNames.Add("Domain Admins",$this.GetObjectBySid("$($this.domainsid)-512","domain").properties.samaccountname)
        } catch {}
        try {
            $this.privilegedGroupNames.Add("Enterprise Admins",$this.GetObjectBySid("$($this.forestsid)-519","forest").properties.samaccountname)
        } catch {}
        try {
            $this.privilegedGroupNames.Add("Schema Admins",$this.GetObjectBySid("$($this.forestsid)-518","forest").properties.samaccountname)
        } catch {}
        try {
            $this.privilegedGroupNames.Add("Administrators",$this.GetObjectBySid("S-1-5-32-544","domain").properties.samaccountname)
        } catch {}
        try {
            $this.privilegedGroupNames.Add("Account Operators",$this.GetObjectBySid("S-1-5-32-548","domain").properties.samaccountname)
        } catch {}
        try {
            $this.privilegedGroupNames.Add("Backup Operators",$this.GetObjectBySid("S-1-5-32-551","domain").properties.samaccountname)
        } catch {}
        try {
            $this.privilegedGroupNames.Add("Server Operators",$this.GetObjectBySid("S-1-5-32-549","domain").properties.samaccountname)
        } catch {}
        try {
            $this.privilegedGroupNames.Add("Print Operators",$this.GetObjectBySid("S-1-5-32-550","domain").properties.samaccountname)
        } catch {}
        try {
            $this.privilegedGroupNames.Add("Group Policy Creator Owners",$this.GetObjectBySid("$($this.domainsid)-520","domain").properties.samaccountname)
        } catch {} 
        try {
            $this.privilegedGroupNames.Add("Domain Controllers",$this.GetObjectBySid("$($this.domainsid)-516","domain").properties.samaccountname)
        } catch {}
        try {
            $this.privilegedGroupNames.Add("Domain Guests",$this.GetObjectBySid("$($this.domainsid)-514","domain").properties.samaccountname)
        } catch {}
        $this.AddLogEntry("$env:temp\adalops.log","Exiting GetPrivGroupNameMaps.","Info")
    }
    [void]DiscoverByDcName([string]$domainControllerName) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering DiscoverByDcName. DC name is`: $domainControllerName","Info")
        $wellKnownObjects = $null
        $credParams = @{}
        try {
            if ($this.TestDcConnection($domainControllerName)) {
                $this.chosenDc = $domainControllerName
                
                if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                    $credParams.Add("Credential",$this.credential)
                }
                $this.domainDetail = Get-ADDomain -Server $this.chosenDc @credParams
                $this.forestDetail = Get-ADDomain -server $this.domainDetail.forest @credParams
                $this.adForest = get-adforest -server ($this.forestDetail).DnsRoot @credParams
                $this.domainControllerDetail = (Get-ADDomainController -server $this.chosenDc @credParams)
                $wellKnownObjects = (get-adobject @credParams -Server $this.chosenDc -filter 'ObjectClass -eq "domain"' -Properties wellKnownObjects).wellKnownObjects
                $this.sysvolPath = $this.GetSysvolPath($this.chosenDc)
                $this.isAdRecycleBinEnabled = if ((Get-ADOptionalFeature @credParams -Filter 'name -like "Recycle Bin Feature"' -server $this.chosenDc).EnabledScopes) {$true} else {$false}
                $this.domainDn = $this.domainDetail.DistinguishedName
                $this.forestDn = $this.forestDetail.DistinguishedName
                $this.domainNetbiosName = $this.domainDetail.NetBIOSName
                $this.domainFqdn = $this.domainDetail.DnsRoot
                $this.domainSid = $this.domainDetail.DomainSid.value
                $this.forestNetbiosName = ($this.forestDetail).netbiosname
                $this.forestFqdn = ($this.forestDetail).DnsRoot
                $this.forestSid = $this.forestDetail.DomainSid.value
                $this.pdc = $this.domainDetail.PDCEmulator
                $this.remoteSysvolPath = "\\$($this.chosenDc)\$($this.sysvolPath.Replace(':','$'))"
                $this.GetPrivGroupNameMaps()
                $wellKnownObjects | ForEach-Object {
                    if ($_ -match '^B:32:A9D1CA15768811D1ADED00C04FD8D5CD:(.*)$')
                    {
                        $this.usersContainer = $matches[1]
                    }
                    if ($_ -match '^B:32:AA312825768811D1ADED00C04FD8D5CD:(.*)$')
                    {
                        $this.computersContainer = $matches[1]
                    }
                }
                try {
                    $this.GetSysvolReplicationInfo()
                } catch {}
                try {
                    $this.fsmoMap = @{}
                    $this.fsmoMap.Add("SchemaMaster",$this.adForest.SchemaMaster)
                    $this.fsmoMap.Add("DomainNamingMaster",$this.adForest.DomainNamingMaster)
                    $this.fsmoMap.Add("PDCEmulator",$this.domainDetail.PDCEmulator)
                    $this.fsmoMap.Add("RIDMaster",$this.domainDetail.RIDMaster)
                    $this.fsmoMap.Add("InfrastructureMaster",$this.domainDetail.InfrastructureMaster)
                } catch {}
            } else {
                throw
            }
            if ($this.runningSystem.isDC) {
                if ($this.TestDcConnection($this.runningSystem.hostname)) {
                    $this.runningSystem.sysvolPath = $this.GetSysvolPath($this.runningSystem.hostname)
                }
            }
            $this.GetWritableSysvolPath()
        } catch {
            write-error "Specified Domain Controller $domainControllerName is non-responsive."
            $this.AddLogEntry("$env:temp\adalops.log","Specified Domain Controller $domainControllerName is non-responsive.","Error")
            throw
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting DiscoverByDcName.","Info")
    }
    [void]NewDcConnectivityMap() {
        $this.AddLogEntry("$env:temp\adalops.log","Entering NewDcConnectivityMap.","Info")
        # init the hash
        $this.dcConnectivityMap = @{}

        # get a list of DCs
        foreach ($dc in $this.domainDetail.ReplicaDirectoryServers) {
            try {
                $pingobj = new-object System.Net.NetworkInformation.Ping
                # ping them, trap the host not found exception
                if (($pingobj.Send($dc,2)).status -eq "Success") {
                    $this.dcConnectivityMap.Add("$dc",$true)
                } else {
                    $this.dcConnectivityMap.Add("$dc",$false)
                }
            } catch {
                $this.dcConnectivityMap.Add("$dc",$false)
            }
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting NewDcConnectivityMap.","Info")
    }
    [void]DiscoverByDomainName([string]$domainName) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering DiscoverByDomainName. Domain name is`: $domainName","Info")
        $this.chosenDc = $this.GetReachableDc($domainName)
        if (-not [string]::IsNullOrEmpty($this.chosenDc)) {
            $this.DiscoverByDcName($this.chosenDc)
        } else {
            $this.AddLogEntry("$env:temp\adalops.log","Unable to find reachable DC in domain $domainName.","Warn")
        }
        
        $this.AddLogEntry("$env:temp\adalops.log","Exiting DiscoverByDomainName.","Info")
    }
    [bool]TestDcConnection([string]$Server) {
        $returnVal = $false
        try {
            $socket = New-Object -TypeName System.Net.Sockets.TcpClient
            $socket.SendTimeout = 3000
            $socket.ReceiveTimeout = 3000
            $socket.Connect($Server, 9389)
            if ($socket.Connected) {
                $returnVal = $true
            }
            $socket.Close()
        } catch {
            $returnVal = $false
        }
        return $returnVal
    }
    [string]GetReachableDc([string]$domainName) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetReachableDc. Domain name is`: $domainName","Info")
        $dcCandidate = $null
        $dcCandidates = $null
        $successPdc = $false
        $successDC = $false
        #where are we running this from
        try {
            $runningHost = [System.Net.DNS]::GetHostByName($Null).HostName
        } catch {
            $runningHost = $null
        }
        #build invoke command splat params
        $credParams = @{} 
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) { $credParams.Add("credential",$this.credential) }
        $getAdForestParams = @{
            server = $domainName
        } 
        # check for PDC in the domain first before we loop through the rest
        try {
            $lPdc = (Get-ADDomainController @credParams -DomainName $domainName -Service PrimaryDC,ADWS -Discover -ForceDiscover -ErrorAction SilentlyContinue).HostName
            if ([string]::IsNullOrEmpty($lPdc)) {
                $lPdc = ((Resolve-DnsName -name "_ldap._tcp.pdc._msdcs.$domainName" -QuickTimeout -ea silentlycontinue -type srv) | ? { $_.QueryType -eq 'SRV'}).NameTarget
            }
        } catch {
            $lPdc = $null
        }
        if (!($lPdc)) {
            try {
                $fsmo = Invoke-Expression "netdom query fsmo /domain $domainName"
                $x=((($fsmo | ? { $_ -like "PDC*" }).trim()) -match "\s(.*)")
                $lPdc = $Matches[0].trim()
            } catch {
                $lPdc = $null
            }
        }
        if (-not [string]::IsNullOrEmpty($lPdc)) {
            if ($this.TestDcConnection($lPdc)) {
                $dcCandidate = $lPdc
                $successDC = $true
                $successPdc = $true
            } else {
                $this.AddLogEntry("$env:temp\adalops.log","GetReachableDc PDC AD Web Services connection test failure $($_.message).","Error")
            }
        } else {
            $this.AddLogEntry("$env:temp\adalops.log","GetReachableDc PDC detection failure $($_.message).","Error")
        }
        if ((!$successPdc) -and (!($successDC))) {
            $possibleDC = (Get-ADDomainController @credParams -DomainName $domainName -Service adws -Discover -ForceDiscover -ErrorAction SilentlyContinue).HostName
            if ($this.TestDcConnection($possibleDC)) {
                $dcCandidate = $possibleDC
                $successDC = $true
            } else {
                $this.AddLogEntry("$env:temp\adalops.log","GetReachableDc $possibleDC AD Web Services connection test failure $($_.message).","Error")
            }
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting GetReachableDc. dcCandidate name is`: $dcCandidate","Info")
        return $dcCandidate
    }
    [system.object]GetGpo([string]$gpoName,[string]$gpoGuid) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetGpo. GpoName name is`: $gpoName. GpoGuid is`: $gpoGuid","Info")
        $returnGpo = $null
        try {
            $path = "LDAP://$($this.chosenDc)/CN=Policies,CN=System,$($this.domainDn)"
            if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                $SearchRoot = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
            } else {
                $SearchRoot = [adsi]("$path")
            }
            $search = new-object System.DirectoryServices.DirectorySearcher($SearchRoot)
            if ($gpoName) {
                $search.filter = "(&(objectclass=groupPolicyContainer)(displayName=$gpoName))"
            } elseif ($gpoGuid) {
                $search.filter = "(&(objectclass=groupPolicyContainer)(name={$gpoGuid}))"
            }
            $returnGpo = $search.FindOne()
            $search.dispose()
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","GetGpo threw an error $($_.Exception)","Error")
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting GetGpo. ReturnGpo is`: $($returnGpo.Path)","Info")
        return $returnGpo
    }
    [string]NewGpo([string]$gpoName,[int]$flags) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering NewGpo. GpoName name is`: $gpoName","Info")
        $gpo = $null
        $returnVal = $null
        $guidCheck = $null
        # since we need to run code on the DC anyway we're going to simplify this
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            try {
                Set-Item WSMan:localhost\client\trustedhosts -value $this.chosenDc -Force
                $sb = {
                    param($pGpoName,$pCurrentDc,$pDomainFqdn)
                    new-gpo -name "$pGpoName" -server $pCurrentDc -domain $pDomainFqdn
                }
                $gpo = Invoke-Command -computerName $($this.chosenDc) -credential $($this.credential) -scriptBlock $sb -ArgumentList $gpoName,$($this.chosenDc),$($this.domainFqdn)
                get-Item WSMan:localhost\client\trustedhosts | ? { $_.value -eq $($this.chosenDc)} | clear-item -force
                start-sleep 1
                try {
                    $guidCheck = $this.GetGpo($gpoName,$null).properties["cn"]
                    $guidCheck = $guidCheck.Trim("{").Trim("}")
                } catch {
                    $this.AddLogEntry("$env:temp\adalops.log","NewGpo threw an error in guid check in credentialed block $($_.Exception)","Error")
                }
                if (!($guidCheck)) {
                    throw
                }
                $returnVal = $guidCheck
            } catch {
                $returnVal = $null
                $this.AddLogEntry("$env:temp\adalops.log","NewGpo threw an error in credentialed block $($_.Exception)","Error")
            }
        } else {
            try {
                $gpo = new-gpo -name "$gpoName" -server $($this.chosenDc) -domain $($this.domainFqdn)
                # get it to confirm
                start-sleep 1
                try {
                    $guidCheck = $this.GetGpo($gpoName,$null).properties["cn"]
                    $guidCheck = $guidCheck.Trim("{").Trim("}")
                } catch {
                    $this.AddLogEntry("$env:temp\adalops.log","NewGpo threw an error in guid check in non-credentialed block $($_.Exception)","Error")
                }
                if (!($guidCheck)) {
                    throw
                }
                $returnVal = $guidCheck
            } catch {
                $returnVal = $null
                $this.AddLogEntry("$env:temp\adalops.log","NewGpo threw an error in non-credentialed block $($_.Exception)","Error")
            }
        }
        # set flags
        try {
            $fullGuid = [string]"{"+$guidCheck+"}" 
            $path = "LDAP://$($this.chosenDc)/CN=$fullGuid,CN=Policies,CN=System,$($this.domainDn)"
            if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                $pol = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
            } else {
                $pol = [adsi]("$path")
            }
            $pol.put("flags",$flags)
            $pol.setinfo()
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","NewGpo threw an error in flags block $($_.Exception)","Error")
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting NewGpo. GpoGuid for returnVal is`: $returnVal","Info")
        return $returnVal
    }
    [bool]RemoveGpo([string]$gpoName,[string]$gpoGuid) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering RemoveGpo. GpoName name is`: $gpoName. GpoGuid is`: $gpoGuid","Info")
        $returnVal = $false
        try {
            $gpoToRemove = $this.GetGpo($gpoName,$gpoGuid)
            $gpoDirEnt = [adsi]"$($gpoToRemove.getdirectoryentry().path)"
            $gpoDirEnt.deletetree()
            start-sleep -Milliseconds 500
            Remove-Item ("{0}\Policies\{1}" -f $this.writableSysvolPath,$(($gpoDirEnt.cn).split('='))) -Recurse -Force
            $returnVal = $true
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","RemoveGpo threw an error $($_.Exception)","Error")
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting RemoveGpo. ReturnVal is`: $returnVal","Info")
        return $returnVal
    }
    [string]NewWmiFilter([string]$wmiFilterName,[string]$wmiFilterDescription,[string]$wmiFilterNamespace,[string]$wmiFilterQuery) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering NewWmiFilter. WmiFilterName is`: $wmiFilterName, WmiFilterDescription is`: $wmiFilterDescription, WmiFilterNamespace is`: $wmiFilterNamespace, WmiFilterQuery is`: $wmiFilterQuery","Info")
        $returnVal=""
        $msWMIAuthor = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $guid=([System.Guid]::NewGuid()).guid
        $WMIGUID = [string]"{"+$guid+"}"    
        $WMIDN = "CN="+$WMIGUID+",CN=SOM,CN=WMIPolicy,CN=System,"+$($this.domainDn) 
        $WMICN = $WMIGUID 
        $WMIdistinguishedname = $WMIDN 
        $WMIID = $WMIGUID 
        $now = (Get-Date).ToUniversalTime() 
        $msWMICreationDate = ($now.Year).ToString("0000") + ($now.Month).ToString("00") + ($now.Day).ToString("00") + ($now.Hour).ToString("00") + ($now.Minute).ToString("00") + ($now.Second).ToString("00") + "." + ($now.Millisecond * 1000).ToString("000000") + "-000" 
        $msWMIName = $wmiFilterName 
        $msWMIParm1 = $wmiFilterDescription + " " 
        $msWMIParm2 = "1;3;" + $wmiFilterNamespace.Length.ToString() + ";" + $wmiFilterQuery.Length.ToString() + ";WQL;" + $wmiFilterNamespace + ";" + $wmiFilterQuery + ";"
        $array = @()
        $path = "LDAP://$($this.chosenDc)/CN=SOM,CN=WMIPolicy,CN=System,$($this.domainDn)"
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $SearchRoot = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $SearchRoot = [adsi]("$path")
        }
        $search = new-object System.DirectoryServices.DirectorySearcher($SearchRoot)
        $search.filter = "(objectclass=msWMI-Som)"
        $results = $search.FindAll()
        $search.dispose()
        ForEach ($result in $results) {
            $array += $result.properties["mswmi-name"].item(0)
        }
        if ($array -notcontains $msWMIName) {
            write-verbose "Creating the $msWMIName WMI Filter"
            if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                $path = "LDAP://$($this.chosenDc)/CN=SOM,CN=WMIPolicy,CN=System,$($this.domainDn)"
                $SOMContainer = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password))
            } else {
                $SOMContainer = [adsi]("$path")
            }
            $NewWMIFilter = $SOMContainer.create('msWMI-Som',"CN="+$WMIGUID)
            $NewWMIFilter.put("msWMI-Name",$msWMIName) | out-null
            $NewWMIFilter.put("msWMI-Parm1",$msWMIParm1) | out-null
            $NewWMIFilter.put("msWMI-Parm2",$msWMIParm2) | out-null
            $NewWMIFilter.put("msWMI-Author",$msWMIAuthor) | out-null
            $NewWMIFilter.put("msWMI-ID",$WMIID) | out-null
            $NewWMIFilter.put("instanceType",4) | out-null
            $NewWMIFilter.put("showInAdvancedViewOnly","TRUE") | out-null
            $NewWMIFilter.put("distinguishedname",$WMIdistinguishedname) | out-null
            $NewWMIFilter.put("msWMI-ChangeDate",$msWMICreationDate) | out-null
            $NewWMIFilter.put("msWMI-CreationDate",$msWMICreationDate) | out-null
            try {
                $NewWMIFilter.setinfo() | out-null
                $returnVal=$guid
            } catch {
                $this.AddLogEntry("$env:temp\adalops.log","NewWmiFilter threw an error $($_.Exception)","Error")
            }
            
        } else {
            write-verbose "The $msWMIName WMI Filter already exists"
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting NewWmiFilter. WmiGuid for returnVal is`: $returnVal","Info")
        return $returnVal
    }
    [bool]AddGpoApplyAcl([string]$gpoGuid,[string]$identity,[string]$permissionType,[string]$identityLocation) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering AddGpoApplyAcl. GpoGuid is`: $gpoGuid, Identity is`: $identity, PermissionType is`: $permissionType, IdentityLocation is`: $identityLocation","Info")
        $objtoAcl = $null
        $translatedId = $null
        $netBios = $null
        $adsiGpo = $null
        switch ($identityLocation.ToUpper()) {
            "FOREST" {$netBios = $this.forestNetbiosName}
            "DOMAIN" {$netBios = $this.domainNetbiosName}
            default {throw "Identity location must be one of FOREST or DOMAIN"}
        }
        $returnVal = $false
        $path = "LDAP://$($this.chosenDc)/CN=`{$($gpoGuid)`},CN=Policies,CN=System,$($this.domainDn)"
        $objtoAcl = [Security.principal.NTAccount]::new($netBios,$identity)
        try {
            $translatedId = $objtoAcl.translate([system.security.principal.securityidentifier])
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","AddGpoApplyAcl failed to translate identity using netbios $netBios, identity $identity. $($_.Exception)","Error")
            $returnVal = $false
        }
        if ($null -ne $translatedId) {
            if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                try {
                    Set-Item WSMan:localhost\client\trustedhosts -value $this.chosenDc -Force
                    $sb = {
                        param($pCurrentDc,$pPath,$pIdent,$pPermType)
                        $adsiGpo = [ADSI]$pPath
                        $rule = New-Object System.DirectoryServices.ActiveDirectoryAccessRule($pIdent,"ExtendedRight",$pPermType,[Guid]"edacfd8f-ffb3-11d1-b41d-00a0c968f939")
                        $adsiGpo.PSBase.ObjectSecurity.AddAccessRule($rule) | out-null
                        $adsiGpo.CommitChanges() | out-null
                    }
                    Invoke-Command -computerName $($this.chosenDc) -credential $($this.credential) -scriptBlock $sb -ArgumentList $($this.chosenDc),$path,$translatedId,$permissionType
                    get-Item WSMan:localhost\client\trustedhosts | ? { $_.value -eq $($this.chosenDc)} | clear-item -force
                    $returnVal = $true
                } catch {
                    $returnVal = $false
                    $this.AddLogEntry("$env:temp\adalops.log","AddGpoApplyAcl threw an error in credentialed block contacting DC $($this.chosenDc). $($_.Exception)","Error")
                }
            } else {
                $adsiGpo = [ADSI]"$path"
                if ($adsiGpo) {
                    try {
                        $rule = New-Object System.DirectoryServices.ActiveDirectoryAccessRule($translatedId,"ExtendedRight",$permissionType,[Guid]"edacfd8f-ffb3-11d1-b41d-00a0c968f939")
                        $acl = $adsiGpo.ObjectSecurity
                        $acl.AddAccessRule($rule) | out-null
                        $adsiGpo.CommitChanges() | out-null
                        $returnVal = $true
                    }
                    catch {
                        $this.AddLogEntry("$env:temp\adalops.log","AddGpoApplyAcl failed to commit changes. $($_.Exception)","Error")
                        $returnVal = $false
                    }
                    
                } else {
                    $this.AddLogEntry("$env:temp\adalops.log","AddGpoApplyAcl reports GPO not found. $($_.Exception)""Error")
                    $returnVal=$false
                }
            }
        }
        
        $this.AddLogEntry("$env:temp\adalops.log","Exiting AddGpoApplyAcl. returnVal is`: $returnVal","Info")
        return $returnVal
    }
    [system.object]GetWmiFilter([string]$wmiFilterName,[string]$identityLocation){
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetWmiFilter. WmiFilterName is`: $wmiFilterName, IdentityLocation is`: $identityLocation","Info")
        $returnVal = $null
        $results = $null
        $path = $null
        switch ($identityLocation.ToUpper()) {
            "FOREST" {$path = "LDAP://$($this.forestDetail.PDCEmulator)/CN=SOM,CN=WMIPolicy,CN=System,$($this.forestDn)"}
            "DOMAIN" {$path = "LDAP://$($this.chosenDc)/CN=SOM,CN=WMIPolicy,CN=System,$($this.domainDn)"}
            default {throw "Identity location must be one of FOREST or DOMAIN"}
        }
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $SearchRoot = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $SearchRoot = [adsi]("$path")
        }
        $search = new-object System.DirectoryServices.DirectorySearcher($SearchRoot)
        $search.ReferralChasing = [System.DirectoryServices.ReferralChasingOption]::None
        $search.filter = "(&(objectclass=msWMI-Som)(msWMI-Name=$wmiFilterName))"
        try {
            $results = $search.FindOne()
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","GetWmiFilter threw an error $($_.Exception)","Error")
        }
        $search.dispose()
        if ($results) {
            $returnVal = [PSCustomObject]@{ 
                name=$results.properties["mswmi-name"].item(0)
                id=$results.properties["mswmi-id"].item(0)
            }
        }
        try {
            $this.AddLogEntry("$env:temp\adalops.log","Exiting GetWmiFilter. Name is`: $($returnVal.name). ID is`: $($returnVal.id)","Info")
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","Exiting GetWmiFilter. returnVal is NULL","Info")
        }
        return $returnVal
    }
    [bool]SetWmiFilter([string]$gpoGuid,[string]$wmiFilterGuid) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering SetWmiFilter. GpoGuid is`: $gpoGuid. WmiFilterGuid is`: $wmiFilterGuid","Info")
        $returnVal = $false
        #determine the value
        # [domainfqdn;{wmifilterguid};0]
        $gPCWQLFilterValue = "["+$($this.domainFqdn)+";{"+$wmiFilterGuid+"};0]"
        # find the GPO by guid
        $gppath = "CN={"+$gpoGuid+"},CN=Policies,CN=System,"+$($this.domainDn)
        try {
            if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                Set-ADObject -Identity $gppath -Replace @{gPCWQLFilter=$gPCWQLFilterValue} -server $($this.chosenDc) -credential $this.credential | out-null
            } else {
                Set-ADObject -Identity $gppath -Replace @{gPCWQLFilter=$gPCWQLFilterValue} -server $($this.chosenDc) | out-null
            }
            $returnVal = $true
        }
        catch {
            $this.AddLogEntry("$env:temp\adalops.log","SetWmiFilter threw an error $($_.Exception)","Error")
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting SetWmiFilter. returnVal is $returnVal","Info")
        return $returnVal
    }
    [bool]SetGpoGptVersion([string]$gpoGuid) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering SetGpoGptVersion. GpoGuid is`: $gpoGuid.","Info")
        $gptSet = $false
        $gptPath = $null
        $stream = $null
        $map = $null
        $gppath = "CN={"+$gpoGuid+"},CN=Policies,CN=System,$($this.domainDn)"
        # set the version
        # get existing version first
        try {
            if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                $gpVersionNumber = (Get-ADObject -Identity $gppath -Properties * -server $this.chosenDc -credential $this.credential -erroraction silentlycontinue).versionNumber
            } else {
                $gpVersionNumber = (Get-ADObject -Identity $gppath -Properties * -server $this.chosenDc -erroraction silentlycontinue).versionNumber
            }
            
            $gpVersionNumber += 1
        }
        catch {
            $gpVersionNumber = 1
        }
        try {
            if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                Set-ADObject -Identity $gppath -Replace @{versionNumber=$gpVersionNumber} -server $this.chosenDc -credential $this.credential | out-null
            } else {
                Set-ADObject -Identity $gppath -Replace @{versionNumber=$gpVersionNumber} -server $this.chosenDc | out-null
            }
            try
            {
                $gptPath = $($this.writableSysvolPath)+'\Policies\{'+$gpoGuid+'}\GPT.ini'
                if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                    #figure out the root path and do new-psdrive here
                    $rootPath = "{0}\{1}" -f $this.chosenDc,"SYSVOL"
                    $map = New-PSDrive -Name "PSDrive" -PSProvider "FileSystem" -Root $rootPath -Credential $this.credential
                }
                if (!(Test-Path $gptPath)) {
                    start-sleep 5
                }
                $gptContent = get-content $gptPath
                $existingVersion = ($gptContent | ? { $_ -match "Version\="}).split('=')[1]
                $matchString = ($gptContent | ? { $_ -match "Version\="})
                $gptContent = $gptContent.replace($matchString,"Version=$gpVersionNumber")
                $stream = [System.IO.StreamWriter]::new( $gptPath )
                $gptContent | ForEach-Object{ $stream.WriteLine( $_ ) }
                $gptSet = $true
            }
            catch {
                $this.AddLogEntry("$env:temp\adalops.log","SetGpoGptVersion failed to set GPT version of $gptPath $($_.Exception)","Error")
                $gptSet = $false
            }
            finally
            {
                if ($stream) {
                    $stream.close()
                }
                if ($map) {
                    Remove-psdrive $map
                }
            }
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","SetGpoGptVersion failed to get GP version for $gppath $($_.Exception)","Error")
            $gptSet = $false
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting SetGpoGptVersion. gptSet is`: $gptSet.","Info")
        return $gptSet
    }
    [bool]SetGpoGpcExtension([string]$gpoGuid,[string[]]$features){
        $this.AddLogEntry("$env:temp\adalops.log","Entering SetGpoGpcExtension. GpoGuid is`: $gpoGuid, features is`: $($features -join ',')","Info")
        $gpcPrefixNeeded = $false
        $gpcSet = $false
        # find the GPO by guid
        $gppath = "CN={"+$gpoGuid+"},CN=Policies,CN=System,$($this.domainDn)"
        # build the map
        $stringBuilderGpcPreferencePrefix = [System.Text.StringBuilder]::new()
        $stringBuilderGpcPreferencePrefix.Append('{00000000-0000-0000-0000-000000000000}')
        $stringBuilderGpc = [System.Text.StringBuilder]::new()
        foreach ($feature in $features) {
            $cse = ($this.gpcArray | ? {$_.name -eq $feature}).cse
            $tool = ($this.gpcArray | ? {$_.name -eq $feature}).tool
            [void]$stringBuilderGpc.Append('[{0}]' -f $($cse, $tool -join ''))
            if (($this.gpcArray | ? {$_.name -eq $feature}).'class' -eq 'gpp') {
                $gpcPrefixNeeded = $true
                $stringBuilderGpcPreferencePrefix.Append($tool)
            }
            $cse = $null
            $tool = $null
        }
        if ($gpcPrefixNeeded) {
            $gpcValue = $('[{0}]{1}' -f $stringBuilderGpcPreferencePrefix.ToString(), $stringBuilderGpc.tostring())
        } else {
            $gpcValue = $stringBuilderGpc.tostring()
        }
        # set the attribute
        try {
            $replace = @{gPCMachineExtensionNames=$gpcValue}
            $setAdObjectParams = @{
                Server = $this.chosenDc
                Identity = $gppath
                Replace = $replace
            }
            if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                $setAdObjectParams.Add("credential", $this.credential)
            }
            Set-ADObject @setAdObjectParams | out-null
            $gpcSet = $true
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","SetGpoGpcExtension failed to set gPCMachineExtentsionNames $gpcValue for $gppath $($_.Exception)","Error")
            $gpcSet = $false
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting SetGpoGpcExtension. gpcSet is`: $gpcSet.","Info")
        return $gpcSet
    }
    [bool]SetGpoGpcExtensionRaw([string]$gpoGuid,[string]$gpcValue){
        $this.AddLogEntry("$env:temp\adalops.log","Entering SetGpoGpcExtensionRaw. GpoGuid is`: $gpoGuid, GpcValue is is`: $gpcValue","Info")
        $gpcSet = $false
        # find the GPO by guid
        $gppath = "CN={"+$gpoGuid+"},CN=Policies,CN=System,$($this.domainDn)"
        # set the attribute
        try {
            if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                Set-ADObject -Identity $gppath -Replace @{gPCMachineExtensionNames=$gpcValue} -server $this.chosenDc -credential $this.credential | out-null
            } else {
                Set-ADObject -Identity $gppath -Replace @{gPCMachineExtensionNames=$gpcValue} -server $this.chosenDc | out-null
            }
            
            $gpcSet = $true
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","SetGpoGpcExtensionRaw failed to set gPCMachineExtentsionNames for $gppath $($_.Exception)","Error")
            $gpcSet = $false
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting SetGpoGpcExtensionRaw. gpcSet is`: $gpcSet.","Info")
        return $gpcSet
    }
    [bool]SetGpoGpcExtensionOld([string]$gpoGuid,[bool]$containsAdvAuditPol,[bool]$containsPrefs) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering SetGpoGpcExtension. GpoGuid is`: $gpoGuid, ContainsAdvAuditPol is`: $containsAdvAuditPol, ContainsPrefs is`: $containsPrefs","Info")
        $gpcSet = $false
        #determine the value
        $gpcValue = ""
        if (!($containsAdvAuditPol -and $containsPrefs)) {
            $gpcValue = "[{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}]"
        }
        if ($containsAdvAuditPol -and (!($containsPrefs))) {
            $gpcValue = "[{2A8FDC61-2347-4C87-92F6-B05EB91A201A}{D02B1F72-3407-48AE-BA88-E8213C6761F1}][{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{0F6B957D-509E-11D1-A7CC-0000F87571E3}{62C1845D-C4A6-4ACB-BBB0-C895FD090385}{B05566AC-FE9C-4368-BE01-7A4CBB6CBA11}{D02B1F72-3407-48AE-BA88-E8213C6761F1}][{7933F41E-56F8-41D6-A31C-4148A711EE93}{D02B1F72-3407-48AE-BA88-E8213C6761F1}][{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}][{D76B9641-3288-4F75-942D-087DE603E3EA}{D02B1F72-3407-48AE-BA88-E8213C6761F1}][{F312195E-3D9D-447A-A3F5-08DFFA24735E}{D02B1F72-3407-48AE-BA88-E8213C6761F1}][{F3CCC681-B74C-4060-9F26-CD84525DCA2A}{0F3F3735-573D-9804-99E4-AB2A69BA5FD4}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}]"
        }
        if ($containsPrefs -and (!($containsAdvAuditPol))) {
            $gpcValue = "[{00000000-0000-0000-0000-000000000000}{BEE07A6A-EC9F-4659-B8C9-0B1937907C83}][{B087BE9D-ED37-454F-AF9C-04291E351182}{BEE07A6A-EC9F-4659-B8C9-0B1937907C83}]"
        }
        if ($containsAdvAuditPol -and $containsPrefs) {
            $gpcValue = "[{00000000-0000-0000-0000-000000000000}{BEE07A6A-EC9F-4659-B8C9-0B1937907C83}][{827D319E-6EAC-11D2-A4EA-00C04F79F83A}{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}][{B087BE9D-ED37-454F-AF9C-04291E351182}{BEE07A6A-EC9F-4659-B8C9-0B1937907C83}][{F3CCC681-B74C-4060-9F26-CD84525DCA2A}{0F3F3735-573D-9804-99E4-AB2A69BA5FD4}]"
        }

        # find the GPO by guid
        $gppath = "CN={"+$gpoGuid+"},CN=Policies,CN=System,$($this.domainDn)" #(Get-ADRootDSE).defaultnamingcontext

        # set the attribute
        try {
            if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                Set-ADObject -Identity $gppath -Replace @{gPCMachineExtensionNames=$gpcValue} -server $this.chosenDc -credential $this.credential | out-null
            } else {
                Set-ADObject -Identity $gppath -Replace @{gPCMachineExtensionNames=$gpcValue} -server $this.chosenDc | out-null
            }
            
            $gpcSet = $true
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","SetGpoGpcExtension failed to set gPCMachineExtentsionNames for $gppath $($_.Exception)","Error")
            $gpcSet = $false
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting SetGpoGpcExtension. gpcSet is`: $gpcSet.","Info")
        return $gpcSet
    }
    [bool]SetGpoContent([string]$filePath,[string]$gpoContent,[string]$encoding){
        $this.AddLogEntry("$env:temp\adalops.log","Entering SetGpoContent. FilePath is`: $filePath, GpoContent is not displayed, Encoding is`: $encoding","Info")
        # you need filepath here because each gpo can have multiple files under it
        # just be precise
        $returnVal = $false
        $encodingObj = $null
        $map = $null
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            #figure out the root path and do new-psdrive here
            $rootPath = "{0}\{1}" -f $this.chosenDc,"SYSVOL"
            $map = New-PSDrive -Name "PSDrive" -PSProvider "FileSystem" -Root $rootPath -Credential $this.credential
        }
        switch ($encoding.tostring().toupper()) {
            "ASCII" {$encodingObj = New-Object System.Text.ASCIIEncoding}
            "UTF8" {$encodingObj = New-Object System.Text.UTF8Encoding $False}
            "UNICODE" {$encodingObj = New-Object System.Text.UnicodeEncoding}
            default {$encodingObj = [System.Text.Encoding]::Default}

        }
        try {
            $newItem = New-Item -path $filePath -ItemType "file" -Force
            start-sleep 1
            if (!(Test-Path $filePath)) {
                start-sleep 5
            }
            [System.IO.File]::WriteAllLines($filePath, $gpoContent, $encodingObj)
            $returnVal = $true
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","SetGpoContent threw an error $($_.Exception)","Error")
            $returnVal = $false
        } finally {
            if ($map) {
                Remove-psdrive $map
            }
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting SetGpoContent. returnVal is`: $returnVal.","Info")
        return $returnVal
    }
    [system.object]GetObjectByDn([string]$dn,[string]$identityLocation) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetObjectByDn. DN is`: $dn, IdentityLocation is`: $identityLocation","Info")
        $results = $null
        $path = $null
        switch ($identityLocation.ToUpper()) {
            "FOREST" {$path = "LDAP://$($this.forestDetail.PDCEmulator)/$($this.forestDn)"}
            "DOMAIN" {$path = "LDAP://$($this.chosenDc)/$($this.domainDn)"}
            default {throw "Identity location must be one of FOREST or DOMAIN"}
        }
        
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $SearchRoot = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $SearchRoot = [adsi]("$path")
        }
        $search = new-object System.DirectoryServices.DirectorySearcher($SearchRoot)
        $search.ReferralChasing = [System.DirectoryServices.ReferralChasingOption]::None
        $search.filter = "(distinguishedName=$dn)"
        try {
            $results = $search.FindOne()
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","GetObjectByDn threw an error $($_.Exception)","Error")
        }
        $search.dispose()
        $this.AddLogEntry("$env:temp\adalops.log","Exiting GetObjectByDn.","Info")
        return $results
    }
    [system.object]GetObjectByName([string]$name,[string]$identityLocation,[bool]$isOu) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetObjectByName. Name is`: $name, IdentityLocation is`: $identityLocation, IsOu is`: $isOu","Info")
        $results = $null
        $path = $null
        $samaccountname = $name
        switch ($identityLocation.ToUpper()) {
            "FOREST" {$path = "LDAP://$($this.forestDetail.PDCEmulator)/$($this.forestDn)"}
            "DOMAIN" {$path = "LDAP://$($this.chosenDc)/$($this.domainDn)"}
            default {throw "Identity location must be one of FOREST or DOMAIN"}
        }
        
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $SearchRoot = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $SearchRoot = [adsi]("$path")
        }
        $search = new-object System.DirectoryServices.DirectorySearcher($SearchRoot)
        $search.ReferralChasing = [System.DirectoryServices.ReferralChasingOption]::None
        if ($isOu) {
            $search.filter = "(ou=$name)"
        } else {
            $search.filter = "(samaccountname=$samaccountname)"
        }
        try {
            $results = $search.FindOne()
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","GetObjectByName threw an error $($_.Exception)","Error")
        }
        $search.dispose()
        $this.AddLogEntry("$env:temp\adalops.log","Exiting GetObjectByName.","Info")
        return $results
    }
    [system.object]GetObjectBySid([string]$sid,[string]$identityLocation) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetObjectBySid. SID is`: $sid, IdentityLocation is`: $identityLocation","Info")
        $path = $null
        $results = $null
        switch ($identityLocation.ToUpper()) {
            "FOREST" {$path = "LDAP://$($this.forestDetail.PDCEmulator)/$($this.forestDn)"}
            "DOMAIN" {$path = "LDAP://$($this.chosenDc)/$($this.domainDn)"}
            default {throw "Identity location must be one of FOREST or DOMAIN"}
        }
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $SearchRoot = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $SearchRoot = [adsi]("$path")
        }
        $search = new-object System.DirectoryServices.DirectorySearcher($SearchRoot)
        $search.ReferralChasing = [System.DirectoryServices.ReferralChasingOption]::None
        $search.filter = "(objectSid=$sid)"
        try {
            $results = $search.FindOne()
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","GetObjectBySid threw an error $($_.Exception)","Error")
        }
        $search.dispose()
        $this.AddLogEntry("$env:temp\adalops.log","Exiting GetObjectBySid.","Info")
        return $results
    }
    [system.object]GetObjectByFilter([string]$filter,[string]$identityLocation) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetObjectByFilter. Filter is`: $filter, IdentityLocation is`: $identityLocation","Info")
        $path = $null
        $results = $null
        switch ($identityLocation.ToUpper()) {
            "FOREST" {$path = "LDAP://$($this.forestDetail.PDCEmulator)/$($this.forestDn)"}
            "DOMAIN" {$path = "LDAP://$($this.chosenDc)/$($this.domainDn)"}
            default {throw "Identity location must be one of FOREST or DOMAIN"}
        }
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $SearchRoot = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $SearchRoot = [adsi]("$path")
        }
        $search = new-object System.DirectoryServices.DirectorySearcher($SearchRoot)
        $search.ReferralChasing = [System.DirectoryServices.ReferralChasingOption]::None
        $search.filter = "($filter)"
        try {
            $results = $search.FindAll()
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","GetObjectByFilter threw an error $($_.Exception)","Error")
        }
        $search.dispose()
        $this.AddLogEntry("$env:temp\adalops.log","Exiting GetObjectByFilter.","Info")
        return $results
    }
    [bool]SetGpInheritance([string]$ou,[int]$gpOptions){
        $this.AddLogEntry("$env:temp\adalops.log","Entering SetGpInheritance. OU is`: $ou, GpOptions is`: $gpOptions","Info")
        $adsiGpo = $null
        $returnVal = $false
        $path = "LDAP://$($this.chosenDc)/$ou"
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $adsiGpo = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $adsiGpo = [ADSI]"$path"
        }
        try {
            $adsiGpo.gPOptions = $gpOptions
            $adsiGpo.commitchanges()
            $returnVal = $true

        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","SetGpInheritance threw an error $($_.Exception)","Error")
            $returnVal = $false
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting SetGpInheritance. returnVal is $returnVal","Info")
        return $returnVal
    }
    [bool]SetOuAccessRule([string]$ou,[System.DirectoryServices.ActiveDirectoryAccessRule]$accessRule){
        $this.AddLogEntry("$env:temp\adalops.log","Entering SetOuAccessRule. OU is`: $ou, not displaying accessRule","Info")
        $adsiOu = $null
        $returnVal = $false
        $modified = $false
        $path = "LDAP://$($this.chosenDc)/$ou"
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $adsiOu = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $adsiOu = [ADSI]"$path"
        }
        try {
            $adsiOu.PSBase.ObjectSecurity.ModifyAccessRule([System.Security.AccessControl.AccessControlModification]::Add,$accessRule,[ref]$modified) | out-null
            $adsiOu.commitchanges()
            $returnVal = $true
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","SetOuAccessRule threw an error $($_.Exception)","Error")
            $returnVal = $false
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting SetOuAccessRule. returnVal is $returnVal, modified is $modified","Info")
        return $returnVal -and $modified
    }
    [system.object]NewAdGroup([string]$parentOu,[string]$name,[string]$description,[string]$groupType){
        $this.AddLogEntry("$env:temp\adalops.log","Entering NewAdGroup. ParentOu is`: $parentOu, Name is`: $name, Description is`: $description, GroupType is`: $groupType","Info")
        $returnVal = $null
        $adsiOu = $null
        $calculatedGroupType = $null
        $path = "LDAP://$($this.chosenDc)/$parentOu"
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $adsiOu = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $adsiOu = [ADSI]"$path"
        }
        $adGroupType = @{
            Global      = 0x00000002
            DomainLocal = 0x00000004
            Universal   = 0x00000008
            Security    = 0x80000000
        }
        switch ($groupType.Tostring().toupper()) {
            "GLOBAL" {
                $calculatedGroupType = ($adGroupType.Global -bor $adGroupType.Security)
            }
            "UNIVERSAL" {
                $calculatedGroupType = ($adGroupType.Universal -bor $adGroupType.Security)
            }
            "DOMAINLOCAL" {
                $calculatedGroupType = ($adGroupType.DomainLocal -bor $adGroupType.Security)
            }
        }
        try {
            $newGroup = $adsiOu.Create('group', "CN=$name")
            $newGroup.put('grouptype',$calculatedGroupType)
            $newGroup.put('samaccountname',$name)
            if ($description) {
                $newGroup.put('description',$description)
            }
            $newGroup.SetInfo()
            $returnVal = $newGroup
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","NewAdGroup threw an error $($_.Exception)","Error")
            $returnVal = $null
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting NewAdGroup. returnVal is $returnVal","Info")
        return $returnVal
    }
    [system.object]NewAdUser([string]$parentOu,[string]$name,[string]$description){
        $this.AddLogEntry("$env:temp\adalops.log","Entering NewAdUser. ParentOu is`: $parentOu, Name is`: $name, Description is`: $description","Info")
        $returnVal = $null
        $adsiOu = $null
        $path = "LDAP://$($this.chosenDc)/$parentOu"
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $adsiOu = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $adsiOu = [ADSI]"$path"
        }
        try {
            $guid = (new-guid).guid.split('-')
            $guid[0]+=[char](get-random -min 65 -max 90)
            $guid[1]+=[char](get-random -min 65 -max 90)
            $password = $guid -join '-'
            $newUser = $adsiOu.Create('user', "CN=$name")
            $newUser.put('samaccountname',$name)
            if ($description) {
                $newUser.put('description',$description)
            }
            $newUser.put('userPrincipalName',"$name@$($this.domainFqdn)")
            $newUser.SetInfo()
            $newUser.SetPassword($password)
            $newUser.AccountDisabled = $false
            $newUser.SetInfo()
            $returnVal = $newUser
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","NewAdUser threw an error $($_.Exception)","Error")
            $returnVal = $null
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting NewAdUser. returnVal is $returnVal","Info")
        return $returnVal
    }
    [system.object]NewAdOu([string]$parentOu,[string]$name,[string]$description){
        $this.AddLogEntry("$env:temp\adalops.log","Entering NewAdOu. ParentOu is`: $parentOu, Name is`: $name, Description is`: $description","Info")
        $returnVal = $null
        $adsiOu = $null
        $path = "LDAP://$($this.chosenDc)/$parentOu"
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $adsiOu = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $adsiOu = [ADSI]"$path"
        }
        try {
            $newOu = $adsiOu.Create('organizationalUnit', "OU=$name")
            if ($description) {
                $newOu.put('description',$description)
            }
            $newOu.SetInfo()
            $returnVal = $newOu
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","NewAdOu threw an error $($_.Exception)","Error")
            $returnVal = $null
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting NewAdOu.","Info")
        return $returnVal
    }
    [system.object]NewAdObject([string]$objClass,[string]$parentOu,[string]$name,[string]$description){
        $this.AddLogEntry("$env:temp\adalops.log","Entering NewAdObject. ObjClass is`: $objClass, ParentOu is`: $parentOu, Name is`: $name, Description is`: $description","Info")
        $returnVal = $null
        $adsiOu = $null
        $path = "LDAP://$($this.chosenDc)/$parentOu"
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $adsiOu = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $adsiOu = [ADSI]"$path"
        }
        switch -regex ($objClass.Tostring().toupper()) {
            "GROUP" {
                if (!($this.GetObjectByName($name,"DOMAIN",$false))) {
                    $adGroupType = @{
                        Global      = 0x00000002
                        DomainLocal = 0x00000004
                        Universal   = 0x00000008
                        Security    = 0x80000000
                    }
                    try {
                        $newGroup = $adsiOu.Create('group', "CN=$name")
                        $newGroup.put('grouptype',($adGroupType.Global -bor $adGroupType.Security))
                        $newGroup.put('samaccountname',$name)
                        if ($description) {
                            $newGroup.put('description',$description)
                        }
                        $newGroup.SetInfo()
                        $returnVal = $newGroup
                    } catch {
                        $this.AddLogEntry("$env:temp\adalops.log","NewAdObject threw an error $($_.Exception)","Error")
                        $returnVal = $null
                    }
                }
                continue
            }
            "(OU|CONTAINER|ORGANIZATIONALUNIT)" {
                try {
                    $newOu = $adsiOu.Create('organizationalUnit', "OU=$name")
                    if ($description) {
                        $newOu.put('description',$description)
                    }
                    $newOu.SetInfo()
                    $returnVal = $newOu
                } catch {
                    $this.AddLogEntry("$env:temp\adalops.log","New AdObject threw an error $($_.Exception)","Error")
                    $returnVal = $null
                }
                continue
            }
            "USER" {
                try {
                    $guid = (new-guid).guid.split('-')
                    $guid[0]+=[char](get-random -min 65 -max 90)
                    $guid[1]+=[char](get-random -min 65 -max 90)
                    $password = $guid -join '-'
                    $newUser = $adsiOu.Create('user', "CN=$name")
                    $newUser.put('samaccountname',$name)
                    if ($description) {
                        $newUser.put('description',$description)
                    }
                    $newUser.put('userPrincipalName',"$name@$($this.domainFqdn)")
                    $newUser.SetInfo()
                    $newUser.SetPassword($password)
                    $newUser.AccountDisabled = $false
                    $newUser.SetInfo()
                    $returnVal = $newUser
                } catch {
                    $this.AddLogEntry("$env:temp\adalops.log","New AD Object threw an error $($_.Exception)","Error")
                    $returnVal = $null
                }
                continue
            }
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting NewAdObject.","Info")
        return $returnVal
    }
    [bool]AddObjectToGroup([string]$objectName,[string]$objectIdentityLocation,[string]$groupName,[string]$groupIdentityLocation){
        $this.AddLogEntry("$env:temp\adalops.log","Entering AddObjectToGroup. ObjName is`: $objectName, ObjectIdentityLocation is`: $objectIdentityLocation, GroupName is`: $groupName, GroupIdentityLocation is`: $groupIdentityLocation","Info")
        $returnVal = $false
        # remember that GetObjectByName returns the LDAP path in the "path" property
        try {
            $objectToAdd = ($this.GetObjectByName($objectName,$objectIdentityLocation,$false))
            $groupToMod = ($this.GetObjectByName($groupName,$groupIdentityLocation,$false))
            if ($objectToAdd -and $groupToMod) {
                $path = "LDAP://$($this.chosenDc)/$($groupToMod.properties.distinguishedname)"
                if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                    
                    $adsiGroup = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
                } else {
                    $adsiGroup = [ADSI]"$path"
                }
                $adsiGroup.Add($($objectToAdd.path))
                $returnVal = $true
            } else {
                $returnVal = $false
            }
            
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","AddObjectToGroup threw an error $($_.Exception)","Error")
            $returnVal = $false
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting AddObjectToGroup. returnVal is $returnVal","Info")
        return $returnVal
    }
    [bool]IsObjectInGroup([string]$objectName,[string]$objectIdentityLocation,[string]$groupName,[string]$groupIdentityLocation){
        $this.AddLogEntry("$env:temp\adalops.log","Entering IsObjectInGroup. ObjectName is`: $objectName, ObjectIdentityLocation is`: $objectIdentityLocation, GroupName is`: $groupName, GroupIdentityLocation is $groupIdentityLocation","Info")
        $returnVal = $false
        $adsiGroup = $null
        try {
            $groupToCheck = ($this.GetObjectByName($groupName,$groupIdentityLocation,$false)).properties.distinguishedname
            $objectToVerify = ($this.GetObjectByName($objectName,$objectIdentityLocation,$false)).properties.distinguishedname
            $path = "LDAP://$($this.chosenDc)/$($groupToCheck)"
            if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
                $adsiGroup = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
            } else {
                $adsiGroup = [ADSI]"$path"
            }
            if ($($objectToVerify) -in $adsiGroup.member) {
                $returnVal = $true
            }
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","IsObjectInGroup threw an error $($_.Exception)","Error")
            $returnVal = $false
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting IsObjectInGroup. returnVal is $returnVal","Info")
        return $returnVal
    }
    [bool]SetAdAcl([string]$ou,[string]$identity,[string]$identityLocation,[string]$adRights,[string]$accessType,[string]$objType,[string]$inheritanceType,[string]$inheritedObjectType){
        $this.AddLogEntry("$env:temp\adalops.log","Entering SetAdAcl. Ou is`: $ou, Identity is`: $identity, IdentityLocation is`: $identityLocation, AdRights is $adRights, AccessType is`: $accessType, ObjType is`: $objType, InheritanceTYpe is`: $inheritanceType, InheritedObjectType is`: $inheritedObjectType","Info")
        $returnVal = $false
        $modified=$false
        $referencedIdentity = $null
        $strIdentity = $null
        switch ($identityLocation.toupper()) {
            "FOREST" {
                $referencedIdentity = [Security.principal.NTAccount]"$($this.forestNetbiosName)\$identity"
                $strIdentity = "$($this.forestNetbiosName)\$identity"
            }
            "DOMAIN" {
                $referencedIdentity = [Security.principal.NTAccount]"$($this.domainNetbiosName)\$identity"
                $strIdentity = "$($this.domainNetbiosName)\$identity"
            }
            "NT AUTHORITY" {
                $referencedIdentity = [Security.principal.NTAccount]"NT AUTHORITY\$identity"
                $strIdentity = "NT AUTHORITY\$identity"
            }
        }
        $path = "LDAP://$($this.chosenDc)/$ou"
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            try {
                Set-Item WSMan:localhost\client\trustedhosts -value $this.chosenDc -Force
                $sb = {
                    param($pCurrentDc,$pOu,$pIdent,$pAdRights,$pAccessType,$pObjType,$pInheritanceType,$pInheritedObjectType)
                    $modified=$false
                    $adsiOu=[ADSI]"LDAP://$pCurrentDc/$pOu"
                    $refId = [Security.principal.NTAccount]"$pIdent"
                    $castAdRights = [System.DirectoryServices.ActiveDirectoryRights]$pAdRights
                    $aclRuleObject = New-Object DirectoryServices.ActiveDirectoryAccessRule($refId,$castAdRights,"$pAccessType",[Guid]"$pObjType","$pInheritanceType",[guid]"$pInheritedObjectType")
                    $adsiOu.PSBase.ObjectSecurity.ModifyAccessRule([System.Security.AccessControl.AccessControlModification]::Add,$aclRuleObject,[ref]$modified) | out-null
                    $adsiOu.commitchanges()
                }
                Invoke-Command -computerName $($this.chosenDc) -credential $($this.credential) -scriptBlock $sb -ArgumentList $($this.chosenDc),$ou,"$strIdentity",$adRights,$accessType,$objType,$inheritanceType,$inheritedObjectType 
                get-Item WSMan:localhost\client\trustedhosts | ? { $_.value -eq $($this.chosenDc)} | clear-item -force
                $returnVal = $true
            } catch {
                $this.AddLogEntry("$env:temp\adalops.log","SetAdAcl threw an error in credentialed block contacting DC $($this.chosenDc) $($_.Exception)","Error")
                $returnVal = $false
            }
            
        } else {
            $adsiOu = [ADSI]"$path"
            $castAdRights = [System.DirectoryServices.ActiveDirectoryRights]$adRights        
            try {
                #build the rule object
                $aclRuleObject = New-Object DirectoryServices.ActiveDirectoryAccessRule($referencedIdentity,$castAdRights,"$accessType",[Guid]"$objType","$inheritanceType",[guid]"$inheritedObjectType")
                #add the ace
                $adsiOu.PSBase.ObjectSecurity.ModifyAccessRule([System.Security.AccessControl.AccessControlModification]::Add,$aclRuleObject,[ref]$modified) | out-null
                #modify the acl
                $adsiOu.commitchanges()
                $returnVal = $true
            } catch {
                $this.AddLogEntry("$env:temp\adalops.log","SetAdAcl threw an error $($_.Exception)","Error")
                #write-error $($_.Exception)
                $returnVal = $false
            }
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting SetAdAcl. returnVal is $returnVal","Info")
        return $returnVal
    }
    [system.object]GetAdAcl([string]$dn,[string]$identityLocation){
        $this.AddLogEntry("$env:temp\adalops.log","Entering GetAdAcl. DN is`: $dn","Info")
        $returnVal = $null
        $returnVal = $false
        $modified=$false
        $path = $null
        #ActiveDirectoryAccessRule (System.Security.Principal.IdentityReference identity, System.DirectoryServices.ActiveDirectoryRights adRights, System.Security.AccessControl.AccessControlType type, Guid objectType, System.DirectoryServices.ActiveDirectorySecurityInheritance inheritanceType, Guid inheritedObjectType);
        switch ($identityLocation.ToUpper()) {
            "FOREST" {$path = "LDAP://$($this.forestDetail.PDCEmulator)/$dn"}
            "DOMAIN" {$path = "LDAP://$($this.chosenDc)/$dn"}
            default {throw "Identity location must be one of FOREST or DOMAIN"}
        }
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $adsiObj = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $adsiObj = [ADSI]"$path"
        }
        try {
            $returnVal = ($adsiObj.psbase.ObjectSecurity.access)
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","Get AD ACL threw an error $($_.Exception)","Error")
            $returnVal = $null
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting GetAdAcl.","Info")
        return $returnVal
    }
    [bool]ResetAdAcl([string]$object,[bool]$isProtected,[bool]$preserveInheritance,[bool]$wipePerms){
        $this.AddLogEntry("$env:temp\adalops.log","Entering ResetAdAcl. object is`: $object, isProtected is`: $isProtected, preserveInheritance is`: $preserveInheritance","Info")
        $returnVal = $false
        #ActiveDirectoryAccessRule (System.Security.Principal.IdentityReference identity, System.DirectoryServices.ActiveDirectoryRights adRights, System.Security.AccessControl.AccessControlType type, Guid objectType, System.DirectoryServices.ActiveDirectorySecurityInheritance inheritanceType, Guid inheritedObjectType);
        $path = "LDAP://$($this.chosenDc)/$object"
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            try {
                Set-Item WSMan:localhost\client\trustedhosts -value $this.chosenDc -Force
                $sb = {
                    param($pCurrentDc,$pObject,$pIsProtected,$pPreserveInheritancem,$pWipePerms)
                    $adsiObject=[ADSI]"LDAP://$pCurrentDc/$pOu"
                    if ($pWipePerms) {
                        $adsiObject.psbase.ObjectSecurity.Access | ? { ($_.identityreference -ne "NT AUTHORITY\SELF") -and ($_.IsInherited -eq $false) } | % { 
                            $check=$adsiObject.psbase.ObjectSecurity.removeaccessrule($_)
                            $adsiObject.commitchanges()
                        }
                    }
                    $adsiObject.psbase.ObjectSecurity.SetAccessRuleProtection($pIsProtected,$pPreserveInheritance)
                    $adsiObject.commitchanges()
                } 
                Invoke-Command -computerName $($this.chosenDc) -credential $($this.credential) -scriptBlock $sb -ArgumentList $($this.chosenDc),$object,$isProtected,$preserveInheritance,$wipePerms
                get-Item WSMan:localhost\client\trustedhosts | ? { $_.value -eq $($this.chosenDc)} | clear-item -force
                $returnVal = $true
            } catch {
                $this.AddLogEntry("$env:temp\adalops.log","ResetAdAcl threw an error in credentialed block contacting DC $($this.chosenDc) $($_.Exception)","Error")
                $returnVal = $false
            }
            
        } else {
            $adsiObject = [ADSI]"$path"     
            try {
                if ($wipePerms) {
                    $adsiObject.psbase.ObjectSecurity.Access | ? { ($_.identityreference -ne "NT AUTHORITY\SELF") -and ($_.IsInherited -eq $false) } | % { 
                        $check=$adsiObject.psbase.ObjectSecurity.removeaccessrule($_)
                        $adsiObject.commitchanges()
                    }
                }
                $adsiObject.psbase.ObjectSecurity.SetAccessRuleProtection($isProtected,$preserveInheritance)
                $adsiObject.commitchanges()
                $returnVal = $true
            } catch {
                $this.AddLogEntry("$env:temp\adalops.log","ResetAdAcl threw an error $($_.Exception)","Error")
                $returnVal = $false
            }
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting ResetAdAcl. returnVal is $returnVal","Info")
        return $returnVal
    }
    [string]GetStringSidFromBytes([byte[]]$sid) {
        $returnVal = $null
        $stringSidBuilder = [System.Text.StringBuilder]::new("S-1-")
        $subAuthorityCount = $sid[1]
        [long]$identifierAuthority = 0
        $offset = 2
        $size = 6
        for ($i=0;$i -lt $size;$i++) {
            $identifierAuthority = $identifierAuthority -bor ([long]($sid[$offset+$i]) -shl (8*($size - 1 - $i)))
        }
        $stringSidBuilder.Append($identifierAuthority.ToString())
        $offset = 8
        $size = 4
        for ($x=0;$x -lt $subAuthorityCount;$x++) {
            [long]$subAuthority = 0
            for ($j=0;$j -lt $size; $j++) {
                $subAuthority = $subAuthority -bor ([long]($sid[$offset + $j]) -shl (8 * $j))
            }
            ($stringSidBuilder.Append("-")).Append($subAuthority)
            $offset+=$size
        }
        $returnVal = $stringSidBuilder.Tostring()
        return $returnVal
    }
    [bool]SetObjectByDn([string]$dn,[string]$propertyName,$propertyValue,[string]$identityLocation) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering SetObjectByDn. DN is`: $dn, propertyName is`: $propertyName, propertyValue is`: $propertyValue, IdentityLocation is`: $identityLocation","Info")
        $returnVal = $false
        $results = $null
        $path = $null
        $objPath = $null
        $forestDcToUse = $this.forestDetail.PDCEmulator
        $domainDcToUser = $this.chosenDc
        if ($propertyName -eq "ridavailablepool") {
            $forestDcToUse = $this.forestDetail.ridmaster
            $domainDcToUser = $this.domaindetail.ridmaster
        }
        switch ($identityLocation.ToUpper()) {
            "FOREST" {$path = "LDAP://$forestDcToUse/$dn"}
            "DOMAIN" {$path = "LDAP://$domainDcToUser/$dn"}
            default {throw "Identity location must be one of FOREST or DOMAIN"}
        }
        
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $objPath = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $objPath = [adsi]("$path")
        }
        try {
            if ($propertyValue -eq $null) {
                $objPath.PutEx(1, $propertyName, 0)
            } else {
                $objPath.put($propertyName,$propertyValue)
            }
            $objPath.setinfo()
            $returnVal = $true
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","SetObjectByDn threw an error $($_.Exception)","Error")
            $returnVal = $false
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting SetObjectByDn.","Info")
        return $returnVal
    }
    [bool]RemoveObjectByDn([string]$dn,[string]$identityLocation) {
        $this.AddLogEntry("$env:temp\adalops.log","Entering RemoveObjectByDn. DN is`: $dn, IdentityLocation is`: $identityLocation","Info")
        $returnVal = $false
        $parentDn = $null
        $dnArr = @()
        $obj = $null
        $objPath = $null
        $path = $null
        $objClass = $null
        $dnArr = $dn.split(',')
        $parentDn = $dn.Replace("$($dnArr[0]),",'')
        $deleteCn = $dnArr[0]
        switch ($identityLocation.ToUpper()) {
            "FOREST" {$path = "LDAP://$($this.forestDetail.PDCEmulator)/$parentDn"}
            "DOMAIN" {$path = "LDAP://$($this.chosenDc)/$parentDn"}
            default {throw "Identity location must be one of FOREST or DOMAIN"}
        }
        
        if ($this.credential -ne [System.Management.Automation.PSCredential]::Empty) {
            $objPath = New-Object System.DirectoryServices.DirectoryEntry($path,$($this.credential.username),$($this.credential.getnetworkcredential().password),512)
        } else {
            $objPath = [adsi]("$path")
        }
        try {
            #in order to delete you need the object class
            #do a find on the DN and grab the class
            $obj = $this.GetObjectByDn($dn,$identityLocation)
            if ("msDS-GroupManagedServiceAccount" -in $obj.properties.objectclass) {
                $objClass = "msDS-GroupManagedServiceAccount"
            } else {
                if ("user" -in $obj.properties.objectclass) {
                    $objClass = "User"
                } elseif ("group" -in $obj.properties.objectclass) {
                    $objClass = "Group"
                } elseif ("organizationalUnit" -in $obj.properties.objectclass) {
                    $objClass = "organizationalUnit"
                }
            }
            # if nothing matched...
            if ($objClass -eq $null) {
                $objClass = $obj.properties.objectclass[$obj.properties.objectclass.count -1]
            }
            if ($objClass -eq "organizationalUnit") {
                # can't delete an ou with nested objects

            } else {
                #use the parent bind and then delete the sub obj
                $objPath.Delete($objClass,$deleteCn)
                $returnVal = $true
            }
            
        } catch {
            $this.AddLogEntry("$env:temp\adalops.log","RemoveObjectByDn threw an error $($_.Exception)","Error")
            $returnVal = $false
        }
        $this.AddLogEntry("$env:temp\adalops.log","Exiting RemoveObjectByDn.","Info")
        return $returnVal
    }
    [void]BuildGpcMap(){
        $this.gpcArray = [System.Collections.ArrayList]::new()
        $this.gpcArray.Add([PSCustomObject]@{name="applications"; cse='{F9C77450-3A41-477E-9310-9ACD617BD9E3}'; tool='{0DA274B5-EB93-47A7-AAFB-65BA532D3FE6}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="datasources"; cse='{728EE579-943C-4519-9EF7-AB56765798ED}'; tool='{1612b55c-243c-48dd-a449-ffc097b19776}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="devices"; cse='{1A6364EB-776B-4120-ADE1-B63A406A76B5}'; tool='{1b767e9a-7be4-4d35-85c1-2e174a7ba951}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="environmentvariables"; cse='{0E28E245-9368-4853-AD84-6DA3BA35BB75}'; tool='{35141B6B-498A-4CC7-AD59-CEF93D89B2CE}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="files"; cse='{7150F9BF-48AD-4da4-A49C-29EF4A8369BA}'; tool='{3BAE7E51-E3F4-41D0-853D-9BB9FD47605F}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="folderoptions"; cse='{A3F3E39B-5D83-4940-B954-28315B82F0A8}'; tool='{3BFAE46A-7F3A-467B-8CEA-6AA34DC71F53}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="folders"; cse='{6232C319-91AC-4931-9385-E70C2B099F0E}'; tool='{3EC4E9D3-714D-471F-88DC-4DD4471AAB47}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="inifiles"; cse='{74EE6C03-5363-4554-B161-627540339CAB}'; tool='{516FC620-5D34-4B08-8165-6A06B623EDEB}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="internetsettings"; cse='{E47248BA-94CC-49C4-BBB5-9EB7F05183D0}'; tool='{5C935941-A954-4F7C-B507-885941ECE5C4}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="localusersandgroups"; cse='{17D89FEC-5C44-4972-B12D-241CAEF74509}'; tool='{79F92669-4224-476c-9C5C-6EFB4D87DF4A}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="networkoptions"; cse='{3A0DBA37-F8B2-4356-83DE-3E90BD5C261F}'; tool='{949FB894-E883-42C6-88C1-29169720E8CA}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="networkshares"; cse='{6A4C88C6-C502-4f74-8F60-2CB23EDC24E2}'; tool='{BFCBBEB0-9DF4-4c0c-A728-434EA66A0373}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="poweroptions"; cse='{E62688F0-25FD-4c90-BFF5-F508B9D2E31F}'; tool='{9AD2BAFE-63B4-4883-A08C-C3C6196BCAFD}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="printers"; cse='{BC75B1ED-5833-4858-9BB8-CBF0B166DF9D}'; tool='{A8C42CEA-CDB8-4388-97F4-5831F933DA84}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="regionaloptions"; cse='{E5094040-C46C-4115-B030-04FB2E545B00}'; tool='{B9CCA4DE-E2B9-4CBD-BF7D-11B6EBFBDDF7}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="registry"; cse='{B087BE9D-ED37-454f-AF9C-04291E351182}'; tool='{BEE07A6A-EC9F-4659-B8C9-0B1937907C83}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="scheduledtasks"; cse='{AADCED64-746C-4633-A97C-D61349046527}'; tool='{CAB54552-DEEA-4691-817E-ED4A4D1AFC72}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="services"; cse='{91FBB303-0CD5-4055-BF42-E512A681B325}'; tool='{CC5746A9-9B74-4be5-AE2E-64379C86E0E4}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="shortcuts"; cse='{C418DD9D-0D14-4efb-8FBF-CFE535C8FAC7}'; tool='{CEFFA6E2-E3BD-421B-852C-6F6A79A59BC1}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="startmenu"; cse='{E4F48E54-F38D-4884-BFB9-D4D2E5729C18}'; tool='{CF848D48-888D-4F45-B530-6A201E62A605}';class='gpp'})
        $this.gpcArray.Add([PSCustomObject]@{name="audit"; cse='{F3CCC681-B74C-4060-9F26-CD84525DCA2A}'; tool='{0F3F3735-573D-9804-99E4-AB2A69BA5FD4}';class='audit'})
        $this.gpcArray.Add([PSCustomObject]@{name="firewall"; cse='{35378EAC-683F-11D2-A89A-00C04FBBCFA2}'; tool='{B05566AC-FE9C-4368-BE01-7A4CBB6CBA11}';class='gpc'})
        $this.gpcArray.Add([PSCustomObject]@{name="ipsec"; cse='{DEA8AFA0-CC85-11D0-9CE2-0080C7221EBD}'; tool='{E437BC1C-AA7D-11D2-A382-00C04F991E27}';class='gpc'})
        $this.gpcArray.Add([PSCustomObject]@{name="security"; cse='{827D319E-6EAC-11D2-A4EA-00C04F79F83A}'; tool='{803E14A0-B4FB-11D0-A0D0-00A0C90F574B}';class='gpc'})
        $this.gpcArray.Add([PSCustomObject]@{name="softwareinstall"; cse='{C6DC5466-785A-11D2-84D0-00C04FB169F7}'; tool='{942A8E4F-A261-11D1-A760-00C04FB9603F}';class='gpc'})
        $this.gpcArray.Add([PSCustomObject]@{name="laps"; cse='{35378EAC-683F-11D2-A89A-00C04FBBCFA2}'; tool='{D02B1F72-3407-48AE-BA88-E8213C6761F1}';class='gpc'})
    }
    [string[]]DecodeUserAccountControl([int]$uac){
        $UACPropertyFlags = @(
            "SCRIPT",
            "ACCOUNTDISABLE",
            "RESERVED",
            "HOMEDIR_REQUIRED",
            "LOCKOUT",
            "PASSWD_NOTREQD",
            "PASSWD_CANT_CHANGE",
            "ENCRYPTED_TEXT_PWD_ALLOWED",
            "TEMP_DUPLICATE_ACCOUNT",
            "NORMAL_ACCOUNT",
            "RESERVED",
            "INTERDOMAIN_TRUST_ACCOUNT",
            "WORKSTATION_TRUST_ACCOUNT",
            "SERVER_TRUST_ACCOUNT",
            "RESERVED",
            "RESERVED",
            "DONT_EXPIRE_PASSWORD",
            "MNS_LOGON_ACCOUNT",
            "SMARTCARD_REQUIRED",
            "TRUSTED_FOR_DELEGATION",
            "NOT_DELEGATED",
            "USE_DES_KEY_ONLY",
            "DONT_REQ_PREAUTH",
            "PASSWORD_EXPIRED",
            "TRUSTED_TO_AUTH_FOR_DELEGATION",
            "RESERVED",
            "PARTIAL_SECRETS_ACCOUNT"
            "RESERVED"
            "RESERVED"
            "RESERVED"
            "RESERVED"
            "RESERVED"
        )
        return (0..($UACPropertyFlags.Length) | ?{$uac -bAnd [math]::Pow(2,$_)} | %{$UACPropertyFlags[$_]})
    }
    [string]ToString(){
        return ("ChosenDC:{0} | PDC:{1} | DomainMode:{2} | DomainDn:{3} | DomainNetBiosName:{4} | DomainFQDN:{5} | Sysvol:{6} | ForestNetBiosName:{7} | ForestFQDN:{8} | ForestMode:{9}" -f $this.chosenDc, $this.pdc, $this.domainDetail.DomainMode, $this.domainDn, $this.domainNetbiosName, $this.domainFqdn, $this.sysvolPath, $this.forestNetbiosName, $this.forestFqdn, $this.adforest.forestmode)
    }
}
class MdiDomainReadiness {
    [string]$identity
    [string]$identityLocation
    hidden [string]$identitySid
    [bool]$identityExists
    [bool]$isGmsa
    hidden [bool]$domainAuditPoliciesExists
    hidden [bool]$exchangeAuditPoliciesExists
    [bool]$exchangeAuditPoliciesNeeded
    [bool]$adfsAuditPoliciesNeeded
    [bool]$pkiAuditPoliciesNeeded
    hidden [bool]$adfsAuditPoliciesExists
    [bool]$wmiFilterExists
    [bool]$isAdRecycleBinEnabled
    hidden [PSCustomObject]$AdvancedAuditPolicyCAs
    hidden [PSCustomObject]$AdvancedAuditPolicyDCs
    hidden [PSCustomObject]$EntraIDAuditing
    hidden [PSCustomObject]$LogonAsService
    hidden [PSCustomObject]$NTLMAuditing
    hidden [PSCustomObject]$PerformanceLib
    hidden [PSCustomObject]$ProcessorPerformance
    hidden [PSCustomObject]$RemoteSAM
    hidden [PSCustomObject]$DeployGpo
    hidden [PSCustomObject]$RemoveGpo
    [string]$gpoReport
    [string]$domainFqdn
    hidden $myDomain
    MdiDomainReadiness ($myDomain){
        $this.myDomain = $myDomain
        $this.isAdRecycleBinEnabled = $this.myDomain.isAdRecycleBinEnabled
        $this.domainFqdn = $this.myDomain.domainFqdn
        $this.InitObjects()
    }
    MdiDomainReadiness ($myDomain,[string]$identity){
        $this.myDomain = $myDomain
        $this.identity = $identity
        $this.isAdRecycleBinEnabled = $this.myDomain.isAdRecycleBinEnabled
        $this.domainFqdn = $this.myDomain.domainFqdn
        $this.InitObjects()
    }
    [void]GetOptionalComponents(){
        $this.exchangeAuditPoliciesNeeded = ([System.DirectoryServices.DirectoryEntry]::Exists('LDAP://CN=Microsoft Exchange,CN=Services,CN=Configuration,{0}' -f $this.myDomain.forestDetail.DistinguishedName))
        $this.adfsAuditPoliciesNeeded = ([System.DirectoryServices.DirectoryEntry]::Exists('LDAP://CN=ADFS,CN=Microsoft,CN=Program Data,{0}' -f $this.myDomain.domainDn))
        $this.pkiAuditPoliciesNeeded = ([System.DirectoryServices.DirectoryEntry]::Exists('LDAP://CN=NTAuthCertificates,CN=Public Key Services,CN=Services,CN=Configuration,{0}' -f $this.myDomain.forestDetail.DistinguishedName))
    }
    [void]InitObjects(){
        $this.AdvancedAuditPolicyCAs = @{
            GpoName = "MDI - Required Audit Policies - AD Cert Services ONLY"
            GpoExists = $false
        }
        $this.AdvancedAuditPolicyDCs = @{
            GpoName = "MDI - Required Audit Policies - DC ONLY"
            GpoExists = $false
        }
        $this.EntraIDAuditing = @{
            GpoName = "MDI - Entra ID Connect Auditing - Entra Connect ONLY"
            GpoExists = $false
        }
        $this.LogonAsService = @{
            GpoName = "MDI - Logon As a Service - Entra Connect ADCS ADFS ONLY"
            GpoExists = $false
        }
        $this.NTLMAuditing = @{
            GpoName = "MDI - NTLM Auditing - DC ONLY"
            GpoExists = $false
        }
        $this.PerformanceLib = @{
            GpoName = "MDI - Perflib Registry Permissions - DC ONLY"
            GpoExists = $false
        }
        $this.ProcessorPerformance = @{
            GpoName = "MDI - High Performance PowerPlan - ALL MDI DEPLOYMENTS"
            GpoExists = $false
        }
        $this.RemoteSAM = @{
            GpoName = "MDI - Remote SAM Access - TOP LEVEL - DC FILTERED"
            GpoExists = $false
        }
        $this.DeployGpo = @{
            GpoName = "MDI - Deploy"
            GpoExists = $false
        }
        $this.RemoveGpo = @{
            GpoName = "MDI - Remove"
            GpoExists = $false
        }
    }
    [void]CreateGpoReport(){
        $this.gpoReport = "Name: {0}, Exists: {1}{2}" -f $this.AdvancedAuditPolicyCAs.GpoName,$this.AdvancedAuditPolicyCAs.GpoExists,[environment]::NewLine
        $this.gpoReport += "Name: {0}, Exists: {1}{2}" -f $this.AdvancedAuditPolicyDCs.GpoName,$this.AdvancedAuditPolicyDCs.GpoExists,[environment]::NewLine
        $this.gpoReport += "Name: {0}, Exists: {1}{2}" -f $this.EntraIDAuditing.GpoName,$this.EntraIDAuditing.GpoExists,[environment]::NewLine
        $this.gpoReport += "Name: {0}, Exists: {1}{2}" -f $this.LogonAsService.GpoName,$this.LogonAsService.GpoExists,[environment]::NewLine
        $this.gpoReport += "Name: {0}, Exists: {1}{2}" -f $this.NTLMAuditing.GpoName,$this.NTLMAuditing.GpoExists,[environment]::NewLine
        $this.gpoReport += "Name: {0}, Exists: {1}{2}" -f $this.PerformanceLib.GpoName,$this.PerformanceLib.GpoExists,[environment]::NewLine
        $this.gpoReport += "Name: {0}, Exists: {1}{2}" -f $this.ProcessorPerformance.GpoName,$this.ProcessorPerformance.GpoExists,[environment]::NewLine
    }
    [string]ToString(){
        return "Identity:{0},IdentityExists:{1},IsGMSA:{2},ExchangeAuditPoliciesNeeded:{3},AdfsAuditPoliciesNeeded:{4},PkiAuditPoliciesNeeded:{5},WmiFilterExists:{6},IsAdRecycleBinEnabled:{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},DomainFQDN:{24}" `
        -f $this.identity,$this.identityExists,$this.isGmsa,$this.exchangeAuditPoliciesNeeded,$this.adfsAuditPoliciesNeeded,$this.pkiAuditPoliciesNeeded,$this.wmiFilterExists,$this.isAdRecycleBinEnabled,$this.AdvancedAuditPolicyCAs.GpoName,$this.AdvancedAuditPolicyCAs.GpoExists,$this.AdvancedAuditPolicyDCs.GpoName,$this.AdvancedAuditPolicyDCs.GpoExists,$this.EntraIDAuditing.GpoName,$this.EntraIDAuditing.GpoExists,$this.LogonAsService.GpoName,$this.LogonAsService.GpoExists,$this.NTLMAuditing.GpoName,$this.NTLMAuditing.GpoExists,$this.PerformanceLib.GpoName,$this.PerformanceLib.GpoExists,$this.ProcessorPerformance.GpoName,$this.ProcessorPerformance.GpoExists,$this.RemoteSAM.GpoName,$this.RemoteSAM.GpoExists,$this.domainFqdn
    }
}
Class MdiGpo {
    [string]$name
    [string]$guid
    [System.Collections.Hashtable]$filePath
    [System.Collections.Hashtable]$content
    hidden [bool]$created
    MdiGpo ([string]$name) {
        $this.filePath = @{}
        $this.content = @{}
        $this.name = $name
    }
    MdiGpo ([string]$name,[string]$guid) {
        $this.filePath = @{}
        $this.content = @{}
        $this.guid = $guid
        $this.name = $name
    }
    MdiGpo ([string]$name,[string]$guid,[System.Collections.Hashtable]$filePath) {
        $this.filePath = @{}
        $this.content = @{}
        $this.guid = $guid
        $this.name = $name
        $this.filePath = $filePath
    }
    MdiGpo ([string]$name,[string]$guid,[System.Collections.Hashtable]$filePath,[System.Collections.Hashtable]$content) {
        $this.filePath = @{}
        $this.content = @{}
        $this.guid = $guid
        $this.name = $name
        $this.filePath = $filePath
        $this.content = $content
    }
    [string]ToString(){
        return ("{0},{1}" -f $this.name, $this.guid)
    }
}
Class MdiManifest {
    [System.Collections.Hashtable]$Manifest = @{}
    MdiManifest() {
        $this.Manifest.Add("Error", $false)
        $this.Manifest.Add("IsSupported", $true)
        $this.Manifest.Add("RunGuid",$null)
        $this.Manifest.Add("RunDate",$null)
        $this.Manifest.Add("DeviceNetBIOSName",'')
        $this.Manifest.Add("DomainName",'')
        $this.Manifest.Add("DomainRole",'')
        $this.Manifest.Add("MachineType",'')
        $this.Manifest.Add("PreReq", [System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("Hardware",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("PowerPlan",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("AuditPolicies",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("NtlmAuditing",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("Certificates",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("SensorVersion",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("OperatingSystem",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("Sysvol",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("CertificateAutoUpdate",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("CipherSuite",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("PerformanceCounter",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("CpuScheduler",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("LogonAsAService",[System.Collections.Hashtable]::new())
        $this.Manifest.PreReq.Add("DotNet",[System.Collections.Hashtable]::new())
        $this.Manifest.Add("Npcap", [System.Collections.Hashtable]::new())
        $this.Manifest.Npcap.Add("RequiresInstall",$true)
        $this.Manifest.Npcap.Add("FileName",'')
        $this.Manifest.Npcap.Add("FilePath",'')
        $this.Manifest.Npcap.Add("InstallState",'')
        $this.Manifest.Npcap.Add("ExitCode",0)
        $this.Manifest.Add("MDI", [System.Collections.Hashtable]::new())
        $this.Manifest.MDI.Add("ScriptVersion", '')
        $this.Manifest.MDI.Add("SensorConfig", [System.Collections.Hashtable]::new())
        $this.Manifest.MDI.SensorConfig.Add("FileName",'')
        $this.Manifest.MDI.SensorConfig.Add("FilePath",'')
        $this.Manifest.MDI.Add("Agent", [System.Collections.Hashtable]::new())
        $this.Manifest.MDI.Agent.Add("IsRunning",$false)
        $this.Manifest.MDI.Agent.Add("RequiresInstall",$true)
        $this.Manifest.MDI.Agent.Add("FileName",'')
        $this.Manifest.MDI.Agent.Add("FilePath",'')
        $this.Manifest.MDI.Agent.Add("InstallState",'')
        $this.Manifest.MDI.Agent.Add("ExitCode",0)
    }
}
class MdiServerReadiness {
    [bool]$rootCertsPresent
    [bool]$cipherSuiteOrder
    [bool]$perfCountersHealthy
    [bool]$cpuSchedulerDefault
    [bool]$npcapCompatible
    $npcapDetails
    [bool]$osCompliant
    [bool]$netCompliant
    [string]$server
    [bool]$isReady
    MdiServerReadiness(){
        $this.server = "localhost"
        $this.rootCertsPresent = $false
        $this.cipherSuiteOrder = $false
        $this.perfCountersHealthy = $false
        $this.cpuSchedulerDefault = $false
        $this.npcapCompatible = $false
        $this.osCompliant = $false
        $this.netCompliant = $false
        $this.isReady = $false
    }
    MdiServerReadiness([string]$server){
        $this.server = $server
        $this.rootCertsPresent = $false
        $this.cipherSuiteOrder = $false
        $this.perfCountersHealthy = $false
        $this.cpuSchedulerDefault = $false
        $this.npcapCompatible = $false
        $this.osCompliant = $false
        $this.netCompliant = $false
        $this.isReady = $false
    }
    [string]ToString(){
        return ("Server:{0} | RootCerts:{1} | CipherSuite:{2} | PerfCount:{3} | CpuSched:{4} | Npcap:{5} | NpcapDetails:{9} | OS:{6} | Net:{7} | IsReady:{8}" -f $this.server, $this.rootCertsPresent, $this.cipherSuiteOrder, $this.perfCountersHealthy, $this.cpuSchedulerDefault, $this.npcapCompatible, $this.osCompliant, $this.netCompliant, $this.isReady, $this.npcapDetails -join '')
    }
}

function Add-LogEntry 
{
        ##########################################################################################################
    <#
    .SYNOPSIS
        Adds a log entry to a file
    
    .DESCRIPTION
        Adds a log entry to a file

    .EXAMPLE
        Add-LogEntry -logFilePath C:\windows\temp\mylog.log -logMessage "a string" -logSev "Error"

        The -logFilePath argument is the filepath
        The -logMessage argument is the string to add
        The -logSev is one of Info, Warn, Error
        The -clobber switch allows you to delete the file

    .OUTPUTS
        To the specified log file.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    
    #Requires -version 4

    #Define and validate parameters
    [CmdletBinding()]
    Param(
        [parameter(Mandatory=$True,Position=1)]
        [String]$logFilePath,
        [parameter(Mandatory=$True,Position=2)]
        [String]$logMessage,
        [parameter(Mandatory=$True,Position=3)]
        [ValidateSet("Info","Warn", "Error")]
        [String]$logSev,
        [parameter(Mandatory=$false,Position=4)]
        [switch]$clobber=$false
    )
    $normalizedLogMessage = $null
    try {
        if (!(test-path $logFilePath)) {
            new-item -itemtype file -path $logFilePath -force | out-null
        }
        try {
            $normalizedLogMessage = [string]::join(" ",($logMessage.Split("`r?`n")))
        } catch {
            $normalizedLogMessage = $logMessage
        }
        if ($clobber) {
            "{0}`t{1}`t{2}" -f (get-date).touniversaltime().tostring("dd/MM/yyyy HH:mm:ss"), $logSev, $normalizedLogMessage | set-content -path $logFilePath -force | out-null
        } else {
            "{0}`t{1}`t{2}" -f (get-date).touniversaltime().tostring("dd/MM/yyyy HH:mm:ss"), $logSev, $normalizedLogMessage | add-content -path $logFilePath -force | out-null
        }   
    } catch {
        write-warning "Logging engine failure!"
    }
    
}
function Get-AdGuids
{
    $ObjectTypeGUID = @{}

    $GetADObjectParameter=@{
        SearchBase=(Get-ADRootDSE).SchemaNamingContext
        LDAPFilter='(SchemaIDGUID=*)'
        Properties=@("Name", "SchemaIDGUID")
    }

    $SchGUID=Get-ADObject @GetADObjectParameter
        Foreach ($SchemaItem in $SchGUID){
        $ObjectTypeGUID.Add([GUID]$SchemaItem.SchemaIDGUID,$SchemaItem.Name)
    }

    $ADObjExtPar=@{
        SearchBase="CN=Extended-Rights,$((Get-ADRootDSE).ConfigurationNamingContext)"
        LDAPFilter='(ObjectClass=ControlAccessRight)'
        Properties=@("Name", "RightsGUID")
    }

    $SchExtGUID=Get-ADObject @ADObjExtPar
        ForEach($SchExtItem in $SchExtGUID){
        try {
            $ObjectTypeGUID.Add([GUID]$SchExtItem.RightsGUID,$SchExtItem.Name)
        } catch {}
        
    }
    return $ObjectTypeGUID
}
function Get-OnboardingFile
{
    [CmdletBinding()]
    [OutputType([string])]
    param
    (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string] $FileName,

        [Parameter()]
        [switch] $Refresh
    )

    $fileExists = $false

    if ($script:moduleDocsPath)
    {
        $localInstallerPath = (Get-ChildItem -Path $script:moduleDocsPath -Filter $FileName -Recurse).FullName

        if ($null -eq $localInstallerPath)
        {
            throw ('''{0}'' was not found. Please copy the file to ''{1}'' and rerun the command.' -f $FileName, $script:moduleDocsPath)
        }
    }
    else
    {
        # check to see if there are myDestPath and mySourcePath environment variables from GPO
        $myDestPath = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' -Name 'myDestPath' -ErrorAction 'SilentlyContinue').MyDestPath
        $mySourcePath = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' -Name 'mySourcePath' -ErrorAction 'SilentlyContinue').MySourcePath

        # Get the setup type from the call stack
        #$null = (Get-PSCallStack)[0].Location -match '\w+-(?<target>\w+)'
        $setupType = 'MDISetup' # $matches.target

        # $PSCmdlet.MyInvocation.ScriptName is available across all versions of PS
        $invocationPath = Split-Path -Path $PSCmdlet.MyInvocation.ScriptName
        $localInstallerPath = Join-Path $invocationPath -ChildPath $FileName
    }

    if (Test-Path -Path $localInstallerPath) # manual script /module invocation, check for files locally
    {
        Write-Log -Message ('{0} found in local invocation directory. {1}' -f $FileName, $localInstallerPath)
        $filePath = $localInstallerPath
        $fileExists = $true
    }
    # a null check is required first, because Test-Path will throw an error if $env:myDestPath is null even with
    # the error pref set to SilentlyContinue. By anding the null check first, .Net optimization will return false
    # before running the Test-Path
    elseif (($null -ne $myDestPath) -and (Test-Path -Path $myDestPath)) # myDestPath and mySource set via GPO
    {
        $myDestPath = Join-Path -Path $myDestPath -ChildPath $setupType
        $mySourcePath = Join-Path -Path $mySourcePath -ChildPath $setupType

        $localMyDestinationPath = Join-Path -Path $myDestPath -ChildPath $FileName

        if (Test-Path -Path $localMyDestinationPath)
        {
            Write-Log -Message ('Setup file found in myDestPath {0}.' -f $localMyDestinationPath)
            $filePath = $localMyDestinationPath
            $fileExists = $true
        }
        elseIf (($null -ne $mySourcePath) -and (Test-Path -Path $mySourcePath))
        {
            $sourceFilePath = (Get-ChildItem -Path $mySourcePath -Filter $FileName -Recurse).FullName

            try
            {
                $copyResult = Copy-Item -Path $sourceFilePath -Destination $myDestPath -ErrorAction 'Stop' -PassThru -Force
                $filePath = $copyResult.FullName
                Write-Log -Message ('Setup file copied from {0} to {1}.' -f $sourceFilePath, $myDestPath ) -TypeName 'Error'
            }
            catch
            {
                Write-Log -Message ('Setup file not found {0}. Cannot continue.' -f $FileName ) -TypeName 'Error'
                $filePath = $null
            }
        }
        else
        {
            Write-Log -Message ('Ensure ''{0}'' contains the needed files and rerun.' -f $mySourcePath) -TypeName 'Error'
            $filePath = $null
        }
    }
    else
    {
        Write-Log -Message ('{0} not found in the local invocation directory and myDesPath is null.' -f $FileName) -TypeName Error
        $filePath = $null
    }

    if ($fileExists -and $Refresh)
    {
        $remotePath = (Get-ChildItem -Path $mySourcePath -Filter $FileName -Recurse).FullName
        $localPath = Join-Path -Path $myDestPath -ChildPath $FileName

        $remoteHash = Get-OnboardingFileHash -FilePath $remotePath
        $localHash = Get-OnboardingFileHash -FilePath $localPath

        if ($remoteHash -ne $localHash)
        {
            $copyResult = Copy-Item -Path $remotePath -Destination $myDestPath -ErrorAction Stop -PassThru -Force
            $filePath = $copyResult.FullName
        }
    }

    # TODO This doesn't work on Win7, so we need to make a decision on using this
    # We need to update documentation to unblock all setup files at setup time.
    # Unblock-File -Path $destinationPath

    $filePath
}
function Get-OnboardingFileHash
{
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingInvokeExpression', '', Justification = 'Inputs are restricted.')]
    [CmdletBinding()]
    [OutputType([string])]
    param
    (
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo] $FilePath
    )

    try
    {
        $certUtil = Get-Command -Name Certutil -ErrorAction Stop
        $result = Invoke-Expression -Command ('{0} -hashfile {1}  SHA256' -f $certUtil.Source, $FilePath.FullName)
        ($result | Select-String '[A-Fa-f0-9]{64}').ToString()
    }
    catch
    {
        (Get-Date).ToString('yymmddhhmmssfff')
    }
}
function Initialize-Manifest {
    [CmdletBinding()]
    [OutputType([MdiManifest])]
    param
    (
        [Parameter()]
        [string] $ComputerName,
        [Parameter(Mandatory)]
        [string] $Identity,
        [Parameter(Mandatory)]
        [string] $CloudType,
        [Parameter(Mandatory)]
        [MdiManifest] $manifest
    )

    # https://docs.microsoft.com/en-us/dotnet/api/microsoft.powershell.commands.producttype?view=powershellsdk-1.1.0
    $productType = @{
        0 = 'Unknown'
        1 = 'WorkStation'
        2 = 'DomainController'
        3 = 'Server'
    }

    $deviceNetBIOSName = if ($ComputerName)
    {
        $ComputerName
    }
    else
    {
        $env:COMPUTERNAME
    }

    $DomainDnsName = ''

    $manifest.manifest.RunGuid = [Guid]::NewGuid().Guid.ToLower()
    $manifest.manifest.RunDate = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $manifest.manifest.DeviceNetBIOSName = $deviceNetBIOSName
    $manifest.manifest.DeviceDnsName = [System.Net.Dns]::GetHostEntry($deviceNetBIOSName).HostName
    $manifest.manifest.DomainName = [Environment]::UserDomainName
    $manifest.manifest.MachineType = Get-MDIMachineType -ComputerName $ComputerName

    $manifest.manifest.PreReq.Hardware = Get-MDIServerRequirements -ComputerName $ComputerName
    $manifest.manifest.PreReq.PowerPlan = Get-MDIPowerScheme -ComputerName $ComputerName
    $manifest.manifest.PreReq.AuditPolicies = Get-MDIAdvancedAuditing -ComputerName $ComputerName
    $manifest.manifest.PreReq.NtlmAuditing = Get-MDINtlmAuditing -ComputerName $ComputerName
    $manifest.manifest.PreReq.Certificates = Get-MDICertificateReadiness -ComputerName $ComputerName -CloudType $CloudType
    $manifest.manifest.PreReq.SensorVersion = Get-MDISensorVersion -ComputerName $ComputerName
    $manifest.manifest.PreReq.OperatingSystem = Get-MDIOSVersion -ComputerName $ComputerName
    $manifest.manifest.PreReq.CertificateAutoUpdate = Get-MDICertificateAutoUpdate -ComputerName $ComputerName
    $manifest.manifest.PreReq.CipherSuite = Get-MDICipherSuiteOrder -ComputerName $ComputerName
    $manifest.manifest.PreReq.PerformanceCounter = Get-MDIPerformanceCounter -ComputerName $ComputerName
    $manifest.manifest.PreReq.CpuScheduler = Get-MdiCpuSchedulerManifest -ComputerName $ComputerName
    

    $manifest.manifest.PreReq.DotNet = Get-MDIDotNetVersion -ComputerName $ComputerName

    $manifest.manifest.Npcap.RequiresInstall = -not (Get-MDINpcap)

    $serviceParams = @{
        Name        = 'AATPSensor'
        ErrorAction = 'SilentlyContinue'
    }

    if ($ComputerName)
    {
        $serviceParams.Add('ComputerName', $ComputerName)
    }

    $manifest.manifest.MDI.Agent.IsRunning = (Get-Service @serviceParams).Status -eq 'running'

    $manifest.manifest.DomainRole = $productType[[int]$manifest.manifest.PreReq.OperatingSystem.details.ProductType]

    if ($manifest.manifest.DomainRole -ne 'DomainController') {
        try {
            $manifest.manifest.PreReq.LogonAsAService = Get-MDILogonAsAService -ComputerName $ComputerName -Identity $Identity
        } catch {
            $manifest.manifest.PreReq.LogonAsAService = [pscustomobject]@{
                IsOk    = $false
                Details = 'Failed to get logon as a service configuration'
            }
        }
        
    } else {
        $manifest.manifest.PreReq.LogonAsAService = [pscustomobject]@{
            IsOk    = $true
            Details = '{0} is a domain controller' -f $deviceNetBIOSName
        }
        $manifest.manifest.PreReq.Sysvol = Get-MDISysvolReady -ComputerName $ComputerName
    }
    $manifest.manifest.IsSupported = $true
    $manifest
}
function Initialize-MyDomain {
    [CmdletBinding()]
    Param (
        [Parameter(mandatory=$false)]
        [AllowEmptyString()]
        [string]$domain,
        [Parameter(mandatory=$false)]
        [AllowEmptyString()]
        $myDomain,
        [Parameter(mandatory=$false)]
        [AllowEmptyString()]
        $credential
    )

    if ($myDomain -eq $null) {
        try {
            if ($credential) {
                $myDomain = [AdAl]::new($credential)
            } else {
                $myDomain = [AdAl]::new()
            }
            if ($domain) {
                $myDomain.AutoDomainDiscovery($domain)
            } else {
                $myDomain.AutoDomainDiscovery($null)
            }
            if ([string]::IsNullOrEmpty($myDomain.chosenDc)) {
                throw
            }
        } catch {
            write-error "Failed to discover AD. See $env:temp\adalops.log for details. Critical stop!"
        	Add-LogEntry -logFilePath $env:temp\adalops.log -logMessage "Failed to discover AD $($_.Exception)" -logSev "Error" | out-null
        	throw
        }
    }
    return $myDomain
}
function Resolve-CloudType
{
    [CmdletBinding()]
    param
    (
        [Parameter()]
        [switch] $Storage,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Commercial', 'GCC', 'GCC-H', 'DOD')]
        [string] $CloudType
    )

    $cloudTypeMap = @{
        Commercial = 0
        GCC        = 1
        'GCC-H'    = 1
        DOD        = 1
    }
    $storageBaseUrlMap = @{
        Commercial = 'windows.net'
        GCC        = 'windows.net'
        'GCC-H'    = 'usgovcloudapi.net'
        DOD        = ''
    }

    if ($Storage)
    {
        return $storageBaseUrlMap[$CloudType]
    }

    return $cloudTypeMap[$CloudType]
}
function Send-OnboardingLogFile
{
    [CmdletBinding()]
    param
    (
        [Parameter(Mandatory = $true)]
        [string] $StorageAccountName,

        [Parameter(Mandatory = $true)]
        [string] $SASToken,

        [Parameter(Mandatory = $true)]
        $Manifest,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Commercial', 'GCC', 'GCC-H')]
        [string] $CloudType,

        [Parameter()]
        [uri] $Proxy
    )

    $webClient = New-Object -TypeName 'System.Net.WebClient'
    $webClient.Headers.Add('x-ms-blob-type', 'BlockBlob')
    $jsonlFileName = '{0}#{1}#MdiSetupLog#{2}.jsonl' -f $manifest.manifest.DomainName, $manifest.manifest.DeviceNetBIOSName, $manifest.manifest.RunGuid

    $storageBaseUrl = Resolve-CloudType -CloudType $CloudType -Storage

    $inboundUri = 'https://{0}.blob.core.{1}/inbound/{2}{3}' -f $StorageAccountName, $storageBaseUrl, [uri]::EscapeDataString($jsonlFileName), $SASToken

    $jsonlObject = ConvertTo-Json -InputObject $Manifest.Manifest -Compress
    $jsonLBytes = [System.Text.Encoding]::ASCII.GetBytes($jsonlObject)

    if ($null -ne $Proxy)
    {
        $webProxy = New-Object -TypeName 'System.Net.WebProxy' -ArgumentList $Proxy
        $webClient.Proxy = $webProxy
    }

    try
    {
        $webClient.UploadData($inboundUri, 'PUT', $jsonLBytes)
        Write-Log -Message 'Uploading manifest to storage'
    }
    catch
    {
        Write-Log -Message $_.Exception.Message -TypeName Error
    }
    finally
    {
        if ($null -ne $webClient)
        {
            $webClient.Dispose()
        }
    }
}
function Set-ScriptParameters
{
    [CmdletBinding()]
    param
    (
        [Parameter()]
        [string] $Path
    )

    $returnString = @()

    $fileName = Split-Path -Path $Path -Leaf
    $baseDirectory = Split-Path -Path $Path

    Import-LocalizedData -BindingVariable 'configuration' -BaseDirectory $baseDirectory -FileName $fileName

    $argumentList = $configuration.Keys |
        Where-Object -FilterScript { -not [string]::IsNullOrEmpty($configuration.$_) } |
            Sort-Object

    foreach ($argument in $argumentList)
    {
        $variable = Set-Variable -Name $argument -Value $configuration.$argument -Scope script -PassThru
        $returnString += '`t{0}={1}' -f $variable.Name, $variable.Value
    }

    '{{{0}}}' -f ($returnString -join ', ')
}
function Update-ManifestToOnboard
{
    [CmdletBinding()]
    [OutputType([MdiManifest])]
    param
    (
        [Parameter(Mandatory)]
        $Manifest,

        [Parameter()]
        [switch] $RefreshFiles
    )

    if ($Manifest.manifest.MDI.Agent.RequiresInstall)
    {
        if (-not $Manifest.manifest.PreReq.LogonAsAService.IsOk)
        {
            Write-Log -Message 'The MDI Service account does not have the Logon as a Service User right.' -TypeName 'Error'
        }
    }
    return $manifest
}
function Write-Log
{
    [CmdletBinding(DefaultParameterSetName = 'OutLog')]
    param
    (
        [Parameter(Mandatory, ParameterSetName = 'OutLog')]
        [Parameter(Mandatory, ParameterSetName = 'OutHost')]
        [string] $Message,

        [Parameter()]
        [ValidateSet('Normal', 'Warning', 'Error')]
        [string] $TypeName = 'Normal',

        [Parameter(ParameterSetName = 'OutHost')]
        [ValidateSet('Gray', 'Green', 'Red', 'White', 'Yellow')]
        [System.ConsoleColor] $ForegroundColor,

        [Parameter(ParameterSetName = 'OutHost')]
        [switch] $PrependNewLine,

        [Parameter(ParameterSetName = 'OutHost')]
        [switch] $AppendNewLine,

        [Parameter(ParameterSetName = 'OutLog')]
        [string] $LogFileRootPath = $env:SystemRoot
    )

    if ($WhatIfPreference.isPresent)
    {
        return
    }

    $existingScanTime = (Get-Variable -Scope 'Script' -Name scriptStartTime -ErrorAction SilentlyContinue).Value

    $logPath = '{0}\Temp\MSS\MDISetup' -f $LogFileRootPath
    if (-not (Test-Path $logPath)) {
        New-Item -type Directory $logPath -force -ErrorAction SilentlyContinue | out-null
    }
    if ([string]::IsNullOrEmpty($existingScanTime))
    {
        $script:scriptStartTime = Get-Date -Format 'MMddHHmm'

        # Since we are creating a new file name, clean up any previous logs.
        Get-ChildItem -Path $logPath |
            Where-Object { $_.Name -match 'MDISetup-\d{8}.log' } |
                Remove-Item -Force
    }

    if ([string]::IsNullOrEmpty($script:moduleDocsPath))
    {

        $logFile = Join-Path -Path $logPath -ChildPath ('MDISetup-{0}.log' -f $script:scriptStartTime)
    }
    else
    {
        $logPath = $script:moduleDocsPath
        $logFile = Join-Path -Path $logPath -ChildPath 'MDISetup.log'
    }

    if (-not (Test-Path -Path $logPath))
    {
        New-Item -Path $logPath -ItemType 'Directory' | Out-Null
    }

    if (-not [string]::IsNullOrEmpty($ForegroundColor))
    {
        $previousForegroundColor = $host.UI.RawUI.ForegroundColor
        $host.UI.RawUI.ForegroundColor = $ForegroundColor

        if ($PrependNewLine)
        {
            Write-Information -MessageData `n -InformationAction Continue
        }

        Write-Information -MessageData $Message -InformationAction Continue

        if ($AppendNewLine)
        {
            Write-Information -MessageData `n -InformationAction Continue
        }

        $host.UI.RawUI.ForegroundColor = $previousForegroundColor
    }

    $typeNameMap = @{
        Normal  = 1
        Warning = 2
        Error   = 3
    }

    $logHeader = '<![LOG[{0}]LOG]!>' -f $Message
    $logEntry = @(
        '<time="{0}"' -f (Get-Date -Format 'HH:mm:ss.ffffff')
        'date="{0}"' -f (Get-Date -Format 'M-d-yyyy')
        'component="{0}"' -f ((Get-PSCallStack)[1]).Command
        'context="{0}"' -f [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        'type="{0}"' -f $typeNameMap[$TypeName]
        'thread="{0}"' -f [Threading.Thread]::CurrentThread.ManagedThreadId
        'file="{0}">' -f (Split-Path -Path $MyInvocation.ScriptName -Leaf)
    )

    $content = $logEntry -join ' '
    $content = $logHeader + $content
    Add-Content -Path $logFile -Value $content -WhatIf:$false
}
function Get-ReturnCode {
    [CmdletBinding()]
    [OutputType([int], ParameterSetName = ('code', 'string2code'))]
    [OutputType([string], ParameterSetName = 'code2string')]
    param
    (
        [Parameter(ParameterSetName = 'code', Mandatory = $true)]
        [MdiManifest] $Manifest,

        [Parameter(ParameterSetName = 'code2string', Mandatory = $true)]
        [int] $ErrorCode,

        [Parameter(ParameterSetName = 'string2code', Mandatory = $true)]
        [string] $ErrorString
    )

    # https://docs.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_arithmetic_operators?view=powershell-7.2#bitwise-operators
    # Beginning in PowerShell 2.0, all bitwise operators work with 64-bit integers.

    $errorStringTable = @{
        'success'                     = 0x00000000 # 0  (0
        #''                           = 0x00000001 # 1  (1
        'missing_param_in_config'     = 0x00000002 # 2  (2
        'installer_invalid_signature' = 0x00000004 # 3  (4
        'reboot_required'             = 0x00000008 # 4  (8
        'notOk_logonAsAService'       = 0x00000010 # 5  (16
        #''       = 0x00000020 # 6  (32
        'notOk_CertificateAutoUpdate' = 0x00000040 # 7  (64
        'notOk_NtlmAuditing'          = 0x00000080 # 8  (128
        'notOk_CipherSuite'           = 0x00000100 # 9  (256
        'notOk_Certificates'          = 0x00000200 # 10 (512
        'notOk_AuditPolicies'         = 0x00000400 # 11 (1024
        'notOk_Sysvol'                = 0x00000800 # 12 (2048
        'notOk_PerformanceCounter'    = 0x00001000 # 13 (4096
        'notOk_CpuScheduler'          = 0x00002000 # 14 (8192
        'notOk_PowerPlan'             = 0x00004000 # 15 (16384
        'notOk_DotNet'                = 0x00008000 # 16 (32768
        'install_failed'              = 0x00010000 # 17 (65536
        'notOk_SensorVersion'         = 0x00020000 # 18 (131072
        'notOk_Hardware'              = 0x00040000 # 19 (262144
        'sensor_NotRunning'           = 0x00080000 # 20 (524288
        #''                           = 0x00100000 # 21 (1048576
        #''                           = 0x00200000 # 22 (2097152
        #''                           = 0x00400000 # 23 (4194304
        #''                           = 0x00800000 # 24 (8388608
        'mySourcePath_missing'        = 0x01000000 # 25 (16777216
        #''                           = 0x02000000 # 26 (33554432
        #''                           = 0x04000000 # 27 (67108864
        #''                           = 0x08000000 # 28 (134217728
        #''                           = 0x10000000 # 29 (268435456
        'manifest_error'              = 0x20000000 # 30 (536870912
        'Unknown_Failure'             = 0x40000000 # 31 (1073741824
    }

    if ($PSCmdlet.ParameterSetName -eq 'string2code')
    {
        $errorStringTable[$ErrorString]
    }
    elseif ($PSCmdlet.ParameterSetName -eq 'code2string')
    {
        $errorCodeTable = @{}

        foreach ($key in $errorStringTable.Keys)
        {
            $errorCodeTable.add($errorStringTable[$key], $key)
        }

        if ($ErrorCode -eq 0)
        {
            $errorCodeTable[0]
        }

        foreach ($errorCodeBit in $errorStringTable.Values)
        {
            if ($ErrorCode -band $errorCodeBit)
            {
                $errorCodeTable[$errorCodeBit]
            }
        }
    }
    else
    {
        [int] $returnErrorCode = 0

        foreach ($preReq in $manifest.Manifest.PreReq.GetEnumerator() |
                Where-Object -FilterScript { -not $PSItem.Value.IsOk -and $PSItem.key -ne 'Hardware' })
        {
            $returnErrorCode = $returnErrorCode + $errorStringTable[('notOk_{0}' -f $preReq.Name)]
        }

        if ($Manifest.Manifest.MDI.Agent.ExitCode -eq 3010)
        {
            if (($returnErrorCode -band $errorStringTable['reboot_required']) -eq 0)
            {
                $returnErrorCode = $returnErrorCode + $errorStringTable['reboot_required']
            }
        }
        elseif ($Manifest.Manifest.MDI.Agent.ExitCode -eq 191)
        {
            if (($returnErrorCode -band $errorStringTable['installer_invalid_signature']) -eq 0)
            {
                $returnErrorCode = $returnErrorCode + $errorStringTable['installer_invalid_signature']
            }
        }
        elseif ($Manifest.Manifest.MDI.Agent.InstallState -ne 'current')
        {
            $returnErrorCode = $returnErrorCode + $errorStringTable['install_failed']
        }

        if (-not $Manifest.Manifest.MDI.Agent.IsRunning)
        {
            $returnErrorCode = $returnErrorCode + $errorStringTable['sensor_NotRunning']
        }

        $returnErrorCode
    }
}
function Get-MdiAdvancedAuditing
{
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingInvokeExpression', '', Justification = 'Inputs are restricted.')]
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    $expectedAuditing = @'
Policy Target,Subcategory,Subcategory GUID,Inclusion Setting,Setting Value
System,Security System Extension,{0CCE9211-69AE-11D9-BED3-505054503030},Success and Failure,3
System,Distribution Group Management,{0CCE9238-69AE-11D9-BED3-505054503030},Success and Failure,3
System,Security Group Management,{0CCE9237-69AE-11D9-BED3-505054503030},Success and Failure,3
System,Computer Account Management,{0CCE9236-69AE-11D9-BED3-505054503030},Success and Failure,3
System,User Account Management,{0CCE9235-69AE-11D9-BED3-505054503030},Success and Failure,3
System,Directory Service Access,{0CCE923B-69AE-11D9-BED3-505054503030},Success and Failure,3
System,Directory Service Changes,{0CCE923C-69AE-11D9-BED3-505054503030},Success and Failure,3
System,Credential Validation,{0CCE923F-69AE-11D9-BED3-505054503030},Success and Failure,3
'@ | ConvertFrom-Csv
    $properties = ($expectedAuditing | Get-Member -MemberType NoteProperty).Name

    $localFile = 'C:\Windows\Temp\mdi-{0}.csv' -f [guid]::NewGuid().Guid
    $commandLine = 'cmd.exe /c auditpol.exe /backup /file:{0}' -f $localFile

    if ($ComputerName)
    {
        $output = Invoke-mdiRemoteCommand -ComputerName $ComputerName -CommandLine $commandLine -LocalFile $localFile
    }
    else
    {
        try
        {
            $null = Invoke-Expression $commandLine
            $output = Get-Content -Path $localFile -ErrorAction Stop
            Remove-Item -Path $localFile -Force
        }
        catch
        {
            throw $PSItem
        }
    }

    if ($output -and $output.Count -gt 1)
    {
        $advancedAuditing = $output | ConvertFrom-Csv | Where-Object {
            $_.Subcategory -in $expectedAuditing.Subcategory
        } | Select-Object -Property $properties

        $compareParams = @{
            ReferenceObject  = $expectedAuditing
            DifferenceObject = $advancedAuditing
            Property         = $properties
        }
        $isAdvancedAuditingOk = $null -eq (Compare-Object @compareParams)
        $return = [pscustomobject]@{
            IsOk    = $isAdvancedAuditingOk
            Details = $advancedAuditing
        }
    }
    else
    {
        $return = [pscustomobject]@{
            IsOk    = $false
            Details = 'Unable to get the advanced auditing settings remotely'
        }
    }
    $return
}
function Get-MdiCertificateAutoUpdate
{
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    $expectedRegistrySet = 'Software\Policies\Microsoft\SystemCertificates\AuthRoot,DisableRootAutoUpdate,0'

    if ($ComputerName)
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet -ComputerName $ComputerName
    }
    else
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet
    }

    return [pscustomobject]@{
        IsOk    = $details.value -ne 1
        Details = $details.value
    }
}
function Get-MdiCertificateReadiness
{
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName,

        [Parameter(Mandatory)]
        [ValidateSet('Commercial', 'GCC', 'GCC-H', 'DOD')]
        [string] $CloudType
    )

    $expectedRootCertificates = @{
        Base      = @{
            Name       = 'Baltimore CyberTrust Root'
            Thumbprint = 'D4DE20D05E66FC53FE1A50882C78DB2852CAE474'
            Found      = $false # All customers, Baltimore CyberTrust Root
        }
        CloudType = @{
            Name       = ''
            Thumbprint = ''
            Found      = $false
        }
    }

    if ($CloudType -eq 'Commercial')
    {
        $expectedRootCertificates.CloudType.Name = 'DigiCert Global Root G2'
        $expectedRootCertificates.CloudType.Thumbprint = 'DF3C24F9BFD666761B268073FE06D1CC8D4F82A4' # Commercial
    }
    else
    {
        $expectedRootCertificates.CloudType.Name = 'DigiCert Global Root CA'
        $expectedRootCertificates.CloudType.Thumbprint = 'A8985D3A65E5E5C4B2D7D66D40C6DD2FB19C5436' # USGov
    }

    if ($ComputerName)
    {
        $store = New-Object -TypeName 'System.Security.Cryptography.X509Certificates.X509Store' -ArgumentList (
            "\\$ComputerName\Root", [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine
        )
    }
    else
    {
        $store = New-Object -TypeName 'System.Security.Cryptography.X509Certificates.X509Store' -ArgumentList (
            'Root', [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine
        )
    }

    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    $details = $store.Certificates
    $store.Close()

    $isOk = $true

    $expectedRootCertificates.GetEnumerator() | ForEach-Object {
        $PSitem.Value['Found'] = if ($details.Thumbprint -contains $PSitem.Value['Thumbprint'])
        {
            $true
        }
        else
        {
            $false
        }
    }

    return [pscustomobject]@{
        IsOk    = $isOk
        Details = $expectedRootCertificates
    }
}
function Get-MdiCipherSuiteOrder
{
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    $expectedRegistrySet = 'SOFTWARE\Policies\Microsoft\Cryptography\Configuration\SSL\00010002,Functions,0'

    if ($ComputerName)
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet -ComputerName $ComputerName
    }
    else
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet
    }

    return [pscustomobject]@{
        IsOk    = $null -eq $details.value -or ($details.value).split(',')[0] -eq 'TLS_DHE_RSA_WITH_AES_256_GCM_SHA384'
        Details = $details.value
    }
}
function Get-MdiCpuSchedulerManifest
{
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    $expectedRegistrySet = 'SYSTEM\CurrentControlSet\Control\Session Manager\Quota System,EnableCpuQuota,0'

    if ($ComputerName)
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet -ComputerName $ComputerName
    }
    else
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet
    }

    return [pscustomobject]@{
        IsOk    = ($null -eq $details.value) -or ($details.value -eq 0)
        Details = $details.value
    }
}
function Get-MdiDotNetVersion
{
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    $expectedRegistrySet = 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full,Version,4.7'

    if ($ComputerName)
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet -ComputerName $ComputerName
    }
    else
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet
    }

    return [pscustomobject]@{
        IsOk    = [version] $details.value -gt [version]'4.7'
        Details = $details.value
    }
}
function Get-MdiLogonAsAService
{
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName,

        [Parameter(Mandatory)]
        [string] $Identity
        
    )

    $secpolParams = @{
        Area = 'USER_RIGHTS'
    }

    if ($ComputerName)
    {
        $secpolParams.Add('ComputerName', $ComputerName)
    }

    $policyConfiguration = Get-MDISecurityPolicy @secpolParams

    if ($policyConfiguration.ContainsKey('SeServiceLogonRight'))
    {
        $logonRights = ($policyConfiguration['SeServiceLogonRight']).Trim() -split ','
    }
    else
    {
        $logonRights = $null
    }
    $logonRightsUserName = @()
    $forestFqdn = (Get-AdForest).RootDomain
    $domainSid = (get-addomain).domainsid.value
    foreach($principal in $logonRights) {
        if ($principal -match '^S\-1\-5.*') {
            if ($principal.StartsWith($domainSid)) {
                $username = (get-adobject -LDAPFilter ('(objectSid={0})' -f $logonRights[1]) -Properties *).samaccountname
            } else {
                $username = (get-adobject -LDAPFilter ('(objectSid={0})' -f $logonRights[1]) -Properties * -Server $forestFqdn).samaccountname
            }
        }
        $logonRightsUserName += $username
    }
    if ($logonRightsUserName -contains $Identity)
    {
        $isOk = $true
    }
    else
    {
        foreach ($group in $logonRights.Where({ $PSItem -notlike 'S-1-*' }))
        {
            $principal = Get-ADObject -Filter ('Name -eq "{0}"' -f $group)

            if ($principal.ObjectClass -eq 'group')
            {
                if ((Get-ADGroupMember -Identity $principal.distinguishedName -Recursive).samAccountName -contains $Identity)
                {
                    $isOk = $true
                    break
                }
            }
        }

        # if there is nothing to search for, return false
        $isOk = $false
    }

    return [pscustomobject]@{
        IsOk    = $isOk
        Details = $logonRights
    }
}
function Get-MdiMachineType
{
    [CmdletBinding()]
    [OutputType([string])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    try
    {
        $csiParams = @{
            Namespace   = 'root\cimv2'
            Class       = 'Win32_ComputerSystem'
            Property    = 'Model', 'Manufacturer'
            ErrorAction = 'SilentlyContinue'
        }

        if ($ComputerName)
        {
            $csiParams.Add('ComputerName', $ComputerName)
        }

        $csi = Get-CimInstance @csiParams
        $return = switch ($csi.Model)
        {
            { $_ -eq 'Virtual Machine' }
            {
                'Hyper-V'; break
            }
            { $_ -match 'VMware|VirtualBox' }
            {
                $_; break
            }
            default
            {
                switch ($csi.Manufacturer)
                {
                    { $_ -match 'Xen|Google' }
                    {
                        $_; break
                    }
                    { $_ -match 'QEMU' }
                    {
                        'KVM'; break
                    }
                    { $_ -eq 'Microsoft Corporation' }
                    {
                        $azgaParams = @{
                            ComputerName = $ComputerName
                            Namespace    = 'root\cimv2'
                            Class        = 'Win32_Service'
                            Filter       = "Name = 'WindowsAzureGuestAgent'"
                            ErrorAction  = 'SilentlyContinue'
                        }
                        if (Get-CimInstance @azgaParams)
                        {
                            'Azure'
                        }
                        else
                        {
                            'Hyper-V'
                        }
                        break
                    }
                    default
                    {
                        $cspParams = @{
                            ComputerName = $ComputerName
                            Namespace    = 'root\cimv2'
                            Class        = 'Win32_ComputerSystemProduct'
                            Property     = 'uuid'
                            ErrorAction  = 'SilentlyContinue'
                        }
                        $uuid = (Get-CimInstance @cspParams).UUID
                        if ($uuid -match '^EC2')
                        {
                            'AWS'
                        }
                        else
                        {
                            'Physical'
                        }
                    }
                }
            }
        }
    }
    catch
    {
        $return = $_.Exception.Message
    }
    $return
}
function Get-MdiNtlmAuditing
{
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    $expectedRegistrySet = @(
        'System\CurrentControlSet\Control\Lsa\MSV1_0,AuditReceivingNTLMTraffic,2',
        'System\CurrentControlSet\Control\Lsa\MSV1_0,RestrictSendingNTLMTraffic,1|2',
        'System\CurrentControlSet\Services\Netlogon\Parameters,AuditNTLMInDomain,7'
    )

    if ($ComputerName)
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet -ComputerName $ComputerName
    }
    else
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet
    }

    return [pscustomobject]@{
        IsOk    = @($details | Where-Object { $_.value -notmatch $_.expectedValue }).Count -eq 0
        Details = $details | Select-Object regKey, value
    }
}
function Get-MdiOSVersion
{
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    try
    {
        $osParams = @{
            Namespace   = 'root\cimv2'
            Class       = 'Win32_OperatingSystem'
            Property    = 'Version', 'Caption', 'ProductType'
            ErrorAction = 'SilentlyContinue'
        }

        if ($ComputerName)
        {
            $osParams.Add('ComputerName', $ComputerName)
        }

        $os = Get-CimInstance @osParams
        return [pscustomobject]@{
            IsOk    = [version]($os.Version) -ge [version]('6.3') -and $os.ProductType -gt 1
            Details = [pscustomobject]@{
                Caption     = $os.Caption
                Version     = $os.Version
                ProductType = $os.ProductType
            }
        }
    }
    catch
    {
        return [pscustomobject]@{
            IsOk    = $false
            Details = [pscustomobject]@{
                Caption = 'N/A'
                Version = 'N/A'
            }
        }
    }
}
function Get-MdiPerformanceCounter
{
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingInvokeExpression', '', Justification = 'Inputs are restricted.')]
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    $expectedRegistrySet = @(
        'System\CurrentControlSet\Services\PerfOs\Performance,Disable Performance Counters,1',
        'System\CurrentControlSet\Services\PerfProc\Performance,Disable Performance Counters,1',
        'System\CurrentControlSet\Services\PerfDisk\Performance,Disable Performance Counters,1',
        'System\CurrentControlSet\Services\PerfNet\Performance,Disable Performance Counters,1'
    )

    if ($ComputerName)
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet -ComputerName $ComputerName
    }
    else
    {
        $details = Get-MdiRegistryValueSet -ExpectedRegistrySet $expectedRegistrySet
    }

    $countersDisabled = @($details | Where-Object { $PSItem.value -notmatch $PSItem.expectedValue }).Count -eq 0
    $details = $details | Select-Object regKey, value

    $command = '@{
        ProcessorInformation  = [System.Diagnostics.PerformanceCounterCategory]::Exists(''Processor Information'');
        ProcessorPercent      = [System.Diagnostics.PerformanceCounterCategory]::CounterExists(''% Processor Utility'',''Processor Information'');
        ProcessorTotal        = [System.Diagnostics.PerformanceCounterCategory]::InstanceExists(''_Total'',''Processor Information'');
        NetworkInterface      = [System.Diagnostics.PerformanceCounterCategory]::Exists(''Network Interface'');
        NetworkAdapter        = [System.Diagnostics.PerformanceCounterCategory]::Exists(''Network Adapter'');
        NetworkAdapterPackets = [System.Diagnostics.PerformanceCounterCategory]::CounterExists(''Packets/sec'',''Network Adapter'')
    } | ConvertTo-Json'

    $commandLine = 'PowerShell -WindowStyle Hidden -Command "& {{{0}}}"' -f $command

    if ($ComputerName)
    {
        $counterDetails = Invoke-MDIRemoteCommand -ComputerName $ComputerName -CommandLine $commandLine
    }
    else
    {
        $counterDetails = Invoke-Expression -Command $command
    }

    #run the assessment
    # -AsHashtable is not available in PSv5, so just make our own
    $counterDetailsHt = @{}
    ($counterDetails | ConvertFrom-Json).psobject.properties |
        ForEach-Object { $counterDetailsHt.Add($PSItem.Name, $PSItem.Value) }

    $bustedCounters = @($counterDetailsHt.GetEnumerator() |
            Where-Object { $PSItem.Value -ne $true }
    )

    return [pscustomobject]@{
        IsOk    = -not $countersDisabled -and $bustedCounters.count -eq 0
        Details = @{
            countersDisabledDetails = $details
            counterStatusDetails    = $counterDetails
        }
    }
}
function Get-MdiPowerScheme
{
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingInvokeExpression', '', Justification = 'Inputs are restricted.')]
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    $commandLine = 'cmd.exe /c %windir%\system32\powercfg.exe /getactivescheme'

    if ($ComputerName)
    {
        $details = Invoke-MdiRemoteCommand -ComputerName $ComputerName -CommandLine $commandLine
    }
    else
    {
        $details = Invoke-Expression $commandLine
    }

    if ($details -match ':\s+(?<guid>[a-fA-F0-9]{8}[-]?([a-fA-F0-9]{4}[-]?){3}[a-fA-F0-9]{12})\s+\((?<name>.*)\)')
    {
        return [pscustomobject]@{
            IsOk    = $Matches.guid -eq '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
            Details = $details
        }
    }

    return [pscustomobject]@{
        IsOk    = $false
        Details = $details
    }
}
function Get-MdiRegistryValueSet
{
    [CmdletBinding()]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName,

        [Parameter(Mandatory)]
        [string[]] $ExpectedRegistrySet
    )

    if ($ComputerName -and ($ComputerName -ne 'localhost'))
    {
        $hklm = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', $ComputerName, 'Registry64')
    }
    else
    {
        $hklm = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Registry64')
    }

    $details = foreach ($reg in $ExpectedRegistrySet)
    {
        $regKeyPath, $regValue, $expectedValue = $reg -split ','
        $regKey = $hklm.OpenSubKey($regKeyPath)
        try
        {
            $value = $regKey.GetValue($regValue)
        }
        catch
        {
            $value = $null
        }

        [pscustomobject]@{
            regKey        = '{0}\{1}' -f $regKeyPath, $regValue
            value         = $value
            expectedValue = $expectedValue
        }
    }

    $hklm.Close()
    $details
}
function Get-MDISecurityPolicy
{
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingInvokeExpression', '', Justification = 'Inputs are restricted.')]
    [OutputType([Hashtable])]
    [CmdletBinding()]
    param
    (
        [Parameter(Mandatory = $true)]
        [ValidateSet('SECURITYPOLICY', 'GROUP_MGMT', 'USER_RIGHTS', 'REGKEYS', 'FILESTORE', 'SERVICES')]
        [string] $Area,

        [Parameter()]
        [string] $ComputerName
    )

    $currentSecurityPolicyFilePath = ('{0}\Temp\SecurityPolicy.inf' -f $env:windir)

    $commandLine = "secedit.exe /export /cfg $currentSecurityPolicyFilePath /areas $Area"

    if ($ComputerName)
    {
        $details = Invoke-MdiRemoteCommand -ComputerName $ComputerName -CommandLine $commandLine -LocalFile $currentSecurityPolicyFilePath
    }
    else
    {
        $null = Invoke-Expression $commandLine
        $details = Get-Content -Path $currentSecurityPolicyFilePath
        Remove-Item -Path $currentSecurityPolicyFilePath
    }

    $policyConfiguration = @{}
    switch -regex ($details)
    {
        '^\[(.+)\]' # Section
        {
            $section = $matches[1]
            $policyConfiguration[$section] = @{}
            $CommentCount = 0
        }
        '^(;.*)$' # Comment
        {
            $value = $matches[1]
            $commentCount = $commentCount + 1
            $name = 'Comment' + $commentCount
            $policyConfiguration[$section][$name] = $value
        }
        '(.+?)\s*=(.*)' # Key
        {
            $name, $value = $matches[1..2] -replace '\*'
            $policyConfiguration[$section][$name] = $value
        }
    }

    Switch ($Area)
    {
        'USER_RIGHTS'
        {
            return $policyConfiguration.'Privilege Rights'
        }
        Default
        {
            return $policyConfiguration
        }
    }
}
function Get-MdiSensorVersion
{
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )
    try
    {
        $serviceParams = @{
            Namespace   = 'root\cimv2'
            Class       = 'Win32_Service'
            Property    = 'Name', 'PathName', 'State'
            Filter      = "Name = 'AATPSensor'"
            ErrorAction = 'SilentlyContinue'
        }

        if ($ComputerName)
        {
            $serviceParams.Add('ComputerName', $ComputerName)
        }

        $service = Get-CimInstance @serviceParams

        if ($service)
        {
            $versionParams = @{
                Namespace   = 'root\cimv2'
                Class       = 'CIM_DataFile'
                Property    = 'Version'
                Filter      = 'Name={0}' -f ($service.PathName -replace '\\', '\\')
                ErrorAction = 'SilentlyContinue'
            }

            if ($ComputerName)
            {
                $versionParams.Add('ComputerName', $ComputerName)
            }

            $details = (Get-CimInstance @versionParams).Version
        }
        else
        {
            $details = 'N/A'
        }

        return [pscustomobject]@{
            isOk    = $details -ge 2
            details = $details
        }

    }
    catch
    {
        return [pscustomobject]@{
            isOk    = $false
            details = $PSItem.Exception.Message
        }
    }
}
function Get-MdiServerRequirements
{
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    try
    {
        $csiParams = @{
            Namespace   = 'root\cimv2'
            Class       = 'Win32_ComputerSystem'
            Property    = 'NumberOfLogicalProcessors', 'TotalPhysicalMemory'
            ErrorAction = 'SilentlyContinue'
        }

        if ($ComputerName)
        {
            $csiParams.Add('ComputerName', $ComputerName)
        }

        $csi = Get-CimInstance @csiParams

        $osParams = @{
            Namespace   = 'root\cimv2'
            Class       = 'Win32_OperatingSystem'
            Property    = 'SystemDrive'
            ErrorAction = 'SilentlyContinue'
        }

        if ($ComputerName)
        {
            $osParams.Add('ComputerName', $ComputerName)
        }

        $osdiskParams = @{
            Namespace   = 'root\cimv2'
            Class       = 'Win32_LogicalDisk'
            Property    = 'FreeSpace', 'DeviceID'
            Filter      = "DeviceID = '{0}'" -f (Get-CimInstance @osParams).SystemDrive
            ErrorAction = 'SilentlyContinue'
        }

        if ($ComputerName)
        {
            $osdiskParams.Add('ComputerName', $ComputerName)
        }

        $osdisk = Get-CimInstance @osdiskParams

        $minRequirements = @{
            NumberOfLogicalProcessors = 2
            TotalPhysicalMemory       = 6gb - 1mb
            OsDiskFreeSpace           = 6gb
        }

        $return = [pscustomobject]@{
            isOk    = (
                $csi.NumberOfLogicalProcessors -ge $minRequirements.NumberOfLogicalProcessors -and
                $csi.TotalPhysicalMemory -ge $minRequirements.TotalPhysicalMemory -and
                $osdisk.FreeSpace -ge $minRequirements.OsDiskFreeSpace
            )
            details = [pscustomobject]@{
                NumberOfLogicalProcessors = $csi.NumberOfLogicalProcessors
                TotalPhysicalMemory       = $csi.TotalPhysicalMemory
                OsDiskDeviceID            = $osdisk.DeviceID
                OsDiskFreeSpace           = $osdisk.FreeSpace
            }
        }
    }
    catch
    {
        $return = [pscustomobject]@{
            isOk    = $false
            details = $PSItem.Exception.Message
        }
    }
    $return
}
function Get-MdiSysvolReady
{
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingInvokeExpression', '', Justification = 'Inputs are restricted.')]
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param
    (
        [Parameter()]
        [AllowNull()]
        [string] $ComputerName
    )

    $expectedRegistrySet = 'SYSTEM\CurrentControlSet\Services\Netlogon\Parameters,SysvolReady,1'
    $sysvolReady = Get-MdiRegistryValueSet -ComputerName $ComputerName -ExpectedRegistrySet $expectedRegistrySet

    $command = '(Get-SmbShare -Name ''sysvol'').path'
    $commandLine = 'PowerShell -WindowStyle Hidden -Command "& {{{0}}}"' -f $command
    try {
        if ($ComputerName)
        {
            $sysvolPath = Invoke-MdiRemoteCommand -ComputerName $ComputerName -CommandLine $commandLine
        }
        else
        {
            $sysvolPath = Invoke-Expression $command
        }
    } catch {
        $sysvolPath = $null
    }

    return [pscustomobject]@{
        IsOk    = $sysvolReady.value -eq 1
        Details = @{
            SysvolPath  = $sysvolPath
            sysvolReady = $sysvolReady.value
        }
    }
}
function Invoke-MdiOnboard {
    [CmdletBinding()]
    [OutputType([void])]
    param
    (
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $AccessKey,
        [Parameter(Mandatory=$false)]
        [switch]$IgnoreNPCAP
    )
    #check if sensor exists
    if (-not $(Test-MDIIsInstalled)) {
        try {
            Write-Log -Message 'Starting MDI setup'
            $IgnoreNPCAP = $IgnoreNPCAP -or (Get-Variable -Name IgnoreNPCAP).value
            if ($IgnoreNPCAP) {
                Write-Log -Message 'IgnoreNPCAP is TRUE'
            }
            $manifest = [MdiManifest]::new()
            try {
                $manifest = Initialize-Manifest -Identity $Identity -CloudType $CloudType -Manifest $manifest
            } catch {
                Write-Log -Message ('Stopping MDISetup. Exit Code: {0}, {1}' -f '30', 'manifest_error')
                throw
            }
            if ([string]::IsNullOrEmpty($AccessKey)) {
                Write-Log -Message ('Stopping MDISetup. Error reading AccessKey. Exit Code: {0}, {1}' -f '2', 'missing_param_in_config')
                $Manifest.Manifest.MDI.Agent.ExitCode = 2
                $manifest.Manifest.ReturnCode = Get-ReturnCode -Manifest $manifest
                throw
            }
            try {
                $myDestPath = '{0}\{1}' -f ((Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' -Name 'myDestPath' -ErrorAction 'SilentlyContinue').MyDestPath), "mdisetup"
                $mySourcePath = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' -Name 'mySourcePath' -ErrorAction 'SilentlyContinue').MySourcePath
            } catch {

            }
            Set-MdiServerReadiness -IgnoreNPCAP:$IgnoreNPCAP
            $serverReady = Get-MdiServerReadiness -IgnoreNPCAP:$IgnoreNPCAP
            Write-Log -Message ('Readiness report: {0}' -f $($serverReady.tostring()))
            if ($serverReady.isReady) {
                $manifest.manifest.MDI.ScriptVersion = '202506'
                $parameterSource = 'Configuration File'
                $parameterMessage = Set-ScriptParameters -Path "$PSScriptRoot\config.psd1"
                Write-Log -Message ('Starting MDI setup : {0} with {1}' -f '202506', $parameterSource)
                Write-Log -Message ('Setup started with arguments: {0}' -f ($parameterMessage -replace '(?<=SASToken|AccessKey=)(.*)(?=,)', '<Redacted>'))
                Write-Log -Message ('Running on {0} : {1}' -f $manifest.manifest.DeviceNetBIOSName, $manifest.manifest.ManifestName)
                $testConnectionParams = @{}
                
                if ($mySourcePath) {
                $path = $("$mySourcePath\{0}" -f "mdisetup")
                } else {
                    $path = $PSScriptRoot
                }
                $testConnectionParams.Add("path",$path)
                if (-not [string]::IsNullOrEmpty($Proxy)){
                    $testConnectionParams.Add("proxyUrl",$Proxy)
                }
                if (Test-MDISensorApiConnection @testConnectionParams) {
                    Write-Log -Message ('Successfully connected to API Endpoint')
                } else {
                    Write-Log -Message ('Failed to connect to API Endpoint') -TypeName Error
                }
                #purge tickets
                $null = & "$($env:SystemRoot)\system32\cmd.exe" @('/c','klist -li 0x3e7 purge')
                Write-Log -Message ('Purging Kerberos tickets')
                $mdiInstallString ='"{0}\Azure ATP sensor Setup.exe" /quiet NetFrameworkCommandLineArguments="/q" AccessKey="{1}"' -f $path,$AccessKey
                if (-not [string]::IsNullOrEmpty($Proxy)) {
                    $mdiInstallString = $mdiInstallString + (' ProxyUrl="{0}"' -f $Proxy)
                } 
                Write-Log -Message ('Installing MDI with command: {0}' -f ($mdiInstallString.Replace($AccessKey,"Redacted")))
                $c = 'cmd /c '+$mdiInstallString
                $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
                try {
                    $mdiCommand = Invoke-Expression $command
                    $Manifest.Manifest.MDI.Agent.InstallState = 'current'
                } catch {
                    Write-Log -Message $_.ExceptionMessage -TypeName 'Error'
                    $Manifest.Manifest.MDI.Agent.InstallState = 'failed'
                    return
                }
                $mdiStatus = Get-Service -Name 'AATPSensor' -ErrorAction 'SilentlyContinue'
                if ($mdiStatus -ne $null) {
                    $x = 0
                    $sleepSeconds = 10
                    while (($mdiStatus.Status -ne 'Running') -and ($x -le 20))
                    {
                        Write-Log -Message ('Waiting for MDI service to start. Sleeping for {0} seconds.' -f $sleepSeconds)
                        $mdiStatus = Get-Service -Name 'AATPSensor' -ErrorAction 'SilentlyContinue'
                        $x++
                        Start-Sleep -Seconds $sleepSeconds
                    }
                    if ($mdiStatus.Status -ne 'Running')
                    {
                        $Manifest.Manifest.MDI.Agent.IsRunning = $False
                        Write-Log -Message 'MDI service failed to start.' -TypeName 'Error'
                        Write-Log -Message 'View logfiles at C:\Program Files\Azure Advanced Threat Protection Sensor\<VERSION>\Logs'
                    }
                    else
                    {
                        $Manifest.Manifest.MDI.Agent.IsRunning = $true
                        $Manifest.Manifest.MDI.Agent.ExitCode = 0
                        Write-Log -Message ('MDI service is {0}' -f $mdiStatus.Status)
                    }
                } else {
                    Write-Log -Message ('MDI Service AATPSensor not found') -TypeName 'Error'
                }
                $manifest.Manifest.ReturnCode = Get-ReturnCode -Manifest $manifest
                if ($manifest.Manifest.ReturnCode -ne 0)
                {
                    $manifest.Manifest.Error = $true
                }
                Write-Log -Message ('Stopping MDISetup. Exit Code: {0}' -f $manifest.Manifest.returnCode)
            } else {
                Write-Log -Message ('Server reports not ready for MDI installation: {0}' -f $($serverReady.ToString())) -TypeName 'Error'
            }
        } catch {
            Write-Log -Message ('Stopping MDISetup. Exit Code: {0}' -f $manifest.Manifest.returnCode)
        } finally {
            $manifest = Update-ManifestToOnboard -Manifest $manifest
            if (![string]::IsNullOrEmpty($StorageAccountName) -and ![string]::IsNullOrEmpty($SASToken))
            {
                Send-OnboardingLogFile -StorageAccountName $StorageAccountName -Manifest $manifest -SASToken $SASToken -CloudType $CloudType -Proxy $Proxy
            } else {
                $manifest.Manifest | convertto-json | out-file -FilePath "$env:temp\mdimanifest.json" -force
            }
        }
    }
}
function Invoke-MdiRemoteCommand
{
    param
    (
        [Parameter(Mandatory)]
        [string] $ComputerName,

        [Parameter(Mandatory)]
        [string] $CommandLine,

        [Parameter()]
        [string] $LocalFile = $null
    )

    try
    {
        $cimParams = @{
            ComputerName = $ComputerName
            Namespace    = 'root\cimv2'
            Class        = 'Win32_Process'
            Name         = 'Create'
            ErrorAction  = 'SilentlyContinue'
            Arguments    = @{
                ProcessStartupInformation = (New-CimInstance -CimClass (
                        Get-CimClass -ClassName 'Win32_ProcessStartup') -Property @{ShowWindow = 0 } -ClientOnly)
            }
        }

        if ($LocalFile -eq [string]::Empty)
        {
            $LocalFile = 'C:\Windows\Temp\mdi-{0}.tmp' -f [guid]::NewGuid().GUID
            $cimParams.Arguments.Add('CommandLine', ('{0} 2>&1>{1}' -f $CommandLine, $LocalFile))
        }
        else
        {
            $cimParams.Arguments.Add('CommandLine', $CommandLine)
        }

        $result = Invoke-CimMethod @cimParams
        $maxWait = [datetime]::Now.AddSeconds(15)

        $waitForProcessParams = @{
            ComputerName = $ComputerName
            Namespace    = 'root\cimv2'
            Class        = 'Win32_Process'
            Filter       = ("ProcessId='{0}'" -f $result.ProcessId)
        }

        if ($result.ReturnValue -eq 0)
        {
            do
            {
                Start-Sleep -Milliseconds 200
            }
            while (([datetime]::Now -lt $maxWait) -and (Get-CimInstance @waitForProcessParams).CommandLine -eq $cimParams.Arguments.CommandLine)
        }

        try
        {
            # Read the file using SMB
            $remoteFile = $LocalFile -replace 'C:', ('\\{0}\C$' -f $ComputerName)
            $return = Get-Content -Path $remoteFile -ErrorAction Stop
            Remove-Item -Path $remoteFile -Force -ErrorAction Break
        }
        catch
        {
            try
            {
                # Read the remote file using WMI
                $psmClassParams = @{
                    Namespace    = 'root\Microsoft\Windows\Powershellv3'
                    ClassName    = 'PS_ModuleFile'
                    ComputerName = $ComputerName
                }
                $cimParams = @{
                    CimClass   = Get-CimClass @psmClassParams
                    Property   = @{ InstanceID = $LocalFile }
                    ClientOnly = $true
                }
                $fileInstanceParams = @{
                    InputObject  = New-CimInstance @cimParams
                    ComputerName = $ComputerName
                }
                $fileContents = Get-CimInstance @fileInstanceParams -ErrorAction Stop
                $fileLengthBytes = $fileContents.FileData[0..3]
                [array]::Reverse($fileLengthBytes)
                $fileLength = [BitConverter]::ToUInt32($fileLengthBytes, 0)
                $fileBytes = $fileContents.FileData[4..($fileLength - 1)]
                $localTempFile = [System.IO.Path]::GetTempFileName()
                Set-Content -Value $fileBytes -Encoding Byte -Path $localTempFile
                $return = Get-Content -Path $localTempFile
                Remove-Item -Path $localTempFile -Force
            }
            catch
            {
                $return = $null
            }
        }
    }
    catch
    {
        $return = $_.Exception.Message
    }
    $return
}
function Test-MDIIsInstalled {
    [CmdletBinding()]
    Param(
        [Parameter(mandatory=$false)]
        [string]$server,
        [Parameter(DontShow)]
        [switch]$returnUninstallString=$false
    )
    $returnVal = $false
    $swInventory = @()
    try {
        $apps = @()
        @('Get-ItemProperty "HKLM:\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"','Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"') | % {
            if (-not [string]::IsNullOrEmpty($server)) {
                $command = "Invoke-command -scriptblock {$_} -ComputerName $server"+' 2> $null'
            } else {
                $command = "Invoke-command -scriptblock {$_}"+' 2> $null'
            }
            $apps += invoke-Expression $command
        }
        foreach ($app in $apps) {
            $i = @{}
            $i.Name = $app.DisplayName
            $i.Version = $app.DisplayVersion
            $i.QuietUninstallString = $app.UninstallString
            $swInventory += New-Object PSObject -Property $i
        }
    } catch {
        try {
            if ([string]::IsNullOrEmpty($server)) {
                $server = "localhost"
            }
            [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$server") | out-null
            $reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$server")
            $keysToSearch = @()
            $key32 = $reg.OpenSubKey("SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
            $keysToSearch += $key32
            $key64 = $reg.OpenSubKey("SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
            $keysToSearch += $key64
            foreach ($key in $keysToSearch) {
                $key.GetSubKeyNames() | ForEach-Object {
                    $subkey = $key.OpenSubKey($_)
                    $i = @{}
                    $i.Name = $subkey.GetValue('DisplayName')
                    $i.Version = $subkey.GetValue('DisplayVersion')
                    $i.QuietUninstallString = $subkey.GetValue('UninstallString',"")
                    $swInventory += New-Object PSObject -Property $i
                    $subkey.Close()
                }
                $key.close()
            }
            try {
                $key32.close()
                $key64.close()
            } catch {}
        } catch {
            $errorMessage = $_.Exception
            write-error "$server`: Error reading registry"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Error reading registry - MDI. $errorMessage" -logSev "Error" | out-null
            throw
        }
    } finally {
        if ($reg) {
            $reg.Close()
        }
    }

    $mdiPresent = $false
    if ([bool]($swInventory | ? { $_.name -eq "Azure Advanced Threat Protection Sensor" })){
        $mdiPresent = $true
        $uninstallString = ($swInventory | ? { $_.name -eq "Azure Advanced Threat Protection Sensor" }).QuietUninstallString
        $returnVal = $mdiPresent
    } else {
        $returnVal = $false
    }
    if ($returnUninstallString) {
        return $uninstallString
    } else {
        return $returnVal
    }
}
function Get-MDICipherSuite {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Checks for incompatible cipher suite settings in the registry.
    
    .DESCRIPTION
        Reads registry for TLS ciphers

    .EXAMPLE
        Get-MDICipherSuite

    .OUTPUTS
        Returns false if incompatible suite, else returns true.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    [OutputType([bool])]
    Param(
        [Parameter(mandatory=$false)]
        [string]$server="localhost"
    )
    $returnVal=$false
    try {
        $c = '((Get-ItemProperty -Path Registry::"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Cryptography\Configuration\SSL\00010002").Functions)'
        if ($server -ne "localhost") {
            $command  = "Invoke-command -scriptblock {$c} -ComputerName $server"+' 2> $null'
        } else {
            $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
        }
        $regCheck = iex $command
        if ($regCheck) {
            $regCheck = ($regCheck).split(',')[0]
        }
    } catch {
        try {
            [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$server") | out-null
            $reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$server")
            $key = $reg.OpenSubKey("SOFTWARE\Policies\Microsoft\Cryptography\Configuration\SSL\00010002")
            if ("Functions" -in $key.getvaluenames()) {
                $regCheck = ($key.GetValue("Functions")).split(',')[0]
            } else {
                $returnVal = $true
            }
            $key.close()
            $reg.close()
        } catch {
            $errorMessage = $_.Exception
            write-error "$server`: Error reading registry"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Error reading registry - Cipher. $errorMessage" -logSev "Error" | out-null
            throw
        } 
    } finally {
        if ($reg) {
            $reg.Close()
        }
    }
    if ($regCheck) {
        write-verbose "$server`: Existing first value of HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Cryptography\Configuration\SSL\00010002\Functions is $regCheck"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Existing first value of HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Cryptography\Configuration\SSL\00010002\Functions is $regCheck" -logSev "Info" | out-null
        if ($regCheck -eq 'TLS_DHE_RSA_WITH_AES_256_GCM_SHA384') {
            $returnVal=$true
        }
    } else {
        $returnVal=$true
    }
    return $returnVal
}
function Get-MDICpuScheduler {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Checks for custom CPU scheduler in the registry.
    
    .DESCRIPTION
        Reads registry for custom CPU scheduler setting

    .EXAMPLE
        Get-MDICpuScheduler

    .OUTPUTS
        Verbose output to host.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    [OutputType([bool])]
    Param(
        [Parameter(mandatory=$false)]
        [string]$server="localhost"
    )
    $returnVal=$false
    
    try {
        $c = '((Get-ItemProperty -Path Registry::"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Quota System").EnableCpuQuota)'
        if ($server -ne "localhost") {
            $command  = "Invoke-command -scriptblock {$c} -ComputerName $server"+' 2> $null'
        } else {
            $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
        }
        $regCheck = iex $command
    } catch {
        try {
            [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$server") | out-null
            $reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$server")
            $key = $reg.OpenSubKey("SYSTEM\CurrentControlSet\Control\Session Manager\Quota System")
            if ("EnableCpuQuota" -in $key.getvaluenames()) {
                $regCheck = $key.GetValue("EnableCpuQuota")
                
            } else {
                $returnVal = $true
            }
            $key.close()
        } catch {
            $errorMessage = $_.Exception
            write-error "$server`: Error reading registry"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Error reading registry - CPU quota. $errorMessage" -logSev "Error" | out-null
            throw
        }
        
    } finally {
        if ($reg) {
            $reg.Close()
        }
    }
    if ($regCheck) {
        write-verbose "$server`: Existing value of HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Quota System\EnableCpuQuota is $regCheck"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Existing value of HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Quota System\EnableCpuQuota is $regCheck" -logSev "Info" | out-null
        if ($regCheck -eq 0) {
            $returnVal=$true
        }
    } else {
        $returnVal = $true
    }
    
    return $returnVal
}
function Get-MDINetFrameworkVersion {
        ##########################################################################################################
    <#
    .SYNOPSIS
        Checks for .Net 4.7 prerequisite.
    
    .DESCRIPTION
        Reads registry for Net Version

    .EXAMPLE
        Get-MDINetFrameworkVersion

    .OUTPUTS
        Verbose output to host.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    [OutputType([bool])]
    Param(
        [Parameter(mandatory=$false)]
        [string]$server="localhost"
    )
    $returnVal=$false
    
    try {
        $c = '((Get-ItemProperty -Path Registry::"HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full").Release)'
        if ($server -ne "localhost") {
            $command  = "Invoke-command -scriptblock {$c} -ComputerName $server"+' 2> $null'
        } else {
            $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
        }
        $regCheck = Invoke-Expression $command
    } catch {
        try {
            [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$server") | out-null
            $reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$server")
            $key = $reg.OpenSubKey("SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")
            if ("Release" -in $key.getvaluenames()) {
                $regCheck = ($key.GetValue("Release"))
            }
            $key.close()
        } catch {
            $errorMessage = $_.Exception
            write-error "$server`: Error reading registry"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Error reading registry - CPU quota. $errorMessage" -logSev "Error" | out-null
            throw
        }
        
    } finally {
        if ($reg) {
            $reg.Close()
        }
    }
    write-verbose "$server`: Existing value of HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\Release is $regCheck"
    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Existing value of HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\Release is $regCheck" -logSev "Info" | out-null
    if ($regCheck -ge 460805) {
        $returnVal=$true
    } else {
        $returnVal = $false
    }
    
    return $returnVal
}
function Get-MDINpcap {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Checks for any conflicting installed version of NPCAP.
    
    .DESCRIPTION
        Reads registry for list of installed applications and their version

    .EXAMPLE
        Get-MDINpcap

    .OUTPUTS
        Verbose output to host.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    #[OutputType([bool])]
    Param(
        [Parameter(mandatory=$false)]
        [string]$server="localhost",
        [Parameter(DontShow)]
        [switch]$returnUninstallString=$false,
        [Parameter(mandatory=$false)]
        [switch]$Detailed
    )
    $swInventory = @()
    try {
        $apps = @()
        @('Get-ItemProperty "HKLM:\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"','Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"') | % {
            if ($server -ne "localhost") {
                $command = "Invoke-command -scriptblock {$_} -ComputerName $server"+' 2> $null'
            } else {
                $command = "Invoke-command -scriptblock {$_}"+' 2> $null'
            }
            $apps += invoke-Expression $command
        }
        foreach ($app in $apps) {
            $i = @{}
            $i.Name = $app.Publisher
            $i.Version = $app.DisplayVersion
            $i.QuietUninstallString = $app.QuietUninstallString
            $swInventory += New-Object PSObject -Property $i
            if ($i.Name -eq 'Nmap Project') {
                $npcapInventory = @()
                @('Get-ItemProperty "HKLM:\SOFTWARE\Wow6432Node\npcap"','Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\npcap\Parameters"') | % {
                    if ($server -ne "localhost") {
                        $command = "Invoke-command -scriptblock {$_} -ComputerName $server"+' 2> $null'
                    } else {
                        $command = "Invoke-command -scriptblock {$_}"+' 2> $null'
                    }
                    $keyCheck = invoke-Expression $command
                    $x = @{}
                    $x.AdminOnly = $keyCheck.AdminOnly
                    $x.WinPcapCompatible = $keyCheck.WinPcapCompatible
                    if ($_ -eq 'Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\npcap\Parameters"') {
                        $x.LoopbackSupport = $keyCheck.LoopbackSupport
                        $x.LoopbackAdapter = [bool]($keyCheck.LoopbackAdapter)
                    }
                    $npcapInventory += New-Object PSObject -Property $x
                }
            }
        }
    } catch {
        try {
            [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, "$server") | out-null
            $reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, "$server")
            $keysToSearch = @()
            $key32 = $reg.OpenSubKey("SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
            $keysToSearch += $key32
            $key64 = $reg.OpenSubKey("SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
            $keysToSearch += $key64
            foreach ($key in $keysToSearch) {
                $key.GetSubKeyNames() | ForEach-Object {
                    $subkey = $key.OpenSubKey($_)
                    $i = @{}
                    $i.Name = $subkey.GetValue('Publisher')
                    $i.Version = $subkey.GetValue('DisplayVersion')
                    $i.QuietUninstallString = $subkey.GetValue('QuietUninstallString',"")
                    $swInventory += New-Object PSObject -Property $i
                    $subkey.Close()
                    if ($i.Name -eq 'Nmap Project') {
                        $npcapInventory = @()
                        @("SOFTWARE\Wow6432Node\npcap","SYSTEM\CurrentControlSet\Services\npcap\Parameters") | % {
                            $npcapKey = $reg.OpenSubKey($_)
                            $x = @{}
                            $x.AdminOnly = $npcapKey.GetValue('AdminOnly')
                            $x.WinPcapCompatible = $npcapKey.GetValue('WinPcapCompatible')
                            if ($_ -eq 'SYSTEM\CurrentControlSet\Services\npcap\Parameters') {
                                $x.LoopbackSupport = $npcapKey.GetValue('LoopbackSupport')
                                if ("LoopbackAdapter" -in $npcapKey.GetValueNames()) {
                                    $x.LoopbackAdapter = $true
                                } else {
                                    $x.LoopbackAdapter = $false
                                }
                            }
                            $npcapInventory += New-Object PSObject -Property $x
                            $npcapKey.Close()
                        }
                    }
                }
                $key.close()
            }
            try {
                $key32.close()
                $key64.close()
            } catch {}
        } catch {
            $errorMessage = $_.Exception
            write-error "$server`: Error reading registry"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Error reading registry - NPCAP. $errorMessage" -logSev "Error" | out-null
            throw
        }
    } finally {
        if ($reg) {
            $reg.Close()
        }
    }

    $npcapPresent = $false
    $npcapDesiredVersion = $false
    if ([bool]($swInventory | ? { $_.name -eq "Nmap Project" })){
        $npcapPresent = $true
        $uninstallString = ($swInventory | ? { $_.name -eq "Nmap Project" }).QuietUninstallString
        # what version
        write-verbose "$server`: NPCAP is version $(($swInventory | ? {$_.name -eq 'Nmap Project'}).version)"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: NPCAP is version $(($swInventory | ? {$_.name -eq 'Nmap Project'}).version)" -logSev "Info" | out-null
        if (($swInventory | ? {$_.name -eq 'Nmap Project'}).version -ge "1.00") {
            $npcapDesiredVersion = $true
        }
        $returnObj = [PSCustomObject]@{
            Status            = ($npcapPresent -and $npcapDesiredVersion) -and (($npcapInventory[0].AdminOnly -eq 0) -and ($npcapInventory[1].AdminOnly -eq 0)) -and (($npcapInventory[0].WinPcapCompatible -eq 1) -and ($npcapInventory[1].WinPcapCompatible -eq 1)) -and ($npcapInventory[1].LoopbackSupport -eq 1) -and ($npcapInventory[1].LoopbackAdapter -eq $false)
            AdminOnly         = (($npcapInventory[0].AdminOnly -eq 0) -and ($npcapInventory[1].AdminOnly -eq 0))
            WinPcapCompatible = (($npcapInventory[0].WinPcapCompatible -eq 1) -and ($npcapInventory[1].WinPcapCompatible -eq 1))
            LoopbackSupport   = ($npcapInventory[1].LoopbackSupport -eq 1)
            LoopbackAdapter   = ($npcapInventory[1].LoopbackAdapter -eq $false) 
            Version           = ($swInventory | ? {$_.name -eq 'Nmap Project'}).version
            UninstallString   = $uninstallString
        }
    } else {
        $returnObj = [PSCustomObject]@{
            Status = $true
        }
    }
    $returnObj | Add-Member -MemberType NoteProperty -Name IsNpcapInstalled -Value $npcapPresent
    if ($returnUninstallString) {
        return $returnObj.UninstallString
    } else {
        if ($Detailed) {
            return $returnObj
        } else {
            return $returnObj.Status
        }
    }
}
function Get-MDIPerfcounter {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Checks for required MDI performance counters.
    
    .DESCRIPTION
        Reads registry and performance counters

    .EXAMPLE
        Get-MDIPerfcounter

    .OUTPUTS
        Verbose output to host.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    [OutputType([bool])]
    Param(
        [Parameter(mandatory=$false)]
        [string]$server="localhost"
    )
    $returnVal=$false
    $perfCountRegCheck=$false
    $perfOs = $false
    $perfProc = $false
    $perfDisk = $false
    $perfNet = $false
    $perfCountReg = @("PerfOs", "PerfProc", "PerfDisk", "PerfNet")
    try {
        foreach ($perfReg in $perfCountReg)  {
            $c = '(Get-ItemProperty -Path Registry::"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Services\'+$perfReg+'\Performance")."Disable Performance Counters"'
            if ($server -ne "localhost") {
                $command  = "Invoke-command -scriptblock {$c} -ComputerName $server"+' 2> $null'
            } else {
                $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
            }
            $regCheck = iex $command
            
            if ((!($regCheck)) -or ($regCheck -eq 0)) {
                switch ($perfReg) {
                    "PerfOs" {$perfOs = $true}
                    "PerfProc" {$perfProc = $true}
                    "PerfDisk" {$perfDisk = $true}
                    "PerfNet" {$perfNet = $true}
                }
            }
        }
        
    } catch {
        try {
            [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$server") | out-null
            $reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine', "$server")
            #check perf counters status in registry
            
            $perfCountReg | %  {
                $key = $reg.OpenSubKey("System\CurrentControlSet\Services\$_\Performance")
                #check if the value exists
                if ("Disable Performance Counters" -in $key.getvaluenames()) {
                    #what it set as
                    if ($key.GetValue("Disable Performance Counters") -eq 0) {
                        $perfCountRegCheck=$true
                        switch ($_) {
                            "PerfOs" {$perfOs = $true}
                            "PerfProc" {$perfProc = $true}
                            "PerfDisk" {$perfDisk = $true}
                            "PerfNet" {$perfNet = $true}
                        }
                    }
                } else {
                    switch ($_) {
                        "PerfOs" {$perfOs = $true}
                        "PerfProc" {$perfProc = $true}
                        "PerfDisk" {$perfDisk = $true}
                        "PerfNet" {$perfNet = $true}
                    }
                }
                #close the key handle
                $key.close()
            }
            
        } catch {
            $errorMessage = $_.Exception
            write-error "$server`: Error reading registry"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Error reading registry - Perf. $errorMessage" -logSev "Error" | out-null
            throw
        }
    } finally {
        if ($reg) {
            $reg.Close()
        }
    }
    $perfCountRegCheck = $perfOs -and $perfProc -and $perfDisk -and $perfNet
    try {
        $perfCountExist = @{
            '[System.Diagnostics.PerformanceCounterCategory]::Exists("Processor Information")' = $true;
            '[System.Diagnostics.PerformanceCounterCategory]::CounterExists("% Processor Utility","Processor Information")' = $true;
            '[System.Diagnostics.PerformanceCounterCategory]::InstanceExists("_Total","Processor Information")' = $true;
            '[System.Diagnostics.PerformanceCounterCategory]::Exists("Network Interface")' = $true;
            '[System.Diagnostics.PerformanceCounterCategory]::Exists("Network Adapter")' = $true;
            '[System.Diagnostics.PerformanceCounterCategory]::CounterExists("Packets/sec","Network Adapter")' = $true;
        }
        $perfCountExist.Keys.Clone() | % { 
            if ($server -ne "localhost") {
                $command = "invoke-command -scriptblock {$_} -ComputerName $server"
            } else {
                $command = "invoke-command -scriptblock {$_}"
            }
            
            $perfCountExist["$_"] = iex $command
        }
        #check for failures
        if (($perfCountExist.Values |? {$_ -eq $false}).count -gt 0 ) {
            $returnVal= $false -and $perfCountRegCheck
        }
        else {
            $returnVal= $true -and $perfCountRegCheck
        }

    } catch {
        $errorMessage = $_.Exception
        write-error "$server`: Error running perf counter exist checks"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Error running perf counter exist checks. $errorMessage" -logSev "Error" | out-null
        
    }
    
    return $returnVal
}
function Get-MDIRootCert {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Checks for missing root certificates.
    
    .DESCRIPTION
        Reads cert store

    .EXAMPLE
        Get-MDIRootCert

    .OUTPUTS
        Returns false if incompatible suite, else returns true.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    [OutputType([bool])]
    Param(
        [Parameter(mandatory=$false)]
        [string]$server="localhost"
    )
    [bool]$RootCertsPresent = $false
    [bool]$DigicertG2CertPresent = $false
    [bool]$DigicertCACertPresent = $false
    [bool]$BaltimoreCertPresent = $false
    try {
        $c = 'Get-ChildItem -Path "Cert:\LocalMachine\Root"'
        if ($server -ne "localhost") {
            $command  = "Invoke-command -scriptblock {$c} -ComputerName $server"+' 2> $null'
        } else {
            $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
        }
        $certInventory = Invoke-Expression $command
        if ($certInventory | Where-Object { $_.Thumbprint -eq "df3c24f9bfd666761b268073fe06d1cc8d4f82a4"}) {
            $DigicertG2CertPresent = $true
        }
        if ($certInventory | Where-Object { $_.Thumbprint -eq "a8985d3a65e5e5c4b2d7d66d40c6dd2fb19c5436"}) {
            $DigicertCACertPresent = $true
        }
        if ($certInventory | Where-Object { $_.Thumbprint -eq "d4de20d05e66fc53fe1a50882c78db2852cae474" }) {
            $BaltimoreCertPresent = $true
        }
        if ($BaltimoreCertPresent -and $DigicertG2CertPresent -and $DigicertCACertPresent) {
            $RootCertsPresent = $true
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "$server`: Error accessing root cert store"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Error accessing root cert store. $errorMessage" -logSev "Error" | out-null
    }
    
    return $RootCertsPresent
}
function Get-MDIServerReadiness {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Checks server for MDI readiness.

    .DESCRIPTION
        Checks server for MDI readiness.

    .EXAMPLE
        Get-MDIServerReadiness
        This gets the local server

        Get-MDIServerReadiness -allDomainControllers
        This gets all domain controllers

        Get-MDIServerReadiness -server myDC1
        This gets the server myDC1

        Get-MDIServerReadiness -server myDC1,myDC2,myDC3
        This gets the servers myDC1, myDC2, and myDC3

    .OUTPUTS
        Verbose output to host.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################

    [CmdletBinding(DefaultParameterSetName="server")]
    Param(
        [parameter(mandatory=$false,ParameterSetName="server")]
        [string[]]$Server,
        [Parameter(ParameterSetName="allDCs")]
        [switch]$AllDomainControllers,
        [Parameter(mandatory=$true,ParameterSetName="allDCs")]
        [ValidateSet('Domain', 'Forest')]
        [string]$Location,
        [Parameter(Mandatory=$false)]
        [switch]$IgnoreNPCAP,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    $actionableServers = @()
    $returnServerReadiness = [System.Collections.Generic.List[MdiServerReadiness]]::new()
    if ($PSCmdlet.ParameterSetName -eq 'server') {
        if ($server -match ',') {
            $actionableServers += ($server.split(','))
        } else {
            if ($server.count -gt 0) {
                $server | % { 
                    $actionableServers += $_
                }
            } else {
                $actionableServers += "localhost"
            }
        }
    }
    if ($PSCmdlet.ParameterSetName -eq 'allDCs') {
        try {
            $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
            if ($Location -eq 'Forest') {
                if (Test-MDIUserInEnterpriseAdmins -mydomain $mydomain -domain $domain) {
                    $domainList = Get-DomainsInForestAsAdalList -domain $domain -myDomain $myDomain
                    foreach ($dom in $domainList) {
                        $dom.domainDetail.ReplicaDirectoryServers | ForEach-Object {
                            $actionableServers += $_
                        }
                    }
                } else {
                    write-warning "Must be a member of Enterprise Admins to use Forest location"
                }
            } else {
                $myDomain.domainDetail.ReplicaDirectoryServers | ForEach-Object { 
                    $actionableServers += $_
                }
            }
            $counter = 1
        } catch {
            write-warning "Domain initialization failed, unable to work with All Domain Controllers"
            $AllDomainControllers = $false
        }
        
    }
    if ($AllDomainControllers) {
        $counter = 1
    }
    foreach ($actionableServer in $actionableServers){
        if ($AllDomainControllers) {
            Write-Progress -Activity "Testing server $actionableServer" -Status "Server $counter of $($actionableServers.Count)"
        }
        $params = @{}
        if ($actionableServer -ne "localhost") {$params.add("server",$actionableServer)}
        try {
            write-verbose "Acting on server $actionableServer"
            $serverReadinessReport = [MdiServerReadiness]::new($actionableServer)
            write-verbose "Checking Root Certs"
            $serverReadinessReport.rootCertsPresent = Get-MDIRootCert @params
            write-verbose "Checking Ciphers"
            $serverReadinessReport.cipherSuiteOrder = Get-MDICipherSuite @params
            write-verbose "Checking Performance Counters"
            $serverReadinessReport.perfCountersHealthy = Get-MDIPerfcounter @params
            write-verbose "Checking CPU Scheduler"
            $serverReadinessReport.cpuSchedulerDefault = Get-MDICpuScheduler @params
            write-verbose "Checking NPCAP"
            $npcapCheck = Get-MDINpcap @params -Detailed
            if ($IgnoreNPCAP) {
                $serverReadinessReport.npcapCompatible = $true
            } else {
                $serverReadinessReport.npcapCompatible = $npcapCheck.Status
            }
            $serverReadinessReport.npcapDetails = $npcapCheck
            write-verbose "Checking OS Version"
            try {
                $c = '((Get-WmiObject -Query "select version from win32_operatingsystem").version).split(".")'
                if ($actionableServer -ne "localhost") {
                    $command  = "Invoke-command -scriptblock {$c} -ComputerName $actionableServer"+' 2> $null'
                } else {
                    $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
                }
                $va = Invoke-Expression $command
                $osVersion = [double]($va[0]+"."+$va[1])
                if ($osVersion -gt 6.3) {
                    $serverReadinessReport.osCompliant = $true
                } else {
                    $serverReadinessReport.osCompliant = $false
                }
            } catch {
                write-warning "$actionableServer`: Failed to retrieve OS version."
            }
            write-verbose "Checking .NET Version"
            $serverReadinessReport.netCompliant = (Get-MDIDotNetVersion -ComputerName $actionableServer).IsOk
            $serverReadinessReport.isReady = $serverReadinessReport.osCompliant -and $serverReadinessReport.rootCertsPresent -and $serverReadinessReport.cipherSuiteOrder -and $serverReadinessReport.perfCountersHealthy -and $serverReadinessReport.cpuSchedulerDefault -and $serverReadinessReport.npcapCompatible -and $serverReadinessReport.netCompliant
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($serverReadinessReport.ToString())" -logSev "Info" | out-null
            $returnServerReadiness.Add($serverReadinessReport)
        } catch {
            $errorMessage = $_.Exception
            write-warning "$actionableServer`: Failed to retrieve readiness."
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$actionableServer`: Failed to retrieve readiness. $errorMessage" -logSev "Error" | out-null
        }
        if ($AllDomainControllers) {
            $counter++
        }
    }
    return $returnServerReadiness
}
function Set-MDICipherSuite {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Reorders the TLS cipher suites for MDI compatability.
    
    .DESCRIPTION
        Modifies the registry

    .EXAMPLE
        Set-MDICipherSuite

    .OUTPUTS
        Verbose output to host.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    [OutputType([bool])]
    Param(
        [Parameter(mandatory=$false)]
        [string]$server="localhost"
    )
    $returnVal = $false
    $c = '((Get-ItemProperty -Path Registry::"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Cryptography\Configuration\SSL\00010002").Functions)'
    if ($server -ne "localhost") {
        $command  = "Invoke-command -scriptblock {$c} -ComputerName $server"+' 2> $null'
    } else {
        $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
    }
    $regCheck = iex $command
    # clone to arraylist
    if ($regCheck) {
        [System.Collections.ArrayList]$ral = $regCheck.Split(',')
        # is the correct cipher in the list?
        if ($regCheck.Split(',').IndexOf('TLS_DHE_RSA_WITH_AES_256_GCM_SHA384') -ne -1) {
            # we need to re-order
            # get index of, where is it?
            $i = $regCheck.Split(',').IndexOf('TLS_DHE_RSA_WITH_AES_256_GCM_SHA384')
            # remove element
            $ral.RemoveAt($i)
        }
        # add in at 0
        [System.Collections.ArrayList]$regNew = @()
        $regNew.Add('TLS_DHE_RSA_WITH_AES_256_GCM_SHA384') | out-null
        $regNew.AddRange($ral)
        # write back to registry
        
        try {
            $regNewArray = ([string](($regNew.ToArray()) -join ","))
            $c = 'Set-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Cryptography\Configuration\SSL\00010002" -Name "Functions" -Value "'+$regNewArray+'" -type String'
            if (-not [string]::IsNullOrEmpty($server)) {
                $command  = "Invoke-command -scriptblock {$c} -ComputerName $server"+' 2> $null'
            } else {
                $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
            }
            $regCheck = iex $command
            
        }
        catch {
            $errorMessage = $_.Exception
            write-error "$server`: Failed to write corrected cipher order to registry"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Failed to write corrected cipher order to registry. $errorMessage" -logSev "Error" | out-null
        }
    }
    $returnVal = Get-MDICipherSuite -server $server
    return $returnVal
}
function Set-MDICpuScheduler {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Modifies the registry value for custom CPU scheduler.
    
    .DESCRIPTION
        Resets the registry value for custom CPU scheduler to default

    .EXAMPLE
        Set-MDICpuScheduler

    .OUTPUTS
        Verbose output to host.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    [OutputType([bool])]
    Param(
        [Parameter(mandatory=$false)]
        [string]$server="localhost"
    )
    $returnVal = $false
    #write-output "Attempting to autofix EnableCpuQuota"
    try {
        $c = 'Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Quota System" -Name "EnableCpuQuota" -Value 0 -type DWord'
        if ($server -ne "localhost") {
            $command  = "Invoke-command -scriptblock {$c} -ComputerName $server"+' 2> $null'
        } else {
            $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
        }
        $regCheck = Invoke-Expression $command
        $returnVal = Get-MDICpuScheduler -server $server
    }
    catch {
        $errorMessage = $_.Exception
        write-error "$server`: Failed to write corrected CPU scheduler to registry"
        $returnVal = $false
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Failed to write corrected CPU scheduler to registry. $errorMessage" -logSev "Error" | out-null
    }
    return $returnVal
}

function Set-MDINpcap {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Uninstalls NPCAP if present.

    .DESCRIPTION
        Uninstalls NPCAP if present.

    .EXAMPLE
        Set-MDINpcap

    .OUTPUTS
        Verbose output to host.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    [OutputType([bool])]
    Param(
        [Parameter(mandatory=$false)]
        [string]$server="localhost"
    )
    $returnVal=$false
    try {
        if ($server -ne "localhost") {
            $uninstallString = Get-MDINpcap -server $server -returnUninstallString
            $c = 'cmd /c'+" $uninstallString"
            $command  = "Invoke-command -scriptblock {$c} -ComputerName $server"+' 2> $null'
            
        } else {
            $uninstallString = Get-MDINpcap -returnUninstallString
            $c = 'cmd /c'+" $uninstallString"
            $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
        }
        if ($uninstallString) {
            iex $command
        } else {
            throw "Uninstall string not found"
        }
    } catch {
        $errorMessage = $_.Exception
        write-error "$server`: Failed to uninstall NPCAP"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Failed to uninstall NPCAP. $errorMessage" -logSev "Error" | out-null
    }
    start-sleep 5
    $returnVal = Get-MDINpcap -server $server
    return $returnVal
}
function Set-MDIPerfCounter {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Resets or enables required MDI performance counters.
    
    .DESCRIPTION
        Modifies registry and performance counters

    .EXAMPLE
        Set-MDIPerfCounter

    .OUTPUTS
        Verbose output to host.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    [OutputType([bool])]
    Param(
        [Parameter(mandatory=$false)]
        [string]$server="localhost"
    )
    #$cd = (pwd).path
    $perfCountReg = @("PerfOs", "PerfProc", "PerfDisk", "PerfNet")
    $perfOs = $false
    $perfProc = $false
    $perfDisk = $false
    $perfNet = $false
    $returnVal=$false
    $perfCountRegCheck=$false
    try {
        
        foreach ($perfReg in $perfCountReg)  { 
            $c = '(Get-ItemProperty -Path Registry::"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Services\'+$perfReg+'\Performance")."Disable Performance Counters"'
            if ($server -ne "localhost") {
                $command  = "Invoke-command -scriptblock {$c} -ComputerName $server"+' 2> $null'
            } else {
                $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
            }
            $regCheck = iex $command
            if ($regCheck) {
                try {
                    $c = 'Remove-ItemProperty -Path HKLM:\System\CurrentControlSet\Services\'+$perfReg+'\Performance -Name "Disable Performance Counters"'
                    if ($server -ne "localhost") {
                        $command  = "Invoke-command -scriptblock {$c} -ComputerName $server"+' 2> $null'
                    } else {
                        $command  = "Invoke-command -scriptblock {$c}"+' 2> $null'
                    }
                    $regCheck = iex $command
                }
                catch {
                    write-error "FAILED to remove registry entry HKEY_LOCAL_MACHINE\System\CurrentControlSet\Services\$_\Performance\Disable Performance Counters"
                }
            }
        }
    } catch {
        write-error "FAILED to connect to WINRM on $server"
    } finally {

    }
    $perfCountRegCheck = Get-MDIPerfCounter -server $server
    try {
        if ($server -ne "localhost") {
            $remoteWindir = invoke-command -scriptblock {"$env:windir"} -ComputerName $server
        } else {
            $remoteWindir = invoke-command -scriptblock {"$env:windir"}
        }
        
        @("$remoteWindir\sysWOW64\lodctr.exe /R","$remoteWindir\system32\lodctr.exe /R","$remoteWindir\system32\wbem\WINMGMT.EXE /RESYNCPERF","$remoteWindir\sysWOW64\wbem\WINMGMT.EXE /RESYNCPERF") | % {
            if ($server -ne "localhost") {
                $command = "invoke-command -scriptblock {cmd /c `"$_`"} -ComputerName $server"
            } else {
                $command = "invoke-command -scriptblock {cmd /c `"$_`"}"
            }
            iex $command |out-null
        }
        $returnVal = $true -and $perfCountRegCheck        
    }
    catch {
        $errorMessage = $_.Exception
        write-error "$server`: Failed to resync performance counters"
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Failed to resync performance counters. $errorMessage" -logSev "Error" | out-null
        $returnVal = $false
    }
    return $returnVal
}
function Set-MDIRootCert {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Installs the 2 required root certificates for MDI
    
    .DESCRIPTION
        Requires elevated access to use the local machine MY store

    .EXAMPLE
        Set-MDIRootCert -path c:\temp

        The -path argument requires the directory where your certificates live. They must end in *.crt, as this is the naming convention from a download. This will not try to download from the vendor

        Set-MDIRootCert -download
        Specifying the -download argument by itself will create a connection without a proxy and download both certificates

        Set-MDIRootCert -download -proxyUrl http://my.proxy:3128
        Adding the -proxyUrl argument with the URL of your proxy will use the proxy to download both certificates

    .OUTPUTS
        Verbose output to host.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    [OutputType([bool])]
    Param(
        [Parameter(mandatory=$false)]
        [string]$Server="localhost",
        [parameter(Mandatory=$false)]
        [string[]]$Path,
        [parameter(Mandatory=$false)]
        [switch]$Download,
        [parameter(Mandatory=$false)]
        [string]$Proxy,
        [parameter(Mandatory=$false)]
        [switch]$SkipInstall
    )
    $returnVal = $false
    $certFileNameArray = @('DigiCertGlobalRootG2.crt','BaltimoreCyberTrustRoot.crt','DigiCertGlobalRootCA.crt')
    $certUrlArray = @("https://cacerts.digicert.com/DigiCertGlobalRootG2.crt", "https://cacerts.digicert.com/BaltimoreCyberTrustRoot.crt","https://cacerts.digicert.com/DigiCertGlobalRootCA.crt")
    $certsToInstall = @()
    $command = 'invoke-command -scriptblock {"$env:temp"}'
    if ($Server -ne 'localhost') {
        $command += " -ComputerName $Server"
    }
    $envTemp = Invoke-Expression $command
    if (!($Path)) {
        $Path = "$envTemp"
        if ($null -ne $env:mySourcePath) {
            $Path += '{0}\{1}' -f $env:mySourcePath,"MDISetup"
        }
    }
    # if we were told to download certs don't look for them locally
    if (!($Download)) {
        foreach ($p in $Path) {
            foreach ($certFile in $certFileNameArray) {
                $command = "Invoke-Command -ScriptBlock {"+"(Get-ChildItem -Path $p\$certFile -ErrorAction SilentlyContinue).FullName"+"}"
                $result = Invoke-Expression $command
                if ($null -ne $result) {
                    $certsToInstall += $result
                }
            }
        }
        # check to be sure we have any/all the certs to install. if no certs then we force download, if missing certs then we force download
        if ($certsToInstall.Count -ne 0) {
            if ((Compare-Object -ReferenceObject ($certUrlArray | split-path -leaf | sort) -DifferenceObject ($certsToInstall | split-path -leaf | sort))) {
                $Download = $true
            }
        } else {
            $Download = $true
        }
        
    }
    
    # remediate
    if ($Download) {
        if ($Path.Count -gt 1) {
            $savePath = $Path[0]
        } else {
            $savePath = $Path
        }
        # download certs
        foreach ($certUrl in $certUrlArray) {
            # set file name because doing the split on function call causes extra new lines
            $f = ($certUrl).split('/')[3]
            $wgetCmd = "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12;"
            $wgetCmd += '$ProgressPreference= "SilentlyContinue";'
            $wgetCmd += "Invoke-WebRequest -Uri $certUrl -OutFile $savePath\$f"
            if ($Proxy) {
                $wgetCmd +=" -Proxy "+$Proxy
            }
            $command = "Invoke-Command -ScriptBlock {$wgetCmd}"
            if ($Server -ne 'localhost') {
                $command += " -ComputerName $Server"
            }
            try {
                $null = Invoke-Expression $command
            } catch {
                Write-Warning "Failed to download certificate $certUrl. Please download manually and place in the Netlogon\MSS\MDISetup folder"
                Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Failed to install root certificates. Failed to download certificate $certUrl. $errorMessage" -logSev "Error" | out-null
            }
            
        }
    }
    # install
    if (!($SkipInstall)) {
        try {
            foreach ($p in $Path) {
                foreach ($certFile in $certFileNameArray) {
                    $command = "Invoke-Command -ScriptBlock {"+"(Get-ChildItem -Path $p\$certFile -ErrorAction SilentlyContinue).FullName"+"}"
                    if ($Server -ne 'localhost') {
                        $command += " -ComputerName $Server"
                    }
                    try {
                        $certsToInstall += Invoke-Expression $command
                    } catch {

                    }
                    
                }
            }
            foreach ($cert in $certsToInstall) {
                if (-not [string]::IsNullOrEmpty($cert)) {
                    $c = 'Import-Certificate -FilePath "'+$cert+'"  -CertStoreLocation "Cert:\LocalMachine\Root"'
                    $command = "Invoke-command -scriptblock {$c}"
                    if ($Server -ne 'localhost') {
                        $command += " -ComputerName $Server"
                    }
                    try {
                        $certImport = Invoke-Expression $command
                    } catch {

                    }
                    
                }
            }
        }
        catch {
            $errorMessage = $_.Exception
            write-error "$Server`: Failed to install root certificates"
            Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$server`: Failed to install root certificates. $errorMessage" -logSev "Error" | out-null
        }
    }
    $returnVal = Get-MDIRootCert -server $server
    return $returnVal
}
function Set-MDIServerReadiness {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Fixes server for MDI readiness.

    .DESCRIPTION
        Fixes server for MDI readiness.

    .EXAMPLE
        Set-MDIServerReadiness

    .OUTPUTS
        Verbose output to host.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding(DefaultParameterSetName="server")]
    Param(
        [Parameter(Mandatory = $false,ValueFromPipeline = $true,ParameterSetName='pipeline',DontShow)]
        $pipelineInput,
        [parameter(ParameterSetName="server")]
        [string[]]$Server,
        [Parameter(ParameterSetName="allDCs")]
        [switch]$AllDomainControllers,
        [Parameter(mandatory=$true,ParameterSetName="allDCs")]
        [ValidateSet('Domain', 'Forest')]
        [string]$Location,
        [Parameter(Mandatory=$false)]
        [switch]$IgnoreNPCAP,
        [Parameter(DontShow)]
        [string]$domain,
        [Parameter(DontShow)]
        $myDomain
    )
    begin {
        $actionableServers = [System.Collections.Generic.List[MdiServerReadiness]]::new()
    }
    process {
        if ($PSCmdlet.ParameterSetName -eq 'pipeline') {
            $actionableServers.Add($pipelineInput)
        }
        if ($PSCmdlet.ParameterSetName -eq 'server') {
            if ($server -match ',') {
                ($server.split(',')) | % {
                    $actionableServers.Add([MDIServerReadiness]::new($_))
                }
            } else {
                if ($server.count -gt 0) {
                    $server | % { 
                        $actionableServers.Add([MDIServerReadiness]::new($_))
                    }
                } else {
                    $actionableServers.Add([MDIServerReadiness]::new("localhost"))
                }
            }
        }
        if ($AllDomainControllers) {
            try {
                $myDomain = Initialize-MyDomain -domain $domain -myDomain $myDomain
                if ($Location -eq 'Forest') {
                    if (Test-MDIUserInEnterpriseAdmins -mydomain $mydomain -domain $domain) {
                        $domainList = Get-DomainsInForestAsAdalList -domain $domain -myDomain $myDomain
                        foreach ($dom in $domainList) {
                            $dom.domainDetail.ReplicaDirectoryServers | % {
                                $actionableServers += $_
                            }
                        }
                    } else {
                        write-warning "Must be a member of Enterprise Admins to use Forest location"
                    }
                } else {
                    $myDomain.domainDetail.ReplicaDirectoryServers | % { 
                        $actionableServers += $_
                    }
                }
                $counter = 1
            } catch {
                write-warning "Domain initialization failed, unable to work with All Domain Controllers"
                $AllDomainControllers = $false
            }
            
        }
        foreach ($actionableServer in $actionableServers) {
            $params = @{}
            if ($AllDomainControllers) {
                Write-Progress -Activity "Setting server $actionableServer" -Status "Server $counter of $($actionableServers.Count)"
            }
            if ($PSCmdlet.ParameterSetName -eq 'pipeline') {
                if ($actionableServer -ne "localhost") {$params.add("server",$actionableServer.server)}
                $rs = $actionableServer
            } else {
                if ($actionableServer -ne "localhost") {$params.add("server",$actionableServer.server)}
                $rs = Get-MDIServerReadiness @params -IgnoreNPCAP:$IgnoreNPCAP
            }
            if ($rs.osCompliant) {
                write-verbose "$($rs.server) passed check OS compliance"
                if (!($rs.rootCertsPresent)) {
                    if (Set-MDIRootCert @params) {
                        write-host "$($rs.server)`: Fixed root certs"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Fixed root certs" -logSev "Info" | out-null
                        Write-Log -Message ('Fixed root certs')
                    } else {
                        write-error "$($rs.server)`: Failed to fix root certs"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Failed to root certs" -logSev "Error" | out-null
                        Write-Log -Message ('Failed to fix root certs') -TypeName Error
                    }
                } else {
                    write-verbose "$($rs.server)`: Passed check root certs"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Passed check root certs" -logSev "Info" | out-null
                }
                if (!($rs.cipherSuiteOrder)) {
                    if (Set-MDICipherSuite @params) {
                        write-host "$($rs.server)`: Fixed Cipher Suite"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Fixed Cipher Suite" -logSev "Info" | out-null
                        Write-Log -Message ('Fixed Cipher Suite')
                    } else {
                        write-error "$($rs.server)`: Failed to fix cipher suite"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Failed to fix cipher suite" -logSev "Error" | out-null
                        Write-Log -Message ('Fixed Cipher Suite') -TypeName Error
                    }
                } else {
                    write-verbose "$($rs.server)`: Passed check cipher suites"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Passed check cipher suites" -logSev "Info" | out-null
                }
                if (!($rs.perfCountersHealthy)) {
                    if (Set-MDIPerfCounter @params) {
                        write-host "$($rs.server)`: Fixed perf counters"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Fixed perf counters" -logSev "Info" | out-null
                        Write-Log -Message ('Fixed perf counters')
                    } else {
                        write-error "$($rs.server)`: Failed to fix perf counters"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Failed to fix perf counters" -logSev "Error" | out-null
                        Write-Log -Message ('Fixed perf counters') -TypeName Error
                    }
                } else {
                    write-verbose "$($rs.server)`: Passed check perf counter"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Passed check perf counter" -logSev "Info" | out-null
                }
                if (!($rs.cpuSchedulerDefault)) {
                    if (Set-MDICpuScheduler @params) {
                        write-host "$($rs.server)`: Fixed CPU scheduler"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Fixed CPU scheduler" -logSev "Info" | out-null
                        Write-Log -Message ('Fixed CPU scheduler')
                    } else {
                        write-error "$($rs.server)`: Failed to fix CPU scheduler"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Failed to fix CPU scheduler" -logSev "Error" | out-null
                        Write-Log -Message ('Fixed CPU scheduler') -TypeName Error
                    }
                } else {
                    write-verbose "$($rs.server)`: Passed check CPU scheduler"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Passed check CPU scheduler" -logSev "Info" | out-null
                }
                if (!($rs.npcapCompatible)) {
                    if (Set-MDINpcap @params) {
                        write-host "$($rs.server)`: Uninstalled NPCAP"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Uninstalled NPCAP" -logSev "Info" | out-null
                        Write-Log -Message ('Uninstalled NPCAP')
                    } else {
                        write-error "$($rs.server)`: Failed to uninstall NPCAP"
                        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Failed to uninstall NPCAP" -logSev "Error" | out-null
                        Write-Log -Message ('Failed to uninstall NPCAP') -TypeName Error
                    }
                } else {
                    write-verbose "$($rs.server)`: Passed check NPCAP"
                    Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "$($rs.server)`: Passed check NPCAP" -logSev "Info" | out-null
                }
            } else {
                write-warning "$($rs.server) has an incompatible OS for MDI installation. Skipping..."
                Write-Log -Message ('Incompatible OS for MDI installation') -TypeName Error
                continue
            }
            if ($AllDomainControllers) {
                $counter++
            }
        }
        # do not remove or move this
        if ($PSCmdlet.ParameterSetName -eq 'pipeline') {
            $actionableServers.Remove($rs) | out-null
        }
    }
}
function Test-MDISensorApiConnection {
    ##########################################################################################################
    <#
    .SYNOPSIS
        Runs the check of the API endpoint.
    
    .DESCRIPTION
        Runs the check of the API endpoint.

    .EXAMPLE
        Test-MDISensorApiConnection -path "C:\temp\Azure Atp Setup"
        

        Supplying the -proxyUrl argument with the URL of your proxy will direct traffic through that server.

    .OUTPUTS
        Verbose to the console.

    .NOTES
        THIS CODE-SAMPLE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESSED 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR 
        FITNESS FOR A PARTICULAR PURPOSE.

        This sample is not supported under any Microsoft standard support program or service. 
        The script is provided AS IS without warranty of any kind. Microsoft further disclaims all
        implied warranties including, without limitation, any implied warranties of merchantability
        or of fitness for a particular purpose. The entire risk arising out of the use or performance
        of the sample and documentation remains with you. In no event shall Microsoft, its authors,
        or anyone else involved in the creation, production, or delivery of the script be liable for 
        any damages whatsoever (including, without limitation, damages for loss of business profits, 
        business interruption, loss of business information, or other pecuniary loss) arising out of 
        the use of or inability to use the sample or documentation, even if Microsoft has been advised 
        of the possibility of such damages, rising out of the use of or inability to use the sample script, 
        even if Microsoft has been advised of the possibility of such damages. 
    #>
    ##########################################################################################################
    [CmdletBinding()]
    [OutputType([bool])]
    Param(
        [parameter(Mandatory=$true)]
        [string]$path,
        [parameter(mandatory=$false)]
        [string]$server,
        [Parameter(Mandatory=$false)]
        [string]$proxyUrl,
        [Parameter(Mandatory=$false)]
        [PSCredential]$ProxyCredential
    )
    
    $atpJson = Get-Content $path\SensorInstallationConfiguration.json | convertfrom-json
    $endpoint=$atpJson.WorkspaceApplicationSensorApiEndpoint.Address
    $test=$false
    $sensorApiPath = 'tri/sensor/api/ping'
    $params = @{ URI = $endpoint }
    $params.Add("UseBasicParsing", $true)
    if ($ProxyUrl) { $params.Add('Proxy', $ProxyUrl) }
    if ($ProxyCredential) { $params.Add('ProxyCredential', $ProxyCredential) }
    if ($params.URI -notmatch "$sensorApiPath`$") {
        $params.URI = '{0}{1}/{2}' -f "https://", $params.URI, $sensorApiPath
    }
    Write-Log -Message ('Testing endpoint: {0}' -f $($params.URI))
    try {
        $sb = {
            param($params)
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest @params -ea silentlycontinue
        }
        $invokeParams = @{
            scriptblock = $sb
            ArgumentList = $params
        }
        if (-not [string]::IsNullOrEmpty($server)) {
            $invokeParams.Add("Server",$server)
        }
        $response = Invoke-Command @invokeParams
        $test = (200 -eq $response.StatusCode)
        if (!($test)) {
            throw
        }
    }
    catch {
        Add-LogEntry -logFilePath $env:temp\mircatmdi.log -logMessage "Failed to createconnect to API endpoint. $($_.Exception.Message). URI used was $($params.URI)" -logSev "Error" | out-null
        $test = $false
    }

    return $test
}

}
process {
$parameterMessage = Set-ScriptParameters -Path "$PSScriptRoot\config.psd1"
Invoke-MdiOnboard -AccessKey $AccessKey -IgnoreNPCAP:$IgnoreNPCAP
}


# SIG # Begin signature block
# MIIoPAYJKoZIhvcNAQcCoIIoLTCCKCkCAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCCFC9+SwY5oVLmJ
# QmWb2wmouJg3jfKmqIJ8JGvdIbsNXqCCDYUwggYDMIID66ADAgECAhMzAAAEhJji
# EuB4ozFdAAAAAASEMA0GCSqGSIb3DQEBCwUAMH4xCzAJBgNVBAYTAlVTMRMwEQYD
# VQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNy
# b3NvZnQgQ29ycG9yYXRpb24xKDAmBgNVBAMTH01pY3Jvc29mdCBDb2RlIFNpZ25p
# bmcgUENBIDIwMTEwHhcNMjUwNjE5MTgyMTM1WhcNMjYwNjE3MTgyMTM1WjB0MQsw
# CQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9u
# ZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMR4wHAYDVQQDExVNaWNy
# b3NvZnQgQ29ycG9yYXRpb24wggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIB
# AQDtekqMKDnzfsyc1T1QpHfFtr+rkir8ldzLPKmMXbRDouVXAsvBfd6E82tPj4Yz
# aSluGDQoX3NpMKooKeVFjjNRq37yyT/h1QTLMB8dpmsZ/70UM+U/sYxvt1PWWxLj
# MNIXqzB8PjG6i7H2YFgk4YOhfGSekvnzW13dLAtfjD0wiwREPvCNlilRz7XoFde5
# KO01eFiWeteh48qUOqUaAkIznC4XB3sFd1LWUmupXHK05QfJSmnei9qZJBYTt8Zh
# ArGDh7nQn+Y1jOA3oBiCUJ4n1CMaWdDhrgdMuu026oWAbfC3prqkUn8LWp28H+2S
# LetNG5KQZZwvy3Zcn7+PQGl5AgMBAAGjggGCMIIBfjAfBgNVHSUEGDAWBgorBgEE
# AYI3TAgBBggrBgEFBQcDAzAdBgNVHQ4EFgQUBN/0b6Fh6nMdE4FAxYG9kWCpbYUw
# VAYDVR0RBE0wS6RJMEcxLTArBgNVBAsTJE1pY3Jvc29mdCBJcmVsYW5kIE9wZXJh
# dGlvbnMgTGltaXRlZDEWMBQGA1UEBRMNMjMwMDEyKzUwNTM2MjAfBgNVHSMEGDAW
# gBRIbmTlUAXTgqoXNzcitW2oynUClTBUBgNVHR8ETTBLMEmgR6BFhkNodHRwOi8v
# d3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL2NybC9NaWNDb2RTaWdQQ0EyMDExXzIw
# MTEtMDctMDguY3JsMGEGCCsGAQUFBwEBBFUwUzBRBggrBgEFBQcwAoZFaHR0cDov
# L3d3dy5taWNyb3NvZnQuY29tL3BraW9wcy9jZXJ0cy9NaWNDb2RTaWdQQ0EyMDEx
# XzIwMTEtMDctMDguY3J0MAwGA1UdEwEB/wQCMAAwDQYJKoZIhvcNAQELBQADggIB
# AGLQps1XU4RTcoDIDLP6QG3NnRE3p/WSMp61Cs8Z+JUv3xJWGtBzYmCINmHVFv6i
# 8pYF/e79FNK6P1oKjduxqHSicBdg8Mj0k8kDFA/0eU26bPBRQUIaiWrhsDOrXWdL
# m7Zmu516oQoUWcINs4jBfjDEVV4bmgQYfe+4/MUJwQJ9h6mfE+kcCP4HlP4ChIQB
# UHoSymakcTBvZw+Qst7sbdt5KnQKkSEN01CzPG1awClCI6zLKf/vKIwnqHw/+Wvc
# Ar7gwKlWNmLwTNi807r9rWsXQep1Q8YMkIuGmZ0a1qCd3GuOkSRznz2/0ojeZVYh
# ZyohCQi1Bs+xfRkv/fy0HfV3mNyO22dFUvHzBZgqE5FbGjmUnrSr1x8lCrK+s4A+
# bOGp2IejOphWoZEPGOco/HEznZ5Lk6w6W+E2Jy3PHoFE0Y8TtkSE4/80Y2lBJhLj
# 27d8ueJ8IdQhSpL/WzTjjnuYH7Dx5o9pWdIGSaFNYuSqOYxrVW7N4AEQVRDZeqDc
# fqPG3O6r5SNsxXbd71DCIQURtUKss53ON+vrlV0rjiKBIdwvMNLQ9zK0jy77owDy
# XXoYkQxakN2uFIBO1UNAvCYXjs4rw3SRmBX9qiZ5ENxcn/pLMkiyb68QdwHUXz+1
# fI6ea3/jjpNPz6Dlc/RMcXIWeMMkhup/XEbwu73U+uz/MIIHejCCBWKgAwIBAgIK
# YQ6Q0gAAAAAAAzANBgkqhkiG9w0BAQsFADCBiDELMAkGA1UEBhMCVVMxEzARBgNV
# BAgTCldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jv
# c29mdCBDb3Jwb3JhdGlvbjEyMDAGA1UEAxMpTWljcm9zb2Z0IFJvb3QgQ2VydGlm
# aWNhdGUgQXV0aG9yaXR5IDIwMTEwHhcNMTEwNzA4MjA1OTA5WhcNMjYwNzA4MjEw
# OTA5WjB+MQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UE
# BxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSgwJgYD
# VQQDEx9NaWNyb3NvZnQgQ29kZSBTaWduaW5nIFBDQSAyMDExMIICIjANBgkqhkiG
# 9w0BAQEFAAOCAg8AMIICCgKCAgEAq/D6chAcLq3YbqqCEE00uvK2WCGfQhsqa+la
# UKq4BjgaBEm6f8MMHt03a8YS2AvwOMKZBrDIOdUBFDFC04kNeWSHfpRgJGyvnkmc
# 6Whe0t+bU7IKLMOv2akrrnoJr9eWWcpgGgXpZnboMlImEi/nqwhQz7NEt13YxC4D
# dato88tt8zpcoRb0RrrgOGSsbmQ1eKagYw8t00CT+OPeBw3VXHmlSSnnDb6gE3e+
# lD3v++MrWhAfTVYoonpy4BI6t0le2O3tQ5GD2Xuye4Yb2T6xjF3oiU+EGvKhL1nk
# kDstrjNYxbc+/jLTswM9sbKvkjh+0p2ALPVOVpEhNSXDOW5kf1O6nA+tGSOEy/S6
# A4aN91/w0FK/jJSHvMAhdCVfGCi2zCcoOCWYOUo2z3yxkq4cI6epZuxhH2rhKEmd
# X4jiJV3TIUs+UsS1Vz8kA/DRelsv1SPjcF0PUUZ3s/gA4bysAoJf28AVs70b1FVL
# 5zmhD+kjSbwYuER8ReTBw3J64HLnJN+/RpnF78IcV9uDjexNSTCnq47f7Fufr/zd
# sGbiwZeBe+3W7UvnSSmnEyimp31ngOaKYnhfsi+E11ecXL93KCjx7W3DKI8sj0A3
# T8HhhUSJxAlMxdSlQy90lfdu+HggWCwTXWCVmj5PM4TasIgX3p5O9JawvEagbJjS
# 4NaIjAsCAwEAAaOCAe0wggHpMBAGCSsGAQQBgjcVAQQDAgEAMB0GA1UdDgQWBBRI
# bmTlUAXTgqoXNzcitW2oynUClTAZBgkrBgEEAYI3FAIEDB4KAFMAdQBiAEMAQTAL
# BgNVHQ8EBAMCAYYwDwYDVR0TAQH/BAUwAwEB/zAfBgNVHSMEGDAWgBRyLToCMZBD
# uRQFTuHqp8cx0SOJNDBaBgNVHR8EUzBRME+gTaBLhklodHRwOi8vY3JsLm1pY3Jv
# c29mdC5jb20vcGtpL2NybC9wcm9kdWN0cy9NaWNSb29DZXJBdXQyMDExXzIwMTFf
# MDNfMjIuY3JsMF4GCCsGAQUFBwEBBFIwUDBOBggrBgEFBQcwAoZCaHR0cDovL3d3
# dy5taWNyb3NvZnQuY29tL3BraS9jZXJ0cy9NaWNSb29DZXJBdXQyMDExXzIwMTFf
# MDNfMjIuY3J0MIGfBgNVHSAEgZcwgZQwgZEGCSsGAQQBgjcuAzCBgzA/BggrBgEF
# BQcCARYzaHR0cDovL3d3dy5taWNyb3NvZnQuY29tL3BraW9wcy9kb2NzL3ByaW1h
# cnljcHMuaHRtMEAGCCsGAQUFBwICMDQeMiAdAEwAZQBnAGEAbABfAHAAbwBsAGkA
# YwB5AF8AcwB0AGEAdABlAG0AZQBuAHQALiAdMA0GCSqGSIb3DQEBCwUAA4ICAQBn
# 8oalmOBUeRou09h0ZyKbC5YR4WOSmUKWfdJ5DJDBZV8uLD74w3LRbYP+vj/oCso7
# v0epo/Np22O/IjWll11lhJB9i0ZQVdgMknzSGksc8zxCi1LQsP1r4z4HLimb5j0b
# pdS1HXeUOeLpZMlEPXh6I/MTfaaQdION9MsmAkYqwooQu6SpBQyb7Wj6aC6VoCo/
# KmtYSWMfCWluWpiW5IP0wI/zRive/DvQvTXvbiWu5a8n7dDd8w6vmSiXmE0OPQvy
# CInWH8MyGOLwxS3OW560STkKxgrCxq2u5bLZ2xWIUUVYODJxJxp/sfQn+N4sOiBp
# mLJZiWhub6e3dMNABQamASooPoI/E01mC8CzTfXhj38cbxV9Rad25UAqZaPDXVJi
# hsMdYzaXht/a8/jyFqGaJ+HNpZfQ7l1jQeNbB5yHPgZ3BtEGsXUfFL5hYbXw3MYb
# BL7fQccOKO7eZS/sl/ahXJbYANahRr1Z85elCUtIEJmAH9AAKcWxm6U/RXceNcbS
# oqKfenoi+kiVH6v7RyOA9Z74v2u3S5fi63V4GuzqN5l5GEv/1rMjaHXmr/r8i+sL
# gOppO6/8MO0ETI7f33VtY5E90Z1WTk+/gFcioXgRMiF670EKsT/7qMykXcGhiJtX
# cVZOSEXAQsmbdlsKgEhr/Xmfwb1tbWrJUnMTDXpQzTGCGg0wghoJAgEBMIGVMH4x
# CzAJBgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRt
# b25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xKDAmBgNVBAMTH01p
# Y3Jvc29mdCBDb2RlIFNpZ25pbmcgUENBIDIwMTECEzMAAASEmOIS4HijMV0AAAAA
# BIQwDQYJYIZIAWUDBAIBBQCgga4wGQYJKoZIhvcNAQkDMQwGCisGAQQBgjcCAQQw
# HAYKKwYBBAGCNwIBCzEOMAwGCisGAQQBgjcCARUwLwYJKoZIhvcNAQkEMSIEIBgn
# nOCIi8IbWDtGgiZgf1/ZHd1i2Sl+7E465uUTRoA7MEIGCisGAQQBgjcCAQwxNDAy
# oBSAEgBNAGkAYwByAG8AcwBvAGYAdKEagBhodHRwOi8vd3d3Lm1pY3Jvc29mdC5j
# b20wDQYJKoZIhvcNAQEBBQAEggEAihTSlstwNUyiDq4dBS50RfMVtRYuoFigEmFj
# UnCBBTOGD/kHxk/3BUpA0ZQLkUqTqgwjQNR2bTSNtL+0evTSFfenRd9fTsPhcSDh
# R4n+HjRBejOj6kijZx8MQGESSTNKXm+/UCgWNC55v9ZaB16306MEeYLEVmxRseMN
# krR5fLd+9omh5NjqAf6J4YYRN4X5YPvng/kxIgUA5Xa+wZE8NeWDU+SrTOYzzPdN
# kxZeaS1OHk80AdZ/qEMKi/ySzfj5gmy3PuZFZA91ePsXQ1sh6UKgsbraU3mM/jrT
# ctt2fuQt57MVGtZsd/4Bry7qsJ3/xhhKfCJfSm6ecuVxuD8hUqGCF5cwgheTBgor
# BgEEAYI3AwMBMYIXgzCCF38GCSqGSIb3DQEHAqCCF3AwghdsAgEDMQ8wDQYJYIZI
# AWUDBAIBBQAwggFSBgsqhkiG9w0BCRABBKCCAUEEggE9MIIBOQIBAQYKKwYBBAGE
# WQoDATAxMA0GCWCGSAFlAwQCAQUABCD4OFdmncZtEubWifnV9Yl2m1qRNEY2wlFI
# JEh3vkOkUgIGaKOduBmCGBMyMDI1MDgyNzE4MTA1My44NzlaMASAAgH0oIHRpIHO
# MIHLMQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMH
# UmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSUwIwYDVQQL
# ExxNaWNyb3NvZnQgQW1lcmljYSBPcGVyYXRpb25zMScwJQYDVQQLEx5uU2hpZWxk
# IFRTUyBFU046REMwMC0wNUUwLUQ5NDcxJTAjBgNVBAMTHE1pY3Jvc29mdCBUaW1l
# LVN0YW1wIFNlcnZpY2WgghHtMIIHIDCCBQigAwIBAgITMwAAAgO7HlwAOGx0ygAB
# AAACAzANBgkqhkiG9w0BAQsFADB8MQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2Fz
# aGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENv
# cnBvcmF0aW9uMSYwJAYDVQQDEx1NaWNyb3NvZnQgVGltZS1TdGFtcCBQQ0EgMjAx
# MDAeFw0yNTAxMzAxOTQyNDZaFw0yNjA0MjIxOTQyNDZaMIHLMQswCQYDVQQGEwJV
# UzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UE
# ChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSUwIwYDVQQLExxNaWNyb3NvZnQgQW1l
# cmljYSBPcGVyYXRpb25zMScwJQYDVQQLEx5uU2hpZWxkIFRTUyBFU046REMwMC0w
# NUUwLUQ5NDcxJTAjBgNVBAMTHE1pY3Jvc29mdCBUaW1lLVN0YW1wIFNlcnZpY2Uw
# ggIiMA0GCSqGSIb3DQEBAQUAA4ICDwAwggIKAoICAQChl0MH5wAnOx8Uh8RtidF0
# J0yaFDHJYHTpPvRR16X1KxGDYfT8PrcGjCLCiaOu3K1DmUIU4Rc5olndjappNuOg
# zwUoj43VbbJx5PFTY/a1Z80tpqVP0OoKJlUkfDPSBLFgXWj6VgayRCINtLsUasy0
# w5gysD7ILPZuiQjace5KxASjKf2MVX1qfEzYBbTGNEijSQCKwwyc0eavr4Fo3X/+
# sCuuAtkTWissU64k8rK60jsGRApiESdfuHr0yWAmc7jTOPNeGAx6KCL2ktpnGegL
# Dd1IlE6Bu6BSwAIFHr7zOwIlFqyQuCe0SQALCbJhsT9y9iy61RJAXsU0u0TC5YYm
# TSbEI7g10dYx8Uj+vh9InLoKYC5DpKb311bYVd0bytbzlfTRslRTJgotnfCAIGML
# qEqk9/2VRGu9klJi1j9nVfqyYHYrMPOBXcrQYW0jmKNjOL47CaEArNzhDBia1wXd
# JANKqMvJ8pQe2m8/cibyDM+1BVZquNAov9N4tJF4ACtjX0jjXNDUMtSZoVFQH+Fk
# WdfPWx1uBIkc97R+xRLuPjUypHZ5A3AALSke4TaRBvbvTBYyW2HenOT7nYLKTO4j
# w5Qq6cw3Z9zTKSPQ6D5lyiYpes5RR2MdMvJS4fCcPJFeaVOvuWFSQ/EGtVBShhmL
# B+5ewzFzdpf1UuJmuOQTTwIDAQABo4IBSTCCAUUwHQYDVR0OBBYEFLIpWUB+EeeQ
# 29sWe0VdzxWQGJJ9MB8GA1UdIwQYMBaAFJ+nFV0AXmJdg/Tl0mWnG1M1GelyMF8G
# A1UdHwRYMFYwVKBSoFCGTmh0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMv
# Y3JsL01pY3Jvc29mdCUyMFRpbWUtU3RhbXAlMjBQQ0ElMjAyMDEwKDEpLmNybDBs
# BggrBgEFBQcBAQRgMF4wXAYIKwYBBQUHMAKGUGh0dHA6Ly93d3cubWljcm9zb2Z0
# LmNvbS9wa2lvcHMvY2VydHMvTWljcm9zb2Z0JTIwVGltZS1TdGFtcCUyMFBDQSUy
# MDIwMTAoMSkuY3J0MAwGA1UdEwEB/wQCMAAwFgYDVR0lAQH/BAwwCgYIKwYBBQUH
# AwgwDgYDVR0PAQH/BAQDAgeAMA0GCSqGSIb3DQEBCwUAA4ICAQCQEMbesD6TC08R
# 0oYCdSC452AQrGf/O89GQ54CtgEsbxzwGDVUcmjXFcnaJSTNedBKVXkBgawRonP1
# LgxH4bzzVj2eWNmzGIwO1FlhldAPOHAzLBEHRoSZ4pddFtaQxoabU/N1vWyICiN6
# 0It85gnF5JD4MMXyd6pS8eADIi6TtjfgKPoumWa0BFQ/aEzjUrfPN1r7crK+qkmL
# ztw/ENS7zemfyx4kGRgwY1WBfFqm/nFlJDPQBicqeU3dOp9hj7WqD0Rc+/4VZ6wQ
# jesIyCkv5uhUNy2LhNDi2leYtAiIFpmjfNk4GngLvC2Tj9IrOMv20Srym5J/Fh7y
# WAiPeGs3yA3QapjZTtfr7NfzpBIJQ4xT/ic4WGWqhGlRlVBI5u6Ojw3ZxSZCLg3v
# RC4KYypkh8FdIWoKirjidEGlXsNOo+UP/YG5KhebiudTBxGecfJCuuUspIdRhStH
# AQsjv/dAqWBLlhorq2OCaP+wFhE3WPgnnx5pflvlujocPgsN24++ddHrl3O1FFab
# W8m0UkDHSKCh8QTwTkYOwu99iExBVWlbYZRz2qOIBjL/ozEhtCB0auKhfTLLeuNG
# BUaBz+oZZ+X9UAECoMhkETjb6YfNaI1T7vVAaiuhBoV/JCOQT+RYZrgykyPpzpmw
# MNFBD1vdW/29q9nkTWoEhcEOO0L9NzCCB3EwggVZoAMCAQICEzMAAAAVxedrngKb
# SZkAAAAAABUwDQYJKoZIhvcNAQELBQAwgYgxCzAJBgNVBAYTAlVTMRMwEQYDVQQI
# EwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNyb3Nv
# ZnQgQ29ycG9yYXRpb24xMjAwBgNVBAMTKU1pY3Jvc29mdCBSb290IENlcnRpZmlj
# YXRlIEF1dGhvcml0eSAyMDEwMB4XDTIxMDkzMDE4MjIyNVoXDTMwMDkzMDE4MzIy
# NVowfDELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24xEDAOBgNVBAcT
# B1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEmMCQGA1UE
# AxMdTWljcm9zb2Z0IFRpbWUtU3RhbXAgUENBIDIwMTAwggIiMA0GCSqGSIb3DQEB
# AQUAA4ICDwAwggIKAoICAQDk4aZM57RyIQt5osvXJHm9DtWC0/3unAcH0qlsTnXI
# yjVX9gF/bErg4r25PhdgM/9cT8dm95VTcVrifkpa/rg2Z4VGIwy1jRPPdzLAEBjo
# YH1qUoNEt6aORmsHFPPFdvWGUNzBRMhxXFExN6AKOG6N7dcP2CZTfDlhAnrEqv1y
# aa8dq6z2Nr41JmTamDu6GnszrYBbfowQHJ1S/rboYiXcag/PXfT+jlPP1uyFVk3v
# 3byNpOORj7I5LFGc6XBpDco2LXCOMcg1KL3jtIckw+DJj361VI/c+gVVmG1oO5pG
# ve2krnopN6zL64NF50ZuyjLVwIYwXE8s4mKyzbnijYjklqwBSru+cakXW2dg3viS
# kR4dPf0gz3N9QZpGdc3EXzTdEonW/aUgfX782Z5F37ZyL9t9X4C626p+Nuw2TPYr
# bqgSUei/BQOj0XOmTTd0lBw0gg/wEPK3Rxjtp+iZfD9M269ewvPV2HM9Q07BMzlM
# jgK8QmguEOqEUUbi0b1qGFphAXPKZ6Je1yh2AuIzGHLXpyDwwvoSCtdjbwzJNmSL
# W6CmgyFdXzB0kZSU2LlQ+QuJYfM2BjUYhEfb3BvR/bLUHMVr9lxSUV0S2yW6r1AF
# emzFER1y7435UsSFF5PAPBXbGjfHCBUYP3irRbb1Hode2o+eFnJpxq57t7c+auIu
# rQIDAQABo4IB3TCCAdkwEgYJKwYBBAGCNxUBBAUCAwEAATAjBgkrBgEEAYI3FQIE
# FgQUKqdS/mTEmr6CkTxGNSnPEP8vBO4wHQYDVR0OBBYEFJ+nFV0AXmJdg/Tl0mWn
# G1M1GelyMFwGA1UdIARVMFMwUQYMKwYBBAGCN0yDfQEBMEEwPwYIKwYBBQUHAgEW
# M2h0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvRG9jcy9SZXBvc2l0b3J5
# Lmh0bTATBgNVHSUEDDAKBggrBgEFBQcDCDAZBgkrBgEEAYI3FAIEDB4KAFMAdQBi
# AEMAQTALBgNVHQ8EBAMCAYYwDwYDVR0TAQH/BAUwAwEB/zAfBgNVHSMEGDAWgBTV
# 9lbLj+iiXGJo0T2UkFvXzpoYxDBWBgNVHR8ETzBNMEugSaBHhkVodHRwOi8vY3Js
# Lm1pY3Jvc29mdC5jb20vcGtpL2NybC9wcm9kdWN0cy9NaWNSb29DZXJBdXRfMjAx
# MC0wNi0yMy5jcmwwWgYIKwYBBQUHAQEETjBMMEoGCCsGAQUFBzAChj5odHRwOi8v
# d3d3Lm1pY3Jvc29mdC5jb20vcGtpL2NlcnRzL01pY1Jvb0NlckF1dF8yMDEwLTA2
# LTIzLmNydDANBgkqhkiG9w0BAQsFAAOCAgEAnVV9/Cqt4SwfZwExJFvhnnJL/Klv
# 6lwUtj5OR2R4sQaTlz0xM7U518JxNj/aZGx80HU5bbsPMeTCj/ts0aGUGCLu6WZn
# OlNN3Zi6th542DYunKmCVgADsAW+iehp4LoJ7nvfam++Kctu2D9IdQHZGN5tggz1
# bSNU5HhTdSRXud2f8449xvNo32X2pFaq95W2KFUn0CS9QKC/GbYSEhFdPSfgQJY4
# rPf5KYnDvBewVIVCs/wMnosZiefwC2qBwoEZQhlSdYo2wh3DYXMuLGt7bj8sCXgU
# 6ZGyqVvfSaN0DLzskYDSPeZKPmY7T7uG+jIa2Zb0j/aRAfbOxnT99kxybxCrdTDF
# NLB62FD+CljdQDzHVG2dY3RILLFORy3BFARxv2T5JL5zbcqOCb2zAVdJVGTZc9d/
# HltEAY5aGZFrDZ+kKNxnGSgkujhLmm77IVRrakURR6nxt67I6IleT53S0Ex2tVdU
# CbFpAUR+fKFhbHP+CrvsQWY9af3LwUFJfn6Tvsv4O+S3Fb+0zj6lMVGEvL8CwYKi
# excdFYmNcP7ntdAoGokLjzbaukz5m/8K6TT4JDVnK+ANuOaMmdbhIurwJ0I9JZTm
# dHRbatGePu1+oDEzfbzL6Xu/OHBE0ZDxyKs6ijoIYn/ZcGNTTY3ugm2lBRDBcQZq
# ELQdVTNYs6FwZvKhggNQMIICOAIBATCB+aGB0aSBzjCByzELMAkGA1UEBhMCVVMx
# EzARBgNVBAgTCldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoT
# FU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjElMCMGA1UECxMcTWljcm9zb2Z0IEFtZXJp
# Y2EgT3BlcmF0aW9uczEnMCUGA1UECxMeblNoaWVsZCBUU1MgRVNOOkRDMDAtMDVF
# MC1EOTQ3MSUwIwYDVQQDExxNaWNyb3NvZnQgVGltZS1TdGFtcCBTZXJ2aWNloiMK
# AQEwBwYFKw4DAhoDFQDNrxRX/iz6ss1lBCXG8P1LFxD0e6CBgzCBgKR+MHwxCzAJ
# BgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25k
# MR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xJjAkBgNVBAMTHU1pY3Jv
# c29mdCBUaW1lLVN0YW1wIFBDQSAyMDEwMA0GCSqGSIb3DQEBCwUAAgUA7FlQEzAi
# GA8yMDI1MDgyNzA5MzYxOVoYDzIwMjUwODI4MDkzNjE5WjB3MD0GCisGAQQBhFkK
# BAExLzAtMAoCBQDsWVATAgEAMAoCAQACAhFBAgH/MAcCAQACAhMZMAoCBQDsWqGT
# AgEAMDYGCisGAQQBhFkKBAIxKDAmMAwGCisGAQQBhFkKAwKgCjAIAgEAAgMHoSCh
# CjAIAgEAAgMBhqAwDQYJKoZIhvcNAQELBQADggEBAHbRbw1g7dYLhsCAygArgM9J
# ENUhRjFPt/4b970ZSJPM4gAc5LP3zqkvzDulR64hQL6s4f/3gFTpw844epQz+gkr
# fWl89JSQnButE/MNrkoVp8xQGZ4N1fLplQLP5o0NeaG3SZy3UiaAKBSSZWnElM2o
# Bl9aF3CZln/YnUrPSpvJAfhOqm5oIidoBSSwT33upGuqjhluGB1jlUAlRTTcb8Xh
# Kh3xT8yDFX8ghIosu3bHvt43Vo+m5jo4PukIzu9U+7GO1YgkjFd73IyiVXvjtLP/
# LKdg8e3DrFkEZEX2DyT32k9hPW6NpONmohQ/8yvQyrNCbMycEzJ5l5ifNed7Uz8x
# ggQNMIIECQIBATCBkzB8MQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3Rv
# bjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0
# aW9uMSYwJAYDVQQDEx1NaWNyb3NvZnQgVGltZS1TdGFtcCBQQ0EgMjAxMAITMwAA
# AgO7HlwAOGx0ygABAAACAzANBglghkgBZQMEAgEFAKCCAUowGgYJKoZIhvcNAQkD
# MQ0GCyqGSIb3DQEJEAEEMC8GCSqGSIb3DQEJBDEiBCBjvFm/Pl78+AtkkLOqYo/2
# CMi+F4bQ4YWGRIiRmeP/0DCB+gYLKoZIhvcNAQkQAi8xgeowgecwgeQwgb0EIEsD
# 3RtxlvaTxFOZZnpQw0DksPmVduo5SyK9h9w++hMtMIGYMIGApH4wfDELMAkGA1UE
# BhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAc
# BgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEmMCQGA1UEAxMdTWljcm9zb2Z0
# IFRpbWUtU3RhbXAgUENBIDIwMTACEzMAAAIDux5cADhsdMoAAQAAAgMwIgQgGCY+
# ns9qmROR76warMhvwfUIJWSI47VK8KkI3UKN2hswDQYJKoZIhvcNAQELBQAEggIA
# aBp78zTs2ZTVRKL67ufiOSCp2x4adgu9s//SVuPEFv55ryY1PTZv3lnyk4D1mki6
# 2Jv1FMvNfBgJ1G1cORVxMxekbvgAlPPSbuMWRGHwaz+BOb/IZBAwBTzCm7CKnBI/
# uPvh5iZhSfuTlkBSWcoty2L0TRD+2gjF/jVBOQ//Ceh5+TtB04qlreHPle4FHmf9
# mgyr/vXC4gCSbxL8J/BnDbcvGd4foSudxYk/lpl9HCefHLukMOh0s5f3gW38XLRG
# qTUuGL2NI7pL8lN+Ulv7jvseFtF3OI9pP+1M7iL3lzdnyMAJobxnLmstli6EImVk
# yXQwf1UiuZPIwOsPaLR8DTpdFd2CxF1F28nEN5Vsx/K8ba6vMPq1LDN6XiYiBk1Y
# VMkixnlUx7jx9yJSEOyr3EINE2sLrInwmgaRHc/oo4dcw8V7YfgPKyimRrCGMuR5
# Q00OZ2SUXoUB51otyFX6zoqXGPQloWoNC1rbLhBtYzsA1lKy4Z93/tnuNzjbrJLb
# 1XGuJMt+5Wd80OKN03enlzN/RBzf4jJPl1EUwWhDW8LRY3wpBZT3aLaGoxFm4fXY
# g9QEpqjt1MaUF85r8yOHoHLEcXSRnSZ5yDGy9StqpVesTEcUSdUkTyIvNkDRo7m8
# G8myOSgDSQ8QUjNS+wzFiUdoa4k7XRaAEh/uMSY5Hr0=
# SIG # End signature block
